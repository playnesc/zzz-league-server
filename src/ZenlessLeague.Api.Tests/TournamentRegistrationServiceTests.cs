using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Teams;
using ZenlessLeague.Api.Features.Tournaments;

namespace ZenlessLeague.Api.Tests;

public class TournamentRegistrationServiceTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static (TournamentRegistrationService Service, FakeFirebaseStorageClient Storage, FakeTournamentNotifier Notifier) CreateService(AppDbContext context)
    {
        var storage = new FakeFirebaseStorageClient();
        var notifier = new FakeTournamentNotifier();
        var service = new TournamentRegistrationService(
            new TournamentRepository(context),
            new TeamRepository(context),
            new PlayerRepository(context),
            new TournamentRegistrationRepository(context),
            storage,
            notifier);
        return (service, storage, notifier);
    }

    private static async Task<Tournament> SeedSoloTournamentAsync(AppDbContext context)
    {
        var tournament = new Tournament
        {
            Id = Guid.NewGuid(),
            Name = "Cup",
            RegistrationType = TournamentRegistrationType.Solo,
            State = TournamentState.Registration,
            RegistrationStartDate = DateTimeOffset.UtcNow.AddDays(-1),
            RegistrationEndDate = DateTimeOffset.UtcNow.AddDays(1),
            MinTier = 0,
            MaxTier = 1000,
        };
        context.Tournaments.Add(tournament);
        await context.SaveChangesAsync();
        return tournament;
    }

    private static PlayerRegistrationDetailsRequest Details(string? rosterScreenshot = null, string? hoyolabScreenshot = null) =>
        new("game-uid", true, null, "nick", "preset", "roster", rosterScreenshot, hoyolabScreenshot);

    [Fact]
    public async Task RegisterSolo_WithScreenshots_UploadsAndCachesOnPlayer()
    {
        await using var context = CreateContext();
        var tournament = await SeedSoloTournamentAsync(context);
        context.Players.Add(new Player { Uid = "p1", Name = "p1", CreatedAt = DateTimeOffset.UtcNow });
        await context.SaveChangesAsync();
        var (service, storage, notifier) = CreateService(context);

        var result = await service.RegisterSoloAsync(tournament.Id, new SoloRegistrationRequest("p1", Details("data:image/png;base64,Zm9v", "data:image/png;base64,YmFy")));

        Assert.NotNull(result);
        Assert.Equal(2, storage.Uploads.Count);
        Assert.NotNull(result!.Player1.RosterScreenshotUrl);
        Assert.NotNull(result.Player1.HoyolabScreenshotUrl);

        var player = await context.Players.SingleAsync(p => p.Uid == "p1");
        Assert.NotNull(player.LastRegistrationCache);
        Assert.Equal(result.Player1.RosterScreenshotUrl, player.LastRegistrationCache!.RosterScreenshotUrl);

        Assert.Contains((tournament.Id, result.Id), notifier.RegistrationChanged);
    }

    [Fact]
    public async Task RegisterSolo_ReregisterWithoutNewScreenshots_ReusesExistingRegistrationUrls()
    {
        await using var context = CreateContext();
        var tournament = await SeedSoloTournamentAsync(context);
        context.Players.Add(new Player { Uid = "p1", Name = "p1", CreatedAt = DateTimeOffset.UtcNow });
        await context.SaveChangesAsync();
        var (service, storage, notifier) = CreateService(context);

        var first = await service.RegisterSoloAsync(tournament.Id, new SoloRegistrationRequest("p1", Details("data:image/png;base64,Zm9v", "data:image/png;base64,YmFy")));
        storage.Uploads.Clear();

        // Re-register with a changed nickname but no new screenshots.
        var second = await service.RegisterSoloAsync(tournament.Id, new SoloRegistrationRequest("p1", Details() with { DarteNickname = "new-nick" }));

        Assert.Empty(storage.Uploads); // no new upload calls
        Assert.Equal(first!.Player1.RosterScreenshotUrl, second!.Player1.RosterScreenshotUrl);
        Assert.Equal(first.Player1.HoyolabScreenshotUrl, second.Player1.HoyolabScreenshotUrl);
        Assert.Equal("new-nick", second.Player1.DarteNickname);
    }

    [Fact]
    public async Task RegisterSolo_NoExistingRegistration_ReusesPlayersLastRegistrationCache()
    {
        await using var context = CreateContext();
        var tournament = await SeedSoloTournamentAsync(context);
        context.Players.Add(new Player
        {
            Uid = "p1", Name = "p1", CreatedAt = DateTimeOffset.UtcNow,
            LastRegistrationCache = new LastRegistrationCache
            {
                GameUid = "old", DarteNickname = "old", DartePresetName = "old", RosterName = "old",
                RosterScreenshotUrl = "https://cached/roster.jpg", HoyolabScreenshotUrl = "https://cached/hoyolab.jpg",
            },
        });
        await context.SaveChangesAsync();
        var (service, storage, notifier) = CreateService(context);

        var result = await service.RegisterSoloAsync(tournament.Id, new SoloRegistrationRequest("p1", Details()));

        Assert.Empty(storage.Uploads);
        Assert.Equal("https://cached/roster.jpg", result!.Player1.RosterScreenshotUrl);
        Assert.Equal("https://cached/hoyolab.jpg", result.Player1.HoyolabScreenshotUrl);
    }

    [Fact]
    public async Task RegisterSolo_NoScreenshotsAnywhere_ThrowsValidation()
    {
        await using var context = CreateContext();
        var tournament = await SeedSoloTournamentAsync(context);
        context.Players.Add(new Player { Uid = "p1", Name = "p1", CreatedAt = DateTimeOffset.UtcNow });
        await context.SaveChangesAsync();
        var (service, _, _) = CreateService(context);

        await Assert.ThrowsAsync<TournamentValidationException>(() =>
            service.RegisterSoloAsync(tournament.Id, new SoloRegistrationRequest("p1", Details())));
    }
}
