namespace ZenlessLeague.Api.Features.CommunityCosts.Agents;

public interface IAgentCostService
{
	Task<List<AgentCostDto>> ListAsync(CancellationToken ct = default);
	Task<string> ExportCsvAsync(CancellationToken ct = default);
	Task ImportCsvAsync(string csv, CancellationToken ct = default);
}

public class AgentCostService(IAgentCostRepository repository) : IAgentCostService
{
	public async Task<List<AgentCostDto>> ListAsync(CancellationToken ct = default)
	{
		var catalog = await repository.ListCatalogAsync(ct);
		var rows = await repository.ListAllCostRowsAsync(ct);

		// Per agent: a no-tier row (PotentialTier null) if one exists, else the tier-0 base
		// row. Agents seeded without any cost row yet (shouldn't happen post-seed, but kept
		// defensive) fall back to an all-zero cost array rather than being omitted.
		var baseRowsByAgent = rows
			.Where(r => r.PotentialTier is null or 0)
			.GroupBy(r => r.AgentId)
			.ToDictionary(g => g.Key, g => g.OrderBy(r => r.PotentialTier is null ? -1 : r.PotentialTier).First());

		return catalog
			.Select(agent => new AgentCostDto(
				agent.Id,
				agent.Name,
				agent.Specialty,
				agent.Rarity,
				baseRowsByAgent.TryGetValue(agent.Id, out var row) ? row.Costs : new int[7]))
			.ToList();
	}

	public async Task<string> ExportCsvAsync(CancellationToken ct = default)
	{
		var catalog = await repository.ListCatalogAsync(ct);
		var rows = await repository.ListAllCostRowsAsync(ct);
		return AgentCostCsv.Export(catalog, rows);
	}

	public async Task ImportCsvAsync(string csv, CancellationToken ct = default)
	{
		var parsed = AgentCostCsv.Parse(csv);
		var catalog = await repository.ListCatalogAsync(ct);
		var idByName = catalog.ToDictionary(a => a.Name, a => a.Id, StringComparer.Ordinal);

		var now = DateTimeOffset.UtcNow;
		var rows = new List<AgentCostRow>(parsed.Count);
		foreach (var row in parsed)
		{
			if (!idByName.TryGetValue(row.AgentName, out var agentId))
			{
				throw new AgentCostCsvException($"Unknown agent '{row.AgentName}'.");
			}

			rows.Add(new AgentCostRow
			{
				Id = Guid.NewGuid(),
				AgentId = agentId,
				PotentialTier = row.PotentialTier,
				Costs = row.Costs,
				UpdatedAt = now,
			});
		}

		await repository.ReplaceAllCostRowsAsync(rows, ct);
	}
}
