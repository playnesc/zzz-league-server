using ZenlessLeague.Api.Data.Authorization;

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

		group.MapGet("/", async (IPlayerService service, CancellationToken ct) =>
			Results.Ok(await service.ListAsync(ct)));

		group.MapGet("/{uid}", async (string uid, IPlayerService service, CancellationToken ct) =>
		{
			var player = await service.GetAsync(uid, ct);
			return player is null ? Results.NotFound() : Results.Ok(player);
		});

		group.MapDelete("/{uid}", async (string uid, IPlayerService service, CancellationToken ct) =>
			await service.DeleteAsync(uid, ct) ? Results.NoContent() : Results.NotFound())
			.RequireAuthorization("Admin");

		// Self-service: a signed-in user can only claim a reserved row onto their own uid.
		group.MapPost("/claim", async (ClaimPlayerRequest request, HttpContext http, IPlayerService service, CancellationToken ct) =>
		{
			if (http.User.GetUid() != request.NewUid)
			{
				return Results.Forbid();
			}

			return Results.Ok(await service.ClaimAsync(request, ct));
		}).RequireAuthorization();

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
