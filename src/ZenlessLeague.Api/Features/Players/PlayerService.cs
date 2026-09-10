using ZenlessLeague.Api.Data;

namespace ZenlessLeague.Api.Features.Players;

public interface IPlayerService
{
	Task<PlayerDto> CreateAsync(CreatePlayerRequest request, CancellationToken ct = default);
	Task<PlayerDto?> GetAsync(string uid, CancellationToken ct = default);
	Task<List<PlayerDto>> ListAsync(CancellationToken ct = default);
	Task<bool> DeleteAsync(string uid, CancellationToken ct = default);
	Task<PlayerDto> ClaimAsync(ClaimPlayerRequest request, CancellationToken ct = default);
	Task<PlayerDto?> UpdateRoleAsync(string uid, UpdatePlayerRoleRequest request, CancellationToken ct = default);

	// Port of functions/user/updateProfile.js. Name uniqueness is enforced by the same
	// check CreateAsync uses (a DB unique index backs it too) instead of legacy's separate
	// "usernames/" RTDB tracking node, which Postgres doesn't need.
	Task<PlayerDto?> UpdateNameAsync(string uid, string name, CancellationToken ct = default);

	// Port of functions/user/updateAvatar.js.
	Task<PlayerDto?> UpdateAvatarAsync(string uid, string avatar, CancellationToken ct = default);
}

/// <summary>Thrown for player validation failures (duplicate uid/name, missing claim target) — maps to 400.</summary>
public class PlayerValidationException(string message) : ValidationApiException(message);

public class PlayerService(IPlayerRepository playerRepository) : IPlayerService
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

		return ToDto(player);
	}

	public async Task<PlayerDto?> GetAsync(string uid, CancellationToken ct = default)
	{
		var player = await playerRepository.GetEntityByUidAsync(uid, ct);
		return player is null ? null : ToDto(player);
	}

	public async Task<List<PlayerDto>> ListAsync(CancellationToken ct = default) =>
		(await playerRepository.ListAsync(ct)).Select(ToDto).ToList();

	public Task<bool> DeleteAsync(string uid, CancellationToken ct = default) => playerRepository.DeleteByUidAsync(uid, ct);

	// Ports the legacy user/register.js "claim a reserved player row" flow: admin/addPlayer.js
	// can create a bare-name row ahead of time (Uid == Name, no real Firebase Auth account
	// behind it yet); when that person actually signs up, their stats move onto their real
	// Firebase uid instead of starting over from a fresh row. Now that Player.Id (not Uid) is
	// the primary key, "claiming" is just updating Uid in place — every FK pointing at this
	// row's Id stays valid without any further work.
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
		return ToDto(existing);
	}

	public async Task<PlayerDto?> UpdateRoleAsync(string uid, UpdatePlayerRoleRequest request, CancellationToken ct = default)
	{
		var player = await playerRepository.GetEntityByUidAsync(uid, ct);
		if (player is null)
		{
			return null;
		}

		player.Role = request.Role;
		await playerRepository.SaveChangesAsync(ct);
		return ToDto(player);
	}

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
		return ToDto(player);
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
		return ToDto(player);
	}

	private static PlayerDto ToDto(Player p) => new(
		p.Uid, p.Name, p.DiscordId, p.DiscordUsername,
		p.Elo, p.TournamentPoints, p.IsMidConfirmed, p.IsHighConfirmed,
		p.Wins, p.Losses, p.PlayedTournamentCount, p.SeasonalPlayedTournamentCount,
		p.LastPlayedTournamentTimestamp, p.Avatar, p.Role, p.CreatedAt,
		p.LastRegistrationCache is null ? null : new LastRegistrationCacheDto(
			p.LastRegistrationCache.GameUid, p.LastRegistrationCache.PrizeAsMoney, p.LastRegistrationCache.PrizeUid,
			p.LastRegistrationCache.DarteNickname, p.LastRegistrationCache.DartePresetName, p.LastRegistrationCache.RosterName,
			p.LastRegistrationCache.RosterScreenshotUrl, p.LastRegistrationCache.HoyolabScreenshotUrl));
}
