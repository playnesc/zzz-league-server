using ZenlessLeague.Api.Features.Tournaments.Bracket;
using ZenlessLeague.Api.Features.Tournaments.Matches;
using ZenlessLeague.Api.Features.Tournaments.Realtime;

namespace ZenlessLeague.Api.Features.Tournaments;

// Tournaments and everything under them: registrations, matches, the bracket, and the
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

		services.AddScoped<ITournamentParticipantRepository, TournamentParticipantRepository>();
		services.AddScoped<IBracketService, BracketService>();
		services.AddScoped<ITournamentMatchRepository, TournamentMatchRepository>();
		services.AddScoped<NexusResultResolver>();
		services.AddScoped<IMatchService, MatchService>();

		services.AddSignalR();
		services.AddScoped<ITournamentNotifier, TournamentNotifier>();
		return services;
	}
}
