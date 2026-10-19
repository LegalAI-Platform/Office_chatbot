using Microsoft.EntityFrameworkCore;
using Npgsql;
using Office.Infrastructure.Persistence;

[assembly: AssemblyFixture(typeof(Office.IntegrationTests.TestDatabase))]

namespace Office.IntegrationTests;

/// <summary>
/// One throwaway database per test run on the local PostgreSQL: created by the superuser,
/// owned by office_migrator, migrated exactly like production (EF + Hangfire schema),
/// used by the API as office_app, dropped at the end.
/// </summary>
public sealed class TestDatabase : IAsyncLifetime
{
    public string Name { get; } = "office_test_" + Guid.NewGuid().ToString("N")[..10];
    public string AppConnectionString { get; private set; } = "";
    public string RedisConnectionString { get; }

    private readonly string _host = Env("OFFICE_TEST_PG_HOST", "127.0.0.1");
    private readonly string _superuser = Env("POSTGRES_SUPERUSER");
    private readonly string _superPassword = Env("POSTGRES_SUPERUSER_PASSWORD");

    public TestDatabase()
    {
        RedisConnectionString = $"{Env("OFFICE_TEST_REDIS_HOST", "127.0.0.1")}:6379,password={Env("REDIS_PASSWORD")},abortConnect=false";
    }

    public async ValueTask InitializeAsync()
    {
        await using (var admin = new NpgsqlConnection(Conn("postgres", _superuser, _superPassword)))
        {
            await admin.OpenAsync();
            await Exec(admin, $"""CREATE DATABASE "{Name}" OWNER office_migrator""");
            await Exec(admin, $"""REVOKE ALL ON DATABASE "{Name}" FROM PUBLIC; GRANT CONNECT ON DATABASE "{Name}" TO office_app, office_readonly""");
        }

        await using (var admin = new NpgsqlConnection(Conn(Name, _superuser, _superPassword)))
        {
            await admin.OpenAsync();
            await Exec(admin, "REVOKE CREATE ON SCHEMA public FROM PUBLIC");
        }

        var migratorOptions = new DbContextOptionsBuilder<OfficeDbContext>()
            .UseNpgsql(Conn(Name, "office_migrator", Env("OFFICE_MIGRATOR_PASSWORD")),
                n => n.MigrationsHistoryTable(OfficeDbContext.MigrationsHistoryTable, OfficeDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .Options;
        await using (var db = new OfficeDbContext(migratorOptions))
        {
            await new DatabaseMigrator(db).MigrateAsync(null, CancellationToken.None);
        }

        AppConnectionString = Conn(Name, "office_app", Env("OFFICE_APP_PASSWORD"));
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(Conn("postgres", _superuser, _superPassword));
        await admin.OpenAsync();
        await Exec(admin, $"""DROP DATABASE IF EXISTS "{Name}" WITH (FORCE)""");
    }

    /// <summary>Runs SQL as the superuser on the test database (setup and assertions only).</summary>
    public async Task<T?> ScalarAsync<T>(string sql)
    {
        await using var c = new NpgsqlConnection(Conn(Name, _superuser, _superPassword));
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        var value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task ExecAsync(string sql)
    {
        await using var c = new NpgsqlConnection(Conn(Name, _superuser, _superPassword));
        await c.OpenAsync();
        await Exec(c, sql);
    }

    public Task ResetAsync() => ExecAsync("""
        TRUNCATE app.wa_message_statuses, app.wa_outbound_messages, app.wa_inbound_messages,
                 app.wa_contact_windows, app.wa_templates, app.wa_webhook_events CASCADE
        """);

    private string Conn(string db, string user, string password) =>
        new NpgsqlConnectionStringBuilder { Host = _host, Port = 5432, Database = db, Username = user, Password = password, Pooling = true }.ConnectionString;

    private static async Task Exec(NpgsqlConnection c, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string Env(string name, string? fallback = null) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v
        : fallback ?? throw new InvalidOperationException($"{name} is not set. Run the tests with scripts/*/backend-test.");
}
