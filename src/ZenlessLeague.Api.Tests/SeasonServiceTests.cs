using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.Discord;
using ZenlessLeague.Api.Features.History;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Seasons;

namespace ZenlessLeague.Api.Tests;

public class SeasonServiceTests
{
	private static AppDbContext CreateContext() =>
		new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

	private static ISeasonService CreateService(AppDbContext context) =>
		new SeasonService(
			new PlayerRepository(context),
			new ArchiveRepository(context),
			new HistoryRepository(context),
			TestDiscord.CreateRoleAssignmentService(context),
			new SeasonTimerRepository(context),
			NullLogger<SeasonService>.Instance);

	[Fact]
	public async Task FinalizeSeasonAsync_FoldsTournamentPointsIntoEloAndRecomputesTier()
	{
		await using var context = CreateContext();
		var p1 = TestPlayers.Create(context, "p1", elo: 1000);
		p1.TournamentPoints = 250;
		await context.SaveChangesAsync();
		var service = CreateService(context);

		await service.FinalizeSeasonAsync();

		Assert.Equal(1250, p1.Elo);
		Assert.Equal(0, p1.TournamentPoints);
		Assert.True(p1.IsMidConfirmed);
		Assert.NotNull(p1.LastEloUpdateTimestamp);
	}

	[Fact]
	public async Task FinalizeSeasonAsync_DiscordFailureForOnePlayer_StillResyncsTheRestAndReportsIt()
	{
		await using var context = CreateContext();
		foreach (var uid in new[] { "p1", "p2" })
		{
			var player = TestPlayers.Create(context, uid, elo: 1000);
			player.TournamentPoints = 250; // crosses the mid-tier threshold, so both need a role resync
			player.DiscordId = $"d-{uid}";
		}
		await context.SaveChangesAsync();

		var discord = new FakeDiscordClient();
		discord.UnknownMemberDiscordIds.Add("d-p1");
		var roleAssignment = new DiscordRoleAssignmentService(
			new PlayerRepository(context), discord,
			Options.Create(new DiscordOptions { NewbieRoleId = "newbie", MidRoleId = "mid", HighRoleId = "high" }));
		var service = new SeasonService(
			new PlayerRepository(context), new ArchiveRepository(context), new HistoryRepository(context),
			roleAssignment, new SeasonTimerRepository(context), NullLogger<SeasonService>.Instance);

		var ex = await Assert.ThrowsAsync<SeasonValidationException>(() => service.FinalizeSeasonAsync());

		Assert.Contains("p1", ex.Message);
		// p1 (first by name) failing didn't stop p2's resync, and the rollover itself is committed.
		Assert.Contains(("d-p2", "mid"), discord.AddedMemberRoles);
		Assert.All(await context.Players.ToListAsync(), p => Assert.Equal(1250, p.Elo));
	}

	[Fact]
	public async Task FinalizeSeasonAsync_NoPlayers_Throws()
	{
		await using var context = CreateContext();
		var service = CreateService(context);

		await Assert.ThrowsAsync<SeasonValidationException>(() => service.FinalizeSeasonAsync());
	}

	[Fact]
	public async Task ResetSeasonAsync_ArchivesThenResetsToTierBaseline()
	{
		await using var context = CreateContext();
		var seeded = TestPlayers.Create(context, "p1", elo: 1450, midConfirmed: true, highConfirmed: true);
		seeded.TournamentPoints = 30;
		seeded.SeasonalPlayedTournamentCount = 5;
		await context.SaveChangesAsync();
		var service = CreateService(context);

		await service.ResetSeasonAsync("Season 1");

		var p1 = await context.Players.SingleAsync(p => p.Uid == "p1");
		Assert.Equal(1400, p1.Elo); // high-confirmed baseline
		Assert.Equal(0, p1.TournamentPoints);
		Assert.Equal(0, p1.SeasonalPlayedTournamentCount);

		var archives = await service.ListArchivesAsync();
		var archive = Assert.Single(archives);
		Assert.Equal("Season 1", archive.SeasonName);
		var snapshot = Assert.Single(archive.Players);
		Assert.Equal(1450, snapshot.Elo); // pre-reset value preserved in the archive
		Assert.True(snapshot.IsHighConfirmed);
	}

	[Fact]
	public async Task ResetSeasonAsync_SameNameTwice_OverwritesArchive()
	{
		await using var context = CreateContext();
		TestPlayers.Create(context, "p1", elo: 1000);
		await context.SaveChangesAsync();
		var service = CreateService(context);

		await service.ResetSeasonAsync("Season 1");
		var player = await context.Players.SingleAsync(p => p.Uid == "p1");
		player.Elo = 1234;
		await context.SaveChangesAsync();

		await service.ResetSeasonAsync("Season 1");

		var archives = await service.ListArchivesAsync();
		var archive = Assert.Single(archives);
		var snapshot = Assert.Single(archive.Players);
		Assert.Equal(1234, snapshot.Elo);
	}

	[Fact]
	public async Task DeleteArchiveAsync_RemovesMatchingArchive()
	{
		await using var context = CreateContext();
		TestPlayers.Create(context, "p1");
		await context.SaveChangesAsync();
		var service = CreateService(context);
		await service.ResetSeasonAsync("Season 1");

		Assert.True(await service.DeleteArchiveAsync("Season 1"));
		Assert.Empty(await service.ListArchivesAsync());
		Assert.False(await service.DeleteArchiveAsync("Season 1"));
	}

	[Fact]
	public async Task BackfillLastPlayedTimestampsAsync_OnlyUpdatesWhenHistoryIsNewer()
	{
		await using var context = CreateContext();
		var older = DateTimeOffset.UtcNow.AddDays(-10);
		var newer = DateTimeOffset.UtcNow.AddDays(-1);

		var p1Seed = TestPlayers.Create(context, "p1");
		p1Seed.LastPlayedTournamentTimestamp = older;
		var p2Seed = TestPlayers.Create(context, "p2");
		context.HistoryEntries.Add(new HistoryEntry { Id = Guid.NewGuid(), P1PlayerId = p1Seed.Id, P1Change = 5, P2PlayerId = p2Seed.Id, P2Change = -5, Timestamp = newer });
		await context.SaveChangesAsync();
		var service = CreateService(context);

		var updated = await service.BackfillLastPlayedTimestampsAsync();

		Assert.Equal(2, updated); // both p1 (older -> newer) and p2 (null -> newer) get updated
		var p1 = await context.Players.SingleAsync(p => p.Uid == "p1");
		var p2 = await context.Players.SingleAsync(p => p.Uid == "p2");
		Assert.Equal(newer, p1.LastPlayedTournamentTimestamp);
		Assert.Equal(newer, p2.LastPlayedTournamentTimestamp);
	}

	[Fact]
	public async Task Timer_DefaultsToNull_ThenRoundTripsAfterSet()
	{
		await using var context = CreateContext();
		var service = CreateService(context);

		Assert.Null(await service.GetTimerAsync());

		var endTimestamp = DateTimeOffset.UtcNow.AddDays(7);
		await service.SetTimerAsync(endTimestamp);

		Assert.Equal(endTimestamp, await service.GetTimerAsync());
	}

	[Fact]
	public async Task Timer_SetTwice_OverwritesPreviousValue()
	{
		await using var context = CreateContext();
		var service = CreateService(context);

		await service.SetTimerAsync(DateTimeOffset.UtcNow.AddDays(1));
		var second = DateTimeOffset.UtcNow.AddDays(14);
		await service.SetTimerAsync(second);

		Assert.Equal(second, await service.GetTimerAsync());
	}
}
