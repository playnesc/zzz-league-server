using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ZenlessLeague.Api.Features.Auth;

namespace ZenlessLeague.Api.Tests;

public class GameTokenIssuerTests
{
	private static readonly SymmetricSecurityKey Key = SessionTokenIssuer.CreateSigningKey(new string('k', 32));

	private static Task<TokenValidationResult> ValidateAsync(string token, TokenValidationParameters parameters) =>
		new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);

	[Fact]
	public async Task Issue_ProducesShortLivedTokenThatValidatesWithSubjectUid()
	{
		var before = DateTimeOffset.UtcNow;
		var game = new GameTokenIssuer(Key, TimeProvider.System).Issue("p1");

		var result = await ValidateAsync(game.Token, GameTokenIssuer.CreateValidationParameters(Key));

		Assert.True(result.IsValid);
		Assert.Equal("p1", result.ClaimsIdentity.FindFirst("sub")?.Value);
		Assert.InRange(game.ExpiresAt, before + GameTokenIssuer.Lifetime, DateTimeOffset.UtcNow + GameTokenIssuer.Lifetime);
	}

	// The two token kinds share a key and issuer; only the audience keeps them apart.
	[Fact]
	public async Task GameAndSessionTokens_AreNotAcceptedInEachOthersPlace()
	{
		var game = new GameTokenIssuer(Key, TimeProvider.System).Issue("p1").Token;
		var session = new SessionTokenIssuer(Key, TimeProvider.System).Issue("p1").Token;

		Assert.False((await ValidateAsync(game, SessionTokenIssuer.CreateValidationParameters(Key))).IsValid);
		Assert.False((await ValidateAsync(session, GameTokenIssuer.CreateValidationParameters(Key))).IsValid);
	}
}
