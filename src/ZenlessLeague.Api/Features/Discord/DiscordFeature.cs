using ZenlessLeague.Api.Infrastructure.Http;

namespace ZenlessLeague.Api.Features.Discord;

public static class DiscordFeature
{
	public static IServiceCollection AddDiscordFeature(this IServiceCollection services, IConfiguration configuration)
	{
		services.Configure<DiscordOptions>(configuration.GetSection("Discord"));
		// 429s are left to DiscordClient's own Retry-After loop (which also covers POSTs — a 429 means the
		// request wasn't processed), so the pipeline doesn't retry them a second time on top.
		services.AddHttpClient<IDiscordClient, DiscordClient>(client =>
			client.BaseAddress = new Uri("https://discord.com/api/v10/"))
			.AddThirdPartyResilience(TimeSpan.FromSeconds(10), retryOnTooManyRequests: false);

		// Separate client/base address from IDiscordClient above — the OAuth token endpoint is
		// unversioned (discord.com/api/oauth2/token), unlike the bot API's /api/v10/... surface. The token
		// exchange is a POST (single-use code) and so never retried; the /users/@me GET is.
		services.AddHttpClient<IDiscordOAuthClient, DiscordOAuthClient>(client =>
			client.BaseAddress = new Uri("https://discord.com/api/"))
			.AddThirdPartyResilience(TimeSpan.FromSeconds(10));

		services.AddScoped<IDiscordRoleAssignmentService, DiscordRoleAssignmentService>();
		services.AddScoped<ITournamentDiscordResourcesService, TournamentDiscordResourcesService>();
		return services;
	}
}
