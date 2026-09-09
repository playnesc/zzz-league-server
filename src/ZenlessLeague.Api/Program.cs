using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Data.Authorization;
using ZenlessLeague.Api.Features.Discord;
using ZenlessLeague.Api.Features.History;
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
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// Vite's default dev/preview origins — the SvelteKit frontend calls this API directly
// with a Bearer token (not cookies), so no AllowCredentials() is needed.
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins("http://localhost:5173", "http://localhost:4173")
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

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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

builder.Services.AddScoped<IPlayerRepository, PlayerRepository>();
builder.Services.AddScoped<IPlayerService, PlayerService>();

builder.Services.AddScoped<ITeamRepository, TeamRepository>();
builder.Services.AddScoped<ITeamService, TeamService>();

builder.Services.AddScoped<ITournamentRepository, TournamentRepository>();
builder.Services.AddScoped<ITournamentService, TournamentService>();
builder.Services.AddScoped<ITournamentRegistrationRepository, TournamentRegistrationRepository>();
builder.Services.AddScoped<ITournamentRegistrationService, TournamentRegistrationService>();

builder.Services.AddScoped<IHistoryRepository, HistoryRepository>();
builder.Services.AddScoped<IHistoryService, HistoryService>();

builder.Services.Configure<ChallongeOptions>(builder.Configuration.GetSection("Challonge"));
builder.Services.AddHttpClient<IChallongeClient, ChallongeClient>(client =>
    client.BaseAddress = new Uri("https://api.challonge.com/v2.1/"));

builder.Services.AddScoped<ITournamentChallongeParticipantRepository, TournamentChallongeParticipantRepository>();
builder.Services.AddScoped<ITournamentMatchRepository, TournamentMatchRepository>();
builder.Services.AddScoped<IMatchService, MatchService>();

builder.Services.AddSignalR();
builder.Services.AddScoped<ITournamentNotifier, TournamentNotifier>();

builder.Services.Configure<DiscordOptions>(builder.Configuration.GetSection("Discord"));
builder.Services.AddHttpClient<IDiscordClient, DiscordClient>(client =>
    client.BaseAddress = new Uri("https://discord.com/api/v10/"));

builder.Services.AddScoped<IDiscordRoleAssignmentService, DiscordRoleAssignmentService>();
builder.Services.AddScoped<ITournamentDiscordResourcesService, TournamentDiscordResourcesService>();

// Lazy singleton — only built (and only fails if Firebase:CredentialsPath is missing/invalid)
// the first time something actually needs to call Firebase Storage, same lazy-config pattern as
// ChallongeOptions/DiscordOptions above.
builder.Services.AddSingleton(_ =>
{
    var credentialsPath = builder.Configuration["Firebase:CredentialsPath"]
        ?? throw new InvalidOperationException("Firebase:CredentialsPath is not configured.");
#pragma warning disable CS0618
    return GoogleCredential.FromFile(credentialsPath)
        .CreateScoped("https://www.googleapis.com/auth/devstorage.read_write");
#pragma warning restore CS0618
});
builder.Services.Configure<FirebaseStorageOptions>(builder.Configuration.GetSection("Firebase"));
builder.Services.AddHttpClient<IFirebaseStorageClient, FirebaseStorageClient>();

builder.Services.AddScoped<IArchiveRepository, ArchiveRepository>();
builder.Services.AddScoped<ISeasonService, SeasonService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapPlayerEndpoints();
app.MapTeamEndpoints();
app.MapTournamentEndpoints();
app.MapRegistrationEndpoints();
app.MapHistoryEndpoints();
app.MapMatchEndpoints();
app.MapSeasonEndpoints();

// See the OnMessageReceived handler above for how a bearer token reaches a WebSocket handshake.
app.MapHub<TournamentHub>("/hubs/tournaments").RequireAuthorization();

app.Run();
