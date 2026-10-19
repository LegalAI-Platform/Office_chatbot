using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Office.Infrastructure.Persistence;

/// <summary>
/// Applies EF Core migrations and the Hangfire schema. Runs only from `Office.Cli migrate`
/// as office_migrator; the API never migrates (T-19).
/// </summary>
public sealed class DatabaseMigrator(OfficeDbContext db)
{
    public const string HangfireSchema = "hangfire";

    public async Task<IReadOnlyList<string>> MigrateAsync(string? targetMigration, CancellationToken ct)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (targetMigration is null)
        {
            await db.Database.MigrateAsync(ct);
        }
        else
        {
            await db.GetService<IMigrator>().MigrateAsync(targetMigration, ct);
        }

        await InstallHangfireSchemaAsync(ct);
        return pending;
    }

    public async Task<(IReadOnlyList<string> Applied, IReadOnlyList<string> Pending)> ListAsync(CancellationToken ct) =>
        ((await db.Database.GetAppliedMigrationsAsync(ct)).ToList(), (await db.Database.GetPendingMigrationsAsync(ct)).ToList());

    private async Task InstallHangfireSchemaAsync(CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            PostgreSqlObjectsInstaller.Install(connection, HangfireSchema);

            // The API runs as office_app with PrepareSchemaIfNecessary = false: data access only.
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"""
                GRANT USAGE ON SCHEMA {HangfireSchema} TO office_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {HangfireSchema} TO office_app;
                GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA {HangfireSchema} TO office_app;
                ALTER DEFAULT PRIVILEGES IN SCHEMA {HangfireSchema} GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO office_app;
                ALTER DEFAULT PRIVILEGES IN SCHEMA {HangfireSchema} GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO office_app;
                """;
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }
}
