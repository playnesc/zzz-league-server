namespace ZenlessLeague.Api.Features.Players;

// Rank order matters — RoleAuthorizationHandler compares via >=, mirroring
// functions/utils/roles.js's ROLE_RANK (player=0 < moderator=1 < admin=2).
public enum PlayerRole { Player, Moderator, Admin }

// Mirrors the Firebase `Player` interface (zzz-league/src/lib/types.ts) plus the
// runtime-only `role`/`lastEloUpdateTimestamp` fields that exist in the live data
// but were never declared in the frontend types. `Id` is a real surrogate primary
// key; `Uid` (the Firebase Auth uid) stays a required, unique, non-PK column since
// it's still the cross-system join key — Firebase JWTs carry it as the "sub" claim
// and the frontend's Firebase Auth SDK only ever deals in this string.
public class Player
{
	public Guid Id { get; set; }
	public required string Uid { get; set; }
	public required string Name { get; set; }
	public string? DiscordId { get; set; }
	public string? DiscordUsername { get; set; } // legacy `discord`

	public int Elo { get; set; } = 1000;
	public int TournamentPoints { get; set; }
	public bool IsMidConfirmed { get; set; }
	public bool IsHighConfirmed { get; set; }
	public int Wins { get; set; }
	public int Losses { get; set; }
	public int PlayedTournamentCount { get; set; }
	public int SeasonalPlayedTournamentCount { get; set; }
	public DateTimeOffset? LastPlayedTournamentTimestamp { get; set; }
	public DateTimeOffset? LastEloUpdateTimestamp { get; set; }

	public string? Avatar { get; set; }
	public PlayerRole Role { get; set; } = PlayerRole.Player;
	public DateTimeOffset CreatedAt { get; set; }

	public LastRegistrationCache? LastRegistrationCache { get; set; }
}

// EF Core owned type — embedded on Player, not its own table. Mirrors the legacy
// `player.lastRegistration` cache (functions/user/applyForTournament.js) that lets a
// player skip re-uploading screenshots the next time they register for a tournament.
public class LastRegistrationCache
{
	public required string GameUid { get; set; }
	public bool PrizeAsMoney { get; set; }
	public string? PrizeUid { get; set; }
	public required string DarteNickname { get; set; }
	public required string DartePresetName { get; set; }
	public required string RosterName { get; set; }
	public string? RosterScreenshotUrl { get; set; }
	public string? HoyolabScreenshotUrl { get; set; }
}
