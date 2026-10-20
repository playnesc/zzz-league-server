using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ZenlessLeague.Api.Features.Auth;

public record SessionToken(string Token, DateTimeOffset ExpiresAt);

public interface ISessionTokenIssuer
{
	SessionToken Issue(string uid);
}

// Self-issued session JWTs for the SvelteKit server. The browser still authenticates with Firebase
// ID tokens; it trades one for a session token (POST /api/auth/session), the frontend server keeps
// that in an httpOnly cookie on its own origin and sends it back here as a Bearer token when it
// renders pages. That lets SSR know who's logged in without the browser ever sending cookies to
// this API (so no CSRF surface here). Only "sub" is carried — role etc. are always read from the
// Players table, same as for Firebase tokens.
public class SessionTokenIssuer(SymmetricSecurityKey signingKey, TimeProvider timeProvider) : ISessionTokenIssuer
{
	public const string Scheme = "Session";
	public const string Issuer = "nesc-api";
	public const string Audience = "nesc-web";
	public static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

	public static SymmetricSecurityKey CreateSigningKey(string secret)
	{
		var bytes = Encoding.UTF8.GetBytes(secret);
		if (bytes.Length < 32)
		{
			throw new InvalidOperationException("Auth:SessionSigningKey must be at least 32 bytes.");
		}

		return new SymmetricSecurityKey(bytes);
	}

	public static TokenValidationParameters CreateValidationParameters(SecurityKey signingKey) => new()
	{
		ValidIssuer = Issuer,
		ValidAudience = Audience,
		IssuerSigningKey = signingKey,
		ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
		ValidateIssuer = true,
		ValidateAudience = true,
		ValidateLifetime = true,
		ValidateIssuerSigningKey = true,
	};

	// Unvalidated peek, only for routing a Bearer token to the right scheme — the chosen scheme
	// still fully validates it.
	public static bool IsSessionToken(string token)
	{
		var handler = new JsonWebTokenHandler();
		return handler.CanReadToken(token) && handler.ReadJsonWebToken(token).Issuer == Issuer;
	}

	public SessionToken Issue(string uid)
	{
		var now = timeProvider.GetUtcNow();
		var expiresAt = now.Add(Lifetime);
		var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
		{
			Issuer = Issuer,
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
