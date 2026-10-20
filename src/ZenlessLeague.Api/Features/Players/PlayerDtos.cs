namespace ZenlessLeague.Api.Features.Players;

public record CreatePlayerRequest(string Uid, string Name, string? Avatar = null);

public record ClaimPlayerRequest(string ExistingName, string NewUid);

public record UpdatePlayerRoleRequest(PlayerRole Role);

// GET /api/players/staff (admin page) — moderators and admins only.
public record StaffMemberDto(string Uid, string Name, string? Avatar, PlayerRole Role);

public record UpdatePlayerNameRequest(string Name);

public record UpdatePlayerAvatarRequest(string Avatar);

// GET /api/players/me/last-registration — the caller's most recent form-filled solo registration,
// for the register page's "fill from previous registration" button.
public record LastRegistrationDto(
	string GameUid,
	bool PrizeAsMoney,
	string? PrizeUid,
	string DarteNickname,
	string DartePresetName,
	string RosterName,
	string? RosterScreenshotUrl,
	string? HoyolabScreenshotUrl);

public record PlayerDto(
	string Uid,
	string Name,
	string? DiscordId,
	string? DiscordUsername,
	int Elo,
	int TournamentPoints,
	bool IsMidConfirmed,
	bool IsHighConfirmed,
	int Wins,
	int Losses,
	int PlayedTournamentCount,
	int SeasonalPlayedTournamentCount,
	DateTimeOffset? LastPlayedTournamentTimestamp,
	string? Avatar,
	PlayerRole Role,
	DateTimeOffset CreatedAt);

// Lean shape for GET /api/players (optionally filtered by ?uids=) — everything list-context
// consumers (leaderboard, pickers, tournament rosters, history rows) actually render. Excludes
// DiscordId/Role/CreatedAt, which are only ever used off a single-player fetch
// (the logged-in user's own PlayerDto), never in a list of other players.
public record PlayerListItemDto(
	string Uid,
	string Name,
	string? DiscordUsername,
	int Elo,
	int TournamentPoints,
	bool IsMidConfirmed,
	bool IsHighConfirmed,
	int Wins,
	int Losses,
	int PlayedTournamentCount,
	int SeasonalPlayedTournamentCount,
	DateTimeOffset? LastPlayedTournamentTimestamp,
	string? Avatar);
