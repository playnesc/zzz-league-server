namespace ZenlessLeague.Api.Features.History;

public static class HistoryEndpoints
{
    public static void MapHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        var history = app.MapGroup("/api/history");

        // Port of functions/admin/registerMatch.js — an ad-hoc (non-tournament) 1v1 result.
        history.MapPost("/matches", async (RegisterMatchResultRequest request, IHistoryService service, CancellationToken ct) =>
        {
            try
            {
                var entry = await service.RegisterMatchResultAsync(
                    request.P1PlayerId, request.P2PlayerId, request.P1Win, request.OverrideEloChange,
                    techLoss: request.TechLoss, resultP1: request.ResultP1, resultP2: request.ResultP2,
                    resultScreenshotUrl: request.ResultScreenshotUrl, ct: ct);
                return Results.Created($"/api/history/{entry.Id}", entry);
            }
            catch (HistoryValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).RequireAuthorization("Admin");

        history.MapGet("/", async (IHistoryService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(ct)));

        history.MapGet("/player/{uid}", async (string uid, IHistoryService service, CancellationToken ct) =>
            Results.Ok(await service.ListByPlayerAsync(uid, ct)));

        // Port of functions/admin/history/deleteHistoryEntry.js.
        history.MapDelete("/{id:guid}", async (Guid id, IHistoryService service, CancellationToken ct) =>
            await service.ReverseEntryAsync(id, ct) ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization("Admin");

        // Port of functions/admin/updatePlayerElo.js.
        app.MapGroup("/api/players").MapPut("/{uid}/elo", async (string uid, SetPlayerEloRequest request, IHistoryService service, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.SetPlayerEloAsync(uid, request.Elo, ct));
            }
            catch (HistoryValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).RequireAuthorization("Admin");
    }
}
