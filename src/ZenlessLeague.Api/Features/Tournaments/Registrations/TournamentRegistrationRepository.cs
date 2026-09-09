using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Data;

namespace ZenlessLeague.Api.Features.Tournaments;

public interface ITournamentRegistrationRepository
{
    Task<TournamentRegistration?> FindExistingAsync(Guid tournamentId, string? playerId, Guid? teamId, CancellationToken ct = default);
    Task<TournamentRegistration> CreateAsync(TournamentRegistration registration, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<TournamentRegistration?> GetByIdAsync(Guid tournamentId, Guid id, CancellationToken ct = default);
    Task<List<TournamentRegistration>> ListByTournamentAsync(Guid tournamentId, CancellationToken ct = default);
    Task DeleteAsync(TournamentRegistration registration, CancellationToken ct = default);
}

public class TournamentRegistrationRepository(AppDbContext context) : ITournamentRegistrationRepository
{
    public Task<TournamentRegistration?> FindExistingAsync(Guid tournamentId, string? playerId, Guid? teamId, CancellationToken ct = default) =>
        context.TournamentRegistrations.FirstOrDefaultAsync(r =>
            r.TournamentId == tournamentId &&
            (playerId != null ? r.PlayerId == playerId : r.TeamId == teamId), ct);

    public async Task<TournamentRegistration> CreateAsync(TournamentRegistration registration, CancellationToken ct = default)
    {
        context.TournamentRegistrations.Add(registration);
        await context.SaveChangesAsync(ct);
        return registration;
    }

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
