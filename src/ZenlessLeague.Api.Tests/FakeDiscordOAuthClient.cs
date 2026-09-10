using ZenlessLeague.Api.Features.Discord;

namespace ZenlessLeague.Api.Tests;

// Hand-rolled test double — returns a canned Discord identity instead of doing a real OAuth code
// exchange, so AuthService's login/link logic can be tested without a real Discord app.
public class FakeDiscordOAuthClient : IDiscordOAuthClient
{
	public DiscordOAuthUser NextUser { get; set; } = new("discord-id", "DiscordUser");
	public (string Code, string RedirectUri)? LastExchange { get; private set; }

	public Task<DiscordOAuthUser> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default)
	{
		LastExchange = (code, redirectUri);
		return Task.FromResult(NextUser);
	}
}
