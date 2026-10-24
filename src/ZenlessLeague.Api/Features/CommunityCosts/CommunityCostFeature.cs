using ZenlessLeague.Api.Features.CommunityCosts.Agents;
using ZenlessLeague.Api.Features.CommunityCosts.Contributions;
using ZenlessLeague.Api.Features.CommunityCosts.Engines;

namespace ZenlessLeague.Api.Features.CommunityCosts;

public static class CommunityCostFeature
{
	public static IServiceCollection AddCommunityCostFeature(this IServiceCollection services)
	{
		services.AddScoped<IAgentCostRepository, AgentCostRepository>();
		services.AddScoped<IAgentCostService, AgentCostService>();
		services.AddScoped<IEngineCostRepository, EngineCostRepository>();
		services.AddScoped<IEngineCostService, EngineCostService>();
		services.AddScoped<IContributionRepository, ContributionRepository>();
		services.AddScoped<IContributionService, ContributionService>();
		return services;
	}
}
