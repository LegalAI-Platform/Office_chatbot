// Who is signed in. Shared because the layout and every feature need it (features never import
// each other). Server state lives in TanStack Query only.
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
