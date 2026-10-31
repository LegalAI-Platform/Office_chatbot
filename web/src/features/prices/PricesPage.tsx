import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { errorMessage } from "../../shared/api/client";
import { prices, usePrices } from "../../shared/api/prices";
import { can, useMe } from "../../shared/api/session";
import { cairoWhen } from "../../shared/lib/time";
import { Icon } from "../../shared/ui/Icon";
import { Button, ErrorState, Loading } from "../../shared/ui/ui";
import { confirmAction, toast } from "../../shared/ui/feedback";

/**
 * «الأسعار» (TASK 06, DECISIONS v1.17 B-23): the price list Hazem quotes and the booking asks for. Everyone sees it; the lawyer
 * and the manager change it. A new price counts for new requests only: a booking or an order made before keeps its amount.
 * As «liquid glass»: a glass header saying what the list is, each price a glass row (a line at its start
 * once changed), and the last changes as glass rows that read «who changed what, from and to».
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
    onSuccess: () => { setDraft({}); void qc.invalidateQueries({ queryKey: ["prices"] }); void qc.invalidateQueries({ queryKey: ["price-changes"] }); toast("الأسعار اتحفظت ✅"); },
  });
  const changed = (list.data ?? []).filter((p) => draft[p.key] !== undefined && Number(draft[p.key]) !== p.value);

  return (
    <div className="relative isolate grid gap-5 pb-4">
      {/* the soft light the glass shows through */}
      <div aria-hidden className="pointer-events-none absolute inset-x-0 -top-6 -z-10 h-[30rem] overflow-hidden">
        <span className="absolute -top-16 end-[4%] size-[24rem] rounded-full bg-gold/30 blur-[100px]" />
        <span className="absolute top-24 start-[10%] size-[20rem] rounded-full bg-green/10 blur-[100px]" />
      </div>

      <header className="liquid relative overflow-hidden rounded-[26px] p-5 sm:p-6">
        <span aria-hidden className="absolute -top-20 -start-16 size-64 rounded-full bg-gradient-to-br from-gold/30 to-transparent blur-2xl" />
        <span aria-hidden className="absolute inset-x-8 top-0 h-px bg-gradient-to-l from-transparent via-gold to-transparent opacity-70" />
        <div className="relative flex items-start gap-4">
          <span aria-hidden className="grid size-14 shrink-0 place-items-center rounded-[18px] bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6),0_8px_20px_-10px_rgb(140_98_58/0.7)]">
            <Icon name="receipt" className="size-7" />
          </span>
          <div className="grid min-w-0 gap-1">
            <div className="flex flex-wrap items-center gap-2">
              <h1 className="text-[26px] leading-tight font-bold">الأسعار</h1>
              {list.data ? (
                <span className="tabular rounded-full border border-gold-line bg-gold-tint px-2.5 py-0.5 text-[12.5px] font-bold text-gold-ink">{list.data.length} سعر</span>
              ) : null}
            </div>
            <p className="max-w-2xl text-[14px] leading-relaxed text-ink-soft">
              الأسعار اللي حازم بيقولها للعميل وبيطلبها في الحجز والخدمات. السعر الجديد <strong className="text-ink">للطلبات الجديدة بس</strong>:
              الحجز أو الطلب اللي اتعمل قبل كده بيفضل بسعره.
              {editor ? "" : " التعديل للأستاذ والمدير."}
            </p>
          </div>
        </div>
      </header>

      {list.isPending ? <Loading /> : list.isError ? <ErrorState message={errorMessage(list.error)} onRetry={() => void list.refetch()} /> : (
        <section aria-labelledby="prices-list" className="liquid relative isolate grid gap-3 overflow-hidden rounded-[22px] p-4 sm:p-5">
          <span aria-hidden className="pointer-events-none absolute -top-16 -end-10 -z-10 size-48 rounded-full bg-gold/15 blur-3xl" />
          <h2 id="prices-list" className="flex items-center gap-2 text-[16px] font-bold">
            <Icon name="receipt" className="size-4 text-gold-ink" />
            قايمة الأسعار
          </h2>
          <ul className="grid gap-2">
            {list.data.map((p) => {
              const edited = draft[p.key] !== undefined && Number(draft[p.key]) !== p.value;
              return (
                <li key={p.key}
                  className={`relative flex flex-wrap items-center gap-x-3 gap-y-2 overflow-hidden rounded-[16px] border bg-card/55 py-3 ps-5 pe-4 shadow-[inset_0_1px_0_rgb(255_255_255/0.45),0_4px_14px_-8px_rgb(60_44_20/0.25)] transition motion-reduce:transition-none ${edited ? "border-gold-line bg-gold-tint/40" : "border-line/60 hover:border-gold-line"}`}>
                  <span aria-hidden className={`absolute inset-y-3 start-0 w-1 rounded-e-full ${edited ? "bg-gold" : "bg-line-strong"}`} />
                  <span className="grid min-w-0 flex-1 basis-56 gap-0.5">
                    <label htmlFor={p.key} className="text-[14px] font-semibold">{p.name}</label>
                    {p.value !== p.default ? <span className="text-[12px] text-ink-soft">(الأصلي {p.default.toLocaleString("en-US")})</span> : null}
                  </span>
                  <span className="flex items-center gap-2">
                    {edited ? <span className="tabular rounded-full bg-gold-tint px-2 py-0.5 text-[11.5px] font-bold text-gold-ink">كان {p.value.toLocaleString("en-US")}</span> : null}
                    {editor ? (
                      <input id={p.key} type="number" min={1} inputMode="numeric" dir="ltr"
                        className="tabular min-h-10 w-32 rounded-full border border-line/60 bg-card/70 px-4 text-left text-ink shadow-[inset_0_1px_2px_rgb(0_0_0/0.06)] focus:border-gold"
                        value={draft[p.key] ?? String(p.value)} onChange={(e) => setDraft({ ...draft, [p.key]: e.target.value })} />
                    ) : <b id={p.key} className="tabular text-[17px]">{p.value.toLocaleString("en-US")}</b>}
                    <span className="text-[13px] text-ink-soft">جنيه</span>
                  </span>
                </li>
              );
            })}
          </ul>
          {editor ? (
            <div className="grid gap-2 border-t border-line/60 pt-3">
              {save.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(save.error)}</p> : null}
              <div className="flex flex-wrap items-center gap-3">
                <Button variant="primary" busy={save.isPending} disabled={changed.length === 0}
                  onClick={async () => {
                    const lines = changed.map((p) => `${p.name}: ${p.value.toLocaleString("en-US")} ← ${Number(draft[p.key]).toLocaleString("en-US")}`).join("\n");
                    if (await confirmAction({ title: "تحفظ الأسعار الجديدة؟", body: `للطلبات الجديدة بس:\n${lines}`, yes: "احفظ الأسعار" })) save.mutate();
                  }}>
                  احفظ الأسعار
                </Button>
              </div>
            </div>
          ) : null}
        </section>
      )}
      {editor ? <Changes /> : null}
    </div>
  );
}

/** The last changes, each a glass row: when, who, which price, from and to. */
function Changes() {
  const changes = useQuery({ queryKey: ["price-changes"], queryFn: prices.changes });
  const names = Object.fromEntries((usePrices().data ?? []).map((p) => [p.key, p.name]));
  if (!changes.data || changes.data.length === 0) return null;
  return (
    <section aria-labelledby="prices-changes" className="liquid relative isolate grid gap-3 overflow-hidden rounded-[22px] p-4 sm:p-5">
      <h2 id="prices-changes" className="flex items-center gap-2 text-[16px] font-bold">
        <Icon name="clock" className="size-4 text-gold-ink" />
        آخر التغييرات
      </h2>
      <ul className="soft-scroll grid max-h-[24rem] gap-2 overflow-y-auto">
        {changes.data.map((c, i) => (
          <li key={i} className="flex flex-wrap items-center gap-x-4 gap-y-1 rounded-[14px] border border-line/60 bg-card/50 px-3.5 py-2.5 text-[13px] shadow-[inset_0_1px_0_rgb(255_255_255/0.4)]">
            <span className="min-w-0 flex-1 basis-48 font-semibold">{names[c.key] ?? c.key}</span>
            <span className="tabular flex items-center gap-1.5">
              <span className="text-ink-soft line-through decoration-ink-soft/50">{c.from}</span>
              <span className="text-ink-soft">←</span>
              <span className="rounded-full bg-gold-tint px-2 font-bold text-gold-ink">{c.to}</span>
            </span>
            <span className="flex items-center gap-1 text-[12px] text-ink-soft">
              <Icon name="account" className="size-3.5" />{c.by ?? "—"}
            </span>
            <span className="tabular flex items-center gap-1 text-[12px] text-ink-soft">
              <Icon name="clock" className="size-3.5" />{cairoWhen(c.at)}
            </span>
          </li>
        ))}
      </ul>
    </section>
  );
}
