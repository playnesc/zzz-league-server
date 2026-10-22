using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.Discord;
using ZenlessLeague.Api.Features.Storage;
using ZenlessLeague.Api.Features.Tournaments.Challonge;

namespace ZenlessLeague.Api.IntegrationTests;

// Boots the real Program.cs (Development environment, so the container validates every service
// registration at build time) against an empty database — the same thing a deploy does.
[Collection(PostgresCollection.Name)]
public class AppStartupTests(PostgresFixture postgres)
{
	[Fact]
	public async Task App_StartsOnAnEmptyDatabase_MigratesItAndServesRequests()
	{
		var database = await postgres.CreateEmptyDatabaseAsync();
		await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
		{
			builder.UseEnvironment("Development");
			builder.UseSetting("ConnectionStrings:Default", postgres.ConnectionString(database));
		});
		var client = factory.CreateClient();

		Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
		// Anonymous, DB-backed reads through the full pipeline (auth, visibility, EF).
		Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/tournaments")).StatusCode);
		Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/players")).StatusCode);
		Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/players/me")).StatusCode);

		// Startup applied every migration.
		await using (var db = postgres.CreateContext(database))
		{
			Assert.Empty(await db.Database.GetPendingMigrationsAsync());
		}

		// Typed clients build their resilience pipelines (whose options are only validated then).
		using var scope = factory.Services.CreateScope();
		Assert.NotNull(scope.ServiceProvider.GetRequiredService<IChallongeClient>());
		Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDiscordClient>());
		Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDiscordOAuthClient>());
		Assert.NotNull(scope.ServiceProvider.GetRequiredService<IFirebaseStorageClient>());
		Assert.NotNull(scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
	}
}
