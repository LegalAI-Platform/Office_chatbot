# TASK 13 — الأمان والنسخ الاحتياطي (DECISIONS v1.17, T-15 / T-23 / T-24)

Branch `task13-security-backup` from `main` (`8ab5cd4`, after the security hardening of PR #18). Read DECISIONS v1.17
T-15 (backups), T-23 (accounts), T-24 (Chatwoot), the TASK 07b / 07c / 12 reports, `docs/infrastructure.md`,
`infra/tools/bin/backup` and `restore`, and PR #14's report (the tunnel note).

## What is there today, and the gaps

- `backup` dumps the office, n8n and Chatwoot databases to `office-backups` (Garage) and keeps 30 days. **The client
  files in storage (`office-media`, `office-sensitive`: receipts, IDs, contracts, voice notes) are not backed up.**
- `restore` restores office or n8n into a new database and checks it. **Not Chatwoot, not the files.** No restore has
  been tried on a schedule.
- The backups are **not encrypted** and sit **on the same disk** as the system (K:).
- The tunnel (Quick and Named) points at **all of n8n**: its login page and API are on the internet, not only the
  WhatsApp webhook.
- A failed n8n workflow alerts by Telegram only if a bot is set up (it is not). A failed backup or healthcheck tells
  nobody.
- Keys passed through the chat (OpenAI, Google) are still to be replaced; there is no written way to rotate a secret.

## Owner's decisions (the chat, «زي ما هي»: the 8 proposals as written, DECISIONS v1.18)

| # | Proposal |
|---|---|
| 1 | **The files join the backup:** every night `office-media` and `office-sensitive` are copied into `office-backups` (only what changed); a deleted file stays 30 days. `restore` brings files back too |
| 2 | **Backups encrypted:** every dump and file copy is encrypted with a key of its own (`BACKUP_ENCRYPTION_KEY`, made by `gen-secrets`). The owner keeps a copy of the key **outside the machine** (a password manager or on paper): without it no backup can be read, by anyone |
| 3 | **Restore covers Chatwoot and the files**, and **a restore drill runs by itself once a month** (into a throwaway database, checked, then dropped). The healthcheck fails when the last drill is older than 35 days or failed |
| 4 | **A copy off this disk:** a `backup-export` command copies the latest backups (still encrypted) to a folder the owner names, e.g. an external drive. The automatic copy to the cloud (R2) is TASK 14 |
| 5 | **The tunnel shows only the WhatsApp webhook:** a small nginx (`edge`) between the tunnel and n8n lets through only Meta's webhook path; n8n's login page and API answer 404 from the internet. The owner also turns on 2FA for n8n's own account |
| 6 | **System alerts:** a failed backup, a failed healthcheck or a failed n8n workflow → the panel's bell for the managers (a new kind «مشكلة في السيستم») and an e-mail from the no-reply sender, with no client data. Telegram stays optional |
| 7 | **Secrets:** `docs/runbooks/secrets.md` says how to replace each key (OpenAI, Google, Meta, the databases, `PANEL_SECRET_KEY`, the backup key), and the healthcheck scans the last day of the API's and n8n's logs for anything that looks like a token or a full phone number |
| 8 | **One nightly job at 03:00:** backup → files → healthcheck → an alert if anything failed. The owner adds it to Windows' Task Scheduler with one command from the README |

## What to build

1. **`infra/tools/bin/backup`**: the file copy (1), encryption (2), the run's result recorded (`hazem.ops_runs`: kind,
   started, finished, ok, size, error code — never a file name).
2. **`restore`**: Chatwoot and the files (into a new database / a new bucket prefix, never over the live ones), the
   decryption; **`restore-drill`** (3); **`backup-export <folder>`** (4).
3. **Migration 0019**: `hazem.ops_runs`; alert kind `system` (no task); grants (the tools container writes as
   `office_migrator`, the API reads).
4. **The API**: the bell shows `system` alerts to the managers only; the mailer sends them (no client data). The
   alert worker reads `ops_runs` and n8n's error log.
5. **`edge`** (nginx, read-only, no capabilities) in front of n8n for both tunnel profiles (5); the tunnel's target
   becomes `http://edge:8081`.
6. **Healthcheck**: the drill's age, the last night's job, the edge (a non-webhook path answers 404 through it), the
   log scan (7).
7. **Docs**: `docs/runbooks/backup-restore.md` (a full restore step by step, the key), `docs/runbooks/secrets.md`,
   README (the nightly job), `env-variables.md`, DECISIONS v1.18, the session report.
8. **Tests**: backup → restore round trip with files and Chatwoot (a throwaway database), a wrong key fails clearly,
   the edge blocks everything but the webhook, `system` alerts reach managers only and carry no client data, the
   log scan catches a planted token. Healthcheck, hazem-e2e.

## Not in this task

The domain and HTTPS, R2 and its automatic copy, `SAFE_FETCH_ALLOW_PRIVATE_NETWORK` (TASK 14); Meta (TASK 05b); the
panel's new look (PR #14); replacing the keys themselves (the owner, with the new runbook).
