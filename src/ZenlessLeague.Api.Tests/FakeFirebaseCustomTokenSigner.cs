using ZenlessLeague.Api.Infrastructure.Firebase;


namespace ZenlessLeague.Api.Tests;

// Deterministic stand-in for real RS256 signing — lets tests assert which uid a token was minted
// for without needing a real Firebase service account key.
public class FakeFirebaseCustomTokenSigner : IFirebaseCustomTokenSigner
{
	public string CreateCustomToken(string uid) => $"token-for:{uid}";
}
