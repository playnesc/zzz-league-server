namespace ZenlessLeague.Api.Features.History;

public static class HistoryFeature
{
	public static IServiceCollection AddHistoryFeature(this IServiceCollection services)
	{
		services.AddScoped<IHistoryRepository, HistoryRepository>();
		services.AddScoped<IHistoryService, HistoryService>();
		return services;
	}
}
