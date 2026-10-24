namespace ZenlessLeague.Api.Features.Players;

public static class PlayerFeature
{
	public static IServiceCollection AddPlayerFeature(this IServiceCollection services)
	{
		services.AddScoped<IPlayerRepository, PlayerRepository>();
		services.AddScoped<IPlayerService, PlayerService>();
		return services;
	}
}
