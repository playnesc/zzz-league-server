using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Data.Authorization;
using ZenlessLeague.Api.Features.Auth;
using ZenlessLeague.Api.Features.CommunityCosts.Agents;
using ZenlessLeague.Api.Features.CommunityCosts.Contributions;
using ZenlessLeague.Api.Features.CommunityCosts.Engines;
using ZenlessLeague.Api.Features.Discord;
using ZenlessLeague.Api.Features.History;
using ZenlessLeague.Api.Features.Nexus;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Seasons;
using ZenlessLeague.Api.Features.Storage;
using ZenlessLeague.Api.Features.Teams;
using ZenlessLeague.Api.Features.Tournaments;
using ZenlessLeague.Api.Features.Tournaments.Challonge;
using ZenlessLeague.Api.Features.Tournaments.Matches;
using ZenlessLeague.Api.Features.Tournaments.Realtime;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
// AddProblemDetails() is required alongside AddExceptionHandler<T>() — UseExceptionHandler()
// validates at startup that *some* fallback strategy is configured, even though ApiExceptionHandler
// always handles the exception itself and ProblemDetails's own formatting is never reached.
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddDbContext<AppDbContext>(options =>
	options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// Snake-case string enums (e.g. TournamentState.RegistrationClosed -> "registration_closed")
// so the frontend's existing string-literal state predicates (tournamentState.ts, ported
// verbatim from functions/utils/tournamentState.js) work unchanged against this API.
builder.Services.ConfigureHttpJsonOptions(options =>
{
	options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
});

// Config-driven (Cors:AllowedOrigins in appsettings.json) so the deployed frontend's real origin
// doesn't require a code change — the SvelteKit frontend calls this API directly with a Bearer
// token (not cookies), so no AllowCredentials() is needed.
var corsAllowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
	?? throw new InvalidOperationException("Cors:AllowedOrigins is not configured.");

builder.Services.AddCors(options =>
{
	options.AddPolicy("Frontend", policy => policy
		.WithOrigins(corsAllowedOrigins)
		.AllowAnyHeader()
		.AllowAnyMethod());
});

// Provider-neutral by design: Authority-based JWT validation via OIDC discovery works
// unchanged for any standards-compliant IdP (Firebase today; Keycloak, Auth0, etc. later) —
// swapping providers is a config change here, not a code change. Business logic never assumes
// "Firebase" — it only reads the standard "sub" claim (see AuthenticatedUserExtensions) and
// looks up role from our own Players table, not from provider-specific claims.
var authAuthority = builder.Configuration["Auth:Authority"]
	?? throw new InvalidOperationException("Auth:Authority is not configured.");
var authAudience = builder.Configuration["Auth:Audience"]
	?? throw new InvalidOperationException("Auth:Audience is not configured.");
var sessionSigningKey = SessionTokenIssuer.CreateSigningKey(builder.Configuration["Auth:SessionSigningKey"]
	?? throw new InvalidOperationException("Auth:SessionSigningKey is not configured."));

// Two kinds of Bearer token: Firebase ID tokens from the browser, and our own session tokens from
// the SvelteKit server (see SessionTokenIssuer). The default scheme just routes each request to the
// right one by the token's issuer, so every endpoint — including ones where auth is optional —
// accepts both without listing schemes.
const string bearerSelectorScheme = "BearerSelector";
builder.Services.AddAuthentication(bearerSelectorScheme)
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

builder.Services.AddAuthorizationBuilder()
	.AddPolicy("Moderator", policy => policy.Requirements.Add(new RoleRequirement(PlayerRole.Moderator)))
	.AddPolicy("Admin", policy => policy.Requirements.Add(new RoleRequirement(PlayerRole.Admin)));

builder.Services.AddScoped<IAuthorizationHandler, RoleAuthorizationHandler>();
builder.Services.AddSingleton<ISessionTokenIssuer>(new SessionTokenIssuer(sessionSigningKey, TimeProvider.System));

builder.Services.AddScoped<IPlayerRepository, PlayerRepository>();
builder.Services.AddScoped<IPlayerService, PlayerService>();

builder.Services.AddScoped<ITeamRepository, TeamRepository>();
builder.Services.AddScoped<ITeamService, TeamService>();

builder.Services.AddScoped<IAgentCostRepository, AgentCostRepository>();
builder.Services.AddScoped<IAgentCostService, AgentCostService>();
builder.Services.AddScoped<IEngineCostRepository, EngineCostRepository>();
builder.Services.AddScoped<IEngineCostService, EngineCostService>();
builder.Services.AddScoped<IContributionRepository, ContributionRepository>();
builder.Services.AddScoped<IContributionService, ContributionService>();

builder.Services.AddScoped<ITournamentRepository, TournamentRepository>();
builder.Services.AddScoped<ITournamentService, TournamentService>();
builder.Services.AddScoped<ITournamentLifecycleService, TournamentLifecycleService>();
builder.Services.AddScoped<ITournamentRegistrationRepository, TournamentRegistrationRepository>();
builder.Services.AddScoped<ITournamentRegistrationService, TournamentRegistrationService>();

builder.Services.AddScoped<IHistoryRepository, HistoryRepository>();
builder.Services.AddScoped<IHistoryService, HistoryService>();

builder.Services.Configure<ChallongeOptions>(builder.Configuration.GetSection("Challonge"));
builder.Services.AddHttpClient<IChallongeClient, ChallongeClient>(client =>
	client.BaseAddress = new Uri("https://api.challonge.com/v2.1/"));

builder.Services.Configure<NexusOptions>(builder.Configuration.GetSection("Nexus"));
builder.Services.AddHttpClient<INexusClient, NexusClient>(client =>
{
	client.BaseAddress = new Uri("https://nexus-shiyu.com/");
	client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddScoped<ITournamentChallongeParticipantRepository, TournamentChallongeParticipantRepository>();
builder.Services.AddScoped<ITournamentMatchRepository, TournamentMatchRepository>();
builder.Services.AddScoped<IMatchService, MatchService>();

builder.Services.AddSignalR();
builder.Services.AddScoped<ITournamentNotifier, TournamentNotifier>();

builder.Services.Configure<DiscordOptions>(builder.Configuration.GetSection("Discord"));
builder.Services.AddHttpClient<IDiscordClient, DiscordClient>(client =>
	client.BaseAddress = new Uri("https://discord.com/api/v10/"));

// Separate client/base address from IDiscordClient above — the OAuth token endpoint is
// unversioned (discord.com/api/oauth2/token), unlike the bot API's /api/v10/... surface.
builder.Services.AddHttpClient<IDiscordOAuthClient, DiscordOAuthClient>(client =>
	client.BaseAddress = new Uri("https://discord.com/api/"));

builder.Services.AddScoped<IDiscordRoleAssignmentService, DiscordRoleAssignmentService>();
builder.Services.AddScoped<ITournamentDiscordResourcesService, TournamentDiscordResourcesService>();

builder.Services.AddSingleton<IFirebaseCustomTokenSigner, FirebaseCustomTokenSigner>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Genuinely lazy singleton (wrapped in Lazy<T>, not just an AddSingleton factory) — a plain
// AddSingleton factory still runs as soon as anything holding a FirebaseStorageClient is
// constructed (e.g. MatchService, pulled in by every TournamentService, including read-only
// endpoints), which defeats the "only fails when Storage is actually used" intent. Lazy<T> defers
// the GoogleCredential.FromJson call to the first real .Value access inside FirebaseStorageClient.
builder.Services.AddSingleton(_ => new Lazy<GoogleCredential>(() =>
{
#pragma warning disable CS0618
	return GoogleCredential.FromJson(FirebaseCredentials.ResolveJson(builder.Configuration))
		.CreateScoped("https://www.googleapis.com/auth/devstorage.read_write");
#pragma warning restore CS0618
}));
builder.Services.Configure<FirebaseStorageOptions>(builder.Configuration.GetSection("Firebase"));
builder.Services.AddHttpClient<IFirebaseStorageClient, FirebaseStorageClient>();

builder.Services.AddScoped<IArchiveRepository, ArchiveRepository>();
builder.Services.AddScoped<ISeasonTimerRepository, SeasonTimerRepository>();
builder.Services.AddScoped<ISeasonService, SeasonService>();

var app = builder.Build();

// Apply pending EF migrations before serving requests, so a deploy that ships a migration can't
// run new code against an old schema. Single API instance, so no cross-instance race; if a
// migration fails the app doesn't start (visible in Coolify's deploy/app logs) instead of serving
// requests against a half-migrated database.
using (var scope = app.Services.CreateScope())
{
	var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
	var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
	if (pending.Count > 0)
	{
		app.Logger.LogInformation("Applying {Count} pending migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
		await db.Database.MigrateAsync();
	}
}

// First in the pipeline so it wraps every other middleware/endpoint below.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
}

// No UseHttpsRedirection: TLS terminates at Coolify's proxy, which already redirects http→https;
// the container only listens on plain HTTP (see Dockerfile), so the middleware had no port to use.
app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAuthEndpoints();
app.MapPlayerEndpoints();
app.MapTeamEndpoints();
app.MapAgentCostEndpoints();
app.MapEngineCostEndpoints();
app.MapContributionEndpoints();
app.MapTournamentEndpoints();
app.MapRegistrationEndpoints();
app.MapHistoryEndpoints();
app.MapMatchEndpoints();
app.MapSeasonEndpoints();

// See the OnMessageReceived handler above for how a bearer token reaches a WebSocket handshake.
app.MapHub<TournamentHub>("/hubs/tournaments").RequireAuthorization();

app.Run();
