namespace ZenlessLeague.Api.Features.Players;

public interface IPlayerService
{
    Task<PlayerDto> CreateAsync(CreatePlayerRequest request, CancellationToken ct = default);
    Task<PlayerDto?> GetAsync(string uid, CancellationToken ct = default);
    Task<List<PlayerDto>> ListAsync(CancellationToken ct = default);
    Task<bool> DeleteAsync(string uid, CancellationToken ct = default);
    Task<PlayerDto> ClaimAsync(ClaimPlayerRequest request, CancellationToken ct = default);
    Task<PlayerDto?> UpdateRoleAsync(string uid, UpdatePlayerRoleRequest request, CancellationToken ct = default);
}

/// <summary>Thrown for player validation failures (duplicate uid/name, missing claim target) — maps to 400.</summary>
public class PlayerValidationException(string message) : Exception(message);

public class PlayerService(IPlayerRepository playerRepository) : IPlayerService
{
    public async Task<PlayerDto> CreateAsync(CreatePlayerRequest request, CancellationToken ct = default)
    {
        if (await playerRepository.GetEntityByIdAsync(request.Uid, ct) is not null)
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
        var player = await playerRepository.GetEntityByIdAsync(uid, ct);
        return player is null ? null : ToDto(player);
    }

    public async Task<List<PlayerDto>> ListAsync(CancellationToken ct = default) =>
        (await playerRepository.ListAsync(ct)).Select(ToDto).ToList();

    public Task<bool> DeleteAsync(string uid, CancellationToken ct = default) => playerRepository.DeleteAsync(uid, ct);

    // Ports the legacy user/register.js "claim a reserved player row" flow: admin/addPlayer.js
    // can create a bare-name row ahead of time (Uid == Name, no real Firebase Auth account
    // behind it yet); when that person actually signs up, their stats move onto their real
    // Firebase uid instead of starting over from a fresh row.
    public async Task<PlayerDto> ClaimAsync(ClaimPlayerRequest request, CancellationToken ct = default)
    {
        var existing = await playerRepository.GetEntityByIdAsync(request.ExistingName, ct)
            ?? throw new PlayerValidationException($"No reserved player named '{request.ExistingName}' exists.");

        if (await playerRepository.GetEntityByIdAsync(request.NewUid, ct) is not null)
        {
            throw new PlayerValidationException($"Player '{request.NewUid}' already exists.");
        }

        var claimed = new Player
        {
            Uid = request.NewUid,
            Name = existing.Name,
            DiscordId = existing.DiscordId,
            DiscordUsername = existing.DiscordUsername,
            Elo = existing.Elo,
            TournamentPoints = existing.TournamentPoints,
            IsMidConfirmed = existing.IsMidConfirmed,
            IsHighConfirmed = existing.IsHighConfirmed,
            Wins = existing.Wins,
            Losses = existing.Losses,
            PlayedTournamentCount = existing.PlayedTournamentCount,
            SeasonalPlayedTournamentCount = existing.SeasonalPlayedTournamentCount,
            LastPlayedTournamentTimestamp = existing.LastPlayedTournamentTimestamp,
            LastEloUpdateTimestamp = existing.LastEloUpdateTimestamp,
            Avatar = existing.Avatar,
            Role = existing.Role,
            CreatedAt = existing.CreatedAt,
            LastRegistrationCache = existing.LastRegistrationCache,
        };

        await playerRepository.ReplaceAsync(existing, claimed, ct);
        return ToDto(claimed);
    }

    public async Task<PlayerDto?> UpdateRoleAsync(string uid, UpdatePlayerRoleRequest request, CancellationToken ct = default)
    {
        var player = await playerRepository.GetEntityByIdAsync(uid, ct);
        if (player is null)
        {
            return null;
        }

        player.Role = request.Role;
        await playerRepository.SaveChangesAsync(ct);
        return ToDto(player);
    }

    private static PlayerDto ToDto(Player p) => new(
        p.Uid, p.Name, p.DiscordId, p.DiscordUsername,
        p.Elo, p.TournamentPoints, p.IsMidConfirmed, p.IsHighConfirmed,
        p.Wins, p.Losses, p.PlayedTournamentCount, p.SeasonalPlayedTournamentCount,
        p.LastPlayedTournamentTimestamp, p.Avatar, p.Role, p.CreatedAt);
}
