using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Data;
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
            TestDiscord.CreateRoleAssignmentService(context));

    [Fact]
    public async Task FinalizeSeasonAsync_FoldsTournamentPointsIntoEloAndRecomputesTier()
    {
        await using var context = CreateContext();
        context.Players.Add(new Player { Uid = "p1", Name = "p1", Elo = 1000, TournamentPoints = 250, CreatedAt = DateTimeOffset.UtcNow });
        await context.SaveChangesAsync();
        var service = CreateService(context);

        await service.FinalizeSeasonAsync();

        var p1 = await context.Players.SingleAsync(p => p.Uid == "p1");
        Assert.Equal(1250, p1.Elo);
        Assert.Equal(0, p1.TournamentPoints);
        Assert.True(p1.IsMidConfirmed);
        Assert.NotNull(p1.LastEloUpdateTimestamp);
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
        context.Players.Add(new Player
        {
            Uid = "p1", Name = "p1", Elo = 1450, TournamentPoints = 30, SeasonalPlayedTournamentCount = 5,
            IsMidConfirmed = true, IsHighConfirmed = true, CreatedAt = DateTimeOffset.UtcNow,
        });
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
        context.Players.Add(new Player { Uid = "p1", Name = "p1", Elo = 1000, CreatedAt = DateTimeOffset.UtcNow });
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
        context.Players.Add(new Player { Uid = "p1", Name = "p1", CreatedAt = DateTimeOffset.UtcNow });
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

        context.Players.Add(new Player { Uid = "p1", Name = "p1", LastPlayedTournamentTimestamp = older, CreatedAt = DateTimeOffset.UtcNow });
        context.Players.Add(new Player { Uid = "p2", Name = "p2", LastPlayedTournamentTimestamp = null, CreatedAt = DateTimeOffset.UtcNow });
        context.HistoryEntries.Add(new HistoryEntry { Id = Guid.NewGuid(), P1PlayerId = "p1", P1Change = 5, P2PlayerId = "p2", P2Change = -5, Timestamp = newer });
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var updated = await service.BackfillLastPlayedTimestampsAsync();

        Assert.Equal(2, updated); // both p1 (older -> newer) and p2 (null -> newer) get updated
        var p1 = await context.Players.SingleAsync(p => p.Uid == "p1");
        var p2 = await context.Players.SingleAsync(p => p.Uid == "p2");
        Assert.Equal(newer, p1.LastPlayedTournamentTimestamp);
        Assert.Equal(newer, p2.LastPlayedTournamentTimestamp);
    }
}
