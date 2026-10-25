// Who is signed in. Shared because the layout and every feature need it (features never import
// each other). Server state lives in TanStack Query only.
import { useEffect, useRef } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError, read, resetCsrf, takeEarlyMe } from "./client";
import type { Me, Permission, SecondStep } from "./types";

export const meKey = ["me"] as const;

export function useMe() {
  return useQuery<Me | null>({
    queryKey: meKey,
    queryFn: async () => {
      try {
        // the answer src/boot.ts already asked for while the code loaded (once; later checks ask again)
        const res = await takeEarlyMe();
        return res ? await read<Me>(res) : await api<Me>("GET", "/auth/me");
      } catch (e) {
        if (e instanceof ApiError && e.status === 401) return null;
        throw e;
      }
    },
    staleTime: 60_000,
    retry: false,
  });
}

export const can = (me: Me | null | undefined, permission: Permission): boolean =>
  !!me && me.permissions.includes(permission);

/** The account, or the second step when the authenticator is on (no session until the code). */
export async function login(username: string, password: string): Promise<Me | SecondStep> {
  resetCsrf();
  const r = await api<Me | SecondStep>("POST", "/auth/login", { username, password });
  resetCsrf();
  return r;
}

export const isSecondStep = (r: Me | SecondStep): r is SecondStep => "ticket" in r;

export async function loginSecondStep(ticket: string, code: string): Promise<Me> {
  const me = await api<Me>("POST", "/auth/login/2fa", { ticket, code });
  resetCsrf();
  return me;
}

/** An hour without a mouse, key or touch in any tab signs out (the 60-second refreshes do not count). */
export const IDLE_LIMIT_MS = 60 * 60_000;
const LAST_ACTIVITY_KEY = "hazem.last_activity";

export function useIdleSignOut(active: boolean, onSignedOut: () => void) {
  const logout = useLogout();
  const latest = useRef({ logout, onSignedOut });
  latest.current = { logout, onSignedOut };

  useEffect(() => {
    if (!active) return;
    let last = Date.now();
    // Shared between tabs, so working in one tab keeps the others signed in too.
    const lastSeen = (): number => {
      try {
        return Math.max(last, Number(localStorage.getItem(LAST_ACTIVITY_KEY)) || 0);
      } catch {
        return last;
      }
    };
    const touch = () => {
      const now = Date.now();
      if (now - last < 5_000) return;
      last = now;
      try {
        localStorage.setItem(LAST_ACTIVITY_KEY, String(now));
      } catch {
        // private window: this tab still counts its own activity
      }
    };
    let done = false;
    const check = async () => {
      if (done || Date.now() - lastSeen() < IDLE_LIMIT_MS) return;
      done = true;
      await latest.current.logout();
      latest.current.onSignedOut();
    };
    const onVisible = () => { if (document.visibilityState === "visible") void check(); };

    touch();
    const events = ["pointerdown", "keydown", "wheel", "touchstart"] as const;
    events.forEach((e) => window.addEventListener(e, touch, { passive: true }));
    document.addEventListener("visibilitychange", onVisible);
    const timer = setInterval(() => void check(), 30_000);
    return () => {
      events.forEach((e) => window.removeEventListener(e, touch));
      document.removeEventListener("visibilitychange", onVisible);
      clearInterval(timer);
    };
  }, [active]);
}

export function useLogout() {
  const qc = useQueryClient();
  return async () => {
    try {
      await api<void>("POST", "/auth/logout");
    } finally {
      resetCsrf();
      qc.clear();
      qc.setQueryData(meKey, null);
    }
  };
}
