using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ZenlessLeague.Api.Features.Auth;

namespace ZenlessLeague.Api.Tests;

public class SessionTokenIssuerTests
{
	private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now;
	}

	private static readonly SymmetricSecurityKey Key = SessionTokenIssuer.CreateSigningKey(new string('k', 32));

	private static async Task<TokenValidationResult> ValidateAsync(string token, SecurityKey key, DateTimeOffset now)
	{
		var parameters = SessionTokenIssuer.CreateValidationParameters(key);
		parameters.LifetimeValidator = (notBefore, expires, _, _) => notBefore <= now.UtcDateTime && now.UtcDateTime < expires;
		return await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);
	}

	[Fact]
	public async Task Issue_ProducesTokenThatValidatesWithSubjectUid()
	{
		var time = new FixedTimeProvider(DateTimeOffset.UtcNow);
		var session = new SessionTokenIssuer(Key, time).Issue("p1");

		var result = await ValidateAsync(session.Token, Key, time.GetUtcNow());

		Assert.True(result.IsValid);
		Assert.Equal("p1", result.ClaimsIdentity.FindFirst("sub")?.Value);
		Assert.Equal(time.GetUtcNow() + SessionTokenIssuer.Lifetime, session.ExpiresAt);
	}

	[Fact]
	public async Task Issue_TokenIsRejectedAfterItExpires()
	{
		var time = new FixedTimeProvider(DateTimeOffset.UtcNow);
		var session = new SessionTokenIssuer(Key, time).Issue("p1");

		var result = await ValidateAsync(session.Token, Key, time.GetUtcNow() + SessionTokenIssuer.Lifetime + TimeSpan.FromMinutes(1));

		Assert.False(result.IsValid);
	}

	[Fact]
	public async Task Issue_TokenIsRejectedUnderADifferentKey()
	{
		var time = new FixedTimeProvider(DateTimeOffset.UtcNow);
		var session = new SessionTokenIssuer(Key, time).Issue("p1");

		var result = await ValidateAsync(session.Token, SessionTokenIssuer.CreateSigningKey(new string('x', 32)), time.GetUtcNow());

		Assert.False(result.IsValid);
	}

	[Fact]
	public void CreateSigningKey_ShortSecret_Throws()
	{
		Assert.Throws<InvalidOperationException>(() => SessionTokenIssuer.CreateSigningKey("too-short"));
	}

	[Fact]
	public void IsSessionToken_OnlyMatchesOurIssuer()
	{
		var ours = new SessionTokenIssuer(Key, TimeProvider.System).Issue("p1").Token;
		var firebaseLike = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
		{
			Issuer = "https://securetoken.google.com/zzz-league",
			SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256),
		});

		Assert.True(SessionTokenIssuer.IsSessionToken(ours));
		Assert.False(SessionTokenIssuer.IsSessionToken(firebaseLike));
		Assert.False(SessionTokenIssuer.IsSessionToken("not-a-jwt"));
	}
}
