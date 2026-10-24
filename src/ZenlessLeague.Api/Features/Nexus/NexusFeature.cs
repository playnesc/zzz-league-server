namespace ZenlessLeague.Api.Features.Nexus;

public static class NexusFeature
{
	public static IServiceCollection AddNexusFeature(this IServiceCollection services, IConfiguration configuration)
	{
		services.Configure<NexusOptions>(configuration.GetSection("Nexus"));
		services.AddHttpClient<INexusClient, NexusClient>(client =>
		{
			client.BaseAddress = new Uri("https://nexus-shiyu.com/");
			client.Timeout = TimeSpan.FromSeconds(10);
		});
		return services;
	}
}
