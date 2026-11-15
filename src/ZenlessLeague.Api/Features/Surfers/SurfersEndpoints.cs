using ZenlessLeague.Api.Features.Auth;
using ZenlessLeague.Api.Infrastructure.Authorization;
using ZenlessLeague.Api.Infrastructure.Http;

namespace ZenlessLeague.Api.Features.Surfers;

// Leaderboard for the Anime Surfers game at surfers.playnesc.com. Reads are public; the game signs
// in with a game token (POST /api/auth/game-token, see GameTokenIssuer), never a site session.
public static class SurfersEndpoints
{
	public static void MapSurfersEndpoints(this IEndpointRouteBuilder app)
	{
		var surfers = app.MapGroup("/api/surfers");

		surfers.MapGet("/leaderboard", async (ISurfersService service, CancellationToken ct, int limit = SurfersService.DefaultLimit) =>
			Results.Ok(await service.GetLeaderboardAsync(limit, ct)));

		surfers.MapGet("/me", async (HttpContext http, ISurfersService service, CancellationToken ct) =>
			await service.GetMineAsync(http.User.GetUid()!, ct) is { } mine ? Results.Ok(mine) : Results.NotFound())
			.RequireAuthorization(GameTokenIssuer.Policy);

		surfers.MapPost("/scores", async (SubmitSurfersScoreRequest request, HttpContext http, ISurfersService service, CancellationToken ct) =>
			await service.SubmitAsync(http.User.GetUid()!, request, ct) is { } result ? Results.Ok(result) : Results.NotFound())
			.RequireAuthorization(GameTokenIssuer.Policy).RequireRateLimiting(RateLimits.UserWrites);
	}
}
