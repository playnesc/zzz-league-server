using ZenlessLeague.Api.Features.Players;

namespace ZenlessLeague.Api.Features.Teams;

public interface ITeamService
{
    Task<TeamDto> CreateAsync(CreateTeamRequest request, CancellationToken ct = default);
    Task<TeamDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<List<TeamDto>> ListAsync(CancellationToken ct = default);
    Task<TeamDto?> UpdateAsync(Guid id, EditTeamRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Thrown for cross-store validation failures (missing player, duplicate player id) — maps to 400.</summary>
public class TeamValidationException(string message) : Exception(message);

public class TeamService(ITeamRepository teamRepository, IPlayerRepository playerRepository) : ITeamService
{
    public async Task<TeamDto> CreateAsync(CreateTeamRequest request, CancellationToken ct = default)
    {
        if (request.CreatorPlayerId == request.Player2PlayerId)
        {
            throw new TeamValidationException("A team's two players must be different.");
        }

        var creator = await playerRepository.GetByIdAsync(request.CreatorPlayerId, ct)
            ?? throw new TeamValidationException($"Player '{request.CreatorPlayerId}' does not exist.");
        var player2 = await playerRepository.GetByIdAsync(request.Player2PlayerId, ct)
            ?? throw new TeamValidationException($"Player '{request.Player2PlayerId}' does not exist.");

        var team = await teamRepository.CreateAsync(new Team
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            PhotoUrl = request.PhotoUrl,
            CreatorPlayerId = request.CreatorPlayerId,
            Player2PlayerId = request.Player2PlayerId,
            CreatedAt = DateTimeOffset.UtcNow,
        }, ct);

        return ToDto(team, creator, player2);
    }

    public async Task<TeamDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var team = await teamRepository.GetByIdAsync(id, ct);
        return team is null ? null : await EnrichAsync(team, ct);
    }

    public async Task<List<TeamDto>> ListAsync(CancellationToken ct = default)
    {
        var teams = await teamRepository.ListAsync(ct);
        var dtos = new List<TeamDto>(teams.Count);
        foreach (var team in teams)
        {
            dtos.Add(await EnrichAsync(team, ct));
        }

        return dtos;
    }

    public async Task<TeamDto?> UpdateAsync(Guid id, EditTeamRequest request, CancellationToken ct = default)
    {
        var team = await teamRepository.GetByIdAsync(id, ct);
        if (team is null)
        {
            return null;
        }

        if (request.Player2PlayerId == team.CreatorPlayerId)
        {
            throw new TeamValidationException("A team's two players must be different.");
        }

        var player2 = await playerRepository.GetByIdAsync(request.Player2PlayerId, ct)
            ?? throw new TeamValidationException($"Player '{request.Player2PlayerId}' does not exist.");

        team.Name = request.Name;
        team.PhotoUrl = request.PhotoUrl;
        team.Player2PlayerId = request.Player2PlayerId;
        await teamRepository.SaveChangesAsync(ct);

        var creator = await playerRepository.GetByIdAsync(team.CreatorPlayerId, ct)
            ?? throw new TeamValidationException($"Player '{team.CreatorPlayerId}' no longer exists.");

        return ToDto(team, creator, player2);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) => teamRepository.DeleteAsync(id, ct);

    // CreatorPlayerId/Player2PlayerId are real FKs into Players now, so these rows are
    // guaranteed to exist — no "player vanished" fallback needed anymore.
    private async Task<TeamDto> EnrichAsync(Team team, CancellationToken ct)
    {
        var creator = await playerRepository.GetByIdAsync(team.CreatorPlayerId, ct)
            ?? throw new InvalidOperationException($"Player '{team.CreatorPlayerId}' referenced by team '{team.Id}' is missing.");
        var player2 = await playerRepository.GetByIdAsync(team.Player2PlayerId, ct)
            ?? throw new InvalidOperationException($"Player '{team.Player2PlayerId}' referenced by team '{team.Id}' is missing.");

        return ToDto(team, creator, player2);
    }

    private static TeamDto ToDto(Team team, PlayerSummary creator, PlayerSummary player2) =>
        new(team.Id, team.Name, team.PhotoUrl, creator, player2, team.CreatedAt);
}
