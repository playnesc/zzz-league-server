using Microsoft.AspNetCore.Authorization;
using ZenlessLeague.Api.Infrastructure.Authorization;
using ZenlessLeague.Api.Infrastructure.Http;

namespace ZenlessLeague.Api.Features.Teams;

public static class TeamEndpoints
{
	public static void MapTeamEndpoints(this IEndpointRouteBuilder app)
	{
		var group = app.MapGroup("/api/teams");

		// Self-service, direct-add: the caller must be one of the two players on the team they're
		// creating (no invite/accept step) — an admin may create a team on anyone's behalf.
		group.MapPost("/", async (CreateTeamRequest request, HttpContext http, IAuthorizationService authorization, ITeamService service, CancellationToken ct) =>
		{
			if (!await CallerIsAsync(http, authorization, request.CreatorPlayerId))
			{
				return Results.Forbid();
			}

			var team = await service.CreateAsync(request, ct);
			return Results.Created($"/api/teams/{team.Id}", team);
		}).RequireAuthorization().RequireRateLimiting(RateLimits.UserWrites);

		// Keyset-paginated (CreatedAt desc) — the all-teams list, same pattern as /api/history.
		group.MapGet("/", async (int? take, DateTimeOffset? beforeCreatedAt, Guid? beforeId, ITeamService service, CancellationToken ct) =>
			Results.Ok(await service.ListPageAsync(ClampPageSize(take), beforeCreatedAt, beforeId, ct)));

		// Unpaginated — every team `uid` belongs to. Backs the "My teams" popup.
		group.MapGet("/by-player/{uid}", async (string uid, ITeamService service, CancellationToken ct) =>
			Results.Ok(await service.ListByPlayerAsync(uid, ct)));

		group.MapGet("/{id:guid}", async (Guid id, ITeamService service, CancellationToken ct) =>
		{
			var team = await service.GetAsync(id, ct);
			return team is null ? Results.NotFound() : Results.Ok(team);
		});

		group.MapPut("/{id:guid}", async (Guid id, EditTeamRequest request, HttpContext http, IAuthorizationService authorization, ITeamService service, CancellationToken ct) =>
		{
			var existing = await service.GetAsync(id, ct);
			if (existing is null)
			{
				return Results.NotFound();
			}

			if (!await IsMemberOrAdminAsync(http, authorization, existing))
			{
				return Results.Forbid();
			}

			var team = await service.UpdateAsync(id, request, ct);
			return team is null ? Results.NotFound() : Results.Ok(team);
		}).RequireAuthorization().RequireRateLimiting(RateLimits.UserWrites);

		group.MapDelete("/{id:guid}", async (Guid id, HttpContext http, IAuthorizationService authorization, ITeamService service, CancellationToken ct) =>
		{
			var existing = await service.GetAsync(id, ct);
			if (existing is null)
			{
				return Results.NotFound();
			}

			if (!await IsMemberOrAdminAsync(http, authorization, existing))
			{
				return Results.Forbid();
			}

			return await service.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound();
		}).RequireAuthorization().RequireRateLimiting(RateLimits.UserWrites);
	}

	private static async Task<bool> CallerIsAsync(HttpContext http, IAuthorizationService authorization, string playerId) =>
		http.User.GetUid() == playerId || await IsAdminAsync(http, authorization);

	private static async Task<bool> IsMemberOrAdminAsync(HttpContext http, IAuthorizationService authorization, TeamDto team)
	{
		var uid = http.User.GetUid();
		return uid == team.Creator.Uid || uid == team.Player2.Uid || await IsAdminAsync(http, authorization);
	}

	private static async Task<bool> IsAdminAsync(HttpContext http, IAuthorizationService authorization) =>
		(await authorization.AuthorizeAsync(http.User, "Admin")).Succeeded;

	private const int DefaultPageSize = 20;
	private const int MaxPageSize = 100;

	private static int ClampPageSize(int? requested) =>
		Math.Clamp(requested ?? DefaultPageSize, 1, MaxPageSize);
}
