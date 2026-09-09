using Microsoft.AspNetCore.SignalR;

namespace ZenlessLeague.Api.Features.Tournaments.Realtime;

// Thin "X changed" signals only — clients re-fetch via the existing REST DTOs on notification.
// One serialization contract (the DTOs) instead of two competing ones (DTOs + hub payloads).
public interface ITournamentNotifier
{
    Task TournamentChangedAsync(Guid tournamentId, CancellationToken ct = default);
    Task RegistrationChangedAsync(Guid tournamentId, Guid registrationId, CancellationToken ct = default);
    Task MatchChangedAsync(Guid tournamentId, Guid matchId, CancellationToken ct = default);
    Task MatchesSyncedAsync(Guid tournamentId, CancellationToken ct = default);
}

public class TournamentNotifier(IHubContext<TournamentHub> hubContext) : ITournamentNotifier
{
    public Task TournamentChangedAsync(Guid tournamentId, CancellationToken ct = default) =>
        Task.WhenAll(
            hubContext.Clients.Group(TournamentHub.GroupName(tournamentId)).SendAsync("TournamentChanged", tournamentId, ct),
            hubContext.Clients.Group(TournamentHub.ListGroupName).SendAsync("TournamentChanged", tournamentId, ct));

    public Task RegistrationChangedAsync(Guid tournamentId, Guid registrationId, CancellationToken ct = default) =>
        hubContext.Clients.Group(TournamentHub.GroupName(tournamentId)).SendAsync("RegistrationChanged", tournamentId, registrationId, ct);

    public Task MatchChangedAsync(Guid tournamentId, Guid matchId, CancellationToken ct = default) =>
        hubContext.Clients.Group(TournamentHub.GroupName(tournamentId)).SendAsync("MatchChanged", tournamentId, matchId, ct);

    public Task MatchesSyncedAsync(Guid tournamentId, CancellationToken ct = default) =>
        hubContext.Clients.Group(TournamentHub.GroupName(tournamentId)).SendAsync("MatchesSynced", tournamentId, ct);
}
