using Microsoft.Extensions.Options;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Teams;
using ZenlessLeague.Api.Features.Tournaments;

namespace ZenlessLeague.Api.Features.Discord;

public interface ITournamentDiscordResourcesService
{
	// Port of functions/discord/tournamentDiscordResources.js's createTournamentDiscordResources —
	// no-ops if the tournament has neither a Discord role nor channel name set. Creates whichever
	// of the two is missing, then adds the role to every approved, Discord-linked registrant.
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
	IOptions<DiscordOptions> options) : ITournamentDiscordResourcesService
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
			var discordIds = await GetApprovedDiscordIdsAsync(tournamentId, ct);
			await Task.WhenAll(discordIds.Select(discordId => discordClient.AddMemberRoleAsync(discordId, roleId, ct)));
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

	// Legacy is solo-only (registration.uid); we additionally resolve both members of a Team
	// registration since that's a new concept legacy never had to handle.
	private async Task<List<string>> GetApprovedDiscordIdsAsync(Guid tournamentId, CancellationToken ct)
	{
		var approved = (await registrationRepository.ListByTournamentAsync(tournamentId, ct)).Where(r => r.Approved).ToList();
		var playerIds = new List<Guid>();

		foreach (var registration in approved)
		{
			if (registration.PlayerId is Guid playerId)
			{
				playerIds.Add(playerId);
			}
			else if (registration.TeamId is Guid teamId)
			{
				var team = await teamRepository.GetByIdAsync(teamId, ct);
				if (team is not null)
				{
					playerIds.Add(team.Player1Id);
					playerIds.Add(team.Player2Id);
				}
			}
		}

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
