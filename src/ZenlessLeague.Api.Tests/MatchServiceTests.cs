using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZenlessLeague.Api.Features.History;
using ZenlessLeague.Api.Features.Nexus;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Teams;
using ZenlessLeague.Api.Features.Tournaments;
using ZenlessLeague.Api.Features.Tournaments.Bracket;
using ZenlessLeague.Api.Features.Tournaments.Matches;
using ZenlessLeague.Api.Infrastructure.Persistence;

namespace ZenlessLeague.Api.Tests;

public class MatchServiceTests
{
	private static AppDbContext CreateContext() =>
		new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

	private static async Task<(Tournament Tournament, TournamentMatch Match, Player P1, Player P2)> SeedAsync(
		AppDbContext context, string matchState = "open", TournamentGameMode gameMode = TournamentGameMode.ShiyuDefense)
	{
		var p1 = TestPlayers.Create(context, "p1");
		var p2 = TestPlayers.Create(context, "p2");

		var tournament = new Tournament
		{
			Id = Guid.NewGuid(),
			Name = "Test Cup",
			RegistrationType = TournamentRegistrationType.Solo,
			GameMode = gameMode,
			State = TournamentState.Started,
			BracketType = "single elimination",
			OverrideEloChange = -1,
		};
		context.Tournaments.Add(tournament);

		context.TournamentParticipants.Add(new TournamentParticipant { Id = Guid.NewGuid(), TournamentId = tournament.Id, PlayerId = p1.Id, Slot = 0 });
		context.TournamentParticipants.Add(new TournamentParticipant { Id = Guid.NewGuid(), TournamentId = tournament.Id, PlayerId = p2.Id, Slot = 1 });

		// A 2-player single-elimination bracket: this match is the final.
		var match = new TournamentMatch
		{
			Id = Guid.NewGuid(),
			TournamentId = tournament.Id,
			Section = BracketSection.Winners,
			Round = 1,
			Position = 0,
			Number = 1,
			P1PlayerId = p1.Id,
			P2PlayerId = p2.Id,
			State = matchState,
		};
		context.TournamentMatches.Add(match);

		await context.SaveChangesAsync();
		return (tournament, match, p1, p2);
	}

	private static async Task<(Tournament Tournament, TournamentMatch Match, Team Team1, Team Team2)> SeedTeamAsync(AppDbContext context, string matchState = "open")
	{
		var p1a = TestPlayers.Create(context, "p1a");
		var p1b = TestPlayers.Create(context, "p1b");
		var p2a = TestPlayers.Create(context, "p2a");
		var p2b = TestPlayers.Create(context, "p2b");

		var team1 = new Team { Id = Guid.NewGuid(), Name = "Team One", Player1Id = p1a.Id, Player2Id = p1b.Id, CreatedAt = DateTimeOffset.UtcNow };
		var team2 = new Team { Id = Guid.NewGuid(), Name = "Team Two", Player1Id = p2a.Id, Player2Id = p2b.Id, CreatedAt = DateTimeOffset.UtcNow };
		context.Teams.Add(team1);
		context.Teams.Add(team2);

		var tournament = new Tournament
		{
			Id = Guid.NewGuid(),
			Name = "Test 2v2 Cup",
			RegistrationType = TournamentRegistrationType.Team,
			State = TournamentState.Started,
			BracketType = "single elimination",
			OverrideEloChange = -1,
		};
		context.Tournaments.Add(tournament);

		context.TournamentParticipants.Add(new TournamentParticipant { Id = Guid.NewGuid(), TournamentId = tournament.Id, TeamId = team1.Id, Slot = 0 });
		context.TournamentParticipants.Add(new TournamentParticipant { Id = Guid.NewGuid(), TournamentId = tournament.Id, TeamId = team2.Id, Slot = 1 });

		var match = new TournamentMatch
		{
			Id = Guid.NewGuid(),
			TournamentId = tournament.Id,
			Section = BracketSection.Winners,
			Round = 1,
			Position = 0,
			Number = 1,
			P1TeamId = team1.Id,
			P2TeamId = team2.Id,
			State = matchState,
		};
		context.TournamentMatches.Add(match);

		await context.SaveChangesAsync();
		return (tournament, match, team1, team2);
	}

	internal static (IMatchService Service, FakeTournamentNotifier Notifier) CreateService(
		AppDbContext context, FakeNexusClient? nexus = null, bool parseNexusResults = true)
	{
		var playerRepository = new PlayerRepository(context);
		var historyService = new HistoryService(playerRepository, new HistoryRepository(context), new TeamRepository(context), TestDiscord.CreateRoleAssignmentService(context), new TournamentRepository(context), NullLogger<HistoryService>.Instance);
		var notifier = new FakeTournamentNotifier();
		var service = new MatchService(
			new TournamentRepository(context),
			new TournamentMatchRepository(context),
			new TeamRepository(context),
			playerRepository,
			new BracketService(new TournamentRepository(context), new TournamentParticipantRepository(context), new TournamentMatchRepository(context), playerRepository, historyService, notifier, new EfUnitOfWork(context)),
			new NexusResultResolver(nexus ?? NexusFixtures.ClientWithSamples(), new TournamentRegistrationRepository(context), playerRepository, NullLogger<NexusResultResolver>.Instance),
			historyService,
			new FakeFirebaseStorageClient(),
			notifier,
			Options.Create(new NexusOptions { ParseResults = parseNexusResults }),
			new EfUnitOfWork(context));
		return (service, notifier);
	}

	// Solo registration carrying the player's Nexus nickname — what MatchService matches the
	// names on a Nexus result against.
	private static async Task RegisterAsync(AppDbContext context, Tournament tournament, Player player, string nexusNickname)
	{
		context.TournamentRegistrations.Add(new TournamentRegistration
		{
			Id = Guid.NewGuid(), TournamentId = tournament.Id, PlayerId = player.Id,
			Player1Details = new PlayerRegistrationDetails { GameUid = "g", DarteNickname = nexusNickname, DartePresetName = "p", RosterName = "r" },
			Approved = true, RegisteredAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
		});
		await context.SaveChangesAsync();
	}

	[Fact]
	public async Task SubmitPlayerResultAsync_NexusLink_BothApprove_FinalizesAndRegistersHistory()
	{
		await using var context = CreateContext();
		var (tournament, match, p1Player, p2Player) = await SeedAsync(context);
		// Shiyu sample: m_Debil (left) 4:54 vs shad1w (right) 3:43 — lower time wins.
		await RegisterAsync(context, tournament, p1Player, "shad1w");
		await RegisterAsync(context, tournament, p2Player, "m_Debil");
		var (service, notifier) = CreateService(context);


		// p1 submits the link — not both approved yet, match stays open.
		var afterP1 = await service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p1",
			new SubmitPlayerResultRequest(NexusResultUrl: NexusFixtures.Link(NexusFixtures.ShiyuFinishedId)));
		Assert.NotNull(afterP1);
		Assert.True(afterP1!.P1ApprovedResult);
		Assert.False(afterP1.P2ApprovedResult);
		Assert.NotEqual("complete", afterP1.State);
		Assert.Equal("03:43", afterP1.ResultP1); // p1 is shad1w = right side
		Assert.Equal("04:54", afterP1.ResultP2);
		Assert.Equal(NexusFixtures.ShiyuFinishedId, afterP1.NexusResultId);
		Assert.Empty(afterP1.NexusMissing!);

		// p2 confirms without re-sending anything — triggers finalize.
		var afterP2 = await service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p2", new SubmitPlayerResultRequest());
		Assert.NotNull(afterP2);
		Assert.Equal("complete", afterP2!.State);
		Assert.Equal("p1", afterP2.WinnerPlayerId); // 3:43 < 4:54

		Assert.Equal(TournamentState.AwaitingReview, (await context.Tournaments.SingleAsync()).State);

		var p1 = await context.Players.SingleAsync(p => p.Uid == "p1");
		Assert.Equal(1, p1.Wins);
		Assert.True(p1.TournamentPoints > 0);

		var history = await context.HistoryEntries.ToListAsync();
		Assert.Single(history);
		Assert.Equal(match.Id, history[0].TournamentMatchId);

		Assert.Contains((tournament.Id, match.Id), notifier.MatchChanged);
		Assert.Contains(tournament.Id, notifier.MatchesSynced);
	}

	[Fact]
	public async Task SubmitPlayerResultAsync_RealNexusDraftLink_UsesSeriesTotals()
	{
		await using var context = CreateContext();
		var (tournament, match, p1Player, p2Player) = await SeedAsync(context);
		await RegisterAsync(context, tournament, p1Player, "Ratper");
		await RegisterAsync(context, tournament, p2Player, "m_Debil");
		// Game 2's draft isn't served — the decided series' totals still cover it.
		var nexus = new FakeNexusClient();
		nexus.Results[NexusFixtures.RealId] = NexusFixtures.Real;
		var (service, _) = CreateService(context, nexus);

		var afterP1 = await service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p1",
			new SubmitPlayerResultRequest(NexusResultUrl: $"https://zzz.nexus/drafts/{NexusFixtures.RealId}?adminToken=cmwto087500ajx8vto2lbfkfy"));
		Assert.Equal("03:29", afterP1!.ResultP1);
		Assert.Equal("04:12", afterP1.ResultP2);
		Assert.Equal(NexusFixtures.RealId, afterP1.NexusResultId);

		var afterP2 = await service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p2", new SubmitPlayerResultRequest());
		Assert.Equal("complete", afterP2!.State);
		Assert.Equal("p1", afterP2.WinnerPlayerId);
	}

	[Fact]
	public async Task SubmitPlayerResultAsync_NonParticipant_ThrowsForbidden()
	{
		await using var context = CreateContext();
		var (tournament, match, _, _) = await SeedAsync(context);
		var (service, _) = CreateService(context);

		await Assert.ThrowsAsync<MatchForbiddenException>(() =>
			service.SubmitPlayerResultAsync(tournament.Id, match.Id, "someone-else",
				new SubmitPlayerResultRequest(NexusResultUrl: NexusFixtures.Link(NexusFixtures.ShiyuFinishedId))));
	}

	[Fact]
	public async Task SubmitPlayerResultAsync_DeadlyAssaultNexusLink_HigherScoreWins()
	{
		await using var context = CreateContext();
		var (tournament, match, p1Player, _) = await SeedAsync(context, gameMode: TournamentGameMode.DeadlyAssault);
		// DA sample: m_Debil (left) 81 110 vs shad1w (right) 41 110. Only p1 is recognizable —
		// that's enough to place both.
		await RegisterAsync(context, tournament, p1Player, "  M_DEBIL ");
		var (service, _) = CreateService(context);

		var afterP1 = await service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p1",
			new SubmitPlayerResultRequest(NexusResultUrl: NexusFixtures.Link(NexusFixtures.DeadlyAssaultId)));
		Assert.Equal("81110", afterP1!.ResultP1);
		Assert.Equal("41110", afterP1.ResultP2);

		var afterP2 = await service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p2", new SubmitPlayerResultRequest());

		Assert.Equal("complete", afterP2!.State);
		Assert.Equal("p1", afterP2.WinnerPlayerId); // 81110 > 41110
	}

	[Fact]
	public async Task SubmitPlayerResultAsync_SoloWithoutLinkAndNoStoredResult_Throws()
	{
		await using var context = CreateContext();
		var (tournament, match, _, _) = await SeedAsync(context);
		var (service, _) = CreateService(context);

		// Typed results/screenshots are no longer accepted from players in solo matches.
		await Assert.ThrowsAsync<MatchValidationException>(() =>
			service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p1", new SubmitPlayerResultRequest("1:00", "1:30")));
	}

	[Fact]
	public async Task SubmitPlayerResultAsync_UnmatchedNames_RequireSideThenUseIt()
	{
		await using var context = CreateContext();
		var (tournament, match, _, _) = await SeedAsync(context);
		var (service, _) = CreateService(context);
		var link = NexusFixtures.Link(NexusFixtures.ShiyuFinishedId);

		// Neither "p1" nor "p2" appears on the Nexus result.
		await Assert.ThrowsAsync<MatchValidationException>(() =>
			service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p1", new SubmitPlayerResultRequest(NexusResultUrl: link)));

		var afterP1 = await service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p1",
			new SubmitPlayerResultRequest(NexusResultUrl: link, NexusP1Side: "left"));
		Assert.Equal("04:54", afterP1!.ResultP1);
		Assert.Equal("03:43", afterP1.ResultP2);
	}

	[Fact]
	public async Task SubmitPlayerResultAsync_UnfinishedResult_CountsFinishedGameAndReportsMissing()
	{
		await using var context = CreateContext();
		var (tournament, match, p1Player, p2Player) = await SeedAsync(context);
		await RegisterAsync(context, tournament, p1Player, "m_Debil");
		await RegisterAsync(context, tournament, p2Player, "shad1w");
		var (service, _) = CreateService(context);

		var afterP1 = await service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p1",
			new SubmitPlayerResultRequest(NexusResultUrl: NexusFixtures.Link(NexusFixtures.ShiyuUnfinishedId)));

		Assert.Equal("01:23", afterP1!.ResultP1);
		Assert.Equal("02:22", afterP1.ResultP2);
		Assert.Equal(2, afterP1.NexusTotalGames);
		Assert.Equal(1, afterP1.NexusCountedGames);
		Assert.Equal([new NexusMissingDto(2, "p1"), new NexusMissingDto(2, "p2")], afterP1.NexusMissing);
	}

	[Fact]
	public async Task SubmitPlayerResultAsync_ChangedNexusResult_ResetsOpponentApproval()
	{
		await using var context = CreateContext();
		var (tournament, match, p1Player, p2Player) = await SeedAsync(context);
		await RegisterAsync(context, tournament, p1Player, "m_Debil");
		await RegisterAsync(context, tournament, p2Player, "shad1w");
		var nexus = NexusFixtures.ClientWithSamples();
		var (service, _) = CreateService(context, nexus);
		var link = NexusFixtures.Link(NexusFixtures.ShiyuUnfinishedId);

		// p1 submits the half-finished result; p2 resubmits the same link after game 2 was played.
		await service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p1", new SubmitPlayerResultRequest(NexusResultUrl: link));
		NexusFixtures.AddSeries(nexus, NexusFixtures.ShiyuUnfinishedId, "m_Debil", "shad1w", scoreBased: false, decided: true, (83, 142), (60, 70));
		var afterP2 = await service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p2", new SubmitPlayerResultRequest(NexusResultUrl: link));

		Assert.False(afterP2!.P1ApprovedResult);
		Assert.True(afterP2.P2ApprovedResult);
		Assert.Equal("02:23", afterP2.ResultP1);
		Assert.Equal("03:32", afterP2.ResultP2);
		Assert.NotEqual("complete", afterP2.State);
	}

	[Theory]
	[InlineData(TournamentGameMode.ShiyuDefense, NexusFixtures.DeadlyAssaultId)]
	[InlineData(TournamentGameMode.DeadlyAssault, NexusFixtures.ShiyuFinishedId)]
	public async Task SubmitPlayerResultAsync_WrongGameMode_Throws(TournamentGameMode mode, string resultId)
	{
		await using var context = CreateContext();
		var (tournament, match, _, _) = await SeedAsync(context, gameMode: mode);
		var (service, _) = CreateService(context);

		await Assert.ThrowsAsync<NexusResultException>(() =>
			service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p1",
				new SubmitPlayerResultRequest(NexusResultUrl: NexusFixtures.Link(resultId), NexusP1Side: "left")));
	}

	[Theory]
	[InlineData("https://example.com/results/cmvfrhirw0i2652vtboz37ace/obs")]
	[InlineData("https://nexus-shiyu.com/results/doesnotexist0000/obs")]
	public async Task SubmitPlayerResultAsync_BadOrUnknownLink_Throws(string link)
	{
		await using var context = CreateContext();
		var (tournament, match, _, _) = await SeedAsync(context);
		var (service, _) = CreateService(context);

		await Assert.ThrowsAsync<NexusResultException>(() =>
			service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p1", new SubmitPlayerResultRequest(NexusResultUrl: link, NexusP1Side: "left")));
	}

	[Fact]
	public async Task SubmitPlayerResultAsync_UnreadableNexusResponse_ThrowsFormatException()
	{
		await using var context = CreateContext();
		var (tournament, match, _, _) = await SeedAsync(context);
		var nexus = new FakeNexusClient();
		// Simulates Nexus renaming its fields — must surface as 502 ("report to the admins").
		nexus.Results["changedformat00"] = "{\"players\":{\"left\":\"a\",\"right\":\"b\"},\"games\":[]}";
		var (service, _) = CreateService(context, nexus);

		var ex = await Assert.ThrowsAsync<NexusFormatException>(() => service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p1",
			new SubmitPlayerResultRequest(NexusResultUrl: NexusFixtures.Link("changedformat00"), NexusP1Side: "left")));
		Assert.Equal(502, ex.StatusCode);
	}

	[Fact]
	public async Task SubmitPlayerResultAsync_NoFinishedGameOrTie_Throws()
	{
		await using var context = CreateContext();
		var (tournament, match, _, _) = await SeedAsync(context);
		var nexus = new FakeNexusClient();
		NexusFixtures.AddSeries(nexus, "nothingplayed00", "a", "b", scoreBased: false, decided: false, (null, null), (null, null));
		NexusFixtures.AddSeries(nexus, "tiedresult00000", "a", "b", scoreBased: false, decided: true, (60, 120), (120, 60));
		var (service, _) = CreateService(context, nexus);

		await Assert.ThrowsAsync<MatchValidationException>(() => service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p1",
			new SubmitPlayerResultRequest(NexusResultUrl: NexusFixtures.Link("nothingplayed00"), NexusP1Side: "left")));
		await Assert.ThrowsAsync<MatchValidationException>(() => service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p1",
			new SubmitPlayerResultRequest(NexusResultUrl: NexusFixtures.Link("tiedresult00000"), NexusP1Side: "left")));
	}

	// ---- NexusOptions.ParseResults off (fallback): typed result + link stored as proof ----

	[Fact]
	public async Task SubmitPlayerResultAsync_ParsingOff_TypedResultWithNexusLink_BothApprove_FinalizesAndRegistersHistory()
	{
		await using var context = CreateContext();
		var (tournament, match, _, _) = await SeedAsync(context);
		var (service, notifier) = CreateService(context, parseNexusResults: false);


		// p1 submits typed times + the link — not both approved yet, match stays open.
		var afterP1 = await service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p1",
			new SubmitPlayerResultRequest(" 03:43 ", "04:54", NexusResultUrl: NexusFixtures.Link(NexusFixtures.ShiyuFinishedId)));
		Assert.NotNull(afterP1);
		Assert.True(afterP1!.P1ApprovedResult);
		Assert.False(afterP1.P2ApprovedResult);
		Assert.NotEqual("complete", afterP1.State);
		Assert.Equal("03:43", afterP1.ResultP1);
		Assert.Equal("04:54", afterP1.ResultP2);
		Assert.Equal(NexusFixtures.ShiyuFinishedId, afterP1.NexusResultId);
		Assert.Null(afterP1.NexusMissing); // not fetched, so nothing parsed

		// p2 confirms the same values without re-sending the link — triggers finalize.
		var afterP2 = await service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p2", new SubmitPlayerResultRequest("03:43", "04:54"));
		Assert.NotNull(afterP2);
		Assert.Equal("complete", afterP2!.State);
		Assert.Equal("p1", afterP2.WinnerPlayerId); // 3:43 < 4:54
		Assert.Equal(NexusFixtures.ShiyuFinishedId, afterP2.NexusResultId);

		Assert.Equal(TournamentState.AwaitingReview, (await context.Tournaments.SingleAsync()).State);

		var p1 = await context.Players.SingleAsync(p => p.Uid == "p1");
		Assert.Equal(1, p1.Wins);
		Assert.True(p1.TournamentPoints > 0);

		var history = await context.HistoryEntries.ToListAsync();
		Assert.Single(history);
		Assert.Equal(match.Id, history[0].TournamentMatchId);

		Assert.Contains((tournament.Id, match.Id), notifier.MatchChanged);
		Assert.Contains(tournament.Id, notifier.MatchesSynced);
	}

	[Fact]
	public async Task SubmitPlayerResultAsync_ParsingOff_DeadlyAssault_HigherScoreWins()
	{
		await using var context = CreateContext();
		var (tournament, match, _, _) = await SeedAsync(context, gameMode: TournamentGameMode.DeadlyAssault);
		var (service, _) = CreateService(context, parseNexusResults: false);

		await service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p1",
			new SubmitPlayerResultRequest("81110", "41110", NexusResultUrl: NexusFixtures.Link(NexusFixtures.DeadlyAssaultId)));
		var afterP2 = await service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p2", new SubmitPlayerResultRequest("81110", "41110"));

		Assert.Equal("complete", afterP2!.State);
		Assert.Equal("p1", afterP2.WinnerPlayerId); // 81110 > 41110
	}

	[Fact]
	public async Task SubmitPlayerResultAsync_ParsingOff_SoloWithoutLinkOrResult_Throws()
	{
		await using var context = CreateContext();
		var (tournament, match, _, _) = await SeedAsync(context);
		var (service, _) = CreateService(context, parseNexusResults: false);

		// The link is the proof for solo matches, so typed values alone aren't enough...
		await Assert.ThrowsAsync<MatchValidationException>(() =>
			service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p1", new SubmitPlayerResultRequest("1:00", "1:30")));
		// ...and neither is a link without typed values.
		await Assert.ThrowsAsync<MatchValidationException>(() =>
			service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p1",
				new SubmitPlayerResultRequest(NexusResultUrl: NexusFixtures.Link(NexusFixtures.ShiyuFinishedId))));
	}

	[Fact]
	public async Task SubmitPlayerResultAsync_ParsingOff_ChangedResultOrLink_ResetsOpponentApproval()
	{
		await using var context = CreateContext();
		var (tournament, match, _, _) = await SeedAsync(context);
		var (service, _) = CreateService(context, parseNexusResults: false);
		var link = NexusFixtures.Link(NexusFixtures.ShiyuFinishedId);

		await service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p1", new SubmitPlayerResultRequest("03:43", "04:54", NexusResultUrl: link));
		var afterTimeChange = await service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p2", new SubmitPlayerResultRequest("03:43", "04:50"));

		Assert.False(afterTimeChange!.P1ApprovedResult);
		Assert.True(afterTimeChange.P2ApprovedResult);
		Assert.Equal("04:50", afterTimeChange.ResultP2);
		Assert.NotEqual("complete", afterTimeChange.State);

		// p1 confirms with a different link — the link change resets p2's confirmation.
		var afterLinkChange = await service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p1",
			new SubmitPlayerResultRequest("03:43", "04:50", NexusResultUrl: NexusFixtures.Link(NexusFixtures.ShiyuUnfinishedId)));

		Assert.True(afterLinkChange!.P1ApprovedResult);
		Assert.False(afterLinkChange.P2ApprovedResult);
		Assert.Equal(NexusFixtures.ShiyuUnfinishedId, afterLinkChange.NexusResultId);
	}

	[Fact]
	public async Task SubmitPlayerResultAsync_ParsingOff_NotANexusLink_Throws()
	{
		await using var context = CreateContext();
		var (tournament, match, _, _) = await SeedAsync(context);
		var (service, _) = CreateService(context, parseNexusResults: false);

		await Assert.ThrowsAsync<NexusResultException>(() =>
			service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p1",
				new SubmitPlayerResultRequest("03:43", "04:54", NexusResultUrl: "https://example.com/results/cmvfrhirw0i2652vtboz37ace/obs")));
	}

	[Fact]
	public async Task SubmitPlayerResultAsync_ParsingOff_UnknownNexusResult_IsStoredWithoutFetching()
	{
		await using var context = CreateContext();
		var (tournament, match, _, _) = await SeedAsync(context);
		// Empty fake client: any fetch would fail with "not found".
		var (service, _) = CreateService(context, new FakeNexusClient(), parseNexusResults: false);

		var afterP1 = await service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p1",
			new SubmitPlayerResultRequest("03:43", "04:54", NexusResultUrl: "https://nexus-shiyu.com/results/doesnotexist0000/obs"));

		Assert.Equal("doesnotexist0000", afterP1!.NexusResultId);
	}

	[Fact]
	public async Task SetAdminResultAsync_ParsingOff_NexusLink_StoredAsProofTypedResultsUsed()
	{
		await using var context = CreateContext();
		var (tournament, match, _, _) = await SeedAsync(context);
		var (service, _) = CreateService(context, new FakeNexusClient(), parseNexusResults: false);

		var result = await service.SetAdminResultAsync(tournament.Id, match.Id,
			new SetAdminResultRequest("05:00", "03:43", NexusResultUrl: NexusFixtures.Link(NexusFixtures.ShiyuFinishedId)));

		Assert.Equal("complete", result!.State);
		Assert.Equal("05:00", result.ResultP1);
		Assert.Equal("03:43", result.ResultP2);
		Assert.Equal("p2", result.WinnerPlayerId);
		Assert.Equal(NexusFixtures.ShiyuFinishedId, result.NexusResultId);
	}

	[Fact]
	public async Task PreviewNexusResultAsync_ReturnsMappedResultWithoutSaving()
	{
		await using var context = CreateContext();
		var (tournament, match, _, p2Player) = await SeedAsync(context);
		await RegisterAsync(context, tournament, p2Player, "m_Debil");
		var (service, _) = CreateService(context);

		var preview = await service.PreviewNexusResultAsync(tournament.Id, match.Id, "p2",
			new NexusPreviewRequest(NexusFixtures.Link(NexusFixtures.ShiyuUnfinishedId)));

		Assert.NotNull(preview);
		Assert.False(preview!.P1IsLeft); // p2 is m_Debil = left
		Assert.Equal("02:22", preview.ResultP1);
		Assert.Equal("01:23", preview.ResultP2);
		Assert.Equal(1, preview.CountedGames);
		Assert.Equal([new NexusMissingDto(2, "p2"), new NexusMissingDto(2, "p1")], preview.Missing);

		var stored = await context.TournamentMatches.SingleAsync(m => m.Id == match.Id);
		Assert.Null(stored.NexusResultId);
		Assert.Null(stored.ResultP1);
	}

	[Fact]
	public async Task PreviewNexusResultAsync_UnmatchedNames_ReturnsUnknownSides()
	{
		await using var context = CreateContext();
		var (tournament, match, _, _) = await SeedAsync(context);
		var (service, _) = CreateService(context);

		var preview = await service.PreviewNexusResultAsync(tournament.Id, match.Id, "p1",
			new NexusPreviewRequest(NexusFixtures.Link(NexusFixtures.ShiyuFinishedId)));

		Assert.Null(preview!.P1IsLeft);
		Assert.Null(preview.ResultP1);
		Assert.Equal("04:54", preview.LeftTotal);
		Assert.Equal("03:43", preview.RightTotal);
	}

	[Fact]
	public async Task PreviewNexusResultAsync_NonParticipant_ThrowsForbidden()
	{
		await using var context = CreateContext();
		var (tournament, match, _, _) = await SeedAsync(context);
		TestPlayers.Create(context, "outsider");
		await context.SaveChangesAsync();
		var (service, _) = CreateService(context);

		await Assert.ThrowsAsync<MatchForbiddenException>(() => service.PreviewNexusResultAsync(tournament.Id, match.Id, "outsider",
			new NexusPreviewRequest(NexusFixtures.Link(NexusFixtures.ShiyuFinishedId))));
	}

	[Fact]
	public async Task SetAdminResultAsync_NexusLink_TypedResultsWinOverLink()
	{
		await using var context = CreateContext();
		var (tournament, match, _, _) = await SeedAsync(context);
		var (service, _) = CreateService(context);

		// Link says p1 (left) 04:54 vs p2 03:43; the moderator corrects p1's time to 05:00.
		var result = await service.SetAdminResultAsync(tournament.Id, match.Id,
			new SetAdminResultRequest(ResultP1: "05:00", NexusResultUrl: NexusFixtures.Link(NexusFixtures.ShiyuFinishedId), NexusP1Side: "left"));

		Assert.Equal("complete", result!.State);
		Assert.Equal("05:00", result.ResultP1);
		Assert.Equal("03:43", result.ResultP2);
		Assert.Equal("p2", result.WinnerPlayerId);
		Assert.Equal(NexusFixtures.ShiyuFinishedId, result.NexusResultId);
	}

	[Fact]
	public async Task SetAdminResultAsync_DeadlyAssault_HigherScoreWins()
	{
		await using var context = CreateContext();
		var (tournament, match, _, _) = await SeedAsync(context, gameMode: TournamentGameMode.DeadlyAssault);
		var (service, _) = CreateService(context);

		var result = await service.SetAdminResultAsync(tournament.Id, match.Id, new SetAdminResultRequest("2000", "1000"));

		Assert.Equal("p1", result!.WinnerPlayerId); // 2000 > 1000
	}

	[Fact]
	public async Task SetAdminResultAsync_TechLoss_WinnerIsOpponentOfTechLossPlayer()
	{
		await using var context = CreateContext();
		var (tournament, match, _, _) = await SeedAsync(context);
		var (service, notifier) = CreateService(context);


		var result = await service.SetAdminResultAsync(tournament.Id, match.Id, new SetAdminResultRequest(TechLossPlayerId: "p2"));

		Assert.NotNull(result);
		Assert.Equal("p1", result!.WinnerPlayerId);
		Assert.Equal("p2", result.TechLossPlayerId);
		Assert.Equal("complete", result.State);

		var p1 = await context.Players.SingleAsync(p => p.Uid == "p1");
		var p2 = await context.Players.SingleAsync(p => p.Uid == "p2");
		Assert.Equal(0, p1.TournamentPoints); // winner gets 0 change on a tech loss
		Assert.True(p2.TournamentPoints < 0);
	}

	[Fact]
	public async Task SetAdminResultAsync_TechLossAfterTypedResult_HistoryEntryIsTaggedTechLoss()
	{
		await using var context = CreateContext();
		var (tournament, match, _, _) = await SeedAsync(context);
		var (service, _) = CreateService(context);


		await service.SetAdminResultAsync(tournament.Id, match.Id, new SetAdminResultRequest("01:00", "02:00"));
		await service.SetAdminResultAsync(tournament.Id, match.Id, new SetAdminResultRequest(TechLossPlayerId: "p1"));

		var stored = await context.TournamentMatches.SingleAsync(m => m.Id == match.Id);
		var entry = await context.HistoryEntries.SingleAsync(e => e.Id == stored.HistoryEntryId);
		Assert.Equal(TournamentMatchKind.TechLoss, entry.Kind);
	}

	[Fact]
	public async Task SetAdminResultAsync_Override_ReversesOldHistoryBeforeRegisteringNew()
	{
		await using var context = CreateContext();
		var (tournament, match, _, _) = await SeedAsync(context, matchState: "complete");
		var (service, notifier) = CreateService(context);

		// First result: p1 wins.
		var first = await service.SetAdminResultAsync(tournament.Id, match.Id, new SetAdminResultRequest("1:00", "1:30"));
		Assert.Equal("p1", first!.WinnerPlayerId);
		var p1AfterFirst = await context.Players.SingleAsync(p => p.Uid == "p1");
		Assert.Equal(1, p1AfterFirst.Wins);

		// Admin corrects it: p2 actually won — the old history entry should be reversed first.
		var corrected = await service.SetAdminResultAsync(tournament.Id, match.Id, new SetAdminResultRequest("2:00", "1:30"));
		Assert.Equal("p2", corrected!.WinnerPlayerId);

		var p1AfterCorrection = await context.Players.SingleAsync(p => p.Uid == "p1");
		var p2AfterCorrection = await context.Players.SingleAsync(p => p.Uid == "p2");
		Assert.Equal(0, p1AfterCorrection.Wins); // reversed back to 0, then not re-incremented (p1 lost this time)
		Assert.Equal(1, p1AfterCorrection.Losses);
		Assert.Equal(1, p2AfterCorrection.Wins);

		// Exactly one live history entry should remain (the reversed one was deleted).
		Assert.Single(await context.HistoryEntries.ToListAsync());
	}

	[Fact]
	public async Task SubmitPlayerResultAsync_TeamMember_CanApproveOnBehalfOfTeam()
	{
		await using var context = CreateContext();
		var (tournament, match, team1, team2) = await SeedTeamAsync(context);
		var (service, _) = CreateService(context);

		// p1b (team1's second player, not the creator) approves on behalf of team1.
		var afterTeam1 = await service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p1b", new SubmitPlayerResultRequest("1:00", "1:30"));
		Assert.True(afterTeam1!.P1ApprovedResult);
		Assert.False(afterTeam1.P2ApprovedResult);

		// p2a (team2's creator) approves — triggers finalize.
		var afterTeam2 = await service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p2a", new SubmitPlayerResultRequest("1:00", "1:30"));
		Assert.Equal("complete", afterTeam2!.State);
		Assert.Equal(team1.Id, afterTeam2.WinnerTeamId);
		Assert.Null(afterTeam2.WinnerPlayerId);

		// All four players get the same team-derived change, and both go to the winning/losing side uniformly.
		var p1a = await context.Players.SingleAsync(p => p.Uid == "p1a");
		var p1b = await context.Players.SingleAsync(p => p.Uid == "p1b");
		var p2a = await context.Players.SingleAsync(p => p.Uid == "p2a");
		var p2b = await context.Players.SingleAsync(p => p.Uid == "p2b");
		Assert.Equal(p1a.TournamentPoints, p1b.TournamentPoints);
		Assert.Equal(p2a.TournamentPoints, p2b.TournamentPoints);
		Assert.True(p1a.TournamentPoints > 0);
		Assert.True(p2a.TournamentPoints < 0);
		Assert.Equal(1, p1a.Wins);
		Assert.Equal(1, p2a.Losses);

		// Two "lane" history entries (creator-vs-creator, player2-vs-player2), both referenced by the match.
		var history = await context.HistoryEntries.Where(h => h.TournamentMatchId == match.Id).ToListAsync();
		Assert.Equal(2, history.Count);

		var reloadedMatch = await context.TournamentMatches.SingleAsync(m => m.Id == match.Id);
		Assert.NotNull(reloadedMatch.HistoryEntryId);
		Assert.NotNull(reloadedMatch.HistoryEntryId2);
		Assert.NotEqual(reloadedMatch.HistoryEntryId, reloadedMatch.HistoryEntryId2);
	}

	[Fact]
	public async Task SubmitPlayerResultAsync_NonTeamMember_ThrowsForbidden()
	{
		await using var context = CreateContext();
		var (tournament, match, _, _) = await SeedTeamAsync(context);
		var (service, _) = CreateService(context);

		await Assert.ThrowsAsync<MatchForbiddenException>(() =>
			service.SubmitPlayerResultAsync(tournament.Id, match.Id, "someone-else", new SubmitPlayerResultRequest("1:00", "1:30")));
	}

	[Fact]
	public async Task SetAdminResultAsync_Unranked_RecordsZeroChangeButCountsWinLoss()
	{
		await using var context = CreateContext();
		var (tournament, match, _, _) = await SeedAsync(context);
		tournament.IsRanked = false;
		await context.SaveChangesAsync();
		var (service, _) = CreateService(context);

		var result = await service.SetAdminResultAsync(tournament.Id, match.Id, new SetAdminResultRequest("01:00", "02:00"));

		Assert.Equal("p1", result!.WinnerPlayerId);
		var stored = await context.TournamentMatches.SingleAsync(m => m.Id == match.Id);
		var entry = await context.HistoryEntries.SingleAsync(e => e.Id == stored.HistoryEntryId);
		Assert.Equal(0, entry.P1Change);
		Assert.Equal(0, entry.P2Change);
		Assert.True(entry.P1Won);

		var p1 = await context.Players.SingleAsync(p => p.Uid == "p1");
		var p2 = await context.Players.SingleAsync(p => p.Uid == "p2");
		Assert.Equal(0, p1.TournamentPoints);
		Assert.Equal(0, p2.TournamentPoints);
		Assert.Equal(1, p1.Wins);
		Assert.Equal(1, p2.Losses);
	}

	[Fact]
	public async Task SetAdminResultAsync_UnrankedTechLoss_LoserLosesNoPoints()
	{
		await using var context = CreateContext();
		var (tournament, match, _, _) = await SeedAsync(context);
		tournament.IsRanked = false;
		await context.SaveChangesAsync();
		var (service, _) = CreateService(context);

		await service.SetAdminResultAsync(tournament.Id, match.Id, new SetAdminResultRequest(TechLossPlayerId: "p2"));

		var p1 = await context.Players.SingleAsync(p => p.Uid == "p1");
		var p2 = await context.Players.SingleAsync(p => p.Uid == "p2");
		Assert.Equal(0, p1.TournamentPoints);
		Assert.Equal(0, p2.TournamentPoints);
		Assert.Equal(1, p2.Losses);
	}

	[Fact]
	public async Task SetAdminResultAsync_UnrankedTeam_RecordsZeroChangeForBothLanes()
	{
		await using var context = CreateContext();
		var (tournament, match, team1, _) = await SeedTeamAsync(context);
		tournament.IsRanked = false;
		await context.SaveChangesAsync();
		var (service, _) = CreateService(context);

		var result = await service.SetAdminResultAsync(tournament.Id, match.Id, new SetAdminResultRequest("01:00", "02:00"));

		Assert.Equal(team1.Id, result!.WinnerTeamId);
		var entries = await context.HistoryEntries.ToListAsync();
		Assert.Equal(2, entries.Count);
		Assert.All(entries, e => Assert.Equal((0, 0), (e.P1Change, e.P2Change!.Value)));
		Assert.All(await context.Players.ToListAsync(), p => Assert.Equal(0, p.TournamentPoints));
		Assert.Equal(1, (await context.Players.SingleAsync(p => p.Uid == "p1b")).Wins);
		Assert.Equal(1, (await context.Players.SingleAsync(p => p.Uid == "p2b")).Losses);
	}

	[Fact]
	public async Task SetAdminResultAsync_TeamTechLoss_WinnerIsOpponentTeam()
	{
		await using var context = CreateContext();
		var (tournament, match, team1, team2) = await SeedTeamAsync(context);
		var (service, _) = CreateService(context);

		var result = await service.SetAdminResultAsync(tournament.Id, match.Id, new SetAdminResultRequest(TechLossTeamId: team2.Id));

		Assert.NotNull(result);
		Assert.Equal(team1.Id, result!.WinnerTeamId);
		Assert.Equal(team2.Id, result.TechLossTeamId);
		Assert.Null(result.TechLossPlayerId);
		Assert.Equal("complete", result.State);

		var p1a = await context.Players.SingleAsync(p => p.Uid == "p1a");
		var p2a = await context.Players.SingleAsync(p => p.Uid == "p2a");
		Assert.Equal(0, p1a.TournamentPoints); // winning team gets 0 change on a tech loss
		Assert.True(p2a.TournamentPoints < 0);
	}

	[Fact]
	public async Task SetAdminResultAsync_TeamOverride_ReversesBothOldHistoryEntriesBeforeRegisteringNew()
	{
		await using var context = CreateContext();
		var (tournament, match, team1, team2) = await SeedTeamAsync(context, matchState: "complete");
		var (service, _) = CreateService(context);

		var first = await service.SetAdminResultAsync(tournament.Id, match.Id, new SetAdminResultRequest("1:00", "1:30"));
		Assert.Equal(team1.Id, first!.WinnerTeamId);
		Assert.Equal(2, await context.HistoryEntries.CountAsync());

		// Admin corrects it: team2 actually won.
		var corrected = await service.SetAdminResultAsync(tournament.Id, match.Id, new SetAdminResultRequest("2:00", "1:30"));
		Assert.Equal(team2.Id, corrected!.WinnerTeamId);

		var p1a = await context.Players.SingleAsync(p => p.Uid == "p1a");
		var p2a = await context.Players.SingleAsync(p => p.Uid == "p2a");
		Assert.Equal(0, p1a.Wins);
		Assert.Equal(1, p1a.Losses);
		Assert.Equal(1, p2a.Wins);

		// The two reversed entries from the first result were deleted; exactly the two new ones remain.
		Assert.Equal(2, await context.HistoryEntries.CountAsync());
	}
}
