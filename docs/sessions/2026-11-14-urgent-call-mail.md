# 2026-11-14 — v1.110: a paid urgent consultation is one task, and e-mails only when something goes wrong

Mohamed Emad's session. Right after v1.109 went to the test server (with #154), he asked «هو دلوقتي الاستشاره العاجله بتروح ل كام
ايميل ؟؟؟». Worked out from main's code and the server's active accounts (2 lawyers, 1 staff, 1 maintenance): 9 e-mails for one
urgent consultation on the test server, up to 12, because it made 3 tasks (`urgent_request`, `urgent_call_failed`,
`urgent_call_unanswered`), each e-mailed to the lawyers and the staff, then «late» for each. He said «انا حاسس كميه ايميلات كتيره
اوي», approved the proposal («موافق»), asked how many e-mails the rest of the system sends, asked whether the urgent
consultation should send no e-mail at all, and kept the two fallback e-mails («سيبهم»). Branch `feat/urgent-call-mail`
(worktree `K:\wt\urgent-mail`), from `main` at 7fe3b29.

## The rest of the system, measured on the server (the last 7 days, no address printed)

| Source | Last 7 days | To |
|---|---|---|
| The panel's alerts, under today's rules | 17: 9 urgent + 2 late from the one urgent consultation, 3 other late tasks, 3 system problems | whose turn it is; the system's to the maintenance account |
| The panel's account e-mails | about 13: 5 codes (1 verify, 4 reset), password changed, rejections, an e-mail already taken | the person; a new account to the managers |
| The monitoring (Alertmanager) | 4 in its first ~2 hours (the test e-mail and the old tunnel, each FIRING and RESOLVED) | `ALERT_EMAIL_TO` |
| Chatwoot | 0 (its agents' e-mail flag is «assigned to me» only) | the agent |
| n8n, the scripts, Grafana | none send e-mail | |

So the urgent consultation was the one multiplier: 11 of the 17 alert e-mails.

## What was done

| Part | Where |
|---|---|
| `urgent_call`, the paid urgent consultation's one task, opened with the receipt instead of `urgent_request` | migration `0077_urgent_call.sql`, `PendingTask.UrgentCall`, `HazemResponder.SayUrgentPaid` |
| The WhatsApp alert that did not reach the lawyer, and nobody's decision in time, move that task to `urgent_call_failed` / `urgent_call_unanswered`; its urgent alert is made again (a new bell line, one e-mail). None or closed: a task of that type, as before | `BookingStore.UrgentStepAsync`, `UrgentNotices` |
| No task for the manager's refused WhatsApp, nor for a receipt that could not be shown to the presser | `UrgentNotices` |
| A second receipt (after «فيه مشكلة») opens no second task while the call's is open | `ConversationStores` |
| E-mail: nobody while the WhatsApp alert is the lawyer's, the lawyer when it failed, the staff when nobody answered, never «late» for it; the bell as before | `AlertKinds.BellOnly`, `AlertAudience.For`, `PanelAlertStore.UnsentAsync`, `PanelMails` (two new e-mails) |
| The task stays urgent in the panel at every step | `PanelTasks.UrgentTypes`, `web/src/shared/lib/labels.ts` (`URGENT_TYPES`), tasks page, home |
| Docs | `DECISIONS-v1.110.md`, CLAUDE.md, START-HERE |

## Checked

- `dotnet build` (warnings are errors): clean. Unit tests: 883 of 883, with new ones for whose e-mail it is and the two e-mails' words.
- The panel: `tsc --noEmit` clean, vitest 120 of 120.
- The integration tests (the urgent flow end to end, the e-mail routing) were changed or added here and run in the CI only (they need the database).

## Next

- His OK to merge, then to deploy (deploy.sh runs migration 0077 after its backup).
- On the test server the lawyer's number is no test recipient, so his WhatsApp alert always fails there: every test urgent call still sends the
  lawyer's fallback e-mail (2 accounts) and, after 10 minutes, the staff's. Adding his number to the test recipients would stop that; his choice.
