using ZenlessLeague.Api.Features.CommunityCosts.Catalog;

namespace ZenlessLeague.Api.Features.CommunityCosts.Engines;

public record EngineAgentOverrideDto(string AgentId, int[] Costs);

// BaseCosts is length 6 (matches EngineCostRow); AgentOverrides.Costs is trimmed to 5
// (R1-R5 only) to match the frontend's existing weaponData.ts `WeaponAgentOverride` shape.
public record EngineCostDto(
	string EngineId,
	string Name,
	Specialty Specialty,
	int Rarity,
	int[] BaseCosts,
	List<EngineAgentOverrideDto> AgentOverrides);

public record ImportEngineCostCsvRequest(string Csv);
