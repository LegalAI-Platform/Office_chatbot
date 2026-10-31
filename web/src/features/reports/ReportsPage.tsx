import { useId, useState, type ReactNode } from "react";
import { queryOptions, useQuery, type QueryClient } from "@tanstack/react-query";
import { api, errorMessage } from "../../shared/api/client";
import type { MonthReport, RatingReport, ReportRow } from "../../shared/api/types";
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
const reportQuery = (month: string) => queryOptions({ queryKey: ["report", month], queryFn: () => api<MonthReport>("GET", `/reports?month=${month}`) });

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
  const report = useQuery(reportQuery(month));
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
          <label className="grid gap-1.5 text-sm">
            <span className="flex items-center gap-1.5 text-[12.5px] font-bold text-gold-ink">
              <Icon name="calendar" className="size-4" />
              الشهر
            </span>
            <input type="month" className="tabular min-h-11 rounded-full border border-line/60 bg-card/60 px-4 text-ink shadow-[inset_0_1px_2px_rgb(0_0_0/0.06)] focus:border-gold" value={month} max={thisMonth()}
              onChange={(e) => e.target.value && setMonth(e.target.value)} />
          </label>
        </div>
      </header>

      {report.isPending ? <Loading /> : report.isError ? <ErrorState message={errorMessage(report.error)} onRetry={() => void report.refetch()} /> : r ? (
        <>
          <Group icon="tasks" title="المهام">
            <div className="grid grid-cols-2 gap-3 xl:grid-cols-4">
              <Number icon="tasks" title="المهام" value={r.tasks} />
              <Number icon="check-circle" tone="green" title="خلصت" value={r.done} note={pct(r.done, r.tasks)} />
              <Number icon="clock" tone="blue" title="خلصت في وقتها" value={r.done_on_time} note={pct(r.done_on_time, r.done)} />
              <Number icon="user-plus" tone="orange" title="عملاء جداد" value={r.new_clients} />
            </div>
            <div className="grid gap-4 lg:grid-cols-2">
              <Table icon="tasks" title="المهام حسب النوع" rows={r.by_type} name={(k) => label(TASK_TYPES, k)} />
              <Staff rows={r.by_staff} />
            </div>
          </Group>

          <Group icon="clients" title="العملاء الجداد">
            <div className="grid gap-4 lg:grid-cols-2 2xl:grid-cols-3">
              <Table icon="trend-up" title="العملاء الجداد حسب المصدر" rows={r.by_source} name={(k) => (k === "unknown" ? "مش معروف" : label(SOURCES, k))} />
              <Table icon="document" title="العملاء الجداد حسب الموضوع" rows={r.by_matter} name={(k) => (k === "unknown" ? "لسه ما اتحددش" : matterLabel(k))} />
              <Table icon="users" title="حسب نوع العميل" rows={r.by_client_type} name={(k) => (k === "unknown" ? "لسه ما اتحددش" : label(CLIENT_TYPES, k))} />
            </div>
          </Group>

          <Group icon="calendar" title="الحجوزات والفلوس">
            <div className="grid gap-4 lg:grid-cols-2">
              <Table icon="calendar" title="الحجوزات حسب الحالة" rows={r.by_booking} name={(k) => label(APPOINTMENT_STATES, k)} />
              <div className="grid content-start gap-3 sm:grid-cols-3 lg:grid-cols-1 2xl:grid-cols-3">
                <Number icon="check-circle" tone="green" title="وصل (جنيه)" value={r.received} />
                <Number icon="alert-circle" tone="orange" title="ناقص (جنيه)" value={r.missing} />
                <Number icon="refresh" tone="grey" title="اترد (جنيه)" value={r.refunded} />
              </div>
            </div>
            <Note>«في وقتها» = خلصت قبل الميعاد المطلوب. الفلوس = اللي المكتب علّم إنه وصل فعلًا لحجوزات الشهر.</Note>
          </Group>

          {r.ratings ? <Ratings ratings={r.ratings} /> : null}
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
  icon: IconName; tone?: string; title: string; value: number; note?: string | undefined;
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
        {value.toLocaleString("en-US")}
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
      {rows.length === 0 ? <Blank>مفيش حاجة الشهر ده.</Blank> : (
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
      {rows.length === 0 ? <Blank>مفيش مهام الشهر ده.</Blank> : (
        <div className="soft-scroll overflow-x-auto">
          <table className="w-full min-w-[20rem] text-[13.5px]">
            <thead><tr className="border-b border-line/70"><th className={TH}>الموظف</th><th className={TH}>ليه</th><th className={TH}>خلصت</th><th className={TH}>في وقتها</th></tr></thead>
            <tbody className="divide-y divide-line/60">
              {rows.map((s) => (
                <tr key={s.staff} className="transition hover:bg-card/60">
                  <td className="px-2 py-2 font-semibold">{s.staff || "مش متعيّنة"}</td><td className="tabular px-2">{s.assigned}</td><td className="tabular px-2">{s.done}</td><td className="tabular px-2">{s.on_time}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </GlassCard>
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
  const score = (n: number | null) => (n === null ? "—" : n.toFixed(1));
  const cairoDay = (at: string) => new Intl.DateTimeFormat("ar-EG", { timeZone: "Africa/Cairo", day: "numeric", month: "numeric" }).format(new Date(at));
  return (
    <section className="grid gap-3" aria-labelledby="ratings-title">
      <GroupHead id="ratings-title" icon="message" title="تقييمات العملاء" />
      <div className="grid gap-3 sm:grid-cols-3">
        <div className="liquid relative grid min-w-0 content-start gap-1 overflow-hidden rounded-[18px] py-3.5 ps-5 pe-4">
          <span aria-hidden className="absolute inset-y-3 start-0 w-1 rounded-e-full bg-gold" />
          <h3 className="text-[13px] font-semibold text-ink-soft">متوسط التقييم (من 5)</h3>
          <p className="tabular text-3xl font-bold">{score(ratings.average)}</p>
          {ratings.average !== null ? <Bar share={ratings.average / 5} /> : null}
        </div>
        <Number icon="mail" title="اتبعت" value={ratings.sent} />
        <Number icon="check-circle" tone="green" title="اتقيّمت" value={ratings.answered} note={ratings.sent === 0 ? undefined : `${Math.round((ratings.answered / ratings.sent) * 100)}%`} />
      </div>
      <div className="grid gap-4 lg:grid-cols-2">
        <GlassCard icon="trend-up" title="المتوسط حسب الخدمة">
          {ratings.by_service.length === 0 ? <Blank>مفيش تقييمات الشهر ده.</Blank> : (
            <ul className="grid gap-2.5">
              {ratings.by_service.map((s) => (
                <li key={s.key} className="grid gap-1">
                  <span className="flex items-baseline justify-between gap-3 text-[13.5px]">
                    <span className="min-w-0">{label(RATED_SERVICES, s.key)}</span>
                    <span className="flex shrink-0 items-baseline gap-2">
                      <span className="tabular text-[12px] text-ink-soft">{s.count} تقييم</span>
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
          {ratings.low.length === 0 ? <Blank>مفيش تقييمات ضعيفة الشهر ده.</Blank> : (
            <div className="soft-scroll overflow-x-auto">
              <table className="w-full min-w-[18rem] text-[13.5px]">
                <thead><tr className="border-b border-line/70"><th className={TH}>اليوم</th><th className={TH}>الخدمة</th><th className={TH}>العميل</th></tr></thead>
                <tbody className="divide-y divide-line/60">
                  {ratings.low.map((l, i) => (
                    <tr key={`${l.at}-${i}`} className="transition hover:bg-card/60">
                      <td className="tabular px-2 py-2">{cairoDay(l.at)}</td><td className="px-2">{label(RATED_SERVICES, l.service)}</td><td className="px-2 font-semibold">{l.client_name ?? "رقم من غير اسم"}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </GlassCard>
      </div>
      <Note>التقييمات اللي اتبعتت الشهر ده. «ليس الآن» مش بيتحسب في المتوسط. سبب التقييم الضعيف في مهمته («كلام العميل»).</Note>
    </section>
  );
}
