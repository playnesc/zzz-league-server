namespace ZenlessLeague.Api.Features.CommunityCosts.Catalog;

// Reference data for Community Costs — one row per W-engine. Id is the darte numeric-looking
// id already used by the frontend's weaponAvatars.ts and every engine contribution.
public class EngineCatalogEntry
{
	public required string Id { get; set; }
	public required string Name { get; set; }
	public Specialty Specialty { get; set; }

	// Darte's raw rarity value: 2 = B-rank, 3 = A-rank, 4 = S-rank.
	public int Rarity { get; set; }

	public int SortOrder { get; set; }
}
