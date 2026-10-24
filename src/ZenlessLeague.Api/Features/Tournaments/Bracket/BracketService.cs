using ZenlessLeague.Api.Features.History;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Tournaments.Matches;
using ZenlessLeague.Api.Features.Tournaments.Realtime;
using ZenlessLeague.Api.Infrastructure.Persistence;

namespace ZenlessLeague.Api.Features.Tournaments.Bracket;

public interface IBracketService
{
	// Writes the participants (randomly laid out) and the generated matches for a tournament whose
	// roster was just locked. The caller owns the transaction, the state change and notifications.
	Task CreateAsync(Tournament tournament, IReadOnlyList<Guid> entrantIds, CancellationToken ct = default);

	Task<BracketDto?> GetAsync(Guid tournamentId, CancellationToken ct = default);

	// Admin rearrangement (the UI shuffles or swaps a draft, then saves it) — only while the
	// tournament is BracketCreated, since no match has been played yet and the whole bracket can
	// simply be regenerated.
	Task<BracketDto?> SaveLayoutAsync(Guid tournamentId, SaveBracketLayoutRequest request, CancellationToken ct = default);

	// The bracket a layout would generate, without saving anything — the editor redraws the later
	// rounds from it after every swap, so what the admin sees is exactly what Save produces.
	Task<List<BracketPreviewMatchDto>?> PreviewLayoutAsync(Guid tournamentId, SaveBracketLayoutRequest request, CancellationToken ct = default);

	// Called inside MatchService's result transaction once `match` is complete with its winner set:
	// moves both sides into their next matches and flips Started -> AwaitingReview when nothing is
	// left to play. Returns whether the tournament's own state changed.
	Task<bool> AdvanceAsync(Tournament tournament, TournamentMatch match, CancellationToken ct = default);

	// The downstream matches that changing `match`'s winner would wipe: those with a result or a
	// submission, reached through matches that were already played.
	Task<List<TournamentMatch>> ListAffectedByWinnerChangeAsync(TournamentMatch match, CancellationToken ct = default);

	// Undoes everything `match` fed into (results, their Elo, the slots it filled) so a new winner
	// can be advanced — the equivalent of Challonge's "reopen". Runs inside the caller's transaction.
	Task ResetDownstreamAsync(Tournament tournament, TournamentMatch match, CancellationToken ct = default);

	// Ranks every participant from the finished bracket and stores them on the participant rows.
	Task<List<TournamentParticipant>> FinalizeRanksAsync(Tournament tournament, CancellationToken ct = default);
}

public class BracketService(
	ITournamentRepository tournamentRepository,
	ITournamentParticipantRepository participantRepository,
	ITournamentMatchRepository matchRepository,
	IPlayerRepository playerRepository,
	IHistoryService historyService,
	ITournamentNotifier notifier,
	IUnitOfWork unitOfWork) : IBracketService
{
	public const string Pending = "pending";
	public const string Open = "open";
	public const string Complete = "complete";

	public async Task CreateAsync(Tournament tournament, IReadOnlyList<Guid> entrantIds, CancellationToken ct = default)
	{
		var isTeam = tournament.RegistrationType == TournamentRegistrationType.Team;
		var slots = BracketLayout.Random(entrantIds, Random.Shared);

		await participantRepository.AddRangeAsync(entrantIds.Select(id => new TournamentParticipant
		{
			Id = Guid.NewGuid(),
			TournamentId = tournament.Id,
			PlayerId = isTeam ? null : id,
			TeamId = isTeam ? id : null,
			Slot = slots.IndexOf(id),
		}), ct);

		await GenerateMatchesAsync(tournament, slots, ct);
	}

	public async Task<BracketDto?> GetAsync(Guid tournamentId, CancellationToken ct = default)
	{
		var tournament = await tournamentRepository.GetByIdAsync(tournamentId, ct);
		if (tournament is null)
		{
			return null;
		}

		var participants = await participantRepository.ListByTournamentAsync(tournamentId, ct);
		return participants.Any(p => p.Slot is not null) ? await ToDtoAsync(tournament, participants, ct) : null;
	}

	public async Task<BracketDto?> SaveLayoutAsync(Guid tournamentId, SaveBracketLayoutRequest request, CancellationToken ct = default)
	{
		if (await LoadForRearrangingAsync(tournamentId, ct) is not { } rearranging)
		{
			return null;
		}

		var (tournament, participants) = rearranging;

		var slots = await ParseValidLayoutAsync(tournament, participants, request.Slots, ct);

		await unitOfWork.InTransactionAsync(async () =>
		{
			foreach (var participant in participants)
			{
				participant.Slot = slots.IndexOf(participant.EntrantId);
			}

			await participantRepository.SaveChangesAsync(ct);
			await matchRepository.RemoveRangeAsync(await matchRepository.ListByTournamentAsync(tournamentId, ct), ct);
			await GenerateMatchesAsync(tournament, slots, ct);
		}, ct);

		await notifier.MatchesSyncedAsync(tournamentId);
		return await ToDtoAsync(tournament, participants, ct);
	}

	public async Task<List<BracketPreviewMatchDto>?> PreviewLayoutAsync(Guid tournamentId, SaveBracketLayoutRequest request, CancellationToken ct = default)
	{
		if (await LoadForRearrangingAsync(tournamentId, ct) is not { } rearranging)
		{
			return null;
		}

		var (tournament, participants) = rearranging;

		var slots = await ParseValidLayoutAsync(tournament, participants, request.Slots, ct);

		// The request already names every entrant the way the API addresses them (uid / team id).
		var keys = new Dictionary<Guid, string>();
		for (var i = 0; i < slots.Count; i++)
		{
			if (slots[i] is Guid id)
			{
				keys[id] = request.Slots[i]!;
			}
		}

		string? Key(Guid? id) => id is Guid entrant ? keys[entrant] : null;
		return Generate(tournament, slots).Select(s => new BracketPreviewMatchDto(
			s.Number, s.Section, s.Round, s.Position, Key(s.P1), Key(s.P2),
			s.WinnerToNumber, s.WinnerToSlot, s.LoserToNumber, s.LoserToSlot)).ToList();
	}

	public async Task<bool> AdvanceAsync(Tournament tournament, TournamentMatch match, CancellationToken ct = default)
	{
		if (match.Section is null)
		{
			return false; // a Challonge-era match: there's no local bracket to move through
		}

		var matches = (await matchRepository.ListByTournamentAsync(tournament.Id, ct)).ToDictionary(m => m.Id);
		var winner = match.WinnerPlayerId ?? match.WinnerTeamId;
		var p1Won = winner is not null && winner == (match.P1PlayerId ?? match.P1TeamId);
		var (winnerPlayer, winnerTeam) = p1Won ? (match.P1PlayerId, match.P1TeamId) : (match.P2PlayerId, match.P2TeamId);
		var (loserPlayer, loserTeam) = p1Won ? (match.P2PlayerId, match.P2TeamId) : (match.P1PlayerId, match.P1TeamId);

		if (match.WinnerToMatchId is Guid winnerTo)
		{
			Place(matches[winnerTo], match.WinnerToSlot!.Value, winnerPlayer, winnerTeam);
		}

		if (match.LoserToMatchId is Guid loserTo)
		{
			Place(matches[loserTo], match.LoserToSlot!.Value, loserPlayer, loserTeam);
		}

		await matchRepository.SaveChangesAsync(ct);

		// Only ever Started -> AwaitingReview: correcting a result after Finish must not knock
		// Complete back, which would let Finish award prize Elo a second time.
		if (tournament.State == TournamentState.Started && matches.Values.Where(m => m.Section is not null).All(m => m.State == Complete))
		{
			tournament.State = TournamentState.AwaitingReview;
			await tournamentRepository.SaveChangesAsync(ct);
			return true;
		}

		return false;
	}

	public async Task<List<TournamentMatch>> ListAffectedByWinnerChangeAsync(TournamentMatch match, CancellationToken ct = default)
	{
		var matches = (await matchRepository.ListByTournamentAsync(match.TournamentId, ct)).ToDictionary(m => m.Id);
		var affected = new List<TournamentMatch>();
		var seen = new HashSet<Guid>();

		void Visit(Guid? id)
		{
			if (id is not Guid matchId || !seen.Add(matchId))
			{
				return;
			}

			var target = matches[matchId];
			if (target.State == Complete || HasSubmission(target))
			{
				affected.Add(target);
			}

			if (target.State == Complete)
			{
				Visit(target.WinnerToMatchId);
				Visit(target.LoserToMatchId);
			}
		}

		Visit(match.WinnerToMatchId);
		Visit(match.LoserToMatchId);
		return affected.OrderBy(m => m.Number).ToList();
	}

	public async Task ResetDownstreamAsync(Tournament tournament, TournamentMatch match, CancellationToken ct = default)
	{
		var matches = (await matchRepository.ListByTournamentAsync(tournament.Id, ct)).ToDictionary(m => m.Id);

		async Task ResetAsync(Guid? id, int? slot)
		{
			if (id is not Guid matchId)
			{
				return;
			}

			var target = matches[matchId];
			if (target.State == Complete)
			{
				// Its result rested on an entrant who's about to change — void it, and with it
				// everything it advanced in turn.
				await ResetAsync(target.WinnerToMatchId, target.WinnerToSlot);
				await ResetAsync(target.LoserToMatchId, target.LoserToSlot);
				await ReverseHistoryAsync(target, ct);
			}

			ClearResult(target);
			Place(target, slot!.Value, null, null);
		}

		await ResetAsync(match.WinnerToMatchId, match.WinnerToSlot);
		await ResetAsync(match.LoserToMatchId, match.LoserToSlot);
		await matchRepository.SaveChangesAsync(ct);

		if (tournament.State == TournamentState.AwaitingReview)
		{
			tournament.State = TournamentState.Started;
			await tournamentRepository.SaveChangesAsync(ct);
		}
	}

	public async Task<List<TournamentParticipant>> FinalizeRanksAsync(Tournament tournament, CancellationToken ct = default)
	{
		var format = BracketFormats.Parse(tournament.BracketType);
		var matches = (await matchRepository.ListByTournamentAsync(tournament.Id, ct)).Where(m => m.Section is not null).ToList();
		if (matches.Count == 0 || matches.Any(m => m.State != Complete))
		{
			throw new TournamentLifecycleException("Every bracket match must be finished first.");
		}

		var completed = matches.Select(m =>
		{
			var p1 = (m.P1PlayerId ?? m.P1TeamId)!.Value;
			var p2 = (m.P2PlayerId ?? m.P2TeamId)!.Value;
			var winner = (m.WinnerPlayerId ?? m.WinnerTeamId)!.Value;
			return new CompletedBracketMatch(m.Section!.Value, m.Round!.Value, winner, winner == p1 ? p2 : p1, m.LoserToMatchId is not null);
		}).ToList();

		var ranks = BracketEngine.ComputeRanks(format, completed);
		var participants = await participantRepository.ListByTournamentAsync(tournament.Id, ct);
		foreach (var participant in participants)
		{
			participant.FinalRank = ranks.GetValueOrDefault(participant.EntrantId);
		}

		await participantRepository.SaveChangesAsync(ct);
		return participants;
	}

	// The tournament and its participants, if its bracket may still be rearranged.
	private async Task<(Tournament Tournament, List<TournamentParticipant> Participants)?> LoadForRearrangingAsync(Guid tournamentId, CancellationToken ct)
	{
		var tournament = await tournamentRepository.GetByIdAsync(tournamentId, ct);
		if (tournament is null)
		{
			return null;
		}

		if (tournament.State != TournamentState.BracketCreated)
		{
			throw new TournamentLifecycleException("The bracket can only be rearranged before the tournament starts.");
		}

		return (tournament, await participantRepository.ListByTournamentAsync(tournamentId, ct));
	}

	private async Task<List<Guid?>> ParseValidLayoutAsync(
		Tournament tournament, List<TournamentParticipant> participants, List<string?> requested, CancellationToken ct)
	{
		var slots = await ParseSlotsAsync(tournament, requested, ct);
		BracketLayout.Validate(slots, participants.Select(p => p.EntrantId).ToList());
		return slots;
	}

	private static List<BracketMatchSpec> Generate(Tournament tournament, List<Guid?> slots) =>
		BracketEngine.Generate(BracketFormats.Parse(tournament.BracketType), slots, thirdPlace: tournament.ConsolationMatchesTargetRank is not null);

	private async Task GenerateMatchesAsync(Tournament tournament, List<Guid?> slots, CancellationToken ct)
	{
		var isTeam = tournament.RegistrationType == TournamentRegistrationType.Team;
		var specs = Generate(tournament, slots);

		var ids = specs.ToDictionary(s => s.Number, _ => Guid.NewGuid());
		var matches = specs.Select(s => new TournamentMatch
		{
			Id = ids[s.Number],
			TournamentId = tournament.Id,
			Section = s.Section,
			Round = s.Round,
			Position = s.Position,
			Number = s.Number,
			P1PlayerId = isTeam ? null : s.P1,
			P2PlayerId = isTeam ? null : s.P2,
			P1TeamId = isTeam ? s.P1 : null,
			P2TeamId = isTeam ? s.P2 : null,
			WinnerToMatchId = s.WinnerToNumber is int w ? ids[w] : null,
			WinnerToSlot = s.WinnerToSlot,
			LoserToMatchId = s.LoserToNumber is int l ? ids[l] : null,
			LoserToSlot = s.LoserToSlot,
			State = s.P1 is not null && s.P2 is not null ? Open : Pending,
		}).ToList();

		await matchRepository.AddRangeAsync(matches, ct);
	}

	private async Task<List<Guid?>> ParseSlotsAsync(Tournament tournament, List<string?> slots, CancellationToken ct)
	{
		if (tournament.RegistrationType == TournamentRegistrationType.Team)
		{
			return slots.Select(s => s is null
				? (Guid?)null
				: Guid.TryParse(s, out var teamId) ? teamId : throw new BracketValidationException($"'{s}' is not a team id.")).ToList();
		}

		var idsByUid = await playerRepository.GetIdsByUidsAsync(slots.Where(s => s is not null).Select(s => s!), ct);
		return slots.Select(s => s is null
			? (Guid?)null
			: idsByUid.TryGetValue(s, out var playerId) ? playerId : throw new BracketValidationException($"Player '{s}' does not exist.")).ToList();
	}

	private async Task<BracketDto> ToDtoAsync(Tournament tournament, List<TournamentParticipant> participants, CancellationToken ct)
	{
		var uidsById = await playerRepository.GetUidsByIdsAsync(participants.Where(p => p.PlayerId is not null).Select(p => p.PlayerId!.Value), ct);
		string Label(TournamentParticipant p) => p.PlayerId is Guid playerId ? uidsById.GetValueOrDefault(playerId)! : p.TeamId!.Value.ToString();

		var size = BracketEngine.SizeFor(participants.Count);
		var slots = new string?[size];
		foreach (var participant in participants.Where(p => p.Slot is not null))
		{
			slots[participant.Slot!.Value] = Label(participant);
		}

		var standings = participants
			.Where(p => p.FinalRank is not null)
			.OrderBy(p => p.FinalRank)
			.Select(p => new BracketStandingDto(p.PlayerId is Guid id ? uidsById.GetValueOrDefault(id) : null, p.TeamId, p.FinalRank!.Value))
			.ToList();

		return new BracketDto(tournament.BracketType ?? BracketFormats.SingleElimination, size, slots.ToList(), standings);
	}

	private async Task ReverseHistoryAsync(TournamentMatch match, CancellationToken ct)
	{
		if (match.HistoryEntryId is Guid entry)
		{
			await historyService.ReverseEntryAsync(entry, ct);
		}

		if (match.HistoryEntryId2 is Guid entry2)
		{
			await historyService.ReverseEntryAsync(entry2, ct);
		}
	}

	private static bool HasSubmission(TournamentMatch m) =>
		m.ResultP1 is not null || m.ResultP2 is not null || m.P1ApprovedResult || m.P2ApprovedResult || m.NexusResultId is not null;

	// Wipes a match back to "not played": result, confirmations, proof and the Elo bookkeeping ids
	// (whose entries the caller has already reversed if the match was complete).
	private static void ClearResult(TournamentMatch m)
	{
		m.WinnerPlayerId = null;
		m.WinnerTeamId = null;
		m.ResultP1 = null;
		m.ResultP2 = null;
		m.ResultScreenshotUrl = null;
		m.P1ApprovedResult = false;
		m.P2ApprovedResult = false;
		m.TechLossPlayerId = null;
		m.TechLossTeamId = null;
		m.NexusResultId = null;
		m.NexusSnapshot = null;
		m.NexusP1IsLeft = null;
		m.HistoryEntryId = null;
		m.HistoryEntryId2 = null;
		m.State = Pending;
	}

	private static void Place(TournamentMatch target, int slot, Guid? playerId, Guid? teamId)
	{
		if (slot == 1)
		{
			target.P1PlayerId = playerId;
			target.P1TeamId = teamId;
		}
		else
		{
			target.P2PlayerId = playerId;
			target.P2TeamId = teamId;
		}

		if (target.State != Complete)
		{
			var bothKnown = (target.P1PlayerId ?? target.P1TeamId) is not null && (target.P2PlayerId ?? target.P2TeamId) is not null;
			target.State = bothKnown ? Open : Pending;
		}
	}
}
