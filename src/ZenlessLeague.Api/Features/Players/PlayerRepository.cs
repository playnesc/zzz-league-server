using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Data;

namespace ZenlessLeague.Api.Features.Players;

public class PlayerRepository(AppDbContext context) : IPlayerRepository
{
	public async Task<PlayerSummary?> GetByUidAsync(string uid, CancellationToken ct = default)
	{
		var player = await context.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Uid == uid, ct);
		return player is null ? null : ToSummary(player);
	}

	public async Task<PlayerSummary?> GetByIdAsync(Guid id, CancellationToken ct = default)
	{
		var player = await context.Players.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
		return player is null ? null : ToSummary(player);
	}

	public Task<Player?> GetEntityByUidAsync(string uid, CancellationToken ct = default) =>
		context.Players.FirstOrDefaultAsync(p => p.Uid == uid, ct);

	public Task<Player?> GetEntityByIdAsync(Guid id, CancellationToken ct = default) =>
		context.Players.FirstOrDefaultAsync(p => p.Id == id, ct);

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

	public async Task<bool> DeleteByUidAsync(string uid, CancellationToken ct = default)
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

	public async Task<Dictionary<Guid, string>> GetUidsByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
	{
		var idList = ids.Distinct().ToList();
		if (idList.Count == 0)
		{
			return [];
		}

		return await context.Players.AsNoTracking()
			.Where(p => idList.Contains(p.Id))
			.ToDictionaryAsync(p => p.Id, p => p.Uid, ct);
	}

	public async Task<Dictionary<string, Guid>> GetIdsByUidsAsync(IEnumerable<string> uids, CancellationToken ct = default)
	{
		var uidList = uids.Distinct().ToList();
		if (uidList.Count == 0)
		{
			return [];
		}

		return await context.Players.AsNoTracking()
			.Where(p => uidList.Contains(p.Uid))
			.ToDictionaryAsync(p => p.Uid, p => p.Id, ct);
	}

	public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);

	private static PlayerSummary ToSummary(Player p) => new(p.Uid, p.Name, p.Avatar, p.IsMidConfirmed, p.IsHighConfirmed);
}
