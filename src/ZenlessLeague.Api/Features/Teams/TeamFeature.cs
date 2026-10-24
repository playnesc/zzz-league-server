namespace ZenlessLeague.Api.Features.Teams;

public static class TeamFeature
{
	public static IServiceCollection AddTeamFeature(this IServiceCollection services)
	{
		services.AddScoped<ITeamRepository, TeamRepository>();
		services.AddScoped<ITeamService, TeamService>();
		return services;
	}
}
