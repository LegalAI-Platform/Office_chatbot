// The office's alerts (TASK 07c): the bell in the top bar. Shared: the layout shows them, and a
// person reads them wherever they are.
import { useEffect } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { API_BASE, api } from "./client";
import type { AlertList } from "./types";

export const alertsKey = ["alerts"] as const;

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
 */
export function useAlertStream() {
  const qc = useQueryClient();
  useEffect(() => {
    if (typeof EventSource === "undefined") return;
    const stream = new EventSource(`${API_BASE}/alerts/stream`);
    stream.addEventListener("alerts", () => void qc.invalidateQueries({ queryKey: alertsKey }));
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
