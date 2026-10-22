using Microsoft.AspNetCore.Authorization;
using ZenlessLeague.Api.Infrastructure.Authorization;

namespace ZenlessLeague.Api.Features.Players;

public static class PlayerEndpoints
{
	public static void MapPlayerEndpoints(this IEndpointRouteBuilder app)
	{
		var group = app.MapGroup("/api/players");

		group.MapPost("/", async (CreatePlayerRequest request, IPlayerService service, CancellationToken ct) =>
		{
			var player = await service.CreateAsync(request, ct);
			return Results.Created($"/api/players/{player.Uid}", player);
		}).RequireAuthorization("Admin");

		// ?uids=a,b,c batch-resolves a known set of players (e.g. a tournament's registrants);
		// omitted, returns the whole roster. Always the lean PlayerListItemDto shape.
		group.MapGet("/", async (string? uids, IPlayerService service, CancellationToken ct) =>
			Results.Ok(await service.ListAsync(uids?.Split(',', StringSplitOptions.RemoveEmptyEntries), ct)));

		// Admin page's Roles section. Literal segment, so it wins over /{uid} below.
		group.MapGet("/staff", async (IPlayerService service, CancellationToken ct) =>
			Results.Ok(await service.ListStaffAsync(ct)))
			.RequireAuthorization("Admin");

		// The caller's own full player — used by the SvelteKit server to resolve its session cookie
		// (see SessionTokenIssuer). Literal segment, so it wins over /{uid} below.
		group.MapGet("/me", async (HttpContext http, IPlayerService service, CancellationToken ct) =>
		{
			var player = await service.GetAsync(http.User.GetUid()!, ct);
			return player is null ? Results.NotFound() : Results.Ok(player);
		}).RequireAuthorization();

		// The caller's own most recent solo registration details (PrizeUid, screenshot URLs), for
		// the register page's prefill button. 204 when there's nothing to prefill from.
		group.MapGet("/me/last-registration", async (HttpContext http, IPlayerService service, CancellationToken ct) =>
		{
			var lastRegistration = await service.GetLastRegistrationAsync(http.User.GetUid()!, ct);
			return lastRegistration is null ? Results.NoContent() : Results.Ok(lastRegistration);
		}).RequireAuthorization();

		// Self-or-admin: the full PlayerDto includes DiscordId — not something to expose for
		// arbitrary other players (lists use the lean PlayerListItemDto instead).
		group.MapGet("/{uid}", async (string uid, HttpContext http, IAuthorizationService authorization, IPlayerService service, CancellationToken ct) =>
		{
			if (http.User.GetUid() != uid && !(await authorization.AuthorizeAsync(http.User, "Admin")).Succeeded)
			{
				return Results.Forbid();
			}

			var player = await service.GetAsync(uid, ct);
			return player is null ? Results.NotFound() : Results.Ok(player);
		}).RequireAuthorization();

		group.MapDelete("/{uid}", async (string uid, IPlayerService service, CancellationToken ct) =>
			await service.DeleteAsync(uid, ct) ? Results.NoContent() : Results.NotFound())
			.RequireAuthorization("Admin");

		group.MapPut("/{uid}/role", async (string uid, UpdatePlayerRoleRequest request, IPlayerService service, CancellationToken ct) =>
		{
			var player = await service.UpdateRoleAsync(uid, request, ct);
			return player is null ? Results.NotFound() : Results.Ok(player);
		}).RequireAuthorization("Admin");

		group.MapPut("/{uid}/name", async (string uid, UpdatePlayerNameRequest request, HttpContext http, IPlayerService service, CancellationToken ct) =>
		{
			if (http.User.GetUid() != uid)
			{
				return Results.Forbid();
			}

			var player = await service.UpdateNameAsync(uid, request.Name, ct);
			return player is null ? Results.NotFound() : Results.Ok(player);
		}).RequireAuthorization();

		group.MapPut("/{uid}/avatar", async (string uid, UpdatePlayerAvatarRequest request, HttpContext http, IPlayerService service, CancellationToken ct) =>
		{
			if (http.User.GetUid() != uid)
			{
				return Results.Forbid();
			}

			var player = await service.UpdateAvatarAsync(uid, request.Avatar, ct);
			return player is null ? Results.NotFound() : Results.Ok(player);
		}).RequireAuthorization();
	}
}
