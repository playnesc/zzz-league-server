namespace ZenlessLeague.Api.Features.Players;

public enum PlayerRole { Player, Moderator, Admin }

// `Id` is a real surrogate primary key; `Uid` (the Firebase Auth uid) stays a required, unique,
// non-PK column since it's still the cross-system join key — Firebase JWTs carry it as the "sub"
// claim and the frontend's Firebase Auth SDK only ever deals in this string.
public class Player
{
	// Column limits (AppDbContext) and request validation (PlayerService) share these.
	public const int UidMaxLength = 128;
	public const int NameMaxLength = 100;
	// Avatar is an id from the frontend's avatar picker, not free text — this is only an abuse cap.
	public const int AvatarMaxLength = 200;

	public Guid Id { get; set; }
	public required string Uid { get; set; }
	public required string Name { get; set; }
	public string? DiscordId { get; set; }
	public string? DiscordUsername { get; set; }

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
}
