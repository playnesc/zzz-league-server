using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

	private sealed class ListLogger<T> : ILogger<T>
	{
		public List<string> Messages { get; } = [];

		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
			Messages.Add(formatter(state, exception));
	}
}
