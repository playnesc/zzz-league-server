using Microsoft.AspNetCore.SignalR;

namespace ZenlessLeague.Api.Features.Tournaments.Realtime;

public class TournamentHub : Hub
{
	public const string ListGroupName = "tournaments:list";

	public Task JoinTournament(Guid tournamentId) =>
		Groups.AddToGroupAsync(Context.ConnectionId, GroupName(tournamentId));

	public Task LeaveTournament(Guid tournamentId) =>
		Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(tournamentId));

	public static string GroupName(Guid tournamentId) => $"tournament:{tournamentId}";

	public override async Task OnConnectedAsync()
	{
		await Groups.AddToGroupAsync(Context.ConnectionId, ListGroupName);
		await base.OnConnectedAsync();
	}
}
