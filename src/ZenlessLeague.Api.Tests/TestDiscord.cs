using Microsoft.Extensions.Options;
using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.Discord;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Teams;
using ZenlessLeague.Api.Features.Tournaments;

namespace ZenlessLeague.Api.Tests;

// Shared construction for the real Discord services wired to a FakeDiscordClient — safe to use
// in any test where players/tournaments don't have Discord ids/role names set, since those
// services no-op before ever touching IDiscordClient in that case.
public static class TestDiscord
{
    public static IDiscordRoleAssignmentService CreateRoleAssignmentService(AppDbContext context) =>
        new DiscordRoleAssignmentService(new PlayerRepository(context), new FakeDiscordClient(), Options.Create(new DiscordOptions()));

    public static ITournamentDiscordResourcesService CreateTournamentResourcesService(AppDbContext context) =>
        new TournamentDiscordResourcesService(
            new TournamentRepository(context),
            new TournamentRegistrationRepository(context),
            new TeamRepository(context),
            new PlayerRepository(context),
            new FakeDiscordClient(),
            Options.Create(new DiscordOptions()));
}
