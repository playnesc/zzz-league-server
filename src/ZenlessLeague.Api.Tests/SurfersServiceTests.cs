using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Surfers;
using ZenlessLeague.Api.Infrastructure.Persistence;

namespace ZenlessLeague.Api.Tests;

public class SurfersServiceTests
{
	private static AppDbContext CreateContext() =>
		new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

	private static SurfersService CreateService(AppDbContext context) =>
		new(new SurfersScoreRepository(context), new PlayerRepository(context),
			Options.Create(new SurfersOptions { MaxScorePerSecond = 100, MaxRunSeconds = 3600 }),
			NullLogger<SurfersService>.Instance);

	[Fact]
	public async Task SubmitAsync_KeepsOnlyThePlayersBestScore()
	{
		await using var context = CreateContext();
		TestPlayers.Create(context, "p1");
		await context.SaveChangesAsync();
		var service = CreateService(context);

		var first = await service.SubmitAsync("p1", new SubmitSurfersScoreRequest(500, 60));
		var worse = await service.SubmitAsync("p1", new SubmitSurfersScoreRequest(300, 60));
		var better = await service.SubmitAsync("p1", new SubmitSurfersScoreRequest(800, 60));

		Assert.Equal(new SubmitSurfersScoreResponse(500, true, 1), first);
		Assert.Equal(new SubmitSurfersScoreResponse(500, false, 1), worse);
		Assert.Equal(new SubmitSurfersScoreResponse(800, true, 1), better);
		Assert.Equal(800, context.SurfersScores.Single().Score);
	}

	[Fact]
	public async Task SubmitAsync_ZeroScoreDoesNotPutThePlayerOnTheBoard()
	{
		await using var context = CreateContext();
		TestPlayers.Create(context, "p1");
		await context.SaveChangesAsync();

		var result = await CreateService(context).SubmitAsync("p1", new SubmitSurfersScoreRequest(0, 5));

		Assert.Equal(new SubmitSurfersScoreResponse(0, false, null), result);
		Assert.Empty(context.SurfersScores);
	}

	[Fact]
	public async Task SubmitAsync_UnknownPlayer_ReturnsNull()
	{
		await using var context = CreateContext();

		Assert.Null(await CreateService(context).SubmitAsync("ghost", new SubmitSurfersScoreRequest(100, 60)));
	}

	[Theory]
	[InlineData(-1, 60)]
	[InlineData(100, 0)]
	[InlineData(100, -5)]
	[InlineData(100, double.NaN)]
	[InlineData(100, 3601)]
	[InlineData(6001, 60)] // over 100 points/s
	public async Task SubmitAsync_ImpossibleRun_IsRejected(int score, double durationSeconds)
	{
		await using var context = CreateContext();
		TestPlayers.Create(context, "p1");
		await context.SaveChangesAsync();

		await Assert.ThrowsAsync<SurfersValidationException>(() =>
			CreateService(context).SubmitAsync("p1", new SubmitSurfersScoreRequest(score, durationSeconds)));
		Assert.Empty(context.SurfersScores);
	}

	[Fact]
	public async Task GetLeaderboardAsync_RanksByScoreWithTiesSharingARank()
	{
		await using var context = CreateContext();
		foreach (var uid in new[] { "a", "b", "c", "d" })
		{
			TestPlayers.Create(context, uid);
		}

		await context.SaveChangesAsync();
		var service = CreateService(context);
		await service.SubmitAsync("a", new SubmitSurfersScoreRequest(300, 60));
		await service.SubmitAsync("b", new SubmitSurfersScoreRequest(500, 60));
		await service.SubmitAsync("c", new SubmitSurfersScoreRequest(300, 60));
		await service.SubmitAsync("d", new SubmitSurfersScoreRequest(100, 60));

		var board = await service.GetLeaderboardAsync(10);

		// a and c tie; a got there first, so it's listed first.
		Assert.Equal(["b", "a", "c", "d"], board.Select(e => e.Uid));
		Assert.Equal([1, 2, 2, 4], board.Select(e => e.Rank));
		Assert.Equal(2, (await service.GetMineAsync("c"))!.Rank);
		Assert.Equal(2, (await service.GetLeaderboardAsync(2)).Count);
	}

	[Fact]
	public async Task GetMineAsync_PlayerWithoutScore_ReturnsNull()
	{
		await using var context = CreateContext();
		TestPlayers.Create(context, "p1");
		await context.SaveChangesAsync();

		Assert.Null(await CreateService(context).GetMineAsync("p1"));
	}
}
