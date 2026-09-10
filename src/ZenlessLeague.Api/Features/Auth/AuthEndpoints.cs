using ZenlessLeague.Api.Data.Authorization;

namespace ZenlessLeague.Api.Features.Auth;

public record LoginWithDiscordRequest(string Code, string RedirectUri);
public record LoginWithDiscordResponse(string Token);
public record LinkDiscordRequest(string Code, string RedirectUri);

public static class AuthEndpoints
{
	public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
	{
		var auth = app.MapGroup("/api/auth");

		// Anonymous by design — this IS the login mechanism, there's no token to require yet.
		auth.MapPost("/discord/login", async (LoginWithDiscordRequest request, IAuthService service, CancellationToken ct) =>
		{
			var token = await service.LoginWithDiscordAsync(request.Code, request.RedirectUri, ct);
			return Results.Ok(new LoginWithDiscordResponse(token));
		});

		var players = app.MapGroup("/api/players/me/discord");

		players.MapPost("/link", async (LinkDiscordRequest request, HttpContext http, IAuthService service, CancellationToken ct) =>
			await service.LinkDiscordAsync(http.User.GetUid()!, request.Code, request.RedirectUri, ct)
				? Results.NoContent()
				: Results.NotFound())
			.RequireAuthorization();

		players.MapPost("/unlink", async (HttpContext http, IAuthService service, CancellationToken ct) =>
			await service.UnlinkDiscordAsync(http.User.GetUid()!, ct)
				? Results.NoContent()
				: Results.NotFound())
			.RequireAuthorization();
	}
}
