using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Infrastructure.Persistence;

namespace ZenlessLeague.Api.Features.Tournaments.Bracket;

// One entrant of a tournament's bracket, written when the bracket is created. PlayerId (solo) XOR
// TeamId (team), per Tournament.RegistrationType. Slot is the entrant's round-1 position (0-based;
// slots nobody holds are byes) — null on rows carried over from Challonge-era tournaments.
// FinalRank is set when the tournament finishes.
public class TournamentParticipant
{
	public Guid Id { get; set; }
	public Guid TournamentId { get; set; }
	public Guid? PlayerId { get; set; }
	public Guid? TeamId { get; set; }
	public int? Slot { get; set; }
	public int? FinalRank { get; set; }

	public Guid EntrantId => PlayerId ?? TeamId ?? throw new InvalidOperationException($"Participant {Id} has neither a player nor a team.");
}

public interface ITournamentParticipantRepository
{
	// Tracked — callers may change Slot/FinalRank in place and save.
	Task<List<TournamentParticipant>> ListByTournamentAsync(Guid tournamentId, CancellationToken ct = default);
	Task AddRangeAsync(IEnumerable<TournamentParticipant> participants, CancellationToken ct = default);
	Task SaveChangesAsync(CancellationToken ct = default);
}

public class TournamentParticipantRepository(AppDbContext context) : ITournamentParticipantRepository
{
	public Task<List<TournamentParticipant>> ListByTournamentAsync(Guid tournamentId, CancellationToken ct = default) =>
		context.TournamentParticipants.Where(p => p.TournamentId == tournamentId).OrderBy(p => p.Slot).ToListAsync(ct);

	public async Task AddRangeAsync(IEnumerable<TournamentParticipant> participants, CancellationToken ct = default)
	{
		context.TournamentParticipants.AddRange(participants);
		await context.SaveChangesAsync(ct);
	}

	public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
}
