using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Data;

namespace ZenlessLeague.Api.Features.Tournaments;

public interface ITournamentRepository
{
    Task<Tournament> CreateAsync(Tournament tournament, CancellationToken ct = default);

    // Returns a tracked entity — callers may mutate it in place and call SaveChangesAsync.
    Task<Tournament?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Tournament>> ListAsync(CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public class TournamentRepository(AppDbContext context) : ITournamentRepository
{
    public async Task<Tournament> CreateAsync(Tournament tournament, CancellationToken ct = default)
    {
        context.Tournaments.Add(tournament);
        await context.SaveChangesAsync(ct);
        return tournament;
    }

    public Task<Tournament?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        context.Tournaments.FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<List<Tournament>> ListAsync(CancellationToken ct = default) =>
        context.Tournaments.OrderBy(t => t.TournamentStartDate).ToListAsync(ct);

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var tournament = await context.Tournaments.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tournament is null)
        {
            return false;
        }

        context.Tournaments.Remove(tournament);
        await context.SaveChangesAsync(ct);
        return true;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
}
