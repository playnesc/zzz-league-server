using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.Discord;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Teams;

namespace ZenlessLeague.Api.Features.History;

public interface IHistoryService
{
	// Direct port of functions/utils/registerMatchResult.js. `tournamentId`/`kind` are null/None
	// for ad-hoc (non-tournament) matches. Player ids are internal Guid FKs (Player.Id) —
	// callers resolve a Firebase uid to one first (see HistoryEndpoints's ad-hoc /matches route).
	Task<HistoryEntryDto> RegisterMatchResultAsync(
		Guid p1PlayerId, Guid p2PlayerId, bool p1Win, int overrideEloChange,
		Guid? tournamentId = null, Guid? tournamentMatchId = null, TournamentMatchKind kind = TournamentMatchKind.None,
		string? resultP1 = null, string? resultP2 = null, string? resultScreenshotUrl = null,
		bool techLoss = false, CancellationToken ct = default);

	// 2v2 equivalent of RegisterMatchResultAsync. There's no such thing in the legacy app — this
	// is new domain modeling (see Tournament.RegistrationType). A team match produces TWO
	// HistoryEntry rows ("lanes": creator-vs-creator, player2-vs-player2) since HistoryEntry is
	// inherently a 2-player record; both lanes carry the identical team-level change values, per
	// the user's explicit choice of "average team rating, same change for all 4 players" over
	// computing something per-individual.
	Task<(HistoryEntryDto Entry1, HistoryEntryDto Entry2)> RegisterTeamMatchResultAsync(
		Guid p1TeamId, Guid p2TeamId, bool p1Win, int overrideEloChange,
		Guid tournamentId, Guid tournamentMatchId, TournamentMatchKind kind,
		string? resultP1 = null, string? resultP2 = null, string? resultScreenshotUrl = null,
		bool techLoss = false, CancellationToken ct = default);

	// Undoes a history entry's effect on the referenced player(s) and deletes it. Ports both the
	// reversal block embedded in functions/admin/tournament/adminSetMatchResult.js (2-player match
	// results — used before a result gets recomputed) and functions/admin/history/deleteHistoryEntry.js's
	// single-player branch (Elo-adjustment entries — reverts elo directly, recomputes tier flags).
	// Returns false if the entry doesn't exist.
	Task<bool> ReverseEntryAsync(Guid historyEntryId, CancellationToken ct = default);

	// Port of functions/utils/setPlayerElo.js. Keyed by uid (not Player.Id) since it's driven
	// directly off an admin-facing route parameter.
	Task<HistoryEntryDto> SetPlayerEloAsync(string uid, int elo, CancellationToken ct = default);

	Task<List<HistoryEntryDto>> ListAsync(CancellationToken ct = default);
	Task<List<HistoryEntryDto>> ListByPlayerAsync(string uid, CancellationToken ct = default);

	Task<HistoryPageDto> ListPageAsync(int take, DateTimeOffset? beforeTimestamp, Guid? beforeId, CancellationToken ct = default);
	Task<HistoryPageDto> ListPageByPlayerAsync(string uid, int take, DateTimeOffset? beforeTimestamp, Guid? beforeId, CancellationToken ct = default);
}

/// <summary>Thrown for history validation failures (missing player) — maps to 400.</summary>
public class HistoryValidationException(string message) : ValidationApiException(message);

public class HistoryService(
	IPlayerRepository playerRepository,
	IHistoryRepository historyRepository,
	ITeamRepository teamRepository,
	IDiscordRoleAssignmentService discordRoleAssignmentService) : IHistoryService
{
	public async Task<HistoryEntryDto> RegisterMatchResultAsync(
		Guid p1PlayerId, Guid p2PlayerId, bool p1Win, int overrideEloChange,
		Guid? tournamentId = null, Guid? tournamentMatchId = null, TournamentMatchKind kind = TournamentMatchKind.None,
		string? resultP1 = null, string? resultP2 = null, string? resultScreenshotUrl = null,
		bool techLoss = false, CancellationToken ct = default)
	{
		var p1 = await playerRepository.GetEntityByIdAsync(p1PlayerId, ct)
			?? throw new HistoryValidationException($"Player '{p1PlayerId}' does not exist.");
		var p2 = await playerRepository.GetEntityByIdAsync(p2PlayerId, ct)
			?? throw new HistoryValidationException($"Player '{p2PlayerId}' does not exist.");

		var r1 = new EloCalculator.RatingInfo(p1.Elo, p1.TournamentPoints, p1.IsMidConfirmed);
		var r2 = new EloCalculator.RatingInfo(p2.Elo, p2.TournamentPoints, p2.IsMidConfirmed);
		var (p1Change, p2Change) = CalculateChanges(r1, r2, p1Win, overrideEloChange, techLoss);

		p1.TournamentPoints += p1Change;
		p2.TournamentPoints += p2Change;
		if (p1Win)
		{
			p1.Wins++;
			p2.Losses++;
		}
		else
		{
			p1.Losses++;
			p2.Wins++;
		}

		var now = DateTimeOffset.UtcNow;
		p1.LastPlayedTournamentTimestamp = now;
		p2.LastPlayedTournamentTimestamp = now;

		var entry = new HistoryEntry
		{
			Id = Guid.NewGuid(),
			P1PlayerId = p1PlayerId,
			P1Change = p1Change,
			P2PlayerId = p2PlayerId,
			P2Change = p2Change,
			TournamentId = tournamentId,
			TournamentMatchId = tournamentMatchId,
			// Legacy: when there's no tournamentId, the "kind" sentinel is forced from techLoss
			// regardless of what the caller passed — a real tournament match always passes its
			// own tournamentId, so this branch only ever applies to ad-hoc matches.
			Kind = tournamentId is null ? (techLoss ? TournamentMatchKind.TechLoss : TournamentMatchKind.Custom) : kind,
			ResultP1 = resultP1,
			ResultP2 = resultP2,
			ResultScreenshotUrl = resultScreenshotUrl,
			Timestamp = now,
		};

		await historyRepository.CreateAsync(entry, ct);
		return ToDto(entry, p1.Uid, p2.Uid);
	}

	public async Task<(HistoryEntryDto Entry1, HistoryEntryDto Entry2)> RegisterTeamMatchResultAsync(
		Guid p1TeamId, Guid p2TeamId, bool p1Win, int overrideEloChange,
		Guid tournamentId, Guid tournamentMatchId, TournamentMatchKind kind,
		string? resultP1 = null, string? resultP2 = null, string? resultScreenshotUrl = null,
		bool techLoss = false, CancellationToken ct = default)
	{
		var team1 = await teamRepository.GetByIdAsync(p1TeamId, ct)
			?? throw new HistoryValidationException($"Team '{p1TeamId}' does not exist.");
		var team2 = await teamRepository.GetByIdAsync(p2TeamId, ct)
			?? throw new HistoryValidationException($"Team '{p2TeamId}' does not exist.");

		var team1P1 = await playerRepository.GetEntityByIdAsync(team1.Player1Id, ct)
			?? throw new HistoryValidationException($"Player '{team1.Player1Id}' does not exist.");
		var team1P2 = await playerRepository.GetEntityByIdAsync(team1.Player2Id, ct)
			?? throw new HistoryValidationException($"Player '{team1.Player2Id}' does not exist.");
		var team2P1 = await playerRepository.GetEntityByIdAsync(team2.Player1Id, ct)
			?? throw new HistoryValidationException($"Player '{team2.Player1Id}' does not exist.");
		var team2P2 = await playerRepository.GetEntityByIdAsync(team2.Player2Id, ct)
			?? throw new HistoryValidationException($"Player '{team2.Player2Id}' does not exist.");

		// Team rating = average effective rating (elo + tournamentPoints) of its two members,
		// fed to EloCalculator as a single virtual "player" so the whole win/loss/K-factor formula
		// is reused unchanged. IsMidConfirmed requires BOTH members confirmed — a judgment call
		// (not specified by the user) reasoning that a team is only as "proven" as its weaker half.
		var r1 = new EloCalculator.RatingInfo(TeamEffectiveRating(team1P1, team1P2), 0, team1P1.IsMidConfirmed && team1P2.IsMidConfirmed);
		var r2 = new EloCalculator.RatingInfo(TeamEffectiveRating(team2P1, team2P2), 0, team2P1.IsMidConfirmed && team2P2.IsMidConfirmed);
		var (team1Change, team2Change) = CalculateChanges(r1, r2, p1Win, overrideEloChange, techLoss);

		var now = DateTimeOffset.UtcNow;
		ApplyTeamMemberChange(team1P1, team1Change, p1Win, now);
		ApplyTeamMemberChange(team1P2, team1Change, p1Win, now);
		ApplyTeamMemberChange(team2P1, team2Change, !p1Win, now);
		ApplyTeamMemberChange(team2P2, team2Change, !p1Win, now);

		var entry1 = new HistoryEntry
		{
			Id = Guid.NewGuid(),
			P1PlayerId = team1P1.Id,
			P1Change = team1Change,
			P2PlayerId = team2P1.Id,
			P2Change = team2Change,
			TournamentId = tournamentId,
			TournamentMatchId = tournamentMatchId,
			Kind = kind,
			ResultP1 = resultP1,
			ResultP2 = resultP2,
			ResultScreenshotUrl = resultScreenshotUrl,
			Timestamp = now,
		};
		var entry2 = new HistoryEntry
		{
			Id = Guid.NewGuid(),
			P1PlayerId = team1P2.Id,
			P1Change = team1Change,
			P2PlayerId = team2P2.Id,
			P2Change = team2Change,
			TournamentId = tournamentId,
			TournamentMatchId = tournamentMatchId,
			Kind = kind,
			ResultP1 = resultP1,
			ResultP2 = resultP2,
			ResultScreenshotUrl = resultScreenshotUrl,
			Timestamp = now,
		};

		await historyRepository.CreateAsync(entry1, ct);
		await historyRepository.CreateAsync(entry2, ct);
		return (ToDto(entry1, team1P1.Uid, team2P1.Uid), ToDto(entry2, team1P2.Uid, team2P2.Uid));
	}

	public async Task<bool> ReverseEntryAsync(Guid historyEntryId, CancellationToken ct = default)
	{
		var entry = await historyRepository.GetByIdAsync(historyEntryId, ct);
		if (entry is null)
		{
			return false;
		}

		if (entry.P2PlayerId is not null && entry.P2Change is not null)
		{
			var p1 = await playerRepository.GetEntityByIdAsync(entry.P1PlayerId, ct)
				?? throw new InvalidOperationException($"Player '{entry.P1PlayerId}' referenced by history entry '{entry.Id}' is missing.");
			var p2 = await playerRepository.GetEntityByIdAsync(entry.P2PlayerId.Value, ct)
				?? throw new InvalidOperationException($"Player '{entry.P2PlayerId}' referenced by history entry '{entry.Id}' is missing.");

			var oldP1Won = entry.P1Change > entry.P2Change.Value;

			p1.TournamentPoints -= entry.P1Change;
			p2.TournamentPoints -= entry.P2Change.Value;

			if (oldP1Won)
			{
				p1.Wins--;
				p2.Losses--;
			}
			else
			{
				p1.Losses--;
				p2.Wins--;
			}
		}
		else
		{
			// Single-player Elo adjustment (e.g. setPlayerElo/updatePlayerElo) — revert elo
			// directly and recompute tier flags, matching deleteHistoryEntry.js's other branch.
			var p1 = await playerRepository.GetEntityByIdAsync(entry.P1PlayerId, ct)
				?? throw new InvalidOperationException($"Player '{entry.P1PlayerId}' referenced by history entry '{entry.Id}' is missing.");

			var revertedElo = p1.Elo - entry.P1Change;
			var (isMidConfirmed, isHighConfirmed) = TierConfirmationCalculator.Compute(p1.IsMidConfirmed, p1.IsHighConfirmed, revertedElo);
			p1.Elo = revertedElo;
			p1.IsMidConfirmed = isMidConfirmed;
			p1.IsHighConfirmed = isHighConfirmed;

			if (entry.P1Change > 0)
			{
				p1.Wins--;
			}
			else if (entry.P1Change < 0)
			{
				p1.Losses--;
			}
		}

		await historyRepository.DeleteAsync(entry, ct);
		return true;
	}

	public async Task<HistoryEntryDto> SetPlayerEloAsync(string uid, int elo, CancellationToken ct = default)
	{
		var player = await playerRepository.GetEntityByUidAsync(uid, ct)
			?? throw new HistoryValidationException($"Player '{uid}' does not exist.");

		var change = elo - player.Elo;
		var (isMidConfirmed, isHighConfirmed) = TierConfirmationCalculator.Compute(player.IsMidConfirmed, player.IsHighConfirmed, elo);

		player.Elo = elo;
		player.IsMidConfirmed = isMidConfirmed;
		player.IsHighConfirmed = isHighConfirmed;

		var entry = new HistoryEntry
		{
			Id = Guid.NewGuid(),
			P1PlayerId = player.Id,
			P1Change = change,
			Kind = TournamentMatchKind.Adjustment,
			Timestamp = DateTimeOffset.UtcNow,
		};

		await historyRepository.CreateAsync(entry, ct);

		// Legacy calls this fire-and-forget (unawaited); we await it but swallow failures so a
		// Discord outage (or Discord simply not being configured) never blocks an Elo update.
		try
		{
			await discordRoleAssignmentService.AssignRoleForPlayerAsync(uid, ct);
		}
		catch
		{
			// best-effort — see comment above.
		}

		return ToDto(entry, player.Uid, null);
	}

	public async Task<List<HistoryEntryDto>> ListAsync(CancellationToken ct = default) =>
		await ToDtosAsync(await historyRepository.ListAsync(ct), ct);

	public async Task<List<HistoryEntryDto>> ListByPlayerAsync(string uid, CancellationToken ct = default)
	{
		var player = await playerRepository.GetEntityByUidAsync(uid, ct);
		if (player is null)
		{
			return [];
		}

		return await ToDtosAsync(await historyRepository.ListByPlayerAsync(player.Id, ct), ct);
	}

	public async Task<HistoryPageDto> ListPageAsync(int take, DateTimeOffset? beforeTimestamp, Guid? beforeId, CancellationToken ct = default)
	{
		var page = await historyRepository.ListPageAsync(take, beforeTimestamp, beforeId, ct);
		return new HistoryPageDto(await ToDtosAsync(page, ct), page.Count == take);
	}

	public async Task<HistoryPageDto> ListPageByPlayerAsync(string uid, int take, DateTimeOffset? beforeTimestamp, Guid? beforeId, CancellationToken ct = default)
	{
		var player = await playerRepository.GetEntityByUidAsync(uid, ct);
		if (player is null)
		{
			return new HistoryPageDto([], false);
		}

		var page = await historyRepository.ListPageByPlayerAsync(player.Id, take, beforeTimestamp, beforeId, ct);
		return new HistoryPageDto(await ToDtosAsync(page, ct), page.Count == take);
	}

	// Average of both members' effective rating (elo + tournamentPoints) — used as a single
	// virtual "player" rating for team-match Elo (see RegisterTeamMatchResultAsync).
	private static int TeamEffectiveRating(Player a, Player b) =>
		(int)Math.Round((EloCalculator.EffectiveRating(new(a.Elo, a.TournamentPoints, a.IsMidConfirmed))
			+ EloCalculator.EffectiveRating(new(b.Elo, b.TournamentPoints, b.IsMidConfirmed))) / 2.0);

	private static void ApplyTeamMemberChange(Player player, int change, bool win, DateTimeOffset now)
	{
		player.TournamentPoints += change;
		if (win)
		{
			player.Wins++;
		}
		else
		{
			player.Losses++;
		}

		player.LastPlayedTournamentTimestamp = now;
	}

	// Direct port of registerMatchResult.js's win/loss change calculation. Also reused for
	// team matches with a team-average RatingInfo standing in for an individual player's.
	private static (int P1Change, int P2Change) CalculateChanges(EloCalculator.RatingInfo r1, EloCalculator.RatingInfo r2, bool p1Win, int overrideEloChange, bool techLoss)
	{
		if (techLoss)
		{
			var loserChange = overrideEloChange != -1
				? -overrideEloChange
				: (p1Win ? EloCalculator.CalculateChange(r2, r1, false) : EloCalculator.CalculateChange(r1, r2, false));
			return p1Win ? (0, loserChange) : (loserChange, 0);
		}

		var p1Change = overrideEloChange != -1
			? (p1Win ? overrideEloChange : -overrideEloChange)
			: EloCalculator.CalculateChange(r1, r2, p1Win);
		var p2Change = overrideEloChange != -1
			? (p1Win ? -overrideEloChange : overrideEloChange)
			: EloCalculator.CalculateChange(r2, r1, !p1Win);

		return (p1Change, p2Change);
	}

	// Batch Id -> Uid resolution for list-returning methods — one query for the whole page/list,
	// not one per row. Single-entry callers that already have the relevant Player(s) in hand
	// (RegisterMatchResultAsync et al.) use the other ToDto overload directly instead.
	private async Task<List<HistoryEntryDto>> ToDtosAsync(List<HistoryEntry> entries, CancellationToken ct)
	{
		var ids = entries
			.SelectMany(e => e.P2PlayerId is Guid p2 ? [e.P1PlayerId, p2] : new[] { e.P1PlayerId })
			.Distinct();
		var uidsById = await playerRepository.GetUidsByIdsAsync(ids, ct);

		return entries.Select(e => ToDto(
			e,
			uidsById.GetValueOrDefault(e.P1PlayerId) ?? throw new InvalidOperationException($"Player '{e.P1PlayerId}' referenced by history entry '{e.Id}' is missing."),
			e.P2PlayerId is Guid p2 ? uidsById.GetValueOrDefault(p2) ?? throw new InvalidOperationException($"Player '{p2}' referenced by history entry '{e.Id}' is missing.") : null))
			.ToList();
	}

	private static HistoryEntryDto ToDto(HistoryEntry e, string p1Uid, string? p2Uid) => new(
		e.Id, p1Uid, e.P1Change, p2Uid, e.P2Change,
		e.TournamentId, e.TournamentMatchId, e.Kind, e.ResultP1, e.ResultP2, e.ResultScreenshotUrl, e.Timestamp);
}
