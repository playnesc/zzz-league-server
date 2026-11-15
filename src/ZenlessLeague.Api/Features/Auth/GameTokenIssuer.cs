using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ZenlessLeague.Api.Features.Auth;

public interface IGameTokenIssuer
{
	SessionToken Issue(string uid);
}

// Short-lived tokens for the Anime Surfers game (surfers.playnesc.com), which can't read the site's
// httpOnly session cookie. The SvelteKit server trades its session for one of these and hands it to
// the game. They're signed with the session key but carry their own audience, so they only work on
// endpoints that ask for the Game policy: the default scheme selector sends them (same issuer) to the
// Session scheme, which rejects the audience, so a leaked game token can't touch the rest of the API
// or mint itself a new one.
public class GameTokenIssuer(SymmetricSecurityKey signingKey, TimeProvider timeProvider) : IGameTokenIssuer
{
	public const string Scheme = "Game";
	public const string Policy = "Game";
	public const string Audience = "nesc-surfers";
	public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

	public static TokenValidationParameters CreateValidationParameters(SecurityKey signingKey)
	{
		var parameters = SessionTokenIssuer.CreateValidationParameters(signingKey);
		parameters.ValidAudience = Audience;
		return parameters;
	}

	public SessionToken Issue(string uid)
	{
		var now = timeProvider.GetUtcNow();
		var expiresAt = now.Add(Lifetime);
		var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
		{
			Issuer = SessionTokenIssuer.Issuer,
			Audience = Audience,
			Subject = new ClaimsIdentity([new Claim("sub", uid)]),
			IssuedAt = now.UtcDateTime,
			NotBefore = now.UtcDateTime,
			Expires = expiresAt.UtcDateTime,
			SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256),
		});

		return new SessionToken(token, expiresAt);
	}
}
