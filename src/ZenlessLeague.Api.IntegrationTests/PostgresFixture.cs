using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using ZenlessLeague.Api.Data;

namespace ZenlessLeague.Api.IntegrationTests;

// One Postgres container (same major version as production) for the whole test run. All
// migrations are applied once to a template database — which also proves the full migration chain
// applies cleanly — and every test then gets its own instant copy via CREATE DATABASE ... TEMPLATE,
// so tests never see each other's data.
public sealed class PostgresFixture : IAsyncLifetime
{
	private const string TemplateDatabase = "zl_template";

	private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:18-alpine").Build();

	public async Task InitializeAsync()
	{
		await container.StartAsync();
		await ExecuteAsync($"CREATE DATABASE \"{TemplateDatabase}\"");

		await using (var db = CreateContext(TemplateDatabase))
		{
			await db.Database.MigrateAsync();
		}

		// A database can only be used as a template while nothing is connected to it, and Npgsql's
		// pool keeps physical connections open after the context is disposed.
		NpgsqlConnection.ClearAllPools();
	}

	// A fresh, fully migrated database for one test.
	public async Task<string> CreateDatabaseAsync()
	{
		var name = NewDatabaseName();
		await ExecuteAsync($"CREATE DATABASE \"{name}\" TEMPLATE \"{TemplateDatabase}\"");
		return name;
	}

	// An empty database with no migrations applied, for tests that migrate step by step.
	public async Task<string> CreateEmptyDatabaseAsync()
	{
		var name = NewDatabaseName();
		await ExecuteAsync($"CREATE DATABASE \"{name}\"");
		return name;
	}

	public string ConnectionString(string database) =>
		new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = database }.ConnectionString;

	public AppDbContext CreateContext(string database) =>
		new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString(database)).Options);

	public Task DisposeAsync() => container.DisposeAsync().AsTask();

	private static string NewDatabaseName() => $"t_{Guid.NewGuid():N}";

	private async Task ExecuteAsync(string sql)
	{
		await using var connection = new NpgsqlConnection(container.GetConnectionString());
		await connection.OpenAsync();
		await using var command = new NpgsqlCommand(sql, connection);
		await command.ExecuteNonQueryAsync();
	}
}

[CollectionDefinition(Name)]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
	public const string Name = "Postgres";
}
