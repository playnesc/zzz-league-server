namespace ZenlessLeague.Api.Features.Surfers;

public static class SurfersFeature
{
	public static IServiceCollection AddSurfersFeature(this IServiceCollection services, IConfiguration configuration)
	{
		services.Configure<SurfersOptions>(configuration.GetSection("Surfers"));
		services.AddScoped<ISurfersScoreRepository, SurfersScoreRepository>();
		services.AddScoped<ISurfersService, SurfersService>();
		return services;
	}
}
