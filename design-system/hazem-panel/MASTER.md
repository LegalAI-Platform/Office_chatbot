# Hazem panel — design system (MASTER)

Approved by the owner on 2026-10-23 (preview: https://claude.ai/artifact/5fG18ydEt5Ei56NRTxB93z).
Source of truth for TASK 07a and every later panel task (07b, 07c, 08, 09). The tokens live in
`web/src/styles.css`; this file explains them. Page-specific rules go in `pages/<page>.md`.

## Direction

An internal tool for a law office in Cairo, used mostly on a phone. Calm, trustworthy, Arabic
first (RTL), with **iOS Liquid Glass** (owner's request) used where Apple uses it: navigation
and controls. Content sits on calmer, nearly solid surfaces so it is always readable.

## Color

| Token | Light | Dark | Use |
|---|---|---|---|
| `--ink` | `#14233D` court navy | `#E8EDF6` | text, primary buttons |
| `--ink-soft` | `#4A5876` | `#A9B4C9` | secondary text |
| `--brass` / `--brass-ink` | `#A67C2E` / `#6E4F12` | `#D3A851` / `#E2BE72` | the office seal: focus ring, the seal mark, the quote rule. **Rare.** |
| `--page` | `#EEF1F6` cool paper | `#0E1626` | background (never cream) |
| `--wall-1..3` | blue, sand, sea-green | deep variants | the soft colors behind the glass |
| state | ok / warn / bad / info | lighter in dark | task states only, **always with a word** |

Task states: open = info (blue), in progress = ok (green), overdue = bad (red, plus a red edge
on the card), urgent = warn (amber), done / cancelled = muted.

## Type

- **Amiri** (Naskh, the feel of legal papers): page titles and the client's own words.
- **IBM Plex Sans Arabic**: everything else.
- Both OFL, bundled from `@fontsource` (no request leaves the machine). Base 15px, line height 1.6.
- Digits 0-9 (as in DECISIONS), `tabular-nums` for times and counts. Times in Africa/Cairo.

## Liquid Glass rules

1. Glass (`.glass`: blur 22px + saturate 170%, a thin light edge, a soft shadow, radius 28px)
   **only** for the top bar, the tab bar, the side rail, sheets and the login card.
2. Content cards use `.surface` (80% opaque) — never a blur per card in a long list (phones).
3. Text on glass keeps 4.5:1 contrast in light and dark.
4. `prefers-reduced-transparency` → solid surfaces, no blur. `prefers-reduced-motion` → no motion.
5. Radii by role: sheets 28px, cards 20px, controls 14px, chips round.

## Layout

- Phone first: top glass bar, content, floating glass tab bar (safe-area aware).
- Computer (≥1024px): glass side rail, content max 64rem, tasks as a table.
- Touch targets ≥44px; visible focus ring in brass; every state (loading, empty, error) in Arabic.

## Icons

One family of line icons drawn in `web/src/shared/ui/Icon.tsx` (24px grid, 2px stroke). No icon
library, no emoji. Icons that point along the reading direction flip in RTL.

## Words

Plain Egyptian Arabic, sentence case, the action named on the button («هاشتغل عليها», «خلصت»).
Errors say what happened and what to do. Never internal codes (no «07b» in the interface).
