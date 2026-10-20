using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.Tournaments;

namespace ZenlessLeague.Api.Features.Players;

public interface IPlayerService
{
	Task<PlayerDto> CreateAsync(CreatePlayerRequest request, CancellationToken ct = default);
	Task<PlayerDto?> GetAsync(string uid, CancellationToken ct = default);

	// uids: optional filter (GET /api/players?uids=...) for batch-resolving a known set of
	// players; null returns the whole roster. Always the lean list shape — DiscordId/Role/
	// CreatedAt/LastRegistration aren't included since no list-context consumer needs them.
	Task<List<PlayerListItemDto>> ListAsync(IEnumerable<string>? uids = null, CancellationToken ct = default);
	Task<bool> DeleteAsync(string uid, CancellationToken ct = default);
	Task<PlayerDto> ClaimAsync(ClaimPlayerRequest request, CancellationToken ct = default);
	Task<PlayerDto?> UpdateRoleAsync(string uid, UpdatePlayerRoleRequest request, CancellationToken ct = default);
	Task<List<StaffMemberDto>> ListStaffAsync(CancellationToken ct = default);

	Task<PlayerDto?> UpdateNameAsync(string uid, string name, CancellationToken ct = default);
	Task<PlayerDto?> UpdateAvatarAsync(string uid, string avatar, CancellationToken ct = default);
}

/// <summary>Thrown for player validation failures (duplicate uid/name, missing claim target) — maps to 400.</summary>
public class PlayerValidationException(string message) : ValidationApiException(message);

public class PlayerService(IPlayerRepository playerRepository, ITournamentRegistrationRepository registrationRepository) : IPlayerService
{
	public async Task<PlayerDto> CreateAsync(CreatePlayerRequest request, CancellationToken ct = default)
	{
		if (await playerRepository.GetEntityByUidAsync(request.Uid, ct) is not null)
		{
			throw new PlayerValidationException($"Player '{request.Uid}' already exists.");
		}

		if (await playerRepository.GetByNameAsync(request.Name, ct) is not null)
		{
			throw new PlayerValidationException($"Name '{request.Name}' is already taken.");
		}

		var player = await playerRepository.CreateAsync(new Player
		{
			Uid = request.Uid,
			Name = request.Name,
			Avatar = request.Avatar,
			CreatedAt = DateTimeOffset.UtcNow,
		}, ct);

		return await BuildDtoAsync(player, ct);
	}

	public async Task<PlayerDto?> GetAsync(string uid, CancellationToken ct = default)
	{
		var player = await playerRepository.GetEntityByUidAsync(uid, ct);
		return player is null ? null : await BuildDtoAsync(player, ct);
	}

	public async Task<List<PlayerListItemDto>> ListAsync(IEnumerable<string>? uids = null, CancellationToken ct = default) =>
		(await playerRepository.ListAsync(uids, ct)).Select(ToListItemDto).ToList();

	public Task<bool> DeleteAsync(string uid, CancellationToken ct = default) => playerRepository.DeleteByUidAsync(uid, ct);

	// "Claim a reserved player row": an admin can create a bare-name row ahead of time (Uid ==
	// Name, no real Firebase Auth account behind it yet); when that person actually signs up,
	// their stats move onto their real Firebase uid instead of starting over from a fresh row.
	// Since Player.Id (not Uid) is the primary key, "claiming" is just updating Uid in place —
	// every FK pointing at this row's Id stays valid without any further work.
	public async Task<PlayerDto> ClaimAsync(ClaimPlayerRequest request, CancellationToken ct = default)
	{
		var existing = await playerRepository.GetEntityByUidAsync(request.ExistingName, ct)
			?? throw new PlayerValidationException($"No reserved player named '{request.ExistingName}' exists.");

		if (await playerRepository.GetEntityByUidAsync(request.NewUid, ct) is not null)
		{
			throw new PlayerValidationException($"Player '{request.NewUid}' already exists.");
		}

		existing.Uid = request.NewUid;
		await playerRepository.SaveChangesAsync(ct);
		return await BuildDtoAsync(existing, ct);
	}

	public async Task<PlayerDto?> UpdateRoleAsync(string uid, UpdatePlayerRoleRequest request, CancellationToken ct = default)
	{
		var player = await playerRepository.GetEntityByUidAsync(uid, ct);
		if (player is null)
		{
			return null;
		}

		// Only Player <-> Moderator is managed through the API; the Admin role is granted and
		// revoked directly in the DB, so an admin can't be demoted (or demote themselves) here.
		if (request.Role == PlayerRole.Admin)
		{
			throw new PlayerValidationException("The admin role can't be granted through the API.");
		}

		if (player.Role == PlayerRole.Admin)
		{
			throw new PlayerValidationException("An admin's role can't be changed through the API.");
		}

		player.Role = request.Role;
		await playerRepository.SaveChangesAsync(ct);
		return await BuildDtoAsync(player, ct);
	}

	public async Task<List<StaffMemberDto>> ListStaffAsync(CancellationToken ct = default) =>
		(await playerRepository.ListByMinRoleAsync(PlayerRole.Moderator, ct))
			.Select(p => new StaffMemberDto(p.Uid, p.Name, p.Avatar, p.Role))
			.ToList();

	public async Task<PlayerDto?> UpdateNameAsync(string uid, string name, CancellationToken ct = default)
	{
		var player = await playerRepository.GetEntityByUidAsync(uid, ct);
		if (player is null)
		{
			return null;
		}

		if (name == player.Name)
		{
			throw new PlayerValidationException("Username haven't changed.");
		}

		if (await playerRepository.GetByNameAsync(name, ct) is not null)
		{
			throw new PlayerValidationException($"Name '{name}' is already taken.");
		}

		player.Name = name;
		await playerRepository.SaveChangesAsync(ct);
		return await BuildDtoAsync(player, ct);
	}

	public async Task<PlayerDto?> UpdateAvatarAsync(string uid, string avatar, CancellationToken ct = default)
	{
		var player = await playerRepository.GetEntityByUidAsync(uid, ct);
		if (player is null)
		{
			return null;
		}

		player.Avatar = avatar;
		await playerRepository.SaveChangesAsync(ct);
		return await BuildDtoAsync(player, ct);
	}

	// LastRegistration isn't stored on Player (see the removed LastRegistrationCache) — it's the
	// player's most recent solo registration, queried live. Only worth doing for a single full
	// PlayerDto; the lean ListAsync/ToListItemDto path never included this field at all.
	private async Task<PlayerDto> BuildDtoAsync(Player p, CancellationToken ct)
	{
		var lastRegistration = await registrationRepository.GetMostRecentSoloByPlayerAsync(p.Id, ct);
		return ToDto(p, lastRegistration?.Player1Details);
	}

	// Internal, not private — AuthService reuses this exact mapping so link/unlink can return the
	// updated player without duplicating the mapping.
	internal static PlayerDto ToDto(Player p, PlayerRegistrationDetails? lastRegistration = null) => new(
		p.Uid, p.Name, p.DiscordId, p.DiscordUsername,
		p.Elo, p.TournamentPoints, p.IsMidConfirmed, p.IsHighConfirmed,
		p.Wins, p.Losses, p.PlayedTournamentCount, p.SeasonalPlayedTournamentCount,
		p.LastPlayedTournamentTimestamp, p.Avatar, p.Role, p.CreatedAt,
		lastRegistration is null ? null : new LastRegistrationCacheDto(
			lastRegistration.GameUid, lastRegistration.PrizeAsMoney, lastRegistration.PrizeUid,
			lastRegistration.DarteNickname, lastRegistration.DartePresetName, lastRegistration.RosterName,
			lastRegistration.RosterScreenshotUrl, lastRegistration.HoyolabScreenshotUrl));

	private static PlayerListItemDto ToListItemDto(Player p) => new(
		p.Uid, p.Name, p.DiscordUsername,
		p.Elo, p.TournamentPoints, p.IsMidConfirmed, p.IsHighConfirmed,
		p.Wins, p.Losses, p.PlayedTournamentCount, p.SeasonalPlayedTournamentCount,
		p.LastPlayedTournamentTimestamp, p.Avatar);
}
