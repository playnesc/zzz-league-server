namespace ZenlessLeague.Api.Features.CommunityCosts.Catalog;

// Reference data for Community Costs — one row per playable agent. Id is the same slug the
// frontend already uses to resolve bundled avatar images (agentAvatars.ts) and to key every
// contribution, so it's kept as the primary key rather than adding a Guid surrogate on top of it.
public class AgentCatalogEntry
{
	public required string Id { get; set; }
	public required string Name { get; set; }
	public Specialty Specialty { get; set; }

	// Darte's raw rarity value (3/4/5, higher = rarer/stronger). 5 appears exactly once (a
	// hand-placed exception, Claret Flint) — seeded verbatim rather than normalized.
	public int Rarity { get; set; }

	// Preserves the community-authored ordering from the frontend's costData.ts array so CSV
	// export reproduces a stable, sensible row order instead of an arbitrary alphabetical one.
	public int SortOrder { get; set; }
}
