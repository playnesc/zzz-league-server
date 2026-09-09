using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.History;
using ZenlessLeague.Api.Features.Players;

namespace ZenlessLeague.Api.Tests;

public class HistoryServiceTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task SeedPlayerAsync(AppDbContext context, string uid, int elo = 1000, bool midConfirmed = false)
    {
        context.Players.Add(new Player { Uid = uid, Name = uid, Elo = elo, IsMidConfirmed = midConfirmed, CreatedAt = DateTimeOffset.UtcNow });
        await context.SaveChangesAsync();
    }

    private static HistoryService CreateService(AppDbContext context) =>
        new(new PlayerRepository(context), new HistoryRepository(context), TestDiscord.CreateRoleAssignmentService(context));

    [Fact]
    public async Task RegisterMatchResult_ComputesEloAndUpdatesCounters()
    {
        await using var context = CreateContext();
        await SeedPlayerAsync(context, "p1");
        await SeedPlayerAsync(context, "p2");
        var service = CreateService(context);

        var entry = await service.RegisterMatchResultAsync("p1", "p2", p1Win: true, overrideEloChange: -1);

        Assert.Equal(25, entry.P1Change);
        Assert.Equal(-22, entry.P2Change);

        var p1 = await context.Players.SingleAsync(p => p.Uid == "p1");
        var p2 = await context.Players.SingleAsync(p => p.Uid == "p2");
        Assert.Equal(25, p1.TournamentPoints);
        Assert.Equal(1, p1.Wins);
        Assert.Equal(0, p1.Losses);
        Assert.Equal(-22, p2.TournamentPoints);
        Assert.Equal(1, p2.Losses);
        Assert.NotNull(p1.LastPlayedTournamentTimestamp);
    }

    [Fact]
    public async Task RegisterMatchResult_WithOverride_UsesFixedDelta()
    {
        await using var context = CreateContext();
        await SeedPlayerAsync(context, "p1");
        await SeedPlayerAsync(context, "p2");
        var service = CreateService(context);

        var entry = await service.RegisterMatchResultAsync("p1", "p2", p1Win: true, overrideEloChange: 10);

        Assert.Equal(10, entry.P1Change);
        Assert.Equal(-10, entry.P2Change);
    }

    [Fact]
    public async Task RegisterMatchResult_TechLoss_WinnerGetsZeroChange()
    {
        await using var context = CreateContext();
        await SeedPlayerAsync(context, "p1");
        await SeedPlayerAsync(context, "p2");
        var service = CreateService(context);

        // p1 wins by tech loss of p2 — p1 (winner) gets 0, p2 (loser) gets the "as if lost normally" change.
        var entry = await service.RegisterMatchResultAsync("p1", "p2", p1Win: true, overrideEloChange: -1, techLoss: true);

        Assert.Equal(0, entry.P1Change);
        Assert.Equal(-22, entry.P2Change);
        Assert.Equal(TournamentMatchKind.TechLoss, entry.Kind);
    }

    [Fact]
    public async Task ReverseMatchResult_RestoresOriginalPlayerState()
    {
        await using var context = CreateContext();
        await SeedPlayerAsync(context, "p1");
        await SeedPlayerAsync(context, "p2");
        var service = CreateService(context);

        var entry = await service.RegisterMatchResultAsync("p1", "p2", p1Win: true, overrideEloChange: -1);
        await service.ReverseEntryAsync(entry.Id);

        var p1 = await context.Players.SingleAsync(p => p.Uid == "p1");
        var p2 = await context.Players.SingleAsync(p => p.Uid == "p2");
        Assert.Equal(0, p1.TournamentPoints);
        Assert.Equal(0, p1.Wins);
        Assert.Equal(0, p2.TournamentPoints);
        Assert.Equal(0, p2.Losses);
        Assert.Empty(await service.ListAsync());
    }

    [Fact]
    public async Task SetPlayerElo_UpdatesEloAndTierFlagsAndWritesAdjustmentEntry()
    {
        await using var context = CreateContext();
        await SeedPlayerAsync(context, "p1", elo: 1000);
        var service = CreateService(context);

        var entry = await service.SetPlayerEloAsync("p1", 1250);

        Assert.Equal(250, entry.P1Change);
        Assert.Null(entry.P2PlayerId);
        Assert.Equal(TournamentMatchKind.Adjustment, entry.Kind);

        var p1 = await context.Players.SingleAsync(p => p.Uid == "p1");
        Assert.Equal(1250, p1.Elo);
        Assert.True(p1.IsMidConfirmed);
    }

    [Fact]
    public async Task ReverseEntry_SinglePlayerAdjustment_RevertsEloAndTierFlags()
    {
        await using var context = CreateContext();
        await SeedPlayerAsync(context, "p1", elo: 1000);
        var service = CreateService(context);

        var entry = await service.SetPlayerEloAsync("p1", 1250); // crosses the mid-confirm threshold
        var reversed = await service.ReverseEntryAsync(entry.Id);

        Assert.True(reversed);
        var p1 = await context.Players.SingleAsync(p => p.Uid == "p1");
        Assert.Equal(1000, p1.Elo);
        Assert.False(p1.IsMidConfirmed);
        Assert.Empty(await service.ListAsync());
    }

    [Fact]
    public async Task ReverseEntry_UnknownId_ReturnsFalse()
    {
        await using var context = CreateContext();
        var service = CreateService(context);

        Assert.False(await service.ReverseEntryAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task RegisterMatchResult_MissingPlayer_ThrowsValidationException()
    {
        await using var context = CreateContext();
        await SeedPlayerAsync(context, "p1");
        var service = CreateService(context);

        await Assert.ThrowsAsync<HistoryValidationException>(() =>
            service.RegisterMatchResultAsync("p1", "ghost", p1Win: true, overrideEloChange: -1));
    }
}
