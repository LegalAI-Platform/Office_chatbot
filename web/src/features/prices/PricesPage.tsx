import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { errorMessage } from "../../shared/api/client";
import { prices, usePrices } from "../../shared/api/prices";
import { can, useMe } from "../../shared/api/session";
import { cairoWhen } from "../../shared/lib/time";
import { Button, Card, ErrorState, Loading } from "../../shared/ui/ui";

/**
 * «الأسعار» (TASK 06, DECISIONS v1.17 B-23): the price list Hazem quotes and the booking asks for. Everyone sees it; the lawyer
 * and the manager change it. A new price counts for new requests only: a booking or an order made before keeps its amount.
 */
export function PricesPage() {
  const { data: me } = useMe();
  const list = usePrices();
  const editor = can(me, "prices.manage");
  const qc = useQueryClient();
  const [draft, setDraft] = useState<Record<string, string>>({});
  const save = useMutation({
    // Only the lines changed here: a line someone else changed meanwhile keeps their value.
    mutationFn: () => prices.save(Object.fromEntries(changed.map((p) => [p.key, Number(draft[p.key])]))),
    onSuccess: () => { setDraft({}); void qc.invalidateQueries({ queryKey: ["prices"] }); void qc.invalidateQueries({ queryKey: ["price-changes"] }); },
  });
  const changed = (list.data ?? []).filter((p) => draft[p.key] !== undefined && Number(draft[p.key]) !== p.value);

  return (
    <div className="grid gap-4">
      <h1 className="text-2xl">الأسعار</h1>
      <p className="text-sm text-ink-soft">
        الأسعار اللي حازم بيقولها للعميل وبيطلبها في الحجز والخدمات. السعر الجديد للطلبات الجديدة بس: الحجز أو الطلب اللي اتعمل قبل كده بيفضل بسعره.
        {editor ? "" : " التعديل للأستاذ والمدير."}
      </p>
      {list.isPending ? <Loading /> : list.isError ? <ErrorState message={errorMessage(list.error)} onRetry={() => void list.refetch()} /> : (
        <Card>
          <ul className="grid gap-2">
            {list.data.map((p) => (
              <li key={p.key} className="flex flex-wrap items-center gap-3 border-b border-line py-2 last:border-0">
                <label htmlFor={p.key} className="min-w-0 flex-1 text-sm">{p.name}</label>
                {editor ? (
                  <input id={p.key} type="number" min={1} inputMode="numeric" dir="ltr"
                    className="surface min-h-10 w-32 rounded-[var(--radius-control)] px-3 text-left tabular"
                    value={draft[p.key] ?? String(p.value)} onChange={(e) => setDraft({ ...draft, [p.key]: e.target.value })} />
                ) : <b id={p.key} className="tabular">{p.value.toLocaleString("en-US")}</b>}
                <span className="text-sm text-ink-soft">جنيه</span>
                {p.value !== p.default ? <span className="text-xs text-ink-soft">(الأصلي {p.default.toLocaleString("en-US")})</span> : null}
              </li>
            ))}
          </ul>
          {editor ? (
            <div className="grid gap-2 pt-3">
              {save.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(save.error)}</p> : null}
              <div>
                <Button variant="primary" busy={save.isPending} disabled={changed.length === 0}
                  onClick={() => {
                    const lines = changed.map((p) => `${p.name}: ${p.value.toLocaleString("en-US")} ← ${Number(draft[p.key]).toLocaleString("en-US")}`).join("\n");
                    if (window.confirm(`الأسعار الجديدة للطلبات الجديدة بس:\n${lines}\nأكيد؟`)) save.mutate();
                  }}>
                  احفظ الأسعار
                </Button>
              </div>
            </div>
          ) : null}
        </Card>
      )}
      {editor ? <Changes /> : null}
    </div>
  );
}

function Changes() {
  const changes = useQuery({ queryKey: ["price-changes"], queryFn: prices.changes });
  const names = Object.fromEntries((usePrices().data ?? []).map((p) => [p.key, p.name]));
  if (!changes.data || changes.data.length === 0) return null;
  return (
    <Card>
      <h2 className="text-lg">آخر التغييرات</h2>
      <ul className="grid gap-1 text-sm">
        {changes.data.map((c, i) => (
          <li key={i}>
            {cairoWhen(c.at)} · {c.by ?? "—"} · {names[c.key] ?? c.key}: <span className="tabular">{c.from} ← {c.to}</span>
          </li>
        ))}
      </ul>
    </Card>
  );
}
