namespace ZenlessLeague.Api.Features.Teams;

public static class TeamEndpoints
{
    public static void MapTeamEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/teams");

        group.MapPost("/", async (CreateTeamRequest request, ITeamService service, CancellationToken ct) =>
        {
            try
            {
                var team = await service.CreateAsync(request, ct);
                return Results.Created($"/api/teams/{team.Id}", team);
            }
            catch (TeamValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapGet("/", async (ITeamService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(ct)));

        group.MapGet("/{id:guid}", async (Guid id, ITeamService service, CancellationToken ct) =>
        {
            var team = await service.GetAsync(id, ct);
            return team is null ? Results.NotFound() : Results.Ok(team);
        });

        group.MapPut("/{id:guid}", async (Guid id, EditTeamRequest request, ITeamService service, CancellationToken ct) =>
        {
            try
            {
                var team = await service.UpdateAsync(id, request, ct);
                return team is null ? Results.NotFound() : Results.Ok(team);
            }
            catch (TeamValidationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapDelete("/{id:guid}", async (Guid id, ITeamService service, CancellationToken ct) =>
            await service.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());
    }
}
