using Microsoft.AspNetCore.SignalR;

namespace ZenlessLeague.Api.Features.Tournaments.Realtime;

// Push-only — no client-invokable business methods, just group membership. Replaces the legacy
// frontend's raw RTDB `onValue` listeners: clients join a tournament's group to get live updates
// for that page, and re-fetch the affected resource via the existing REST endpoints on notification
// (thin "X changed" signals, not full object payloads — see ITournamentNotifier).
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
        // Every client gets list-level updates for free — mirrors the legacy root-layout's
        // always-on `tournaments` listener (used by the tournament list page).
        await Groups.AddToGroupAsync(Context.ConnectionId, ListGroupName);
        await base.OnConnectedAsync();
    }
}
