using Microsoft.AspNetCore.Authorization;
using ZenlessLeague.Api.Data.Authorization;
using ZenlessLeague.Api.Features.Teams;

namespace ZenlessLeague.Api.Features.Tournaments;

public static class RegistrationEndpoints
{
	public static void MapRegistrationEndpoints(this IEndpointRouteBuilder app)
	{
		var group = app.MapGroup("/api/tournaments/{id:guid}/registrations");

		group.MapPost("/solo", async (
			Guid id, SoloRegistrationRequest request, ITournamentRegistrationService service, CancellationToken ct) =>
		{
			var registration = await service.RegisterSoloAsync(id, request, ct);
			return registration is null ? Results.NotFound() : Results.Ok(registration);
		}).RequireAuthorization();

		// Only the team's two players (or an admin) may register it — mirrors the membership
		// check on PUT/DELETE /api/teams/{id}.
		group.MapPost("/team", async (
			Guid id, TeamRegistrationRequest request, HttpContext http, IAuthorizationService authorization,
			ITeamService teamService, ITournamentRegistrationService service, CancellationToken ct) =>
		{
			var team = await teamService.GetAsync(request.TeamId, ct);
			if (team is null)
			{
				return Results.BadRequest(new { error = $"Team '{request.TeamId}' does not exist." });
			}

			var uid = http.User.GetUid();
			if (uid != team.Creator.Uid && uid != team.Player2.Uid &&
				!(await authorization.AuthorizeAsync(http.User, "Admin")).Succeeded)
			{
				return Results.Forbid();
			}

			var registration = await service.RegisterTeamAsync(id, request, ct);
			return registration is null ? Results.NotFound() : Results.Ok(registration);
		}).RequireAuthorization();

		group.MapGet("/", async (Guid id, ITournamentRegistrationService service, CancellationToken ct) =>
			Results.Ok(await service.ListAsync(id, ct)));

		group.MapGet("/{registrationId:guid}", async (
			Guid id, Guid registrationId, ITournamentRegistrationService service, CancellationToken ct) =>
		{
			var registration = await service.GetAsync(id, registrationId, ct);
			return registration is null ? Results.NotFound() : Results.Ok(registration);
		});

		group.MapPost("/admin-add", async (
			Guid id, AdminAddRegistrationRequest request, ITournamentRegistrationService service, CancellationToken ct) =>
		{
			var registration = await service.AdminAddAsync(id, request.PlayerId, ct);
			return registration is null ? Results.NotFound() : Results.Ok(registration);
		}).RequireAuthorization("Admin");

		group.MapPut("/{registrationId:guid}/approve", async (
			Guid id, Guid registrationId, SetApprovedRequest request, ITournamentRegistrationService service, CancellationToken ct) =>
		{
			var registration = await service.SetApprovedAsync(id, registrationId, request.Approved, ct);
			return registration is null ? Results.NotFound() : Results.Ok(registration);
		}).RequireAuthorization("Admin");

		group.MapDelete("/", async (Guid id, HttpContext http, ITournamentRegistrationService service, CancellationToken ct) =>
		{
			var uid = http.User.GetUid();
			if (uid is null)
			{
				return Results.Unauthorized();
			}

			var result = await service.CancelAsync(id, uid, ct);
			return result switch
			{
				null => Results.NotFound(),
				false => Results.NotFound(),
				true => Results.NoContent(),
			};
		}).RequireAuthorization();
	}
}
