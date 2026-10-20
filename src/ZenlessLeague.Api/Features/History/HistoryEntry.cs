namespace ZenlessLeague.Api.Features.History;

// Kind is None (and TournamentMatchId is set) for a real bracket-match entry, or TechLoss when that
// bracket match was decided by tech loss; Custom/Adjustment tag entries not tied to a bracket match.
public enum TournamentMatchKind { None, Custom, TechLoss, Adjustment }

public class HistoryEntry
{
	public Guid Id { get; set; }

	public Guid P1PlayerId { get; set; }
	public int P1Change { get; set; }

	public Guid? P2PlayerId { get; set; } // null for setPlayerElo-style single-player adjustments
	public int? P2Change { get; set; }

	public Guid? TournamentId { get; set; }
	public Guid? TournamentMatchId { get; set; }
	public TournamentMatchKind Kind { get; set; } = TournamentMatchKind.None;

	public string? ResultP1 { get; set; }
	public string? ResultP2 { get; set; }

	public DateTimeOffset Timestamp { get; set; }

	public string? LegacyHistoryKey { get; set; }
}
