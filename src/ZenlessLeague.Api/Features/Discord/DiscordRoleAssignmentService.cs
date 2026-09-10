using Microsoft.Extensions.Options;
using ZenlessLeague.Api.Features.Players;

namespace ZenlessLeague.Api.Features.Discord;

public interface IDiscordRoleAssignmentService
{
	// Direct port of functions/discord/assignDiscordRole.js — computes the correct tier role
	// from confirmation flags and adds it while removing the other two. No-ops if the player
	// has no linked Discord account.
	Task AssignRoleForPlayerAsync(string uid, CancellationToken ct = default);
}

public class DiscordRoleAssignmentService(
	IPlayerRepository playerRepository,
	IDiscordClient discordClient,
	IOptions<DiscordOptions> options) : IDiscordRoleAssignmentService
{
	public async Task AssignRoleForPlayerAsync(string uid, CancellationToken ct = default)
	{
		var player = await playerRepository.GetEntityByUidAsync(uid, ct);
		if (player?.DiscordId is null)
		{
			return;
		}

		var opts = options.Value;
		var newRoleId = player.IsHighConfirmed ? opts.HighRoleId : player.IsMidConfirmed ? opts.MidRoleId : opts.NewbieRoleId;

		var tasks = new[] { opts.NewbieRoleId, opts.MidRoleId, opts.HighRoleId }
			.Where(roleId => roleId is not null)
			.Select(roleId => roleId == newRoleId
				? discordClient.AddMemberRoleAsync(player.DiscordId, roleId!, ct)
				: discordClient.RemoveMemberRoleAsync(player.DiscordId, roleId!, ct));

		await Task.WhenAll(tasks);
	}
}
