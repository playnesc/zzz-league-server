namespace ZenlessLeague.Api.Features.Players;

public interface IPlayerRepository
{
    Task<PlayerSummary?> GetByIdAsync(string uid, CancellationToken ct = default);

    // Returns a tracked entity — callers may mutate it in place and call SaveChangesAsync.
    Task<Player?> GetEntityByIdAsync(string uid, CancellationToken ct = default);
    Task<Player?> GetByNameAsync(string name, CancellationToken ct = default);
    Task<List<Player>> ListAsync(CancellationToken ct = default);
    Task<Player> CreateAsync(Player player, CancellationToken ct = default);
    Task<bool> DeleteAsync(string uid, CancellationToken ct = default);

    // Removes `oldEntity` and adds `newEntity` in a single SaveChangesAsync — needed because
    // Player.Uid is the primary key, so "renaming" a uid means replacing the row, and the two
    // halves must commit atomically (see PlayerService.ClaimAsync).
    Task<Player> ReplaceAsync(Player oldEntity, Player newEntity, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

public record PlayerSummary(string Uid, string Name, string? Avatar, bool IsMidConfirmed, bool IsHighConfirmed);
