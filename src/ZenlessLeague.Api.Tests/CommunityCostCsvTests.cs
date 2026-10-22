using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Features.CommunityCosts.Agents;
using ZenlessLeague.Api.Features.CommunityCosts.Catalog;
using ZenlessLeague.Api.Features.CommunityCosts.Engines;
using ZenlessLeague.Api.Infrastructure.Persistence;

namespace ZenlessLeague.Api.Tests;

public class CommunityCostCsvTests
{
	private static AppDbContext CreateContext() =>
		new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

	[Fact]
	public async Task AgentCsv_ExportThenImport_RoundTrips()
	{
		await using var context = CreateContext();
		context.AgentCatalogEntries.Add(new AgentCatalogEntry { Id = "anby", Name = "Anby", Specialty = Specialty.Stun, Rarity = 3, SortOrder = 0 });
		context.AgentCatalogEntries.Add(new AgentCatalogEntry { Id = "nekomata", Name = "Nekomata", Specialty = Specialty.Attack, Rarity = 4, SortOrder = 1 });
		context.AgentCostRows.Add(new AgentCostRow { Id = Guid.NewGuid(), AgentId = "anby", PotentialTier = null, Costs = [10, 10, 10, 10, 10, 10, 10], UpdatedAt = DateTimeOffset.UtcNow });
		context.AgentCostRows.Add(new AgentCostRow { Id = Guid.NewGuid(), AgentId = "nekomata", PotentialTier = 0, Costs = [150, 200, 200, 260, 300, 360, 425], UpdatedAt = DateTimeOffset.UtcNow });
		context.AgentCostRows.Add(new AgentCostRow { Id = Guid.NewGuid(), AgentId = "nekomata", PotentialTier = 1, Costs = [0, 0, 0, 0, 0, 0, 0], UpdatedAt = DateTimeOffset.UtcNow });
		await context.SaveChangesAsync();
		var repository = new AgentCostRepository(context);
		var service = new AgentCostService(repository);

		var csv = await service.ExportCsvAsync();

		Assert.Equal(
			"Agent,M0,M1,M2,M3,M4,M5,M6,P\r\n" +
			"Anby,10,10,10,10,10,10,10,null\r\n" +
			"Nekomata,150,200,200,260,300,360,425,0\r\n" +
			"Nekomata,0,0,0,0,0,0,0,1\r\n",
			csv);

		await service.ImportCsvAsync(csv);

		var rows = await context.AgentCostRows.ToListAsync();
		Assert.Equal(3, rows.Count);
		var anby = Assert.Single(rows, r => r.AgentId == "anby");
		Assert.Null(anby.PotentialTier);
		Assert.Equal([10, 10, 10, 10, 10, 10, 10], anby.Costs);
	}

	[Fact]
	public async Task AgentCsv_Import_UnknownAgentName_Throws()
	{
		await using var context = CreateContext();
		context.AgentCatalogEntries.Add(new AgentCatalogEntry { Id = "anby", Name = "Anby", Specialty = Specialty.Stun, Rarity = 3, SortOrder = 0 });
		await context.SaveChangesAsync();
		var service = new AgentCostService(new AgentCostRepository(context));
		const string csv = "Agent,M0,M1,M2,M3,M4,M5,M6,P\r\nGhost,1,2,3,4,5,6,7,null\r\n";

		await Assert.ThrowsAsync<AgentCostCsvException>(() => service.ImportCsvAsync(csv));
	}

	[Fact]
	public async Task AgentCsv_Import_IsWholesaleReplace_DropsRowsMissingFromCsv()
	{
		await using var context = CreateContext();
		context.AgentCatalogEntries.Add(new AgentCatalogEntry { Id = "anby", Name = "Anby", Specialty = Specialty.Stun, Rarity = 3, SortOrder = 0 });
		context.AgentCostRows.Add(new AgentCostRow { Id = Guid.NewGuid(), AgentId = "anby", PotentialTier = null, Costs = [9, 9, 9, 9, 9, 9, 9], UpdatedAt = DateTimeOffset.UtcNow });
		await context.SaveChangesAsync();
		var service = new AgentCostService(new AgentCostRepository(context));
		const string csv = "Agent,M0,M1,M2,M3,M4,M5,M6,P\r\n";

		await service.ImportCsvAsync(csv);

		Assert.Empty(await context.AgentCostRows.ToListAsync());
	}

	[Fact]
	public async Task EngineCsv_ExportThenImport_RoundTrips()
	{
		await using var context = CreateContext();
		context.EngineCatalogEntries.Add(new EngineCatalogEntry { Id = "e1", Name = "Steel Cushion", Specialty = Specialty.Attack, Rarity = 4, SortOrder = 0 });
		context.AgentCatalogEntries.Add(new AgentCatalogEntry { Id = "nekomata", Name = "Nekomata", Specialty = Specialty.Attack, Rarity = 4, SortOrder = 0 });
		context.EngineCostRows.Add(new EngineCostRow { Id = Guid.NewGuid(), EngineId = "e1", AgentId = null, Costs = [1, 2, 3, 4, 5, 0], UpdatedAt = DateTimeOffset.UtcNow });
		context.EngineCostRows.Add(new EngineCostRow { Id = Guid.NewGuid(), EngineId = "e1", AgentId = "nekomata", Costs = [25, 27, 30, 32, 35, 0], UpdatedAt = DateTimeOffset.UtcNow });
		await context.SaveChangesAsync();
		var service = new EngineCostService(new EngineCostRepository(context), new AgentCostRepository(context));

		var csv = await service.ExportCsvAsync();

		Assert.Equal(
			"Engine,R1,R2,R3,R4,R5,off,bis\r\n" +
			"Steel Cushion,1,2,3,4,5,0,\r\n" +
			"Steel Cushion,25,27,30,32,35,0,Nekomata\r\n",
			csv);

		await service.ImportCsvAsync(csv);

		var rows = await context.EngineCostRows.ToListAsync();
		Assert.Equal(2, rows.Count);
		var overrideRow = Assert.Single(rows, r => r.AgentId == "nekomata");
		Assert.Equal([25, 27, 30, 32, 35, 0], overrideRow.Costs);
	}

	[Fact]
	public async Task EngineCsv_Import_UnknownBisAgentName_Throws()
	{
		await using var context = CreateContext();
		context.EngineCatalogEntries.Add(new EngineCatalogEntry { Id = "e1", Name = "Steel Cushion", Specialty = Specialty.Attack, Rarity = 4, SortOrder = 0 });
		await context.SaveChangesAsync();
		var service = new EngineCostService(new EngineCostRepository(context), new AgentCostRepository(context));
		const string csv = "Engine,R1,R2,R3,R4,R5,off,bis\r\nSteel Cushion,1,2,3,4,5,0,Ghost\r\n";

		await Assert.ThrowsAsync<EngineCostCsvException>(() => service.ImportCsvAsync(csv));
	}
}
