namespace ZenlessLeague.Api.Features.CommunityCosts.Agents;

// One row per (agent, potential tier). PotentialTier == null means the agent has no
// potential-tier system at all and this single row *is* the whole M0-M6 cost (matches the
// NESC_agent_costs.csv "P=null" rows, e.g. Anby/Nicole). PotentialTier == 0 is the base M0-M6
// row for an agent that *does* have tiers (CSV "P=0"), and 1-6 are additive per-tier overlay
// rows (CSV "P=1..6") layered on top of the base row.
public class AgentCostRow
{
	public Guid Id { get; set; }
	public required string AgentId { get; set; }
	public int? PotentialTier { get; set; }

	// Length 7: M0..M6.
	public required int[] Costs { get; set; }

	public DateTimeOffset UpdatedAt { get; set; }
}
