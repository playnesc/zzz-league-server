namespace ZenlessLeague.Api.Features.CommunityCosts.Engines;

// One row per engine, or per (engine, overriding agent). AgentId == null is the engine's own
// base row (NESC_engine_costs.csv blank "bis" column); AgentId set is a per-agent override
// (named "bis"). A row's existence *is* the entire "is this (agent, engine) pair assigned"
// mechanism — there's no separate assignment table.
public class EngineCostRow
{
	public Guid Id { get; set; }
	public required string EngineId { get; set; }
	public string? AgentId { get; set; }

	// Length 6 on every row (base and override alike), for exact CSV round-trip fidelity:
	// R1-R5 refinement plus the CSV's unlabeled "off" 6th value (meaning undocumented — see
	// plan §7). The frontend only ever displays 5 values for an override row; the service
	// layer trims the 6th when building EngineCostDto.AgentOverrides.
	public required int[] Costs { get; set; }

	public DateTimeOffset UpdatedAt { get; set; }
}
