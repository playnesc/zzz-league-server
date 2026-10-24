using ZenlessLeague.Api.Features.Discord;
using ZenlessLeague.Api.Features.History;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Teams;
using ZenlessLeague.Api.Features.Tournaments.Bracket;
using ZenlessLeague.Api.Features.Tournaments.Realtime;
using ZenlessLeague.Api.Infrastructure.Errors;
using ZenlessLeague.Api.Infrastructure.Persistence;

namespace ZenlessLeague.Api.Features.Tournaments;

public interface ITournamentLifecycleService
{
	// Locks the roster: every approved registration becomes a bracket participant in a random
	// layout, and the matches are generated (see BracketService).
	Task<TournamentDto?> CreateBracketAsync(Guid id, CancellationToken ct = default);

	// Start/Finish/Delete retry their Discord calls as one unit on transient failure — each is
	// individually idempotent, so retrying is always safe, but this is not a transaction: on a
	// TournamentLifecycleException, some of those calls (and their DB writes) may have already
	// gone through. Only the tournament's own State field is guaranteed to not advance until the
	// whole sequence succeeds.
	Task<TournamentDto?> StartAsync(Guid id, CancellationToken ct = default);
	Task<TournamentDto?> FinishAsync(Guid id, CancellationToken ct = default);
	Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

	Task<SplitTournamentResult?> SplitAsync(Guid id, SplitTournamentRequest request, CancellationToken ct = default);

	Task IncrementTournamentCountAsync(Guid id, bool seasonal, CancellationToken ct = default);
}

public class TournamentLifecycleService(
	ITournamentRepository tournamentRepository,
	ITournamentRegistrationRepository registrationRepository,
	ITeamRepository teamRepository,
	IPlayerRepository playerRepository,
	ITournamentParticipantRepository participantRepository,
	IBracketService bracketService,
	IHistoryService historyService,
	ITournamentDiscordResourcesService discordResourcesService,
	ITournamentNotifier notifier,
	IUnitOfWork unitOfWork) : ITournamentLifecycleService
{
	public async Task<TournamentDto?> CreateBracketAsync(Guid id, CancellationToken ct = default)
	{
		var tournament = await tournamentRepository.GetByIdAsync(id, ct);
		if (tournament is null)
		{
			return null;
		}

		if (TournamentStateMachine.IsLocked(tournament.State))
		{
			throw new TournamentLifecycleException("The bracket has already been created.");
		}

		var isTeamTournament = tournament.RegistrationType == TournamentRegistrationType.Team;
		var approved = (await registrationRepository.ListByTournamentAsync(id, ct)).Where(r => r.Approved).ToList();
		if (approved.Count < 2)
		{
			throw new TournamentLifecycleException(isTeamTournament ? "Need at least 2 approved teams." : "Need at least 2 approved players.");
		}

		// Every approved registration must actually be shaped for this tournament's
		// RegistrationType (TeamId set for a team tournament, PlayerId set for a solo one) — this
		// should always hold given RegisterSoloAsync/RegisterTeamAsync/AdminAddAsync/AdminAddTeamAsync
		// each check it on the way in, but a clean error here beats a null-dereference crash if
		// that ever gets violated (e.g. by stale data from before that validation existed).
		var mismatched = approved.Where(r => isTeamTournament ? r.TeamId is null : r.PlayerId is null).ToList();
		if (mismatched.Count > 0)
		{
			throw new TournamentLifecycleException(
				$"{mismatched.Count} registration(s) don't match this tournament's registration type ({tournament.RegistrationType}) — fix or remove them before creating the bracket.");
		}

		var entrantIds = approved.Select(r => isTeamTournament ? r.TeamId!.Value : r.PlayerId!.Value).ToList();

		await unitOfWork.InTransactionAsync(async () =>
		{
			await bracketService.CreateAsync(tournament, entrantIds, ct);
			tournament.State = TournamentState.BracketCreated;
			await tournamentRepository.SaveChangesAsync(ct);
		}, ct);

		await notifier.TournamentChangedAsync(id);
		await notifier.MatchesSyncedAsync(id);

		return await ToDtoAsync(tournament, ct);
	}

	public async Task<TournamentDto?> StartAsync(Guid id, CancellationToken ct = default)
	{
		var tournament = await tournamentRepository.GetByIdAsync(id, ct);
		if (tournament is null)
		{
			return null;
		}

		if (TournamentStateMachine.HasStarted(tournament.State))
		{
			throw new TournamentLifecycleException("Tournament has already started.");
		}

		if (!TournamentStateMachine.IsBracketCreated(tournament.State))
		{
			throw new TournamentLifecycleException("The bracket has not been created yet.");
		}

		// Discord resource creation is idempotent (it checks for an existing role/channel id before
		// creating one and skips already-assigned roles), so retrying it on a transient failure is
		// safe. It's not transactional: a role created before a later failure stays — the admin
		// just calls start again once the underlying issue (Discord outage, bad role name…) is
		// fixed, and what already succeeded is skipped. The matches already exist from bracket
		// creation; starting only lets them be played.
		try
		{
			await RetryAsync(() => discordResourcesService.CreateResourcesAsync(id, ct), ct);
		}
		catch (Exception ex)
		{
			throw new TournamentLifecycleException(
				$"Failed to start tournament after retries — tournament is still not started, though some Discord resources may already be set up from this attempt (safe to just retry). {ex.Message}");
		}

		if (!TournamentStateMachine.HasStarted(tournament.State))
		{
			tournament.State = TournamentState.Started;
		}

		await tournamentRepository.SaveChangesAsync(ct);
		await notifier.TournamentChangedAsync(id);

		var now = DateTimeOffset.UtcNow;
		var participants = await participantRepository.ListByTournamentAsync(id, ct);
		foreach (var playerId in (await teamRepository.ExpandToPlayerIdsAsync(participants.Select(p => (p.PlayerId, p.TeamId)), ct)).Distinct())
		{
			var player = await playerRepository.GetEntityByIdAsync(playerId, ct);
			if (player is not null)
			{
				player.LastPlayedTournamentTimestamp = now;
			}
		}

		await playerRepository.SaveChangesAsync(ct);

		return await ToDtoAsync(tournament, ct);
	}

	private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1)];

	// Runs `action` up to RetryDelays.Length + 1 times, with a short backoff between attempts,
	// re-throwing the last failure once attempts are exhausted. Shared by Start/Finish/Delete —
	// each wraps its external Discord calls in this so the whole sequence can be
	// retried as one unit on a transient failure without double-creating/double-deleting anything
	// (every step it wraps is individually idempotent). Individual HTTP calls already retry their
	// own transient failures (see ThirdPartyHttpResilience); this covers the sequence as a whole.
	// ApiExceptions (validation/state errors, e.g. "no participant with final_rank 1") are never
	// retried — they'd fail identically every time.
	private static async Task RetryAsync(Func<Task> action, CancellationToken ct)
	{
		for (var attempt = 0; ; attempt++)
		{
			try
			{
				await action();
				return;
			}
			catch (Exception ex) when (ex is not ApiException && attempt < RetryDelays.Length && !ct.IsCancellationRequested)
			{
				await Task.Delay(RetryDelays[attempt], ct);
			}
		}
	}

	public async Task<TournamentDto?> FinishAsync(Guid id, CancellationToken ct = default)
	{
		var tournament = await tournamentRepository.GetByIdAsync(id, ct);
		if (tournament is null)
		{
			return null;
		}

		// Finishing awards played counts and the prize Elo bonus — never apply those twice.
		if (tournament.State == TournamentState.Complete)
		{
			throw new TournamentLifecycleException("Tournament is already complete.");
		}

		if (!TournamentStateMachine.HasStarted(tournament.State))
		{
			throw new TournamentLifecycleException("Tournament has not started yet.");
		}

		// Discord teardown is idempotent (delete checks for an existing role/channel id first), so
		// it's retried as one unit — but it commits its own DB write as soon as it succeeds, so a
		// later failure doesn't undo it. Only the tournament's own State is guaranteed to stay
		// unchanged if retries run out, so the admin can just call finish again once fixed.
		try
		{
			await RetryAsync(async () =>
			{
				await discordResourcesService.DeleteChannelAsync(id, ct);
				await discordResourcesService.DeleteRoleAsync(id, ct);
			}, ct);
		}
		catch (Exception ex)
		{
			throw new TournamentLifecycleException(
				$"Failed to finish tournament after retries — tournament is still not complete, though its Discord role/channel may already be torn down from this attempt (safe to just retry). {ex.Message}");
		}

		var isTeamTournament = tournament.RegistrationType == TournamentRegistrationType.Team;

		// Ranking, completing the tournament and awarding its rewards commit together: since Finish
		// refuses an already-complete tournament, a partial commit (Complete but bonuses missing)
		// could never be repaired by retrying. Two concurrent Finish calls conflict on
		// Tournament.Version here.
		await unitOfWork.InTransactionAsync(async () =>
		{
			var participants = await bracketService.FinalizeRanksAsync(tournament, ct);
			var winner = participants.Single(p => p.FinalRank == 1);

			tournament.State = TournamentState.Complete;
			tournament.WinnerId = isTeamTournament ? null : winner.PlayerId;
			tournament.WinnerTeamId = isTeamTournament ? winner.TeamId : null;
			await tournamentRepository.SaveChangesAsync(ct);

			var isSeasonal = (tournament.OverrideEloChange ?? -1) == -1;

			await IncrementPlayedCountsAsync(participants, isSeasonal, ct);
			await ApplyPrizeEloBonusAsync(participants, isTeamTournament, isSeasonal, ct);
		}, ct);

		await notifier.TournamentChangedAsync(id);

		return await ToDtoAsync(tournament, ct);
	}

	// PlayedTournamentCount/SeasonalPlayedTournamentCount — every participating player, win or
	// lose (both members of a team, for team tournaments).
	private async Task IncrementPlayedCountsAsync(List<TournamentParticipant> participants, bool isSeasonal, CancellationToken ct)
	{
		foreach (var playerId in (await teamRepository.ExpandToPlayerIdsAsync(participants.Select(p => (p.PlayerId, p.TeamId)), ct)).Distinct())
		{
			var player = await playerRepository.GetEntityByIdAsync(playerId, ct);
			if (player is null)
			{
				continue;
			}

			player.PlayedTournamentCount++;
			if (isSeasonal)
			{
				player.SeasonalPlayedTournamentCount++;
			}
		}

		await playerRepository.SaveChangesAsync(ct);
	}

	// Flat Elo bonus for top-3 (seasonal) / top-2 (non-seasonal) finishers. Both team members get
	// the same flat bonus applied independently — same "no combined formula, just extend the
	// existing per-player logic to both members" spirit as the Elo-averaging approach used for
	// team match results. A shared rank (both semifinal losers on 3rd without a 3rd-place match)
	// gives each of them the bonus.
	private async Task ApplyPrizeEloBonusAsync(
		List<TournamentParticipant> participants, bool isTeamTournament, bool isSeasonal, CancellationToken ct)
	{
		var prizeEloBonus = isSeasonal
			? new Dictionary<int, int> { [1] = 40, [2] = 20, [3] = 10 }
			: new Dictionary<int, int> { [1] = 15, [2] = 10 };

		foreach (var participant in participants)
		{
			if (participant.FinalRank is not int rank || !prizeEloBonus.TryGetValue(rank, out var bonus))
			{
				continue;
			}

			var bonusPlayerIds = isTeamTournament
				? (participant.TeamId is Guid teamId ? await ExpandTeamAsync(teamId, ct) : [])
				: (participant.PlayerId is Guid pid ? [pid] : []);

			foreach (var bonusPlayerId in bonusPlayerIds)
			{
				var player = await playerRepository.GetEntityByIdAsync(bonusPlayerId, ct);
				if (player is null)
				{
					continue;
				}

				await historyService.SetPlayerEloAsync(player.Uid, player.Elo + bonus, ct);
			}
		}
	}

	public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
	{
		var tournament = await tournamentRepository.GetByIdAsync(id, ct);
		if (tournament is null)
		{
			return false;
		}

		// Discord cleanup runs as one retryable unit before the tournament row itself is deleted
		// (the bracket's participants and matches go with it by cascade). Discord delete checks
		// for an existing role/channel id, so retrying is safe — but each delete commits its own DB
		// write as soon as it succeeds, so a later failure doesn't undo it. The tournament row is
		// only deleted once the whole sequence succeeds. A tournament run on Challonge leaves its
		// bracket there; it's just no longer linked from anywhere.
		try
		{
			await RetryAsync(async () =>
			{
				await discordResourcesService.DeleteChannelAsync(id, ct);
				await discordResourcesService.DeleteRoleAsync(id, ct);
			}, ct);
		}
		catch (Exception ex)
		{
			throw new TournamentLifecycleException(
				$"Failed to delete tournament after retries — tournament still exists, though its Discord role/channel may already be torn down from this attempt (safe to just retry). {ex.Message}");
		}

		await tournamentRepository.DeleteAsync(id, ct);
		await notifier.TournamentChangedAsync(id);

		return true;
	}

	public async Task<SplitTournamentResult?> SplitAsync(Guid id, SplitTournamentRequest request, CancellationToken ct = default)
	{
		if (request.DivisionGroups.Count < 2 || request.DivisionGroups.Any(g => g.Count < 2))
		{
			throw new TournamentLifecycleException(
				"divisionGroups must be an array of >=2 groups, each with >=2 players.");
		}

		var tournament = await tournamentRepository.GetByIdAsync(id, ct);
		if (tournament is null)
		{
			return null;
		}

		if (TournamentStateMachine.IsLocked(tournament.State))
		{
			throw new TournamentLifecycleException("Tournament has already started.");
		}

		if (tournament.DivisionGroupId is not null)
		{
			throw new TournamentLifecycleException("Tournament has already been split into divisions.");
		}

		if (tournament.RegistrationType != TournamentRegistrationType.Solo)
		{
			throw new TournamentLifecycleException("Splitting into divisions currently only supports solo tournaments.");
		}

		var approved = (await registrationRepository.ListByTournamentAsync(id, ct)).Where(r => r.Approved).ToList();
		var approvedIds = approved.Select(r => r.PlayerId!.Value).ToHashSet();

		var groupedUids = request.DivisionGroups.SelectMany(g => g).ToList();
		var uidToId = await playerRepository.GetIdsByUidsAsync(groupedUids, ct);
		var groupedIds = groupedUids.Select(uid => uidToId.GetValueOrDefault(uid)).ToList();
		var groupedIdsSet = groupedIds.ToHashSet();
		if (groupedUids.Any(uid => !uidToId.ContainsKey(uid)) ||
			groupedIds.Count != groupedIdsSet.Count ||
			groupedIds.Count != approved.Count ||
			!groupedIds.All(approvedIds.Contains))
		{
			throw new TournamentLifecycleException("divisionGroups must contain every approved registration exactly once.");
		}

		var registrationsById = approved.ToDictionary(r => r.PlayerId!.Value, r => r);
		var groups = request.DivisionGroups.Select(g => g.Select(uid => registrationsById[uidToId[uid]]).ToList()).ToList();

		var closedRegistrationEndDate = tournament.RegistrationEndDate < DateTimeOffset.UtcNow
			? tournament.RegistrationEndDate
			: DateTimeOffset.UtcNow;

		var originalName = tournament.Name;
		var originalDiscordChannelName = tournament.DiscordChannelName;
		var originalId = tournament.Id;

		var division1Ids = groups[0].Select(r => r.PlayerId!.Value).ToHashSet();
		var divisionTournamentIds = new List<Guid> { originalId };

		// One transaction for the whole split — otherwise a failure partway leaves the original
		// renamed to "A" with the other divisions' players removed but their tournaments missing.
		await unitOfWork.InTransactionAsync(async () =>
		{
			tournament.Name = $"{originalName} A";
			if (originalDiscordChannelName is not null)
			{
				tournament.DiscordChannelName = $"{originalDiscordChannelName}-A";
			}

			tournament.DivisionGroupId = originalId.ToString();
			tournament.DivisionIndex = 1;
			tournament.RegistrationEndDate = closedRegistrationEndDate;
			tournament.State = TournamentState.RegistrationClosed;

			foreach (var registration in approved.Where(r => !division1Ids.Contains(r.PlayerId!.Value)))
			{
				await registrationRepository.DeleteAsync(registration, ct);
			}

			await tournamentRepository.SaveChangesAsync(ct);

			for (var i = 1; i < groups.Count; i++)
			{
				var divisionIndex = i + 1;
				var divisionLetter = (char)('A' + i);

				var newTournament = new Tournament
				{
					Id = Guid.NewGuid(),
					Name = $"{originalName} {divisionLetter}",
					Description = tournament.Description,
					RegistrationStartDate = tournament.RegistrationStartDate,
					RegistrationEndDate = closedRegistrationEndDate,
					TournamentStartDate = tournament.TournamentStartDate,
					TournamentEndDate = tournament.TournamentEndDate,
					MinCost = tournament.MinCost,
					MaxCost = tournament.MaxCost,
					MinCharacters = tournament.MinCharacters,
					MinTier = tournament.MinTier,
					MaxTier = tournament.MaxTier,
					RegistrationType = tournament.RegistrationType,
					GameMode = tournament.GameMode,
					BracketType = tournament.BracketType,
					OverrideEloChange = tournament.OverrideEloChange,
					ConsolationMatchesTargetRank = tournament.ConsolationMatchesTargetRank,
					Visible = tournament.Visible,
					DiscordRoleName = tournament.DiscordRoleName,
					DiscordChannelName = originalDiscordChannelName is not null ? $"{originalDiscordChannelName}-{divisionLetter}" : null,
					State = TournamentState.RegistrationClosed,
					DivisionGroupId = originalId.ToString(),
					DivisionIndex = divisionIndex,
				};

				await tournamentRepository.CreateAsync(newTournament, ct);
				divisionTournamentIds.Add(newTournament.Id);

				foreach (var registration in groups[i])
				{
					await registrationRepository.CreateAsync(new TournamentRegistration
					{
						Id = Guid.NewGuid(),
						TournamentId = newTournament.Id,
						PlayerId = registration.PlayerId,
						TeamId = registration.TeamId,
						Player1Details = CloneDetails(registration.Player1Details),
						Player2Details = registration.Player2Details is null ? null : CloneDetails(registration.Player2Details),
						Approved = true,
						RegisteredAt = registration.RegisteredAt,
						UpdatedAt = DateTimeOffset.UtcNow,
					}, ct);
				}
			}
		}, ct);

		foreach (var divisionTournamentId in divisionTournamentIds)
		{
			await notifier.TournamentChangedAsync(divisionTournamentId);
		}

		return new SplitTournamentResult(divisionTournamentIds);
	}

	public async Task IncrementTournamentCountAsync(Guid id, bool seasonal, CancellationToken ct = default)
	{
		var approved = (await registrationRepository.ListByTournamentAsync(id, ct)).Where(r => r.Approved).ToList();
		if (approved.Count == 0)
		{
			throw new TournamentLifecycleException("No approved participants found for this tournament.");
		}

		var playerIds = await teamRepository.ExpandToPlayerIdsAsync(approved.Select(r => (r.PlayerId, r.TeamId)), ct);

		foreach (var playerId in playerIds.Distinct())
		{
			var player = await playerRepository.GetEntityByIdAsync(playerId, ct);
			if (player is null)
			{
				continue;
			}

			player.PlayedTournamentCount++;
			if (seasonal)
			{
				player.SeasonalPlayedTournamentCount++;
			}
		}

		await playerRepository.SaveChangesAsync(ct);
	}

	private async Task<List<Guid>> ExpandTeamAsync(Guid teamId, CancellationToken ct)
	{
		var team = await teamRepository.GetByIdAsync(teamId, ct);
		return team is null ? [] : [team.Player1Id, team.Player2Id];
	}

	private static PlayerRegistrationDetails CloneDetails(PlayerRegistrationDetails d) => new()
	{
		GameUid = d.GameUid,
		PrizeAsMoney = d.PrizeAsMoney,
		PrizeUid = d.PrizeUid,
		DarteNickname = d.DarteNickname,
		DartePresetName = d.DartePresetName,
		RosterName = d.RosterName,
		RosterScreenshotUrl = d.RosterScreenshotUrl,
		HoyolabScreenshotUrl = d.HoyolabScreenshotUrl,
	};

	// t.WinnerId is only ever set post-FinishAsync, so this is a no-op query for every tournament
	// still in progress — the ternary short-circuits before touching playerRepository.
	private async Task<TournamentDto> ToDtoAsync(Tournament t, CancellationToken ct)
	{
		var winnerUid = t.WinnerId is Guid wid ? (await playerRepository.GetByIdAsync(wid, ct))?.Uid : null;
		return TournamentService.ToDto(t, winnerUid);
	}
}
