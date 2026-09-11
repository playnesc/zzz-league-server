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
}
