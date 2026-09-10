using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.Players;

namespace ZenlessLeague.Api.Tests;

public class PlayerServiceTests
{
	private static AppDbContext CreateContext() =>
		new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

	private static async Task<Player> SeedPlayerAsync(AppDbContext context, string uid, string name)
	{
		var player = new Player { Uid = uid, Name = name, CreatedAt = DateTimeOffset.UtcNow };
		context.Players.Add(player);
		await context.SaveChangesAsync();
		return player;
	}

	[Fact]
	public async Task UpdateNameAsync_NewUniqueName_Updates()
	{
		await using var context = CreateContext();
		await SeedPlayerAsync(context, "u1", "Old Name");
		var service = new PlayerService(new PlayerRepository(context));

		var result = await service.UpdateNameAsync("u1", "New Name");

		Assert.NotNull(result);
		Assert.Equal("New Name", result!.Name);
	}

	[Fact]
	public async Task UpdateNameAsync_SameName_Throws()
	{
		await using var context = CreateContext();
		await SeedPlayerAsync(context, "u1", "Old Name");
		var service = new PlayerService(new PlayerRepository(context));

		await Assert.ThrowsAsync<PlayerValidationException>(() => service.UpdateNameAsync("u1", "Old Name"));
	}

	[Fact]
	public async Task UpdateNameAsync_NameAlreadyTaken_Throws()
	{
		await using var context = CreateContext();
		await SeedPlayerAsync(context, "u1", "Alice");
		await SeedPlayerAsync(context, "u2", "Bob");
		var service = new PlayerService(new PlayerRepository(context));

		await Assert.ThrowsAsync<PlayerValidationException>(() => service.UpdateNameAsync("u1", "Bob"));
	}

	[Fact]
	public async Task UpdateAvatarAsync_UpdatesAvatar()
	{
		await using var context = CreateContext();
		await SeedPlayerAsync(context, "u1", "Alice");
		var service = new PlayerService(new PlayerRepository(context));

		var result = await service.UpdateAvatarAsync("u1", "agent-1");

		Assert.NotNull(result);
		Assert.Equal("agent-1", result!.Avatar);
	}
}
