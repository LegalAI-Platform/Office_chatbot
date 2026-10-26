# Runbook: Chatwoot, the staff's conversations (TASK 07b)

Chatwoot Community Edition `v4.19.1-ce` at http://127.0.0.1:3000 (this machine only until
TASK 14). DECISIONS v1.10 T-24. It is linked to the .NET API by an **API inbox** and never to
WhatsApp or Meta: every reply goes Chatwoot → api → n8n → Meta, with every guard.

## How it works

| What | Where it goes |
|---|---|
| A client's message and Hazem's replies | copied into Chatwoot by the api every few seconds (`hazem.chatwoot_turns`); one conversation per client. A file shows only its kind («📷 صورة — بتتفتح من لوحة المكتب») and its caption; a voice note shows its transcript |
| A staff member's reply | Chatwoot's webhook (signed) → api → n8n → WhatsApp, once (`hazem.chatwoot_replies`). Hazem then stays quiet with that client (B-12) |
| A reply n8n refuses (outside the 24 hours, not a test number…) | a private note in the conversation, in Arabic |
| **Resolve** in Chatwoot | the client goes back to Hazem (the owner's choice), with a private note |
| A file or photo sent from Chatwoot | not sent (text only); a private note says so |

## Accounts

- Every active panel account has a Chatwoot user, made automatically: **everyone is an Agent**
  (the manager too) and a member of «واتساب (حازم)», and **sees every conversation** (the owner's
  choice; the free edition cannot limit a staff member to their own clients). A disabled account
  is taken out of Chatwoot. The api re-checks every account every 10 minutes.
- Only «حازم (النظام)» is an Administrator: nobody can connect WhatsApp to Chatwoot directly or
  move its webhook.
- People open Chatwoot from the panel: **المحادثات → افتح المحادثات**, or **افتح المحادثة في
  Chatwoot** on a task. A one-time link signs them in: no Chatwoot password.
- «حازم (النظام)» (`hazem@office.invalid`) is the api's own user. Nobody signs in as it.

## Set up (once, and after new tokens)

1. `.env` has `CHATWOOT_SECRET_KEY_BASE`, `CHATWOOT_DB_PASSWORD`, `CHATWOOT_REDIS_PASSWORD`,
   `CHATWOOT_PLATFORM_TOKEN`, `CHATWOOT_BOT_TOKEN`, `CHATWOOT_WEBHOOK_TOKEN` (from `gen-secrets`).
2. `docker compose up -d` (the first start creates Chatwoot's database; a few minutes).
3. `scripts/windows/chatwoot-setup.ps1` (or `scripts/linux/chatwoot-setup.sh`). It prints the
   account and inbox ids, never a token, closes Chatwoot's first-run page, and turns
   `handoff.enabled` on («موظف» hands the client to the staff). Safe to run again.
4. `docker compose up -d api` so the api picks up the tokens. `healthcheck` checks Chatwoot.

## When something is wrong

- **A client's messages do not appear:** `healthcheck` ("no turn waits more than 10 minutes").
  The api retries by itself (30 s, 1 min, 2 min … up to an hour). `docker compose logs api | grep Chatwoot`.
- **A reply did not reach the client:** look for the private note in the conversation; the
  reason is also in `hazem.chatwoot_replies.error` and `hazem.send_results`. A note «مش متأكدين»
  (state `unknown`) means n8n did not answer in time: check `hazem.send_results` for
  `chatwoot:<message id>` before anyone sends it again.
- **Known:** Hazem's replies appear in Chatwoot as Hazem decided them, even if n8n then refused to
  send one (outside the 24 hours, for example).
- **«Invalid webhook URL» in `chatwoot-sidekiq`'s log:** `SAFE_FETCH_ALLOW_PRIVATE_NETWORK` must be
  `true` (Chatwoot calls the api on the Docker network). Review this before TASK 14.

## Backup

`backup` dumps `chatwoot` with `office` and `n8n`. Restoring it is TASK 13 (security and backup):
until then `pg_restore` into a new database on `chatwoot-db` by hand.
