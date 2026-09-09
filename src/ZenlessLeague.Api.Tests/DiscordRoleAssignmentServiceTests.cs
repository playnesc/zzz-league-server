using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.Discord;
using ZenlessLeague.Api.Features.Players;

namespace ZenlessLeague.Api.Tests;

public class DiscordRoleAssignmentServiceTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static readonly DiscordOptions TestOptions = new()
    {
        NewbieRoleId = "newbie",
        MidRoleId = "mid",
        HighRoleId = "high",
    };

    private static (DiscordRoleAssignmentService Service, FakeDiscordClient Discord) CreateService(AppDbContext context)
    {
        var discord = new FakeDiscordClient();
        return (new DiscordRoleAssignmentService(new PlayerRepository(context), discord, Options.Create(TestOptions)), discord);
    }

    [Fact]
    public async Task AssignRoleForPlayerAsync_NoLinkedDiscordAccount_NoOps()
    {
        await using var context = CreateContext();
        context.Players.Add(new Player { Uid = "p1", Name = "p1", CreatedAt = DateTimeOffset.UtcNow });
        await context.SaveChangesAsync();
        var (service, discord) = CreateService(context);

        await service.AssignRoleForPlayerAsync("p1");

        Assert.Empty(discord.AddedMemberRoles);
        Assert.Empty(discord.RemovedMemberRoles);
    }

    [Fact]
    public async Task AssignRoleForPlayerAsync_Unconfirmed_GetsNewbieRole()
    {
        await using var context = CreateContext();
        context.Players.Add(new Player { Uid = "p1", Name = "p1", DiscordId = "d1", CreatedAt = DateTimeOffset.UtcNow });
        await context.SaveChangesAsync();
        var (service, discord) = CreateService(context);

        await service.AssignRoleForPlayerAsync("p1");

        Assert.Contains(("d1", "newbie"), discord.AddedMemberRoles);
        Assert.Contains(("d1", "mid"), discord.RemovedMemberRoles);
        Assert.Contains(("d1", "high"), discord.RemovedMemberRoles);
    }

    [Fact]
    public async Task AssignRoleForPlayerAsync_HighConfirmed_GetsHighRoleOnly()
    {
        await using var context = CreateContext();
        context.Players.Add(new Player
        {
            Uid = "p1", Name = "p1", DiscordId = "d1", IsMidConfirmed = true, IsHighConfirmed = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();
        var (service, discord) = CreateService(context);

        await service.AssignRoleForPlayerAsync("p1");

        Assert.Contains(("d1", "high"), discord.AddedMemberRoles);
        Assert.Contains(("d1", "newbie"), discord.RemovedMemberRoles);
        Assert.Contains(("d1", "mid"), discord.RemovedMemberRoles);
    }
}
