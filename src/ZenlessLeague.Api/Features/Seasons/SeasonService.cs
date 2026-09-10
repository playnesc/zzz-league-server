using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.Discord;
using ZenlessLeague.Api.Features.History;
using ZenlessLeague.Api.Features.Players;

namespace ZenlessLeague.Api.Features.Seasons;

public interface ISeasonService
{
	// Port of functions/admin/finalizeTournament.js — season-end rollover: folds tournamentPoints
	// into elo for every player, resets points to 0, recomputes tier flags, and re-syncs Discord
	// roles for anyone whose tier changed. Distinct from TournamentService.FinishAsync, which
	// finalizes a single Challonge bracket (legacy's confusingly similar finishTournament.js).
	Task FinalizeSeasonAsync(CancellationToken ct = default);

	// Port of functions/admin/resetSeason.js — archives every player's current standing, then
	// resets elo to a tier-based baseline and zeroes tournamentPoints/seasonalPlayedTournamentCount.
	Task ResetSeasonAsync(string seasonName, CancellationToken ct = default);

	Task<List<ArchiveDto>> ListArchivesAsync(CancellationToken ct = default);

	// Port of functions/admin/history/deleteArchive.js.
	Task<bool> DeleteArchiveAsync(string seasonName, CancellationToken ct = default);

	// Port of functions/admin/history/backfillLastPlayedTimestamps.js. Returns the number of
	// players whose lastPlayedTournamentTimestamp was updated.
	Task<int> BackfillLastPlayedTimestampsAsync(CancellationToken ct = default);

	// Port of functions/admin/setTimer.js — a purely informational "league ends at" countdown
	// shown on the home page, set manually by an admin ahead of a planned ResetSeasonAsync.
	Task<DateTimeOffset?> GetTimerAsync(CancellationToken ct = default);
	Task SetTimerAsync(DateTimeOffset endTimestamp, CancellationToken ct = default);
}

/// <summary>Thrown for season validation failures (no players found, etc.) — maps to 400.</summary>
public class SeasonValidationException(string message) : ValidationApiException(message);

public class SeasonService(
	IPlayerRepository playerRepository,
	IArchiveRepository archiveRepository,
	IHistoryRepository historyRepository,
	IDiscordRoleAssignmentService discordRoleAssignmentService,
	ISeasonTimerRepository seasonTimerRepository) : ISeasonService
{
	public async Task FinalizeSeasonAsync(CancellationToken ct = default)
	{
		var players = await playerRepository.ListAsync(ct);
		if (players.Count == 0)
		{
			throw new SeasonValidationException("No players found.");
		}

		var uidsToResync = new List<string>();
		var now = DateTimeOffset.UtcNow;

		foreach (var p in players)
		{
			var next = p.Elo + p.TournamentPoints;
			var (isMidConfirmed, isHighConfirmed) = TierConfirmationCalculator.Compute(p.IsMidConfirmed, p.IsHighConfirmed, next);
			var tierChanged = isMidConfirmed != p.IsMidConfirmed || isHighConfirmed != p.IsHighConfirmed;

			p.Elo = next;
			p.TournamentPoints = 0;
			p.IsMidConfirmed = isMidConfirmed;
			p.IsHighConfirmed = isHighConfirmed;
			p.LastEloUpdateTimestamp = now;

			if (p.DiscordId is not null && tierChanged)
			{
				uidsToResync.Add(p.Uid);
			}
		}

		await playerRepository.SaveChangesAsync(ct);

		// Unguarded, matching legacy — the elo/points rollover above is already committed, so a
		// Discord failure here surfaces as an error without undoing the season finalize.
		await Task.WhenAll(uidsToResync.Select(uid => discordRoleAssignmentService.AssignRoleForPlayerAsync(uid, ct)));
	}

	public async Task ResetSeasonAsync(string seasonName, CancellationToken ct = default)
	{
		var players = await playerRepository.ListAsync(ct);
		if (players.Count == 0)
		{
			throw new SeasonValidationException("No players found.");
		}

		var snapshots = players.Select(p => new ArchivedPlayerSnapshot
		{
			Id = Guid.NewGuid(),
			Name = p.Name,
			Elo = p.Elo,
			IsMidConfirmed = p.IsMidConfirmed,
			IsHighConfirmed = p.IsHighConfirmed,
		}).ToList();

		// Re-running resetSeason with a name that already exists overwrites the snapshot, matching
		// legacy's plain RTDB-key overwrite (`updates["archives/" + seasonName] = ...`). Deleting
		// and recreating (rather than clearing/refilling the tracked Players collection) keeps
		// this simple and avoids EF change-tracking edge cases around severed required relationships.
		var existingArchive = await archiveRepository.GetBySeasonNameAsync(seasonName, ct);
		if (existingArchive is not null)
		{
			await archiveRepository.DeleteAsync(existingArchive, ct);
		}

		await archiveRepository.CreateAsync(new Archive
		{
			Id = Guid.NewGuid(),
			SeasonName = seasonName,
			CreatedAt = DateTimeOffset.UtcNow,
			Players = snapshots,
		}, ct);

		foreach (var player in players)
		{
			player.Elo = player.IsHighConfirmed ? 1400 : player.IsMidConfirmed ? 1200 : 1000;
			player.TournamentPoints = 0;
			player.SeasonalPlayedTournamentCount = 0;
		}

		await playerRepository.SaveChangesAsync(ct);
	}

	public async Task<List<ArchiveDto>> ListArchivesAsync(CancellationToken ct = default) =>
		(await archiveRepository.ListAsync(ct)).Select(ToDto).ToList();

	public async Task<bool> DeleteArchiveAsync(string seasonName, CancellationToken ct = default)
	{
		var archive = await archiveRepository.GetBySeasonNameAsync(seasonName, ct);
		if (archive is null)
		{
			return false;
		}

		await archiveRepository.DeleteAsync(archive, ct);
		return true;
	}

	public async Task<int> BackfillLastPlayedTimestampsAsync(CancellationToken ct = default)
	{
		var history = await historyRepository.ListAsync(ct);
		var lastPlayed = new Dictionary<Guid, DateTimeOffset>();

		foreach (var entry in history)
		{
			TrackLatest(lastPlayed, entry.P1PlayerId, entry.Timestamp);
			if (entry.P2PlayerId is Guid p2)
			{
				TrackLatest(lastPlayed, p2, entry.Timestamp);
			}
		}

		var updated = 0;
		foreach (var (playerId, timestamp) in lastPlayed)
		{
			var player = await playerRepository.GetEntityByIdAsync(playerId, ct);
			if (player is null)
			{
				continue;
			}

			if (player.LastPlayedTournamentTimestamp is null || timestamp > player.LastPlayedTournamentTimestamp)
			{
				player.LastPlayedTournamentTimestamp = timestamp;
				updated++;
			}
		}

		await playerRepository.SaveChangesAsync(ct);
		return updated;
	}

	public Task<DateTimeOffset?> GetTimerAsync(CancellationToken ct = default) =>
		seasonTimerRepository.GetEndTimestampAsync(ct);

	public Task SetTimerAsync(DateTimeOffset endTimestamp, CancellationToken ct = default) =>
		seasonTimerRepository.SetEndTimestampAsync(endTimestamp, ct);

	private static void TrackLatest(Dictionary<Guid, DateTimeOffset> lastPlayed, Guid playerId, DateTimeOffset timestamp)
	{
		if (!lastPlayed.TryGetValue(playerId, out var current) || timestamp > current)
		{
			lastPlayed[playerId] = timestamp;
		}
	}

	private static ArchiveDto ToDto(Archive a) => new(
		a.Id, a.SeasonName, a.CreatedAt,
		a.Players.Select(p => new ArchivedPlayerSnapshotDto(p.Name, p.Elo, p.IsMidConfirmed, p.IsHighConfirmed)).ToList());
}
