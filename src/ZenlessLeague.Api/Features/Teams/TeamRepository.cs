using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Data;

namespace ZenlessLeague.Api.Features.Teams;

public interface ITeamRepository
{
    Task<Team> CreateAsync(Team team, CancellationToken ct = default);

    // Returns a tracked entity — callers may mutate it in place and call SaveChangesAsync.
    Task<Team?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Team>> ListAsync(CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public class TeamRepository(AppDbContext context) : ITeamRepository
{
    public async Task<Team> CreateAsync(Team team, CancellationToken ct = default)
    {
        context.Teams.Add(team);
        await context.SaveChangesAsync(ct);
        return team;
    }

    public Task<Team?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        context.Teams.FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<List<Team>> ListAsync(CancellationToken ct = default) =>
        context.Teams.OrderBy(t => t.CreatedAt).ToListAsync(ct);

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var team = await context.Teams.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (team is null)
        {
            return false;
        }

        context.Teams.Remove(team);
        await context.SaveChangesAsync(ct);
        return true;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
}
