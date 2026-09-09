using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Data;

namespace ZenlessLeague.Api.Features.History;

public interface IHistoryRepository
{
    // Adds `entry` and saves — also persists any Player mutations already tracked on the
    // shared AppDbContext (see HistoryService), so a match-result registration and its
    // player point/win-loss updates commit as a single transaction.
    Task<HistoryEntry> CreateAsync(HistoryEntry entry, CancellationToken ct = default);

    Task<HistoryEntry?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<HistoryEntry>> ListAsync(CancellationToken ct = default);
    Task<List<HistoryEntry>> ListByPlayerAsync(string uid, CancellationToken ct = default);

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

    public Task<List<HistoryEntry>> ListByPlayerAsync(string uid, CancellationToken ct = default) =>
        context.HistoryEntries
            .Where(h => h.P1PlayerId == uid || h.P2PlayerId == uid)
            .OrderByDescending(h => h.Timestamp)
            .ToListAsync(ct);

    public async Task DeleteAsync(HistoryEntry entry, CancellationToken ct = default)
    {
        context.HistoryEntries.Remove(entry);
        await context.SaveChangesAsync(ct);
    }
}
