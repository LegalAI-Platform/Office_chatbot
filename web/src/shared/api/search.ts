// v1.110 (the owner: «السيرش بار هنا حاسه بيسرش غلط» … «اعمل بحث شامل»): the search bar's one search: the clients, their
// appointments and the tasks, as it is typed. The term goes in the body (POST), never in the address: a number typed there
// would stay in the browser's history and the edge's log.
import { useQuery } from "@tanstack/react-query";
import { api } from "./client";
import type { SearchResults } from "./types";

/** Fewer letters than this is no search yet (a whole office starts with one letter). */
export const SEARCH_MIN = 2;

export function useSearch(term: string) {
  const q = term.trim();
  return useQuery({
    queryKey: ["search", q],
    queryFn: () => api<SearchResults>("POST", "/search", { q }),
    enabled: q.length >= SEARCH_MIN,
    staleTime: 10_000,
    placeholderData: (previous) => previous, // the last results stay while the next ones come
  });
}
