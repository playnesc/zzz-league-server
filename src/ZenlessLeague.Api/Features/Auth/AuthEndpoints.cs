using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Infrastructure.Authorization;
using ZenlessLeague.Api.Infrastructure.Http;

namespace ZenlessLeague.Api.Features.Auth;

public record LoginWithDiscordRequest(string Code, string RedirectUri);
public record LoginWithDiscordResponse(string Token);
public record LinkDiscordRequest(string Code, string RedirectUri);
public record CreateSessionResponse(string Token, DateTimeOffset ExpiresAt, PlayerDto Player);

public static class AuthEndpoints
{
	public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
	{
		var auth = app.MapGroup("/api/auth");

		auth.MapPost("/discord/login", async (LoginWithDiscordRequest request, IAuthService service, CancellationToken ct) =>
		{
			var token = await service.LoginWithDiscordAsync(request.Code, request.RedirectUri, ct);
			return Results.Ok(new LoginWithDiscordResponse(token));
		}).RequireRateLimiting(RateLimits.DiscordLogin);

		// Trades a Firebase ID token for a session token (see SessionTokenIssuer). Firebase scheme
		// only, so a session token can't be used to keep minting fresh ones — renewal always needs
		// the browser's live Firebase session.
		var firebaseOnly = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
			.RequireAuthenticatedUser()
			.Build();
		auth.MapPost("/session", async (HttpContext http, IPlayerService playerService, ISessionTokenIssuer issuer, CancellationToken ct) =>
		{
			var uid = http.User.GetUid()!;
			var player = await playerService.GetAsync(uid, ct);
			if (player is null)
			{
				return Results.NotFound();
			}

			var session = issuer.Issue(uid);
			return Results.Ok(new CreateSessionResponse(session.Token, session.ExpiresAt, player));
		}).RequireAuthorization(firebaseOnly).RequireRateLimiting(RateLimits.UserWrites);

		var players = app.MapGroup("/api/players/me/discord");

		players.MapPost("/link", async (LinkDiscordRequest request, HttpContext http, IAuthService service, CancellationToken ct) =>
		{
			var player = await service.LinkDiscordAsync(http.User.GetUid()!, request.Code, request.RedirectUri, ct);
			return player is null ? Results.NotFound() : Results.Ok(player);
		}).RequireAuthorization().RequireRateLimiting(RateLimits.UserWrites);

		players.MapPost("/unlink", async (HttpContext http, IAuthService service, CancellationToken ct) =>
		{
			var player = await service.UnlinkDiscordAsync(http.User.GetUid()!, ct);
			return player is null ? Results.NotFound() : Results.Ok(player);
		}).RequireAuthorization().RequireRateLimiting(RateLimits.UserWrites);
	}
}
