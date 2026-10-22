using ZenlessLeague.Api.Infrastructure.Authorization;
using ZenlessLeague.Api.Infrastructure.Http;


namespace ZenlessLeague.Api.Features.CommunityCosts.Contributions;

public static class ContributionEndpoints
{
	public static void MapContributionEndpoints(this IEndpointRouteBuilder app)
	{
		var group = app.MapGroup("/api/community-costs/contributions");

		// Public reads — same audience as the /costs page itself.
		group.MapGet("/", async (IContributionService service, CancellationToken ct) =>
			Results.Ok(await service.ListAsync(ct)));

		group.MapGet("/{id:guid}", async (Guid id, IContributionService service, CancellationToken ct) =>
		{
			var contribution = await service.GetAsync(id, ct);
			return contribution is null ? Results.NotFound() : Results.Ok(contribution);
		});

		// Any logged-in player.
		group.MapPost("/agent", async (CreateAgentContributionRequest request, HttpContext http, IContributionService service, CancellationToken ct) =>
		{
			var contribution = await service.CreateAgentContributionAsync(http.User.GetUid()!, request, ct);
			return Results.Created($"/api/community-costs/contributions/{contribution.Id}", contribution);
		}).RequireAuthorization().RequireRateLimiting(RateLimits.UserWrites);

		group.MapPost("/engine", async (CreateEngineContributionRequest request, HttpContext http, IContributionService service, CancellationToken ct) =>
		{
			var contribution = await service.CreateEngineContributionAsync(http.User.GetUid()!, request, ct);
			return Results.Created($"/api/community-costs/contributions/{contribution.Id}", contribution);
		}).RequireAuthorization().RequireRateLimiting(RateLimits.UserWrites);

		group.MapPut("/{id:guid}/review", async (Guid id, UpsertReviewRequest request, HttpContext http, IContributionService service, CancellationToken ct) =>
		{
			var contribution = await service.UpsertReviewAsync(id, http.User.GetUid()!, request, ct);
			return contribution is null ? Results.NotFound() : Results.Ok(contribution);
		}).RequireAuthorization().RequireRateLimiting(RateLimits.UserWrites);

		group.MapPost("/{id:guid}/reviews/{reviewId:guid}/comments", async (Guid id, Guid reviewId, AddReviewCommentRequest request, HttpContext http, IContributionService service, CancellationToken ct) =>
		{
			var contribution = await service.AddCommentAsync(id, reviewId, http.User.GetUid()!, request, ct);
			return contribution is null ? Results.NotFound() : Results.Ok(contribution);
		}).RequireAuthorization().RequireRateLimiting(RateLimits.UserWrites);

		// Moderator or Admin — matches the mock's $isAdmin || $isModerator gate exactly, since
		// the "Moderator" policy already succeeds for either role (PlayerRole is ordinal:
		// Player < Moderator < Admin, and RoleAuthorizationHandler checks role >= MinRole).
		group.MapPost("/{id:guid}/approve", async (Guid id, HttpContext http, IContributionService service, CancellationToken ct) =>
		{
			var contribution = await service.ApproveAsync(id, http.User.GetUid()!, ct);
			return contribution is null ? Results.NotFound() : Results.Ok(contribution);
		}).RequireAuthorization("Moderator");

		group.MapPost("/{id:guid}/reject", async (Guid id, HttpContext http, IContributionService service, CancellationToken ct) =>
		{
			var contribution = await service.RejectAsync(id, http.User.GetUid()!, ct);
			return contribution is null ? Results.NotFound() : Results.Ok(contribution);
		}).RequireAuthorization("Moderator");
	}
}
