# web — the panel (TASK 07a)

React + TypeScript (Vite), Arabic only (RTL), phone first, iOS Liquid Glass for navigation and
controls (DECISIONS v1.8 T-11, design: [design-system/hazem-panel/MASTER.md](../design-system/hazem-panel/MASTER.md)).
It talks to the .NET API's `/api/panel` only ([docs/panel-api.md](../docs/panel-api.md)).

## Run, build, test

| What | How |
|---|---|
| In Docker (normal) | `up` starts `web` on http://127.0.0.1:8080 (nginx serves the build and forwards `/api/panel` to the API) |
| Tests without Node on the machine | `scripts/windows/web-test.ps1` / `scripts/linux/web-test.sh`: type-check, Vitest, build and bundle budget in a Node container |
| With Node 24 on the machine | `npm ci`, then `npm test`, `npm run build`, or `npm run dev` (Vite on :5173, forwarding `/api/panel` to the running `web` container) |

`npm run build` fails when the gzipped JS passes 150 KB or the CSS passes 15 KB
(`tools/check-bundle.mjs`). Fonts are bundled from `@fontsource` (no request leaves the machine).

## Structure (frontend-architecture)

```text
src/
  app/          routing, the sign-in guard, the layout (glass top bar, tab bar, side rail)
  features/
    auth/       login, change password, my account
    tasks/      task list and task detail (S-05)
    users/      accounts (the manager only)
  shared/
    api/        the one HTTP client (cookie, antiforgery, Arabic errors), the session, the types
    ui/         buttons, fields, chips, states, the line icons
    lib/        Arabic labels for codes, Cairo time
```

Rules (checked by `src/architecture.test.ts`): a feature never imports another feature; `shared`
never imports a feature or `app`; only `shared/api/client.ts` calls `fetch`. Server data lives in
TanStack Query only.

## Adding a page

1. A folder under `src/features/<name>/` with its `api.ts` (queries) and its page.
2. A route in `src/app/App.tsx`; behind `RequirePermission` if it needs a permission.
3. A link in `src/app/Layout.tsx` only for people who have the permission (the API refuses the
   others anyway: the panel hides, the API decides).
4. Words: plain Egyptian Arabic, the action on the button, errors that say what to do.
