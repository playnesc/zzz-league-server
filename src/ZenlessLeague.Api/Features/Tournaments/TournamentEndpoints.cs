namespace ZenlessLeague.Api.Features.Tournaments;

public static class TournamentEndpoints
{
	public static void MapTournamentEndpoints(this IEndpointRouteBuilder app)
	{
		var group = app.MapGroup("/api/tournaments");

		group.MapPost("/", async (CreateTournamentRequest request, ITournamentService service, CancellationToken ct) =>
		{
			var tournament = await service.CreateAsync(request, ct);
			return Results.Created($"/api/tournaments/{tournament.Id}", tournament);
		}).RequireAuthorization("Admin");

		group.MapGet("/", async (ITournamentService service, CancellationToken ct) =>
			Results.Ok(await service.ListAsync(ct)));

		group.MapGet("/{id:guid}", async (Guid id, ITournamentService service, CancellationToken ct) =>
		{
			var tournament = await service.GetAsync(id, ct);
			return tournament is null ? Results.NotFound() : Results.Ok(tournament);
		});

		group.MapPut("/{id:guid}", async (Guid id, UpdateTournamentRequest request, ITournamentService service, CancellationToken ct) =>
		{
			var tournament = await service.UpdateAsync(id, request, ct);
			return tournament is null ? Results.NotFound() : Results.Ok(tournament);
		}).RequireAuthorization("Admin");

		group.MapPost("/{id:guid}/close-registration", async (Guid id, ITournamentService service, CancellationToken ct) =>
			await Handle(service.CloseRegistrationAsync(id, ct))).RequireAuthorization("Admin");

		group.MapPost("/{id:guid}/challonge-bracket", async (Guid id, ITournamentLifecycleService service, CancellationToken ct) =>
			await Handle(service.CreateChallongeBracketAsync(id, ct))).RequireAuthorization("Admin");

		group.MapPost("/{id:guid}/start", async (Guid id, ITournamentLifecycleService service, CancellationToken ct) =>
			await Handle(service.StartAsync(id, ct))).RequireAuthorization("Admin");

		group.MapPost("/{id:guid}/finish", async (Guid id, ITournamentLifecycleService service, CancellationToken ct) =>
			await Handle(service.FinishAsync(id, ct))).RequireAuthorization("Admin");

		group.MapPost("/{id:guid}/refresh-games", async (Guid id, ITournamentLifecycleService service, CancellationToken ct) =>
		{
			await service.RefreshGamesAsync(id, ct);
			return Results.NoContent();
		}).RequireAuthorization("Admin");

		group.MapPost("/{id:guid}/increment-count", async (Guid id, ITournamentLifecycleService service, CancellationToken ct) =>
		{
			await service.IncrementTournamentCountAsync(id, seasonal: false, ct);
			return Results.NoContent();
		}).RequireAuthorization("Admin");

		group.MapPost("/{id:guid}/increment-seasonal-count", async (Guid id, ITournamentLifecycleService service, CancellationToken ct) =>
		{
			await service.IncrementTournamentCountAsync(id, seasonal: true, ct);
			return Results.NoContent();
		}).RequireAuthorization("Admin");

		group.MapPost("/{id:guid}/split", async (Guid id, SplitTournamentRequest request, ITournamentLifecycleService service, CancellationToken ct) =>
		{
			var result = await service.SplitAsync(id, request, ct);
			return result is null ? Results.NotFound() : Results.Ok(result);
		}).RequireAuthorization("Admin");

		group.MapDelete("/{id:guid}", async (Guid id, ITournamentLifecycleService service, CancellationToken ct) =>
		{
			try
			{
				return await service.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound();
			}
			catch (TournamentLifecycleException ex)
			{
				// The tournament row is already gone by the time this can be thrown — surface the
				// cleanup failure without implying the delete itself failed.
				return Results.Ok(new { deleted = true, warning = ex.Message });
			}
		}).RequireAuthorization("Admin");
	}

	private static async Task<IResult> Handle(Task<TournamentDto?> action)
	{
		var tournament = await action;
		return tournament is null ? Results.NotFound() : Results.Ok(tournament);
	}
}
