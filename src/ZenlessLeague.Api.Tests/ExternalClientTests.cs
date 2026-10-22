using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.Discord;
using ZenlessLeague.Api.Features.Tournaments.Challonge;

namespace ZenlessLeague.Api.Tests;

// The real ChallongeClient/DiscordClient driven through a stubbed HttpMessageHandler — covers their
// response handling and logging without network access.
public class ExternalClientTests
{
	private const string ChallongeApiKey = "secret-challonge-key";

	[Fact]
	public async Task ChallongeClient_FailedCall_LogsResponseButNeverTheApiKey()
	{
		var logger = new ListLogger<ChallongeClient>();
		var client = new ChallongeClient(
			new HttpClient(new StubHandler(HttpStatusCode.InternalServerError, "{\"errors\":[]}")) { BaseAddress = new Uri("https://challonge.test/") },
			Options.Create(new ChallongeOptions { ApiKey = ChallongeApiKey }),
			logger);

		await Assert.ThrowsAsync<ChallongeApiException>(() => client.GetMatchesAsync("ctid"));

		var message = Assert.Single(logger.Messages);
		Assert.Contains("500", message);
		Assert.DoesNotContain(ChallongeApiKey, message);
	}

	[Fact]
	public async Task DiscordClient_RemoveMemberRole_UnknownMember_IsANoOp()
	{
		var client = CreateDiscordClient(HttpStatusCode.NotFound, "{\"message\": \"Unknown Member\", \"code\": 10007}");

		await client.RemoveMemberRoleAsync("user", "role");
		await client.AddMemberRoleAsync("user", "role");
	}

	[Fact]
	public async Task DiscordClient_AddMemberRole_UnknownRole_StillThrows()
	{
		// A misconfigured role id must surface, not be silently ignored like a departed member.
		var client = CreateDiscordClient(HttpStatusCode.NotFound, "{\"message\": \"Unknown Role\", \"code\": 10011}");

		var ex = await Assert.ThrowsAsync<DiscordApiException>(() => client.AddMemberRoleAsync("user", "role"));
		Assert.Equal(404, ex.StatusCode);
	}

	[Fact]
	public async Task ChallongeClient_GetMatches_FollowsNextLinksOnChallongeHostOnly()
	{
		var handler = new ScriptedHandler(request => request.RequestUri!.Query switch
		{
			"" => Json(HttpStatusCode.OK, MatchesPage(["1", "2"], next: "https://challonge.test/v2.1/tournaments/ctid/matches.json?page=2")),
			"?page=2" => Json(HttpStatusCode.OK, MatchesPage(["3"], next: "https://evil.test/steal?page=3")),
			_ => throw new InvalidOperationException($"Unexpected request {request.RequestUri}"),
		});
		var client = new ChallongeClient(
			new HttpClient(handler) { BaseAddress = new Uri("https://challonge.test/v2.1/") },
			Options.Create(new ChallongeOptions { ApiKey = ChallongeApiKey }),
			new ListLogger<ChallongeClient>());

		var matches = await client.GetMatchesAsync("ctid");

		Assert.Equal(["1", "2", "3"], matches.Select(m => m.ChallongeMatchId));
		// The off-host next link (which would have received the API key) was never requested.
		Assert.Equal(2, handler.Requests.Count);
	}

	[Fact]
	public async Task Resilience_TransientFailureOnIdempotentRequest_IsRetried()
	{
		var handler = new ScriptedHandler(FailFirstThen(HttpStatusCode.ServiceUnavailable, Json(HttpStatusCode.OK, "{}")));
		var client = CreateResilientChallongeClient(handler);

		// A PUT match-result report is idempotent — the same score re-sent is harmless.
		await client.UpdateMatchResultAsync("ctid", "555", "c1", "c2", p1Win: true, loserScore: "0");

		Assert.Equal(2, handler.Requests.Count);
	}

	[Fact]
	public async Task Resilience_Post_IsNeverRetried()
	{
		var handler = new ScriptedHandler(FailFirstThen(HttpStatusCode.ServiceUnavailable, Json(HttpStatusCode.OK, "{}")));
		var client = CreateResilientChallongeClient(handler);

		// A lost response to a POST may still have created the tournament — retrying could create a second one.
		await Assert.ThrowsAsync<ChallongeApiException>(() =>
			client.CreateTournamentAsync("Cup", "single elimination", null, DateTimeOffset.UtcNow, null));

		Assert.Single(handler.Requests);
	}

	[Fact]
	public async Task Resilience_ChallongeRandomize_IsNeverRetriedDespiteBeingAPut()
	{
		var handler = new ScriptedHandler(FailFirstThen(HttpStatusCode.ServiceUnavailable, Json(HttpStatusCode.OK, "{\"data\":[]}")));
		var client = CreateResilientChallongeClient(handler);

		await Assert.ThrowsAsync<ChallongeApiException>(() => client.RandomizeParticipantsAsync("ctid", [("A", "a"), ("B", "b")]));

		Assert.Single(handler.Requests);
	}

	private static IChallongeClient CreateResilientChallongeClient(ScriptedHandler handler)
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.Configure<ChallongeOptions>(o => o.ApiKey = ChallongeApiKey);
		// Same registration shape as Program.cs, with near-zero backoff so the test doesn't wait.
		services.AddHttpClient<IChallongeClient, ChallongeClient>(c => c.BaseAddress = new Uri("https://challonge.test/v2.1/"))
			.AddThirdPartyResilience(TimeSpan.FromSeconds(5), isNonIdempotent: ChallongeClient.IsNonIdempotent, retryBaseDelay: TimeSpan.FromMilliseconds(1))
			.ConfigurePrimaryHttpMessageHandler(() => handler);

		return services.BuildServiceProvider().GetRequiredService<IChallongeClient>();
	}

	private static string MatchesPage(string[] ids, string? next)
	{
		var data = string.Join(",", ids.Select(id =>
			"{\"id\": \"" + id + "\", \"attributes\": {\"round\": 1, \"state\": \"open\", \"points_by_participant\": []}}"));
		var nextJson = next is null ? "null" : $"\"{next}\"";
		return "{\"data\": [" + data + "], \"links\": {\"next\": " + nextJson + "}, \"meta\": {\"count\": 3}}";
	}

	private static Func<HttpRequestMessage, HttpResponseMessage> FailFirstThen(HttpStatusCode firstStatus, HttpResponseMessage then)
	{
		var calls = 0;
		return _ => calls++ == 0 ? Json(firstStatus, "{}") : then;
	}

	private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
		new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

	private static DiscordClient CreateDiscordClient(HttpStatusCode status, string body) =>
		new(
			new HttpClient(new StubHandler(status, body)) { BaseAddress = new Uri("https://discord.test/api/v10/") },
			Options.Create(new DiscordOptions { BotToken = "bot-token", GuildId = "guild" }));

	private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
			Task.FromResult(Json(status, body));
	}

	private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
	{
		public List<HttpRequestMessage> Requests { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
		{
			Requests.Add(request);
			return Task.FromResult(respond(request));
		}
	}

	private sealed class ListLogger<T> : ILogger<T>
	{
		public List<string> Messages { get; } = [];

		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
			Messages.Add(formatter(state, exception));
	}
}
