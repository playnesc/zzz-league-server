using Microsoft.Extensions.Options;
using ZenlessLeague.Api.Features.Discord;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Infrastructure.Firebase;

namespace ZenlessLeague.Api.Features.Auth;

public interface IAuthService
{
	// Logs in (or, for a Discord id never seen before, silently registers) a player via Discord
	// OAuth, returning a Firebase custom token for the frontend to exchange via signInWithCustomToken.
	Task<string> LoginWithDiscordAsync(string code, string redirectUri, CancellationToken ct = default);

	// Returns the updated player, or null if no player exists for uid.
	Task<PlayerDto?> LinkDiscordAsync(string uid, string code, string redirectUri, CancellationToken ct = default);
	Task<PlayerDto?> UnlinkDiscordAsync(string uid, CancellationToken ct = default);
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

	public async Task<PlayerDto?> LinkDiscordAsync(string uid, string code, string redirectUri, CancellationToken ct = default)
	{
		var player = await playerRepository.GetEntityByUidAsync(uid, ct);
		if (player is null)
		{
			return null;
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
		return PlayerService.ToDto(player);
	}

	public async Task<PlayerDto?> UnlinkDiscordAsync(string uid, CancellationToken ct = default)
	{
		var player = await playerRepository.GetEntityByUidAsync(uid, ct);
		if (player is null)
		{
			return null;
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
		return PlayerService.ToDto(player);
	}

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
