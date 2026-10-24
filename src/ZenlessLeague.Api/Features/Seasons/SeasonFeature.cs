namespace ZenlessLeague.Api.Features.Seasons;

public static class SeasonFeature
{
	public static IServiceCollection AddSeasonFeature(this IServiceCollection services)
	{
		services.AddScoped<IArchiveRepository, ArchiveRepository>();
		services.AddScoped<ISeasonTimerRepository, SeasonTimerRepository>();
		services.AddScoped<ISeasonService, SeasonService>();
		return services;
	}
}
