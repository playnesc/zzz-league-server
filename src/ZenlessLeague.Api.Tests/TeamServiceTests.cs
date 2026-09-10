using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Teams;

namespace ZenlessLeague.Api.Tests;

public class TeamServiceTests
{
	private static AppDbContext CreateContext() =>
		new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

	private static TeamService CreateService(AppDbContext context) =>
		new(new TeamRepository(context), new PlayerRepository(context), new FakeFirebaseStorageClient());

	private static void SeedPlayer(AppDbContext context, string uid) =>
		context.Players.Add(new Player { Uid = uid, Name = uid, CreatedAt = DateTimeOffset.UtcNow });

	[Fact]
	public async Task CreateAsync_ValidPlayers_CreatesTeam()
	{
		await using var context = CreateContext();
		SeedPlayer(context, "p1");
		SeedPlayer(context, "p2");
		await context.SaveChangesAsync();
		var service = CreateService(context);

		var team = await service.CreateAsync(new CreateTeamRequest("p1", "p2", "Dream Team", null));

		Assert.Equal("Dream Team", team.Name);
		Assert.Equal("p1", team.Creator.Uid);
		Assert.Equal("p2", team.Player2.Uid);
	}

	[Fact]
	public async Task CreateAsync_WithPhoto_UploadsAndStoresUrl()
	{
		await using var context = CreateContext();
		SeedPlayer(context, "p1");
		SeedPlayer(context, "p2");
		await context.SaveChangesAsync();
		var storage = new FakeFirebaseStorageClient();
		var service = new TeamService(new TeamRepository(context), new PlayerRepository(context), storage);

		var team = await service.CreateAsync(new CreateTeamRequest("p1", "p2", "Logo Team", "data:image/png;base64,Zm9v"));

		Assert.Single(storage.Uploads);
		Assert.Equal($"https://fake-storage.example/teams/{team.Id}/logo.jpg", team.PhotoUrl);
	}

	[Fact]
	public async Task UpdateAsync_WithoutNewPhoto_KeepsExistingUrl()
	{
		await using var context = CreateContext();
		SeedPlayer(context, "p1");
		SeedPlayer(context, "p2");
		SeedPlayer(context, "p3");
		await context.SaveChangesAsync();
		var storage = new FakeFirebaseStorageClient();
		var service = new TeamService(new TeamRepository(context), new PlayerRepository(context), storage);
		var team = await service.CreateAsync(new CreateTeamRequest("p1", "p2", "Team", "data:image/png;base64,Zm9v"));

		var updated = await service.UpdateAsync(team.Id, new EditTeamRequest("Team", null, "p3"));

		Assert.NotNull(updated);
		Assert.Equal(team.PhotoUrl, updated!.PhotoUrl);
		Assert.Single(storage.Uploads); // no second upload triggered
	}

	[Fact]
	public async Task CreateAsync_SamePlayerBothSlots_Throws()
	{
		await using var context = CreateContext();
		SeedPlayer(context, "p1");
		await context.SaveChangesAsync();
		var service = CreateService(context);

		await Assert.ThrowsAsync<TeamValidationException>(() =>
			service.CreateAsync(new CreateTeamRequest("p1", "p1", "Solo Duo", null)));
	}

	[Fact]
	public async Task ListByPlayerAsync_ReturnsTeamsWhereUidIsEitherSlot()
	{
		await using var context = CreateContext();
		SeedPlayer(context, "p1");
		SeedPlayer(context, "p2");
		SeedPlayer(context, "p3");
		await context.SaveChangesAsync();
		var service = CreateService(context);

		await service.CreateAsync(new CreateTeamRequest("p1", "p2", "Team A", null));
		await service.CreateAsync(new CreateTeamRequest("p3", "p1", "Team B", null));
		await service.CreateAsync(new CreateTeamRequest("p2", "p3", "Team C", null));

		var p1Teams = await service.ListByPlayerAsync("p1");

		Assert.Equal(2, p1Teams.Count);
		Assert.DoesNotContain(p1Teams, t => t.Name == "Team C");
	}

	[Fact]
	public async Task UpdateAsync_ChangesPlayer2AndName()
	{
		await using var context = CreateContext();
		SeedPlayer(context, "p1");
		SeedPlayer(context, "p2");
		SeedPlayer(context, "p3");
		await context.SaveChangesAsync();
		var service = CreateService(context);
		var team = await service.CreateAsync(new CreateTeamRequest("p1", "p2", "Old Name", null));

		var updated = await service.UpdateAsync(team.Id, new EditTeamRequest("New Name", null, "p3"));

		Assert.NotNull(updated);
		Assert.Equal("New Name", updated!.Name);
		Assert.Equal("p3", updated.Player2.Uid);
	}

	[Fact]
	public async Task DeleteAsync_RemovesTeam()
	{
		await using var context = CreateContext();
		SeedPlayer(context, "p1");
		SeedPlayer(context, "p2");
		await context.SaveChangesAsync();
		var service = CreateService(context);
		var team = await service.CreateAsync(new CreateTeamRequest("p1", "p2", "Team", null));

		Assert.True(await service.DeleteAsync(team.Id));
		Assert.Null(await service.GetAsync(team.Id));
	}

	[Fact]
	public async Task DeleteAsync_WithPhoto_DeletesUploadedLogo()
	{
		await using var context = CreateContext();
		SeedPlayer(context, "p1");
		SeedPlayer(context, "p2");
		await context.SaveChangesAsync();
		var storage = new FakeFirebaseStorageClient();
		var service = new TeamService(new TeamRepository(context), new PlayerRepository(context), storage);
		var team = await service.CreateAsync(new CreateTeamRequest("p1", "p2", "Team", "data:image/png;base64,Zm9v"));

		Assert.True(await service.DeleteAsync(team.Id));

		Assert.Contains($"teams/{team.Id}/logo", storage.Deletes);
	}

	[Fact]
	public async Task DeleteAsync_WithoutPhoto_DoesNotCallStorage()
	{
		await using var context = CreateContext();
		SeedPlayer(context, "p1");
		SeedPlayer(context, "p2");
		await context.SaveChangesAsync();
		var storage = new FakeFirebaseStorageClient();
		var service = new TeamService(new TeamRepository(context), new PlayerRepository(context), storage);
		var team = await service.CreateAsync(new CreateTeamRequest("p1", "p2", "Team", null));

		Assert.True(await service.DeleteAsync(team.Id));

		Assert.Empty(storage.Deletes);
	}

	[Fact]
	public async Task DeleteAsync_UnknownTeam_ReturnsFalse()
	{
		await using var context = CreateContext();
		var service = CreateService(context);

		Assert.False(await service.DeleteAsync(Guid.NewGuid()));
	}

	[Fact]
	public async Task ListPageAsync_PaginatesNewestFirst()
	{
		await using var context = CreateContext();
		SeedPlayer(context, "p1");
		SeedPlayer(context, "p2");
		SeedPlayer(context, "p3");
		SeedPlayer(context, "p4");
		SeedPlayer(context, "p5");
		SeedPlayer(context, "p6");
		await context.SaveChangesAsync();
		var service = CreateService(context);

		await service.CreateAsync(new CreateTeamRequest("p1", "p2", "A", null));
		await service.CreateAsync(new CreateTeamRequest("p3", "p4", "B", null));
		await service.CreateAsync(new CreateTeamRequest("p5", "p6", "C", null));

		// CreatedAt is set to UtcNow at creation time, so within an InMemory test these can tie —
		// fetch actual timestamps back out to build a correct expectation instead of assuming order.
		var all = (await context.Teams.ToListAsync()).OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).ToList();

		var firstPage = await service.ListPageAsync(2, null, null);
		Assert.Equal(2, firstPage.Teams.Count);
		Assert.Equal([all[0].Id, all[1].Id], firstPage.Teams.Select(t => t.Id));
		Assert.True(firstPage.HasMore);

		var last = firstPage.Teams[^1];
		var secondPage = await service.ListPageAsync(2, last.CreatedAt, last.Id);
		// Only 1 team left of 3 total — a short page (< take) is the unambiguous "no more" signal.
		Assert.Single(secondPage.Teams);
		Assert.Equal(all[2].Id, secondPage.Teams[0].Id);
		Assert.False(secondPage.HasMore);
	}
}
