using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Features.Tournaments;
using ZenlessLeague.Api.Features.Tournaments.Realtime;
using ZenlessLeague.Api.Infrastructure.Persistence;

namespace ZenlessLeague.Api.Tests;

public class TournamentHubTests
{
	private static AppDbContext CreateContext() =>
		new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

	private static async Task<Guid> SeedTournamentAsync(AppDbContext context, bool visible)
	{
		var tournament = new Tournament { Id = Guid.NewGuid(), Name = "T", Visible = visible };
		context.Tournaments.Add(tournament);
		await context.SaveChangesAsync();
		return tournament.Id;
	}

	private static (TournamentHub Hub, FakeGroupManager Groups) CreateHub(AppDbContext context, bool isModerator)
	{
		var groups = new FakeGroupManager();
		var hub = new TournamentHub(new TournamentRepository(context), new FixedAuthorizationService(isModerator))
		{
			Context = new FakeCallerContext(),
			Groups = groups,
		};
		return (hub, groups);
	}

	[Fact]
	public async Task JoinTournament_VisibleTournament_JoinsItsGroup()
	{
		await using var context = CreateContext();
		var id = await SeedTournamentAsync(context, visible: true);
		var (hub, groups) = CreateHub(context, isModerator: false);

		await hub.JoinTournament(id);

		Assert.Equal([TournamentHub.GroupName(id)], groups.Joined);
	}

	[Fact]
	public async Task JoinTournament_HiddenOrUnknownTournament_IsRefusedBelowModerator()
	{
		await using var context = CreateContext();
		var hidden = await SeedTournamentAsync(context, visible: false);
		var (hub, groups) = CreateHub(context, isModerator: false);

		var hiddenError = await Assert.ThrowsAsync<HubException>(() => hub.JoinTournament(hidden));
		var unknownError = await Assert.ThrowsAsync<HubException>(() => hub.JoinTournament(Guid.NewGuid()));

		// Indistinguishable, so the refusal doesn't reveal which ids are hidden tournaments.
		Assert.Equal(unknownError.Message, hiddenError.Message);
		Assert.Empty(groups.Joined);
	}

	[Fact]
	public async Task JoinTournament_HiddenTournament_ModeratorJoins()
	{
		await using var context = CreateContext();
		var hidden = await SeedTournamentAsync(context, visible: false);
		var (hub, groups) = CreateHub(context, isModerator: true);

		await hub.JoinTournament(hidden);

		Assert.Equal([TournamentHub.GroupName(hidden)], groups.Joined);
	}

	private sealed class FixedAuthorizationService(bool succeed) : IAuthorizationService
	{
		public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) =>
			Task.FromResult(succeed ? AuthorizationResult.Success() : AuthorizationResult.Failed());

		public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) =>
			Task.FromResult(succeed ? AuthorizationResult.Success() : AuthorizationResult.Failed());
	}

	private sealed class FakeGroupManager : IGroupManager
	{
		public List<string> Joined { get; } = [];

		public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
		{
			Joined.Add(groupName);
			return Task.CompletedTask;
		}

		public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
		{
			Joined.Remove(groupName);
			return Task.CompletedTask;
		}
	}

	private sealed class FakeCallerContext : HubCallerContext
	{
		public override string ConnectionId => "connection-1";
		public override string? UserIdentifier => "p1";
		public override ClaimsPrincipal? User { get; } = new(new ClaimsIdentity([new Claim("sub", "p1")], "test"));
		public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
		public override IFeatureCollection Features { get; } = new FeatureCollection();
		public override CancellationToken ConnectionAborted => CancellationToken.None;
		public override void Abort() { }
	}
}
