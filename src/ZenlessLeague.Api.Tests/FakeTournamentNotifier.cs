using ZenlessLeague.Api.Features.Tournaments.Realtime;

namespace ZenlessLeague.Api.Tests;

// Records calls instead of pushing through a real SignalR hub context (which needs a running
// server to construct meaningfully).
public class FakeTournamentNotifier : ITournamentNotifier
{
	public List<Guid> TournamentChanged { get; } = [];
	public List<(Guid TournamentId, Guid RegistrationId)> RegistrationChanged { get; } = [];
	public List<(Guid TournamentId, Guid MatchId)> MatchChanged { get; } = [];
	public List<Guid> MatchesSynced { get; } = [];

	public Task TournamentChangedAsync(Guid tournamentId)
	{
		TournamentChanged.Add(tournamentId);
		return Task.CompletedTask;
	}

	public Task RegistrationChangedAsync(Guid tournamentId, Guid registrationId)
	{
		RegistrationChanged.Add((tournamentId, registrationId));
		return Task.CompletedTask;
	}

	public Task MatchChangedAsync(Guid tournamentId, Guid matchId)
	{
		MatchChanged.Add((tournamentId, matchId));
		return Task.CompletedTask;
	}

	public Task MatchesSyncedAsync(Guid tournamentId)
	{
		MatchesSynced.Add(tournamentId);
		return Task.CompletedTask;
	}
}
