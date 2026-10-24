using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Infrastructure.Authorization;
using ZenlessLeague.Api.Infrastructure.Firebase;

namespace ZenlessLeague.Api.Features.Auth;

public static class AuthFeature
{
	public static IServiceCollection AddAuthFeature(this IServiceCollection services, IConfiguration configuration)
	{
		// Provider-neutral by design: Authority-based JWT validation via OIDC discovery works
		// unchanged for any standards-compliant IdP (Firebase today; Keycloak, Auth0, etc. later) —
		// swapping providers is a config change here, not a code change. Business logic never assumes
		// "Firebase" — it only reads the standard "sub" claim (see AuthenticatedUserExtensions) and
		// looks up role from our own Players table, not from provider-specific claims.
		var authAuthority = configuration["Auth:Authority"]
			?? throw new InvalidOperationException("Auth:Authority is not configured.");
		var authAudience = configuration["Auth:Audience"]
			?? throw new InvalidOperationException("Auth:Audience is not configured.");
		var sessionSigningKey = SessionTokenIssuer.CreateSigningKey(configuration["Auth:SessionSigningKey"]
			?? throw new InvalidOperationException("Auth:SessionSigningKey is not configured."));

		// Two kinds of Bearer token: Firebase ID tokens from the browser, and our own session tokens from
		// the SvelteKit server (see SessionTokenIssuer). The default scheme just routes each request to the
		// right one by the token's issuer, so every endpoint — including ones where auth is optional —
		// accepts both without listing schemes.
		const string bearerSelectorScheme = "BearerSelector";
		services.AddAuthentication(bearerSelectorScheme)
			.AddPolicyScheme(bearerSelectorScheme, null, options =>
			{
				options.ForwardDefaultSelector = context =>
				{
					var header = context.Request.Headers.Authorization.ToString();
					return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
						&& SessionTokenIssuer.IsSessionToken(header["Bearer ".Length..].Trim())
						? SessionTokenIssuer.Scheme
						: JwtBearerDefaults.AuthenticationScheme;
				};
			})
			.AddJwtBearer(SessionTokenIssuer.Scheme, options =>
			{
				options.TokenValidationParameters = SessionTokenIssuer.CreateValidationParameters(sessionSigningKey);
			})
			.AddJwtBearer(options =>
			{
				options.Authority = authAuthority;
				options.TokenValidationParameters = new TokenValidationParameters
				{
					ValidIssuer = authAuthority,
					ValidAudience = authAudience,
					ValidateIssuer = true,
					ValidateAudience = true,
					ValidateLifetime = true,
				};
				options.Events = new JwtBearerEvents
				{
					// SignalR/WebSocket clients can't set an Authorization header on the handshake —
					// accept the token via ?access_token= for hub connections instead (see Phase 7).
					OnMessageReceived = context =>
					{
						var accessToken = context.Request.Query["access_token"];
						if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
						{
							context.Token = accessToken;
						}

						return Task.CompletedTask;
					},
				};
			});

		services.AddAuthorizationBuilder()
			.AddPolicy("Moderator", policy => policy.Requirements.Add(new RoleRequirement(PlayerRole.Moderator)))
			.AddPolicy("Admin", policy => policy.Requirements.Add(new RoleRequirement(PlayerRole.Admin)));
		services.AddScoped<IAuthorizationHandler, RoleAuthorizationHandler>();

		services.AddSingleton<ISessionTokenIssuer>(new SessionTokenIssuer(sessionSigningKey, TimeProvider.System));
		services.AddSingleton<IFirebaseCustomTokenSigner, FirebaseCustomTokenSigner>();
		services.AddScoped<IAuthService, AuthService>();
		return services;
	}
}
