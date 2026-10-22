using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.Discord;
using ZenlessLeague.Api.Features.History;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Teams;
using ZenlessLeague.Api.Features.Tournaments.Challonge;
using ZenlessLeague.Api.Features.Tournaments.Matches;
using ZenlessLeague.Api.Features.Tournaments.Realtime;

namespace ZenlessLeague.Api.Features.Tournaments;

public interface ITournamentLifecycleService
{
	// Extended to also support team tournaments (see TournamentRegistrationType.Team). Team
	// participants are sent to Challonge as "{TeamName} ({Player1} + {Player2})".
	Task<TournamentDto?> CreateChallongeBracketAsync(Guid id, CancellationToken ct = default);

	// Retries its Challonge/Discord calls as one unit on transient failure — each is individually
	// idempotent, so retrying is always safe, but this is not a transaction: on a
	// TournamentLifecycleException, some of those calls (and their DB writes) may have already
	// gone through. Only the tournament's own State field is guaranteed to not advance until the
	// whole sequence succeeds.
	Task<TournamentDto?> StartAsync(Guid id, CancellationToken ct = default);
	Task<TournamentDto?> FinishAsync(Guid id, CancellationToken ct = default);
	Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

	Task<SplitTournamentResult?> SplitAsync(Guid id, SplitTournamentRequest request, CancellationToken ct = default);

	Task RefreshGamesAsync(Guid id, CancellationToken ct = default);

	Task IncrementTournamentCountAsync(Guid id, bool seasonal, CancellationToken ct = default);
}

public class TournamentLifecycleService(
	ITournamentRepository tournamentRepository,
	ITournamentRegistrationRepository registrationRepository,
	ITeamRepository teamRepository,
	IPlayerRepository playerRepository,
	ITournamentChallongeParticipantRepository challongeParticipantRepository,
	IChallongeClient challongeClient,
	IMatchService matchService,
	IHistoryService historyService,
	ITournamentDiscordResourcesService discordResourcesService,
	ITournamentNotifier notifier) : ITournamentLifecycleService
{
	public async Task<TournamentDto?> CreateChallongeBracketAsync(Guid id, CancellationToken ct = default)
	{
		var tournament = await tournamentRepository.GetByIdAsync(id, ct);
		if (tournament is null)
		{
			return null;
		}

		if (TournamentStateMachine.IsLocked(tournament.State))
		{
			throw new TournamentLifecycleException("Challonge bracket has already been created.");
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

		var tournamentType = tournament.BracketType ?? "single elimination";

		// "Uid" is really just an opaque correlation string the Challonge API echoes back
		// unchanged as each participant's "misc" field (see IChallongeClient) — it carries
		// Player.Id.ToString() for solo tournaments, Team.Id.ToString() for team tournaments.
		var participants = new List<(string Name, string Uid)>();
		if (isTeamTournament)
		{
			foreach (var registration in approved)
			{
				var team = await teamRepository.GetByIdAsync(registration.TeamId!.Value, ct)
					?? throw new TournamentLifecycleException($"Team '{registration.TeamId}' not found.");
				var creator = await playerRepository.GetByIdAsync(team.Player1Id, ct)
					?? throw new TournamentLifecycleException($"Player '{team.Player1Id}' not found.");
				var player2 = await playerRepository.GetByIdAsync(team.Player2Id, ct)
					?? throw new TournamentLifecycleException($"Player '{team.Player2Id}' not found.");

				participants.Add(($"{team.Name} ({creator.Name} + {player2.Name})", team.Id.ToString()));
			}
		}
		else
		{
			foreach (var registration in approved)
			{
				var player = await playerRepository.GetByIdAsync(registration.PlayerId!.Value, ct)
					?? throw new TournamentLifecycleException($"Player '{registration.PlayerId}' not found.");
				participants.Add((player.Name, registration.PlayerId!.Value.ToString()));
			}
		}

		var created = await challongeClient.CreateTournamentAsync(
			tournament.Name, tournamentType, tournament.Description, tournament.TournamentStartDate,
			tournament.ConsolationMatchesTargetRank, ct);

		await challongeClient.BulkAddParticipantsAsync(created.ChallongeTournamentId, participants, ct);
		var randomized = await challongeClient.RandomizeParticipantsAsync(created.ChallongeTournamentId, participants, ct);

		await challongeParticipantRepository.AddRangeAsync(
			randomized.Where(r => r.Misc is not null).Select(r => isTeamTournament
				? new TournamentChallongeParticipant
				{
					TournamentId = id,
					ChallongeParticipantId = r.ChallongeParticipantId,
					TeamId = Guid.Parse(r.Misc!),
				}
				: new TournamentChallongeParticipant
				{
					TournamentId = id,
					ChallongeParticipantId = r.ChallongeParticipantId,
					PlayerId = Guid.Parse(r.Misc!),
				}), ct);

		tournament.State = TournamentState.BracketCreated;
		tournament.ChallongeTournamentId = created.ChallongeTournamentId;
		tournament.ChallongeTournamentUrl = created.ChallongeTournamentUrl;
		await tournamentRepository.SaveChangesAsync(ct);
		await notifier.TournamentChangedAsync(id, ct);

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

		if (!TournamentStateMachine.IsBracketCreated(tournament.State) || tournament.ChallongeTournamentId is null)
		{
			throw new TournamentLifecycleException("Challonge bracket has not been created yet.");
		}

		// Discord resource creation runs FIRST, before Challonge is ever told to start — it's the
		// step most likely to fail on bad input (see TournamentService.NullIfBlank), and unlike
		// Challonge's start call it has no visible side effect for participants if it fails, so
		// failing here leaves the tournament genuinely untouched: no Challonge start call made, no
		// matches synced, still cleanly BracketCreated. Calling Challonge's start endpoint is the
		// point of no return — once that succeeds, real matches exist there whether or not our own
		// State ever reaches Started, so nothing after it may run before something that's still
		// allowed to fail for reasons within our control.
		//
		// All three steps are individually idempotent (Challonge tolerates being told to start an
		// already-started tournament; match sync upserts by ChallongeMatchId; Discord resource
		// creation checks for an existing role/channel id before creating one and skips
		// already-assigned roles), so retrying the whole sequence on a transient failure is safe —
		// no risk of double-creating a role or double-counting a match. Note this is
		// idempotent-safe-to-retry, not transactional: a role/channel created by the first step is
		// NOT rolled back if Challonge's start call then fails. The tournament's own State only
		// flips to Started once every step has actually succeeded, so on failure it's left exactly
		// as it was (BracketCreated) — the admin can just call start again once the underlying issue
		// (Discord outage, Challonge hiccup, etc.) is fixed, and whatever already succeeded will be
		// skipped rather than redone.
		try
		{
			await RetryAsync(async () =>
			{
				await discordResourcesService.CreateResourcesAsync(id, ct);
				await challongeClient.StartAsync(tournament.ChallongeTournamentId!, ct);
				await matchService.SyncFromChallongeAsync(id, ct);
			}, ct);
		}
		catch (Exception ex)
		{
			throw new TournamentLifecycleException(
				$"Failed to start tournament after retries — tournament is still not started, though some matches/Discord resources may already be set up from this attempt (safe to just retry). {ex.Message}");
		}

		if (!TournamentStateMachine.HasStarted(tournament.State))
		{
			tournament.State = TournamentState.Started;
		}

		await tournamentRepository.SaveChangesAsync(ct);
		await notifier.TournamentChangedAsync(id, ct);

		var now = DateTimeOffset.UtcNow;
		var participants = await challongeParticipantRepository.ListByTournamentAsync(id, ct);
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
	// each wraps its external Challonge/Discord calls in this so the whole sequence can be
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

		if (tournament.ChallongeTournamentId is null)
		{
			throw new TournamentLifecycleException("Tournament has no Challonge bracket.");
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

		var isTeamTournament = tournament.RegistrationType == TournamentRegistrationType.Team;
		List<ChallongeParticipantResult>? challongeParticipants = null;
		List<TournamentChallongeParticipant>? ourParticipants = null;
		Dictionary<string, TournamentChallongeParticipant>? ourParticipantsByChallongeId = null;
		ChallongeParticipantResult? winner = null;
		TournamentChallongeParticipant? winnerParticipant = null;

		// Challonge finalize, the participants fetch, and Discord teardown are each individually
		// idempotent (finalize tolerates an already-finalized tournament; the participants fetch
		// is a pure read; Discord delete checks for an existing role/channel id before deleting),
		// so the whole sequence is safe to retry as one unit — but Discord teardown commits its own
		// DB write as soon as it succeeds, so a later failure in this block doesn't undo it. Only
		// the tournament's own State is guaranteed to stay unchanged if retries run out, so the
		// admin can just call finish again once fixed.
		try
		{
			await RetryAsync(async () =>
			{
				await challongeClient.FinalizeAsync(tournament.ChallongeTournamentId!, ct);
				challongeParticipants = await challongeClient.GetAllParticipantsAsync(tournament.ChallongeTournamentId!, ct);

				ourParticipants = await challongeParticipantRepository.ListByTournamentAsync(id, ct);
				ourParticipantsByChallongeId = ourParticipants.ToDictionary(p => p.ChallongeParticipantId);

				winner = challongeParticipants.FirstOrDefault(p => p.FinalRank == 1)
					?? throw new TournamentLifecycleException("Could not find a participant with final_rank 1.");

				if (!ourParticipantsByChallongeId.TryGetValue(winner.ChallongeParticipantId, out winnerParticipant))
				{
					throw new TournamentLifecycleException("Tournament winner is not a known participant.");
				}

				await discordResourcesService.DeleteChannelAsync(id, ct);
				await discordResourcesService.DeleteRoleAsync(id, ct);
			}, ct);
		}
		catch (Exception ex)
		{
			throw new TournamentLifecycleException(
				$"Failed to finish tournament after retries — tournament is still not complete, though its Discord role/channel may already be torn down from this attempt (safe to just retry). {ex.Message}");
		}

		tournament.State = TournamentState.Complete;
		tournament.ChallongeWinnerId = winner!.ChallongeParticipantId;
		tournament.WinnerId = isTeamTournament ? null : winnerParticipant!.PlayerId;
		tournament.WinnerTeamId = isTeamTournament ? winnerParticipant!.TeamId : null;
		await tournamentRepository.SaveChangesAsync(ct);
		await notifier.TournamentChangedAsync(id, ct);

		var isSeasonal = (tournament.OverrideEloChange ?? -1) == -1;

		await IncrementPlayedCountsAsync(ourParticipants!, isSeasonal, ct);
		await ApplyPrizeEloBonusAsync(challongeParticipants!, ourParticipantsByChallongeId!, isTeamTournament, isSeasonal, ct);

		return await ToDtoAsync(tournament, ct);
	}

	// PlayedTournamentCount/SeasonalPlayedTournamentCount — every participating player, win or
	// lose (both members of a team, for team tournaments).
	private async Task IncrementPlayedCountsAsync(List<TournamentChallongeParticipant> participants, bool isSeasonal, CancellationToken ct)
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
	// team match results.
	private async Task ApplyPrizeEloBonusAsync(
		List<ChallongeParticipantResult> challongeParticipants,
		Dictionary<string, TournamentChallongeParticipant> ourParticipantsByChallongeId,
		bool isTeamTournament, bool isSeasonal, CancellationToken ct)
	{
		var prizeEloBonus = isSeasonal
			? new Dictionary<int, int> { [1] = 40, [2] = 20, [3] = 10 }
			: new Dictionary<int, int> { [1] = 15, [2] = 10 };

		foreach (var participant in challongeParticipants)
		{
			if (participant.FinalRank is not int rank || !prizeEloBonus.TryGetValue(rank, out var bonus))
			{
				continue;
			}

			if (!ourParticipantsByChallongeId.TryGetValue(participant.ChallongeParticipantId, out var ourParticipant))
			{
				continue;
			}

			var bonusPlayerIds = isTeamTournament
				? (ourParticipant.TeamId is Guid teamId ? await ExpandTeamAsync(teamId, ct) : [])
				: (ourParticipant.PlayerId is Guid pid ? [pid] : []);

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

		// Challonge and Discord cleanup run as one retryable unit before the tournament row itself
		// is deleted. Challonge refuses to delete a non-pending tournament, so it's reset to
		// pending first — Reset/DeleteTournamentAsync/Discord delete are all individually
		// idempotent (Reset tolerates already being pending, DeleteTournamentAsync tolerates 404,
		// Discord delete checks for an existing role/channel id), so the whole sequence is safe to
		// retry — but each Discord delete commits its own DB write as soon as it succeeds, so a
		// later failure in this block doesn't undo it. The tournament row itself is only ever
		// deleted once the whole sequence succeeds, so on failure it's guaranteed to still exist
		// and the admin can just retry the whole delete once the underlying issue is fixed.
		try
		{
			await RetryAsync(async () =>
			{
				if (tournament.ChallongeTournamentId is not null)
				{
					await challongeClient.ResetAsync(tournament.ChallongeTournamentId, ct);
					await challongeClient.DeleteTournamentAsync(tournament.ChallongeTournamentId, ct);
				}

				await discordResourcesService.DeleteChannelAsync(id, ct);
				await discordResourcesService.DeleteRoleAsync(id, ct);
			}, ct);
		}
		catch (Exception ex)
		{
			throw new TournamentLifecycleException(
				$"Failed to delete tournament after retries — tournament still exists, though its Challonge bracket/Discord role/channel may already be torn down from this attempt (safe to just retry). {ex.Message}");
		}

		await tournamentRepository.DeleteAsync(id, ct);
		await notifier.TournamentChangedAsync(id, ct);

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

		if (TournamentStateMachine.IsLocked(tournament.State) || tournament.ChallongeTournamentId is not null)
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

		var divisionTournamentIds = new List<Guid> { originalId };

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

		foreach (var divisionTournamentId in divisionTournamentIds)
		{
			await notifier.TournamentChangedAsync(divisionTournamentId, ct);
		}

		return new SplitTournamentResult(divisionTournamentIds);
	}

	public async Task RefreshGamesAsync(Guid id, CancellationToken ct = default) =>
		await matchService.SyncFromChallongeAsync(id, ct);

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
