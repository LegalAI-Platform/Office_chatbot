# WAHA — the WhatsApp test channel (TASK 02b, DECISIONS v1.19)

WAHA (WhatsApp HTTP API, free edition, engine NOWEB) lets Hazem talk on WhatsApp **before Meta approves the
account**: a spare phone number is linked like WhatsApp Web (a QR code), and n8n sends and receives through it.

> **A spare number only, never the office's number.** WAHA is not an official API: WhatsApp can ban the number it
> runs. Use a SIM you can lose. When Meta approves, switch back to `meta` and stop WAHA.

What changes and what does not:

- **Only the transport.** The API, Hazem, the texts, the booking, the payments, the Urfi path and the reminders do not
  know which channel it is. Meta's rules stay on: only `whatsapp.test_recipients` get sends (0006), the 24-hour window,
  the template approval (a template is refused on WAHA, so an out-of-window reminder becomes the office's task, as
  before Meta's approval), idempotency, the dry run.
- **Buttons and lists go as a numbered list** in the text («1) هحضر ✅ / 2) تغيير أو إلغاء»). A reply «1» (or «١»)
  within 24 hours comes back to the API as the press of that button. Any other reply is read as words.
- The Meta flow is all still there: one setting, `whatsapp.provider`, chooses the channel.

## Start it (once)

1. **Keys.** `gen-secrets` prints `WAHA_API_KEY`, `WAHA_WEBHOOK_TOKEN` and `WAHA_DASHBOARD_PASSWORD`; copy the three
   lines into `.env`. Then `n8n-import` (it creates the credentials `waha-api` and `waha-webhook` and publishes
   `WAHA-INBOUND`; without the keys that webhook is never published).
2. **Start WAHA:**

   ```powershell
   docker compose --profile waha up -d waha
   docker compose run --rm tools waha-session
   ```

   `waha-session` creates the session `default` with its webhook to n8n (`X-Waha-Token`) and NOWEB's store (WAHA needs
   it to turn a sender that comes as a LID, `…@lid`, into the phone number).
3. **Link the spare number:** open <http://127.0.0.1:3010> (user `office`, password `WAHA_DASHBOARD_PASSWORD`), open
   the session `default`, and scan the QR code from the spare phone (WhatsApp → Linked devices). `waha-session` again
   should say `WORKING`.
4. **The test numbers:** the phones that will write to the spare number go in `whatsapp.test_recipients` (app.settings,
   comma-separated `wa_id`s) and in `hazem.recipients` (hazem.settings), as for Meta's test number.
5. **Switch the channel:**

   ```sql
   UPDATE app.settings SET value = 'waha' WHERE key = 'whatsapp.provider';
   ```

   The healthcheck now WARNs that the channel is WAHA (and when the session is not `WORKING`).

## Back to Meta

```sql
UPDATE app.settings SET value = 'meta' WHERE key = 'whatsapp.provider';
```

```powershell
docker compose --profile waha stop waha
```

To forget the spare number too: unlink it from the phone (Linked devices) and remove the volume `office_waha_sessions`
(`docker volume rm office_waha_sessions`) after stopping WAHA. The keys may stay in `.env`.

## How it works

| Step | Where |
|---|---|
| WAHA posts `message` / `message.ack` to `http://n8n:5678/webhook/waha-inbound` with `X-Waha-Token` | `WAHA-INBOUND` (credential `waha-webhook`; inside the Docker network only, the edge exposes Meta's webhook alone) |
| Heard only while `whatsapp.provider` = `waha` and for the session `waha.session` | `WAHA-INBOUND` |
| A LID sender → `GET /api/default/lids/{lid}` → the phone number; unknown → dropped | `WAHA-INBOUND` |
| WAHA's event → Meta's event (`wamid` = `waha.` + WAHA's id, `phone_number_id` = `waha`; a file → `waha:` + its path under `/api/files`; «1» → the press) | `app.waha_event` (migration 0021) |
| The same steps as Meta's: process, media, quiet period, turns, API | `META-WA-PROCESS-EVENT`, `WA-MEDIA-FETCH`, `WA-FORWARD-TURN` |
| A file is downloaded from `waha.base_url` + its path (never an address from the event), sha256 computed here | `WAHA-MEDIA-ONE` (credential `waha-api`) |
| Sending: every rule first (`wa_outbound_prepare`), then `WAHA?` → `WAHA-SEND` (`/api/sendText`, `/api/sendFile`); the list kept in `app.waha_menus` | `META-WA-SEND-MESSAGE`, `WAHA-SEND`, `app.waha_sent` |
| Templates: refused on WAHA | `META-WA-SEND-TEMPLATE` |

## Checks

- `wa-tests`: the event mapping (text, file, LID, group, our own message, «2» / «١» / «3», after 24 hours, the ack,
  WAHA's answer, a failure) in `wa-db-tests`.
- `hazem-e2e` step 17: WAHA (the stub) as the channel: the webhook refused without its token, a greeting and the name
  through it, the offer as a numbered list, «١» back as the press of the first button (no AI), the times as a list,
  then back on Meta the webhook is not heard.
- `healthcheck` («The WAHA test channel»): the setting is `meta` or `waha`, WAHA's address is inside the network; WARN
  while it is `waha` and when the session is not `WORKING`.

## Known limits

- **Not official:** WhatsApp may ban the number, and an update of WhatsApp Web may stop WAHA until WAHA updates.
- **No buttons:** the numbered list is what the client sees; a wrong number is read as words (the AI).
- **Delivery and read ticks** come from WAHA's acks (`message.ack`) and are best effort.
- **A LID WAHA cannot resolve** is dropped (`unknown_number`); saving the tester's number in the spare phone's contacts helps.
- **Files sent to a client** (TASK 10's delivered file) go by `/api/sendFile`. If this WAHA edition refuses it, the send
  fails and the office gets its task, as for any failed send.
- A file a client sent through WAHA just before switching back to `meta` is still fetched from WAHA (its id says where it is).
