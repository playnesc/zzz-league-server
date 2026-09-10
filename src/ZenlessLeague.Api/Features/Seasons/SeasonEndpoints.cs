namespace ZenlessLeague.Api.Features.Seasons;

public static class SeasonEndpoints
{
	public static void MapSeasonEndpoints(this IEndpointRouteBuilder app)
	{
		var seasons = app.MapGroup("/api/seasons");

		seasons.MapPost("/finalize", async (ISeasonService service, CancellationToken ct) =>
		{
			await service.FinalizeSeasonAsync(ct);
			return Results.Ok(new { success = true });
		}).RequireAuthorization("Admin");

		seasons.MapPost("/reset", async (ResetSeasonRequest request, ISeasonService service, CancellationToken ct) =>
		{
			await service.ResetSeasonAsync(request.SeasonName, ct);
			return Results.Ok(new { success = true });
		}).RequireAuthorization("Admin");

		seasons.MapPost("/backfill-last-played-timestamps", async (ISeasonService service, CancellationToken ct) =>
			Results.Ok(new BackfillResultDto(await service.BackfillLastPlayedTimestampsAsync(ct))))
			.RequireAuthorization("Admin");

		seasons.MapGet("/timer", async (ISeasonService service, CancellationToken ct) =>
			Results.Ok(new SeasonTimerDto(await service.GetTimerAsync(ct))));

		seasons.MapPut("/timer", async (SetSeasonTimerRequest request, ISeasonService service, CancellationToken ct) =>
		{
			await service.SetTimerAsync(request.EndTimestamp, ct);
			return Results.Ok(new SeasonTimerDto(request.EndTimestamp));
		}).RequireAuthorization("Admin");

		var archives = app.MapGroup("/api/archives");

		archives.MapGet("/", async (ISeasonService service, CancellationToken ct) =>
			Results.Ok(await service.ListArchivesAsync(ct)));

		archives.MapDelete("/{seasonName}", async (string seasonName, ISeasonService service, CancellationToken ct) =>
			await service.DeleteArchiveAsync(seasonName, ct) ? Results.NoContent() : Results.NotFound())
			.RequireAuthorization("Admin");
	}
}
