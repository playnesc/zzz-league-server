using System.Text.Json;

namespace ZenlessLeague.Migrator;

// Mirrors the legacy Firebase RTDB JSON shapes (zzz-league/src/lib/types.ts + the runtime-only
// fields documented in the tournament migration plan). Field names match the RTDB JSON exactly
// (camelCase) — deserialization uses PropertyNameCaseInsensitive, see RtdbReader.

public record LegacyPlayer(
    string? Name,
    string? DiscordId,
    string? Discord,
    int? Elo,
    int? TournamentPoints,
    bool? IsMidConfirmed,
    bool? IsHighConfirmed,
    int? Wins,
    int? Losses,
    int? PlayedTournamentCount,
    int? SeasonalPlayedTournamentCount,
    long? LastPlayedTournamentTimestamp,
    string? Avatar,
    string? Role,
    long? LastEloUpdateTimestamp,
    LegacyLastRegistration? LastRegistration);

public record LegacyLastRegistration(
    string? ZzzUid,
    string? PrizeUid,
    bool? PrizeAsMoney,
    string? DarteNickname,
    string? DarteAccount,
    string? DartePreset,
    string? RosterScreenshot,
    string? HoyolabScreenshot);

public record LegacyArchivePlayer(string? Name, int? Elo, bool? IsMidConfirmed, bool? IsHighConfirmed);

public record LegacyTournament(
    string? Name,
    string? Description,
    long? RegistrationStartDate,
    long? RegistrationEndDate,
    long? TournamentStartDate,
    long? TournamentEndDate,
    int? MinCost,
    int? MaxCost,
    int? MinCharacters,
    int? MinTier,
    int? MaxTier,
    string? ChallongeTournamentId,
    string? ChallongeTournamentUrl,
    string? ChallongeWinnerId,
    string? State,
    string? WinnerId,
    string? Type,
    int? OverrideEloChange,
    int? ConsolationMatchesTargetRank,
    string? DivisionGroupId,
    int? DivisionIndex,
    bool? Visible,
    string? DiscordRoleName,
    string? DiscordChannelName,
    string? DiscordRoleId,
    string? DiscordChannelId,
    Dictionary<string, string>? ChallongeParticipants, // challongeParticipantId -> uid
    Dictionary<string, LegacyRegistration>? Registrations,
    Dictionary<string, LegacyMatch>? Matches);

// Field names deliberately mismatched vs the C# entity — legacy has a historical naming bug
// (darteAccount is actually the preset name, dartePreset is actually the roster name). See
// PlayerRegistrationDetails.cs's comment; LegacyMapper.ToRegistrationDetails un-swaps this.
public record LegacyRegistration(
    string? ZzzUid,
    string? PrizeUid,
    bool? PrizeAsMoney,
    string? DarteNickname,
    string? DarteAccount,
    string? DartePreset,
    string? RosterScreenshot,
    string? HoyolabScreenshot,
    bool? Approved,
    long? RegistrationTimestamp);

public record LegacyMatch(
    long Id,
    string? P1,
    string? P2,
    string? State,
    string? WinnerId,
    string? ResultScreenshot,
    string? ResultP1,
    string? ResultP2,
    bool? P1ApprovedResult,
    bool? P2ApprovedResult,
    string? TechLossUid,
    string? HistoryKey);

public record LegacyHistoryEntry(
    string? P1,
    int P1Change,
    string? P2,
    int? P2Change,
    string? TournamentId,
    // Either a numeric Challonge match id or a sentinel string ("custom"/"techloss"/"adjustment") —
    // genuinely polymorphic in the legacy JSON, resolved in LegacyMapper.
    JsonElement? TournamentMatch,
    string? ResultP1,
    string? ResultP2,
    string? ResultScreenshot,
    long Timestamp);
