namespace ZenlessLeague.Api.Features.Seasons;

// Replaces the legacy `archives/{seasonName}` RTDB node (functions/admin/resetSeason.js). Keyed
// by a real Guid Id like every other entity here, but SeasonName is unique and is what the
// legacy API's "key" parameter (deleteArchive.js) actually addresses.
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
	public required string Name { get; set; }
	public int Elo { get; set; }
	public bool IsMidConfirmed { get; set; }
	public bool IsHighConfirmed { get; set; }
}
