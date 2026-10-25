// TASK 06 (DECISIONS v1.17, B-23): /api/panel/prices. Read by «الأسعار» and by the pricing of a written service (its ranges),
// so it lives here: one feature never imports another.
import { useQuery } from "@tanstack/react-query";
import { api } from "./client";

export interface Price { key: string; name: string; value: number; default: number }
export interface PriceChange { at: string; by: string | null; key: string; from: string | null; to: string | null }

export const prices = {
  list: () => api<Price[]>("GET", "/prices"),
  changes: () => api<PriceChange[]>("GET", "/prices/changes"),
  save: (values: Record<string, number>) => api<void>("PUT", "/prices", { values }),
};

export function usePrices() {
  return useQuery({ queryKey: ["prices"], queryFn: prices.list, staleTime: 60_000 });
}

/** The price list as a map (key → value). */
export function priceMap(list: Price[]): Record<string, number> {
  return Object.fromEntries(list.map((p) => [p.key, p.value]));
}
