using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Tournaments;

namespace ZenlessLeague.Api.Tests;

public class PlayerServiceTests
{
	private static AppDbContext CreateContext() =>
		new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

	private static PlayerService CreateService(AppDbContext context) =>
		new(new PlayerRepository(context), new TournamentRegistrationRepository(context));

	private static async Task<Player> SeedPlayerAsync(AppDbContext context, string uid, string name, PlayerRole role = PlayerRole.Player)
	{
		var player = new Player { Uid = uid, Name = name, Role = role, CreatedAt = DateTimeOffset.UtcNow };
		context.Players.Add(player);
		await context.SaveChangesAsync();
		return player;
	}

	[Fact]
	public async Task UpdateNameAsync_NewUniqueName_Updates()
	{
		await using var context = CreateContext();
		await SeedPlayerAsync(context, "u1", "Old Name");
		var service = CreateService(context);

		var result = await service.UpdateNameAsync("u1", "New Name");

		Assert.NotNull(result);
		Assert.Equal("New Name", result!.Name);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public async Task UpdateNameAsync_Blank_Throws(string name)
	{
		await using var context = CreateContext();
		await SeedPlayerAsync(context, "u1", "Old Name");
		var service = CreateService(context);

		await Assert.ThrowsAsync<ValidationApiException>(() => service.UpdateNameAsync("u1", name));
	}

	[Fact]
	public async Task UpdateNameAsync_LongerThanColumn_ThrowsInsteadOfFailingInTheDatabase()
	{
		await using var context = CreateContext();
		await SeedPlayerAsync(context, "u1", "Old Name");
		var service = CreateService(context);

		await Assert.ThrowsAsync<ValidationApiException>(() =>
			service.UpdateNameAsync("u1", new string('x', Player.NameMaxLength + 1)));
	}

	[Fact]
	public async Task UpdateNameAsync_TrimsSurroundingWhitespace()
	{
		await using var context = CreateContext();
		await SeedPlayerAsync(context, "u1", "Old Name");
		var service = CreateService(context);

		var result = await service.UpdateNameAsync("u1", "  New Name  ");

		Assert.Equal("New Name", result!.Name);
	}

	[Fact]
	public async Task UpdateNameAsync_SameName_Throws()
	{
		await using var context = CreateContext();
		await SeedPlayerAsync(context, "u1", "Old Name");
		var service = CreateService(context);

		await Assert.ThrowsAsync<PlayerValidationException>(() => service.UpdateNameAsync("u1", "Old Name"));
	}

	[Fact]
	public async Task UpdateNameAsync_NameAlreadyTaken_Throws()
	{
		await using var context = CreateContext();
		await SeedPlayerAsync(context, "u1", "Alice");
		await SeedPlayerAsync(context, "u2", "Bob");
		var service = CreateService(context);

		await Assert.ThrowsAsync<PlayerValidationException>(() => service.UpdateNameAsync("u1", "Bob"));
	}

	[Fact]
	public async Task UpdateAvatarAsync_UpdatesAvatar()
	{
		await using var context = CreateContext();
		await SeedPlayerAsync(context, "u1", "Alice");
		var service = CreateService(context);

		var result = await service.UpdateAvatarAsync("u1", "agent-1");

		Assert.NotNull(result);
		Assert.Equal("agent-1", result!.Avatar);
	}

	[Fact]
	public async Task ListAsync_NoFilter_ReturnsEveryoneInLeanShape()
	{
		await using var context = CreateContext();
		await SeedPlayerAsync(context, "u1", "Alice");
		await SeedPlayerAsync(context, "u2", "Bob");
		var service = CreateService(context);

		var result = await service.ListAsync();

		Assert.Equal(["Alice", "Bob"], result.Select(p => p.Name));
	}

	[Fact]
	public async Task ListAsync_WithUidsFilter_ReturnsOnlyMatchingPlayers()
	{
		await using var context = CreateContext();
		await SeedPlayerAsync(context, "u1", "Alice");
		await SeedPlayerAsync(context, "u2", "Bob");
		await SeedPlayerAsync(context, "u3", "Carol");
		var service = CreateService(context);

		var result = await service.ListAsync(["u1", "u3"]);

		Assert.Equal(["Alice", "Carol"], result.Select(p => p.Name));
	}

	[Fact]
	public async Task ListAsync_UidsFilterIncludesUnknownUid_SilentlyOmitsIt()
	{
		await using var context = CreateContext();
		await SeedPlayerAsync(context, "u1", "Alice");
		var service = CreateService(context);

		var result = await service.ListAsync(["u1", "missing"]);

		Assert.Equal(["Alice"], result.Select(p => p.Name));
	}

	[Fact]
	public async Task ListStaffAsync_ReturnsModeratorsAndAdminsOnly_AdminsFirst()
	{
		await using var context = CreateContext();
		await SeedPlayerAsync(context, "u1", "Bob", PlayerRole.Moderator);
		await SeedPlayerAsync(context, "u2", "Carl");
		await SeedPlayerAsync(context, "u3", "Zed", PlayerRole.Admin);
		await SeedPlayerAsync(context, "u4", "Alice", PlayerRole.Moderator);
		var service = CreateService(context);

		var result = await service.ListStaffAsync();

		Assert.Equal(["Zed", "Alice", "Bob"], result.Select(p => p.Name));
		Assert.Equal([PlayerRole.Admin, PlayerRole.Moderator, PlayerRole.Moderator], result.Select(p => p.Role));
	}

	[Fact]
	public async Task UpdateRoleAsync_PromoteAndDemoteModerator_Updates()
	{
		await using var context = CreateContext();
		await SeedPlayerAsync(context, "u1", "Bob");
		var service = CreateService(context);

		var promoted = await service.UpdateRoleAsync("u1", new UpdatePlayerRoleRequest(PlayerRole.Moderator));
		Assert.Equal(PlayerRole.Moderator, promoted!.Role);

		var demoted = await service.UpdateRoleAsync("u1", new UpdatePlayerRoleRequest(PlayerRole.Player));
		Assert.Equal(PlayerRole.Player, demoted!.Role);
	}

	[Fact]
	public async Task UpdateRoleAsync_GrantAdmin_Throws()
	{
		await using var context = CreateContext();
		await SeedPlayerAsync(context, "u1", "Bob", PlayerRole.Moderator);
		var service = CreateService(context);

		await Assert.ThrowsAsync<PlayerValidationException>(() =>
			service.UpdateRoleAsync("u1", new UpdatePlayerRoleRequest(PlayerRole.Admin)));
	}

	[Fact]
	public async Task UpdateRoleAsync_TargetIsAdmin_Throws()
	{
		await using var context = CreateContext();
		await SeedPlayerAsync(context, "u1", "Zed", PlayerRole.Admin);
		var service = CreateService(context);

		await Assert.ThrowsAsync<PlayerValidationException>(() =>
			service.UpdateRoleAsync("u1", new UpdatePlayerRoleRequest(PlayerRole.Player)));
		Assert.Equal(PlayerRole.Admin, (await context.Players.SingleAsync()).Role);
	}

	private static void SeedSoloRegistration(
		AppDbContext context, Guid playerId, string rosterName, string? rosterScreenshotUrl, DateTimeOffset updatedAt) =>
		context.TournamentRegistrations.Add(new TournamentRegistration
		{
			Id = Guid.NewGuid(),
			TournamentId = Guid.NewGuid(),
			PlayerId = playerId,
			Player1Details = new PlayerRegistrationDetails
			{
				GameUid = "game-uid",
				DarteNickname = "nick",
				DartePresetName = "preset",
				RosterName = rosterName,
				RosterScreenshotUrl = rosterScreenshotUrl,
				HoyolabScreenshotUrl = rosterScreenshotUrl is null ? null : "hoyolab.png",
			},
			RegisteredAt = updatedAt,
			UpdatedAt = updatedAt,
		});

	[Fact]
	public async Task GetLastRegistrationAsync_ReturnsMostRecentlyUpdatedSoloRegistration()
	{
		await using var context = CreateContext();
		var player = await SeedPlayerAsync(context, "u1", "Ann");
		var now = DateTimeOffset.UtcNow;
		SeedSoloRegistration(context, player.Id, "older", "old.png", now.AddDays(-2));
		SeedSoloRegistration(context, player.Id, "newer", "new.png", now.AddDays(-1));
		await context.SaveChangesAsync();

		var result = await CreateService(context).GetLastRegistrationAsync("u1");

		Assert.NotNull(result);
		Assert.Equal("newer", result!.RosterName);
		Assert.Equal("new.png", result.RosterScreenshotUrl);
	}

	[Fact]
	public async Task GetLastRegistrationAsync_SkipsAdminAddedStubWithoutScreenshots()
	{
		await using var context = CreateContext();
		var player = await SeedPlayerAsync(context, "u1", "Ann");
		var now = DateTimeOffset.UtcNow;
		SeedSoloRegistration(context, player.Id, "real", "real.png", now.AddDays(-2));
		SeedSoloRegistration(context, player.Id, "", null, now.AddDays(-1));
		await context.SaveChangesAsync();

		var result = await CreateService(context).GetLastRegistrationAsync("u1");

		Assert.Equal("real", result?.RosterName);
	}

	[Fact]
	public async Task GetLastRegistrationAsync_NoRegistrations_ReturnsNull()
	{
		await using var context = CreateContext();
		await SeedPlayerAsync(context, "u1", "Ann");

		Assert.Null(await CreateService(context).GetLastRegistrationAsync("u1"));
	}
}
