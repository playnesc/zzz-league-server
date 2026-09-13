namespace ZenlessLeague.Api.Features.CommunityCosts.Agents;

public static class AgentCostEndpoints
{
	public static void MapAgentCostEndpoints(this IEndpointRouteBuilder app)
	{
		var group = app.MapGroup("/api/community-costs/agents");

		// Public — same audience as the /costs page itself.
		group.MapGet("/", async (IAgentCostService service, CancellationToken ct) =>
			Results.Ok(await service.ListAsync(ct)));

		// Public — round-trips with the real NESC_agent_costs.csv format, no auth needed to
		// download it (browser-navigable, matches a plain <a download> link on the frontend).
		group.MapGet("/export.csv", async (IAgentCostService service, CancellationToken ct) =>
			Results.Text(await service.ExportCsvAsync(ct), "text/csv"));

		// Admin-only, direct overwrite, no review pipeline — this is a bulk data-management tool,
		// not a community contribution.
		group.MapPost("/import", async (ImportAgentCostCsvRequest request, IAgentCostService service, CancellationToken ct) =>
		{
			await service.ImportCsvAsync(request.Csv, ct);
			return Results.NoContent();
		}).RequireAuthorization("Admin");
	}
}
