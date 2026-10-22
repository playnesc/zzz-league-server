using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZenlessLeague.Api.Features.Discord;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Teams;
using ZenlessLeague.Api.Features.Tournaments;
using ZenlessLeague.Api.Infrastructure.Persistence;

namespace ZenlessLeague.Api.Tests;

public class TournamentDiscordResourcesServiceTests
{
	private static AppDbContext CreateContext() =>
		new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

	private static readonly DiscordOptions TestOptions = new()
	{
		GuildId = "guild1",
		TournamentCategoryId = "category1",
		TournamentStaffRoleIds = ["staff1", "staff2"],
	};

	private static (TournamentDiscordResourcesService Service, FakeDiscordClient Discord) CreateService(AppDbContext context)
	{
		var discord = new FakeDiscordClient();
		var service = new TournamentDiscordResourcesService(
			new TournamentRepository(context),
			new TournamentRegistrationRepository(context),
			new TeamRepository(context),
			new PlayerRepository(context),
			discord,
			Options.Create(TestOptions),
			NullLogger<TournamentDiscordResourcesService>.Instance);
		return (service, discord);
	}

	[Fact]
	public async Task CreateResourcesAsync_NoRoleOrChannelName_NoOps()
	{
		await using var context = CreateContext();
		var tournament = new Tournament { Id = Guid.NewGuid(), Name = "Cup", RegistrationType = TournamentRegistrationType.Solo };
		context.Tournaments.Add(tournament);
		await context.SaveChangesAsync();
		var (service, discord) = CreateService(context);

		await service.CreateResourcesAsync(tournament.Id);

		Assert.Empty(discord.CreatedChannels);
		Assert.Equal(0, discord.NextRoleId - 1);
	}

	[Fact]
	public async Task CreateResourcesAsync_CreatesRoleAndChannelAndGrantsApprovedDiscordLinkedPlayers()
	{
		await using var context = CreateContext();
		var tournament = new Tournament
		{
			Id = Guid.NewGuid(), Name = "Cup", RegistrationType = TournamentRegistrationType.Solo,
			DiscordRoleName = "Cup Player", DiscordChannelName = "cup-chat",
		};
		context.Tournaments.Add(tournament);

		var p1 = TestPlayers.Create(context, "p1");
		p1.DiscordId = "d1";
		var p2 = TestPlayers.Create(context, "p2"); // no Discord linked
		context.TournamentRegistrations.Add(new TournamentRegistration
		{
			Id = Guid.NewGuid(), TournamentId = tournament.Id, PlayerId = p1.Id,
			Player1Details = new PlayerRegistrationDetails { GameUid = "g", DarteNickname = "n", DartePresetName = "p", RosterName = "r" },
			Approved = true, RegisteredAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
		});
		context.TournamentRegistrations.Add(new TournamentRegistration
		{
			Id = Guid.NewGuid(), TournamentId = tournament.Id, PlayerId = p2.Id,
			Player1Details = new PlayerRegistrationDetails { GameUid = "g", DarteNickname = "n", DartePresetName = "p", RosterName = "r" },
			Approved = true, RegisteredAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
		});
		await context.SaveChangesAsync();

		var (service, discord) = CreateService(context);
		await service.CreateResourcesAsync(tournament.Id);

		var reloaded = await context.Tournaments.SingleAsync(t => t.Id == tournament.Id);
		Assert.Equal("role-1", reloaded.DiscordRoleId);
		Assert.Equal("channel-1", reloaded.DiscordChannelId);

		var channel = Assert.Single(discord.CreatedChannels);
		Assert.Equal("cup-chat", channel.Name);
		Assert.Equal("category1", channel.ParentId);
		// @everyone (guild id) denied, the new role allowed, both staff roles allowed.
		Assert.Contains(channel.Overwrites, o => o.Id == "guild1" && o.Deny is not null);
		Assert.Contains(channel.Overwrites, o => o.Id == "role-1" && o.Allow is not null);
		Assert.Contains(channel.Overwrites, o => o.Id == "staff1" && o.Allow is not null);
		Assert.Contains(channel.Overwrites, o => o.Id == "staff2" && o.Allow is not null);

		// Only the Discord-linked approved player gets the role.
		Assert.Single(discord.AddedMemberRoles);
		Assert.Contains(("d1", "role-1"), discord.AddedMemberRoles);
	}

	[Fact]
	public async Task CreateResourcesAsync_OneMemberUnknownToDiscord_SkipsThemAndStillGrantsTheRest()
	{
		await using var context = CreateContext();
		var tournament = new Tournament
		{
			Id = Guid.NewGuid(), Name = "Cup", RegistrationType = TournamentRegistrationType.Solo,
			DiscordRoleName = "Cup Player",
		};
		context.Tournaments.Add(tournament);

		var p1 = TestPlayers.Create(context, "p1");
		p1.DiscordId = "d1"; // left the guild since linking — Discord will 404 this one
		var p2 = TestPlayers.Create(context, "p2");
		p2.DiscordId = "d2";
		context.TournamentRegistrations.Add(new TournamentRegistration
		{
			Id = Guid.NewGuid(), TournamentId = tournament.Id, PlayerId = p1.Id,
			Player1Details = new PlayerRegistrationDetails { GameUid = "g", DarteNickname = "n", DartePresetName = "p", RosterName = "r" },
			Approved = true, RegisteredAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
		});
		context.TournamentRegistrations.Add(new TournamentRegistration
		{
			Id = Guid.NewGuid(), TournamentId = tournament.Id, PlayerId = p2.Id,
			Player1Details = new PlayerRegistrationDetails { GameUid = "g", DarteNickname = "n", DartePresetName = "p", RosterName = "r" },
			Approved = true, RegisteredAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
		});
		await context.SaveChangesAsync();

		var (service, discord) = CreateService(context);
		discord.UnknownMemberDiscordIds.Add("d1");

		await service.CreateResourcesAsync(tournament.Id);

		var reloaded = await context.Tournaments.SingleAsync(t => t.Id == tournament.Id);
		Assert.Equal("role-1", reloaded.DiscordRoleId); // role creation itself still succeeded
		Assert.Single(discord.AddedMemberRoles);
		Assert.Contains(("d2", "role-1"), discord.AddedMemberRoles);
	}

	[Fact]
	public async Task CreateResourcesAsync_AlreadyHasRoleId_DoesNotCreateAnotherRole()
	{
		await using var context = CreateContext();
		var tournament = new Tournament
		{
			Id = Guid.NewGuid(), Name = "Cup", RegistrationType = TournamentRegistrationType.Solo,
			DiscordRoleName = "Cup Player", DiscordRoleId = "existing-role",
		};
		context.Tournaments.Add(tournament);
		await context.SaveChangesAsync();
		var (service, discord) = CreateService(context);

		await service.CreateResourcesAsync(tournament.Id);

		Assert.Empty(discord.CreatedChannels); // no channel name set
		Assert.Equal(0, discord.NextRoleId - 1); // CreateGuildRoleAsync never called
		var reloaded = await context.Tournaments.SingleAsync(t => t.Id == tournament.Id);
		Assert.Equal("existing-role", reloaded.DiscordRoleId);
	}

	[Fact]
	public async Task DeleteRoleAndChannel_ClearsStoredIdsAndCallsDiscord()
	{
		await using var context = CreateContext();
		var tournament = new Tournament
		{
			Id = Guid.NewGuid(), Name = "Cup", RegistrationType = TournamentRegistrationType.Solo,
			DiscordRoleId = "role-1", DiscordChannelId = "channel-1",
		};
		context.Tournaments.Add(tournament);
		await context.SaveChangesAsync();
		var (service, discord) = CreateService(context);

		await service.DeleteRoleAsync(tournament.Id);
		await service.DeleteChannelAsync(tournament.Id);

		Assert.Contains("role-1", discord.DeletedRoleIds);
		Assert.Contains("channel-1", discord.DeletedChannelIds);
		var reloaded = await context.Tournaments.SingleAsync(t => t.Id == tournament.Id);
		Assert.Null(reloaded.DiscordRoleId);
		Assert.Null(reloaded.DiscordChannelId);
	}
}
