using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Tournaments.Realtime;

namespace ZenlessLeague.Api.Features.Tournaments;

public interface ITournamentService
{
	Task<TournamentDto> CreateAsync(CreateTournamentRequest request, CancellationToken ct = default);
	Task<TournamentDto?> GetAsync(Guid id, CancellationToken ct = default);
	Task<List<TournamentDto>> ListAsync(CancellationToken ct = default);
	Task<TournamentDto?> UpdateAsync(Guid id, UpdateTournamentRequest request, CancellationToken ct = default);
	Task<TournamentDto?> CloseRegistrationAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Thrown for tournament lifecycle validation failures (wrong state, missing bracket, etc.) — maps to 400.</summary>
public class TournamentLifecycleException(string message) : ValidationApiException(message);

public class TournamentService(
	ITournamentRepository tournamentRepository,
	IPlayerRepository playerRepository,
	ITournamentNotifier notifier) : ITournamentService
{
	public async Task<TournamentDto> CreateAsync(CreateTournamentRequest request, CancellationToken ct = default)
	{
		var tournament = await tournamentRepository.CreateAsync(new Tournament
		{
			Id = Guid.NewGuid(),
			Name = request.Name,
			Description = request.Description,
			RegistrationStartDate = request.RegistrationStartDate,
			RegistrationEndDate = request.RegistrationEndDate,
			TournamentStartDate = request.TournamentStartDate,
			TournamentEndDate = request.TournamentEndDate,
			MinCost = request.MinCost,
			MaxCost = request.MaxCost,
			MinCharacters = request.MinCharacters,
			MinTier = request.MinTier,
			MaxTier = request.MaxTier,
			RegistrationType = request.RegistrationType,
			GameMode = request.GameMode,
			BracketType = request.BracketType,
			Visible = request.Visible,
			OverrideEloChange = request.OverrideEloChange,
			ConsolationMatchesTargetRank = request.ConsolationMatchesTargetRank,
			DiscordRoleName = request.DiscordRoleName,
			DiscordChannelName = request.DiscordChannelName,
		}, ct);

		await notifier.TournamentChangedAsync(tournament.Id, ct);
		return await ToDtoAsync(tournament, ct);
	}

	public async Task<TournamentDto?> GetAsync(Guid id, CancellationToken ct = default)
	{
		var tournament = await tournamentRepository.GetByIdAsync(id, ct);
		return tournament is null ? null : await ToDtoAsync(tournament, ct);
	}

	public async Task<List<TournamentDto>> ListAsync(CancellationToken ct = default) =>
		await ToDtosAsync(await tournamentRepository.ListAsync(ct), ct);

	public async Task<TournamentDto?> UpdateAsync(Guid id, UpdateTournamentRequest request, CancellationToken ct = default)
	{
		var tournament = await tournamentRepository.GetByIdAsync(id, ct);
		if (tournament is null)
		{
			return null;
		}

		if (TournamentStateMachine.IsLocked(tournament.State))
		{
			throw new TournamentLifecycleException("Tournament has already started.");
		}

		tournament.Name = request.Name;
		tournament.Description = request.Description;
		tournament.RegistrationStartDate = request.RegistrationStartDate;
		tournament.RegistrationEndDate = request.RegistrationEndDate;
		tournament.TournamentStartDate = request.TournamentStartDate;
		tournament.TournamentEndDate = request.TournamentEndDate;
		tournament.MinCost = request.MinCost;
		tournament.MaxCost = request.MaxCost;
		tournament.MinCharacters = request.MinCharacters;
		tournament.MinTier = request.MinTier;
		tournament.MaxTier = request.MaxTier;
		tournament.OverrideEloChange = request.OverrideEloChange;
		tournament.BracketType = request.BracketType;
		tournament.ConsolationMatchesTargetRank = request.ConsolationMatchesTargetRank;
		tournament.Visible = request.Visible;
		tournament.DiscordRoleName = request.DiscordRoleName;
		tournament.DiscordChannelName = request.DiscordChannelName;

		await tournamentRepository.SaveChangesAsync(ct);
		await notifier.TournamentChangedAsync(id, ct);
		return await ToDtoAsync(tournament, ct);
	}

	public async Task<TournamentDto?> CloseRegistrationAsync(Guid id, CancellationToken ct = default)
	{
		var tournament = await tournamentRepository.GetByIdAsync(id, ct);
		if (tournament is null)
		{
			return null;
		}

		if (!TournamentStateMachine.IsRegistrationOpen(tournament.State))
		{
			throw new TournamentLifecycleException("Tournament is not currently accepting registrations.");
		}

		tournament.State = TournamentState.RegistrationClosed;
		await tournamentRepository.SaveChangesAsync(ct);
		await notifier.TournamentChangedAsync(id, ct);
		return await ToDtoAsync(tournament, ct);
	}

	// t.WinnerId is only ever set post-FinishAsync, so this is a no-op query for every tournament
	// still in progress — the ternary short-circuits before touching playerRepository.
	private async Task<TournamentDto> ToDtoAsync(Tournament t, CancellationToken ct)
	{
		var winnerUid = t.WinnerId is Guid wid ? (await playerRepository.GetByIdAsync(wid, ct))?.Uid : null;
		return ToDto(t, winnerUid);
	}

	private async Task<List<TournamentDto>> ToDtosAsync(List<Tournament> tournaments, CancellationToken ct)
	{
		var ids = tournaments.Where(t => t.WinnerId is not null).Select(t => t.WinnerId!.Value).Distinct();
		var uidsById = await playerRepository.GetUidsByIdsAsync(ids, ct);
		return tournaments.Select(t => ToDto(t, t.WinnerId is Guid wid ? uidsById.GetValueOrDefault(wid) : null)).ToList();
	}

	internal static TournamentDto ToDto(Tournament t, string? winnerUid) => new(
		t.Id, t.Name, t.Description,
		t.RegistrationStartDate, t.RegistrationEndDate, t.TournamentStartDate, t.TournamentEndDate,
		t.MinCost, t.MaxCost, t.MinCharacters, t.MinTier, t.MaxTier,
		t.State, t.RegistrationType, t.GameMode, t.Visible, t.BracketType,
		t.ChallongeTournamentId, t.ChallongeTournamentUrl, t.ChallongeWinnerId, winnerUid, t.WinnerTeamId,
		t.DivisionGroupId, t.DivisionIndex,
		t.OverrideEloChange, t.ConsolationMatchesTargetRank, t.DiscordRoleName, t.DiscordChannelName);
}
