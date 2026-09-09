using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Data;

namespace ZenlessLeague.Api.Features.Players;

public class PlayerRepository(AppDbContext context) : IPlayerRepository
{
    public async Task<PlayerSummary?> GetByIdAsync(string uid, CancellationToken ct = default)
    {
        var player = await context.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Uid == uid, ct);
        return player is null ? null : ToSummary(player);
    }

    public Task<Player?> GetEntityByIdAsync(string uid, CancellationToken ct = default) =>
        context.Players.FirstOrDefaultAsync(p => p.Uid == uid, ct);

    public Task<Player?> GetByNameAsync(string name, CancellationToken ct = default) =>
        context.Players.FirstOrDefaultAsync(p => p.Name == name, ct);

    public Task<List<Player>> ListAsync(CancellationToken ct = default) =>
        context.Players.OrderBy(p => p.Name).ToListAsync(ct);

    public async Task<Player> CreateAsync(Player player, CancellationToken ct = default)
    {
        context.Players.Add(player);
        await context.SaveChangesAsync(ct);
        return player;
    }

    public async Task<bool> DeleteAsync(string uid, CancellationToken ct = default)
    {
        var player = await context.Players.FirstOrDefaultAsync(p => p.Uid == uid, ct);
        if (player is null)
        {
            return false;
        }

        context.Players.Remove(player);
        await context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<Player> ReplaceAsync(Player oldEntity, Player newEntity, CancellationToken ct = default)
    {
        context.Players.Remove(oldEntity);
        context.Players.Add(newEntity);
        await context.SaveChangesAsync(ct);
        return newEntity;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);

    private static PlayerSummary ToSummary(Player p) => new(p.Uid, p.Name, p.Avatar, p.IsMidConfirmed, p.IsHighConfirmed);
}
