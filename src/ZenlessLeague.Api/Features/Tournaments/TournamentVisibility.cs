using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace ZenlessLeague.Api.Features.Tournaments;

// Hidden tournaments (Visible == false) are only served to moderators and admins. The read
// endpoints stay anonymous, so this checks the "Moderator" policy against whatever token the
// caller happened to send (the frontend attaches one whenever a user is signed in).
public static class TournamentVisibility
{
	public static async Task<bool> CanSeeHiddenAsync(ClaimsPrincipal user, IAuthorizationService authorization) =>
		(await authorization.AuthorizeAsync(user, "Moderator")).Succeeded;

	// Until the tournament starts its bracket is still being arranged (see BracketService), so the
	// bracket and its matches are only shown to admins; everyone else sees them from Start on.
	public static async Task<bool> CanSeeBracketAsync(
		Guid tournamentId, ClaimsPrincipal user, ITournamentRepository tournamentRepository, IAuthorizationService authorization, CancellationToken ct)
	{
		var tournament = await tournamentRepository.GetByIdAsync(tournamentId, ct);
		return tournament is not null &&
			(TournamentStateMachine.HasStarted(tournament.State) || (await authorization.AuthorizeAsync(user, "Admin")).Succeeded);
	}

	// For routes under /api/tournaments/{id:guid}: 404s a hidden tournament for callers below
	// moderator, the same response as a tournament that doesn't exist.
	public static RouteHandlerBuilder RequireVisibleTournament(this RouteHandlerBuilder builder) =>
		builder.AddEndpointFilter<HiddenTournamentFilter>();
}

public class HiddenTournamentFilter(ITournamentRepository tournamentRepository, IAuthorizationService authorization) : IEndpointFilter
{
	public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
	{
		var http = context.HttpContext;
		if (Guid.TryParse(http.GetRouteValue("id")?.ToString(), out var tournamentId) &&
			await tournamentRepository.GetVisibilityAsync(tournamentId, http.RequestAborted) == false &&
			!await TournamentVisibility.CanSeeHiddenAsync(http.User, authorization))
		{
			return Results.NotFound();
		}

		return await next(context);
	}
}
