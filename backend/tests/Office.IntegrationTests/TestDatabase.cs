using Npgsql;

[assembly: AssemblyFixture(typeof(Office.IntegrationTests.TestDatabase))]

namespace Office.IntegrationTests;

/// <summary>
/// One throwaway database per test run on the local PostgreSQL: created by the superuser,
/// prepared like infra/postgres/init (owner, CONNECT grants, default privileges), migrated
/// with the real dbmate files in database/migrations as office_migrator, used by the API as
/// office_api, and dropped at the end.
/// </summary>
public sealed class TestDatabase : IAsyncLifetime
{
    public string Name { get; } = "office_test_" + Guid.NewGuid().ToString("N")[..10];
    public string ApiConnectionString { get; private set; } = "";
    public string AppConnectionString { get; private set; } = "";

    private readonly string _host = Env("OFFICE_TEST_PG_HOST", "127.0.0.1");
    private readonly string _superuser = Env("POSTGRES_SUPERUSER");
    private readonly string _superPassword = Env("POSTGRES_SUPERUSER_PASSWORD");

    public async ValueTask InitializeAsync()
    {
        await using (var admin = new NpgsqlConnection(Conn("postgres", _superuser, _superPassword)))
        {
            await admin.OpenAsync();
            await Exec(admin, $"""CREATE DATABASE "{Name}" OWNER office_migrator""");
            await Exec(admin, $"""
                REVOKE ALL ON DATABASE "{Name}" FROM PUBLIC;
                GRANT CONNECT ON DATABASE "{Name}" TO office_app, office_readonly, office_api
                """);
        }

        // Same as the second half of infra/postgres/init/01-create-databases-and-roles.sh.
        await using (var admin = new NpgsqlConnection(Conn(Name, _superuser, _superPassword)))
        {
            await admin.OpenAsync();
            await Exec(admin, """
                REVOKE CREATE ON SCHEMA public FROM PUBLIC;
                ALTER DEFAULT PRIVILEGES FOR ROLE office_migrator GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO office_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE office_migrator GRANT USAGE, SELECT ON SEQUENCES TO office_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE office_migrator GRANT SELECT ON TABLES TO office_readonly;
                ALTER DEFAULT PRIVILEGES FOR ROLE office_migrator GRANT SELECT ON SEQUENCES TO office_readonly;
                """);
        }

        await using (var migrator = new NpgsqlConnection(Conn(Name, "office_migrator", Env("OFFICE_MIGRATOR_PASSWORD"))))
        {
            await migrator.OpenAsync();
            foreach (var file in Directory.GetFiles(MigrationsDirectory(), "*.sql").Order(StringComparer.Ordinal))
            {
                await Exec(migrator, UpSection(await File.ReadAllTextAsync(file)));
            }
        }

        ApiConnectionString = Conn(Name, "office_api", Env("OFFICE_API_PASSWORD"));
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

    public Task SetEchoAsync(bool enabled, string recipients) => ExecAsync($"""
        UPDATE hazem.settings SET value = '{(enabled ? "true" : "false")}' WHERE key = 'echo.enabled';
        UPDATE hazem.settings SET value = '{recipients}' WHERE key = 'echo.recipients';
        """);

    /// <summary>The "-- migrate:up" part of a dbmate file.</summary>
    private static string UpSection(string sql)
    {
        var up = sql.IndexOf("-- migrate:up", StringComparison.Ordinal);
        var down = sql.IndexOf("-- migrate:down", StringComparison.Ordinal);
        return up < 0 ? sql : down < 0 ? sql[up..] : sql[up..down];
    }

    private static string MigrationsDirectory()
    {
        if (Environment.GetEnvironmentVariable("OFFICE_TEST_MIGRATIONS_DIR") is { Length: > 0 } configured)
        {
            return configured;
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "database", "migrations");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("database/migrations not found; set OFFICE_TEST_MIGRATIONS_DIR.");
    }

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
