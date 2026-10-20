using Microsoft.EntityFrameworkCore;
using Npgsql;
using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Storage;

namespace ZenlessLeague.Api.Features.Teams;

public interface ITeamService
{
	Task<TeamDto> CreateAsync(CreateTeamRequest request, CancellationToken ct = default);
	Task<TeamDto?> GetAsync(Guid id, CancellationToken ct = default);
	Task<List<TeamDto>> ListByPlayerAsync(string uid, CancellationToken ct = default);
	Task<TeamPageDto> ListPageAsync(int take, DateTimeOffset? beforeCreatedAt, Guid? beforeId, CancellationToken ct = default);
	Task<TeamDto?> UpdateAsync(Guid id, EditTeamRequest request, CancellationToken ct = default);
	Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Thrown for cross-store validation failures (missing player, duplicate player id) — maps to 400.</summary>
public class TeamValidationException(string message) : ValidationApiException(message);

public class TeamService(ITeamRepository teamRepository, IPlayerRepository playerRepository, IFirebaseStorageClient storageClient) : ITeamService
{
	public async Task<TeamDto> CreateAsync(CreateTeamRequest request, CancellationToken ct = default)
	{
		if (request.CreatorPlayerId == request.Player2PlayerId)
		{
			throw new TeamValidationException("A team's two players must be different.");
		}

		var creator = await playerRepository.GetEntityByUidAsync(request.CreatorPlayerId, ct)
			?? throw new TeamValidationException($"Player '{request.CreatorPlayerId}' does not exist.");
		var player2 = await playerRepository.GetEntityByUidAsync(request.Player2PlayerId, ct)
			?? throw new TeamValidationException($"Player '{request.Player2PlayerId}' does not exist.");

		var teamId = Guid.NewGuid();
		var photoUrl = request.Photo is not null
			? await storageClient.UploadImageAsync(request.Photo, $"teams/{teamId}/logo", ct)
			: null;

		var team = await teamRepository.CreateAsync(new Team
		{
			Id = teamId,
			Name = request.Name,
			PhotoUrl = photoUrl,
			Player1Id = creator.Id,
			Player2Id = player2.Id,
			CreatedAt = DateTimeOffset.UtcNow,
		}, ct);

		return ToDto(team, ToSummary(creator), ToSummary(player2));
	}

	public async Task<TeamDto?> GetAsync(Guid id, CancellationToken ct = default)
	{
		var team = await teamRepository.GetByIdAsync(id, ct);
		return team is null ? null : (await EnrichAsync([team], ct))[0];
	}

	public async Task<List<TeamDto>> ListByPlayerAsync(string uid, CancellationToken ct = default)
	{
		var player = await playerRepository.GetEntityByUidAsync(uid, ct);
		if (player is null)
		{
			return [];
		}

		var teams = await teamRepository.ListByPlayerAsync(player.Id, ct);
		return await EnrichAsync(teams, ct);
	}

	public async Task<TeamPageDto> ListPageAsync(int take, DateTimeOffset? beforeCreatedAt, Guid? beforeId, CancellationToken ct = default)
	{
		var teams = await teamRepository.ListPageAsync(take, beforeCreatedAt, beforeId, ct);
		return new TeamPageDto(await EnrichAsync(teams, ct), teams.Count == take);
	}

	public async Task<TeamDto?> UpdateAsync(Guid id, EditTeamRequest request, CancellationToken ct = default)
	{
		var team = await teamRepository.GetByIdAsync(id, ct);
		if (team is null)
		{
			return null;
		}

		team.Name = request.Name;
		if (request.Photo is not null)
		{
			team.PhotoUrl = await storageClient.UploadImageAsync(request.Photo, $"teams/{team.Id}/logo", ct);
		}
		await teamRepository.SaveChangesAsync(ct);

		var creator = await playerRepository.GetEntityByIdAsync(team.Player1Id, ct)
			?? throw new TeamValidationException($"Player '{team.Player1Id}' no longer exists.");
		var player2 = await playerRepository.GetEntityByIdAsync(team.Player2Id, ct)
			?? throw new TeamValidationException($"Player '{team.Player2Id}' no longer exists.");

		return ToDto(team, ToSummary(creator), ToSummary(player2));
	}

	public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
	{
		var team = await teamRepository.GetByIdAsync(id, ct);
		if (team is null)
		{
			return false;
		}

		bool deleted;
		try
		{
			// Restrict-on-delete FKs (TournamentChallongeParticipants, TournamentMatches'
			// p1/p2/winner/tech-loss team, Tournaments.WinnerTeamId) surface here as a raw
			// Postgres 23503 (foreign_key_violation) if this team ever competed in a tournament —
			// translate that into a message the popup's alert() can actually show the user,
			// instead of letting it fall through to the generic 500 handler.
			deleted = await teamRepository.DeleteAsync(id, ct);
		}
		catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23503" })
		{
			throw new TeamValidationException(
				"This team can't be deleted because it has tournament history (bracket entries or match records).");
		}

		// Only remove the logo once the row is actually gone — deleting it first (the old order)
		// left the team without a photo if the DB delete then failed.
		if (deleted && team.PhotoUrl is not null)
		{
			await storageClient.DeleteImageAsync($"teams/{id}/logo", ct);
		}

		return deleted;
	}

	// Player1Id/Player2Id are real FKs into Players now, so these rows are guaranteed to
	// exist — no "player vanished" fallback needed anymore. Resolves every team's players in a
	// single query rather than two per team (the old per-row lookup made the teams page slow).
	private async Task<List<TeamDto>> EnrichAsync(List<Team> teams, CancellationToken ct)
	{
		var players = await playerRepository.GetSummariesByIdsAsync(
			teams.SelectMany(t => new[] { t.Player1Id, t.Player2Id }), ct);

		PlayerSummary Resolve(Team team, Guid playerId) =>
			players.GetValueOrDefault(playerId)
				?? throw new InvalidOperationException($"Player '{playerId}' referenced by team '{team.Id}' is missing.");

		return teams.Select(t => ToDto(t, Resolve(t, t.Player1Id), Resolve(t, t.Player2Id))).ToList();
	}

	private static PlayerSummary ToSummary(Player p) => new(p.Uid, p.Name, p.Avatar, p.IsMidConfirmed, p.IsHighConfirmed);

	private static TeamDto ToDto(Team team, PlayerSummary creator, PlayerSummary player2) =>
		new(team.Id, team.Name, team.PhotoUrl, creator, player2, team.CreatedAt);
}
