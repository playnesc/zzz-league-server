using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ZenlessLeague.Api.Features.Auth;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Teams;
using ZenlessLeague.Api.Features.Tournaments;
using ZenlessLeague.Api.Infrastructure.Http;
using ZenlessLeague.Api.Tests;

namespace ZenlessLeague.Api.IntegrationTests;

// Who may call what, checked against the real app: the routing table, the auth pipeline, and
// roles read from the database. Requests carry real session tokens minted by the app's own
// SessionTokenIssuer, so nothing in the auth path is faked.
[Collection(PostgresCollection.Name)]
public partial class EndpointAuthorizationTests(PostgresFixture postgres) : IAsyncLifetime
{
	private const string Anonymous = "anonymous";
	private const string SignedIn = "signed-in";
	private const string Moderator = "Moderator";
	private const string Admin = "Admin";
	private const string FirebaseOnly = "firebase-only";

	// Every endpoint's access rule. A new endpoint, or a changed rule, fails
	// EveryEndpoint_HasItsReviewedAccessRule until it's added here — so each one gets a deliberate
	// decision instead of silently shipping open. "signed-in" endpoints may still check ownership
	// in the handler (see the owner tests below).
	private static readonly Dictionary<string, string> ExpectedAccess = new()
	{
		["GET /health"] = Anonymous,
		["GET /health/ready"] = Anonymous,
		["GET /openapi/{documentName}.json"] = Anonymous, // Development only

		["POST /api/auth/discord/login"] = Anonymous,
		["POST /api/auth/session"] = FirebaseOnly,
		["POST /api/players/me/discord/link"] = SignedIn,
		["POST /api/players/me/discord/unlink"] = SignedIn,

		["POST /api/players"] = Admin,
		["GET /api/players"] = Anonymous,
		["GET /api/players/staff"] = Admin,
		["GET /api/players/me"] = SignedIn,
		["GET /api/players/me/last-registration"] = SignedIn,
		["GET /api/players/{uid}"] = SignedIn,
		["DELETE /api/players/{uid}"] = Admin,
		["PUT /api/players/{uid}/role"] = Admin,
		["PUT /api/players/{uid}/name"] = SignedIn,
		["PUT /api/players/{uid}/avatar"] = SignedIn,
		["PUT /api/players/{uid}/elo"] = Admin,

		["POST /api/teams"] = SignedIn,
		["GET /api/teams"] = Anonymous,
		["GET /api/teams/by-player/{uid}"] = Anonymous,
		["GET /api/teams/{id:guid}"] = Anonymous,
		["PUT /api/teams/{id:guid}"] = SignedIn,
		["DELETE /api/teams/{id:guid}"] = SignedIn,

		["GET /api/community-costs/agents"] = Anonymous,
		["GET /api/community-costs/agents/export.csv"] = Anonymous,
		["POST /api/community-costs/agents/import"] = Admin,
		["GET /api/community-costs/engines"] = Anonymous,
		["GET /api/community-costs/engines/export.csv"] = Anonymous,
		["POST /api/community-costs/engines/import"] = Admin,
		["GET /api/community-costs/contributions"] = Anonymous,
		["GET /api/community-costs/contributions/{id:guid}"] = Anonymous,
		["POST /api/community-costs/contributions/agent"] = SignedIn,
		["POST /api/community-costs/contributions/engine"] = SignedIn,
		["PUT /api/community-costs/contributions/{id:guid}/review"] = SignedIn,
		["POST /api/community-costs/contributions/{id:guid}/reviews/{reviewId:guid}/comments"] = SignedIn,
		["POST /api/community-costs/contributions/{id:guid}/approve"] = Moderator,
		["POST /api/community-costs/contributions/{id:guid}/reject"] = Moderator,

		["POST /api/tournaments"] = Admin,
		["GET /api/tournaments"] = Anonymous,
		["GET /api/tournaments/paged"] = Anonymous,
		["GET /api/tournaments/{id:guid}"] = Anonymous,
		["PUT /api/tournaments/{id:guid}"] = Admin,
		["DELETE /api/tournaments/{id:guid}"] = Admin,
		["POST /api/tournaments/{id:guid}/close-registration"] = Admin,
		["POST /api/tournaments/{id:guid}/challonge-bracket"] = Admin,
		["POST /api/tournaments/{id:guid}/start"] = Admin,
		["POST /api/tournaments/{id:guid}/finish"] = Admin,
		["POST /api/tournaments/{id:guid}/refresh-games"] = Admin,
		["POST /api/tournaments/{id:guid}/increment-count"] = Admin,
		["POST /api/tournaments/{id:guid}/increment-seasonal-count"] = Admin,
		["POST /api/tournaments/{id:guid}/split"] = Admin,

		["POST /api/tournaments/{id:guid}/registrations/solo"] = SignedIn,
		["POST /api/tournaments/{id:guid}/registrations/team"] = SignedIn,
		["GET /api/tournaments/{id:guid}/registrations"] = Anonymous,
		["GET /api/tournaments/{id:guid}/registrations/{registrationId:guid}"] = Anonymous,
		["POST /api/tournaments/{id:guid}/registrations/admin-add"] = Admin,
		["POST /api/tournaments/{id:guid}/registrations/admin-add-team"] = Admin,
		["PUT /api/tournaments/{id:guid}/registrations/{registrationId:guid}/approve"] = Admin,
		["DELETE /api/tournaments/{id:guid}/registrations"] = SignedIn,

		["GET /api/tournaments/{id:guid}/matches"] = Anonymous,
		["GET /api/tournaments/{id:guid}/matches/{matchId:guid}"] = Anonymous,
		["POST /api/tournaments/{id:guid}/matches/{matchId:guid}/result"] = SignedIn,
		["POST /api/tournaments/{id:guid}/matches/{matchId:guid}/nexus-preview"] = SignedIn,
		["POST /api/tournaments/{id:guid}/matches/{matchId:guid}/admin-result"] = Moderator,

		["POST /api/history/matches"] = Admin,
		["GET /api/history"] = Anonymous,
		["GET /api/history/player/{uid}"] = Anonymous,
		["GET /api/history/elo-preview"] = Admin,
		["DELETE /api/history/{id:guid}"] = Admin,

		["POST /api/seasons/finalize"] = Admin,
		["POST /api/seasons/reset"] = Admin,
		["POST /api/seasons/backfill-last-played-timestamps"] = Admin,
		["GET /api/seasons/timer"] = Anonymous,
		["PUT /api/seasons/timer"] = Admin,
		["GET /api/archives"] = Anonymous,
		["DELETE /api/archives/{seasonName}"] = Admin,

		["ANY /hubs/tournaments/negotiate"] = SignedIn,
		["ANY /hubs/tournaments"] = SignedIn,
	};

	private string database = null!;
	private WebApplicationFactory<Program> factory = null!;

	public async Task InitializeAsync()
	{
		database = await postgres.CreateDatabaseAsync();
		factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
		{
			builder.UseEnvironment("Development");
			builder.UseSetting("ConnectionStrings:Default", postgres.ConnectionString(database));
		});
	}

	public async Task DisposeAsync() => await factory.DisposeAsync();

	[Fact]
	public void EveryEndpoint_HasItsReviewedAccessRule()
	{
		var actual = ActualAccess().ToDictionary(e => e.Key, e => e.Access);

		var missing = ExpectedAccess.Keys.Except(actual.Keys).ToList();
		var unreviewed = actual.Keys.Except(ExpectedAccess.Keys).Select(k => $"{k} => {actual[k]}").ToList();
		var changed = actual.Keys.Intersect(ExpectedAccess.Keys)
			.Where(k => actual[k] != ExpectedAccess[k])
			.Select(k => $"{k}: expected {ExpectedAccess[k]}, actual {actual[k]}")
			.ToList();

		Assert.True(missing.Count == 0 && unreviewed.Count == 0 && changed.Count == 0,
			$"Endpoint access differs from the reviewed table.\n" +
			$"Not mapped any more: {string.Join("; ", missing)}\n" +
			$"New, not reviewed yet: {string.Join("; ", unreviewed)}\n" +
			$"Changed: {string.Join("; ", changed)}");
	}

	[Fact]
	public async Task ProtectedEndpoints_RejectAnonymousCallers()
	{
		var client = factory.CreateClient();
		var failures = new List<string>();

		foreach (var endpoint in Reviewed().Where(e => e.Access != Anonymous))
		{
			var response = await SendAsync(client, endpoint, token: null);
			if (response.StatusCode != HttpStatusCode.Unauthorized)
			{
				failures.Add($"{endpoint.Key} -> {(int)response.StatusCode}");
			}
		}

		Assert.Empty(failures);
	}

	[Fact]
	public async Task RoleGatedEndpoints_RejectLowerRoles()
	{
		await SeedPlayersAsync(("player", PlayerRole.Player), ("moderator", PlayerRole.Moderator));
		var client = factory.CreateClient();
		var failures = new List<string>();

		foreach (var endpoint in Reviewed().Where(e => e.Access is Admin or Moderator))
		{
			var lowerRoles = endpoint.Access == Admin ? new[] { "player", "moderator" } : ["player"];
			foreach (var uid in lowerRoles)
			{
				var response = await SendAsync(client, endpoint, TokenFor(uid));
				if (response.StatusCode != HttpStatusCode.Forbidden)
				{
					failures.Add($"{endpoint.Key} as {uid} -> {(int)response.StatusCode}");
				}
			}
		}

		Assert.Empty(failures);
	}

	// Anything a regular user (or an anonymous caller) can write to has an abuse limit. Admin and
	// moderator endpoints are exempt — bulk tournament management legitimately makes many calls.
	[Fact]
	public void EveryWriteEndpointOpenToRegularUsers_IsRateLimited()
	{
		var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
		var unlimited = ActualAccess()
			.Where(e => e.Method is not ("GET" or "ANY") && e.Access is not (Admin or Moderator))
			.Where(e => !endpoints.Any(endpoint =>
				"/" + endpoint.RoutePattern.RawText!.Trim('/') == e.Route &&
				(endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(e.Method) ?? false) &&
				endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>() is not null))
			.Select(e => e.Key)
			.ToList();

		Assert.Empty(unlimited);
	}

	[Fact]
	public async Task UserWrites_AreLimitedPerUser()
	{
		await SeedPlayersAsync(("alice", PlayerRole.Player), ("bob", PlayerRole.Player));
		var client = factory.CreateClient();

		for (var i = 0; i < RateLimits.UserWritesPermitsPerMinute; i++)
		{
			Assert.Equal(HttpStatusCode.OK, (await PutAsync(client, "/api/players/alice/name", new { name = $"Alice {i}" }, "alice")).StatusCode);
		}

		var limited = await PutAsync(client, "/api/players/alice/name", new { name = "One too many" }, "alice");
		Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
		Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
		Assert.Contains("\"error\"", await limited.Content.ReadAsStringAsync());

		// Someone else's budget is untouched (all requests here come from the same test-server address).
		Assert.Equal(HttpStatusCode.OK, (await PutAsync(client, "/api/players/bob/name", new { name = "Bob" }, "bob")).StatusCode);
	}

	[Fact]
	public async Task PlayerProfile_OnlyItsOwnerCanChangeItOrReadTheFullRecord()
	{
		await SeedPlayersAsync(("alice", PlayerRole.Player), ("bob", PlayerRole.Player), ("admin", PlayerRole.Admin));
		var client = factory.CreateClient();

		Assert.Equal(HttpStatusCode.Forbidden, (await PutAsync(client, "/api/players/bob/name", new { name = "pwned" }, "alice")).StatusCode);
		Assert.Equal(HttpStatusCode.Forbidden, (await PutAsync(client, "/api/players/bob/avatar", new { avatar = "x" }, "alice")).StatusCode);
		Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(client, "/api/players/bob", "alice")).StatusCode);

		Assert.Equal(HttpStatusCode.OK, (await PutAsync(client, "/api/players/alice/name", new { name = "Alice" }, "alice")).StatusCode);
		Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, "/api/players/bob", "admin")).StatusCode);

		await using var db = postgres.CreateContext(database);
		Assert.Equal("bob", db.Players.Single(p => p.Uid == "bob").Name);
	}

	[Fact]
	public async Task Teams_OnlyMembersOrAnAdminCanCreateEditOrDeleteThem()
	{
		await SeedPlayersAsync(("alice", PlayerRole.Player), ("bob", PlayerRole.Player), ("mallory", PlayerRole.Player), ("admin", PlayerRole.Admin));
		Guid teamId;
		await using (var db = postgres.CreateContext(database))
		{
			var team = new Team
			{
				Id = Guid.NewGuid(),
				Name = "Alice & Bob",
				Player1Id = db.Players.Single(p => p.Uid == "alice").Id,
				Player2Id = db.Players.Single(p => p.Uid == "bob").Id,
				CreatedAt = DateTimeOffset.UtcNow,
			};
			db.Teams.Add(team);
			await db.SaveChangesAsync();
			teamId = team.Id;
		}

		var client = factory.CreateClient();

		// Creating a team on someone else's behalf.
		Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(client, "/api/teams",
			new { creatorPlayerId = "alice", player2PlayerId = "bob", name = "Fake" }, "mallory")).StatusCode);
		Assert.Equal(HttpStatusCode.Forbidden, (await PutAsync(client, $"/api/teams/{teamId}", new { name = "Mallory's" }, "mallory")).StatusCode);
		Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Delete, $"/api/teams/{teamId}", null, "mallory")).StatusCode);

		Assert.Equal(HttpStatusCode.OK, (await PutAsync(client, $"/api/teams/{teamId}", new { name = "Renamed by Bob" }, "bob")).StatusCode);
		Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, $"/api/teams/{teamId}", null, "admin")).StatusCode);
	}

	[Fact]
	public async Task SoloRegistration_CannotBeMadeForSomeoneElse()
	{
		await SeedPlayersAsync(("alice", PlayerRole.Player), ("bob", PlayerRole.Player));
		var client = factory.CreateClient();

		var response = await PostAsync(client, $"/api/tournaments/{Guid.NewGuid()}/registrations/solo",
			new { playerId = "bob", details = new { } }, "alice");

		Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
	}

	[Fact]
	public async Task HiddenTournament_IsNotFoundBelowModerator()
	{
		await SeedPlayersAsync(("player", PlayerRole.Player), ("moderator", PlayerRole.Moderator));
		var tournamentId = Guid.NewGuid();
		await using (var db = postgres.CreateContext(database))
		{
			db.Tournaments.Add(new Tournament { Id = tournamentId, Name = "Secret", Visible = false });
			await db.SaveChangesAsync();
		}

		var client = factory.CreateClient();

		foreach (var path in new[] { $"/api/tournaments/{tournamentId}", $"/api/tournaments/{tournamentId}/matches", $"/api/tournaments/{tournamentId}/registrations" })
		{
			Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(client, path, null)).StatusCode);
			Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(client, path, "player")).StatusCode);
			Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, path, "moderator")).StatusCode);
		}

		var listed = await (await GetAsync(client, "/api/tournaments", "player")).Content.ReadFromJsonAsync<List<TournamentListEntry>>();
		Assert.DoesNotContain(listed!, t => t.Id == tournamentId);
	}

	private sealed record TournamentListEntry(Guid Id);

	private sealed record EndpointAccess(string Method, string Route, string Access)
	{
		public string Key => $"{Method} {Route}";
	}

	// The sweeps run off the reviewed table rather than the live routing table, so a weakened rule
	// fails them as well as the snapshot test.
	private static IEnumerable<EndpointAccess> Reviewed() =>
		ExpectedAccess.Select(e => e.Key.Split(' ', 2) is [var method, var route] ? new EndpointAccess(method, route, e.Value) : throw new FormatException(e.Key));

	private IEnumerable<EndpointAccess> ActualAccess() =>
		factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
			.OfType<RouteEndpoint>()
			.SelectMany(endpoint =>
			{
				var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["ANY"];
				var route = "/" + endpoint.RoutePattern.RawText!.Trim('/');
				return methods.Select(method => new EndpointAccess(method, route, DescribeAccess(endpoint)));
			})
			.DistinctBy(e => e.Key);

	private static string DescribeAccess(Endpoint endpoint)
	{
		if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
		{
			return Anonymous;
		}

		// RequireAuthorization(policyObject) — only /api/auth/session, which pins the Firebase scheme.
		if (endpoint.Metadata.GetMetadata<AuthorizationPolicy>() is { } policy)
		{
			return policy.AuthenticationSchemes.SequenceEqual(["Bearer"]) ? FirebaseOnly : "custom policy";
		}

		var authorizeData = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
		if (authorizeData.Count == 0)
		{
			return Anonymous;
		}

		var policies = authorizeData.Select(a => a.Policy).OfType<string>().Distinct().ToList();
		return policies.Count == 0 ? SignedIn : string.Join("+", policies);
	}

	private async Task SeedPlayersAsync(params (string Uid, PlayerRole Role)[] players)
	{
		await using var db = postgres.CreateContext(database);
		foreach (var (uid, role) in players)
		{
			TestPlayers.Create(db, uid).Role = role;
		}

		await db.SaveChangesAsync();
	}

	private string TokenFor(string uid) => factory.Services.GetRequiredService<ISessionTokenIssuer>().Issue(uid).Token;

	// Route parameters get placeholder values; with no real entity behind them, an authorized
	// request would 404/400 — any 401/403 therefore comes from the auth pipeline, not the handler.
	private Task<HttpResponseMessage> SendAsync(HttpClient client, EndpointAccess endpoint, string? token)
	{
		var path = GuidParameter().Replace(endpoint.Route, _ => Guid.NewGuid().ToString());
		path = OtherParameter().Replace(path, "x");
		var method = endpoint.Method == "ANY" ? HttpMethod.Get : new HttpMethod(endpoint.Method);
		var body = method == HttpMethod.Get || method == HttpMethod.Delete ? null : new { };
		return SendRawAsync(client, method, path, body, token);
	}

	private Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string? uid) =>
		SendAsync(client, HttpMethod.Get, path, null, uid);

	private Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body, string uid) =>
		SendAsync(client, HttpMethod.Post, path, body, uid);

	private Task<HttpResponseMessage> PutAsync(HttpClient client, string path, object body, string uid) =>
		SendAsync(client, HttpMethod.Put, path, body, uid);

	private Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body, string? uid) =>
		SendRawAsync(client, method, path, body, uid is null ? null : TokenFor(uid));

	private static Task<HttpResponseMessage> SendRawAsync(HttpClient client, HttpMethod method, string path, object? body, string? token)
	{
		var request = new HttpRequestMessage(method, path);
		if (body is not null)
		{
			request.Content = JsonContent.Create(body);
		}

		if (token is not null)
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		}

		return client.SendAsync(request);
	}

	[GeneratedRegex(@"\{\w+:guid\}")]
	private static partial Regex GuidParameter();

	[GeneratedRegex(@"\{\w+\}")]
	private static partial Regex OtherParameter();
}
