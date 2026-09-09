namespace ZenlessLeague.Api.Features.Seasons;

public static class SeasonEndpoints
{
    public static void MapSeasonEndpoints(this IEndpointRouteBuilder app)
    {
        var seasons = app.MapGroup("/api/seasons");

        seasons.MapPost("/finalize", async (ISeasonService service, CancellationToken ct) =>
        {
            try
            {
                await service.FinalizeSeasonAsync(ct);
                return Results.Ok(new { success = true });
            }
            catch (SeasonValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).RequireAuthorization("Admin");

        seasons.MapPost("/reset", async (ResetSeasonRequest request, ISeasonService service, CancellationToken ct) =>
        {
            try
            {
                await service.ResetSeasonAsync(request.SeasonName, ct);
                return Results.Ok(new { success = true });
            }
            catch (SeasonValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).RequireAuthorization("Admin");

        seasons.MapPost("/backfill-last-played-timestamps", async (ISeasonService service, CancellationToken ct) =>
            Results.Ok(new BackfillResultDto(await service.BackfillLastPlayedTimestampsAsync(ct))))
            .RequireAuthorization("Admin");

        var archives = app.MapGroup("/api/archives");

        archives.MapGet("/", async (ISeasonService service, CancellationToken ct) =>
            Results.Ok(await service.ListArchivesAsync(ct)));

        archives.MapDelete("/{seasonName}", async (string seasonName, ISeasonService service, CancellationToken ct) =>
            await service.DeleteArchiveAsync(seasonName, ct) ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization("Admin");
    }
}
