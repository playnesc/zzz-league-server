namespace ZenlessLeague.Api.Features.Tournaments;

// EF Core owned type — embedded on TournamentRegistration, not its own table.
// Field names are cleaned up versus the legacy Firebase form, where `darteAccount`
// was labeled "preset name" and `dartePreset` was labeled "roster name" (a naming
// bug from when that form was built) — not replicating that mismatch here.
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
