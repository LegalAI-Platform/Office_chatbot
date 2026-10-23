// The home screen's numbers. Shared because the side bar and the bell show them too.
import { useQuery } from "@tanstack/react-query";
import { api } from "./client";
import type { Dashboard } from "./types";

export const dashboardKey = ["dashboard"] as const;

export function useDashboard() {
  return useQuery({
    queryKey: dashboardKey,
    queryFn: () => api<Dashboard>("GET", "/dashboard"),
    refetchInterval: 60_000,
    staleTime: 30_000,
  });
}
