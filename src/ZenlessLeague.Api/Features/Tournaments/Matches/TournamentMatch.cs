using ZenlessLeague.Api.Features.Tournaments.Bracket;

namespace ZenlessLeague.Api.Features.Tournaments.Matches;

public class TournamentMatch
{
	public Guid Id { get; set; }
	public Guid TournamentId { get; set; }

	// Set only on matches synced from Challonge before the bracket moved in-house; those
	// tournaments have no bracket fields below and still show Challonge's embed.
	public long? ChallongeMatchId { get; set; }

	// The match's place in the bracket (see BracketEngine): Number is the play order and what the
	// UI calls it ("M7"); Round/Position place it in the full power-of-two tree. WinnerTo/LoserTo
	// say where each side goes once it's decided — slot 1 = P1, 2 = P2; null = nowhere (final,
	// or the loser is eliminated). Null on legacy Challonge-era rows.
	public BracketSection? Section { get; set; }
	public int? Round { get; set; }
	public int? Position { get; set; }
	public int? Number { get; set; }
	public Guid? WinnerToMatchId { get; set; }
	public int? WinnerToSlot { get; set; }
	public Guid? LoserToMatchId { get; set; }
	public int? LoserToSlot { get; set; }

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

	// Set when the result came from a Nexus draft link (solo matches); NexusResultId is the draft id.
	// NexusSnapshot is a JSON array of the integration endpoint's draft responses as fetched at
	// submission (pasted draft first, then its series' other games — see NexusResultParser); older
	// rows may hold the pre-integration /api/results/{id} JSON, which no longer parses and is ignored.
	// NexusP1IsLeft says whether P1 is the pasted draft's creator (null if a moderator stored a link
	// without the sides being known).
	public string? NexusResultId { get; set; }
	public string? NexusSnapshot { get; set; }
	public bool? NexusP1IsLeft { get; set; }

	// Optimistic concurrency token, mapped to Postgres's xmin system column (see AppDbContext) — two
	// requests racing to finalize the same result can't both save (and both register Elo).
	public uint Version { get; set; }
}
