using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace ZenlessLeague.Api.Features.Discord;

public record DiscordOAuthUser(string Id, string Username);

public interface IDiscordOAuthClient
{
	// Exchanges an OAuth2 authorization code for the authenticated Discord user's identity. Ports
	// functions/discord/linkDiscord.js's token exchange + discordClient.js's getDiscordUser — used
	// for both login (Features/Auth) and account linking. Kept separate from the bot-token
	// IDiscordClient since this authenticates as the end user, not the bot.
	Task<DiscordOAuthUser> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default);
}

public class DiscordOAuthClient(HttpClient httpClient, IOptions<DiscordOptions> options) : IDiscordOAuthClient
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	public async Task<DiscordOAuthUser> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default)
	{
		var opts = options.Value;
		var clientId = opts.ClientId ?? throw new InvalidOperationException("Discord:ClientId is not configured.");
		var clientSecret = opts.ClientSecret ?? throw new InvalidOperationException("Discord:ClientSecret is not configured.");

		// Unversioned, unlike the bot API — this client's base address is just "https://discord.com/api/".
		var tokenResponse = await httpClient.PostAsync("oauth2/token", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["client_id"] = clientId,
			["client_secret"] = clientSecret,
			["grant_type"] = "authorization_code",
			["code"] = code,
			["redirect_uri"] = redirectUri,
		}), ct);

		if (!tokenResponse.IsSuccessStatusCode)
		{
			throw new DiscordApiException((int)tokenResponse.StatusCode, await tokenResponse.Content.ReadAsStringAsync(ct));
		}

		var tokenBody = await tokenResponse.Content.ReadFromJsonAsync<DiscordTokenResponse>(JsonOptions, ct)
			?? throw new InvalidOperationException("Discord token endpoint returned an empty response.");

		using var userRequest = new HttpRequestMessage(HttpMethod.Get, "v10/users/@me");
		userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenBody.AccessToken);
		var userResponse = await httpClient.SendAsync(userRequest, ct);

		if (!userResponse.IsSuccessStatusCode)
		{
			throw new DiscordApiException((int)userResponse.StatusCode, await userResponse.Content.ReadAsStringAsync(ct));
		}

		return await userResponse.Content.ReadFromJsonAsync<DiscordOAuthUser>(JsonOptions, ct)
			?? throw new InvalidOperationException("Discord user endpoint returned an empty response.");
	}

	private record DiscordTokenResponse([property: JsonPropertyName("access_token")] string AccessToken);
}
