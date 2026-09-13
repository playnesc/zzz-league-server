namespace ZenlessLeague.Api.Features.CommunityCosts.Engines;

public static class EngineCostEndpoints
{
	public static void MapEngineCostEndpoints(this IEndpointRouteBuilder app)
	{
		var group = app.MapGroup("/api/community-costs/engines");

		// Public — same audience as the /costs page itself.
		group.MapGet("/", async (IEngineCostService service, CancellationToken ct) =>
			Results.Ok(await service.ListAsync(ct)));

		// Public — round-trips with the real NESC_engine_costs.csv format, no auth needed to
		// download it (browser-navigable, matches a plain <a download> link on the frontend).
		group.MapGet("/export.csv", async (IEngineCostService service, CancellationToken ct) =>
			Results.Text(await service.ExportCsvAsync(ct), "text/csv"));

		// Admin-only, direct overwrite, no review pipeline — this is a bulk data-management tool,
		// not a community contribution.
		group.MapPost("/import", async (ImportEngineCostCsvRequest request, IEngineCostService service, CancellationToken ct) =>
		{
			await service.ImportCsvAsync(request.Csv, ct);
			return Results.NoContent();
		}).RequireAuthorization("Admin");
	}
}
