using ZenlessLeague.Api.Features.CommunityCosts.Catalog;

namespace ZenlessLeague.Api.Features.CommunityCosts.Agents;

// Flattened view of the base (no-tier or P=0) row joined with its catalog entry — matches the
// frontend's existing costData.ts `AgentCost` shape. Potential-tier rows aren't exposed here in
// v1 (see plan §7 open risk 3); CSV export/import (added later) reads/writes the full tier set.
public record AgentCostDto(string AgentId, string Name, Specialty Specialty, int Rarity, int[] Costs);

public record ImportAgentCostCsvRequest(string Csv);
