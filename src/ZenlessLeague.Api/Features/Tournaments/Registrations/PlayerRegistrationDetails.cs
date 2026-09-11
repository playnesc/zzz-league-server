namespace ZenlessLeague.Api.Features.Tournaments;

// EF Core owned type — embedded on TournamentRegistration, not its own table.
public class PlayerRegistrationDetails
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
