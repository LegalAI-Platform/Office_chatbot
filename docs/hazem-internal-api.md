# Internal API between n8n and the .NET backend (DECISIONS v1.3, T-17 to T-20; TASK 03 turn shape)

n8n is the WhatsApp layer (the only thing that talks to Meta); the .NET API is the brain.
They talk only inside the Docker network `office_net`.

> **TASK 05a (additive only):** a message may carry an optional `referral` (B-13) —
> `{ "source_type": "ad", "source_id": "1202…", "headline": "…" }`, never an image or video link
> (n8n's `app.wa_message_json`, migration 0008). Old turns stay valid. And a new endpoint for the
> panel / n8n: `POST /api/hazem/staff-events` (below).
>
> **TASK 04: the contract did not change.** Same requests, same responses, same errors; only
> what the API decides to answer changed (approved texts instead of the test echo). An answer
> may now hold more than one `send_text` action (e.g. the B-20 text then the consultation
> offer), keys `hazem:<turn_id>:0`, `:1`, …; only the first quotes the client's last message.

```mermaid
sequenceDiagram
    participant Meta
    participant n8n
    participant API as .NET API
    Meta->>n8n: webhook (signed)
    n8n->>n8n: wa_process_event (dedupe, 24h window, wa_media row)
    n8n->>Meta: media: GET /{media_id}, then download at once
    n8n->>n8n: S3 (office-sensitive / office-media); voice → OpenAI transcript
    n8n->>n8n: wait 8 s quiet (max 60 s), wa_turns_claim → one turn per client
    n8n->>API: POST /api/hazem/turns
    API-->>n8n: { status, actions[] }
    n8n->>Meta: send each action (idempotency_key)
    n8n->>API: POST /api/hazem/send-results
    n8n->>n8n: wa_turns_finish(turn_id, processed)
    Note over API,n8n: .NET-initiated sends (reminders, panel)
    API->>n8n: POST /webhook/hazem-internal-send
    n8n->>Meta: send (24h window and template rules apply)
    n8n-->>API: { ok, wamid, error_code }
```

## Authentication

| Direction | Header | Secret in `.env` | n8n credential |
|---|---|---|---|
| n8n → .NET | `X-Hazem-Token` | `HAZEM_API_TOKEN` | `hazem-api` (Header Auth) |
| .NET → n8n | `X-Hazem-Token` | `N8N_INTERNAL_SEND_TOKEN` | `hazem-internal-send` (Header Auth on the Webhook node) |

- Two different secrets, at least 32 characters each (preflight refuses anything else).
- .NET compares in constant time and answers `401` without a body detail.
- n8n is reachable from the internet through the tunnel, so its internal webhook is protected
  **only** by the header. The .NET API has no host port and is never behind the tunnel.

## n8n → .NET

Base URL: `http://api:8080`.

### `POST /api/hazem/turns`

One *turn* = what the client said that needs one answer (TASK 03): every message the client
sent until 8 seconds passed without a new one (`whatsapp.group_quiet_seconds`), or at most 60
seconds after the first (`whatsapp.group_max_seconds`), with every media file already
downloaded and every voice note already transcribed. `turn_id` is `wt:<uuid>`: opaque, do not
parse it. Turns from before TASK 03 (`wa:<wamid>`) stay as they are in `hazem.turns`.

```json
{
  "turn_id": "wt:0b6c2a7e-…",
  "wa_id": "201001234567",
  "contact_name": "Client name from WhatsApp",
  "messages": [
    { "wamid": "…", "type": "text", "text": "عايز أحجز استشارة", "sent_at": "2026-10-20T10:15:00+03:00", "context_wamid": null },
    { "wamid": "…", "type": "image", "caption": "البطاقة", "sent_at": "…", "context_wamid": null,
      "media": { "status": "stored", "bucket": "office-sensitive", "key": "wa/2026/10/9f1c….jpg",
                 "mime_type": "image/jpeg", "size_bytes": 123456, "sha256": "…" } },
    { "wamid": "…", "type": "audio", "sent_at": "…", "context_wamid": null,
      "media": { "status": "stored", "bucket": "office-media", "key": "wa/2026/10/3d7a….ogg",
                 "mime_type": "audio/ogg", "size_bytes": 23456, "sha256": "…", "voice": true },
      "transcript": { "status": "done", "text": "…", "language": "ar" } },
    { "wamid": "…", "type": "document", "caption": null, "sent_at": "…", "context_wamid": null,
      "media": { "status": "too_large", "mime_type": "application/pdf", "size_bytes": 73400320, "filename": "…" } },
    { "wamid": "…", "type": "location", "location": { "latitude": 30.0, "longitude": 31.2, "name": null, "address": null } },
    { "wamid": "…", "type": "reaction", "reaction": { "emoji": "👍", "message_wamid": "…" } },
    { "wamid": "…", "type": "interactive", "interactive": { "kind": "button_reply", "id": "…", "title": "…" } },
    { "wamid": "…", "type": "button", "button": { "payload": "…", "text": "…" } },
    { "wamid": "…", "type": "unsupported", "original_type": "contacts" }
  ]
}
```

A text message keeps exactly the TASK 02 shape (`wamid`, `type`, `text`, `sent_at`,
`context_wamid`). Messages are ordered by `sent_at`.

| Field | Rule |
|---|---|
| `turn_id` | required, 1–200 characters, unique per turn |
| `wa_id` | required, 8–15 digits |
| `messages` | 1–50 items, each with `wamid` and `type` |
| `type` | `text`, `image`, `document`, `audio`, `video`, `sticker`, `location`, `reaction`, `interactive` (`button_reply` / `list_reply`), `button` (template quick replies), or `unsupported` (any other Meta type, with `original_type`) |
| `text`, `caption` | up to 4096 characters |
| `media` | required for `image`, `document`, `audio`, `video`, `sticker`, and only there. `status`: `stored`, `failed`, `too_large` (over `whatsapp.media_max_mb`), `rejected` (type not allowed) — never a download in progress |
| `media.bucket`, `media.key` | only when `status` is `stored`. Bucket `office-sensitive` for every image and document (they may be ID cards, Urfi papers, receipts or contracts), `office-media` for audio, video and stickers. Key `wa/<yyyy>/<mm>/<random id>.<ext>`: never a phone number, a name, a wamid or the original file name |
| `media.filename` | the client's document name. **May hold personal data: never log it** |
| `transcript` | audio only, once transcription finished: `status` `done` or `failed`, `text` up to 16000 characters |
| URLs | **none, ever** (T-09). No field of a file reference may contain `://`. The API gets references only and has no key to read the files (until a later task gives it one) |

Response `200`:

```json
{
  "turn_id": "wa:wamid.HBgM...",
  "status": "processed",
  "actions": [
    {
      "type": "send_text",
      "text": "…",
      "idempotency_key": "hazem:wa:wamid.HBgM...:0",
      "context_wamid": "wamid.HBgM..."
    },
    {
      "type": "send_template",
      "template_name": "reminder_day_before",
      "language": "ar",
      "components": [],
      "idempotency_key": "hazem:wa:wamid.HBgM...:1"
    }
  ]
}
```

- `status` is `processed` the first time and `duplicate` when the same `turn_id` comes again;
  a duplicate returns **the same actions** so n8n can safely finish a turn it lost half-way
  (the idempotency keys stop anything from being sent twice).
- `actions` may be empty (nothing to send).
- `400`: invalid request (problem details). n8n marks the messages `failed` and logs the error.
- `401`: missing or wrong token.
- `5xx` or no answer: n8n puts the messages back to `pending` and retries the next minute.

### `POST /api/hazem/send-results`

What happened to each action n8n tried to send.

```json
{
  "turn_id": "wa:wamid.HBgM...",
  "results": [
    {
      "idempotency_key": "hazem:wa:wamid.HBgM...:0",
      "ok": true,
      "outbound_id": "0b9c…",
      "wamid": "wamid.HBgN...",
      "status": "sent",
      "error_code": null,
      "error_message": null
    }
  ]
}
```

Response `200`: `{ "recorded": 1 }`. Recording the same key again updates it (idempotent).
Error codes are the ones in [whatsapp-integration.md](whatsapp-integration.md#error-codes).

### `POST /api/hazem/staff-events` (TASK 05a, B-12)

For the panel (TASK 07) and for n8n when a staff member writes from the WhatsApp Business app
(TASK 05b). Same `X-Hazem-Token`.

```json
{ "wa_id": "201001234567", "event": "staff_replied", "at": "2026-10-22T14:00:00+03:00" }
```

- `staff_replied`: the conversation becomes `human_handoff` and Hazem is silent; Hazem comes back
  by itself when the client writes 12 hours after the last staff message (B-12.2).
- `hand_back`: «رجّع لحازم» → `bot_active`.
- `at` is optional (empty = now). It may be in the past (a late report) but not more than 5
  minutes ahead of the API's clock: a future time would keep Hazem silent long past 12 hours → `400`.
- `200 { "state": "human_handoff" }`, `404` unknown number, `409 { state, error }` when the
  transition is not allowed (e.g. `hand_back` while Hazem already answers), `400` bad body.

## .NET → n8n

### `POST http://n8n:5678/webhook/hazem-internal-send`

```json
{ "type": "send_text", "wa_id": "201001234567", "text": "…",
  "idempotency_key": "reminder:appointment-42:day-before", "requested_by": "hazem-api" }
```

or

```json
{ "type": "send_template", "wa_id": "201001234567", "template_name": "reminder_day_before",
  "language": "ar", "components": [], "idempotency_key": "…", "requested_by": "hazem-api" }
```

Response `200` (always, the outcome is in the body):

```json
{ "ok": true, "outbound_id": "…", "wamid": "wamid…", "status": "sent", "error_code": null, "error_message": null }
```

- Same rules as any other send: 24-hour window for text, `APPROVED` templates only, and the
  same `idempotency_key` never sends twice (the first result is returned with `DUPLICATE`
  when it had not succeeded).
- Wrong or missing token: `403` from n8n, nothing is sent.
- Because of the idempotency key, .NET may retry a call that timed out.

## Who owns what

| | n8n (`office_app`) | .NET (`office_api`) |
|---|---|---|
| Schema `app` (`wa_*`, `settings`) | read/write | **no access** |
| Schema `hazem` (turns, send results, business tables) | **no access** | read/write |
| Meta Graph API | only caller | never |
| Business decisions | never | only place |

## Turn and message states

`app.wa_turns`: `processing` (claimed) → `processed` / `failed`, or `pending` (given back after
the API failed; retried first, with the **same** `turn_id` and the **same** messages). A client
has at most one open turn (`processing` or `pending`) — a partial unique index — so its replies
keep their order and new messages wait for the next turn. A turn left `processing` for longer
than `whatsapp.turn_stale_seconds` (300) is taken again with the same `turn_id`; the API answers
it as a duplicate with the same actions, and the idempotency keys stop a second send.

`app.wa_inbound_messages` follows its turn: `pending` (waiting, or its turn was given back) →
`processing` → `processed` / `failed`. `app.wa_media`: `pending` → `downloading` → `stored` /
`failed` / `too_large` / `rejected`; audio then gets `transcript_status` `done` or `failed`.

## Testing without Meta

- `whatsapp.dry_run = true` in `app.settings`: sends are recorded as `sent` with a fake
  `wamid.DRYRUN-…` and Meta is never called. Keep it `false` (the healthcheck fails otherwise).
- `echo.enabled = true` and `echo.recipients` in `hazem.settings`: the API answers every text
  turn from those numbers with «تم استلام رسالتك (اختبار) ✅». Off by default.
- `whatsapp.graph_base_url` and `ai.openai_base_url` point at `meta-stub` (docker compose
  profile `tests`) during `hazem-e2e` only: a stand-in for Meta's media endpoints and OpenAI
  transcription that records what it received (never the Authorization value).
- `hazem-e2e` (scripts) runs the whole loop with dummy numbers, the stub and both switches,
  then restores every setting it changed. The healthcheck fails while any of them is left on.
