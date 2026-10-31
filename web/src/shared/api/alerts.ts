// The office's alerts (TASK 07c): the bell in the top bar. Shared: the layout shows them, and a
// person reads them wherever they are.
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./client";
import type { AlertList } from "./types";

export const alertsKey = ["alerts"] as const;

export function useAlerts() {
  return useQuery({
    queryKey: alertsKey,
    queryFn: () => api<AlertList>("GET", "/alerts"),
    refetchInterval: 60_000,
    refetchIntervalInBackground: true, // an urgent request must reach a person who is in another tab (shared/lib/notify)
    staleTime: 30_000,
  });
}

/** Marks these alerts read; without ids, every alert the person may see. */
export function useMarkAlertsRead() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (ids?: number[]) => api<void>("POST", "/alerts/read", { ids: ids ?? null }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: alertsKey }),
  });
}
