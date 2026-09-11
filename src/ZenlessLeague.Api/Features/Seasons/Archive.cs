namespace ZenlessLeague.Api.Features.Seasons;

public class Archive
{
	public Guid Id { get; set; }
	public required string SeasonName { get; set; }
	public DateTimeOffset CreatedAt { get; set; }
	public List<ArchivedPlayerSnapshot> Players { get; set; } = [];
}

public class ArchivedPlayerSnapshot
{
	public Guid Id { get; set; }
	public Guid ArchiveId { get; set; }
	// Nullable, SetNull-on-delete — a real FK (unlike the old Name-only snapshot) so a still-live
	// player can be joined back to their own archive history. Null for archives created before
	// this FK existed, and stays null forever if the player is later deleted.
	public Guid? PlayerId { get; set; }
	public required string Name { get; set; }
	public int Elo { get; set; }
	public bool IsMidConfirmed { get; set; }
	public bool IsHighConfirmed { get; set; }
}
