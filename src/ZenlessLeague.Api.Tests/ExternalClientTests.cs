using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZenlessLeague.Api.Features.Discord;
using ZenlessLeague.Api.Infrastructure.Http;

namespace ZenlessLeague.Api.Tests;

// The real DiscordClient and the shared HTTP resilience policy driven through a stubbed
// HttpMessageHandler — covers response handling and retry rules without network access.
public class ExternalClientTests
{
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
	public async Task Resilience_TransientFailureOnIdempotentRequest_IsRetried()
	{
		var handler = new ScriptedHandler(FailFirstThen(HttpStatusCode.ServiceUnavailable, Json(HttpStatusCode.OK, "{}")));
		var client = CreateResilientClient(handler);

		var response = await client.PutAsync("things/1", new StringContent("{}"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal(2, handler.Requests.Count);
	}

	[Fact]
	public async Task Resilience_Post_IsNeverRetried()
	{
		var handler = new ScriptedHandler(FailFirstThen(HttpStatusCode.ServiceUnavailable, Json(HttpStatusCode.OK, "{}")));
		var client = CreateResilientClient(handler);

		// A lost response to a POST may still have created the resource — retrying could create a second one.
		var response = await client.PostAsync("things", new StringContent("{}"));

		Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
		Assert.Single(handler.Requests);
	}

	[Fact]
	public async Task Resilience_PutMarkedNonIdempotent_IsNeverRetried()
	{
		var handler = new ScriptedHandler(FailFirstThen(HttpStatusCode.ServiceUnavailable, Json(HttpStatusCode.OK, "{}")));
		var client = CreateResilientClient(handler, isNonIdempotent: r => r.RequestUri!.AbsolutePath.EndsWith("/shuffle", StringComparison.Ordinal));

		var response = await client.PutAsync("things/shuffle", new StringContent("{}"));

		Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
		Assert.Single(handler.Requests);
	}

	// Same registration shape as the real typed clients, with near-zero backoff so the test doesn't wait.
	private static HttpClient CreateResilientClient(ScriptedHandler handler, Func<HttpRequestMessage, bool>? isNonIdempotent = null)
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddHttpClient("test", c => c.BaseAddress = new Uri("https://api.test/"))
			.AddThirdPartyResilience(TimeSpan.FromSeconds(5), isNonIdempotent: isNonIdempotent, retryBaseDelay: TimeSpan.FromMilliseconds(1))
			.ConfigurePrimaryHttpMessageHandler(() => handler);

		return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("test");
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
}
