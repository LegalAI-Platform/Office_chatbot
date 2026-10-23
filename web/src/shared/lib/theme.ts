// Light or dark: the device's choice unless the person picks one (kept on this device only).
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

const system = (): Theme =>
  typeof matchMedia === "function" && matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";

export const currentTheme = (): Theme => stored() ?? system();

/** Puts the stored choice on <html> (none = follow the device). Called once before the first render. */
export function applyStoredTheme() {
  const t = stored();
  if (t) document.documentElement.dataset.theme = t;
  else delete document.documentElement.dataset.theme;
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
      const mq = typeof matchMedia === "function" ? matchMedia("(prefers-color-scheme: dark)") : null;
      mq?.addEventListener("change", l);
      return () => {
        listeners.delete(l);
        mq?.removeEventListener("change", l);
      };
    },
    () => (document.documentElement.dataset.theme as Theme | undefined) ?? system(),
  );
}
