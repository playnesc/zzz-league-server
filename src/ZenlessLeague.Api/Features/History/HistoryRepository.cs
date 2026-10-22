using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Infrastructure.Persistence;

namespace ZenlessLeague.Api.Features.History;

public interface IHistoryRepository
{
	// Adds `entry` and saves — also persists any Player mutations already tracked on the
	// shared AppDbContext (see HistoryService), so a match-result registration and its
	// player point/win-loss updates commit as a single transaction.
	Task<HistoryEntry> CreateAsync(HistoryEntry entry, CancellationToken ct = default);

	Task<HistoryEntry?> GetByIdAsync(Guid id, CancellationToken ct = default);

	// Full, unpaginated list — for internal server-side consumers only (BackfillLastPlayedTimestampsAsync
	// needs to scan every entry). The HTTP endpoints use the keyset-paginated methods below instead.
	Task<List<HistoryEntry>> ListAsync(CancellationToken ct = default);

	// Keyset ("seek") pagination ordered by Timestamp desc, Id desc as a tiebreaker for entries
	// sharing the same timestamp (e.g. bulk season-finalize adjustments). Pass the last entry from
	// the previous page as the cursor to get the next `take` older entries; omit it for the first
	// page. Chosen over offset pagination (skip/take) because the underlying data can grow between
	// page requests without shifting already-fetched pages.
	Task<List<HistoryEntry>> ListPageAsync(int take, DateTimeOffset? beforeTimestamp, Guid? beforeId, CancellationToken ct = default);
	Task<List<HistoryEntry>> ListPageByPlayerAsync(Guid playerId, int take, DateTimeOffset? beforeTimestamp, Guid? beforeId, CancellationToken ct = default);

	// Removes `entry` and saves — same atomicity note as CreateAsync.
	Task DeleteAsync(HistoryEntry entry, CancellationToken ct = default);
}

public class HistoryRepository(AppDbContext context) : IHistoryRepository
{
	public async Task<HistoryEntry> CreateAsync(HistoryEntry entry, CancellationToken ct = default)
	{
		context.HistoryEntries.Add(entry);
		await context.SaveChangesAsync(ct);
		return entry;
	}

	public Task<HistoryEntry?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
		context.HistoryEntries.FirstOrDefaultAsync(h => h.Id == id, ct);

	public Task<List<HistoryEntry>> ListAsync(CancellationToken ct = default) =>
		context.HistoryEntries.OrderByDescending(h => h.Timestamp).ToListAsync(ct);

	public async Task DeleteAsync(HistoryEntry entry, CancellationToken ct = default)
	{
		context.HistoryEntries.Remove(entry);
		await context.SaveChangesAsync(ct);
	}

	public Task<List<HistoryEntry>> ListPageAsync(int take, DateTimeOffset? beforeTimestamp, Guid? beforeId, CancellationToken ct = default) =>
		ApplyCursor(context.HistoryEntries, beforeTimestamp, beforeId)
			.OrderByDescending(h => h.Timestamp).ThenByDescending(h => h.Id)
			.Take(take)
			.ToListAsync(ct);

	public Task<List<HistoryEntry>> ListPageByPlayerAsync(Guid playerId, int take, DateTimeOffset? beforeTimestamp, Guid? beforeId, CancellationToken ct = default) =>
		ApplyCursor(context.HistoryEntries.Where(h => h.P1PlayerId == playerId || h.P2PlayerId == playerId), beforeTimestamp, beforeId)
			.OrderByDescending(h => h.Timestamp).ThenByDescending(h => h.Id)
			.Take(take)
			.ToListAsync(ct);

	private static IQueryable<HistoryEntry> ApplyCursor(IQueryable<HistoryEntry> query, DateTimeOffset? beforeTimestamp, Guid? beforeId)
	{
		if (beforeTimestamp is null || beforeId is null)
		{
			return query;
		}

		return query.Where(h =>
			h.Timestamp < beforeTimestamp ||
			(h.Timestamp == beforeTimestamp && h.Id.CompareTo(beforeId.Value) < 0));
	}
}
