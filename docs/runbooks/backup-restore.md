# Backup and restore (TASK 13, DECISIONS v1.18 T-15)

## What is backed up

| What | Where in `office-backups` | Kept |
|---|---|---|
| The office database (clients, tasks, appointments, payments, texts, the panel's accounts) | `postgres/office/office_<ts>.dump.enc` | 30 days |
| n8n's database (workflows, credentials encrypted with `N8N_ENCRYPTION_KEY`, the WhatsApp log) | `postgres/n8n/…` | 30 days |
| Chatwoot's database (the staff's conversations, a second copy of the clients' messages) | `postgres/chatwoot/…` | 30 days |
| The client files: receipts, IDs, contracts, documents, voice notes, delivered files (`office-media`, `office-sensitive`) | `files/objects/<key id>/<sha256>.enc` (each file once) + `files/manifests/manifest_<ts>_<key id>.json.enc` (which file was where that night) | a deleted file: 30 days |

Everything is encrypted with **`BACKUP_ENCRYPTION_KEY`** (AES-256, PBKDF2). **Without the key nothing can be read, by
anyone, us included.** Keep a copy outside this machine: a password manager, or on paper in the office's safe. To see
it, open `.env` (on this machine only) and copy the `BACKUP_ENCRYPTION_KEY=` line. Never send it in a chat or an e-mail.

The backups run every night at 03:00 (`scripts/windows/nightly.ps1`, README «Nightly job»), with the healthcheck, the
log scan and, once a month, a **restore drill** (every database restored into a throwaway one, every file decrypted
and checked). A failure reaches the managers in the panel's bell and by e-mail.

## Commands

```powershell
scripts\windows\backup.ps1                                   # a backup now
scripts\windows\restore.ps1 list                             # what there is
scripts\windows\restore.ps1 office latest --drop-after-verify   # a restore test: restored, checked row by row, dropped
docker compose run --rm tools restore-drill                  # the monthly drill, now
scripts\windows\backup-export.ps1 -To E:\OfficeBackups       # a copy off this disk (still encrypted)
```

`restore` never overwrites a live database or a file the office has. A database goes into a new one
(`office_restore_<date>`), and its row counts are compared with the live one (`hazem.ops_runs`, the jobs' own log,
is left out of the comparison). A small difference on n8n is normal: it writes all day.

## A full restore after a disaster (the disk died)

You need: the repository, a `.env` with the **same** secrets (`BACKUP_ENCRYPTION_KEY`, `N8N_ENCRYPTION_KEY` and the
rest; a copy of `.env` from `.local/` or the password manager), and the backups (`office-backups`, or a folder
`backup-export` made).

1. Start the stack empty: `scripts\windows\up.ps1`, then `migrate.ps1 up` and `docker compose run --rm tools storage-init`.
2. If the backups come from an export, copy them back into `office-backups` under the same paths
   (`postgres/<db>/…`, `files/…`).
3. Restore each database into a new one and check it:
   `restore.ps1 office latest office_restored` (and `n8n`, `chatwoot`).
4. Swap it in (the stack stopped except postgres): rename the empty `office` to `office_empty` and `office_restored`
   to `office` (`ALTER DATABASE … RENAME TO …` as the superuser). Chatwoot's on `chatwoot-db` the same way. `restore`
   already gave the new database the live one's owner and CONNECT rights, so every service connects as before.
5. The files: `restore.ps1 files latest --into-live` puts back every file the office no longer has.
6. Start everything, run the healthcheck, open the panel.

## When something fails

| Alert / FAIL | What to do |
|---|---|
| «النسخة الاحتياطية فشلت» | `docker compose run --rm tools backup` again and read where it stops (`failed_at_…` in `hazem.ops_runs.error_code`). Usually Docker or Garage was down at 03:00 |
| «تجربة الاسترجاع فشلت» | `docker compose run --rm tools restore-drill` and read the line that fails. A `cannot be decrypted` means `.env`'s key is not the one the backups were made with |
| «الفحص اليومي لقى حاجة مش شغالة» | `scripts\windows\healthcheck.ps1`, look at the `FAIL` lines |
| The healthcheck says the backup is older than 26 hours | The PC or Docker was off at 03:00: run `nightly.ps1` by hand |
