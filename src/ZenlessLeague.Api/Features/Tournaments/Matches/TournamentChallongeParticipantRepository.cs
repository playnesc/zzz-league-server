using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Infrastructure.Persistence;

namespace ZenlessLeague.Api.Features.Tournaments.Matches;

public interface ITournamentChallongeParticipantRepository
{
	Task<List<TournamentChallongeParticipant>> ListByTournamentAsync(Guid tournamentId, CancellationToken ct = default);

	// Targeted single-row lookup for "resolve this one player/team's Challonge participant id" —
	// exactly one of playerId/teamId should be non-null. Prefer this over ListByTournamentAsync
	// when only one or two participants are actually needed (e.g. reporting a single match result).
	Task<TournamentChallongeParticipant?> FindAsync(Guid tournamentId, Guid? playerId, Guid? teamId, CancellationToken ct = default);

	Task AddRangeAsync(IEnumerable<TournamentChallongeParticipant> participants, CancellationToken ct = default);
}

public class TournamentChallongeParticipantRepository(AppDbContext context) : ITournamentChallongeParticipantRepository
{
	public Task<List<TournamentChallongeParticipant>> ListByTournamentAsync(Guid tournamentId, CancellationToken ct = default) =>
		context.TournamentChallongeParticipants.Where(p => p.TournamentId == tournamentId).ToListAsync(ct);

	public Task<TournamentChallongeParticipant?> FindAsync(Guid tournamentId, Guid? playerId, Guid? teamId, CancellationToken ct = default) =>
		context.TournamentChallongeParticipants.FirstOrDefaultAsync(p =>
			p.TournamentId == tournamentId &&
			(playerId != null ? p.PlayerId == playerId : p.TeamId == teamId), ct);

	public async Task AddRangeAsync(IEnumerable<TournamentChallongeParticipant> participants, CancellationToken ct = default)
	{
		context.TournamentChallongeParticipants.AddRange(participants);
		await context.SaveChangesAsync(ct);
	}
}
