using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using ZenlessLeague.Api.Data;
using ZenlessLeague.Migrator;

var dryRun = args.Contains("--dry-run");

var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development";
var configuration = new ConfigurationBuilder()
	.SetBasePath(AppContext.BaseDirectory)
	.AddJsonFile("appsettings.json", optional: true)
	.AddJsonFile($"appsettings.{environment}.json", optional: true)
	.AddEnvironmentVariables()
	.Build();

var connectionString = configuration.GetConnectionString("Default")
	?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
var credentialsPath = configuration["Firebase:CredentialsPath"]
	?? throw new InvalidOperationException("Firebase:CredentialsPath is not configured.");
var databaseUrl = configuration["Firebase:DatabaseUrl"]
	?? throw new InvalidOperationException("Firebase:DatabaseUrl is not configured.");

Console.WriteLine($"ZenlessLeague.Migrator — {(dryRun ? "DRY RUN (rolled back, nothing committed)" : "LIVE RUN")}");
Console.WriteLine($"RTDB source:	 {databaseUrl}");
Console.WriteLine($"Postgres target: {new NpgsqlConnectionStringBuilder(connectionString).Host}/{new NpgsqlConnectionStringBuilder(connectionString).Database}");
Console.WriteLine();

// Same GoogleCredential bearer-token pattern the old FirebasePlayerRepository used.
#pragma warning disable CS0618
var credential = GoogleCredential.FromFile(credentialsPath)
	.CreateScoped("https://www.googleapis.com/auth/firebase.database", "https://www.googleapis.com/auth/userinfo.email");
#pragma warning restore CS0618

using var httpClient = new HttpClient { BaseAddress = new Uri(databaseUrl.TrimEnd('/') + "/") };
var rtdb = new RtdbReader(httpClient, credential);

var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
await using var db = new AppDbContext(dbOptions);

var runner = new MigrationRunner(db, rtdb, dryRun);
var report = await runner.RunAsync();

Console.WriteLine("Row counts processed:");
Console.WriteLine($"  Players:				 {report.Players}");
Console.WriteLine($"  Archives:				 {report.Archives}");
Console.WriteLine($"  Tournaments:			 {report.Tournaments}");
Console.WriteLine($"  ChallongeParticipants: {report.ChallongeParticipants}");
Console.WriteLine($"  Registrations:		 {report.Registrations}");
Console.WriteLine($"  Matches:				 {report.Matches}");
Console.WriteLine($"  HistoryEntries:		 {report.HistoryEntries}");
Console.WriteLine();
Console.WriteLine(dryRun ? "Dry run complete — no changes were committed." : "Migration committed.");
