using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Data;

namespace ZenlessLeague.Api.Features.Tournaments.Matches;

public interface ITournamentChallongeParticipantRepository
{
    Task<List<TournamentChallongeParticipant>> ListByTournamentAsync(Guid tournamentId, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<TournamentChallongeParticipant> participants, CancellationToken ct = default);
}

public class TournamentChallongeParticipantRepository(AppDbContext context) : ITournamentChallongeParticipantRepository
{
    public Task<List<TournamentChallongeParticipant>> ListByTournamentAsync(Guid tournamentId, CancellationToken ct = default) =>
        context.TournamentChallongeParticipants.Where(p => p.TournamentId == tournamentId).ToListAsync(ct);

    public async Task AddRangeAsync(IEnumerable<TournamentChallongeParticipant> participants, CancellationToken ct = default)
    {
        context.TournamentChallongeParticipants.AddRange(participants);
        await context.SaveChangesAsync(ct);
    }
}
