using Google.Apis.Auth.OAuth2;
using ZenlessLeague.Api.Infrastructure.Firebase;
using ZenlessLeague.Api.Infrastructure.Http;

namespace ZenlessLeague.Api.Features.Storage;

public static class StorageFeature
{
	public static IServiceCollection AddStorageFeature(this IServiceCollection services, IConfiguration configuration)
	{
		// Genuinely lazy singleton (wrapped in Lazy<T>, not just an AddSingleton factory) — a plain
		// AddSingleton factory still runs as soon as anything holding a FirebaseStorageClient is
		// constructed (e.g. MatchService, pulled in by every TournamentService, including read-only
		// endpoints), which defeats the "only fails when Storage is actually used" intent. Lazy<T> defers
		// the GoogleCredential.FromJson call to the first real .Value access inside FirebaseStorageClient.
		services.AddSingleton(_ => new Lazy<GoogleCredential>(() =>
		{
#pragma warning disable CS0618
			return GoogleCredential.FromJson(FirebaseCredentials.ResolveJson(configuration))
				.CreateScoped("https://www.googleapis.com/auth/devstorage.read_write");
#pragma warning restore CS0618
		}));
		services.Configure<FirebaseStorageOptions>(configuration.GetSection("Firebase"));
		// Uploads are POSTs (not retried); the list/delete calls around them are.
		services.AddHttpClient<IFirebaseStorageClient, FirebaseStorageClient>()
			.AddThirdPartyResilience(TimeSpan.FromSeconds(20));
		return services;
	}
}
