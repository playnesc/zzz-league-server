using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Features.Auth;
using ZenlessLeague.Api.Features.CommunityCosts;
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
using ZenlessLeague.Api.Features.Tournaments.Matches;
using ZenlessLeague.Api.Features.Tournaments.Realtime;
using ZenlessLeague.Api.Infrastructure.Errors;
using ZenlessLeague.Api.Infrastructure.Http;
using ZenlessLeague.Api.Infrastructure.Persistence;

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

builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
builder.Services.AddApiRateLimiting();

// Each feature registers its own services (see <Feature>Feature.cs next to its endpoints).
builder.Services
	.AddAuthFeature(builder.Configuration)
	.AddPlayerFeature()
	.AddTeamFeature()
	.AddCommunityCostFeature()
	.AddTournamentFeature(builder.Configuration)
	.AddHistoryFeature()
	.AddNexusFeature(builder.Configuration)
	.AddDiscordFeature(builder.Configuration)
	.AddStorageFeature(builder.Configuration)
	.AddSeasonFeature();

// TLS terminates at Coolify's proxy, so without this every request's RemoteIpAddress is the proxy's
// (one shared rate-limit bucket, useless logs). X-Forwarded-For is only honored when the direct peer
// is on a private network — i.e. the proxy on the Docker network — and only its last hop is used,
// which is the address the proxy itself saw, so a client can't spoof it by sending the header.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
	options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
	options.KnownNetworks.Clear();
	options.KnownProxies.Clear();
	foreach (var (prefix, length) in new[] { ("10.0.0.0", 8), ("172.16.0.0", 12), ("192.168.0.0", 16), ("127.0.0.0", 8) })
	{
		options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse(prefix), length));
	}
	options.KnownProxies.Add(IPAddress.IPv6Loopback);
});

var app = builder.Build();

// Secrets that are only read when first used (so local development works without them) — a
// deployment missing one would otherwise only find out when that feature is first exercised.
if (!app.Environment.IsDevelopment())
{
	string[] requiredSettings = ["Challonge:ApiKey", "Discord:BotToken", "Discord:GuildId", "Discord:ClientId", "Discord:ClientSecret", "Firebase:StorageBucket"];
	var missing = requiredSettings.Where(key => string.IsNullOrWhiteSpace(app.Configuration[key])).ToList();
	if (string.IsNullOrWhiteSpace(app.Configuration["Firebase:CredentialsJson"]) && string.IsNullOrWhiteSpace(app.Configuration["Firebase:CredentialsPath"]))
	{
		missing.Add("Firebase:CredentialsJson (or Firebase:CredentialsPath)");
	}

	if (missing.Count > 0)
	{
		app.Logger.LogError("Missing configuration — the features that need it will fail: {Settings}", string.Join(", ", missing));
	}
}

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

// Before anything that reads the client address or scheme.
app.UseForwardedHeaders();

// Wraps every other middleware/endpoint below.
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
// After authentication: the per-user limits partition by the caller's uid.
app.UseRateLimiter();

// Liveness only — deliberately doesn't touch the database, so a database blip doesn't get the
// container marked unhealthy and taken out of rotation. /health/ready includes the database, for
// monitoring.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", async (AppDbContext db, CancellationToken ct) =>
	await db.Database.CanConnectAsync(ct)
		? Results.Ok(new { status = "ok" })
		: Results.Json(new { status = "database unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable));
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

// Makes the top-level-statements Program visible to WebApplicationFactory<Program> in the
// integration tests, which boot this exact startup.
public partial class Program;
