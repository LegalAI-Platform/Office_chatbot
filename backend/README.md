# Backend (ASP.NET Core, .NET 10)

Modular monolith (DECISIONS v1.1, T-03). One API host, one CLI, shared Core and Infrastructure.

```
backend/
  src/Office.Core            Domain + use cases. No EF Core, ASP.NET Core or HttpClient (enforced by a test).
      WhatsApp/              Entities, rules, IWhatsAppStore / IWhatsAppCloudApi, WebhookIngestion,
                             EventProcessor, MessageSender, EchoResponder
  src/Office.Infrastructure  OfficeDbContext + EF migrations, WhatsAppStore, Graph API client,
                             MetaAdmin (webhook subscription, templates), Telegram notifier, options
  src/Office.Api             Host: webhook endpoints, Hangfire jobs, health checks, Serilog
  src/Office.Cli             Operational commands (migrate, wa-*, templates-*, send-test)
  tests/Office.UnitTests     Rules, options, catalog, architecture
  tests/Office.IntegrationTests  Real HTTP endpoint + real PostgreSQL (throwaway database), Meta faked at HTTP level
  tests/fixtures/whatsapp    Fake Meta events (dummy numbers only)
```

Dependency direction: `Api → Infrastructure → Core`, `Cli → Infrastructure → Core`. Core depends on nothing.

## Build and test

```powershell
# With Docker only (no SDK needed)
powershell -ExecutionPolicy Bypass -File scripts\windows\backend-build.ps1
powershell -ExecutionPolicy Bypass -File scripts\windows\backend-test.ps1 -Docker

# With the .NET 10 SDK installed
powershell -ExecutionPolicy Bypass -File scripts\windows\backend-test.ps1
```

`backend-test` reads the database passwords from `.env`, creates `office_test_<random>` owned by
`office_migrator`, migrates it, runs the API as `office_app`, and drops the database at the end.

- Packages: central management in `Directory.Packages.props`, every version pinned. Warnings are errors.
- NuGet packages are cached in `backend/.nuget-packages` (gitignored), not in the user profile.
- Tests use Microsoft.Testing.Platform (`global.json` → `"test": { "runner": … }`).

## Migrations

```powershell
# create (developer machine with the SDK)
cd backend
dotnet tool restore
dotnet ef migrations add <Name> --project src/Office.Infrastructure --startup-project src/Office.Infrastructure --output-dir Persistence/Migrations

# apply (any machine)
powershell -ExecutionPolicy Bypass -File scripts\windows\migrate.ps1
```

The API never migrates. `Office.Cli migrate` runs as `office_migrator`, installs the Hangfire schema,
and grants `office_app` data-only rights. Every `app.*` table created by a migration is granted to
`office_app` (DML) and `office_readonly` (SELECT) automatically (default privileges set in the first migration).

## Adding a module

1. `src/Office.Core/<Module>/`: entities, rules, interfaces, use cases (pure C#).
2. `src/Office.Infrastructure/`: EF configuration in `OfficeDbContext`, store implementation, external clients.
3. Register services in `Infrastructure/DependencyInjection.cs`; map endpoints in `Office.Api/<Module>/`.
4. Add a migration; add unit tests for the rules and integration tests for endpoints and SQL.
5. Never log customer text, phone numbers (use `PhoneMask`), tokens or raw request bodies.
