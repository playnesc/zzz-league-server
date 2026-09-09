using ZenlessLeague.Api.Data.Authorization;

namespace ZenlessLeague.Api.Features.Tournaments.Matches;

public static class MatchEndpoints
{
    public static void MapMatchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tournaments/{id:guid}/matches");

        group.MapGet("/", async (Guid id, IMatchService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(id, ct)));

        group.MapGet("/{matchId:guid}", async (Guid id, Guid matchId, IMatchService service, CancellationToken ct) =>
        {
            var match = await service.GetAsync(id, matchId, ct);
            return match is null ? Results.NotFound() : Results.Ok(match);
        });

        // Port of functions/user/approveResult.js — participant self-report/approve.
        group.MapPost("/{matchId:guid}/result", async (
            Guid id, Guid matchId, SubmitPlayerResultRequest request, HttpContext http, IMatchService service, CancellationToken ct) =>
        {
            var uid = http.User.GetUid();
            if (uid is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var match = await service.SubmitPlayerResultAsync(id, matchId, uid, request, ct);
                return match is null ? Results.NotFound() : Results.Ok(match);
            }
            catch (MatchForbiddenException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (MatchValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).RequireAuthorization();

        // Port of functions/admin/tournament/adminSetMatchResult.js — moderator/admin override or tech-loss.
        group.MapPost("/{matchId:guid}/admin-result", async (
            Guid id, Guid matchId, SetAdminResultRequest request, IMatchService service, CancellationToken ct) =>
        {
            try
            {
                var match = await service.SetAdminResultAsync(id, matchId, request, ct);
                return match is null ? Results.NotFound() : Results.Ok(match);
            }
            catch (MatchValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).RequireAuthorization("Moderator");
    }
}
