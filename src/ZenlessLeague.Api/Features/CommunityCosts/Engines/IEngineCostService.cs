using ZenlessLeague.Api.Features.CommunityCosts.Agents;

namespace ZenlessLeague.Api.Features.CommunityCosts.Engines;

public interface IEngineCostService
{
	Task<List<EngineCostDto>> ListAsync(CancellationToken ct = default);
	Task<string> ExportCsvAsync(CancellationToken ct = default);
	Task ImportCsvAsync(string csv, CancellationToken ct = default);
}

// Depends on IAgentCostRepository (read-only catalog lookups only) to resolve the CSV's "bis"
// column, which names an agent by Name rather than by its EngineCostRow.AgentId slug.
public class EngineCostService(IEngineCostRepository repository, IAgentCostRepository agentCostRepository) : IEngineCostService
{
	public async Task<List<EngineCostDto>> ListAsync(CancellationToken ct = default)
	{
		var catalog = await repository.ListCatalogAsync(ct);
		var rows = await repository.ListAllCostRowsAsync(ct);
		var rowsByEngine = rows.GroupBy(r => r.EngineId).ToDictionary(g => g.Key, g => g.ToList());

		return catalog
			.Select(engine =>
			{
				rowsByEngine.TryGetValue(engine.Id, out var engineRows);
				var baseRow = engineRows?.FirstOrDefault(r => r.AgentId is null);
				var overrides = (engineRows ?? [])
					.Where(r => r.AgentId is not null)
					// Trim to R1-R5 (drop the 6th "off" value) to match the frontend's override shape.
					.Select(r => new EngineAgentOverrideDto(r.AgentId!, r.Costs.Take(5).ToArray()))
					.ToList();

				return new EngineCostDto(
					engine.Id,
					engine.Name,
					engine.Specialty,
					engine.Rarity,
					baseRow?.Costs ?? new int[6],
					overrides);
			})
			.ToList();
	}

	public async Task<string> ExportCsvAsync(CancellationToken ct = default)
	{
		var catalog = await repository.ListCatalogAsync(ct);
		var rows = await repository.ListAllCostRowsAsync(ct);
		var agentNamesById = (await agentCostRepository.ListCatalogAsync(ct)).ToDictionary(a => a.Id, a => a.Name);
		return EngineCostCsv.Export(catalog, agentNamesById, rows);
	}

	public async Task ImportCsvAsync(string csv, CancellationToken ct = default)
	{
		var parsed = EngineCostCsv.Parse(csv);
		var engineIdByName = (await repository.ListCatalogAsync(ct)).ToDictionary(e => e.Name, e => e.Id, StringComparer.Ordinal);
		var agentIdByName = (await agentCostRepository.ListCatalogAsync(ct)).ToDictionary(a => a.Name, a => a.Id, StringComparer.Ordinal);

		var now = DateTimeOffset.UtcNow;
		var rows = new List<EngineCostRow>(parsed.Count);
		foreach (var row in parsed)
		{
			if (!engineIdByName.TryGetValue(row.EngineName, out var engineId))
			{
				throw new EngineCostCsvException($"Unknown engine '{row.EngineName}'.");
			}

			string? agentId = null;
			if (row.OverrideAgentName is not null)
			{
				if (!agentIdByName.TryGetValue(row.OverrideAgentName, out agentId))
				{
					throw new EngineCostCsvException($"Unknown agent '{row.OverrideAgentName}' (bis column) for engine '{row.EngineName}'.");
				}
			}

			rows.Add(new EngineCostRow
			{
				Id = Guid.NewGuid(),
				EngineId = engineId,
				AgentId = agentId,
				Costs = row.Costs,
				UpdatedAt = now,
			});
		}

		await repository.ReplaceAllCostRowsAsync(rows, ct);
	}
}
