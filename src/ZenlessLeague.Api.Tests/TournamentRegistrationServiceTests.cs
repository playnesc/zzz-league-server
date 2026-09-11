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
	public async Task RegisterSolo_WithScreenshots_Uploads()
	{
		await using var context = CreateContext();
		var tournament = await SeedSoloTournamentAsync(context);
		TestPlayers.Create(context, "p1");
		await context.SaveChangesAsync();
		var (service, storage, notifier) = CreateService(context);

		var result = await service.RegisterSoloAsync(tournament.Id, new SoloRegistrationRequest("p1", Details("data:image/png;base64,Zm9v", "data:image/png;base64,YmFy")));

		Assert.NotNull(result);
		Assert.Equal(2, storage.Uploads.Count);
		Assert.NotNull(result!.Player1.RosterScreenshotUrl);
		Assert.NotNull(result.Player1.HoyolabScreenshotUrl);

		Assert.Contains((tournament.Id, result.Id), notifier.RegistrationChanged);
	}

	[Fact]
	public async Task RegisterSolo_ReregisterWithoutNewScreenshots_ReusesExistingRegistrationUrls()
	{
		await using var context = CreateContext();
		var tournament = await SeedSoloTournamentAsync(context);
		TestPlayers.Create(context, "p1");
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
	public async Task RegisterSolo_NoExistingRegistrationForThisTournament_ReusesPlayersMostRecentRegistrationElsewhere()
	{
		await using var context = CreateContext();
		var previousTournament = await SeedSoloTournamentAsync(context);
		var tournament = await SeedSoloTournamentAsync(context);
		TestPlayers.Create(context, "p1");
		await context.SaveChangesAsync();
		var (service, storage, _) = CreateService(context);

		await service.RegisterSoloAsync(previousTournament.Id, new SoloRegistrationRequest("p1", Details("data:image/png;base64,Zm9v", "data:image/png;base64,YmFy")));
		storage.Uploads.Clear();

		var result = await service.RegisterSoloAsync(tournament.Id, new SoloRegistrationRequest("p1", Details()));

		Assert.Empty(storage.Uploads);
		Assert.NotNull(result!.Player1.RosterScreenshotUrl);
		Assert.NotNull(result.Player1.HoyolabScreenshotUrl);
	}

	[Fact]
	public async Task RegisterSolo_NoScreenshotsAnywhere_ThrowsValidation()
	{
		await using var context = CreateContext();
		var tournament = await SeedSoloTournamentAsync(context);
		TestPlayers.Create(context, "p1");
		await context.SaveChangesAsync();
		var (service, _, _) = CreateService(context);

		await Assert.ThrowsAsync<TournamentValidationException>(() =>
			service.RegisterSoloAsync(tournament.Id, new SoloRegistrationRequest("p1", Details())));
	}

	private static async Task<Tournament> SeedTeamTournamentAsync(AppDbContext context, int minTier = 0, int maxTier = 1000)
	{
		var tournament = new Tournament
		{
			Id = Guid.NewGuid(),
			Name = "Duo Cup",
			RegistrationType = TournamentRegistrationType.Team,
			State = TournamentState.Registration,
			RegistrationStartDate = DateTimeOffset.UtcNow.AddDays(-1),
			RegistrationEndDate = DateTimeOffset.UtcNow.AddDays(1),
			MinTier = minTier,
			MaxTier = maxTier,
		};
		context.Tournaments.Add(tournament);
		await context.SaveChangesAsync();
		return tournament;
	}

	[Fact]
	public async Task RegisterTeam_LowTierPlayers_NotBlockedByTournamentTierFilter()
	{
		await using var context = CreateContext();
		// A high-tier-only tournament — would reject a NEWBIE player under solo's EnsureTierEligible.
		var tournament = await SeedTeamTournamentAsync(context, minTier: 1000, maxTier: 1000);
		var p1 = TestPlayers.Create(context, "p1");
		var p2 = TestPlayers.Create(context, "p2");
		var team = new Team { Id = Guid.NewGuid(), Name = "Duo", Player1Id = p1.Id, Player2Id = p2.Id, CreatedAt = DateTimeOffset.UtcNow };
		context.Teams.Add(team);
		await context.SaveChangesAsync();
		var (service, _, notifier) = CreateService(context);

		var withScreenshots = Details("data:image/png;base64,Zm9v", "data:image/png;base64,YmFy");
		var result = await service.RegisterTeamAsync(tournament.Id, new TeamRegistrationRequest(team.Id, withScreenshots, withScreenshots));

		Assert.NotNull(result);
		Assert.Contains(notifier.RegistrationChanged, e => e.TournamentId == tournament.Id);
	}

	[Fact]
	public async Task RegisterTeam_SoloTournament_Throws()
	{
		await using var context = CreateContext();
		var tournament = await SeedSoloTournamentAsync(context);
		var p1 = TestPlayers.Create(context, "p1");
		var p2 = TestPlayers.Create(context, "p2");
		var team = new Team { Id = Guid.NewGuid(), Name = "Duo", Player1Id = p1.Id, Player2Id = p2.Id, CreatedAt = DateTimeOffset.UtcNow };
		context.Teams.Add(team);
		await context.SaveChangesAsync();
		var (service, _, _) = CreateService(context);

		await Assert.ThrowsAsync<TournamentValidationException>(() =>
			service.RegisterTeamAsync(tournament.Id, new TeamRegistrationRequest(team.Id, Details(), Details())));
	}

	[Fact]
	public async Task AdminAddAsync_TeamTournament_Throws()
	{
		await using var context = CreateContext();
		var tournament = await SeedTeamTournamentAsync(context);
		TestPlayers.Create(context, "p1");
		await context.SaveChangesAsync();
		var (service, _, _) = CreateService(context);

		await Assert.ThrowsAsync<TournamentValidationException>(() => service.AdminAddAsync(tournament.Id, "p1"));
	}

	[Fact]
	public async Task AdminAddAsync_SoloTournament_CreatesApprovedRegistration()
	{
		await using var context = CreateContext();
		var tournament = await SeedSoloTournamentAsync(context);
		TestPlayers.Create(context, "p1");
		await context.SaveChangesAsync();
		var (service, _, notifier) = CreateService(context);

		var result = await service.AdminAddAsync(tournament.Id, "p1");

		Assert.NotNull(result);
		Assert.True(result!.Approved);
		Assert.Equal("p1", result.PlayerId);
		Assert.Contains(notifier.RegistrationChanged, e => e.TournamentId == tournament.Id);
	}

	[Fact]
	public async Task AdminAddTeamAsync_SoloTournament_Throws()
	{
		await using var context = CreateContext();
		var tournament = await SeedSoloTournamentAsync(context);
		var p1 = TestPlayers.Create(context, "p1");
		var p2 = TestPlayers.Create(context, "p2");
		var team = new Team { Id = Guid.NewGuid(), Name = "Duo", Player1Id = p1.Id, Player2Id = p2.Id, CreatedAt = DateTimeOffset.UtcNow };
		context.Teams.Add(team);
		await context.SaveChangesAsync();
		var (service, _, _) = CreateService(context);

		await Assert.ThrowsAsync<TournamentValidationException>(() => service.AdminAddTeamAsync(tournament.Id, team.Id));
	}

	[Fact]
	public async Task AdminAddTeamAsync_TeamTournament_CreatesApprovedRegistration()
	{
		await using var context = CreateContext();
		var tournament = await SeedTeamTournamentAsync(context);
		var p1 = TestPlayers.Create(context, "p1");
		var p2 = TestPlayers.Create(context, "p2");
		var team = new Team { Id = Guid.NewGuid(), Name = "Duo", Player1Id = p1.Id, Player2Id = p2.Id, CreatedAt = DateTimeOffset.UtcNow };
		context.Teams.Add(team);
		await context.SaveChangesAsync();
		var (service, _, notifier) = CreateService(context);

		var result = await service.AdminAddTeamAsync(tournament.Id, team.Id);

		Assert.NotNull(result);
		Assert.True(result!.Approved);
		Assert.Equal(team.Id, result.TeamId);
		Assert.Contains(notifier.RegistrationChanged, e => e.TournamentId == tournament.Id);
	}
}
