using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.Discord;
using ZenlessLeague.Api.Features.History;
using ZenlessLeague.Api.Features.Players;

namespace ZenlessLeague.Api.Features.Seasons;

public interface ISeasonService
{
	// Season-end rollover: folds tournamentPoints into elo for every player, resets points to 0,
	// recomputes tier flags, and re-syncs Discord roles for anyone whose tier changed. Distinct
	// from TournamentService.FinishAsync, which finalizes a single Challonge bracket.
	Task FinalizeSeasonAsync(CancellationToken ct = default);

	// Archives every player's current standing, then resets elo to a tier-based baseline and
	// zeroes tournamentPoints/seasonalPlayedTournamentCount.
	Task ResetSeasonAsync(string seasonName, CancellationToken ct = default);

	Task<List<ArchiveDto>> ListArchivesAsync(CancellationToken ct = default);
	Task<bool> DeleteArchiveAsync(string seasonName, CancellationToken ct = default);

	// Returns the number of players whose lastPlayedTournamentTimestamp was updated.
	Task<int> BackfillLastPlayedTimestampsAsync(CancellationToken ct = default);

	// Purely informational "league ends at" countdown shown on the home page, set manually by an
	// admin ahead of a planned ResetSeasonAsync.
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
	ISeasonTimerRepository seasonTimerRepository,
	ILogger<SeasonService> logger,
	IUnitOfWork unitOfWork) : ISeasonService
{
	public async Task FinalizeSeasonAsync(CancellationToken ct = default)
	{
		var players = await playerRepository.ListAsync(ct: ct);
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

		// Sequential, not Task.WhenAll — each call reads the player through the shared scoped
		// DbContext, which doesn't allow concurrent operations (and every call hits the same Discord
		// rate-limit bucket anyway). One player's Discord failure doesn't stop the rest; the
		// rollover above is already committed, so failures are reported after everyone was tried.
		var failedUids = new List<string>();
		foreach (var uid in uidsToResync)
		{
			try
			{
				await discordRoleAssignmentService.AssignRoleForPlayerAsync(uid, ct);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				logger.LogWarning(ex, "Season finalize: Discord role resync failed for player {Uid}", uid);
				failedUids.Add(uid);
			}
		}

		if (failedUids.Count > 0)
		{
			throw new SeasonValidationException(
				$"Season was finalized, but Discord role sync failed for {failedUids.Count} player(s): {string.Join(", ", failedUids)}.");
		}
	}

	public async Task ResetSeasonAsync(string seasonName, CancellationToken ct = default)
	{
		var players = await playerRepository.ListAsync(ct: ct);
		if (players.Count == 0)
		{
			throw new SeasonValidationException("No players found.");
		}

		var snapshots = players.Select(p => new ArchivedPlayerSnapshot
		{
			Id = Guid.NewGuid(),
			PlayerId = p.Id,
			Name = p.Name,
			Elo = p.Elo,
			IsMidConfirmed = p.IsMidConfirmed,
			IsHighConfirmed = p.IsHighConfirmed,
		}).ToList();

		// Archive and reset commit together — a reset without its archive would lose the season's
		// standings for good.
		await unitOfWork.InTransactionAsync(async () =>
		{
			// Re-running with a name that already exists overwrites the snapshot. Deleting and
			// recreating (rather than clearing/refilling the tracked Players collection) keeps this
			// simple and avoids EF change-tracking edge cases around severed required relationships.
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
		}, ct);
	}

	public async Task<List<ArchiveDto>> ListArchivesAsync(CancellationToken ct = default)
	{
		var archives = await archiveRepository.ListAsync(ct);
		var playerIds = archives.SelectMany(a => a.Players)
			.Where(p => p.PlayerId is not null).Select(p => p.PlayerId!.Value).Distinct();
		var uidsById = await playerRepository.GetUidsByIdsAsync(playerIds, ct);

		return archives.Select(a => ToDto(a, uidsById)).ToList();
	}

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

	private static ArchiveDto ToDto(Archive a, Dictionary<Guid, string> uidsById) => new(
		a.Id, a.SeasonName, a.CreatedAt,
		a.Players.Select(p => new ArchivedPlayerSnapshotDto(
			p.PlayerId is Guid pid ? uidsById.GetValueOrDefault(pid) : null,
			p.Name, p.Elo, p.IsMidConfirmed, p.IsHighConfirmed)).ToList());
}
