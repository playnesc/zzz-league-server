using ZenlessLeague.Api.Features.Players;

namespace ZenlessLeague.Api.Features.History;

public static class HistoryEndpoints
{
	public static void MapHistoryEndpoints(this IEndpointRouteBuilder app)
	{
		var history = app.MapGroup("/api/history");

		// Resolves the request's Firebase uids to Player.Id here, at the HTTP boundary, since
		// HistoryService's own methods work in Guid ids (matching HistoryEntry's real FKs).
		history.MapPost("/matches", async (RegisterMatchResultRequest request, IHistoryService service, IPlayerRepository playerRepository, CancellationToken ct) =>
		{
			var p1 = await playerRepository.GetEntityByUidAsync(request.P1PlayerId, ct)
				?? throw new HistoryValidationException($"Player '{request.P1PlayerId}' does not exist.");
			var p2 = await playerRepository.GetEntityByUidAsync(request.P2PlayerId, ct)
				?? throw new HistoryValidationException($"Player '{request.P2PlayerId}' does not exist.");

			var entry = await service.RegisterMatchResultAsync(
				p1.Id, p2.Id, request.P1Win, request.OverrideEloChange,
				techLoss: request.TechLoss, resultP1: request.ResultP1, resultP2: request.ResultP2,
				resultScreenshotUrl: request.ResultScreenshotUrl, ct: ct);
			return Results.Created($"/api/history/{entry.Id}", entry);
		}).RequireAuthorization("Admin");

		// Keyset-paginated — pass `beforeTimestamp`/`beforeId` from the last entry of the previous
		// page to get the next `take` older entries. Omit both for the first page.
		history.MapGet("/", async (int? take, DateTimeOffset? beforeTimestamp, Guid? beforeId, IHistoryService service, CancellationToken ct) =>
			Results.Ok(await service.ListPageAsync(ClampPageSize(take), beforeTimestamp, beforeId, ct)));

		history.MapGet("/player/{uid}", async (string uid, int? take, DateTimeOffset? beforeTimestamp, Guid? beforeId, IHistoryService service, CancellationToken ct) =>
			Results.Ok(await service.ListPageByPlayerAsync(uid, ClampPageSize(take), beforeTimestamp, beforeId, ct)));

		history.MapGet("/elo-preview", async (string p1Uid, string p2Uid, IHistoryService service, CancellationToken ct) =>
			Results.Ok(await service.PreviewEloChangeAsync(p1Uid, p2Uid, ct)))
			.RequireAuthorization("Admin");

		history.MapDelete("/{id:guid}", async (Guid id, IHistoryService service, CancellationToken ct) =>
			await service.ReverseEntryAsync(id, ct) ? Results.NoContent() : Results.NotFound())
			.RequireAuthorization("Admin");

		app.MapGroup("/api/players").MapPut("/{uid}/elo", async (string uid, SetPlayerEloRequest request, IHistoryService historyService, IPlayerService playerService, CancellationToken ct) =>
		{
			await historyService.SetPlayerEloAsync(uid, request.Elo, ct);
			return Results.Ok(await playerService.GetAsync(uid, ct));
		}).RequireAuthorization("Admin");
	}

	private const int DefaultPageSize = 50;
	private const int MaxPageSize = 200;

	private static int ClampPageSize(int? requested) =>
		Math.Clamp(requested ?? DefaultPageSize, 1, MaxPageSize);
}
