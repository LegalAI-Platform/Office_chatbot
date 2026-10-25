// Light or dark: «Night desk» is dark unless the person picks light (kept on this device only).
import { useSyncExternalStore } from "react";

export type Theme = "light" | "dark";
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

export const currentTheme = (): Theme => stored() ?? "dark";

/** Puts the theme on <html> (the stored choice, else dark). Called once before the first render. */
export function applyStoredTheme() {
  document.documentElement.dataset.theme = currentTheme();
}

export function setTheme(t: Theme) {
  try {
    localStorage.setItem(KEY, t);
  } catch {
    // private window: the choice lasts until the page closes
  }
  document.documentElement.dataset.theme = t;
  listeners.forEach((l) => l());
}

export function useTheme(): Theme {
  return useSyncExternalStore(
    (l) => {
      listeners.add(l);
      return () => listeners.delete(l);
    },
    () => (document.documentElement.dataset.theme as Theme | undefined) ?? currentTheme(),
  );
}
