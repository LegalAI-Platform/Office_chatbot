# Backend (.NET API) — DECISIONS v1.8

The brain of Hazem. n8n is the WhatsApp layer and hands every client *turn* to this API;
the API decides what to answer and n8n sends it. The API never talks to Meta and has no
access to the WhatsApp tables. Contract: [../docs/hazem-internal-api.md](../docs/hazem-internal-api.md)
(unchanged by TASK 04).

## Layers (TASK 04)

```text
Office.Api ──► Office.Infrastructure ──► Office.Application ──► Office.Domain
 (HTTP, DTOs)   (EF Core, OpenAI, n8n)     (use cases, ports)      (entities, state machines)
```

| Project | Content |
|---|---|
| `src/Office.Domain` | No dependency at all. Value objects (`WaId`, `PersonName`, `Money`), the state machines S-01..S-06 (`StateMachine<T>`: allowed transitions and who may make them; a refused one is a `Result` with a clear code, never a random exception), entities with behaviour: `Person`, `Conversation`, `Intake` (clients), `Appointment`, `Payment` (booking, v1.4), `StaffTask`, `UrfCase`. Appointment/payment/task/Urfi have code and tests only; their tables come with their tasks |
| `src/Office.Application` | Use cases `HandleTurn`, `RecordSendResults`, `SendOutbound`; ports `ITurnStore`, `IConversationStore`, `IIntentClassifier`, `IApprovedTexts`, `IWhatsAppGateway`, `ISettingsReader` (+ `TimeProvider`); `ITurnResponder` with `HazemResponder` (TASK 04 flow) and `EchoResponder` (test echo), chosen by `hazem.settings responder`; `AiInput` (the only builder of what the AI sees, T-09); `Classification` (closed lists, fallback); `ReplyComposer`/`TextRenderer` (approved texts only, B-21 split, part-5 placeholders refused) |
| `src/Office.Infrastructure` | EF Core on schema `hazem` (role `office_api`); the turn and everything it changed are saved in one transaction; `OpenAiIntentClassifier` (Structured Outputs, prompt `Ai/prompts/classify-v1.md`); `N8nWhatsAppGateway` |
| `src/Office.Api` | `POST /api/hazem/turns`, `POST /api/hazem/send-results` (header `X-Hazem-Token`), `/health/live`, `/health/ready`. `Contracts/`: the HTTP DTOs, their validation (`TurnRules`) and the mapping to application types |
| `src/Office.Cli` | `send-test --to <wa_id>` (only `echo.recipients`), `classify-eval` (real key: [docs/ai/classification.md](../docs/ai/classification.md)), `import-lists` (B-14 lists from CSV), `tasks-overdue` (S-05), `unmute` (S-01, the manager) |

**TASK 05a additions:** `IClientLists` (B-14 lists, `hazem.list_entries`), `StaffEvents`
(`POST /api/hazem/staff-events`, B-12), real staff tasks with a B-06 due time (`OfficeHours`,
`hazem.staff_tasks`), the client's source (B-13, from Meta's referral), the topic of the request
(B-25, `Intake.MatterType`), abuse (S-01: one warning, then muted), and `classify-v4` (two new
intents, the topic and the sender role in the same call).

**TASK 08 additions (DECISIONS v1.12):** the booking. `Office.Domain/Booking/Availability.cs` (the
free times from the panel's week and exceptions, the buffer, the notice, the horizon, a daily limit,
the clock change), `Office.Application/Booking` (`BookingDesk`, the services of B-23, the ids of the
WhatsApp buttons), buttons and lists in `ReplyComposer` (`send_interactive`), the booking in
`HazemResponder` (B-08, B-09, B-15, B-22), `Office.Infrastructure/Booking` (`BookingStore` over the
SQL functions of migration 0013, `GoogleCalendar` with a service account, `CalendarSync` + its
worker, `PanelBooking` for `/api/panel/booking`). Runbook: [docs/runbooks/booking.md](../docs/runbooks/booking.md).

**TASK 07a additions (the panel, T-11 / T-23):** `Application/Panel`: `Permissions` (RBAC: a role is
a set of permissions, defined once; the code checks permissions, never role names; no super
admin), `PanelAccounts` (sign-in with lockout, sessions ended by a new security stamp, accounts
managed by the manager with a temporary password shown once, every change in `hazem.panel_audit`),
`PanelTasks` (the staff tasks through the S-05 machine; a staff member sees own and unassigned
tasks only, and gets 404 for the others). `Infrastructure/Panel`: the stores and `PanelSecrets`
(ASP.NET Core Identity's `PasswordHasher`). `Api/Panel`: `/api/panel/*` with a cookie session,
one authorization policy per permission, antiforgery on every change and a login rate limit
([docs/panel-api.md](../docs/panel-api.md)). CLI: `create-user` (first manager), `reset-password`,
`unlock-user`, `list-users` ([docs/runbooks/panel.md](../docs/runbooks/panel.md)). No new package:
Identity's hasher, cookies, antiforgery and rate limiting are part of ASP.NET Core.

| Project | Content |
|---|---|
| `tests/Office.UnitTests` | Domain (every transition of S-01..S-06), understanding (AI input, fallback, OpenAI call faked), approved texts, the conversation flow with fakes, the old contract rules |
| `tests/Office.IntegrationTests` | Real API + throwaway PostgreSQL database built from `database/migrations`; n8n and OpenAI faked over HTTP |
| `tests/Office.TestStub` | Meta media, OpenAI transcription and OpenAI classification stand-in for `hazem-e2e` |

## Rules

- **No EF migrations.** The schema comes from dbmate (`database/migrations`, T-21). Change a
  table there first, then the row class in `HazemDbContext`.
- **No text in the code.** Every word Hazem sends is a row of `hazem.approved_texts`
  (`TextKeys` holds the keys only). Inactive texts are never sent.
- **The AI classifies only** (T-07) and sees text only (T-09): `AiInput.Build` is the one place
  that builds its input; phone numbers, national IDs and e-mails are masked first.
- **Never log message text, names or full phone numbers** (`LogMask.Phone`). `hazem.classifications`
  has no text column. Turn logs carry text keys and fallback reasons only.
- **Hazem answers only `hazem.recipients`** until the owner sets `hazem.open_to_all = true`.
- **Times go to the database in UTC** (`ConversationStore.Utc`): Npgsql refuses another offset,
  and B-06 due times are Cairo times. A test pins the clock at night (`OfficeHoursApiTests`).
- **The panel checks permissions, never roles**: every `/api/panel` endpoint names a policy
  (`PanelAuth.Policy(...)`), and a test walks every endpoint to prove it.
- **Every send goes through n8n** (`IWhatsAppGateway`) with a stable idempotency key, so a
  retry can never reach the client twice.
- Package versions are pinned in `Directory.Packages.props`; warnings are errors. TASK 04 added
  no package (OpenAI is called with `HttpClient`).

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
`Hazem__N8nInternalSendToken` (`N8N_INTERNAL_SEND_TOKEN`), `Hazem__OpenAiApiKey`
(`HAZEM_OPENAI_API_KEY`, may be empty), `Hazem__OpenAiModel` (`OPENAI_MODEL`),
`Hazem__OpenAiBaseUrl` (`OPENAI_BASE_URL`). The API refuses to start when a token is shorter
than 32 characters or both tokens are the same.
