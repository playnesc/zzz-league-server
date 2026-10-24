using Microsoft.AspNetCore.SignalR;

namespace ZenlessLeague.Api.Features.Tournaments.Realtime;

// Deliberately takes no CancellationToken: notifications go out after the change they describe has
// been saved, and the caller's request token is cancelled when *they* disconnect — which mustn't
// stop everyone else watching from hearing about a change that already happened.
public interface ITournamentNotifier
{
	Task TournamentChangedAsync(Guid tournamentId);
	Task RegistrationChangedAsync(Guid tournamentId, Guid registrationId);
	Task MatchChangedAsync(Guid tournamentId, Guid matchId);
	Task MatchesSyncedAsync(Guid tournamentId);
}

public class TournamentNotifier(IHubContext<TournamentHub> hubContext) : ITournamentNotifier
{
	public Task TournamentChangedAsync(Guid tournamentId) =>
		Task.WhenAll(
			hubContext.Clients.Group(TournamentHub.GroupName(tournamentId)).SendAsync("TournamentChanged", tournamentId),
			hubContext.Clients.Group(TournamentHub.ListGroupName).SendAsync("TournamentChanged", tournamentId));

	public Task RegistrationChangedAsync(Guid tournamentId, Guid registrationId) =>
		hubContext.Clients.Group(TournamentHub.GroupName(tournamentId)).SendAsync("RegistrationChanged", tournamentId, registrationId);

	public Task MatchChangedAsync(Guid tournamentId, Guid matchId) =>
		hubContext.Clients.Group(TournamentHub.GroupName(tournamentId)).SendAsync("MatchChanged", tournamentId, matchId);

	public Task MatchesSyncedAsync(Guid tournamentId) =>
		hubContext.Clients.Group(TournamentHub.GroupName(tournamentId)).SendAsync("MatchesSynced", tournamentId);
}
