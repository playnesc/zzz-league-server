namespace ZenlessLeague.Api.Features.Storage;

public class FirebaseStorageOptions
{
	// e.g. "zzz-league.appspot.com" or "zzz-league.firebasestorage.app" depending on when the
	// Firebase project's default bucket was provisioned — not guessable, must be configured.
	public string? StorageBucket { get; set; }
}
