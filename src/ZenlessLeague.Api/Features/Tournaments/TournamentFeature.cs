using ZenlessLeague.Api.Features.Tournaments.Challonge;
using ZenlessLeague.Api.Features.Tournaments.Matches;
using ZenlessLeague.Api.Features.Tournaments.Realtime;
using ZenlessLeague.Api.Infrastructure.Http;

namespace ZenlessLeague.Api.Features.Tournaments;

// Tournaments and everything under them: registrations, matches, the Challonge bracket, and the
// SignalR hub that pushes their changes.
public static class TournamentFeature
{
	public static IServiceCollection AddTournamentFeature(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddScoped<ITournamentRepository, TournamentRepository>();
		services.AddScoped<ITournamentService, TournamentService>();
		services.AddScoped<ITournamentLifecycleService, TournamentLifecycleService>();
		services.AddScoped<ITournamentRegistrationRepository, TournamentRegistrationRepository>();
		services.AddScoped<ITournamentRegistrationService, TournamentRegistrationService>();

		services.Configure<ChallongeOptions>(configuration.GetSection("Challonge"));
		// Challonge is slow on bulk operations (bulk_add/randomize for a full bracket), hence the longer
		// per-attempt timeout.
		services.AddHttpClient<IChallongeClient, ChallongeClient>(client =>
			client.BaseAddress = new Uri("https://api.challonge.com/v2.1/"))
			.AddThirdPartyResilience(TimeSpan.FromSeconds(20), isNonIdempotent: ChallongeClient.IsNonIdempotent);

		services.AddScoped<ITournamentChallongeParticipantRepository, TournamentChallongeParticipantRepository>();
		services.AddScoped<ITournamentMatchRepository, TournamentMatchRepository>();
		services.AddScoped<ChallongeMatchSync>();
		services.AddScoped<NexusResultResolver>();
		services.AddScoped<IMatchService, MatchService>();

		services.AddSignalR();
		services.AddScoped<ITournamentNotifier, TournamentNotifier>();
		return services;
	}
}
