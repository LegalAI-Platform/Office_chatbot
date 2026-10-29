import { useRef, useState, type ReactNode } from "react";
import { queryOptions, useMutation, useQuery, useQueryClient, type QueryClient } from "@tanstack/react-query";
import { errorMessage } from "../../shared/api/client";
import { can, useMe } from "../../shared/api/session";
import { label, PAYMENT_STATES, SERVICE_KINDS, SERVICE_STATES } from "../../shared/lib/labels";

import { cairoDate, cairoWhen } from "../../shared/lib/time";
import { useUrlState } from "../../shared/lib/url";
import { ActionBar, CaseCard, CaseHead, CaseNote, Facts, Files, Meta, Phone } from "../../shared/ui/caseCard";
import { Icon } from "../../shared/ui/Icon";
import { confirmAction, toast } from "../../shared/ui/feedback";
import { Button, Card, Chip, Empty, ErrorState, Field, Loading } from "../../shared/ui/ui";
import { priceMap, usePrices } from "../../shared/api/prices";
import { ACCEPT, deliverFile, fileUrl, inRange, services, startPrice, type ServiceOrder, type Show } from "./api";

const TONE: Record<string, string> = {
  collecting: "grey", awaiting_price: "orange", awaiting_payment: "blue", in_progress: "blue", delivered: "green", closed: "grey",
};
const DELIVERY: Record<string, string> = { file: "ملف على واتساب", call: "مكالمة من الأستاذ" };

const SHOWS: Show[] = ["open", "done"];
const listQuery = (show: Show) => queryOptions({ queryKey: ["services", show], queryFn: () => services.list(show) });

/** The open orders (the tab the page opens on), fetched while the pointer is still on the link (idea from PR #14). */
export function prefetchData(qc: QueryClient) {
  void qc.prefetchQuery(listQuery("open"));
}

/**
 * «طلبات الخدمات» (TASK 10, DECISIONS v1.14 B-16 / B-17): a written service from the client's documents to the
 * delivered file. The lawyer or the manager prices it; the work starts once a person found the money (الإيصالات);
 * the file goes from here to storage and Hazem sends it on WhatsApp.
 */
export function ServicesPage() {
  const [show, setShow] = useUrlState<Show>("show", "open", SHOWS);
  const list = useQuery(listQuery(show));
  const tabs: [Show, string][] = [["open", "شغالة"], ["done", "اتسلّمت أو اتقفلت"]];

  return (
    <div className="grid gap-4">
      <h1 className="text-2xl">طلبات الخدمات</h1>
      <p className="text-sm text-ink-soft">مراجعة وصياغة العقود، والإنذار، والرأي المكتوب: من الورق لحد ما الملف يوصل للعميل.</p>
      <div className="flex flex-wrap gap-2" role="tablist" aria-label="طلبات الخدمات">
        {tabs.map(([t, text]) => (
          <Button key={t} role="tab" aria-selected={t === show} variant={t === show ? "primary" : "secondary"} onClick={() => setShow(t)}>{text}</Button>
        ))}
      </div>
      {list.isPending ? <Loading /> : list.isError ? <ErrorState message={errorMessage(list.error)} onRetry={() => void list.refetch()} /> : (
        list.data.length === 0 ? <Card><Empty title="مفيش طلبات هنا" icon="check-circle" /></Card> : (
          <ul className="grid gap-3">{list.data.map((o) => <OrderCard key={o.id} o={o} />)}</ul>
        )
      )}
    </div>
  );
}

function OrderCard({ o }: { o: ServiceOrder }) {
  const qc = useQueryClient();
  const { data: me } = useMe();
  const done = () => { void qc.invalidateQueries({ queryKey: ["services"] }); void qc.invalidateQueries({ queryKey: ["payments"] }); };
  const close = useMutation({ mutationFn: () => services.close(o.id), onSuccess: () => { done(); toast("الطلب اتقفل"); } });
  const resend = useMutation({ mutationFn: () => services.resend(o.id), onSuccess: () => { done(); toast("الملف هيتبعت للعميل تاني"); } });
  const open = o.state !== "delivered" && o.state !== "closed";

  const facts: [string, ReactNode][] = [];
  if (o.price != null) facts.push(["السعر", <><span className="tabular">{o.price.toLocaleString("en-US")}</span> جنيه{o.price_exception ? " (استثناء)" : ""}</>]);
  if (o.due_on) facts.push(["التسليم", o.due_on]);
  if (o.delivery) facts.push(["إزاي", DELIVERY[o.delivery]]);
  if (o.payment) facts.push(["الدفع", label(PAYMENT_STATES, o.payment)]);

  return (
    <CaseCard tone={TONE[o.state] ?? "grey"} label={`${o.name ?? "من غير اسم"}: ${label(SERVICE_KINDS, o.kind)}`}>
      <CaseHead title={o.name ?? "من غير اسم"} kind={label(SERVICE_KINDS, o.kind)}
        meta={[
          <Meta key="w" icon="clock">طلب {cairoWhen(o.created_at)}</Meta>,
          <Phone key="p" wa={o.wa_id} />,
          o.state === "awaiting_payment" && o.price_until ? <span key="u">السعر صالح لحد {cairoWhen(o.price_until)}</span> : null,
          o.delivered_at ? <span key="d">اتسلّم {cairoWhen(o.delivered_at)}{o.file_name ? ` · ${o.file_name}` : ""}</span> : null,
        ]}
        status={<Chip tone={TONE[o.state] ?? "grey"}>{label(SERVICE_STATES, o.state)}</Chip>} />
      {facts.length > 0 ? <Facts items={facts} /> : null}
      <Files label="ورق العميل" count={o.files} href={(i) => fileUrl(o.id, i)} word="مستند" />
      {o.files === 0 && o.state === "collecting" ? <CaseNote>مستنيين العميل يبعت الورق.</CaseNote> : null}
      {o.state === "awaiting_payment" ? <CaseNote>حازم بعت السعر وبيانات الدفع. لما الإيصال يوصل، راجعه من «الإيصالات والمدفوعات».</CaseNote> : null}
      {o.notice === "failed" ? (
        <CaseNote tone="bad">
          {o.notice_error === "OUTSIDE_24H_WINDOW" || !o.notice_error
            ? "آخر رسالة ما وصلتش للعميل (عدّى 24 ساعة من آخر رسالة منه). هتتبعت تاني لوحدها أول ما يكتب، أو كلّموه."
            : `آخر رسالة ما وصلتش للعميل: واتساب رفضها (${o.notice_error}). جرّب «ابعت الملف تاني»، ولو اتكررت كلّموه.`}
        </CaseNote>
      ) : null}

      <ActionBar>
        {(o.state === "collecting" || o.state === "awaiting_price") && can(me, "services.price") ? <PriceForm o={o} onDone={done} /> : null}
        {o.state === "in_progress" ? <Deliver o={o} onDone={done} /> : null}
        {o.state === "delivered" && o.file_name ? (
          <div>
            <Button variant="secondary" busy={resend.isPending} disabled={o.notice === "pending"}
              onClick={async () => { if (await confirmAction({ title: "تبعت الملف تاني؟", body: `حازم هيبعت «${o.file_name}» للعميل تاني.`, yes: "ابعته" })) resend.mutate(); }}>
              <Icon name="document" className="size-4" />ابعت الملف تاني
            </Button>
            {resend.isError ? <span className="text-sm text-bad" role="alert"> {errorMessage(resend.error)}</span> : null}
          </div>
        ) : null}
        {open ? (
          <div>
            <Button variant="ghost" busy={close.isPending}
              onClick={async () => {
                if (await confirmAction({ title: "تقفل الطلب ده؟", body: "لو كان اتدفع، هيتفتح استرداد وحازم هيبلّغ العميل.", yes: "اقفله", danger: true })) close.mutate();
              }}>
              <Icon name="x" className="size-4" />اقفل الطلب
            </Button>
            {close.isError ? <span className="text-sm text-bad" role="alert"> {errorMessage(close.error)}</span> : null}
          </div>
        ) : null}
      </ActionBar>
    </CaseCard>
  );
}

/** B-16.3 (B-10): the price, the delivery day and how; outside the range only as an exception. */
function PriceForm({ o, onDone }: { o: ServiceOrder; onDone: () => void }) {
  // The ranges are the panel's price list (TASK 06): no form until it is here, so the check never disagrees with the API's.
  const prices = usePrices();
  if (prices.isPending) return <Loading />;
  if (prices.isError) return <ErrorState message={errorMessage(prices.error)} onRetry={() => void prices.refetch()} />;
  return <PriceFields o={o} onDone={onDone} list={priceMap(prices.data)} />;
}

function PriceFields({ o, onDone, list }: { o: ServiceOrder; onDone: () => void; list: Record<string, number> }) {
  const [price, setPrice] = useState<string | null>(null);
  const shown = price ?? String(startPrice(o.kind, list));
  const [due, setDue] = useState(cairoDate(new Date(Date.now() + 3 * 86_400_000)));
  const [delivery, setDelivery] = useState<"file" | "call">("file");
  const [exception, setException] = useState(false);
  const save = useMutation({ mutationFn: () => services.price(o.id, { price: Number(shown), exception, due_on: due, delivery }), onSuccess: () => { onDone(); toast("السعر اتبعت للعميل ✅"); } });
  const out = !inRange(o.kind, Number(shown), list);

  return (
    <fieldset className="grid gap-2 border-t border-line pt-3">
      <legend className="text-sm font-semibold">السعر</legend>
      <div className="grid gap-2 md:grid-cols-3">
        <Field label="السعر (جنيه)" type="number" min={1} inputMode="numeric" value={shown} onChange={(e) => setPrice(e.target.value)} />
        <Field label="يوم التسليم" type="date" min={cairoDate(new Date())} value={due} onChange={(e) => setDue(e.target.value)} />
        <label className="grid gap-1.5 text-sm font-semibold">
          التسليم
          <select className="surface min-h-11 rounded-[var(--radius-control)] px-3 font-normal" value={delivery} onChange={(e) => setDelivery(e.target.value as "file" | "call")}>
            <option value="file">{DELIVERY.file}</option>
            <option value="call">{DELIVERY.call}</option>
          </select>
        </label>
      </div>
      {out ? (
        <label className="flex items-center gap-2 text-sm text-orange-ink">
          <input type="checkbox" checked={exception} onChange={(e) => setException(e.target.checked)} />السعر برا المدى المعتاد: ده استثناء
        </label>
      ) : null}
      <div>
        <Button variant="primary" busy={save.isPending} disabled={!(Number(shown) > 0) || !due || (out && !exception)}
          onClick={async () => {
            if (await confirmAction({ title: "تبعت السعر للعميل؟", body: `حازم هيبعت للعميل: ${Number(shown).toLocaleString("en-US")} جنيه، والتسليم يوم ${due}.`, yes: "ابعت السعر" })) save.mutate();
          }}>
          ابعت السعر للعميل
        </Button>
      </div>
      {save.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(save.error)}</p> : null}
    </fieldset>
  );
}

/** B-16.6: the file (PDF or Word, 20 MB) from here to storage, then Hazem sends it; or the call made. */
function Deliver({ o, onDone }: { o: ServiceOrder; onDone: () => void }) {
  const input = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const send = useMutation({ mutationFn: () => (o.delivery === "file" ? deliverFile(o.id, file!) : services.deliver(o.id)), onSuccess: () => { onDone(); toast("اتسلّم للعميل ✅"); } });

  return (
    <div className="grid gap-2 border-t border-line pt-3">
      <p className="text-sm font-semibold">الشغل بدأ. التسليم يوم {o.due_on}.</p>
      {o.delivery === "file" ? (
        <div className="flex flex-wrap items-center gap-2">
          <input ref={input} type="file" accept={ACCEPT} aria-label="الملف" className="text-sm"
            onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
          <Button variant="primary" busy={send.isPending} disabled={!file || file.size > 20 * 1024 * 1024}
            onClick={async () => { if (await confirmAction({ title: "تبعت الملف للعميل؟", body: `حازم هيبعت «${file!.name}» للعميل على واتساب.`, yes: "ابعت الملف" })) send.mutate(); }}>
            ابعت الملف للعميل
          </Button>
          {file && file.size > 20 * 1024 * 1024 ? <span className="text-sm text-bad">الملف أكبر من 20 ميجا.</span> : null}
        </div>
      ) : (
        <div><Button variant="primary" busy={send.isPending} onClick={() => send.mutate()}>المكالمة خلصت: اتسلّم</Button></div>
      )}
      {send.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(send.error)}</p> : null}
    </div>
  );
}
