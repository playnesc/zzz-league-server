using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.Auth;
using ZenlessLeague.Api.Features.Discord;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Tournaments;

namespace ZenlessLeague.Api.Tests;

public class AuthServiceTests
{
	private static AppDbContext CreateContext() =>
		new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

	private static (IAuthService Service, FakeDiscordOAuthClient OAuth, FakeDiscordClient Discord) CreateService(AppDbContext context)
	{
		var oauth = new FakeDiscordOAuthClient();
		var discord = new FakeDiscordClient();
		var options = Options.Create(new DiscordOptions
		{
			NewbieRoleId = "newbie",
			MidRoleId = "mid",
			HighRoleId = "high",
		});
		var roleAssignment = new DiscordRoleAssignmentService(new PlayerRepository(context), discord, options);
		var service = new AuthService(
			new PlayerRepository(context), new TournamentRegistrationRepository(context),
			oauth, discord, roleAssignment, options, new FakeFirebaseCustomTokenSigner());

		return (service, oauth, discord);
	}

	[Fact]
	public async Task LoginWithDiscordAsync_ExistingLinkedPlayer_ReturnsTokenForThatPlayerWithoutCreatingANewOne()
	{
		await using var context = CreateContext();
		TestPlayers.Create(context, "p1").DiscordId = "discord-1";
		await context.SaveChangesAsync();
		var (service, oauth, _) = CreateService(context);
		oauth.NextUser = new DiscordOAuthUser("discord-1", "SomeName");

		var token = await service.LoginWithDiscordAsync("code", "redirect");

		Assert.Equal("token-for:p1", token);
		Assert.Single(context.Players);
	}

	[Fact]
	public async Task LoginWithDiscordAsync_UnknownDiscordId_AutoCreatesPlayerFromDiscordUsername()
	{
		await using var context = CreateContext();
		var (service, oauth, _) = CreateService(context);
		oauth.NextUser = new DiscordOAuthUser("discord-1", "NewGuy");

		var token = await service.LoginWithDiscordAsync("code", "redirect");

		var created = await context.Players.SingleAsync();
		Assert.Equal("NewGuy", created.Name);
		Assert.Equal("discord-1", created.DiscordId);
		Assert.Equal("NewGuy", created.DiscordUsername);
		Assert.Equal($"token-for:{created.Uid}", token);
	}

	[Fact]
	public async Task LoginWithDiscordAsync_NameCollision_SuffixesAnIncrementingNumber()
	{
		await using var context = CreateContext();
		TestPlayers.Create(context, "NewGuy");
		await context.SaveChangesAsync();
		var (service, oauth, _) = CreateService(context);
		oauth.NextUser = new DiscordOAuthUser("discord-1", "NewGuy");

		await service.LoginWithDiscordAsync("code", "redirect");

		var created = await context.Players.SingleAsync(p => p.DiscordId == "discord-1");
		Assert.Equal("NewGuy2", created.Name);
	}

	[Fact]
	public async Task LinkDiscordAsync_SetsDiscordIdAndAssignsTierRole()
	{
		await using var context = CreateContext();
		var player = TestPlayers.Create(context, "p1", highConfirmed: true);
		await context.SaveChangesAsync();
		var (service, oauth, discord) = CreateService(context);
		oauth.NextUser = new DiscordOAuthUser("discord-1", "SomeName");

		var result = await service.LinkDiscordAsync("p1", "code", "redirect");

		Assert.NotNull(result);
		Assert.Equal("discord-1", player.DiscordId);
		Assert.Equal("SomeName", player.DiscordUsername);
		Assert.Contains(("discord-1", "high"), discord.AddedMemberRoles);
	}

	[Fact]
	public async Task LinkDiscordAsync_DiscordIdAlreadyLinkedToAnotherPlayer_Throws()
	{
		await using var context = CreateContext();
		TestPlayers.Create(context, "other").DiscordId = "discord-1";
		TestPlayers.Create(context, "p1");
		await context.SaveChangesAsync();
		var (service, oauth, _) = CreateService(context);
		oauth.NextUser = new DiscordOAuthUser("discord-1", "SomeName");

		await Assert.ThrowsAsync<PlayerValidationException>(() => service.LinkDiscordAsync("p1", "code", "redirect"));
	}

	[Fact]
	public async Task LinkDiscordAsync_UnknownPlayer_ReturnsNull()
	{
		await using var context = CreateContext();
		var (service, _, _) = CreateService(context);

		Assert.Null(await service.LinkDiscordAsync("missing", "code", "redirect"));
	}

	[Fact]
	public async Task UnlinkDiscordAsync_ClearsDiscordFieldsAndRemovesAllTierRoles()
	{
		await using var context = CreateContext();
		var player = TestPlayers.Create(context, "p1", highConfirmed: true);
		player.DiscordId = "discord-1";
		player.DiscordUsername = "SomeName";
		await context.SaveChangesAsync();
		var (service, _, discord) = CreateService(context);

		var result = await service.UnlinkDiscordAsync("p1");

		Assert.NotNull(result);
		Assert.Null(player.DiscordId);
		Assert.Null(player.DiscordUsername);
		Assert.Equal(3, discord.RemovedMemberRoles.Count(r => r.DiscordUserId == "discord-1"));
	}

	[Fact]
	public async Task UnlinkDiscordAsync_NotLinked_SucceedsWithoutTouchingDiscord()
	{
		await using var context = CreateContext();
		TestPlayers.Create(context, "p1");
		await context.SaveChangesAsync();
		var (service, _, discord) = CreateService(context);

		Assert.NotNull(await service.UnlinkDiscordAsync("p1"));
		Assert.Empty(discord.RemovedMemberRoles);
	}

	[Fact]
	public async Task UnlinkDiscordAsync_UnknownPlayer_ReturnsNull()
	{
		await using var context = CreateContext();
		var (service, _, _) = CreateService(context);

		Assert.Null(await service.UnlinkDiscordAsync("missing"));
	}
}
