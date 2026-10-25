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

/** The price list as a map, with B-23's 1.4 values while it loads. */
export function priceMap(list: Price[] | undefined): Record<string, number> {
  const map: Record<string, number> = {
    "price.review_min": 1500, "price.review_max": 3000, "price.drafting_from": 3000,
    "price.formal_notice": 2000, "price.written_opinion": 2000,
  };
  for (const p of list ?? []) map[p.key] = p.value;
  return map;
}
