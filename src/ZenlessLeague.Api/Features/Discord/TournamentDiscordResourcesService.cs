using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Teams;
using ZenlessLeague.Api.Features.Tournaments;

namespace ZenlessLeague.Api.Features.Discord;

public interface ITournamentDiscordResourcesService
{
	Task CreateResourcesAsync(Guid tournamentId, CancellationToken ct = default);

	Task DeleteRoleAsync(Guid tournamentId, CancellationToken ct = default);
	Task DeleteChannelAsync(Guid tournamentId, CancellationToken ct = default);
}

public class TournamentDiscordResourcesService(
	ITournamentRepository tournamentRepository,
	ITournamentRegistrationRepository registrationRepository,
	ITeamRepository teamRepository,
	IPlayerRepository playerRepository,
	IDiscordClient discordClient,
	IOptions<DiscordOptions> options,
	ILogger<TournamentDiscordResourcesService> logger) : ITournamentDiscordResourcesService
{
	private const int PermissionViewChannel = 0x400;
	private const int PermissionSendMessages = 0x800;
	private const int PermissionAddReactions = 0x40;
	private static readonly string ChannelPermissions = (PermissionViewChannel | PermissionSendMessages | PermissionAddReactions).ToString();

	public async Task CreateResourcesAsync(Guid tournamentId, CancellationToken ct = default)
	{
		var tournament = await tournamentRepository.GetByIdAsync(tournamentId, ct);
		if (tournament is null || (tournament.DiscordRoleName is null && tournament.DiscordChannelName is null))
		{
			return;
		}

		var opts = options.Value;
		var roleId = tournament.DiscordRoleId;

		if (tournament.DiscordRoleName is not null && roleId is null)
		{
			roleId = await discordClient.CreateGuildRoleAsync(tournament.DiscordRoleName, ct);
			tournament.DiscordRoleId = roleId;
			await tournamentRepository.SaveChangesAsync(ct);
		}

		if (tournament.DiscordChannelName is not null && tournament.DiscordChannelId is null)
		{
			var guildId = opts.GuildId ?? throw new InvalidOperationException("Discord:GuildId is not configured.");
			var permissionOverwrites = new List<DiscordPermissionOverwrite> { new(guildId, 0, Deny: ChannelPermissions) };
			if (roleId is not null)
			{
				permissionOverwrites.Add(new DiscordPermissionOverwrite(roleId, 0, Allow: ChannelPermissions));
			}

			permissionOverwrites.AddRange(opts.TournamentStaffRoleIds.Select(id => new DiscordPermissionOverwrite(id, 0, Allow: ChannelPermissions)));

			var channelId = await discordClient.CreateGuildChannelAsync(tournament.DiscordChannelName, opts.TournamentCategoryId, permissionOverwrites, ct);
			tournament.DiscordChannelId = channelId;
			await tournamentRepository.SaveChangesAsync(ct);
		}

		if (roleId is not null)
		{
			// Sequential, not Task.WhenAll — these all hit the same Discord rate-limit bucket, so
			// firing them concurrently just means every one of them 429s at once instead of one
			// request at a time each paying its own (usually nonexistent) wait.
			var discordIds = await GetApprovedDiscordIdsAsync(tournamentId, ct);
			foreach (var discordId in discordIds)
			{
				try
				{
					await discordClient.AddMemberRoleAsync(discordId, roleId, ct);
				}
				catch (DiscordApiException ex)
				{
					// One player's Discord state (left the server, revoked the bot's access, etc.)
					// shouldn't block the tournament from starting for everyone else — log and move
					// on rather than letting this abort the whole retry sequence.
					logger.LogWarning(ex, "Failed to assign Discord role {RoleId} to member {DiscordId} for tournament {TournamentId} — skipping.", roleId, discordId, tournamentId);
				}
			}
		}
	}

	public async Task DeleteRoleAsync(Guid tournamentId, CancellationToken ct = default)
	{
		var tournament = await tournamentRepository.GetByIdAsync(tournamentId, ct);
		if (tournament?.DiscordRoleId is null)
		{
			return;
		}

		await discordClient.DeleteGuildRoleAsync(tournament.DiscordRoleId, ct);
		tournament.DiscordRoleId = null;
		await tournamentRepository.SaveChangesAsync(ct);
	}

	public async Task DeleteChannelAsync(Guid tournamentId, CancellationToken ct = default)
	{
		var tournament = await tournamentRepository.GetByIdAsync(tournamentId, ct);
		if (tournament?.DiscordChannelId is null)
		{
			return;
		}

		await discordClient.DeleteGuildChannelAsync(tournament.DiscordChannelId, ct);
		tournament.DiscordChannelId = null;
		await tournamentRepository.SaveChangesAsync(ct);
	}

	private async Task<List<string>> GetApprovedDiscordIdsAsync(Guid tournamentId, CancellationToken ct)
	{
		var approved = (await registrationRepository.ListByTournamentAsync(tournamentId, ct)).Where(r => r.Approved).ToList();
		var playerIds = await teamRepository.ExpandToPlayerIdsAsync(approved.Select(r => (r.PlayerId, r.TeamId)), ct);

		var discordIds = new List<string>();
		foreach (var id in playerIds.Distinct())
		{
			var player = await playerRepository.GetEntityByIdAsync(id, ct);
			if (player?.DiscordId is not null)
			{
				discordIds.Add(player.DiscordId);
			}
		}

		return discordIds;
	}
}
