using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Features.CommunityCosts.Catalog;
using ZenlessLeague.Api.Infrastructure.Persistence;

namespace ZenlessLeague.Api.Features.CommunityCosts.Engines;

public interface IEngineCostRepository
{
	Task<List<EngineCatalogEntry>> ListCatalogAsync(CancellationToken ct = default);
	Task<EngineCatalogEntry?> GetCatalogEntryAsync(string id, CancellationToken ct = default);
	Task<List<EngineCostRow>> ListAllCostRowsAsync(CancellationToken ct = default);

	// Returns a tracked entity — callers may mutate it in place and call SaveChangesAsync.
	Task<EngineCostRow?> GetCostRowAsync(string engineId, string? agentId, CancellationToken ct = default);

	Task<EngineCostRow> CreateCostRowAsync(EngineCostRow row, CancellationToken ct = default);

	// Wholesale delete-and-replace for CSV import — see IAgentCostRepository.ReplaceAllCostRowsAsync.
	Task ReplaceAllCostRowsAsync(List<EngineCostRow> rows, CancellationToken ct = default);

	Task SaveChangesAsync(CancellationToken ct = default);
}

public class EngineCostRepository(AppDbContext context) : IEngineCostRepository
{
	public Task<List<EngineCatalogEntry>> ListCatalogAsync(CancellationToken ct = default) =>
		context.EngineCatalogEntries.OrderBy(e => e.SortOrder).ToListAsync(ct);

	public Task<EngineCatalogEntry?> GetCatalogEntryAsync(string id, CancellationToken ct = default) =>
		context.EngineCatalogEntries.FirstOrDefaultAsync(e => e.Id == id, ct);

	public Task<List<EngineCostRow>> ListAllCostRowsAsync(CancellationToken ct = default) =>
		context.EngineCostRows.ToListAsync(ct);

	public Task<EngineCostRow?> GetCostRowAsync(string engineId, string? agentId, CancellationToken ct = default) =>
		context.EngineCostRows.FirstOrDefaultAsync(r => r.EngineId == engineId && r.AgentId == agentId, ct);

	public async Task<EngineCostRow> CreateCostRowAsync(EngineCostRow row, CancellationToken ct = default)
	{
		context.EngineCostRows.Add(row);
		await context.SaveChangesAsync(ct);
		return row;
	}

	public async Task ReplaceAllCostRowsAsync(List<EngineCostRow> rows, CancellationToken ct = default)
	{
		// See AgentCostRepository.ReplaceAllCostRowsAsync — one SaveChangesAsync call is already
		// atomic, no explicit transaction needed.
		context.EngineCostRows.RemoveRange(context.EngineCostRows);
		context.EngineCostRows.AddRange(rows);
		await context.SaveChangesAsync(ct);
	}

	public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
}
