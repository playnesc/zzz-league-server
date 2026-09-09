using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Data;

namespace ZenlessLeague.Api.Features.Seasons;

public interface IArchiveRepository
{
    // Returns a tracked entity (with Players loaded) — callers may mutate it in place and call SaveChangesAsync.
    Task<Archive?> GetBySeasonNameAsync(string seasonName, CancellationToken ct = default);
    Task<List<Archive>> ListAsync(CancellationToken ct = default);
    Task<Archive> CreateAsync(Archive archive, CancellationToken ct = default);
    Task DeleteAsync(Archive archive, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public class ArchiveRepository(AppDbContext context) : IArchiveRepository
{
    public Task<Archive?> GetBySeasonNameAsync(string seasonName, CancellationToken ct = default) =>
        context.Archives.Include(a => a.Players).FirstOrDefaultAsync(a => a.SeasonName == seasonName, ct);

    public Task<List<Archive>> ListAsync(CancellationToken ct = default) =>
        context.Archives.Include(a => a.Players).OrderByDescending(a => a.CreatedAt).ToListAsync(ct);

    public async Task<Archive> CreateAsync(Archive archive, CancellationToken ct = default)
    {
        context.Archives.Add(archive);
        await context.SaveChangesAsync(ct);
        return archive;
    }

    public async Task DeleteAsync(Archive archive, CancellationToken ct = default)
    {
        context.Archives.Remove(archive);
        await context.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
}
