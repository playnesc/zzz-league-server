using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Infrastructure.Persistence;

namespace ZenlessLeague.Api.Features.Tournaments.Matches;

public interface ITournamentMatchRepository
{
	// Returns a tracked entity — callers may mutate it in place and call SaveChangesAsync.
	Task<TournamentMatch?> GetByIdAsync(Guid tournamentId, Guid id, CancellationToken ct = default);
	Task<TournamentMatch?> GetByChallongeMatchIdAsync(Guid tournamentId, long challongeMatchId, CancellationToken ct = default);
	Task<List<TournamentMatch>> ListByTournamentAsync(Guid tournamentId, CancellationToken ct = default);
	Task<TournamentMatch> CreateAsync(TournamentMatch match, CancellationToken ct = default);
	Task RemoveAsync(TournamentMatch match, CancellationToken ct = default);
	Task SaveChangesAsync(CancellationToken ct = default);
}

public class TournamentMatchRepository(AppDbContext context) : ITournamentMatchRepository
{
	public Task<TournamentMatch?> GetByIdAsync(Guid tournamentId, Guid id, CancellationToken ct = default) =>
		context.TournamentMatches.FirstOrDefaultAsync(m => m.TournamentId == tournamentId && m.Id == id, ct);

	public Task<TournamentMatch?> GetByChallongeMatchIdAsync(Guid tournamentId, long challongeMatchId, CancellationToken ct = default) =>
		context.TournamentMatches.FirstOrDefaultAsync(m => m.TournamentId == tournamentId && m.ChallongeMatchId == challongeMatchId, ct);

	public Task<List<TournamentMatch>> ListByTournamentAsync(Guid tournamentId, CancellationToken ct = default) =>
		context.TournamentMatches.Where(m => m.TournamentId == tournamentId).ToListAsync(ct);

	public async Task<TournamentMatch> CreateAsync(TournamentMatch match, CancellationToken ct = default)
	{
		context.TournamentMatches.Add(match);
		await context.SaveChangesAsync(ct);
		return match;
	}

	public async Task RemoveAsync(TournamentMatch match, CancellationToken ct = default)
	{
		context.TournamentMatches.Remove(match);
		await context.SaveChangesAsync(ct);
	}

	public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
}
