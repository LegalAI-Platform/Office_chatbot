# Chat history (cleaned)

Claude Code conversations behind this project, exported on 2026-10-22, so another Claude account
or person can read how decisions were reached. **This repository is public**, so each export was
cleaned before it was added:

- the content of every tool call that touched `exports/` (raw chat exports with personal data,
  see `CLAUDE.md`) was replaced by `[removed for the public repository …]`;
- Egyptian mobile numbers and e-mail addresses are masked (`01x******nn`, `***@***`), except the
  dummy test numbers `2010000000xx` and the documentation example `201001234567`;
- nothing else was changed. No `.env` secret is in either file (checked before upload).

| File | Session |
|---|---|
| `2026-10-22-main-chat-al-shat-al-harfi.zip` | «الشات الحرفي بكل التفاصيل»: TASK 00–02, the first .NET attempt |
| `2026-10-22-side-chat-backend-and-tasks.zip` | «هو انت هتعمل ال باك اند ب اي ؟؟»: back to n8n, v1.3 (n8n + .NET), TASK 03, v1.4, TASK 04 prompt |

Each zip holds `transcript.jsonl` (one JSON object per message) and the session metadata.
The decisions themselves live in `docs/decisions/` and the summaries in `docs/sessions/`;
read those first. The raw, uncleaned exports stay only on the owner's machine.
