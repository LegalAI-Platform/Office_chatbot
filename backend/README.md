# Backend (.NET API) — DECISIONS v1.3

The brain of Hazem. n8n is the WhatsApp layer and hands every client *turn* to this API;
the API decides what to answer and n8n sends it. The API never talks to Meta and has no
access to the WhatsApp tables. Contract: [../docs/hazem-internal-api.md](../docs/hazem-internal-api.md).

| Project | Content |
|---|---|
| `src/Office.Core` | Rules with no I/O: turn validation, `TurnResponder` (test echo until TASK 04), `TurnService`, `OutboundService` |
| `src/Office.Infrastructure` | EF Core on schema `hazem` (role `office_api`), `N8nWhatsAppGateway` (calls n8n's internal send webhook) |
| `src/Office.Api` | `POST /api/hazem/turns`, `POST /api/hazem/send-results` (header `X-Hazem-Token`), `/health/live`, `/health/ready` |
| `src/Office.Cli` | `send-test --to <wa_id> [--text …]` (only numbers in `hazem.settings` `echo.recipients`) |
| `tests/Office.UnitTests` | Core rules |
| `tests/Office.IntegrationTests` | Real API + throwaway PostgreSQL database built from `database/migrations`; n8n faked over HTTP |

## Rules

- **No EF migrations.** The schema comes from dbmate (`database/migrations`, T-21). Change a
  table there first, then the entity in `HazemDbContext`.
- **Never log message text or full phone numbers** (`TurnRules.Mask`).
- **Every send goes through n8n** (`IWhatsAppGateway`) with a stable idempotency key, so a
  retry can never reach the client twice.
- Package versions are pinned in `Directory.Packages.props`; warnings are errors.

## Run

```powershell
# Tests (local .NET 10 SDK if installed, otherwise the SDK container; the stack must be up)
powershell -ExecutionPolicy Bypass -File scripts\windows\backend-test.ps1
# The service itself is built and started by `up` (docker compose service "api").
# CLI inside the running container:
docker compose exec api dotnet /app/cli/Office.Cli.dll send-test --to 2010XXXXXXXX --text "test"
```

Configuration comes from the environment (`docker-compose.yml`): `ConnectionStrings__Office`
(role `office_api`), `Hazem__ApiToken` (`HAZEM_API_TOKEN`), `Hazem__N8nInternalSendUrl`,
`Hazem__N8nInternalSendToken` (`N8N_INTERNAL_SEND_TOKEN`). The API refuses to start when a token
is shorter than 32 characters or both tokens are the same.
