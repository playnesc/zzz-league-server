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
}
