using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Storage;
using ZenlessLeague.Api.Features.Teams;
using ZenlessLeague.Api.Features.Tournaments.Realtime;
using ZenlessLeague.Api.Infrastructure.Errors;

namespace ZenlessLeague.Api.Features.Tournaments;

public interface ITournamentRegistrationService
{
	Task<TournamentRegistrationDto?> RegisterSoloAsync(Guid tournamentId, SoloRegistrationRequest request, CancellationToken ct = default);
	Task<TournamentRegistrationDto?> RegisterTeamAsync(Guid tournamentId, TeamRegistrationRequest request, CancellationToken ct = default);
	// Public — anonymous visitors can browse a tournament's participants. callerUid (if any)
	// determines which registrations' GameUid/PrizeUid/PrizeAsMoney come through unredacted: a
	// moderator+ sees everyone's, everyone else only sees their own (an anonymous caller sees
	// none of theirs, having none).
	Task<List<TournamentRegistrationDto>> ListAsync(Guid tournamentId, string? callerUid = null, CancellationToken ct = default);
	Task<TournamentRegistrationDto?> GetAsync(Guid tournamentId, Guid id, string? callerUid = null, CancellationToken ct = default);

	Task<TournamentRegistrationDto?> AdminAddAsync(Guid tournamentId, string playerId, CancellationToken ct = default);
	Task<TournamentRegistrationDto?> AdminAddTeamAsync(Guid tournamentId, Guid teamId, CancellationToken ct = default);
	Task<TournamentRegistrationDto?> SetApprovedAsync(Guid tournamentId, Guid registrationId, bool approved, CancellationToken ct = default);
	Task<bool?> CancelAsync(Guid tournamentId, string callerPlayerId, CancellationToken ct = default);
}

/// <summary>Thrown for registration validation failures (wrong type, closed window, ineligible tier, missing fields) — maps to 400.</summary>
public class TournamentValidationException(string message) : ValidationApiException(message);

public class TournamentRegistrationService(
	ITournamentRepository tournamentRepository,
	ITeamRepository teamRepository,
	IPlayerRepository playerRepository,
	ITournamentRegistrationRepository registrationRepository,
	IFirebaseStorageClient storageClient,
	ITournamentNotifier notifier) : ITournamentRegistrationService
{
	public async Task<TournamentRegistrationDto?> RegisterSoloAsync(Guid tournamentId, SoloRegistrationRequest request, CancellationToken ct = default)
	{
		var tournament = await tournamentRepository.GetByIdAsync(tournamentId, ct);
		if (tournament is null)
		{
			return null;
		}

		if (tournament.RegistrationType != TournamentRegistrationType.Solo)
		{
			throw new TournamentValidationException("This tournament requires a team registration.");
		}

		EnsureRegistrationOpen(tournament);

		var player = await playerRepository.GetEntityByUidAsync(request.PlayerId, ct)
			?? throw new TournamentValidationException($"Player '{request.PlayerId}' does not exist.");
		EnsureTierEligible(tournament, player);

		var registration = await registrationRepository.FindExistingAsync(tournamentId, player.Id, null, ct);
		var lastRegistration = await registrationRepository.GetMostRecentSoloByPlayerAsync(player.Id, ct);

		var details = await BuildDetailsAsync(
			tournamentId, player.Id, request.Details, registration?.Player1Details, lastRegistration?.Player1Details, ct);

		if (registration is null)
		{
			registration = new TournamentRegistration
			{
				Id = Guid.NewGuid(),
				TournamentId = tournamentId,
				PlayerId = player.Id,
				Player1Details = details,
				RegisteredAt = DateTimeOffset.UtcNow,
				UpdatedAt = DateTimeOffset.UtcNow,
			};
			await registrationRepository.CreateAsync(registration, ct);
		}
		else
		{
			registration.Player1Details = details;
			registration.UpdatedAt = DateTimeOffset.UtcNow;
			await registrationRepository.SaveChangesAsync(ct);
		}

		await notifier.RegistrationChangedAsync(tournamentId, registration.Id, ct);

		return ToDto(registration, player.Uid, includeSensitive: true);
	}

	public async Task<TournamentRegistrationDto?> RegisterTeamAsync(Guid tournamentId, TeamRegistrationRequest request, CancellationToken ct = default)
	{
		var tournament = await tournamentRepository.GetByIdAsync(tournamentId, ct);
		if (tournament is null)
		{
			return null;
		}

		if (tournament.RegistrationType != TournamentRegistrationType.Team)
		{
			throw new TournamentValidationException("This tournament requires a solo registration.");
		}

		EnsureRegistrationOpen(tournament);

		var team = await teamRepository.GetByIdAsync(request.TeamId, ct)
			?? throw new TournamentValidationException($"Team '{request.TeamId}' does not exist.");

		_ = await playerRepository.GetEntityByIdAsync(team.Player1Id, ct)
			?? throw new TournamentValidationException($"Player '{team.Player1Id}' does not exist.");
		_ = await playerRepository.GetEntityByIdAsync(team.Player2Id, ct)
			?? throw new TournamentValidationException($"Player '{team.Player2Id}' does not exist.");
		// TODO: team registrations intentionally skip the tournament's tier filter for now (unlike solo,
		// see EnsureTierEligible). Decide whether both members should have to fit MinTier/MaxTier.

		var registration = await registrationRepository.FindExistingAsync(tournamentId, null, request.TeamId, ct);
		if (registration is null)
		{
			await EnsureNeitherMemberRegisteredElsewhereAsync(tournamentId, team, ct);
		}

		var player1Details = await BuildDetailsAsync(
			tournamentId, team.Player1Id, request.Player1, registration?.Player1Details, null, ct);
		var player2Details = await BuildDetailsAsync(
			tournamentId, team.Player2Id, request.Player2, registration?.Player2Details, null, ct);

		if (registration is null)
		{
			registration = new TournamentRegistration
			{
				Id = Guid.NewGuid(),
				TournamentId = tournamentId,
				TeamId = request.TeamId,
				Player1Details = player1Details,
				Player2Details = player2Details,
				RegisteredAt = DateTimeOffset.UtcNow,
				UpdatedAt = DateTimeOffset.UtcNow,
			};
			await registrationRepository.CreateAsync(registration, ct);
		}
		else
		{
			registration.Player1Details = player1Details;
			registration.Player2Details = player2Details;
			registration.UpdatedAt = DateTimeOffset.UtcNow;
			await registrationRepository.SaveChangesAsync(ct);
		}

		await notifier.RegistrationChangedAsync(tournamentId, registration.Id, ct);
		return ToDto(registration, playerUid: null, includeSensitive: true);
	}

	public async Task<List<TournamentRegistrationDto>> ListAsync(Guid tournamentId, string? callerUid = null, CancellationToken ct = default)
	{
		var registrations = await registrationRepository.ListByTournamentAsync(tournamentId, ct);
		var uidsById = await playerRepository.GetUidsByIdsAsync(
			registrations.Where(r => r.PlayerId is not null).Select(r => r.PlayerId!.Value), ct);

		var (isModeratorOrAbove, ownRegistrationId) = await ResolveSensitiveFieldAccessAsync(tournamentId, callerUid, ct);

		return registrations.Select(r => ToDto(
			r, r.PlayerId is Guid pid ? uidsById.GetValueOrDefault(pid) : null,
			includeSensitive: isModeratorOrAbove || r.Id == ownRegistrationId)).ToList();
	}

	public async Task<TournamentRegistrationDto?> GetAsync(Guid tournamentId, Guid id, string? callerUid = null, CancellationToken ct = default)
	{
		var registration = await registrationRepository.GetByIdAsync(tournamentId, id, ct);
		if (registration is null)
		{
			return null;
		}

		var playerUid = registration.PlayerId is Guid pid ? (await playerRepository.GetByIdAsync(pid, ct))?.Uid : null;
		var (isModeratorOrAbove, ownRegistrationId) = await ResolveSensitiveFieldAccessAsync(tournamentId, callerUid, ct);
		return ToDto(registration, playerUid, includeSensitive: isModeratorOrAbove || registration.Id == ownRegistrationId);
	}

	// GameUid/PrizeUid/PrizeAsMoney are the only fields ListAsync/GetAsync ever redact — every
	// other caller of ToDto (self-registration, admin actions) always passes includeSensitive:
	// true since those are already legitimately scoped to the registrant or an admin.
	private async Task<(bool IsModeratorOrAbove, Guid? OwnRegistrationId)> ResolveSensitiveFieldAccessAsync(
		Guid tournamentId, string? callerUid, CancellationToken ct)
	{
		if (callerUid is null)
		{
			return (false, null);
		}

		var caller = await playerRepository.GetEntityByUidAsync(callerUid, ct);
		if (caller is null)
		{
			return (false, null);
		}

		var ownRegistration = await registrationRepository.FindExistingAsync(tournamentId, caller.Id, null, ct)
			?? await registrationRepository.FindByPlayerTeamMembershipAsync(tournamentId, caller.Id, ct);

		return (caller.Role >= PlayerRole.Moderator, ownRegistration?.Id);
	}

	public async Task<TournamentRegistrationDto?> AdminAddAsync(Guid tournamentId, string playerId, CancellationToken ct = default)
	{
		var tournament = await tournamentRepository.GetByIdAsync(tournamentId, ct);
		if (tournament is null)
		{
			return null;
		}

		if (TournamentStateMachine.IsLocked(tournament.State) || tournament.ChallongeTournamentId is not null)
		{
			throw new TournamentValidationException("Tournament has already started.");
		}

		if (tournament.RegistrationType != TournamentRegistrationType.Solo)
		{
			throw new TournamentValidationException("This tournament requires a team registration — use admin-add-team instead.");
		}

		var player = await playerRepository.GetEntityByUidAsync(playerId, ct)
			?? throw new TournamentValidationException($"Player '{playerId}' does not exist.");

		if (await registrationRepository.FindExistingAsync(tournamentId, player.Id, null, ct) is not null)
		{
			throw new TournamentValidationException("Player is already registered.");
		}

		var registration = new TournamentRegistration
		{
			Id = Guid.NewGuid(),
			TournamentId = tournamentId,
			PlayerId = player.Id,
			Player1Details = StubDetails(player.Name),
			Approved = true,
			RegisteredAt = DateTimeOffset.UtcNow,
			UpdatedAt = DateTimeOffset.UtcNow,
		};

		await registrationRepository.CreateAsync(registration, ct);
		await notifier.RegistrationChangedAsync(tournamentId, registration.Id, ct);
		return ToDto(registration, player.Uid, includeSensitive: true);
	}

	public async Task<TournamentRegistrationDto?> AdminAddTeamAsync(Guid tournamentId, Guid teamId, CancellationToken ct = default)
	{
		var tournament = await tournamentRepository.GetByIdAsync(tournamentId, ct);
		if (tournament is null)
		{
			return null;
		}

		if (TournamentStateMachine.IsLocked(tournament.State) || tournament.ChallongeTournamentId is not null)
		{
			throw new TournamentValidationException("Tournament has already started.");
		}

		if (tournament.RegistrationType != TournamentRegistrationType.Team)
		{
			throw new TournamentValidationException("This tournament requires a solo registration — use admin-add instead.");
		}

		var team = await teamRepository.GetByIdAsync(teamId, ct)
			?? throw new TournamentValidationException($"Team '{teamId}' does not exist.");
		var player1 = await playerRepository.GetByIdAsync(team.Player1Id, ct)
			?? throw new TournamentValidationException($"Player '{team.Player1Id}' does not exist.");
		var player2 = await playerRepository.GetByIdAsync(team.Player2Id, ct)
			?? throw new TournamentValidationException($"Player '{team.Player2Id}' does not exist.");

		if (await registrationRepository.FindExistingAsync(tournamentId, null, teamId, ct) is not null)
		{
			throw new TournamentValidationException("Team is already registered.");
		}

		await EnsureNeitherMemberRegisteredElsewhereAsync(tournamentId, team, ct);

		var registration = new TournamentRegistration
		{
			Id = Guid.NewGuid(),
			TournamentId = tournamentId,
			TeamId = teamId,
			Player1Details = StubDetails(player1.Name),
			Player2Details = StubDetails(player2.Name),
			Approved = true,
			RegisteredAt = DateTimeOffset.UtcNow,
			UpdatedAt = DateTimeOffset.UtcNow,
		};

		await registrationRepository.CreateAsync(registration, ct);
		await notifier.RegistrationChangedAsync(tournamentId, registration.Id, ct);
		return ToDto(registration, playerUid: null, includeSensitive: true);
	}

	// A player can only be on one registered team per tournament — check both members against
	// every OTHER team's registration here (not just an exact-team lookup, which FindExistingAsync
	// already covers separately for "is this same team already registered").
	private async Task EnsureNeitherMemberRegisteredElsewhereAsync(Guid tournamentId, Team team, CancellationToken ct)
	{
		var player1Conflict = await registrationRepository.FindByPlayerTeamMembershipAsync(tournamentId, team.Player1Id, ct);
		if (player1Conflict is not null)
		{
			throw new TournamentValidationException("One of this team's players is already registered with a different team.");
		}

		var player2Conflict = await registrationRepository.FindByPlayerTeamMembershipAsync(tournamentId, team.Player2Id, ct);
		if (player2Conflict is not null)
		{
			throw new TournamentValidationException("One of this team's players is already registered with a different team.");
		}
	}

	// Admin-added registrations skip the usual roster/screenshot requirements (BuildDetailsAsync)
	// — there's no form input to pull them from — leaving everything but the player's name blank.
	private static PlayerRegistrationDetails StubDetails(string darteNickname) => new()
	{
		GameUid = string.Empty,
		PrizeAsMoney = false,
		PrizeUid = null,
		DarteNickname = darteNickname,
		DartePresetName = string.Empty,
		RosterName = string.Empty,
	};

	public async Task<TournamentRegistrationDto?> SetApprovedAsync(Guid tournamentId, Guid registrationId, bool approved, CancellationToken ct = default)
	{
		var registration = await registrationRepository.GetByIdAsync(tournamentId, registrationId, ct);
		if (registration is null)
		{
			return null;
		}

		registration.Approved = approved;
		registration.UpdatedAt = DateTimeOffset.UtcNow;
		await registrationRepository.SaveChangesAsync(ct);
		await notifier.RegistrationChangedAsync(tournamentId, registrationId, ct);

		var playerUid = registration.PlayerId is Guid pid ? (await playerRepository.GetByIdAsync(pid, ct))?.Uid : null;
		return ToDto(registration, playerUid, includeSensitive: true);
	}

	public async Task<bool?> CancelAsync(Guid tournamentId, string callerPlayerId, CancellationToken ct = default)
	{
		var tournament = await tournamentRepository.GetByIdAsync(tournamentId, ct);
		if (tournament is null)
		{
			return null;
		}

		if (TournamentStateMachine.IsLocked(tournament.State) || tournament.ChallongeTournamentId is not null)
		{
			throw new TournamentValidationException("Tournament has already started.");
		}

		var caller = await playerRepository.GetEntityByUidAsync(callerPlayerId, ct);
		// Solo registrations are keyed by PlayerId directly; a team registration has neither
		// member's id on the row itself, so either member canceling has to be resolved through
		// their team membership instead.
		var registration = caller is null
			? null
			: await registrationRepository.FindExistingAsync(tournamentId, caller.Id, null, ct)
				?? await registrationRepository.FindByPlayerTeamMembershipAsync(tournamentId, caller.Id, ct);
		if (registration is null)
		{
			return false;
		}

		var registrationId = registration.Id;
		await registrationRepository.DeleteAsync(registration, ct);
		await notifier.RegistrationChangedAsync(tournamentId, registrationId, ct);
		return true;
	}

	private static void EnsureRegistrationOpen(Tournament tournament)
	{
		if (!TournamentStateMachine.IsRegistrationOpen(tournament.State))
		{
			throw new TournamentValidationException("Tournament is not accepting registrations.");
		}

		var now = DateTimeOffset.UtcNow;
		if (now < tournament.RegistrationStartDate || now > tournament.RegistrationEndDate)
		{
			throw new TournamentValidationException("Registration window is closed.");
		}
	}

	private static void EnsureTierEligible(Tournament tournament, Player player)
	{
		var tier = player.IsHighConfirmed ? 1000 : player.IsMidConfirmed ? 100 : 0;
		if (tier < tournament.MinTier || tier > tournament.MaxTier)
		{
			throw new TournamentValidationException($"Player '{player.Uid}' is out of this tournament's tier group.");
		}
	}

	private async Task<PlayerRegistrationDetails> BuildDetailsAsync(
		Guid tournamentId, Guid playerId, PlayerRegistrationDetailsRequest r,
		PlayerRegistrationDetails? existingDetails, PlayerRegistrationDetails? lastReg, CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(r.GameUid) ||
			string.IsNullOrWhiteSpace(r.DarteNickname) ||
			string.IsNullOrWhiteSpace(r.DartePresetName) ||
			string.IsNullOrWhiteSpace(r.RosterName) ||
			(!r.PrizeAsMoney && string.IsNullOrWhiteSpace(r.PrizeUid)))
		{
			throw new TournamentValidationException("Missing required registration fields.");
		}

		var rosterScreenshotUrl = r.RosterScreenshot is not null
			? await storageClient.UploadImageAsync(r.RosterScreenshot, $"tournaments/{tournamentId}/{playerId}-roster", ct)
			: existingDetails?.RosterScreenshotUrl ?? lastReg?.RosterScreenshotUrl;

		var hoyolabScreenshotUrl = r.HoyolabScreenshot is not null
			? await storageClient.UploadImageAsync(r.HoyolabScreenshot, $"tournaments/{tournamentId}/{playerId}-hoyolab", ct)
			: existingDetails?.HoyolabScreenshotUrl ?? lastReg?.HoyolabScreenshotUrl;

		if (rosterScreenshotUrl is null || hoyolabScreenshotUrl is null)
		{
			throw new TournamentValidationException("Missing required registration fields.");
		}

		return new PlayerRegistrationDetails
		{
			GameUid = r.GameUid,
			PrizeAsMoney = r.PrizeAsMoney,
			PrizeUid = r.PrizeAsMoney ? null : r.PrizeUid,
			DarteNickname = r.DarteNickname,
			DartePresetName = r.DartePresetName,
			RosterName = r.RosterName,
			RosterScreenshotUrl = rosterScreenshotUrl,
			HoyolabScreenshotUrl = hoyolabScreenshotUrl,
		};
	}

	private static PlayerRegistrationDetailsDto ToDetailsDto(PlayerRegistrationDetails d, bool includeSensitive) => new(
		includeSensitive ? d.GameUid : "",
		includeSensitive && d.PrizeAsMoney,
		includeSensitive ? d.PrizeUid : null,
		d.DarteNickname, d.DartePresetName, d.RosterName, d.RosterScreenshotUrl, d.HoyolabScreenshotUrl);

	private static TournamentRegistrationDto ToDto(TournamentRegistration r, string? playerUid, bool includeSensitive) => new(
		r.Id, r.TournamentId, r.PlayerId is not null ? TournamentRegistrationType.Solo : TournamentRegistrationType.Team,
		playerUid, r.TeamId,
		ToDetailsDto(r.Player1Details, includeSensitive),
		r.Player2Details is null ? null : ToDetailsDto(r.Player2Details, includeSensitive),
		r.Approved, r.RegisteredAt, r.UpdatedAt);
}
