using Microsoft.AspNetCore.Authorization;
using ZenlessLeague.Api.Features.Teams;
using ZenlessLeague.Api.Infrastructure.Authorization;

namespace ZenlessLeague.Api.Features.Tournaments;

public static class RegistrationEndpoints
{
	public static void MapRegistrationEndpoints(this IEndpointRouteBuilder app)
	{
		var group = app.MapGroup("/api/tournaments/{id:guid}/registrations");

		group.MapPost("/solo", async (
			Guid id, SoloRegistrationRequest request, HttpContext http, IAuthorizationService authorization,
			ITournamentRegistrationService service, CancellationToken ct) =>
		{
			var uid = http.User.GetUid();
			if (uid != request.PlayerId && !(await authorization.AuthorizeAsync(http.User, "Admin")).Succeeded)
			{
				return Results.Forbid();
			}

			var registration = await service.RegisterSoloAsync(id, request, ct);
			return registration is null ? Results.NotFound() : Results.Ok(registration);
		}).RequireAuthorization().RequireVisibleTournament();

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
		}).RequireAuthorization().RequireVisibleTournament();

		// Public — anonymous visitors can browse participants too. callerUid (from the token if any
		// was sent; null for anonymous) is what ListAsync/GetAsync use to decide which
		// registrations' sensitive fields (GameUid/PrizeUid/PrizeAsMoney) come through unredacted.
		group.MapGet("/", async (Guid id, HttpContext http, ITournamentRegistrationService service, CancellationToken ct) =>
			Results.Ok(await service.ListAsync(id, http.User.GetUid(), ct)))
			.RequireVisibleTournament();

		group.MapGet("/{registrationId:guid}", async (
			Guid id, Guid registrationId, HttpContext http, ITournamentRegistrationService service, CancellationToken ct) =>
		{
			var registration = await service.GetAsync(id, registrationId, http.User.GetUid(), ct);
			return registration is null ? Results.NotFound() : Results.Ok(registration);
		}).RequireVisibleTournament();

		group.MapPost("/admin-add", async (
			Guid id, AdminAddRegistrationRequest request, ITournamentRegistrationService service, CancellationToken ct) =>
		{
			var registration = await service.AdminAddAsync(id, request.PlayerId, ct);
			return registration is null ? Results.NotFound() : Results.Ok(registration);
		}).RequireAuthorization("Admin");

		group.MapPost("/admin-add-team", async (
			Guid id, AdminAddTeamRegistrationRequest request, ITournamentRegistrationService service, CancellationToken ct) =>
		{
			var registration = await service.AdminAddTeamAsync(id, request.TeamId, ct);
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
