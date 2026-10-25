// A page's tab or filter kept in the address (?show=refunds), so a refresh, the back button or a shared
// link lands on the same view. The default value is left out of the address.
import { useSearchParams } from "react-router";

export function useUrlState<T extends string>(key: string, fallback: T, allowed: readonly T[]) {
  const [params, setParams] = useSearchParams();
  const raw = params.get(key) as T | null;
  const value = raw !== null && allowed.includes(raw) ? raw : fallback;
  const set = (v: T) =>
    setParams((p) => {
      const next = new URLSearchParams(p);
      if (v === fallback) next.delete(key);
      else next.set(key, v);
      return next;
    }, { replace: true });
  return [value, set] as const;
}
