using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Infrastructure.Persistence;

namespace ZenlessLeague.Api.Features.Tournaments.Matches;

public interface ITournamentMatchRepository
{
	// Returns a tracked entity — callers may mutate it in place and call SaveChangesAsync.
	Task<TournamentMatch?> GetByIdAsync(Guid tournamentId, Guid id, CancellationToken ct = default);
	Task<List<TournamentMatch>> ListByTournamentAsync(Guid tournamentId, CancellationToken ct = default);
	Task<TournamentMatch> CreateAsync(TournamentMatch match, CancellationToken ct = default);
	Task AddRangeAsync(IEnumerable<TournamentMatch> matches, CancellationToken ct = default);
	Task RemoveRangeAsync(IEnumerable<TournamentMatch> matches, CancellationToken ct = default);
	Task SaveChangesAsync(CancellationToken ct = default);
}

public class TournamentMatchRepository(AppDbContext context) : ITournamentMatchRepository
{
	public Task<TournamentMatch?> GetByIdAsync(Guid tournamentId, Guid id, CancellationToken ct = default) =>
		context.TournamentMatches.FirstOrDefaultAsync(m => m.TournamentId == tournamentId && m.Id == id, ct);

	public Task<List<TournamentMatch>> ListByTournamentAsync(Guid tournamentId, CancellationToken ct = default) =>
		context.TournamentMatches.Where(m => m.TournamentId == tournamentId).OrderBy(m => m.Number).ThenBy(m => m.ChallongeMatchId).ToListAsync(ct);

	public async Task<TournamentMatch> CreateAsync(TournamentMatch match, CancellationToken ct = default)
	{
		context.TournamentMatches.Add(match);
		await context.SaveChangesAsync(ct);
		return match;
	}

	public async Task AddRangeAsync(IEnumerable<TournamentMatch> matches, CancellationToken ct = default)
	{
		context.TournamentMatches.AddRange(matches);
		await context.SaveChangesAsync(ct);
	}

	public async Task RemoveRangeAsync(IEnumerable<TournamentMatch> matches, CancellationToken ct = default)
	{
		context.TournamentMatches.RemoveRange(matches);
		await context.SaveChangesAsync(ct);
	}

	public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
}
