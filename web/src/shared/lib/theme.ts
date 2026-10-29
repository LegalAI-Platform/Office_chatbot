// Light, dark, or the device's choice (kept on this device only). A change spreads from the button that was pressed
// as a growing circle of the new look (the owner's video, 2026-10-29), where the browser can draw it.
import { useSyncExternalStore } from "react";
import { flushSync } from "react-dom";

export type Theme = "light" | "dark";
/** What the person picked: one look, or «زي الجهاز» (follow the device). */
export type ThemeChoice = Theme | "system";
const KEY = "hazem-theme";
const listeners = new Set<() => void>();

function stored(): Theme | null {
  try {
    const v = localStorage.getItem(KEY);
    return v === "light" || v === "dark" ? v : null;
  } catch {
    return null;
  }
}

const system = (): Theme =>
  typeof matchMedia === "function" && matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";

export const currentTheme = (): Theme => stored() ?? system();

/** Puts the stored choice on <html> (none = follow the device). Called once before the first render. */
export function applyStoredTheme() {
  const t = stored();
  if (t) document.documentElement.dataset.theme = t;
  else delete document.documentElement.dataset.theme;
}

function apply(choice: ThemeChoice) {
  try {
    if (choice === "system") localStorage.removeItem(KEY);
    else localStorage.setItem(KEY, choice);
  } catch {
    // private window: the choice lasts until the page closes
  }
  if (choice === "system") delete document.documentElement.dataset.theme;
  else document.documentElement.dataset.theme = choice;
  // inside the transition's snapshot: the menu shows the new choice in the same frame as the page
  flushSync(() => listeners.forEach((l) => l()));
}

/**
 * Changes the look. With `from` (the pressed button) the new look grows as a circle from its middle until it covers
 * the screen; with no support for it, with «reduce motion» on, or when the look does not really change, it is instant.
 */
export function setTheme(choice: ThemeChoice, from?: HTMLElement | null) {
  const changes = (choice === "system" ? system() : choice) !== currentTheme();
  const still = typeof matchMedia === "function" && matchMedia("(prefers-reduced-motion: reduce)").matches;
  if (!from || !changes || still || typeof document.startViewTransition !== "function") {
    apply(choice);
    return;
  }
  const box = from.getBoundingClientRect();
  const x = box.left + box.width / 2;
  const y = box.top + box.height / 2;
  // far enough to reach the farthest corner of the window
  const r = Math.hypot(Math.max(x, innerWidth - x), Math.max(y, innerHeight - y));
  const t = document.startViewTransition(() => apply(choice));
  t.ready.then(() => {
    document.documentElement.animate(
      { clipPath: [`circle(0px at ${x}px ${y}px)`, `circle(${r}px at ${x}px ${y}px)`] },
      { duration: 450, easing: "cubic-bezier(0.4, 0, 0.2, 1)", pseudoElement: "::view-transition-new(root)" },
    );
  }).catch(() => undefined);
}

/** The look on screen now (the device's when nothing was picked). */
export function useTheme(): Theme {
  return useSyncExternalStore(subscribe, () => (document.documentElement.dataset.theme as Theme | undefined) ?? system());
}

function subscribe(l: () => void) {
  listeners.add(l);
  const mq = typeof matchMedia === "function" ? matchMedia("(prefers-color-scheme: dark)") : null;
  mq?.addEventListener("change", l);
  return () => {
    listeners.delete(l);
    mq?.removeEventListener("change", l);
  };
}
