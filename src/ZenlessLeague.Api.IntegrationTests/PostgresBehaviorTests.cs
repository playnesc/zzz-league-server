using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using IMigrator = Microsoft.EntityFrameworkCore.Migrations.IMigrator;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZenlessLeague.Api.Features.Discord;
using ZenlessLeague.Api.Features.History;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Seasons;
using ZenlessLeague.Api.Features.Teams;
using ZenlessLeague.Api.Features.Tournaments;
using ZenlessLeague.Api.Features.Tournaments.Matches;
using ZenlessLeague.Api.Infrastructure.Errors;
using ZenlessLeague.Api.Infrastructure.Persistence;
using ZenlessLeague.Api.Tests;

namespace ZenlessLeague.Api.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class PostgresBehaviorTests(PostgresFixture postgres)
{
	[Fact]
	public async Task FinalizeSeason_SeveralDiscordLinkedPlayersChangingTier_Completes()
	{
		// With real async I/O, resyncing these players' Discord roles concurrently on one DbContext
		// threw "A second operation was started on this context instance".
		var database = await postgres.CreateDatabaseAsync();
		await using var db = postgres.CreateContext(database);
		foreach (var uid in new[] { "p1", "p2", "p3" })
		{
			var player = TestPlayers.Create(db, uid);
			player.TournamentPoints = 250; // crosses the mid-tier threshold
			player.DiscordId = $"d-{uid}";
		}
		await db.SaveChangesAsync();

		var discord = new FakeDiscordClient();
		var roleAssignment = new DiscordRoleAssignmentService(
			new PlayerRepository(db), discord,
			Options.Create(new DiscordOptions { NewbieRoleId = "newbie", MidRoleId = "mid", HighRoleId = "high" }));
		var service = new SeasonService(
			new PlayerRepository(db), new ArchiveRepository(db), new HistoryRepository(db),
			roleAssignment, new SeasonTimerRepository(db), NullLogger<SeasonService>.Instance, new EfUnitOfWork(db));

		await service.FinalizeSeasonAsync();

		Assert.Equal(3, discord.AddedMemberRoles.Count(r => r.RoleId == "mid"));
	}

	[Fact]
	public async Task DuplicatePlayerName_IsReportedAs409()
	{
		var database = await postgres.CreateDatabaseAsync();
		await using (var first = postgres.CreateContext(database))
		{
			first.Players.Add(new Player { Uid = "u1", Name = "Same", CreatedAt = DateTimeOffset.UtcNow });
			await first.SaveChangesAsync();
		}

		// What two simultaneous sign-ups with the same name end up as: the check-then-insert race
		// is caught by the unique index.
		await using var second = postgres.CreateContext(database);
		second.Players.Add(new Player { Uid = "u2", Name = "Same", CreatedAt = DateTimeOffset.UtcNow });
		var ex = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());

		Assert.Equal(StatusCodes.Status409Conflict, await HandledStatusAsync(ex));
	}

	[Fact]
	public async Task DeletingPlayerWithHistory_IsReportedAs409()
	{
		var database = await postgres.CreateDatabaseAsync();
		await using (var seed = postgres.CreateContext(database))
		{
			var p1 = TestPlayers.Create(seed, "p1");
			var p2 = TestPlayers.Create(seed, "p2");
			await seed.SaveChangesAsync();
			await new HistoryService(new PlayerRepository(seed), new HistoryRepository(seed), new TeamRepository(seed),
					TestDiscord.CreateRoleAssignmentService(seed), new TournamentRepository(seed), NullLogger<HistoryService>.Instance)
				.RegisterMatchResultAsync(p1.Id, p2.Id, p1Win: true, overrideEloChange: -1);
		}

		// A fresh context, like the admin's delete request — the history row isn't tracked, so it's
		// Postgres's foreign key that refuses the delete.
		await using var db = postgres.CreateContext(database);
		var service = new PlayerService(new PlayerRepository(db), new TournamentRegistrationRepository(db));
		var ex = await Assert.ThrowsAsync<DbUpdateException>(() => service.DeleteAsync("p1"));

		Assert.Equal(StatusCodes.Status409Conflict, await HandledStatusAsync(ex));
	}

	[Fact]
	public async Task DeletingTeamWithTournamentHistory_GivesTheFriendlyExplanation()
	{
		var database = await postgres.CreateDatabaseAsync();
		Guid teamId;
		await using (var seed = postgres.CreateContext(database))
		{
			var a = TestPlayers.Create(seed, "a");
			var b = TestPlayers.Create(seed, "b");
			var team = new Team { Id = Guid.NewGuid(), Name = "Duo", Player1Id = a.Id, Player2Id = b.Id, CreatedAt = DateTimeOffset.UtcNow };
			var tournament = new Tournament { Id = Guid.NewGuid(), Name = "2v2 Cup", RegistrationType = TournamentRegistrationType.Team };
			seed.Teams.Add(team);
			seed.Tournaments.Add(tournament);
			seed.TournamentChallongeParticipants.Add(new TournamentChallongeParticipant { TournamentId = tournament.Id, ChallongeParticipantId = "c1", TeamId = team.Id });
			await seed.SaveChangesAsync();
			teamId = team.Id;
		}

		await using var db = postgres.CreateContext(database);
		var service = new TeamService(new TeamRepository(db), new PlayerRepository(db), new FakeFirebaseStorageClient());

		var ex = await Assert.ThrowsAsync<TeamValidationException>(() => service.DeleteAsync(teamId));
		Assert.Contains("tournament history", ex.Message);
	}

	[Fact]
	public async Task AddHistoryEntryP1WonMigration_BackfillsWinnersFromMatchesAndDeltas()
	{
		var database = await postgres.CreateEmptyDatabaseAsync();
		await using var db = postgres.CreateContext(database);
		var migrator = db.GetService<IMigrator>();
		await migrator.MigrateAsync("20261022152324_AddConcurrencyTokens"); // the schema just before P1Won

		var p1 = TestPlayers.Create(db, "p1");
		var p2 = TestPlayers.Create(db, "p2");
		var tournament = new Tournament { Id = Guid.NewGuid(), Name = "Cup" };
		db.Tournaments.Add(tournament);
		var match = new TournamentMatch { Id = Guid.NewGuid(), TournamentId = tournament.Id, ChallongeMatchId = 1, P1PlayerId = p1.Id, P2PlayerId = p2.Id, State = "complete" };
		db.TournamentMatches.Add(match);
		await db.SaveChangesAsync();

		// History rows as they existed before the column (inserted with SQL — the current model
		// already has P1Won, which this schema doesn't).
		var linkedTie = await InsertHistoryAsync(db, p1.Id, 0, p2.Id, 0);  // OverrideEloChange = 0 match P1 won
		var p1Gained = await InsertHistoryAsync(db, p1.Id, 10, p2.Id, -10);
		var p1Lost = await InsertHistoryAsync(db, p1.Id, -5, p2.Id, 5);
		var unlinkedTie = await InsertHistoryAsync(db, p1.Id, 0, p2.Id, 0);
		var adjustment = await InsertHistoryAsync(db, p1.Id, 40, null, null);
		await db.Database.ExecuteSqlAsync(
			$"""UPDATE "TournamentMatches" SET "HistoryEntryId" = {linkedTie}, "WinnerPlayerId" = {p1.Id} WHERE "Id" = {match.Id}""");

		await migrator.MigrateAsync();

		await using var after = postgres.CreateContext(database);
		var p1Won = await after.HistoryEntries.ToDictionaryAsync(h => h.Id, h => h.P1Won);
		Assert.True(p1Won[linkedTie]);   // from the linked match's winner, despite equal deltas
		Assert.True(p1Won[p1Gained]);
		Assert.False(p1Won[p1Lost]);
		Assert.Null(p1Won[unlinkedTie]); // no way to tell — reversal keeps the old fallback
		Assert.Null(p1Won[adjustment]);  // single-player adjustments have no winner
	}

	private static async Task<Guid> InsertHistoryAsync(AppDbContext db, Guid p1, int p1Change, Guid? p2, int? p2Change)
	{
		var id = Guid.NewGuid();
		var kind = p2 is null ? (int)TournamentMatchKind.Adjustment : (int)TournamentMatchKind.Custom;
		await db.Database.ExecuteSqlAsync(
			$"""
			INSERT INTO "HistoryEntries" ("Id", "P1PlayerId", "P1Change", "P2PlayerId", "P2Change", "Kind", "Timestamp")
			VALUES ({id}, {p1}, {p1Change}, {p2}, {p2Change}, {kind}, {DateTimeOffset.UtcNow})
			""");
		return id;
	}

	private static async Task<int> HandledStatusAsync(Exception exception)
	{
		var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
		await new ApiExceptionHandler(NullLogger<ApiExceptionHandler>.Instance).TryHandleAsync(context, exception, CancellationToken.None);
		return context.Response.StatusCode;
	}
}
