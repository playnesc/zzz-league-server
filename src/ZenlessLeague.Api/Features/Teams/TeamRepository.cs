using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Data;

namespace ZenlessLeague.Api.Features.Teams;

public interface ITeamRepository
{
	Task<Team> CreateAsync(Team team, CancellationToken ct = default);

	// Returns a tracked entity — callers may mutate it in place and call SaveChangesAsync.
	Task<Team?> GetByIdAsync(Guid id, CancellationToken ct = default);

	// Every team a player belongs to (as creator or second player) — no pagination, a player's
	// team count is expected to stay small. Used for the "My teams" popup.
	Task<List<Team>> ListByPlayerAsync(Guid playerId, CancellationToken ct = default);

	// Keyset pagination (CreatedAt desc, Id desc tiebreak) for the all-teams page — same pattern
	// as HistoryRepository.ListPageAsync.
	Task<List<Team>> ListPageAsync(int take, DateTimeOffset? beforeCreatedAt, Guid? beforeId, CancellationToken ct = default);

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

	public Task<List<Team>> ListByPlayerAsync(Guid playerId, CancellationToken ct = default) =>
		context.Teams
			.Where(t => t.Player1Id == playerId || t.Player2Id == playerId)
			.OrderByDescending(t => t.CreatedAt)
			.ToListAsync(ct);

	public Task<List<Team>> ListPageAsync(int take, DateTimeOffset? beforeCreatedAt, Guid? beforeId, CancellationToken ct = default)
	{
		var query = context.Teams.AsQueryable();
		if (beforeCreatedAt is not null && beforeId is not null)
		{
			query = query.Where(t =>
				t.CreatedAt < beforeCreatedAt ||
				(t.CreatedAt == beforeCreatedAt && t.Id.CompareTo(beforeId.Value) < 0));
		}

		return query
			.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
			.Take(take)
			.ToListAsync(ct);
	}

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
