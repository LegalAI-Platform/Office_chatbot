# Panel overhaul report — 2026-10-25 — the panel's look, the appointments page, security and speed

Asked in the chat by Nehad (team member, the panel's UI is his task): a new look for the whole panel, the
session ending after an hour without use (it ended every few seconds), a clearer «المواعيد», then the panel
hardened and made faster. Branch `panel-overhaul` (from `main` = `8de9167`). Architecture v1.3 unchanged: no
new service, no stack change, no migration.

## 1. What was done

| Part | What |
|---|---|
| Session | The Data Protection keys on a volume (`api_keys`, `Panel:KeysPath`): a rebuild no longer signs everyone out. `PanelAuth.IdleTimeout` = 1 hour; the panel signs out after 60 minutes without use (`useIdleSignOut`) |
| The look («Night desk») | Chosen by the team from three mockups. Deep navy, one color per job: gold = the main action and what waits for a person, red = urgent and late, blue = appointments and work in progress, green = done and paid. Dark by default, light in the user menu. A narrow rail with the firm's emblem, a home screen of tiles (what needs you now, the numbers, the day's roll, the week, the month, the team, the pipelines). `design-system/hazem-panel/MASTER.md` rewritten; the old PNG icons and photos removed |
| «المواعيد» | Opens on the bookings: counts, «الميعاد الجاي», a filter by kind and a search, each day with its total, each booking with its time, length, «بعد 40 دقيقة», notes (hold ending, receipt to review, not in the calendar yet, passed and not marked) and its full details. The calendar marks only what is not an ordinary day and opens on today with the day's bookings. The fixed week shows hours per day and per week |
| Security (backend) | Ten tries in 15 minutes per account at the current password or a 2FA code inside a session (`panel-sensitive` limiter); a wrong current password counts towards the lockout; a pending account locks too; a session ends 12 hours after sign-in however active; only `office.manage` may change the lawyer's calendar id; no `Server: Kestrel` header |
| Security (panel and containers) | Stricter CSP (Trusted Types, no workers or media), COEP, CORP, a longer Permissions-Policy (`web/security-headers.inc`, included wherever a location sets a header); request timeouts; api and web read-only, no capabilities, memory and process limits; `no-new-privileges` on every long-running service |
| Speed | Every page its own file (`app/lazyPages.tsx`), the libraries in a separate cached file, `.gz` built once and served by nginx, a year's cache for hashed files; «who is signed in» asked first (`boot.ts` → `askMeEarly`); a hover on a link fetches the page's file and the data it opens on (`prefetchData`); the two main fonts preloaded; nginx keeps connections to the API; logos WebP; data younger than 30 s shown at once |

## 2. Results

| Check | Result |
|---|---|
| `backend-test` | 458/458 (new: lockout through change-password, the per-account limit, the 12-hour session, the calendar id) |
| `web-test` | 39/39 (new: the bookings tab opens first, no «خلص» before a booking starts, its details) |
| `hazem-e2e` | all steps passed |
| `healthcheck` | 128 passed, 0 failed (3 WARN: the owner's keys) |
| Home screen (warm cache) | first paint 335 ms → 133 ms, all its data 420 ms → 133 ms (median of 5); the JS 590 KB → 106 KB on the wire |

## 3. Deviations and notes

- A security review (read-only) of the backend, compose and nginx found nothing critical. What it found that
  needs the owner, not code: **the tunnel exposes all of n8n (its login page too), not only the webhooks** —
  limit the named tunnel to `/webhook/` or put Cloudflare Access in front, and turn on 2FA for the n8n owner;
  `PANEL_SECRET_KEY` empty switches off the managers' 2FA rule; if the panel is ever exposed, nginx must take
  the real client address from the edge and the cookie must be Secure.
- Left for later (low): logout ending the session on the server, atomic attempt counters, the timing
  difference on «نسيت كلمة السر».
- Local demo rows for the screenshots (dummy numbers 20100000008x, not in the repo) make `hazem-e2e` count one
  booking and one receipt too many; it was run without them.

## 4. What the next task needs

The owner's review of this branch; the three owner items above; TASK 12 still waits for the owner's answers.
