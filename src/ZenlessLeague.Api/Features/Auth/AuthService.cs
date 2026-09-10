using Microsoft.Extensions.Options;
using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.Discord;
using ZenlessLeague.Api.Features.Players;

namespace ZenlessLeague.Api.Features.Auth;

public interface IAuthService
{
	// Logs in (or, for a Discord id never seen before, silently registers) a player via Discord
	// OAuth, returning a Firebase custom token for the frontend to exchange via
	// signInWithCustomToken. Port of the login half of functions/discord/linkDiscord.js, merged
	// with functions/user/register.js's custom-token-minting responsibility.
	Task<string> LoginWithDiscordAsync(string code, string redirectUri, CancellationToken ct = default);

	// Port of functions/discord/linkDiscord.js's account-linking half, for an already
	// authenticated player. Returns false if no player exists for uid.
	Task<bool> LinkDiscordAsync(string uid, string code, string redirectUri, CancellationToken ct = default);

	// Port of functions/discord/unlinkDiscord.js. Returns false if no player exists for uid.
	Task<bool> UnlinkDiscordAsync(string uid, CancellationToken ct = default);
}

public class AuthService(
	IPlayerRepository playerRepository,
	IDiscordOAuthClient discordOAuthClient,
	IDiscordClient discordClient,
	IDiscordRoleAssignmentService discordRoleAssignmentService,
	IOptions<DiscordOptions> discordOptions,
	IFirebaseCustomTokenSigner tokenSigner) : IAuthService
{
	public async Task<string> LoginWithDiscordAsync(string code, string redirectUri, CancellationToken ct = default)
	{
		var discordUser = await discordOAuthClient.ExchangeCodeAsync(code, redirectUri, ct);

		var player = await playerRepository.GetEntityByDiscordIdAsync(discordUser.Id, ct);
		if (player is null)
		{
			player = await playerRepository.CreateAsync(new Player
			{
				Uid = Guid.NewGuid().ToString(),
				Name = await ResolveAvailableNameAsync(discordUser.Username, ct),
				DiscordId = discordUser.Id,
				DiscordUsername = discordUser.Username,
				CreatedAt = DateTimeOffset.UtcNow,
			}, ct);
		}

		return tokenSigner.CreateCustomToken(player.Uid);
	}

	public async Task<bool> LinkDiscordAsync(string uid, string code, string redirectUri, CancellationToken ct = default)
	{
		var player = await playerRepository.GetEntityByUidAsync(uid, ct);
		if (player is null)
		{
			return false;
		}

		var discordUser = await discordOAuthClient.ExchangeCodeAsync(code, redirectUri, ct);

		var existing = await playerRepository.GetEntityByDiscordIdAsync(discordUser.Id, ct);
		if (existing is not null && existing.Uid != uid)
		{
			throw new PlayerValidationException("This Discord account is already linked to another player.");
		}

		player.DiscordId = discordUser.Id;
		player.DiscordUsername = discordUser.Username;
		await playerRepository.SaveChangesAsync(ct);

		await discordRoleAssignmentService.AssignRoleForPlayerAsync(uid, ct);
		return true;
	}

	public async Task<bool> UnlinkDiscordAsync(string uid, CancellationToken ct = default)
	{
		var player = await playerRepository.GetEntityByUidAsync(uid, ct);
		if (player is null)
		{
			return false;
		}

		if (player.DiscordId is not null)
		{
			var opts = discordOptions.Value;
			var roleIds = new[] { opts.NewbieRoleId, opts.MidRoleId, opts.HighRoleId }
				.Where(roleId => roleId is not null);
			await Task.WhenAll(roleIds.Select(roleId => discordClient.RemoveMemberRoleAsync(player.DiscordId, roleId!, ct)));
		}

		player.DiscordId = null;
		player.DiscordUsername = null;
		await playerRepository.SaveChangesAsync(ct);
		return true;
	}

	// Auto-resolves a Discord username collision by suffixing an incrementing number — used only
	// for first-time Discord login, since the account is being silently created rather than going
	// through a form where the user could pick something else.
	private async Task<string> ResolveAvailableNameAsync(string baseName, CancellationToken ct)
	{
		if (await playerRepository.GetByNameAsync(baseName, ct) is null)
		{
			return baseName;
		}

		for (var suffix = 2; ; suffix++)
		{
			var candidate = $"{baseName}{suffix}";
			if (await playerRepository.GetByNameAsync(candidate, ct) is null)
			{
				return candidate;
			}
		}
	}
}
