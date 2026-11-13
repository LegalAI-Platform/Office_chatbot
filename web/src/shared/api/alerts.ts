// The office's alerts (TASK 07c): the bell in the top bar. Shared: the layout shows them, and a
// person reads them wherever they are.
import { useEffect } from "react";
import { type QueryClient, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { API_BASE, api } from "./client";
import { dashboardKey } from "./dashboard";
import type { AlertList } from "./types";

export const alertsKey = ["alerts"] as const;

/**
 * v1.106 (the owner: «الداش بورد كلها ريل تايم ؟» … «عايزين كله يشتغل بنفس الطريقه دي»): the pages each area of the
 * office's data asks again when the stream's «data» names it. The areas are the API's PanelAlertStore.DataAreas: a new
 * one goes in both. Only the pages on the screen ask at once; the rest when they open. Plain keys: shared code imports
 * no feature.
 */
const AREAS: Record<string, readonly (readonly string[])[]> = {
  tasks: [dashboardKey, ["tasks"], ["task"], ["report"]],
  appointments: [["booking", "appointments"], ["booking", "month"], ["booking", "preview"], ["urf"], ["report"], ["report-trend"]],
  payments: [["payments"], ["booking", "appointments"], ["booking", "month"], ["services"], ["urf"], ["report"], ["report-trend"]],
  services: [["services"], ["payments"], ["report"], ["report-trend"]],
  urf: [["urf"], ["payments"]],
  clients: [["report"], ["ai-cost"]],
  ai: [["report"], ["ai-cost"]],
  lists: [["lists"]],
  muted: [["muted"]],
  texts: [["texts"], ["text"]],
  settings: [["prices"], ["price-changes"], ["booking", "settings"], ["booking", "office"], ["booking", "preview"]],
  booking: [["booking", "week"], ["booking", "changes"], ["booking", "month"], ["booking", "preview"]],
  users: [["users"], ["staff"]],
};

/** Asks again what the named areas show, each query once (a second ask would cancel the first one's answer). */
function refreshAreas(qc: QueryClient, names: string) {
  const prefixes = names.split(",").flatMap((area) => AREAS[area] ?? []);
  if (prefixes.length === 0) return;
  void qc.invalidateQueries({ predicate: (q) => prefixes.some((p) => p.every((part, i) => q.queryKey[i] === part)) });
}

export function useAlerts() {
  return useQuery({
    queryKey: alertsKey,
    queryFn: () => api<AlertList>("GET", "/alerts"),
    // the fallback since v1.104: the stream below is what brings a new alert at once
    refetchInterval: 60_000,
    refetchIntervalInBackground: true, // an urgent request must reach a person who is in another tab (shared/lib/notify)
    staleTime: 30_000,
  });
}

/**
 * v1.104 (the owner: «ريل تايم»): the API's stream says the moment an alert is made or read, and the bell asks for its
 * alerts again. A hidden tab's timers run at most once a minute in Chrome, but not this.
 * - The stream carries no alert, only «something changed»; /alerts keeps who sees what.
 * - The browser opens it again by itself: after the API ends it every 5 minutes, after a deploy, after a broken line.
 * - The minute's ask stays as the fallback.
 * - v1.106: `data` names the areas of the office's data that changed (`tasks,payments`; the API looks every 3 seconds,
 *   whoever changed them), and the pages on the screen that show them ask again (AREAS above).
 */
export function useAlertStream() {
  const qc = useQueryClient();
  useEffect(() => {
    if (typeof EventSource === "undefined") return;
    const stream = new EventSource(`${API_BASE}/alerts/stream`);
    stream.addEventListener("alerts", () => void qc.invalidateQueries({ queryKey: alertsKey }));
    stream.addEventListener("data", (e) => refreshAreas(qc, String(e.data)));
    return () => stream.close();
  }, [qc]);
}

/** Marks these alerts read; without ids, every alert the person may see. */
export function useMarkAlertsRead() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (ids?: number[]) => api<void>("POST", "/alerts/read", { ids: ids ?? null }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: alertsKey }),
  });
}
