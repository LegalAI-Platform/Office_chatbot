# Hazem panel — design system (MASTER)

The source of truth for every panel page. The tokens live in `web/src/styles.css`, the shared parts
in `web/src/shared/ui/` (`ui.tsx`, `Icon.tsx`, `Brand.tsx`); this file explains them.
Replaces the 2026-10-23 sheet (black and gold, stock photos, PNG icons) and the 2026-10-25 «Dossier» and
glass rounds. Chosen by the team on 2026-10-25 from three mockups: «Night desk».

## Direction

A command center on dark navy with the firm's own **gold** (from its emblem). Four colors, each with one job:
gold for the main action and what waits for a person, red for urgent and late, blue for appointments and
work in progress, green for done and paid; everything else navy and its greys. Color goes on numbers,
bars, edges, chips and small washes, never on large solid blocks. Easy first: the home screen says what to do next and every row has its one action.

## Panels

- One kind of panel everywhere: `.surface` (also `.liquid`, `.liquid-tint`, older names): a solid card,
  a 1px line, radius 16px. No blur, no gradients, no glow (fast on every machine).
- `.pop` (menus, the bell, dialogs, toasts): solid card with a deeper shadow. `.glass-bar` (top bar, phone tab
  bar) and `.glass-side` (the rail) are solid too, despite their names.
- The theme lives on `<html data-theme>`: dark unless the person picks light in the user menu (kept on
  the device). Light is the same desk on paper white; the rail stays navy.

## Color

| Role | Use | Dark / light |
|---|---|---|
| navy | page, cards, the rail (a deep blue navy) | `#091330` `#101C3E` / `#EDF1FB` `#FFFFFF`, rail `#050C26` / `#0A1A48` |
| gold (`accent`, `gold`, `saffron`) | main button, links, key numbers, waiting for a person, the now line | `#F7B733` / `#9A6200` (marks `#D99A0F`) |
| red (`coral`) | urgent, late, failed | `#FF5A45` / `#E3341D` |
| blue (`cobalt`) | appointments, in progress, information | `#4D8DFF` / `#2563EB` |
| green (`jade`) | done, paid, on time, all clear | `#2FD08A` / `#0FA968` |
| quiet (`orchid`) | anything without a job of its own | blue grey `#7D93CC` / `#6C80B5` |

The old folder names stay in code so the pages did not change; each now names a role (see `styles.css`
and `shared/ui/folders.ts`). A number takes its job's color (blue appointments, gold waiting, green done) and turns red when a pressing one is above zero.

## Type

IBM Plex Sans Arabic for everything (400–700), numbers tabular. Base 15px.

## The home screen (one screen, in order)

1. The date in gold, the greeting, and **quick actions** (the urgent tasks as the gold button, then the
   receipts, the appointments, the chats).
2. **«محتاج منك دلوقتي»** with its count large in gold: urgent and late tasks, receipts, services waiting for
   a price and Urfi papers, one list, most pressing first, filtered by kind, each row with its action.
   Beside it, the **numbers** as tiles (2 columns), 7-day trend lines for urgent and late.
3. The day's roll across the full width: blue blocks, red for an urgent consultation, a gold ring on
   the next one, hatching while the receipt is awaited, a gold now line.
4. The week ahead (blue bars, today full blue), this month and the team (green for done and received),
   what happened lately, and the written services (gold) and Urfi (blue) pipelines, deeper per stage.

## The rail

A narrow navy rail on the reading start: the firm's emblem, then each section as an icon with its short
name under it, groups split by a fine line, «الإعدادات» at the bottom (lit for its sub-pages). Counts
sit on the icon: urgent + late tasks in red, receipts in gold. The current page has a lighter navy
block and a gold icon.

## Never

- Letters in circles (no initials avatars): people are named in words. The firm's emblem is the logo.
- Large solid color blocks, folder tabs, icons in colored boxes, rainbow stripes, stock photos,
  gradients, emoji, PNG icons.

## Shape and depth

- Radii by role: controls 10px, cards and tiles 16px, sheets 18px, chips round.
- Borders do the work. No shadow on panels in dark; a hairline one in light. Only things that float
  (menus, the bell, dialogs) use `.pop`.

## Parts (`shared/ui/ui.tsx`)

| Part | Rule |
|---|---|
| `PageHeader` | every page: title, one line, actions |
| `Button` | `primary` = one per view; `secondary`, `ghost`, `danger`; `size="sm"` in rows and toolbars |
| `Tabs` | sections of one page (role tablist, underlined) |
| `Segmented` | filters on a list (aria-pressed), e.g. the tasks' «مين» and «الحالة» |
| `Field`, `inputClass`, `FileInput` | fields; the file picker speaks Arabic (never «No file chosen») |
| `Panel` / `Card` | a card with a header row / a plain card. Lists inside are divided rows, not cards in cards |
| `Chip` | a status word with a dot on its tint |
| `Empty`, `Loading`, `ErrorState` | every state in plain Arabic; an empty list says what will appear and when |

## Records and feedback (seamless by default)

- **`shared/ui/caseCard.tsx`**: every record (receipt, service order, Urfi case) is a `CaseCard`: an edge for
  its state (gold when it waits for a person, red when it failed, blue in progress, green done, grey otherwise), `CaseHead` (the person, a tinted kind word, meta with icons, the state chip),
  `Facts` (small boxes), `Files` (the client's documents as chips), `CaseNote` (what happens next, or a
  message that did not reach the client), and an `ActionBar` at the bottom. A list page opens with a
  `Summary` of counts by state.
- **`shared/ui/feedback.tsx`**: `toast()` after every action that worked; `confirmAction()` before anything
  the client will see or that cannot be undone, in the panel's own dialog (never `window.confirm`).
- **`shared/lib/url.ts`**: a page's tab or filter lives in the address (`useUrlState`), so refresh, back
  and shared links land on the same view.
- Pages arrive with a short fade (`.page-in`); dialogs and toasts slide in; all motion is off for
  `prefers-reduced-motion`.

## Layout

- Computer (≥1024px): the rail on the reading start, a top bar (search, date, bell, person), content max 92rem.
- Phone: a top bar with the mark, content, a five-tab bar (safe-area aware). Numbers sit 2×2.
- Touch targets ≥40px (≥44px for the tab bar); a visible focus ring in the accent;
  `prefers-reduced-motion` turns transitions off.

## Icons

One family of line icons in `Icon.tsx` (24px grid, 1.75px stroke, round caps), colored by
`currentColor`. No icon library, no emoji, no PNG icons. Icons that point along the reading direction
flip in RTL.

## Words

Plain Egyptian Arabic, sentence case, the action named on the button («هاشتغل عليها», «خلصت»).
Errors say what happened and what to do. Never internal codes in the interface: no task numbers
(«07b»), no `.env`, no `migration 0008`, no keys where a person reads.
