using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Tournaments.Bracket;
using ZenlessLeague.Api.Features.Tournaments.Realtime;
using ZenlessLeague.Api.Infrastructure.Errors;

namespace ZenlessLeague.Api.Features.Tournaments;

public interface ITournamentService
{
	Task<TournamentDto> CreateAsync(CreateTournamentRequest request, CancellationToken ct = default);
	Task<TournamentDto?> GetAsync(Guid id, CancellationToken ct = default);
	Task<List<TournamentDto>> ListAsync(bool includeHidden, CancellationToken ct = default);
	Task<TournamentPageDto> ListPageAsync(bool includeHidden, int page, int pageSize, CancellationToken ct = default);
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
			Name = InputValidation.RequiredText(request.Name, "Tournament name", Tournament.NameMaxLength),
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
			BracketType = BracketType(request.BracketType),
			Visible = request.Visible,
			IsRanked = request.IsRanked,
			OverrideEloChange = EloOverride(request.IsRanked, request.OverrideEloChange),
			ConsolationMatchesTargetRank = ConsolationRank(request.ConsolationMatchesTargetRank),
			DiscordRoleName = DiscordName(request.DiscordRoleName, "Discord role name"),
			DiscordChannelName = DiscordName(request.DiscordChannelName, "Discord channel name"),
		}, ct);

		await notifier.TournamentChangedAsync(tournament.Id);
		return await ToDtoAsync(tournament, ct);
	}

	public async Task<TournamentDto?> GetAsync(Guid id, CancellationToken ct = default)
	{
		var tournament = await tournamentRepository.GetByIdAsync(id, ct);
		return tournament is null ? null : await ToDtoAsync(tournament, ct);
	}

	public async Task<List<TournamentDto>> ListAsync(bool includeHidden, CancellationToken ct = default) =>
		await ToDtosAsync(await tournamentRepository.ListAsync(includeHidden, ct), ct);

	// `page` is 1-based. A page past the end comes back with no items but the real TotalCount, so
	// the caller can clamp to the last page. Skip is computed in long so a huge `page` query value
	// can't overflow into a negative offset.
	public async Task<TournamentPageDto> ListPageAsync(bool includeHidden, int page, int pageSize, CancellationToken ct = default)
	{
		var skip = (int)Math.Min((long)(page - 1) * pageSize, int.MaxValue);
		var (items, totalCount) = await tournamentRepository.ListPageAsync(includeHidden, skip, pageSize, ct);
		return new TournamentPageDto(await ToDtosAsync(items, ct), page, pageSize, totalCount);
	}

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

		tournament.Name = InputValidation.RequiredText(request.Name, "Tournament name", Tournament.NameMaxLength);
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
		tournament.IsRanked = request.IsRanked;
		tournament.OverrideEloChange = EloOverride(request.IsRanked, request.OverrideEloChange);
		tournament.BracketType = BracketType(request.BracketType);
		tournament.ConsolationMatchesTargetRank = ConsolationRank(request.ConsolationMatchesTargetRank);
		tournament.Visible = request.Visible;
		tournament.DiscordRoleName = DiscordName(request.DiscordRoleName, "Discord role name");
		tournament.DiscordChannelName = DiscordName(request.DiscordChannelName, "Discord channel name");

		await tournamentRepository.SaveChangesAsync(ct);
		await notifier.TournamentChangedAsync(id);
		return await ToDtoAsync(tournament, ct);
	}

	// Only formats the bracket engine can build are accepted (see BracketFormats).
	private static string? BracketType(string? bracketType)
	{
		BracketFormats.Parse(bracketType);
		return bracketType;
	}

	// A fixed Elo change means nothing for an unranked tournament (results award 0), so it's
	// normalized to "no override" rather than stored as a misleading leftover.
	private static int EloOverride(bool isRanked, int overrideEloChange) => isRanked ? overrideEloChange : -1;

	// "Break ties up to place N": only 3 is supported — a 3rd-place match in single elimination
	// (double elimination already has a unique 3rd place). Larger N would need placement brackets.
	private static int? ConsolationRank(int? rank) => rank is null or 3
		? rank
		: throw new TournamentLifecycleException("Only breaking ties for 3rd place is supported.");

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
		await notifier.TournamentChangedAsync(id);
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

	// The Discord name fields come straight from a form input that defaults to "" rather than
	// omitted — stored verbatim, an empty (or whitespace-only) string reads as "set" downstream
	// (TournamentDiscordResourcesService's `is not null` checks) and gets sent to Discord's API as
	// a 0-length role/channel name, which it rejects. Blank means "no Discord resource wanted",
	// same as never filling the field in, so normalize it to null right at the boundary.
	private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

	private static string? DiscordName(string? s, string fieldName) =>
		InputValidation.OptionalText(NullIfBlank(s), fieldName, Tournament.DiscordNameMaxLength);

	internal static TournamentDto ToDto(Tournament t, string? winnerUid) => new(
		t.Id, t.Name, t.Description,
		t.RegistrationStartDate, t.RegistrationEndDate, t.TournamentStartDate, t.TournamentEndDate,
		t.MinCost, t.MaxCost, t.MinCharacters, t.MinTier, t.MaxTier,
		t.State, t.RegistrationType, t.GameMode, t.Visible, t.BracketType,
		t.ChallongeTournamentId, t.ChallongeTournamentUrl, t.ChallongeWinnerId, winnerUid, t.WinnerTeamId,
		t.DivisionGroupId, t.DivisionIndex,
		t.OverrideEloChange, t.ConsolationMatchesTargetRank, t.DiscordRoleName, t.DiscordChannelName, t.IsRanked);
}
