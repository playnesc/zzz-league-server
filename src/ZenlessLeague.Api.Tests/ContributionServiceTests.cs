using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.CommunityCosts.Agents;
using ZenlessLeague.Api.Features.CommunityCosts.Catalog;
using ZenlessLeague.Api.Features.CommunityCosts.Contributions;
using ZenlessLeague.Api.Features.CommunityCosts.Engines;
using ZenlessLeague.Api.Features.Players;

namespace ZenlessLeague.Api.Tests;

public class ContributionServiceTests
{
	private static AppDbContext CreateContext() =>
		new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

	private static ContributionService CreateService(AppDbContext context) =>
		new(new ContributionRepository(context), new AgentCostRepository(context), new EngineCostRepository(context), new PlayerRepository(context));

	private static Player SeedPlayer(AppDbContext context, string uid, PlayerRole role = PlayerRole.Player)
	{
		var player = new Player { Uid = uid, Name = uid, Role = role, CreatedAt = DateTimeOffset.UtcNow };
		context.Players.Add(player);
		return player;
	}

	private static void SeedAgent(AppDbContext context, string id) =>
		context.AgentCatalogEntries.Add(new AgentCatalogEntry { Id = id, Name = id, Specialty = Specialty.Attack, Rarity = 4, SortOrder = 0 });

	private static void SeedEngine(AppDbContext context, string id) =>
		context.EngineCatalogEntries.Add(new EngineCatalogEntry { Id = id, Name = id, Specialty = Specialty.Attack, Rarity = 4, SortOrder = 0 });

	[Fact]
	public async Task UpsertReviewAsync_SecondCallFromSameReviewer_EditsInPlaceInsteadOfDuplicating()
	{
		await using var context = CreateContext();
		SeedPlayer(context, "author");
		var reviewer = SeedPlayer(context, "reviewer");
		SeedAgent(context, "agent-1");
		await context.SaveChangesAsync();
		var service = CreateService(context);
		var contribution = await service.CreateAgentContributionAsync("author", new CreateAgentContributionRequest("agent-1", "msg", [1, 2, 3, 4, 5, 6, 7]));

		var afterFirst = await service.UpsertReviewAsync(contribution.Id, "reviewer", new UpsertReviewRequest(ReviewVote.Positive, "looks good"));
		var afterSecond = await service.UpsertReviewAsync(contribution.Id, "reviewer", new UpsertReviewRequest(ReviewVote.Negative, "changed my mind"));

		Assert.Single(afterFirst!.Reviews);
		Assert.Single(afterSecond!.Reviews);
		var review = Assert.Single(afterSecond.Reviews);
		Assert.Equal(ReviewVote.Negative, review.Vote);
		Assert.Equal("changed my mind", review.Comment);
		Assert.Equal(1, await context.ContributionReviews.CountAsync());
	}

	[Fact]
	public async Task ApproveAsync_AgentContribution_CreatesCostRowWhenMissing()
	{
		await using var context = CreateContext();
		SeedPlayer(context, "author");
		SeedPlayer(context, "mod", PlayerRole.Moderator);
		SeedAgent(context, "agent-1");
		await context.SaveChangesAsync();
		var service = CreateService(context);
		var contribution = await service.CreateAgentContributionAsync("author", new CreateAgentContributionRequest("agent-1", "msg", [1, 2, 3, 4, 5, 6, 7]));

		var result = await service.ApproveAsync(contribution.Id, "mod");

		Assert.Equal(ContributionStatus.Approved, result!.Status);
		var row = await context.AgentCostRows.SingleAsync(r => r.AgentId == "agent-1");
		Assert.Equal([1, 2, 3, 4, 5, 6, 7], row.Costs);
	}

	[Fact]
	public async Task ApproveAsync_AgentContribution_OverwritesExistingCostRow()
	{
		await using var context = CreateContext();
		SeedPlayer(context, "author");
		SeedPlayer(context, "mod", PlayerRole.Moderator);
		SeedAgent(context, "agent-1");
		context.AgentCostRows.Add(new AgentCostRow { Id = Guid.NewGuid(), AgentId = "agent-1", PotentialTier = null, Costs = [9, 9, 9, 9, 9, 9, 9], UpdatedAt = DateTimeOffset.UtcNow });
		await context.SaveChangesAsync();
		var service = CreateService(context);
		var contribution = await service.CreateAgentContributionAsync("author", new CreateAgentContributionRequest("agent-1", "msg", [1, 2, 3, 4, 5, 6, 7]));

		await service.ApproveAsync(contribution.Id, "mod");

		Assert.Equal(1, await context.AgentCostRows.CountAsync(r => r.AgentId == "agent-1"));
		var row = await context.AgentCostRows.SingleAsync(r => r.AgentId == "agent-1");
		Assert.Equal([1, 2, 3, 4, 5, 6, 7], row.Costs);
	}

	[Fact]
	public async Task ApproveAsync_EngineBaseContribution_CreatesBaseRow()
	{
		await using var context = CreateContext();
		SeedPlayer(context, "author");
		SeedPlayer(context, "mod", PlayerRole.Moderator);
		SeedEngine(context, "engine-1");
		await context.SaveChangesAsync();
		var service = CreateService(context);
		var contribution = await service.CreateEngineContributionAsync("author", new CreateEngineContributionRequest("engine-1", null, "msg", [1, 2, 3, 4, 5, 6]));

		await service.ApproveAsync(contribution.Id, "mod");

		var row = await context.EngineCostRows.SingleAsync(r => r.EngineId == "engine-1" && r.AgentId == null);
		Assert.Equal([1, 2, 3, 4, 5, 6], row.Costs);
	}

	[Fact]
	public async Task ApproveAsync_EngineOverrideForUnassignedAgent_CreatesRow_ImplicitAssignment()
	{
		await using var context = CreateContext();
		SeedPlayer(context, "author");
		SeedPlayer(context, "mod", PlayerRole.Moderator);
		SeedEngine(context, "engine-1");
		SeedAgent(context, "agent-1");
		await context.SaveChangesAsync();
		var service = CreateService(context);
		var contribution = await service.CreateEngineContributionAsync("author", new CreateEngineContributionRequest("engine-1", "agent-1", "msg", [1, 2, 3, 4, 5]));

		await service.ApproveAsync(contribution.Id, "mod");

		var row = await context.EngineCostRows.SingleAsync(r => r.EngineId == "engine-1" && r.AgentId == "agent-1");
		Assert.Equal([1, 2, 3, 4, 5, 0], row.Costs);
	}

	[Fact]
	public async Task ApproveAsync_EngineOverrideForAlreadyAssignedAgent_PreservesExistingOffValue()
	{
		await using var context = CreateContext();
		SeedPlayer(context, "author");
		SeedPlayer(context, "mod", PlayerRole.Moderator);
		SeedEngine(context, "engine-1");
		SeedAgent(context, "agent-1");
		context.EngineCostRows.Add(new EngineCostRow { Id = Guid.NewGuid(), EngineId = "engine-1", AgentId = "agent-1", Costs = [9, 9, 9, 9, 9, 42], UpdatedAt = DateTimeOffset.UtcNow });
		await context.SaveChangesAsync();
		var service = CreateService(context);
		var contribution = await service.CreateEngineContributionAsync("author", new CreateEngineContributionRequest("engine-1", "agent-1", "msg", [1, 2, 3, 4, 5]));

		await service.ApproveAsync(contribution.Id, "mod");

		var row = await context.EngineCostRows.SingleAsync(r => r.EngineId == "engine-1" && r.AgentId == "agent-1");
		Assert.Equal([1, 2, 3, 4, 5, 42], row.Costs);
	}

	[Fact]
	public async Task RejectAsync_DoesNotMutateCostRows()
	{
		await using var context = CreateContext();
		SeedPlayer(context, "author");
		SeedPlayer(context, "mod", PlayerRole.Moderator);
		SeedAgent(context, "agent-1");
		await context.SaveChangesAsync();
		var service = CreateService(context);
		var contribution = await service.CreateAgentContributionAsync("author", new CreateAgentContributionRequest("agent-1", "msg", [1, 2, 3, 4, 5, 6, 7]));

		var result = await service.RejectAsync(contribution.Id, "mod");

		Assert.Equal(ContributionStatus.Rejected, result!.Status);
		Assert.Empty(await context.AgentCostRows.ToListAsync());
	}

	[Fact]
	public async Task ApproveAsync_AlreadyResolved_Throws()
	{
		await using var context = CreateContext();
		SeedPlayer(context, "author");
		SeedPlayer(context, "mod", PlayerRole.Moderator);
		SeedAgent(context, "agent-1");
		await context.SaveChangesAsync();
		var service = CreateService(context);
		var contribution = await service.CreateAgentContributionAsync("author", new CreateAgentContributionRequest("agent-1", "msg", [1, 2, 3, 4, 5, 6, 7]));
		await service.ApproveAsync(contribution.Id, "mod");

		await Assert.ThrowsAsync<ContributionValidationException>(() => service.ApproveAsync(contribution.Id, "mod"));
	}

	[Fact]
	public async Task AddCommentAsync_MultipleCommentsFromSameAuthor_AllPersist()
	{
		await using var context = CreateContext();
		SeedPlayer(context, "author");
		SeedPlayer(context, "reviewer");
		SeedAgent(context, "agent-1");
		await context.SaveChangesAsync();
		var service = CreateService(context);
		var contribution = await service.CreateAgentContributionAsync("author", new CreateAgentContributionRequest("agent-1", "msg", [1, 2, 3, 4, 5, 6, 7]));
		var withReview = await service.UpsertReviewAsync(contribution.Id, "reviewer", new UpsertReviewRequest(ReviewVote.Positive, "nice"));
		var reviewId = withReview!.Reviews.Single().Id;

		await service.AddCommentAsync(contribution.Id, reviewId, "author", new AddReviewCommentRequest("first"));
		var afterSecond = await service.AddCommentAsync(contribution.Id, reviewId, "author", new AddReviewCommentRequest("second"));

		Assert.Equal(2, afterSecond!.Reviews.Single().Comments.Count);
	}
}
