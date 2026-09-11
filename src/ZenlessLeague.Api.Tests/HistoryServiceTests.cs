using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.History;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Teams;
using ZenlessLeague.Api.Features.Tournaments;

namespace ZenlessLeague.Api.Tests;

public class HistoryServiceTests
{
	private static AppDbContext CreateContext() =>
		new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

	private static async Task<Player> SeedPlayerAsync(AppDbContext context, string uid, int elo = 1000, bool midConfirmed = false)
	{
		var player = TestPlayers.Create(context, uid, elo, midConfirmed);
		await context.SaveChangesAsync();
		return player;
	}

	private static HistoryService CreateService(AppDbContext context) =>
		new(new PlayerRepository(context), new HistoryRepository(context), new TeamRepository(context),
			TestDiscord.CreateRoleAssignmentService(context), new TournamentRepository(context));

	[Fact]
	public async Task RegisterMatchResult_ComputesEloAndUpdatesCounters()
	{
		await using var context = CreateContext();
		var p1 = await SeedPlayerAsync(context, "p1");
		var p2 = await SeedPlayerAsync(context, "p2");
		var service = CreateService(context);

		var entry = await service.RegisterMatchResultAsync(p1.Id, p2.Id, p1Win: true, overrideEloChange: -1);

		Assert.Equal(25, entry.P1Change);
		Assert.Equal(-22, entry.P2Change);

		var p1Reloaded = await context.Players.SingleAsync(p => p.Uid == "p1");
		var p2Reloaded = await context.Players.SingleAsync(p => p.Uid == "p2");
		Assert.Equal(25, p1Reloaded.TournamentPoints);
		Assert.Equal(1, p1Reloaded.Wins);
		Assert.Equal(0, p1Reloaded.Losses);
		Assert.Equal(-22, p2Reloaded.TournamentPoints);
		Assert.Equal(1, p2Reloaded.Losses);
		Assert.NotNull(p1Reloaded.LastPlayedTournamentTimestamp);
	}

	[Fact]
	public async Task RegisterMatchResult_WithOverride_UsesFixedDelta()
	{
		await using var context = CreateContext();
		var p1 = await SeedPlayerAsync(context, "p1");
		var p2 = await SeedPlayerAsync(context, "p2");
		var service = CreateService(context);

		var entry = await service.RegisterMatchResultAsync(p1.Id, p2.Id, p1Win: true, overrideEloChange: 10);

		Assert.Equal(10, entry.P1Change);
		Assert.Equal(-10, entry.P2Change);
	}

	[Fact]
	public async Task RegisterMatchResult_TechLoss_WinnerGetsZeroChange()
	{
		await using var context = CreateContext();
		var p1 = await SeedPlayerAsync(context, "p1");
		var p2 = await SeedPlayerAsync(context, "p2");
		var service = CreateService(context);

		// p1 wins by tech loss of p2 — p1 (winner) gets 0, p2 (loser) gets the "as if lost normally" change.
		var entry = await service.RegisterMatchResultAsync(p1.Id, p2.Id, p1Win: true, overrideEloChange: -1, techLoss: true);

		Assert.Equal(0, entry.P1Change);
		Assert.Equal(-22, entry.P2Change);
		Assert.Equal(TournamentMatchKind.TechLoss, entry.Kind);
	}

	[Fact]
	public async Task ReverseMatchResult_RestoresOriginalPlayerState()
	{
		await using var context = CreateContext();
		var p1 = await SeedPlayerAsync(context, "p1");
		var p2 = await SeedPlayerAsync(context, "p2");
		var service = CreateService(context);

		var entry = await service.RegisterMatchResultAsync(p1.Id, p2.Id, p1Win: true, overrideEloChange: -1);
		await service.ReverseEntryAsync(entry.Id);

		var p1Reloaded = await context.Players.SingleAsync(p => p.Uid == "p1");
		var p2Reloaded = await context.Players.SingleAsync(p => p.Uid == "p2");
		Assert.Equal(0, p1Reloaded.TournamentPoints);
		Assert.Equal(0, p1Reloaded.Wins);
		Assert.Equal(0, p2Reloaded.TournamentPoints);
		Assert.Equal(0, p2Reloaded.Losses);
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
		var p1 = await SeedPlayerAsync(context, "p1");
		var service = CreateService(context);

		await Assert.ThrowsAsync<HistoryValidationException>(() =>
			service.RegisterMatchResultAsync(p1.Id, Guid.NewGuid(), p1Win: true, overrideEloChange: -1));
	}

	private static async Task<(Team Team1, Team Team2)> SeedTeamsAsync(AppDbContext context)
	{
		var p1a = await SeedPlayerAsync(context, "p1a");
		var p1b = await SeedPlayerAsync(context, "p1b");
		var p2a = await SeedPlayerAsync(context, "p2a");
		var p2b = await SeedPlayerAsync(context, "p2b");
		var team1 = new Team { Id = Guid.NewGuid(), Name = "Team One", Player1Id = p1a.Id, Player2Id = p1b.Id, CreatedAt = DateTimeOffset.UtcNow };
		var team2 = new Team { Id = Guid.NewGuid(), Name = "Team Two", Player1Id = p2a.Id, Player2Id = p2b.Id, CreatedAt = DateTimeOffset.UtcNow };
		context.Teams.Add(team1);
		context.Teams.Add(team2);
		await context.SaveChangesAsync();
		return (team1, team2);
	}

	[Fact]
	public async Task RegisterTeamMatchResult_SameChangeAppliedToAllFourPlayers()
	{
		await using var context = CreateContext();
		var (team1, team2) = await SeedTeamsAsync(context);
		var service = CreateService(context);

		var (entry1, entry2) = await service.RegisterTeamMatchResultAsync(
			team1.Id, team2.Id, p1Win: true, overrideEloChange: -1,
			tournamentId: Guid.NewGuid(), tournamentMatchId: Guid.NewGuid(), kind: TournamentMatchKind.None);

		// Equal-rated teams with no mid-confirm on either side -> same 25/-22 split as the solo test.
		Assert.Equal(25, entry1.P1Change);
		Assert.Equal(-22, entry1.P2Change);
		Assert.Equal(entry1.P1Change, entry2.P1Change);
		Assert.Equal(entry1.P2Change, entry2.P2Change);

		var p1a = await context.Players.SingleAsync(p => p.Uid == "p1a");
		var p1b = await context.Players.SingleAsync(p => p.Uid == "p1b");
		var p2a = await context.Players.SingleAsync(p => p.Uid == "p2a");
		var p2b = await context.Players.SingleAsync(p => p.Uid == "p2b");
		Assert.Equal(25, p1a.TournamentPoints);
		Assert.Equal(25, p1b.TournamentPoints);
		Assert.Equal(-22, p2a.TournamentPoints);
		Assert.Equal(-22, p2b.TournamentPoints);
		Assert.Equal(1, p1a.Wins);
		Assert.Equal(1, p1b.Wins);
		Assert.Equal(1, p2a.Losses);
		Assert.Equal(1, p2b.Losses);
	}

	[Fact]
	public async Task RegisterTeamMatchResult_UsesAverageOfBothMembersEffectiveRating()
	{
		await using var context = CreateContext();
		// team1 averages to 1000 elo (800+1200)/2; team2 is flat 1000 -> same result as the equal-rated case.
		var p1a = await SeedPlayerAsync(context, "p1a", elo: 800);
		var p1b = await SeedPlayerAsync(context, "p1b", elo: 1200);
		var p2a = await SeedPlayerAsync(context, "p2a", elo: 1000);
		var p2b = await SeedPlayerAsync(context, "p2b", elo: 1000);
		var team1 = new Team { Id = Guid.NewGuid(), Name = "Team One", Player1Id = p1a.Id, Player2Id = p1b.Id, CreatedAt = DateTimeOffset.UtcNow };
		var team2 = new Team { Id = Guid.NewGuid(), Name = "Team Two", Player1Id = p2a.Id, Player2Id = p2b.Id, CreatedAt = DateTimeOffset.UtcNow };
		context.Teams.Add(team1);
		context.Teams.Add(team2);
		await context.SaveChangesAsync();
		var service = CreateService(context);

		var (entry1, _) = await service.RegisterTeamMatchResultAsync(
			team1.Id, team2.Id, p1Win: true, overrideEloChange: -1,
			tournamentId: Guid.NewGuid(), tournamentMatchId: Guid.NewGuid(), kind: TournamentMatchKind.None);

		Assert.Equal(25, entry1.P1Change);
		Assert.Equal(-22, entry1.P2Change);
	}

	[Fact]
	public async Task RegisterTeamMatchResult_WithOverride_UsesFixedDeltaForAllFour()
	{
		await using var context = CreateContext();
		var (team1, team2) = await SeedTeamsAsync(context);
		var service = CreateService(context);

		var (entry1, entry2) = await service.RegisterTeamMatchResultAsync(
			team1.Id, team2.Id, p1Win: true, overrideEloChange: 10,
			tournamentId: Guid.NewGuid(), tournamentMatchId: Guid.NewGuid(), kind: TournamentMatchKind.None);

		Assert.Equal(10, entry1.P1Change);
		Assert.Equal(-10, entry1.P2Change);
		Assert.Equal(10, entry2.P1Change);
		Assert.Equal(-10, entry2.P2Change);
	}

	[Fact]
	public async Task RegisterTeamMatchResult_TechLoss_WinningTeamGetsZeroChange()
	{
		await using var context = CreateContext();
		var (team1, team2) = await SeedTeamsAsync(context);
		var service = CreateService(context);

		var (entry1, entry2) = await service.RegisterTeamMatchResultAsync(
			team1.Id, team2.Id, p1Win: true, overrideEloChange: -1,
			tournamentId: Guid.NewGuid(), tournamentMatchId: Guid.NewGuid(), kind: TournamentMatchKind.None, techLoss: true);

		Assert.Equal(0, entry1.P1Change);
		Assert.Equal(-22, entry1.P2Change);
		Assert.Equal(0, entry2.P1Change);
		Assert.Equal(-22, entry2.P2Change);
	}

	[Fact]
	public async Task RegisterTeamMatchResult_ProducesTwoLaneEntriesPairingCorrespondingMembers()
	{
		await using var context = CreateContext();
		var (team1, team2) = await SeedTeamsAsync(context);
		var service = CreateService(context);
		var tournamentId = Guid.NewGuid();
		var matchId = Guid.NewGuid();

		var (entry1, entry2) = await service.RegisterTeamMatchResultAsync(
			team1.Id, team2.Id, p1Win: true, overrideEloChange: -1,
			tournamentId: tournamentId, tournamentMatchId: matchId, kind: TournamentMatchKind.None);

		Assert.Equal("p1a", entry1.P1PlayerId);
		Assert.Equal("p2a", entry1.P2PlayerId);
		Assert.Equal("p1b", entry2.P1PlayerId);
		Assert.Equal("p2b", entry2.P2PlayerId);
		Assert.Equal(tournamentId, entry1.TournamentId);
		Assert.Equal(matchId, entry1.TournamentMatchId);
		Assert.NotEqual(entry1.Id, entry2.Id);
	}

	[Fact]
	public async Task RegisterTeamMatchResult_MissingTeam_ThrowsValidationException()
	{
		await using var context = CreateContext();
		var (team1, _) = await SeedTeamsAsync(context);
		var service = CreateService(context);

		await Assert.ThrowsAsync<HistoryValidationException>(() =>
			service.RegisterTeamMatchResultAsync(
				team1.Id, Guid.NewGuid(), p1Win: true, overrideEloChange: -1,
				tournamentId: Guid.NewGuid(), tournamentMatchId: Guid.NewGuid(), kind: TournamentMatchKind.None));
	}

	private static HistoryEntry MakeEntry(Guid p1, DateTimeOffset timestamp, Guid? p2 = null) => new()
	{
		Id = Guid.NewGuid(),
		P1PlayerId = p1,
		P1Change = 1,
		P2PlayerId = p2,
		P2Change = p2 is null ? null : -1,
		Kind = p2 is null ? TournamentMatchKind.Adjustment : TournamentMatchKind.None,
		Timestamp = timestamp,
	};

	[Fact]
	public async Task ListPageAsync_ReturnsNewestFirst_AndPaginatesInOrder()
	{
		await using var context = CreateContext();
		var p1 = await SeedPlayerAsync(context, "p1");
		var now = DateTimeOffset.UtcNow;
		var entries = Enumerable.Range(0, 5).Select(i => MakeEntry(p1.Id, now.AddMinutes(-i))).ToList();
		context.HistoryEntries.AddRange(entries);
		await context.SaveChangesAsync();
		var service = CreateService(context);

		var firstPage = await service.ListPageAsync(2, null, null);
		Assert.Equal(2, firstPage.Entries.Count);
		Assert.True(firstPage.HasMore);
		Assert.Equal(entries[0].Id, firstPage.Entries[0].Id);
		Assert.Equal(entries[1].Id, firstPage.Entries[1].Id);

		var last = firstPage.Entries[^1];
		var secondPage = await service.ListPageAsync(2, last.Timestamp, last.Id);
		Assert.Equal(2, secondPage.Entries.Count);
		Assert.True(secondPage.HasMore);
		Assert.Equal(entries[2].Id, secondPage.Entries[0].Id);
		Assert.Equal(entries[3].Id, secondPage.Entries[1].Id);

		var lastOfSecond = secondPage.Entries[^1];
		var thirdPage = await service.ListPageAsync(2, lastOfSecond.Timestamp, lastOfSecond.Id);
		Assert.Single(thirdPage.Entries);
		Assert.False(thirdPage.HasMore);
		Assert.Equal(entries[4].Id, thirdPage.Entries[0].Id);
	}

	[Fact]
	public async Task ListPageAsync_TiedTimestamps_SplitAcrossPagesWithoutDuplicationOrGaps()
	{
		await using var context = CreateContext();
		var p1 = await SeedPlayerAsync(context, "p1");
		var sameInstant = DateTimeOffset.UtcNow;
		var entries = Enumerable.Range(0, 4).Select(_ => MakeEntry(p1.Id, sameInstant)).ToList();
		context.HistoryEntries.AddRange(entries);
		await context.SaveChangesAsync();
		var service = CreateService(context);

		var seen = new List<Guid>();
		DateTimeOffset? cursorTimestamp = null;
		Guid? cursorId = null;
		for (var i = 0; i < 10; i++) // bounded loop guards against an infinite loop if pagination regresses
		{
			var page = await service.ListPageAsync(1, cursorTimestamp, cursorId);
			if (page.Entries.Count == 0) break;
			seen.Add(page.Entries[0].Id);
			cursorTimestamp = page.Entries[0].Timestamp;
			cursorId = page.Entries[0].Id;
			if (!page.HasMore) break;
		}

		Assert.Equal(entries.Select(e => e.Id).OrderBy(id => id), seen.OrderBy(id => id));
	}

	[Fact]
	public async Task ListPageAsync_EntryWithTournamentId_ResolvesTournamentName()
	{
		await using var context = CreateContext();
		var p1 = await SeedPlayerAsync(context, "p1");
		var tournament = new Tournament { Id = Guid.NewGuid(), Name = "Spring Cup" };
		context.Tournaments.Add(tournament);
		var entry = MakeEntry(p1.Id, DateTimeOffset.UtcNow);
		entry.TournamentId = tournament.Id;
		context.HistoryEntries.Add(entry);
		await context.SaveChangesAsync();
		var service = CreateService(context);

		var page = await service.ListPageAsync(50, null, null);

		Assert.Equal("Spring Cup", page.Entries[0].TournamentName);
	}

	[Fact]
	public async Task ListPageAsync_OrphanedTournamentId_TournamentNameIsNull()
	{
		await using var context = CreateContext();
		var p1 = await SeedPlayerAsync(context, "p1");
		var entry = MakeEntry(p1.Id, DateTimeOffset.UtcNow);
		entry.TournamentId = Guid.NewGuid(); // no matching Tournament row
		context.HistoryEntries.Add(entry);
		await context.SaveChangesAsync();
		var service = CreateService(context);

		var page = await service.ListPageAsync(50, null, null);

		Assert.Null(page.Entries[0].TournamentName);
	}

	[Fact]
	public async Task ListPageByPlayerAsync_OnlyReturnsEntriesForThatPlayer()
	{
		await using var context = CreateContext();
		var p1 = await SeedPlayerAsync(context, "p1");
		var p2 = await SeedPlayerAsync(context, "p2");
		var p3 = await SeedPlayerAsync(context, "p3");
		var p4 = await SeedPlayerAsync(context, "p4");
		var now = DateTimeOffset.UtcNow;
		context.HistoryEntries.AddRange(
			MakeEntry(p1.Id, now, p2.Id),
			MakeEntry(p3.Id, now.AddMinutes(-1), p4.Id));
		await context.SaveChangesAsync();
		var service = CreateService(context);

		var page = await service.ListPageByPlayerAsync("p1", 50, null, null);

		Assert.Single(page.Entries);
		Assert.False(page.HasMore);
		Assert.Equal("p1", page.Entries[0].P1PlayerId);
	}
}
