using ZenlessLeague.Api.Features.Discord;

namespace ZenlessLeague.Api.Tests;

// Records calls instead of hitting a real Discord bot/guild.
public class FakeDiscordClient : IDiscordClient
{
	public List<(string DiscordUserId, string RoleId)> AddedMemberRoles { get; } = [];
	public List<(string DiscordUserId, string RoleId)> RemovedMemberRoles { get; } = [];
	public List<string> DeletedRoleIds { get; } = [];
	public List<string> DeletedChannelIds { get; } = [];
	public List<(string Name, string? ParentId, IReadOnlyList<DiscordPermissionOverwrite> Overwrites)> CreatedChannels { get; } = [];

	public int NextRoleId { get; set; } = 1;
	public int NextChannelId { get; set; } = 1;

	// Lets tests simulate N transient failures before CreateGuildRoleAsync finally succeeds.
	public int CreateRoleFailuresRemaining { get; set; }

	public Task AddMemberRoleAsync(string discordUserId, string roleId, CancellationToken ct = default)
	{
		AddedMemberRoles.Add((discordUserId, roleId));
		return Task.CompletedTask;
	}

	public Task RemoveMemberRoleAsync(string discordUserId, string roleId, CancellationToken ct = default)
	{
		RemovedMemberRoles.Add((discordUserId, roleId));
		return Task.CompletedTask;
	}

	public Task<string> CreateGuildRoleAsync(string name, CancellationToken ct = default)
	{
		if (CreateRoleFailuresRemaining > 0)
		{
			CreateRoleFailuresRemaining--;
			throw new InvalidOperationException("simulated transient Discord failure");
		}

		return Task.FromResult($"role-{NextRoleId++}");
	}

	public Task DeleteGuildRoleAsync(string roleId, CancellationToken ct = default)
	{
		DeletedRoleIds.Add(roleId);
		return Task.CompletedTask;
	}

	public Task<string> CreateGuildChannelAsync(string name, string? parentId, IReadOnlyList<DiscordPermissionOverwrite> permissionOverwrites, CancellationToken ct = default)
	{
		CreatedChannels.Add((name, parentId, permissionOverwrites));
		return Task.FromResult($"channel-{NextChannelId++}");
	}

	public Task DeleteGuildChannelAsync(string channelId, CancellationToken ct = default)
	{
		DeletedChannelIds.Add(channelId);
		return Task.CompletedTask;
	}
}
