using ZenlessLeague.Api.Data.Authorization;

namespace ZenlessLeague.Api.Features.Tournaments;

public static class RegistrationEndpoints
{
    public static void MapRegistrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tournaments/{id:guid}/registrations");

        group.MapPost("/solo", async (
            Guid id, SoloRegistrationRequest request, ITournamentRegistrationService service, CancellationToken ct) =>
        {
            try
            {
                var registration = await service.RegisterSoloAsync(id, request, ct);
                return registration is null ? Results.NotFound() : Results.Ok(registration);
            }
            catch (TournamentValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).RequireAuthorization();

        group.MapPost("/team", async (
            Guid id, TeamRegistrationRequest request, ITournamentRegistrationService service, CancellationToken ct) =>
        {
            try
            {
                var registration = await service.RegisterTeamAsync(id, request, ct);
                return registration is null ? Results.NotFound() : Results.Ok(registration);
            }
            catch (TournamentValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
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
            try
            {
                var registration = await service.AdminAddAsync(id, request.PlayerId, ct);
                return registration is null ? Results.NotFound() : Results.Ok(registration);
            }
            catch (TournamentValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
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

            try
            {
                var result = await service.CancelAsync(id, uid, ct);
                return result switch
                {
                    null => Results.NotFound(),
                    false => Results.NotFound(),
                    true => Results.NoContent(),
                };
            }
            catch (TournamentValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).RequireAuthorization();
    }
}
