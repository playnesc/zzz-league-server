using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.Tournaments;

namespace ZenlessLeague.Api.Features.Players;

public interface IPlayerService
{
	Task<PlayerDto> CreateAsync(CreatePlayerRequest request, CancellationToken ct = default);
	Task<PlayerDto?> GetAsync(string uid, CancellationToken ct = default);

	// uids: optional filter (GET /api/players?uids=...) for batch-resolving a known set of
	// players; null returns the whole roster. Always the lean list shape — DiscordId/Role/
	// CreatedAt aren't included since no list-context consumer needs them.
	Task<List<PlayerListItemDto>> ListAsync(IEnumerable<string>? uids = null, CancellationToken ct = default);
	Task<bool> DeleteAsync(string uid, CancellationToken ct = default);
	Task<PlayerDto?> UpdateRoleAsync(string uid, UpdatePlayerRoleRequest request, CancellationToken ct = default);
	Task<List<StaffMemberDto>> ListStaffAsync(CancellationToken ct = default);

	Task<PlayerDto?> UpdateNameAsync(string uid, string name, CancellationToken ct = default);
	Task<PlayerDto?> UpdateAvatarAsync(string uid, string avatar, CancellationToken ct = default);

	// Null when the player doesn't exist or has never filled in a solo registration form.
	Task<LastRegistrationDto?> GetLastRegistrationAsync(string uid, CancellationToken ct = default);
}

/// <summary>Thrown for player validation failures (duplicate uid/name) — maps to 400.</summary>
public class PlayerValidationException(string message) : ValidationApiException(message);

public class PlayerService(IPlayerRepository playerRepository, ITournamentRegistrationRepository registrationRepository) : IPlayerService
{
	public async Task<PlayerDto> CreateAsync(CreatePlayerRequest request, CancellationToken ct = default)
	{
		var uid = InputValidation.RequiredText(request.Uid, "Uid", Player.UidMaxLength);
		var name = InputValidation.RequiredText(request.Name, "Name", Player.NameMaxLength);
		var avatar = InputValidation.OptionalText(request.Avatar, "Avatar", Player.AvatarMaxLength);

		if (await playerRepository.GetEntityByUidAsync(uid, ct) is not null)
		{
			throw new PlayerValidationException($"Player '{uid}' already exists.");
		}

		if (await playerRepository.GetByNameAsync(name, ct) is not null)
		{
			throw new PlayerValidationException($"Name '{name}' is already taken.");
		}

		var player = await playerRepository.CreateAsync(new Player
		{
			Uid = uid,
			Name = name,
			Avatar = avatar,
			CreatedAt = DateTimeOffset.UtcNow,
		}, ct);

		return ToDto(player);
	}

	public async Task<PlayerDto?> GetAsync(string uid, CancellationToken ct = default)
	{
		var player = await playerRepository.GetEntityByUidAsync(uid, ct);
		return player is null ? null : ToDto(player);
	}

	public async Task<List<PlayerListItemDto>> ListAsync(IEnumerable<string>? uids = null, CancellationToken ct = default) =>
		(await playerRepository.ListAsync(uids, ct)).Select(ToListItemDto).ToList();

	public Task<bool> DeleteAsync(string uid, CancellationToken ct = default) => playerRepository.DeleteByUidAsync(uid, ct);

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
		return ToDto(player);
	}

	public async Task<List<StaffMemberDto>> ListStaffAsync(CancellationToken ct = default) =>
		(await playerRepository.ListByMinRoleAsync(PlayerRole.Moderator, ct))
			.Select(p => new StaffMemberDto(p.Uid, p.Name, p.Avatar, p.Role))
			.ToList();

	public async Task<PlayerDto?> UpdateNameAsync(string uid, string name, CancellationToken ct = default)
	{
		name = InputValidation.RequiredText(name, "Name", Player.NameMaxLength);
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
		return ToDto(player);
	}

	public async Task<PlayerDto?> UpdateAvatarAsync(string uid, string avatar, CancellationToken ct = default)
	{
		avatar = InputValidation.RequiredText(avatar, "Avatar", Player.AvatarMaxLength);
		var player = await playerRepository.GetEntityByUidAsync(uid, ct);
		if (player is null)
		{
			return null;
		}

		player.Avatar = avatar;
		await playerRepository.SaveChangesAsync(ct);
		return ToDto(player);
	}

	// Not stored on Player — it's the player's most recent solo registration, queried live, and
	// served by its own endpoint so it's always fresh (it used to ride along on PlayerDto, where the
	// frontend cached it for the whole session and went stale after the next registration).
	public async Task<LastRegistrationDto?> GetLastRegistrationAsync(string uid, CancellationToken ct = default)
	{
		var player = await playerRepository.GetEntityByUidAsync(uid, ct);
		if (player is null)
		{
			return null;
		}

		var d = (await registrationRepository.GetMostRecentSoloByPlayerAsync(player.Id, ct))?.Player1Details;
		return d is null ? null : new LastRegistrationDto(
			d.GameUid, d.PrizeAsMoney, d.PrizeUid, d.DarteNickname, d.DartePresetName, d.RosterName,
			d.RosterScreenshotUrl, d.HoyolabScreenshotUrl);
	}

	// Internal, not private — AuthService reuses this exact mapping so link/unlink can return the
	// updated player without duplicating the mapping.
	internal static PlayerDto ToDto(Player p) => new(
		p.Uid, p.Name, p.DiscordId, p.DiscordUsername,
		p.Elo, p.TournamentPoints, p.IsMidConfirmed, p.IsHighConfirmed,
		p.Wins, p.Losses, p.PlayedTournamentCount, p.SeasonalPlayedTournamentCount,
		p.LastPlayedTournamentTimestamp, p.Avatar, p.Role, p.CreatedAt);

	private static PlayerListItemDto ToListItemDto(Player p) => new(
		p.Uid, p.Name, p.DiscordUsername,
		p.Elo, p.TournamentPoints, p.IsMidConfirmed, p.IsHighConfirmed,
		p.Wins, p.Losses, p.PlayedTournamentCount, p.SeasonalPlayedTournamentCount,
		p.LastPlayedTournamentTimestamp, p.Avatar);
}
