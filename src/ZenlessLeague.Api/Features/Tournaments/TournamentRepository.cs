using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Data;

namespace ZenlessLeague.Api.Features.Tournaments;

public interface ITournamentRepository
{
	Task<Tournament> CreateAsync(Tournament tournament, CancellationToken ct = default);

	// Returns a tracked entity — callers may mutate it in place and call SaveChangesAsync.
	Task<Tournament?> GetByIdAsync(Guid id, CancellationToken ct = default);
	Task<List<Tournament>> ListAsync(bool includeHidden, CancellationToken ct = default);
	Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

	// Lightweight Visible lookup for HiddenTournamentFilter; null when the tournament doesn't exist.
	Task<bool?> GetVisibilityAsync(Guid id, CancellationToken ct = default);

	// Batch Id -> Name resolution for list-returning callers (e.g. history entries showing which
	// tournament they belong to) — one query per list, not one per row.
	Task<Dictionary<Guid, string>> GetNamesByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);

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

	public Task<List<Tournament>> ListAsync(bool includeHidden, CancellationToken ct = default) =>
		context.Tournaments
			.Where(t => includeHidden || t.Visible)
			.OrderBy(t => t.TournamentStartDate)
			.ToListAsync(ct);

	public Task<bool?> GetVisibilityAsync(Guid id, CancellationToken ct = default) =>
		context.Tournaments.AsNoTracking()
			.Where(t => t.Id == id)
			.Select(t => (bool?)t.Visible)
			.FirstOrDefaultAsync(ct);

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

	public async Task<Dictionary<Guid, string>> GetNamesByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
	{
		var idList = ids.Distinct().ToList();
		if (idList.Count == 0)
		{
			return [];
		}

		return await context.Tournaments.AsNoTracking()
			.Where(t => idList.Contains(t.Id))
			.ToDictionaryAsync(t => t.Id, t => t.Name, ct);
	}

	public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
}
