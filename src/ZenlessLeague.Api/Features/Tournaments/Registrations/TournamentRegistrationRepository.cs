using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Data;

namespace ZenlessLeague.Api.Features.Tournaments;

public interface ITournamentRegistrationRepository
{
	Task<TournamentRegistration?> FindExistingAsync(Guid tournamentId, Guid? playerId, Guid? teamId, CancellationToken ct = default);

	// Finds the team registration (if any) whose team this player belongs to, as creator or
	// second member — unlike FindExistingAsync, this doesn't require knowing the TeamId upfront,
	// so it's how a team registration gets found from just a player id (self-cancel by either
	// member, "is this player already on a different registered team" checks).
	Task<TournamentRegistration?> FindByPlayerTeamMembershipAsync(Guid tournamentId, Guid playerId, CancellationToken ct = default);

	// Most recently updated solo registration for this player, across ALL tournaments — used to
	// prefill a new registration's roster/Hoyolab screenshots so returning players don't need to
	// re-upload them every time (see TournamentRegistrationService.BuildDetailsAsync).
	Task<TournamentRegistration?> GetMostRecentSoloByPlayerAsync(Guid playerId, CancellationToken ct = default);
	Task<TournamentRegistration> CreateAsync(TournamentRegistration registration, CancellationToken ct = default);
	Task SaveChangesAsync(CancellationToken ct = default);
	Task<TournamentRegistration?> GetByIdAsync(Guid tournamentId, Guid id, CancellationToken ct = default);
	Task<List<TournamentRegistration>> ListByTournamentAsync(Guid tournamentId, CancellationToken ct = default);
	Task DeleteAsync(TournamentRegistration registration, CancellationToken ct = default);
}

public class TournamentRegistrationRepository(AppDbContext context) : ITournamentRegistrationRepository
{
	public Task<TournamentRegistration?> FindExistingAsync(Guid tournamentId, Guid? playerId, Guid? teamId, CancellationToken ct = default) =>
		context.TournamentRegistrations.FirstOrDefaultAsync(r =>
			r.TournamentId == tournamentId &&
			(playerId != null ? r.PlayerId == playerId : r.TeamId == teamId), ct);

	public Task<TournamentRegistration?> FindByPlayerTeamMembershipAsync(Guid tournamentId, Guid playerId, CancellationToken ct = default) =>
		(from r in context.TournamentRegistrations
		 join t in context.Teams on r.TeamId equals t.Id
		 where r.TournamentId == tournamentId && (t.Player1Id == playerId || t.Player2Id == playerId)
		 select r).FirstOrDefaultAsync(ct);

	public async Task<TournamentRegistration> CreateAsync(TournamentRegistration registration, CancellationToken ct = default)
	{
		context.TournamentRegistrations.Add(registration);
		await context.SaveChangesAsync(ct);
		return registration;
	}

	public Task<TournamentRegistration?> GetMostRecentSoloByPlayerAsync(Guid playerId, CancellationToken ct = default) =>
		context.TournamentRegistrations
			// Admin-added stubs (StubDetails) carry no screenshots or form fields — never a useful
			// "last registration" to prefill from or fall back to.
			.Where(r => r.PlayerId == playerId && r.Player1Details.RosterScreenshotUrl != null)
			.OrderByDescending(r => r.UpdatedAt)
			.FirstOrDefaultAsync(ct);

	public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);

	public Task<TournamentRegistration?> GetByIdAsync(Guid tournamentId, Guid id, CancellationToken ct = default) =>
		context.TournamentRegistrations.FirstOrDefaultAsync(r => r.TournamentId == tournamentId && r.Id == id, ct);

	public Task<List<TournamentRegistration>> ListByTournamentAsync(Guid tournamentId, CancellationToken ct = default) =>
		context.TournamentRegistrations
			.Where(r => r.TournamentId == tournamentId)
			.OrderBy(r => r.RegisteredAt)
			.ToListAsync(ct);

	public async Task DeleteAsync(TournamentRegistration registration, CancellationToken ct = default)
	{
		context.TournamentRegistrations.Remove(registration);
		await context.SaveChangesAsync(ct);
	}
}
