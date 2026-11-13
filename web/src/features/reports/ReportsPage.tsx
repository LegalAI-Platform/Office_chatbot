import { Fragment, useId, useState, type ReactNode } from "react";
import { queryOptions, useQuery, type QueryClient } from "@tanstack/react-query";
import { api, errorMessage } from "../../shared/api/client";
import type { AiCostReport, AiCostTurn, FreeAnswerRow, MonthReport, RatingReport, ReportRow, ReviewRow, ServiceReportRow, TrendRow, WrittenSummary } from "../../shared/api/types";
import { APPOINTMENT_STATES, BOOKING_SERVICES, label, SERVICE_KINDS, SOURCES, TASK_TYPES, CLIENT_TYPES, matterLabel } from "../../shared/lib/labels";
import { Icon, type IconName } from "../../shared/ui/Icon";
import { ErrorState, Loading } from "../../shared/ui/ui";

/** «This month» in Cairo, as YYYY-MM. */
function thisMonth(): string {
  const parts = new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Cairo", year: "numeric", month: "2-digit" }).formatToParts(new Date());
  return `${parts.find((p) => p.type === "year")!.value}-${parts.find((p) => p.type === "month")!.value}`;
}

/**
 * The month's report (TASK 07c, the manager and the lawyer): the tasks (by type and by staff
 * member, and how many were done before their due time) and the new clients (by source, topic and
 * kind). TASK 09: the month's bookings by state and their money as the office marked it. v1.31 (B-26): «تقييمات العملاء»,
 * the ratings sent in the month, their average (all and by service) and the low ones («يحتاج للتحسين»).
 */
/** The owner (2026-11-08): the report and the chart refresh themselves every minute, as the home page does. */
const LIVE = 60_000;

const reportQuery = (month: string) => queryOptions({ queryKey: ["report", month], queryFn: () => api<MonthReport>("GET", `/reports?month=${month}`), refetchInterval: LIVE });

/** «Today» in Cairo, as YYYY-MM-DD. */
function today(): string {
  return new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Cairo", year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date());
}

/** The owner (2026-11-07, «باليوم ولا بالسنة»): the report for a month, a day, a range of days or a year. */
type Span = "month" | "day" | "range" | "year";

const SPANS: { value: Span; label: string; icon: IconName }[] = [
  { value: "month", label: "شهر", icon: "calendar" },
  { value: "day", label: "يوم", icon: "clock" },
  { value: "range", label: "من يوم إلى يوم", icon: "trend-up" },
  { value: "year", label: "سنة", icon: "reports" },
];

/** The days a span asks for («from» and «to», both included); null for a month (it has its own query). */
function daysOf(span: Span, day: string, from: string, to: string, year: string): [string, string] | null {
  if (span === "day") return [day, day];
  if (span === "range") return from <= to ? [from, to] : [to, from];
  if (span === "year") return [`${year}-01-01`, year === today().slice(0, 4) ? today() : `${year}-12-31`];
  return null;
}

const rangeQuery = (from: string, to: string) => queryOptions({
  queryKey: ["report", from, to],
  queryFn: () => api<MonthReport>("GET", `/reports?from=${from}&to=${to}`),
  refetchInterval: LIVE,
});

const trendQuery = queryOptions({ queryKey: ["report-trend"], queryFn: () => api<{ months: TrendRow[] }>("GET", "/reports/trend"), refetchInterval: LIVE });

/** This month's report (what the page opens on), fetched while the pointer is still on the link (idea from PR #14). */
export function prefetchData(qc: QueryClient) {
  void qc.prefetchQuery(reportQuery(thisMonth()));
}

/**
 * «التقارير» as «liquid glass»: a glass header with the month beside it, then four groups (the tasks, the new clients, the
 * bookings and their money, the ratings).
 */
export function ReportsPage() {
  const [month, setMonth] = useState(thisMonth);
  const [span, setSpan] = useState<Span>("month");
  const [day, setDay] = useState(today);
  const [from, setFrom] = useState(() => `${today().slice(0, 8)}01`);
  const [to, setTo] = useState(today);
  const [year, setYear] = useState(() => today().slice(0, 4));
  const days = daysOf(span, day, from, to, year);
  const report = useQuery(days ? rangeQuery(days[0], days[1]) : reportQuery(month));
  const r = report.data;
  const pct = (a: number, b: number) => (b === 0 ? "—" : `${Math.round((a / b) * 100)}%`);

  return (
    <div className="relative isolate grid gap-6 pb-4">
      {/* the soft light the glass shows through */}
      <div aria-hidden className="pointer-events-none absolute inset-x-0 -top-6 -z-10 h-[30rem] overflow-hidden">
        <span className="absolute -top-16 end-[4%] size-[24rem] rounded-full bg-gold/30 blur-[100px]" />
        <span className="absolute top-24 start-[10%] size-[20rem] rounded-full bg-blue/10 blur-[100px]" />
      </div>

      <header className="relative">
        <div className="relative flex flex-wrap items-end justify-between gap-5">
          <div className="flex min-w-0 flex-1 basis-[20rem] items-center gap-4">
            <span aria-hidden className="grid size-11 shrink-0 place-items-center rounded-[14px] bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6),0_8px_20px_-10px_rgb(140_98_58/0.7)]">
              <Icon name="reports" className="size-6" />
            </span>
            <h1 className="text-[26px] leading-tight font-bold">التقارير</h1>
          </div>
          <div className="grid gap-2">
            <Spans value={span} onChange={setSpan} />
            <div className="flex flex-wrap items-end gap-2">
              {span === "month" ? <Field label="الشهر"><input type="month" className={INPUT} value={month} min="2024-01" max={thisMonth()}
                onChange={(e) => { const v = e.target.value; if (v >= "2024-01" && v <= thisMonth()) setMonth(v); }} /></Field> : null}
              {span === "day" ? <Field label="اليوم"><input type="date" className={INPUT} value={day} min="2024-01-01" max={today()}
                onChange={(e) => { const v = e.target.value; if (v >= "2024-01-01" && v <= today()) setDay(v); }} /></Field> : null}
              {span === "range" ? (
                <>
                  <Field label="من"><input type="date" className={INPUT} value={from} min="2024-01-01" max={today()}
                    onChange={(e) => { const v = e.target.value; if (v >= "2024-01-01" && v <= today()) setFrom(v); }} /></Field>
                  <Field label="إلى"><input type="date" className={INPUT} value={to} min="2024-01-01" max={today()}
                    onChange={(e) => { const v = e.target.value; if (v >= "2024-01-01" && v <= today()) setTo(v); }} /></Field>
                </>
              ) : null}
              {span === "year" ? <Field label="السنة"><select className={INPUT} value={year} onChange={(e) => setYear(e.target.value)}>
                {Array.from({ length: parseInt(today().slice(0, 4), 10) - 2023 }, (_, i) => String(parseInt(today().slice(0, 4), 10) - i)).map((y) => <option key={y} value={y}>{y}</option>)}
              </select></Field> : null}
            </div>
          </div>
        </div>
      </header>

      {report.isPending ? <Loading /> : report.isError ? <ErrorState message={errorMessage(report.error)} onRetry={() => void report.refetch()} /> : r ? (
        <>
          <Group icon="tasks" title="المهام">
            <div className="grid grid-cols-2 gap-3 xl:grid-cols-4">
              <Number icon="tasks" title="المهام" value={r.tasks} />
              <Number icon="check-circle" tone="green" title="المكتملة" value={r.done} note={pct(r.done, r.tasks)} />
              <Number icon="clock" tone="blue" title="اكتملت في موعدها" value={r.done_on_time} note={pct(r.done_on_time, r.done)} />
              <Number icon="user-plus" tone="orange" title="عملاء جدد" value={r.new_clients} />
            </div>
            <div className="grid gap-4 lg:grid-cols-2">
              <Table icon="tasks" title="المهام حسب النوع" rows={r.by_type} name={(k) => label(TASK_TYPES, k)} />
              <Staff rows={r.by_staff} />
            </div>
          </Group>

          <Group icon="clients" title="العملاء الجدد">
            <div className="grid gap-4 lg:grid-cols-2 2xl:grid-cols-3">
              <Table icon="trend-up" title="العملاء الجدد حسب المصدر" rows={r.by_source} name={(k) => (k === "unknown" ? "غير معروف" : label(SOURCES, k))} />
              <Table icon="document" title="العملاء الجدد حسب الموضوع" rows={r.by_matter} name={(k) => (k === "unknown" ? "لم يُحدَّد بعد" : matterLabel(k))} />
              <Table icon="users" title="حسب نوع العميل" rows={r.by_client_type} name={(k) => (k === "unknown" ? "لم يُحدَّد بعد" : label(CLIENT_TYPES, k))} />
            </div>
          </Group>

          <Group icon="calendar" title="الحجوزات والمبالغ">
            <div className="grid gap-4 lg:grid-cols-2">
              <Table icon="calendar" title="الحجوزات حسب الحالة" rows={r.by_booking} name={(k) => label(APPOINTMENT_STATES, k)} />
              <div className="grid content-start gap-3 sm:grid-cols-3 lg:grid-cols-1 2xl:grid-cols-3">
                <Number icon="check-circle" tone="green" title="المُستلَم (جنيه)" value={r.received} />
                <Number icon="alert-circle" tone="orange" title="الناقص (جنيه)" value={r.missing} />
                <Number icon="refresh" tone="grey" title="المُسترَد (جنيه)" value={r.refunded} />
              </div>
            </div>
            <ByService rows={r.by_service ?? []} />
            <Months />
            <Note>«في موعدها» = اكتملت قبل الموعد المطلوب. المبالغ = ما سجّل المكتب أنه وصل فعلًا عن حجوزات الشهر.</Note>
            <Note>«محجوز» = تم تأكيده بالدفع (تم، أو لم يحن موعده بعد، أو لم يحضر). الخدمات المكتوبة: الطلب الذي تم تسعيره، و«تم» = تم التسليم.</Note>
          </Group>

          {r.ratings ? <Ratings ratings={r.ratings} /> : null}
          <Review rows={r.review ?? []} />
          <FreeAnswers rows={r.free_answers ?? []} written={r.written} />
          {span === "month" ? <AiCost month={month} /> : null}
        </>
      ) : null}
    </div>
  );
}

// ---------- the groups and their cards ----------

function GroupHead({ id, icon, title }: { id: string; icon: IconName; title: string }) {
  return (
    <header className="flex items-center gap-3 px-1">
      <span aria-hidden className="grid size-9 shrink-0 place-items-center rounded-xl bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.5),0_2px_6px_-2px_rgb(60_44_20/0.2)]">
        <Icon name={icon} className="size-5" />
      </span>
      <h2 id={id} className="text-[19px] leading-tight font-bold">{title}</h2>
    </header>
  );
}

/** A group of the report: its title and its cards. */
function Group({ icon, title, children }: { icon: IconName; title: string; children: ReactNode }) {
  const id = useId();
  return (
    <section aria-labelledby={id} className="grid gap-3">
      <GroupHead id={id} icon={icon} title={title} />
      {children}
    </section>
  );
}

/** A glass card with the icon tile and the title (the home page's «Panel»). */
function GlassCard({ icon, title, children }: { icon: IconName; title: string; children: ReactNode }) {
  const id = useId();
  return (
    <section aria-labelledby={id} className="liquid relative isolate grid min-w-0 content-start gap-3 overflow-hidden rounded-[18px] p-4">
      <span aria-hidden className="pointer-events-none absolute -top-16 -end-10 -z-10 size-48 rounded-full bg-gold/15 blur-3xl" />
      <span aria-hidden className="pointer-events-none absolute inset-x-6 top-0 h-px bg-gradient-to-l from-transparent via-gold/70 to-transparent" />
      <header className="flex min-w-0 items-center gap-2">
        <span aria-hidden className="grid size-7 shrink-0 place-items-center rounded-xl bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.5),0_2px_6px_-2px_rgb(60_44_20/0.2)]">
          <Icon name={icon} className="size-4" />
        </span>
        <h3 id={id} className="min-w-0 text-[16px] font-bold">{title}</h3>
      </header>
      {children}
    </section>
  );
}

// the line at a number's start and the tint of its icon tile
const TONE: Record<string, { tile: string; bar: string }> = {
  gold: { tile: "from-gold-tint text-gold-ink", bar: "bg-gold" },
  green: { tile: "from-green-bg text-green-ink", bar: "bg-green" },
  blue: { tile: "from-blue-bg text-blue-ink", bar: "bg-blue" },
  orange: { tile: "from-orange-bg text-orange-ink", bar: "bg-orange" },
  grey: { tile: "from-grey-bg text-grey-ink", bar: "bg-ink-soft/50" },
};

function Number({ icon, tone = "gold", title, value, note }: {
  icon: IconName; tone?: string; title: string; value: number | string; note?: string | undefined;
}) {
  const t = TONE[tone] ?? TONE.gold!;
  return (
    <div className="liquid relative grid min-w-0 content-start gap-1 overflow-hidden rounded-[18px] py-3.5 ps-5 pe-4">
      <span aria-hidden className={`absolute inset-y-3 start-0 w-1 rounded-e-full ${t.bar}`} />
      <div className="flex items-center justify-between gap-2">
        <h3 className="text-[13px] font-semibold text-ink-soft">{title}</h3>
        <span aria-hidden className={`grid size-8 shrink-0 place-items-center rounded-xl bg-gradient-to-br to-card shadow-[inset_0_1px_0_rgb(255_255_255/0.5)] ${t.tile}`}>
          <Icon name={icon} className="size-4" />
        </span>
      </div>
      <p className="tabular text-2xl sm:text-3xl font-bold">
        {typeof value === "number" ? value.toLocaleString("en-US") : value}
        {note ? <span className="ms-2 inline-block rounded-full bg-gold-tint px-2 py-0.5 align-middle text-[12.5px] font-bold text-gold-ink">{note}</span> : null}
      </p>
    </div>
  );
}

/** Rows as bars: the name, the count, and a bar as long as its share of the biggest one. */
function Table({ icon, title, rows, name }: { icon: IconName; title: string; rows: ReportRow[]; name: (key: string) => string }) {
  const max = Math.max(1, ...rows.map((r) => r.count));
  return (
    <GlassCard icon={icon} title={title}>
      {rows.length === 0 ? <Blank>لا توجد بيانات هذا الشهر.</Blank> : (
        <ul className="grid gap-2.5">
          {rows.map((r) => (
            <li key={r.key} className="grid gap-1">
              <span className="flex justify-between gap-3 text-[13.5px]"><span className="min-w-0">{name(r.key)}</span><span className="tabular font-bold">{r.count}</span></span>
              <Bar share={r.count / max} />
            </li>
          ))}
        </ul>
      )}
    </GlassCard>
  );
}

/** A bar on a soft track in the panel's brown: the same literal colours on the light and the dark page, readable on both. */
function Bar({ share }: { share: number }) {
  return (
    <span aria-hidden className="block h-2 overflow-hidden rounded-full bg-line/80 shadow-[inset_0_1px_2px_rgb(0_0_0/0.08)]">
      <span className="block h-2 rounded-full bg-gradient-to-l from-[#dcb98c] to-[#a87a48] transition-[width] motion-reduce:transition-none" style={{ width: `${Math.max(share * 100, 2)}%` }} />
    </span>
  );
}

const TH = "px-2 py-2 text-start text-[12px] font-semibold text-ink-soft";

function Staff({ rows }: { rows: MonthReport["by_staff"] }) {
  return (
    <GlassCard icon="users" title="المهام حسب الموظف">
      {rows.length === 0 ? <Blank>لا توجد مهام هذا الشهر.</Blank> : (
        <div className="soft-scroll overflow-x-auto">
          <table className="w-full min-w-[20rem] text-[13.5px]">
            <thead><tr className="border-b border-line/70"><th className={TH}>الموظف</th><th className={TH}>المُسندة إليه</th><th className={TH}>المكتملة</th><th className={TH}>في موعدها</th></tr></thead>
            <tbody className="divide-y divide-line/60">
              {rows.map((s) => (
                <tr key={s.staff} className="transition hover:bg-card/60">
                  <td className="px-2 py-2 font-semibold">{s.staff || "غير مُسندة"}</td><td className="tabular px-2">{s.assigned}</td><td className="tabular px-2">{s.done}</td><td className="tabular px-2">{s.on_time}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </GlassCard>
  );
}

/** The owner's «جاله كام مكالمة وكل مكالمة سعرها كام»: each service, its price, how many were booked and done, and its money. */
function ByService({ rows }: { rows: ServiceReportRow[] }) {
  const money = (n: number) => n.toLocaleString("en-US");
  const max = Math.max(1, ...rows.map((s) => s.received));
  return (
    <GlassCard icon="calendar" title="حسب الخدمة">
      {rows.length === 0 ? <Blank>لا توجد حجوزات هذا الشهر.</Blank> : (
        <div className="soft-scroll overflow-x-auto">
          <table className="w-full min-w-[24rem] text-[13.5px]">
            <thead><tr className="border-b border-line/70"><th className={TH}>الخدمة</th><th className={TH}>السعر</th><th className={TH}>محجوز</th><th className={TH}>تم</th><th className={TH}>المُستلَم (جنيه)</th></tr></thead>
            <tbody className="divide-y divide-line/60">
              {rows.map((s) => (
                <tr key={s.key} className="transition hover:bg-card/60">
                  <td className="px-2 py-2 font-semibold">{label(RATED_SERVICES, s.key)}</td>
                  <td className="tabular px-2">{s.price === null ? "حسب الطلب" : money(s.price)}</td>
                  <td className="tabular px-2">{s.booked}</td><td className="tabular px-2">{s.completed}</td>
                  <td className="px-2 py-1.5"><span className="tabular block font-bold">{money(s.received)}</span><Bar share={s.received / max} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </GlassCard>
  );
}

/**
 * «الفلوس اللي وصلت كل شهر»: the last 12 months, one bar a month on one baseline (one series: no legend, the title names it).
 * Each bar says its month, its money and its bookings on hover and to a screen reader; the month's name is under it.
 */
function Months() {
  const trend = useQuery(trendQuery);
  const rows = trend.data?.months ?? [];
  const max = Math.max(1, ...rows.map((m) => m.received));
  const name = (m: string) => new Intl.DateTimeFormat("ar-EG", { month: "short", timeZone: "UTC" }).format(new Date(`${m}-15T00:00:00Z`));
  const said = (m: TrendRow) => `${name(m.month)} ${m.month.slice(0, 4)}: ${m.received.toLocaleString("en-US")} جنيه، الحجوزات: ${m.booked}`;
  return (
    <GlassCard icon="trend-up" title="المبالغ المُستلَمة كل شهر">
      {trend.isPending ? <Loading /> : trend.isError ? <ErrorState message={errorMessage(trend.error)} onRetry={() => void trend.refetch()} /> : (
        <ol className="grid h-44 grid-cols-12 items-end gap-1.5" aria-label="آخر 12 شهرًا">
          {rows.map((m) => (
            <li key={m.month} className="group relative flex h-full min-w-0 flex-col items-center justify-end gap-1" title={said(m)}>
              <span className="sr-only">{said(m)}</span>
              <span aria-hidden className="tabular text-[10.5px] font-bold text-ink-soft opacity-0 transition group-hover:opacity-100 motion-reduce:transition-none">
                {m.received >= 1000 ? `${Math.round(m.received / 100) / 10}k` : m.received}
              </span>
              <span aria-hidden className="block w-full max-w-7 rounded-t-[4px] bg-gradient-to-t from-[#a87a48] to-[#dcb98c]"
                style={{ height: `${m.received === 0 ? 0 : Math.max((m.received / max) * 100, 3)}%` }} />
              <span aria-hidden className="block h-px w-full bg-line" />
              <span aria-hidden className="truncate text-[10.5px] text-ink-soft">{name(m.month)}</span>
            </li>
          ))}
        </ol>
      )}
    </GlassCard>
  );
}

/** «مدة التقرير»: glass pills, as the booking page's (features never import each other). */
function Spans({ value, onChange }: { value: Span; onChange: (v: Span) => void }) {
  return (
    <div className="flex min-w-0 flex-wrap gap-1 rounded-[20px] border border-line/60 bg-card/45 p-1 shadow-[inset_0_1px_2px_rgb(0_0_0/0.06)]" role="group" aria-label="مدة التقرير">
      {SPANS.map((o) => (
        <button key={o.value} type="button" aria-pressed={value === o.value} onClick={() => onChange(o.value)}
          className={`flex grow items-center justify-center gap-1.5 whitespace-nowrap rounded-[16px] min-h-8 px-2.5 text-[12.5px] transition motion-reduce:transition-none ${value === o.value
            ? "btn-gold font-bold shadow-[inset_0_1px_0_rgb(255_255_255/0.4),0_6px_14px_-6px_rgb(140_98_58/0.8)]"
            : "text-ink hover:bg-card/80"}`}>
          <Icon name={o.icon} className="size-3.5" />
          {o.label}
        </button>
      ))}
    </div>
  );
}

const INPUT = "tabular min-h-11 rounded-full border border-line/60 bg-card/60 px-4 text-ink shadow-[inset_0_1px_2px_rgb(0_0_0/0.06)] focus:border-gold";

function Field({ label: name, children }: { label: string; children: ReactNode }) {
  return (
    <label className="grid gap-1.5 text-sm">
      <span className="flex items-center gap-1.5 text-[12.5px] font-bold text-gold-ink"><Icon name="calendar" className="size-4" />{name}</span>
      {children}
    </label>
  );
}

function Blank({ children }: { children: ReactNode }) {
  return <p className="rounded-[14px] border border-dashed border-gold-line/70 bg-card/40 px-3 py-4 text-center text-[13px] text-ink-soft">{children}</p>;
}

function Note({ children }: { children: ReactNode }) {
  return (
    <p className="flex items-start gap-1.5 px-1 text-[12.5px] text-ink-soft">
      <Icon name="info" className="mt-0.5 size-3.5 shrink-0 text-gold-ink" />
      <span>{children}</span>
    </p>
  );
}

// ---------- the ratings ----------

/** v1.31 (B-26): the service names of the ratings (a booking's service or a written service). */
const RATED_SERVICES: Record<string, string> = { ...BOOKING_SERVICES, ...SERVICE_KINDS };

function Ratings({ ratings }: { ratings: RatingReport }) {
  const score = (n: number | null | undefined) => (n == null ? "—" : n.toFixed(1)); // the API leaves a null out
  const cairoDay = (at: string) => new Intl.DateTimeFormat("ar-EG-u-nu-latn", { timeZone: "Africa/Cairo", day: "numeric", month: "numeric" }).format(new Date(at));
  return (
    <section className="grid gap-3" aria-labelledby="ratings-title">
      <GroupHead id="ratings-title" icon="message" title="تقييمات العملاء" />
      <div className="grid gap-3 sm:grid-cols-3">
        <div className="liquid relative grid min-w-0 content-start gap-1 overflow-hidden rounded-[18px] py-3.5 ps-5 pe-4">
          <span aria-hidden className="absolute inset-y-3 start-0 w-1 rounded-e-full bg-gold" />
          <h3 className="text-[13px] font-semibold text-ink-soft">متوسط التقييم (من 5)</h3>
          <p className="tabular text-3xl font-bold">{score(ratings.average)}</p>
          {ratings.average != null ? <Bar share={ratings.average / 5} /> : null}
        </div>
        <Number icon="mail" title="المُرسلة" value={ratings.sent} />
        <Number icon="check-circle" tone="green" title="المُقيَّمة" value={ratings.answered} note={ratings.sent === 0 ? undefined : `${Math.round((ratings.answered / ratings.sent) * 100)}%`} />
      </div>
      <div className="grid gap-4 lg:grid-cols-2">
        <GlassCard icon="trend-up" title="المتوسط حسب الخدمة">
          {ratings.by_service.length === 0 ? <Blank>لا توجد تقييمات هذا الشهر.</Blank> : (
            <ul className="grid gap-2.5">
              {ratings.by_service.map((s) => (
                <li key={s.key} className="grid gap-1">
                  <span className="flex items-baseline justify-between gap-3 text-[13.5px]">
                    <span className="min-w-0">{label(RATED_SERVICES, s.key)}</span>
                    <span className="flex shrink-0 items-baseline gap-2">
                      <span className="tabular text-[12px] text-ink-soft">التقييمات: {s.count}</span>
                      <span className="tabular font-bold">{score(s.average)}</span>
                    </span>
                  </span>
                  <Bar share={(s.average ?? 0) / 5} />
                </li>
              ))}
            </ul>
          )}
        </GlassCard>
        <GlassCard icon="alert-circle" title="تقييمات «يحتاج للتحسين»">
          {ratings.low.length === 0 ? <Blank>لا توجد تقييمات ضعيفة هذا الشهر.</Blank> : (
            <div className="soft-scroll overflow-x-auto">
              <table className="w-full min-w-[18rem] text-[13.5px]">
                <thead><tr className="border-b border-line/70"><th className={TH}>اليوم</th><th className={TH}>الخدمة</th><th className={TH}>العميل</th></tr></thead>
                <tbody className="divide-y divide-line/60">
                  {ratings.low.map((l, i) => (
                    <tr key={`${l.at}-${i}`} className="transition hover:bg-card/60">
                      <td className="tabular px-2 py-2">{cairoDay(l.at)}</td><td className="px-2">{label(RATED_SERVICES, l.service)}</td><td className="px-2 font-semibold">{l.client_name ?? "رقم بلا اسم"}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </GlassCard>
      </div>
      <Note>التقييمات المُرسلة هذا الشهر. لا يُحتسب «ليس الآن» في المتوسط. سبب التقييم الضعيف موجود في مهمته («رسائل العميل»).</Note>
    </section>
  );
}

// ---------- v1.58: what the AI cost, by conversation and by message ----------

const KIND_LABELS: Record<string, string> = { classify: "فهم الرسالة", write: "كتابة الرد", review: "مراجعة الرد" };

/** USD: two decimals from a dollar up, more below it (a message costs fractions of a cent). */
export const usd = (n: number) =>
  new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", minimumFractionDigits: n >= 1 ? 2 : 3, maximumFractionDigits: n >= 1 ? 2 : 4 }).format(n);

const tokens = (n: number) => (n >= 1000 ? `${(n / 1000).toFixed(1)}K` : String(n));

const cairoWhen = (at: string) =>
  new Intl.DateTimeFormat("ar-EG-u-nu-latn", { timeZone: "Africa/Cairo", day: "numeric", month: "numeric", hour: "numeric", minute: "2-digit" }).format(new Date(at));

/** One conversation's messages with the calls of each (opened from its row). */
function AiCostTurns({ personId, month }: { personId: string; month: string }) {
  const q = useQuery({
    queryKey: ["ai-cost", month, personId],
    queryFn: () => api<{ turns: AiCostTurn[] }>("GET", `/reports/ai-cost/${personId}?month=${month}`),
  });
  if (q.isPending) return <Loading />;
  if (q.isError) return <ErrorState message={errorMessage(q.error)} onRetry={() => void q.refetch()} />;
  return (
    <ul className="grid gap-1.5" aria-label="رسائل المحادثة">
      {q.data.turns.map((t, i) => (
        <li key={`${t.at}-${i}`} className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1 rounded-xl bg-card/60 px-3 py-2 text-[13px]">
          <span className="tabular text-ink-soft">{cairoWhen(t.at)}</span>
          <span className="flex flex-wrap gap-x-3 gap-y-1 text-ink-soft">
            {t.calls.map((c, j) => (
              <span key={j}>{KIND_LABELS[c.kind] ?? c.kind}: <span className="tabular" dir="ltr">{c.cost_usd === null ? "غير مُسعَّر" : usd(c.cost_usd)}</span></span>
            ))}
          </span>
          <span className="tabular font-bold" dir="ltr">
            {usd(t.cost_usd)} <span className="font-normal text-ink-soft">· {tokens(t.input_tokens)} إدخال / {tokens(t.output_tokens)} إخراج</span>
          </span>
        </li>
      ))}
    </ul>
  );
}

/**
 * «تكلفة المحادثات» (the owner): what the AI spent this month on each conversation, the dearest first, and on each of its
 * messages (open a row). The tokens and USD of every call (understanding the message, writing the reply, reviewing it); never
 * the words of the chat.
 */
function AiCost({ month }: { month: string }) {
  const q = useQuery({ queryKey: ["ai-cost", month], queryFn: () => api<AiCostReport>("GET", `/reports/ai-cost?month=${month}`) });
  const [open, setOpen] = useState<string | null>(null);
  const s = q.data?.summary;
  return (
    <section className="grid gap-3" aria-labelledby="aicost-title">
      <GroupHead id="aicost-title" icon="trend-up" title="تكلفة المحادثات" />
      {q.isPending ? <Loading /> : q.isError ? <ErrorState message={errorMessage(q.error)} onRetry={() => void q.refetch()} /> : s ? (
        <>
          <div className="grid gap-3 sm:grid-cols-3">
            <Number icon="trend-up" title="إنفاق هذا الشهر" value={usd(s.total_usd)} />
            <Number icon="chat" title="المحادثات" value={s.conversations} note={s.conversations > 0 ? `${usd(s.total_usd / s.conversations)} لكل محادثة` : undefined} />
            <Number icon="document" title="طلبات إلى الذكاء الاصطناعي" value={s.calls} note={`${tokens(s.input_tokens)} إدخال / ${tokens(s.output_tokens)} إخراج`} />
          </div>
          <GlassCard icon="chat" title="حسب المحادثة">
            {q.data.conversations.length === 0 ? <Blank>لا يوجد إنفاق مسجّل هذا الشهر.</Blank> : (
              <div className="soft-scroll overflow-x-auto">
                <table className="w-full min-w-[22rem] text-[13.5px]">
                  <thead><tr className="border-b border-line/70"><th className={TH}>العميل</th><th className={TH}>الرسائل</th><th className={TH}>التوكنات</th><th className={TH}>التكلفة</th></tr></thead>
                  <tbody className="divide-y divide-line/60">
                    {q.data.conversations.map((c) => (
                      <Fragment key={c.person_id}>
                        <tr className="transition hover:bg-card/60">
                          <td className="px-2 py-2 font-semibold">
                            <button type="button" aria-expanded={open === c.person_id} className="text-start underline-offset-2 hover:underline"
                              onClick={() => setOpen(open === c.person_id ? null : c.person_id)}>
                              {c.client ?? "رقم بلا اسم"}
                            </button>
                          </td>
                          <td className="tabular px-2">{c.turns}</td>
                          <td className="tabular px-2" dir="ltr">{tokens(c.input_tokens)} / {tokens(c.output_tokens)}</td>
                          <td className="tabular px-2 font-bold" dir="ltr">{usd(c.cost_usd)}{c.unpriced > 0 ? " +" : ""}</td>
                        </tr>
                        {open === c.person_id ? (
                          <tr><td colSpan={4} className="px-2 pb-3"><AiCostTurns personId={c.person_id} month={month} /></td></tr>
                        ) : null}
                      </Fragment>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </GlassCard>
          <Note>
            تُحتسب منذ تفعيل الميزة، وتشمل فهم الرسالة وكتابة الرد ومراجعته، بسعر Anthropic المُعلَن. ولا يُحتسب تحويل الرسائل الصوتية
            إلى نص. «+» بجوار الرقم = يوجد طلب إلى نموذج سعره غير معروف.
          </Note>
        </>
      ) : null}
    </section>
  );
}

// ---------- v1.53 (B-34, layer 6): Hazem's free answers, to review ----------

/** Why a free answer did not go (the approved words went instead). */
const GUARD_REASONS: Record<string, string> = {
  review_promise: "وعد لا يستطيع المكتب ضمانه",
  review_legal: "محتوى قانوني",
  review_not_in_facts: "معلومة غير موجودة في حقائق المكتب",
  review_harmful: "محتوى يضر بالمكتب",
  review_breaks_playbook: "خالف قاعدة في دليل التعامل",
  review_unavailable: "لم يردّ المراجِع في الوقت المحدد",
  review_unavailable_timeout: "تأخّر المراجِع",
  review_unavailable_error: "تعطّل المراجِع",
  review_unavailable_invalid: "ردّ المراجِع بردّ غير مفهوم",
  review_unavailable_nokey: "المراجِع بلا مفتاح",
  review_unavailable_none: "لا يوجد مراجِع",
  legal_word: "كلمة قانونية",
  self_feminine: "كلمة بصيغة المؤنث عن حازم",
  repeats_before: "كرّر ما أُرسل قبله",
  price_dropped: "حذف السعر",
  promise: "كلمة وعد",
  money: "حديث عن المال",
  number_not_given: "رقم غير موجود في الحقائق",
  link: "رابط",
  too_long: "طويل",
  same_as_previous: "مطابق للرد السابق",
  timeout: "تأخّر الكاتب",
  error: "تعطّل الكاتب",
};

/**
 * «ردود حازم الحرة»: every reply Hazem wrote himself this month (no approved text answered the client), and the ones the
 * checks refused. A reply the office does not like becomes a line of «دليل التعامل» (in «النصوص»).
 */
function FreeAnswers({ rows, written }: { rows: FreeAnswerRow[]; written?: WrittenSummary }) {
  const when = (at: string) =>
    new Intl.DateTimeFormat("ar-EG-u-nu-latn", { timeZone: "Africa/Cairo", day: "numeric", month: "numeric", hour: "numeric", minute: "2-digit" }).format(new Date(at));
  return (
    <section className="grid gap-3" aria-labelledby="free-title">
      <GroupHead id="free-title" icon="alert-circle" title="ردود حازم الحرة" />
      {written && written.total > 0 ? (
        <p className="text-[13.5px]" data-testid="written-summary">
          عدد الردود التي كتبها حازم هذا الشهر: <strong>{written.total}</strong>، عاد منها <strong>{written.fallbacks}</strong> إلى النص الثابت ({Math.round((written.fallbacks / written.total) * 100)}%)
          {written.retried > 0 ? <>، وأُعيدت كتابة {written.retried} منها بعد الرفض</> : null}.
          {written.by_reason.length > 0 ? <> أكثر الأسباب: {written.by_reason.slice(0, 3).map((x) => `${GUARD_REASONS[x.key] ?? x.key} (${x.count})`).join("، ")}.</> : null}
        </p>
      ) : null}
      <GlassCard icon="alert-circle" title="ما كتبه حازم بنفسه">
        {rows.length === 0 ? <Blank>لا توجد ردود حرة هذا الشهر.</Blank> : (
          <div className="soft-scroll overflow-x-auto">
            <table className="w-full min-w-[18rem] text-[13.5px]">
              <thead><tr className="border-b border-line/70"><th className={TH}>الوقت</th><th className={TH}>العميل</th><th className={TH}>الرد</th></tr></thead>
              <tbody className="divide-y divide-line/60">
                {rows.map((x, i) => (
                  <tr key={`${x.at}-${i}`} className="transition hover:bg-card/60">
                    <td className="tabular px-2 py-2 align-top">{when(x.at)}</td>
                    <td className="px-2 py-2 align-top font-semibold">{x.client ?? "رقم بلا اسم"}</td>
                    <td className="px-2 py-2">
                      {x.text ?? <span className="text-bad">رُفض: {GUARD_REASONS[x.guard] ?? x.guard} (أُرسل النص الثابت)</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </GlassCard>
      <Note>إذا لم يعجبك ردّ، فأضف سطرًا في «دليل التعامل» من «النصوص»، وسيلتزم به حازم بدءًا من الرسالة التالية.</Note>
    </section>
  );
}

// ---------- v1.39 (TASK 16, 3.4): the conversations to review ----------

/** Why the system flagged a conversation. */
const REVIEW_REASONS: Record<ReviewRow["reason"], string> = {
  offer_after_payment: "عرض مدفوع لعميل دفع (لا ينبغي أن يحدث)",
  repeated: "تكرّر الرد",
  unclear_twice: "لم يفهم الرسالة مرتين متتاليتين",
  asked_status: "سأل العميل «فاكرني؟» أو سأل عن موعده",
  left_after_price: "غادر بعد معرفة السعر",
};

/**
 * «محادثات محتاجة مراجعة»: the conversations the system flagged this month, the alarm first. Each one the office finds
 * wrong becomes a test (the owner's rule).
 */
function Review({ rows }: { rows: ReviewRow[] }) {
  const when = (at: string) =>
    new Intl.DateTimeFormat("ar-EG-u-nu-latn", { timeZone: "Africa/Cairo", day: "numeric", month: "numeric", hour: "numeric", minute: "2-digit" }).format(new Date(at));
  const alarm = (x: ReviewRow) => (x.reason === "offer_after_payment" ? 1 : 0);
  const sorted = [...rows].sort((a, b) => alarm(b) - alarm(a));
  return (
    <section className="grid gap-3" aria-labelledby="review-title">
      <GroupHead id="review-title" icon="alert-circle" title="محادثات تحتاج إلى مراجعة" />
      <GlassCard icon="alert-circle" title="حدّدها النظام">
        {sorted.length === 0 ? <Blank>لا توجد محادثات تحتاج إلى مراجعة هذا الشهر.</Blank> : (
          <div className="soft-scroll overflow-x-auto">
            <table className="w-full min-w-[18rem] text-[13.5px]">
              <thead><tr className="border-b border-line/70"><th className={TH}>الوقت</th><th className={TH}>العميل</th><th className={TH}>السبب</th></tr></thead>
              <tbody className="divide-y divide-line/60">
                {sorted.map((x, i) => (
                  <tr key={`${x.at}-${i}`} className="transition hover:bg-card/60">
                    <td className="tabular px-2 py-2">{when(x.at)}</td>
                    <td className="px-2 font-semibold">{x.client ?? "رقم بلا اسم"}</td>
                    <td className={x.reason === "offer_after_payment" ? "px-2 font-semibold text-bad" : "px-2"}>{REVIEW_REASONS[x.reason] ?? x.reason}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </GlassCard>
      <Note>يحدّدها النظام تلقائيًا. افتح المحادثة من «المحادثات» وراجع كيف ردّ حازم.</Note>
    </section>
  );
}
