namespace ZenlessLeague.Api.Features.Tournaments.Matches;

public class TournamentMatch
{
	public Guid Id { get; set; }
	public Guid TournamentId { get; set; }

	public long ChallongeMatchId { get; set; }

	public Guid? P1PlayerId { get; set; }
	public Guid? P2PlayerId { get; set; }

	public Guid? P1TeamId { get; set; }
	public Guid? P2TeamId { get; set; }

	public required string State { get; set; }
	public Guid? WinnerPlayerId { get; set; }
	public Guid? WinnerTeamId { get; set; }

	public string? ResultScreenshotUrl { get; set; }
	public string? ResultP1 { get; set; }
	public string? ResultP2 { get; set; }
	public bool P1ApprovedResult { get; set; }
	public bool P2ApprovedResult { get; set; }
	public Guid? TechLossPlayerId { get; set; }
	public Guid? TechLossTeamId { get; set; }

	public Guid? HistoryEntryId { get; set; }
	public Guid? HistoryEntryId2 { get; set; }

	// Set when the result came from a nexus-shiyu.com result link (solo matches). NexusSnapshot is
	// the raw /api/results/{id} JSON as fetched at submission; NexusP1IsLeft says which Nexus side
	// is P1 (null if a moderator stored a link without the sides being known).
	public string? NexusResultId { get; set; }
	public string? NexusSnapshot { get; set; }
	public bool? NexusP1IsLeft { get; set; }

	// Optimistic concurrency token, mapped to Postgres's xmin system column (see AppDbContext) — two
	// requests racing to finalize the same result can't both save (and both register Elo).
	public uint Version { get; set; }
}
