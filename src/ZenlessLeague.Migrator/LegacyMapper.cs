using System.Text.Json;
using ZenlessLeague.Api.Features.History;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Seasons;
using ZenlessLeague.Api.Features.Tournaments;
using ZenlessLeague.Api.Features.Tournaments.Matches;

namespace ZenlessLeague.Migrator;

// Pure legacy-JSON-to-entity transforms — no I/O, so these are unit-testable without a database
// or a real RTDB connection (see ZenlessLeague.Migrator.Tests).
public static class LegacyMapper
{
	public static void ApplyTo(Player player, string uid, LegacyPlayer legacy)
	{
		player.Name = legacy.Name ?? uid;
		player.DiscordId = legacy.DiscordId;
		player.DiscordUsername = legacy.Discord;
		player.Elo = legacy.Elo ?? 1000;
		player.TournamentPoints = legacy.TournamentPoints ?? 0;
		player.IsMidConfirmed = legacy.IsMidConfirmed ?? false;
		player.IsHighConfirmed = legacy.IsHighConfirmed ?? false;
		player.Wins = legacy.Wins ?? 0;
		player.Losses = legacy.Losses ?? 0;
		player.PlayedTournamentCount = legacy.PlayedTournamentCount ?? 0;
		player.SeasonalPlayedTournamentCount = legacy.SeasonalPlayedTournamentCount ?? 0;
		player.LastPlayedTournamentTimestamp = ToDateTimeOffset(legacy.LastPlayedTournamentTimestamp);
		player.LastEloUpdateTimestamp = ToDateTimeOffset(legacy.LastEloUpdateTimestamp);
		player.Avatar = legacy.Avatar;
		player.Role = ParseRole(legacy.Role);
		// legacy.LastRegistration used to seed Player.LastRegistrationCache — that cache was
		// removed in favor of a live query against TournamentRegistrations (see
		// PlayerService.BuildDtoAsync), so there's nothing left to map here.
	}

	public static PlayerRole ParseRole(string? role) => role switch
	{
		"admin" => PlayerRole.Admin,
		"moderator" => PlayerRole.Moderator,
		_ => PlayerRole.Player,
	};

	public static DateTimeOffset? ToDateTimeOffset(long? unixMillis) =>
		unixMillis is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(unixMillis.Value);

	public static ArchivedPlayerSnapshot ToArchivedPlayerSnapshot(LegacyArchivePlayer legacy) => new()
	{
		Id = Guid.NewGuid(),
		Name = legacy.Name ?? "unknown",
		Elo = legacy.Elo ?? 1000,
		IsMidConfirmed = legacy.IsMidConfirmed ?? false,
		IsHighConfirmed = legacy.IsHighConfirmed ?? false,
	};

	public static void ApplyTo(Tournament tournament, string legacyId, LegacyTournament legacy, IReadOnlyDictionary<string, Guid> playerIdsByUid)
	{
		tournament.LegacyId = legacyId;
		tournament.Name = legacy.Name ?? legacyId;
		tournament.Description = legacy.Description;
		tournament.RegistrationStartDate = ToDateTimeOffset(legacy.RegistrationStartDate) ?? DateTimeOffset.UnixEpoch;
		tournament.RegistrationEndDate = ToDateTimeOffset(legacy.RegistrationEndDate) ?? DateTimeOffset.UnixEpoch;
		tournament.TournamentStartDate = ToDateTimeOffset(legacy.TournamentStartDate) ?? DateTimeOffset.UnixEpoch;
		tournament.TournamentEndDate = ToDateTimeOffset(legacy.TournamentEndDate) ?? DateTimeOffset.UnixEpoch;
		tournament.MinCost = legacy.MinCost ?? 0;
		tournament.MaxCost = legacy.MaxCost ?? 0;
		tournament.MinCharacters = legacy.MinCharacters ?? 0;
		tournament.MinTier = legacy.MinTier ?? 0;
		tournament.MaxTier = legacy.MaxTier ?? 1000;
		tournament.State = ParseState(legacy.State);
		tournament.RegistrationType = TournamentRegistrationType.Solo; // legacy tournaments are 100% solo
		tournament.Visible = legacy.Visible ?? true;
		tournament.BracketType = legacy.Type;
		tournament.ChallongeTournamentId = legacy.ChallongeTournamentId;
		tournament.ChallongeTournamentUrl = legacy.ChallongeTournamentUrl;
		tournament.ChallongeWinnerId = legacy.ChallongeWinnerId;
		tournament.WinnerId = legacy.WinnerId is not null && playerIdsByUid.TryGetValue(legacy.WinnerId, out var winnerId) ? winnerId : null;
		tournament.OverrideEloChange = legacy.OverrideEloChange;
		tournament.ConsolationMatchesTargetRank = legacy.ConsolationMatchesTargetRank;
		tournament.DivisionGroupId = legacy.DivisionGroupId;
		tournament.DivisionIndex = legacy.DivisionIndex;
		tournament.DiscordRoleName = legacy.DiscordRoleName;
		tournament.DiscordChannelName = legacy.DiscordChannelName;
		tournament.DiscordRoleId = legacy.DiscordRoleId;
		tournament.DiscordChannelId = legacy.DiscordChannelId;
	}

	public static TournamentState ParseState(string? state) => state switch
	{
		"registration" => TournamentState.Registration,
		"registration_closed" => TournamentState.RegistrationClosed,
		"bracket_created" => TournamentState.BracketCreated,
		"started" => TournamentState.Started,
		"awaiting_review" => TournamentState.AwaitingReview,
		"complete" => TournamentState.Complete,
		_ => TournamentState.Registration,
	};

	public static PlayerRegistrationDetails ToRegistrationDetails(LegacyRegistration legacy) => new()
	{
		GameUid = legacy.ZzzUid ?? string.Empty,
		PrizeAsMoney = legacy.PrizeAsMoney ?? false,
		PrizeUid = legacy.PrizeUid,
		DarteNickname = legacy.DarteNickname ?? string.Empty,
		DartePresetName = legacy.DarteAccount ?? string.Empty, // naming-bug swap, see above
		RosterName = legacy.DartePreset ?? string.Empty,
		RosterScreenshotUrl = legacy.RosterScreenshot,
		HoyolabScreenshotUrl = legacy.HoyolabScreenshot,
	};

	public static void ApplyTo(
		TournamentMatch match, Guid tournamentId, LegacyMatch legacy,
		Guid? p1PlayerId, Guid? p2PlayerId, Guid? winnerPlayerId, Guid? techLossPlayerId)
	{
		match.TournamentId = tournamentId;
		match.ChallongeMatchId = legacy.Id;
		match.P1PlayerId = p1PlayerId;
		match.P2PlayerId = p2PlayerId;
		match.State = legacy.State ?? "open";
		match.WinnerPlayerId = winnerPlayerId;
		match.ResultScreenshotUrl = legacy.ResultScreenshot;
		match.ResultP1 = legacy.ResultP1;
		match.ResultP2 = legacy.ResultP2;
		match.P1ApprovedResult = legacy.P1ApprovedResult ?? false;
		match.P2ApprovedResult = legacy.P2ApprovedResult ?? false;
		match.TechLossPlayerId = techLossPlayerId;
	}

	// Resolves the polymorphic `tournamentMatch` field on a history entry: a numeric Challonge
	// match id (real bracket match) or a sentinel string ("custom"/"techloss"/"adjustment").
	public static (long? ChallongeMatchId, TournamentMatchKind Kind) ResolveTournamentMatch(JsonElement? element)
	{
		if (element is not { } value || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
		{
			return (null, TournamentMatchKind.None);
		}

		if (value.ValueKind == JsonValueKind.Number)
		{
			return (value.GetInt64(), TournamentMatchKind.None);
		}

		return value.GetString() switch
		{
			"custom" => (null, TournamentMatchKind.Custom),
			"techloss" => (null, TournamentMatchKind.TechLoss),
			"adjustment" => (null, TournamentMatchKind.Adjustment),
			var text when long.TryParse(text, out var parsed) => (parsed, TournamentMatchKind.None),
			_ => (null, TournamentMatchKind.None),
		};
	}

	// Legacy match participant refs use the literal string "TBD" for an unfilled bracket slot.
	public static Guid? ResolvePlayerRef(string? uid, IReadOnlyDictionary<string, Guid> playerIdsByUid) =>
		string.IsNullOrEmpty(uid) || uid == "TBD" || !playerIdsByUid.TryGetValue(uid, out var id) ? null : id;
}
