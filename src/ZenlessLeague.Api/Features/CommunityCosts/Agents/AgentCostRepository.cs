using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Features.CommunityCosts.Catalog;
using ZenlessLeague.Api.Infrastructure.Persistence;

namespace ZenlessLeague.Api.Features.CommunityCosts.Agents;

public interface IAgentCostRepository
{
	Task<List<AgentCatalogEntry>> ListCatalogAsync(CancellationToken ct = default);
	Task<AgentCatalogEntry?> GetCatalogEntryAsync(string id, CancellationToken ct = default);

	// Every AgentCostRow (all agents, all tiers) — v1 callers filter down to the base/no-tier
	// row per agent themselves; CSV export (added later) needs every tier.
	Task<List<AgentCostRow>> ListAllCostRowsAsync(CancellationToken ct = default);

	// Returns a tracked entity — callers may mutate it in place and call SaveChangesAsync.
	Task<AgentCostRow?> GetCostRowAsync(string agentId, int? potentialTier, CancellationToken ct = default);

	Task<AgentCostRow> CreateCostRowAsync(AgentCostRow row, CancellationToken ct = default);

	// Wholesale delete-and-replace for CSV import — the CSV is a full snapshot each time, so a
	// row's absence means "retract this" rather than "leave it alone" (plan §4/§7). Wrapped in
	// one transaction so a mid-import failure can't leave the table half-replaced.
	Task ReplaceAllCostRowsAsync(List<AgentCostRow> rows, CancellationToken ct = default);

	Task SaveChangesAsync(CancellationToken ct = default);
}

public class AgentCostRepository(AppDbContext context) : IAgentCostRepository
{
	public Task<List<AgentCatalogEntry>> ListCatalogAsync(CancellationToken ct = default) =>
		context.AgentCatalogEntries.OrderBy(a => a.SortOrder).ToListAsync(ct);

	public Task<AgentCatalogEntry?> GetCatalogEntryAsync(string id, CancellationToken ct = default) =>
		context.AgentCatalogEntries.FirstOrDefaultAsync(a => a.Id == id, ct);

	public Task<List<AgentCostRow>> ListAllCostRowsAsync(CancellationToken ct = default) =>
		context.AgentCostRows.ToListAsync(ct);

	public Task<AgentCostRow?> GetCostRowAsync(string agentId, int? potentialTier, CancellationToken ct = default) =>
		context.AgentCostRows.FirstOrDefaultAsync(r => r.AgentId == agentId && r.PotentialTier == potentialTier, ct);

	public async Task<AgentCostRow> CreateCostRowAsync(AgentCostRow row, CancellationToken ct = default)
	{
		context.AgentCostRows.Add(row);
		await context.SaveChangesAsync(ct);
		return row;
	}

	public async Task ReplaceAllCostRowsAsync(List<AgentCostRow> rows, CancellationToken ct = default)
	{
		// A single SaveChangesAsync call is already one atomic transaction on a relational
		// provider — no explicit BeginTransactionAsync needed (which the EF Core InMemory
		// provider used by tests doesn't support anyway).
		context.AgentCostRows.RemoveRange(context.AgentCostRows);
		context.AgentCostRows.AddRange(rows);
		await context.SaveChangesAsync(ct);
	}

	public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
}
