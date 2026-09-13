namespace ZenlessLeague.Api.Data;

// Firebase service-account credentials can come from either a file on disk (local dev --
// Firebase:CredentialsPath, a plain relative path to a gitignored file) or the raw JSON directly
// via an env var (production -- Firebase:CredentialsJson, so Cloud Run can inject it as a plain
// env var instead of needing a mounted secret volume). Shared by Program.cs (GoogleCredential, for
// Storage) and FirebaseCustomTokenSigner (which only needs client_email/private_key out of it).
internal static class FirebaseCredentials
{
	public static string ResolveJson(IConfiguration configuration)
	{
		var json = configuration["Firebase:CredentialsJson"];
		if (!string.IsNullOrEmpty(json))
		{
			return json;
		}

		var path = configuration["Firebase:CredentialsPath"]
			?? throw new InvalidOperationException("Neither Firebase:CredentialsJson nor Firebase:CredentialsPath is configured.");
		return File.ReadAllText(path);
	}
}
