using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ZenlessLeague.Api.Features.Tournaments.Realtime;

public class TournamentHub(ITournamentRepository tournamentRepository, IAuthorizationService authorization) : Hub
{
	public const string ListGroupName = "tournaments:list";

	// Same rule as the HTTP routes (HiddenTournamentFilter): a hidden tournament's live updates are
	// for moderators and up, and to anyone else it looks exactly like a tournament that doesn't
	// exist — so an unknown id is refused too, rather than revealing which ids are hidden.
	public async Task JoinTournament(Guid tournamentId)
	{
		if (await tournamentRepository.GetVisibilityAsync(tournamentId, Context.ConnectionAborted) != true &&
			!await TournamentVisibility.CanSeeHiddenAsync(Context.User!, authorization))
		{
			throw new HubException("Tournament not found.");
		}

		await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(tournamentId));
	}

	public Task LeaveTournament(Guid tournamentId) =>
		Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(tournamentId));

	public static string GroupName(Guid tournamentId) => $"tournament:{tournamentId}";

	public override async Task OnConnectedAsync()
	{
		await Groups.AddToGroupAsync(Context.ConnectionId, ListGroupName);
		await base.OnConnectedAsync();
	}
}
