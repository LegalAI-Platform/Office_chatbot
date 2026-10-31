import { useRef, useState, type ReactNode } from "react";
import { queryOptions, useMutation, useQuery, useQueryClient, type QueryClient } from "@tanstack/react-query";
import { errorMessage } from "../../shared/api/client";
import { can, useMe } from "../../shared/api/session";
import { label, PAYMENT_STATES, SERVICE_KINDS, SERVICE_STATES } from "../../shared/lib/labels";

import { cairoDate, cairoWhen } from "../../shared/lib/time";
import { useUrlState } from "../../shared/lib/url";
import { ActionBar, CaseCard, CaseHead, CaseNote, Facts, Files, Meta, Phone } from "../../shared/ui/caseCard";
import { Icon, type IconName } from "../../shared/ui/Icon";
import { confirmAction, toast } from "../../shared/ui/feedback";
import { Button, Chip, ErrorState, Field, Loading } from "../../shared/ui/ui";
import { priceMap, usePrices } from "../../shared/api/prices";
import { ACCEPT, deliverFile, fileUrl, inRange, services, startPrice, type ServiceOrder, type Show } from "./api";

const TONE: Record<string, string> = {
  collecting: "grey", awaiting_price: "orange", awaiting_payment: "blue", in_progress: "blue", delivered: "green", closed: "grey",
};
const DELIVERY: Record<string, string> = { file: "ملف على واتساب", call: "مكالمة من المستشار" };

const SHOWS: Show[] = ["open", "done"];
const listQuery = (show: Show) => queryOptions({ queryKey: ["services", show], queryFn: () => services.list(show) });

/** The open orders (the tab the page opens on), fetched while the pointer is still on the link (idea from PR #14). */
export function prefetchData(qc: QueryClient) {
  void qc.prefetchQuery(listQuery("open"));
}

// each tab with the words that tell a new person what it shows
const TABS: { value: Show; label: string; hint: string; icon: IconName }[] = [
  { value: "open", label: "شغالة", hint: "الطلبات اللي لسه ما اتسلّمتش: محتاجة سعر، أو فلوس، أو شغل وملف.", icon: "play" },
  { value: "done", label: "اتسلّمت أو اتقفلت", hint: "اللي الملف بتاعها وصل للعميل أو اتقفلت، للرجوع ليها.", icon: "check-circle" },
];

const STEPS: { title: string; text: string }[] = [
  { title: "العميل يبعت الورق", text: "حازم بيجمعه منه على واتساب ويحطه هنا." },
  { title: "المستشار يحدّد السعر", text: "ويوم التسليم، وحازم يبعتهم للعميل." },
  { title: "اشتغل وارفع الملف", text: "بعد ما الفلوس تتراجع، وحازم يبعته على واتساب." },
];

/**
 * «طلبات الخدمات» (TASK 10, DECISIONS v1.14 B-16 / B-17): a written service from the client's documents to the
 * delivered file. The lawyer or the manager prices it; the work starts once a person found the money (الإيصالات);
 * the file goes from here to storage and Hazem sends it on WhatsApp.
 * Made to explain itself as «liquid glass»: what the page is for and its steps at the top, a line under the
 * tabs saying what each shows, and on a card the next step when it needs one.
 */
export function ServicesPage() {
  const [show, setShow] = useUrlState<Show>("show", "open", SHOWS);
  const list = useQuery(listQuery(show));

  return (
    <div className="relative isolate grid gap-5 pb-4">
      {/* the soft light the glass shows through */}
      <div aria-hidden className="pointer-events-none absolute inset-x-0 -top-6 -z-10 h-[30rem] overflow-hidden">
        <span className="absolute -top-16 end-[4%] size-[24rem] rounded-full bg-gold/30 blur-[100px]" />
        <span className="absolute top-24 start-[10%] size-[20rem] rounded-full bg-blue/10 blur-[100px]" />
      </div>

      <Intro count={list.data?.length} />

      <section aria-label="الفلاتر" className="liquid grid gap-2 rounded-[22px] p-4 sm:p-5">
        <p className="text-[12.5px] font-bold text-gold-ink">عايز تشوف إيه؟</p>
        <div className="flex gap-1 overflow-x-auto rounded-full border border-line/60 bg-card/45 p-1 shadow-[inset_0_1px_2px_rgb(0_0_0/0.06)]" role="tablist" aria-label="طلبات الخدمات">
          {TABS.map((t) => {
            const on = t.value === show;
            return (
              <button key={t.value} type="button" role="tab" aria-selected={on} onClick={() => setShow(t.value)}
                className={`flex min-h-9 shrink-0 grow items-center justify-center gap-1.5 whitespace-nowrap rounded-full px-3.5 text-[13.5px] transition ${on
                  ? "btn-gold font-bold shadow-[inset_0_1px_0_rgb(255_255_255/0.4),0_6px_14px_-6px_rgb(140_98_58/0.8)]"
                  : "text-ink hover:bg-card/80"}`}>
                <Icon name={t.icon} className="size-4" />
                {t.label}
              </button>
            );
          })}
        </div>
        <p className="flex items-start gap-1.5 text-[12.5px] text-ink-soft" aria-live="polite">
          <Icon name="info" className="mt-0.5 size-3.5 shrink-0 text-gold-ink" />
          {TABS.find((t) => t.value === show)?.hint}
        </p>
      </section>

      {list.isPending ? <Loading /> : list.isError ? <ErrorState message={errorMessage(list.error)} onRetry={() => void list.refetch()} /> : (
        list.data.length === 0 ? <Nothing show={show} /> : (
          <ul className={GLASS_LIST}>{list.data.map((o) => <OrderCard key={o.id} o={o} />)}</ul>
        )
      )}
    </div>
  );
}

/** The shared case cards as glass: see-through over the page's light, a lit edge, a lift under the pointer. */
const GLASS_LIST = "grid gap-3 [&>li>article]:rounded-[20px] [&>li>article]:border-line/60 [&>li>article]:bg-card/60 [&>li>article]:shadow-[inset_0_1px_0_rgb(255_255_255/0.45),0_8px_24px_-14px_rgb(60_44_20/0.4)] [&>li>article]:backdrop-blur-md [&>li>article]:transition [&>li>article:hover]:-translate-y-px [&>li>article:hover]:bg-card/80 motion-reduce:[&>li>article]:transition-none motion-reduce:[&>li>article:hover]:translate-y-0 [&_dl>div]:border [&_dl>div]:border-line/50 [&_dl>div]:bg-card/55";

function Intro({ count }: { count?: number | undefined }) {
  return (
    <header className="liquid relative overflow-hidden rounded-[26px] p-5 sm:p-6">
      <span aria-hidden className="absolute -top-20 -start-16 size-64 rounded-full bg-gradient-to-br from-gold/30 to-transparent blur-2xl" />
      <span aria-hidden className="absolute inset-x-8 top-0 h-px bg-gradient-to-l from-transparent via-gold to-transparent opacity-70" />
      <div className="relative grid gap-5 xl:grid-cols-[minmax(0,1fr)_minmax(0,1.35fr)] xl:items-center">
        <div className="flex items-start gap-4">
          <span aria-hidden className="grid size-14 shrink-0 place-items-center rounded-[18px] bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6),0_8px_20px_-10px_rgb(140_98_58/0.7)]">
            <Icon name="document" className="size-7" />
          </span>
          <div className="grid min-w-0 gap-1">
            <div className="flex flex-wrap items-center gap-2">
              <h1 className="text-[26px] leading-tight font-bold">طلبات الخدمات</h1>
              {count !== undefined ? (
                <span className="tabular rounded-full border border-gold-line bg-gold-tint px-2.5 py-0.5 text-[12.5px] font-bold text-gold-ink">{count} طلب</span>
              ) : null}
            </div>
            <p className="text-[14px] leading-relaxed text-ink-soft">
              مراجعة وصياغة العقود، والإنذار، والرأي المكتوب: <strong className="text-ink">من الورق لحد ما الملف يوصل للعميل</strong>. كل طلب كارت فيه هو فين ومستني إيه.
            </p>
          </div>
        </div>
        <ol className="grid gap-2 sm:grid-cols-3" aria-label="إزاي طلب الخدمة بيمشي">
          {STEPS.map((s, i) => (
            <li key={s.title} className="relative flex items-start gap-3 rounded-[16px] border border-line/60 bg-card/55 p-3 shadow-[inset_0_1px_0_rgb(255_255_255/0.45)]">
              <span className="tabular grid size-7 shrink-0 place-items-center rounded-full bg-gradient-to-b from-[#dcb98c] to-[#a87a48] text-[13px] font-bold text-[#2a2117] shadow-[inset_0_1px_0_rgb(255_255_255/0.6)]">
                {i + 1}
              </span>
              <span className="grid gap-0.5">
                <span className="text-[13.5px] font-bold">{s.title}</span>
                <span className="text-[12px] leading-snug text-ink-soft">{s.text}</span>
              </span>
            </li>
          ))}
        </ol>
      </div>
    </header>
  );
}

function Nothing({ show }: { show: Show }) {
  return (
    <div className="liquid grid justify-items-center gap-2 rounded-[22px] px-6 py-10 text-center">
      <span className="grid size-14 place-items-center rounded-full bg-gradient-to-br from-green-bg to-card text-green-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6)]">
        <Icon name="check-circle" className="size-7" />
      </span>
      <p className="text-[16px] font-bold">مفيش طلبات هنا</p>
      <p className="max-w-sm text-[13px] text-ink-soft">{show === "open" ? "مفيش طلبات شغالة دلوقتي. أول ما عميل يطلب خدمة هتظهر هنا." : "لسه مفيش طلبات اتسلّمت أو اتقفلت."}</p>
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
      {o.state === "awaiting_price" || (o.state === "collecting" && o.files > 0)
        ? <Next>{can(me, "services.price") ? "الورق وصل: حدّد السعر ويوم التسليم تحت، وحازم يبعتهم للعميل." : "مستني المستشار أو المدير يحدّد السعر."}</Next>
        : null}
      {o.state === "in_progress" ? <Next>الفلوس اتراجعت: اشتغل على الطلب وسلّمه قبل يوم التسليم.</Next> : null}
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

/** What the card needs from whoever reads it now, in a line. */
function Next({ children }: { children: ReactNode }) {
  return (
    <p className="mx-5 mt-3 flex items-start gap-2 rounded-[12px] border border-gold-line/70 bg-gold-tint/70 px-3.5 py-2 text-[13px] font-semibold text-gold-ink">
      <Icon name="play" className="mt-0.5 size-4 shrink-0" />
      <span>{children}</span>
    </p>
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
