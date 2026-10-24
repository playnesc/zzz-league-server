using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZenlessLeague.Api.Features.History;
using ZenlessLeague.Api.Features.Nexus;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Teams;
using ZenlessLeague.Api.Features.Tournaments;
using ZenlessLeague.Api.Features.Tournaments.Challonge;
using ZenlessLeague.Api.Features.Tournaments.Matches;
using ZenlessLeague.Api.Infrastructure.Persistence;

namespace ZenlessLeague.Api.Tests;

public class TournamentServiceTests
{
	private static AppDbContext CreateContext() =>
		new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

	private static (ITournamentService Service, ITournamentLifecycleService Lifecycle, FakeTournamentNotifier Notifier, FakeChallongeClient Challonge, FakeDiscordClient Discord) CreateService(AppDbContext context)
	{
		var playerRepository = new PlayerRepository(context);
		var historyService = new HistoryService(playerRepository, new HistoryRepository(context), new TeamRepository(context), TestDiscord.CreateRoleAssignmentService(context), new TournamentRepository(context), NullLogger<HistoryService>.Instance);
		var challonge = new FakeChallongeClient();
		var matchService = new MatchService(
			new TournamentRepository(context),
			new TournamentMatchRepository(context),
			new TeamRepository(context),
			playerRepository,
			new ChallongeMatchSync(new TournamentRepository(context), new TournamentChallongeParticipantRepository(context), new TournamentMatchRepository(context), challonge),
			new NexusResultResolver(new FakeNexusClient(), new TournamentRegistrationRepository(context), playerRepository, NullLogger<NexusResultResolver>.Instance),
			historyService,
			new FakeFirebaseStorageClient(),
			new FakeTournamentNotifier(),
			Options.Create(new NexusOptions()),
			new EfUnitOfWork(context));

		var notifier = new FakeTournamentNotifier();
		var discord = new FakeDiscordClient();
		var service = new TournamentService(new TournamentRepository(context), playerRepository, notifier);
		var lifecycle = new TournamentLifecycleService(
			new TournamentRepository(context),
			new TournamentRegistrationRepository(context),
			new TeamRepository(context),
			playerRepository,
			new TournamentChallongeParticipantRepository(context),
			challonge,
			matchService,
			historyService,
			TestDiscord.CreateTournamentResourcesService(context, discord),
			notifier,
			new EfUnitOfWork(context),
			NullLogger<TournamentLifecycleService>.Instance);
		return (service, lifecycle, notifier, challonge, discord);
	}

	private static async Task<Tournament> SeedTournamentAsync(
		AppDbContext context, TournamentState state = TournamentState.Registration, string? challongeTournamentId = null)
	{
		var tournament = new Tournament
		{
			Id = Guid.NewGuid(),
			Name = "Cup",
			RegistrationType = TournamentRegistrationType.Solo,
			State = state,
			RegistrationEndDate = DateTimeOffset.UtcNow.AddDays(1),
			ChallongeTournamentId = challongeTournamentId,
		};
		context.Tournaments.Add(tournament);
		await context.SaveChangesAsync();
		return tournament;
	}

	private static async Task<TournamentRegistration> SeedApprovedRegistrationAsync(AppDbContext context, Guid tournamentId, string uid)
	{
		var player = TestPlayers.Create(context, uid);
		var registration = new TournamentRegistration
		{
			Id = Guid.NewGuid(),
			TournamentId = tournamentId,
			PlayerId = player.Id,
			Player1Details = new PlayerRegistrationDetails
			{
				GameUid = "g", DarteNickname = "n", DartePresetName = "p", RosterName = "r",
			},
			Approved = true,
			RegisteredAt = DateTimeOffset.UtcNow,
			UpdatedAt = DateTimeOffset.UtcNow,
		};
		context.TournamentRegistrations.Add(registration);
		await context.SaveChangesAsync();
		return registration;
	}

	[Fact]
	public async Task CloseRegistrationAsync_WhenOpen_TransitionsState()
	{
		await using var context = CreateContext();
		var tournament = await SeedTournamentAsync(context);
		var (service, _, notifier, _, _) = CreateService(context);

		var result = await service.CloseRegistrationAsync(tournament.Id);

		Assert.NotNull(result);
		Assert.Equal(TournamentState.RegistrationClosed, result!.State);
		Assert.Contains(tournament.Id, notifier.TournamentChanged);
	}

	[Fact]
	public async Task CloseRegistrationAsync_WhenAlreadyClosed_Throws()
	{
		await using var context = CreateContext();
		var tournament = await SeedTournamentAsync(context, TournamentState.RegistrationClosed);
		var (service, _, notifier, _, _) = CreateService(context);

		await Assert.ThrowsAsync<TournamentLifecycleException>(() => service.CloseRegistrationAsync(tournament.Id));
	}

	[Fact]
	public async Task IncrementTournamentCountAsync_BumpsOnlyApprovedRegistrants()
	{
		await using var context = CreateContext();
		var tournament = await SeedTournamentAsync(context);
		await SeedApprovedRegistrationAsync(context, tournament.Id, "p1");
		await SeedApprovedRegistrationAsync(context, tournament.Id, "p2");
		var (_, lifecycle, _, _, _) = CreateService(context);

		await lifecycle.IncrementTournamentCountAsync(tournament.Id, seasonal: true);

		var p1 = await context.Players.SingleAsync(p => p.Uid == "p1");
		Assert.Equal(1, p1.PlayedTournamentCount);
		Assert.Equal(1, p1.SeasonalPlayedTournamentCount);
	}

	[Fact]
	public async Task IncrementTournamentCountAsync_NoApprovedRegistrants_Throws()
	{
		await using var context = CreateContext();
		var tournament = await SeedTournamentAsync(context);
		var (_, lifecycle, _, _, _) = CreateService(context);

		await Assert.ThrowsAsync<TournamentLifecycleException>(() => lifecycle.IncrementTournamentCountAsync(tournament.Id, seasonal: false));
	}

	[Fact]
	public async Task SplitAsync_CreatesDivisionsAndReparentsRegistrations()
	{
		await using var context = CreateContext();
		var tournament = await SeedTournamentAsync(context);
		await SeedApprovedRegistrationAsync(context, tournament.Id, "p1");
		await SeedApprovedRegistrationAsync(context, tournament.Id, "p2");
		await SeedApprovedRegistrationAsync(context, tournament.Id, "p3");
		await SeedApprovedRegistrationAsync(context, tournament.Id, "p4");
		var (_, lifecycle, notifier, _, _) = CreateService(context);

		var result = await lifecycle.SplitAsync(tournament.Id, new SplitTournamentRequest([["p1", "p2"], ["p3", "p4"]]));

		Assert.NotNull(result);
		Assert.Equal(2, result!.DivisionTournamentIds.Count);
		Assert.Equal(tournament.Id, result.DivisionTournamentIds[0]);

		var original = await context.Tournaments.SingleAsync(t => t.Id == tournament.Id);
		Assert.Equal("Cup A", original.Name);
		Assert.Equal(TournamentState.RegistrationClosed, original.State);
		Assert.Equal(tournament.Id.ToString(), original.DivisionGroupId);
		Assert.Equal(1, original.DivisionIndex);

		var divisionB = await context.Tournaments.SingleAsync(t => t.Id == result.DivisionTournamentIds[1]);
		Assert.Equal("Cup B", divisionB.Name);
		Assert.Equal(2, divisionB.DivisionIndex);
		Assert.Equal(tournament.Id.ToString(), divisionB.DivisionGroupId);

		// p1/p2 stay on the original tournament, p3/p4 move to the new division.
		var idsByUid = await context.Players.ToDictionaryAsync(p => p.Uid, p => p.Id);
		var originalRegs = await context.TournamentRegistrations.Where(r => r.TournamentId == tournament.Id).ToListAsync();
		Assert.Equal(2, originalRegs.Count);
		Assert.All(originalRegs, r => Assert.Contains(r.PlayerId, new Guid?[] { idsByUid["p1"], idsByUid["p2"] }));

		var divisionBRegs = await context.TournamentRegistrations.Where(r => r.TournamentId == divisionB.Id).ToListAsync();
		Assert.Equal(2, divisionBRegs.Count);
		Assert.All(divisionBRegs, r => Assert.Contains(r.PlayerId, new Guid?[] { idsByUid["p3"], idsByUid["p4"] }));

		// Both the original tournament and the new division get a change notification.
		Assert.Contains(tournament.Id, notifier.TournamentChanged);
		Assert.Contains(divisionB.Id, notifier.TournamentChanged);
	}

	[Fact]
	public async Task SplitAsync_NewDivisionsKeepGameModeAndScreenshots()
	{
		await using var context = CreateContext();
		var tournament = await SeedTournamentAsync(context);
		tournament.GameMode = TournamentGameMode.DeadlyAssault;
		foreach (var uid in new[] { "p1", "p2", "p3", "p4" })
		{
			var registration = await SeedApprovedRegistrationAsync(context, tournament.Id, uid);
			registration.Player1Details.RosterScreenshotUrl = $"https://img/{uid}-roster.png";
			registration.Player1Details.HoyolabScreenshotUrl = $"https://img/{uid}-hoyolab.png";
		}
		await context.SaveChangesAsync();
		var (_, lifecycle, _, _, _) = CreateService(context);

		var result = await lifecycle.SplitAsync(tournament.Id, new SplitTournamentRequest([["p1", "p2"], ["p3", "p4"]]));

		// A Deadly Assault division defaulting to Shiyu Defense would pick match winners backwards.
		var divisionB = await context.Tournaments.SingleAsync(t => t.Id == result!.DivisionTournamentIds[1]);
		Assert.Equal(TournamentGameMode.DeadlyAssault, divisionB.GameMode);

		var divisionBRegs = await context.TournamentRegistrations.Where(r => r.TournamentId == divisionB.Id).ToListAsync();
		Assert.Equal(2, divisionBRegs.Count);
		Assert.All(divisionBRegs, r =>
		{
			Assert.NotNull(r.Player1Details.RosterScreenshotUrl);
			Assert.NotNull(r.Player1Details.HoyolabScreenshotUrl);
		});
	}

	[Fact]
	public async Task CreateChallongeBracketAsync_RecordsParticipantsAndLocksTournament()
	{
		await using var context = CreateContext();
		var tournament = await SeedTournamentAsync(context, TournamentState.RegistrationClosed);
		await SeedApprovedRegistrationAsync(context, tournament.Id, "p1");
		await SeedApprovedRegistrationAsync(context, tournament.Id, "p2");
		var (_, lifecycle, _, challonge, _) = CreateService(context);

		var result = await lifecycle.CreateChallongeBracketAsync(tournament.Id);

		Assert.Equal(TournamentState.BracketCreated, result!.State);
		Assert.Equal("ct-1", result.ChallongeTournamentId);
		Assert.Equal(2, await context.TournamentChallongeParticipants.CountAsync(p => p.TournamentId == tournament.Id));
		Assert.Empty(challonge.DeletedTournamentIds);
	}

	[Fact]
	public async Task CreateChallongeBracketAsync_FailsAfterChallongeCreatedIt_DeletesTheOrphanAndLeavesTournamentUnlocked()
	{
		await using var context = CreateContext();
		var tournament = await SeedTournamentAsync(context, TournamentState.RegistrationClosed);
		await SeedApprovedRegistrationAsync(context, tournament.Id, "p1");
		await SeedApprovedRegistrationAsync(context, tournament.Id, "p2");
		var (_, lifecycle, _, challonge, _) = CreateService(context);
		challonge.FailBulkAdd = true;

		await Assert.ThrowsAsync<ChallongeApiException>(() => lifecycle.CreateChallongeBracketAsync(tournament.Id));

		// Otherwise every retry would leave another empty Challonge tournament behind.
		Assert.Equal(["ct-1"], challonge.DeletedTournamentIds);
		var reloaded = await context.Tournaments.SingleAsync(t => t.Id == tournament.Id);
		Assert.Equal(TournamentState.RegistrationClosed, reloaded.State);
		Assert.Null(reloaded.ChallongeTournamentId);
	}

	// Seeds a started solo tournament whose Challonge bracket has p1 (rank 1) and p2 (rank 2).
	private static async Task<(Tournament Tournament, FakeChallongeClient Challonge, ITournamentLifecycleService Lifecycle)> SeedStartedForFinishAsync(AppDbContext context)
	{
		var tournament = await SeedTournamentAsync(context, TournamentState.AwaitingReview, challongeTournamentId: "ctid");
		var p1 = TestPlayers.Create(context, "p1");
		var p2 = TestPlayers.Create(context, "p2");
		context.TournamentChallongeParticipants.Add(new TournamentChallongeParticipant { TournamentId = tournament.Id, ChallongeParticipantId = "c1", PlayerId = p1.Id });
		context.TournamentChallongeParticipants.Add(new TournamentChallongeParticipant { TournamentId = tournament.Id, ChallongeParticipantId = "c2", PlayerId = p2.Id });
		await context.SaveChangesAsync();

		var (_, lifecycle, _, challonge, _) = CreateService(context);
		challonge.Participants = [new("c1", p1.Id.ToString(), 1), new("c2", p2.Id.ToString(), 2)];
		return (tournament, challonge, lifecycle);
	}

	[Fact]
	public async Task FinishAsync_AppliesPrizeBonusAndPlayedCounts()
	{
		await using var context = CreateContext();
		var (tournament, _, lifecycle) = await SeedStartedForFinishAsync(context);

		var result = await lifecycle.FinishAsync(tournament.Id);

		Assert.Equal(TournamentState.Complete, result!.State);
		Assert.Equal("p1", result.WinnerId);
		var p1 = await context.Players.SingleAsync(p => p.Uid == "p1");
		var p2 = await context.Players.SingleAsync(p => p.Uid == "p2");
		Assert.Equal(1040, p1.Elo); // seasonal (no override): +40 for 1st
		Assert.Equal(1020, p2.Elo); // +20 for 2nd
		Assert.Equal(1, p1.PlayedTournamentCount);
		Assert.Equal(1, p2.SeasonalPlayedTournamentCount);
	}

	[Fact]
	public async Task FinishAsync_AlreadyComplete_ThrowsWithoutReapplyingRewards()
	{
		await using var context = CreateContext();
		var (tournament, challonge, lifecycle) = await SeedStartedForFinishAsync(context);
		await lifecycle.FinishAsync(tournament.Id);

		await Assert.ThrowsAsync<TournamentLifecycleException>(() => lifecycle.FinishAsync(tournament.Id));

		var p1 = await context.Players.SingleAsync(p => p.Uid == "p1");
		Assert.Equal(1040, p1.Elo);
		Assert.Equal(1, p1.PlayedTournamentCount);
		Assert.Equal(1, challonge.FinalizeAttempts);
	}

	[Fact]
	public async Task FinishAsync_NotStarted_Throws()
	{
		await using var context = CreateContext();
		var tournament = await SeedTournamentAsync(context, TournamentState.BracketCreated, challongeTournamentId: "ctid");
		var (_, lifecycle, _, challonge, _) = CreateService(context);

		await Assert.ThrowsAsync<TournamentLifecycleException>(() => lifecycle.FinishAsync(tournament.Id));
		Assert.Equal(0, challonge.FinalizeAttempts);
	}

	[Fact]
	public async Task SplitAsync_GroupsDontCoverEveryApprovedPlayerExactlyOnce_Throws()
	{
		await using var context = CreateContext();
		var tournament = await SeedTournamentAsync(context);
		await SeedApprovedRegistrationAsync(context, tournament.Id, "p1");
		await SeedApprovedRegistrationAsync(context, tournament.Id, "p2");
		await SeedApprovedRegistrationAsync(context, tournament.Id, "p3");
		var (_, lifecycle, _, _, _) = CreateService(context);

		// p2 appears twice and p3's group is short — every approved player must appear exactly once.
		await Assert.ThrowsAsync<TournamentLifecycleException>(() =>
			lifecycle.SplitAsync(tournament.Id, new SplitTournamentRequest([["p1", "p2"], ["p2", "p3"]])));
	}

	private static UpdateTournamentRequest MakeUpdateRequest(string name = "Updated Cup") => new(
		name, "desc",
		DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1),
		DateTimeOffset.UtcNow.AddDays(2), DateTimeOffset.UtcNow.AddDays(3),
		100, 200, 5, 0, 1000,
		OverrideEloChange: 15,
		BracketType: "single elimination",
		ConsolationMatchesTargetRank: 3,
		Visible: false,
		DiscordRoleName: "role",
		DiscordChannelName: "channel");

	[Fact]
	public async Task UpdateAsync_WhenOpen_AppliesFieldsAndNotifies()
	{
		await using var context = CreateContext();
		var tournament = await SeedTournamentAsync(context);
		var (service, _, notifier, _, _) = CreateService(context);

		var result = await service.UpdateAsync(tournament.Id, MakeUpdateRequest());

		Assert.NotNull(result);
		Assert.Equal("Updated Cup", result!.Name);
		Assert.Equal(15, result.OverrideEloChange);
		Assert.Equal(3, result.ConsolationMatchesTargetRank);
		Assert.False(result.Visible);
		Assert.Equal("role", result.DiscordRoleName);
		Assert.Equal("channel", result.DiscordChannelName);
		Assert.Contains(tournament.Id, notifier.TournamentChanged);
	}

	[Fact]
	public async Task UpdateAsync_WhenLocked_Throws()
	{
		await using var context = CreateContext();
		var tournament = await SeedTournamentAsync(context, TournamentState.BracketCreated);
		var (service, _, _, _, _) = CreateService(context);

		await Assert.ThrowsAsync<TournamentLifecycleException>(() =>
			service.UpdateAsync(tournament.Id, MakeUpdateRequest()));
	}

	[Fact]
	public async Task UpdateAsync_UnknownTournament_ReturnsNull()
	{
		await using var context = CreateContext();
		var (service, _, _, _, _) = CreateService(context);

		var result = await service.UpdateAsync(Guid.NewGuid(), MakeUpdateRequest());

		Assert.Null(result);
	}

	[Fact]
	public async Task StartAsync_DiscordRoleCreationFailsTransiently_RetriesAndStillStarts()
	{
		await using var context = CreateContext();
		var tournament = await SeedTournamentAsync(context, TournamentState.BracketCreated, challongeTournamentId: "ctid");
		tournament.DiscordRoleName = "Cup Player"; // required for CreateResourcesAsync to attempt role creation at all
		await context.SaveChangesAsync();
		var (_, lifecycle, notifier, challonge, discord) = CreateService(context);
		// An unplayed match, same as a freshly-started real bracket — an empty match list would
		// vacuously look "fully played" to SyncFromChallongeAsync and jump straight to AwaitingReview.
		challonge.Matches = [new ChallongeMatchResult("555", 1, 1, "open", null, "c1", "c2")];

		// Reproduces the reported bug: Discord role creation fails on the first two attempts
		// (e.g. a transient Discord API blip) — the whole start sequence should retry and still
		// succeed, rather than leaving the tournament half-started.
		discord.CreateRoleFailuresRemaining = 2;

		var result = await lifecycle.StartAsync(tournament.Id);

		Assert.NotNull(result);
		Assert.Equal(TournamentState.Started, result!.State);
		Assert.Equal(0, discord.CreateRoleFailuresRemaining);
		var reloaded = await context.Tournaments.SingleAsync(t => t.Id == tournament.Id);
		Assert.Equal("role-1", reloaded.DiscordRoleId);
		Assert.Contains(tournament.Id, notifier.TournamentChanged);
	}

	[Fact]
	public async Task StartAsync_ChallongeKeepsFailing_ExhaustsRetriesAndLeavesTournamentUnstarted()
	{
		await using var context = CreateContext();
		var tournament = await SeedTournamentAsync(context, TournamentState.BracketCreated, challongeTournamentId: "ctid");
		var (_, lifecycle, notifier, challonge, _) = CreateService(context);

		// Far more failures than the retry budget allows — the whole sequence should give up
		// rather than retry forever, and must not have changed anything about the tournament.
		challonge.StartFailuresRemaining = 10;

		await Assert.ThrowsAsync<TournamentLifecycleException>(() => lifecycle.StartAsync(tournament.Id));

		var reloaded = await context.Tournaments.SingleAsync(t => t.Id == tournament.Id);
		Assert.Equal(TournamentState.BracketCreated, reloaded.State);
		Assert.Null(reloaded.DiscordRoleId);
		Assert.Empty(notifier.TournamentChanged);
	}

	[Theory]
	[InlineData(false, 1)]
	[InlineData(true, 2)]
	public async Task ListAsync_HiddenTournaments_OnlyIncludedWhenRequested(bool includeHidden, int expectedCount)
	{
		await using var context = CreateContext();
		await SeedTournamentAsync(context);
		var hidden = await SeedTournamentAsync(context);
		hidden.Visible = false;
		await context.SaveChangesAsync();
		var (service, _, _, _, _) = CreateService(context);

		var result = await service.ListAsync(includeHidden);

		Assert.Equal(expectedCount, result.Count);
		Assert.Equal(includeHidden, result.Any(t => t.Id == hidden.Id));
	}

	[Fact]
	public async Task ListPageAsync_ReturnsNewestFirstSliceWithTotalCount()
	{
		await using var context = CreateContext();
		var start = DateTimeOffset.UtcNow;
		var seeded = new List<Tournament>();
		for (var i = 0; i < 5; i++)
		{
			var t = await SeedTournamentAsync(context);
			t.TournamentStartDate = start.AddDays(i);
			seeded.Add(t);
		}
		var hidden = await SeedTournamentAsync(context);
		hidden.Visible = false;
		hidden.TournamentStartDate = start.AddDays(10);
		await context.SaveChangesAsync();
		var (service, _, _, _, _) = CreateService(context);

		var page2 = await service.ListPageAsync(includeHidden: false, page: 2, pageSize: 2);

		Assert.Equal(5, page2.TotalCount);
		Assert.Equal(2, page2.Page);
		Assert.Equal([seeded[2].Id, seeded[1].Id], page2.Items.Select(t => t.Id));

		var pastEnd = await service.ListPageAsync(includeHidden: false, page: 4, pageSize: 2);
		Assert.Empty(pastEnd.Items);
		Assert.Equal(5, pastEnd.TotalCount);

		var withHidden = await service.ListPageAsync(includeHidden: true, page: 1, pageSize: 2);
		Assert.Equal(6, withHidden.TotalCount);
		Assert.Equal(hidden.Id, withHidden.Items[0].Id);
	}

	[Fact]
	public async Task GetVisibilityAsync_ReturnsFlagOrNullWhenMissing()
	{
		await using var context = CreateContext();
		var hidden = await SeedTournamentAsync(context);
		hidden.Visible = false;
		await context.SaveChangesAsync();
		var repository = new TournamentRepository(context);

		Assert.False(await repository.GetVisibilityAsync(hidden.Id));
		Assert.Null(await repository.GetVisibilityAsync(Guid.NewGuid()));
	}
}
