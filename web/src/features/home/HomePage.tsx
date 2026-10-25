import { useQuery } from "@tanstack/react-query";
import { useEffect, useId, useState, type ReactNode } from "react";
import { Link } from "react-router";
import { useAppointmentsAhead, type Appointment } from "../../shared/api/appointments";
import { api, errorMessage } from "../../shared/api/client";
import { useDashboard } from "../../shared/api/dashboard";
import { can, useMe } from "../../shared/api/session";
import type { DashboardTask, MonthReport } from "../../shared/api/types";
import { APPOINTMENT_STATES, BOOKING_SERVICES, label, SERVICE_KINDS, SERVICE_STATES, TASK_TYPES, URF_KINDS, URF_STATES } from "../../shared/lib/labels";
import { cairoDate, cairoHour, cairoLongDate, cairoTime, cairoWhen, timeAgo } from "../../shared/lib/time";
import { Icon, type IconName } from "../../shared/ui/Icon";
import { Chip, ErrorState, Segmented } from "../../shared/ui/ui";

const URGENT = new Set(["urgent_request", "safety_alert"]);
const LIVE = new Set(["confirmed", "pending_payment"]);
const who = (t: { client_name?: string | null; name?: string | null }) => t.client_name ?? t.name ?? "رقم من غير اسم";
const money = (n: number) => n.toLocaleString("en-US");
const DAY = 86_400_000;

type Receipt = { appointment_id: string; service: string; start: string; name: string | null; amount_due: number };
type Order = { id: string; kind: string; state: string; name: string | null; created_at: string };
type UrfRow = { id: string; kind: string; state: string; name: string | null; start: string | null; created_at: string };

function thisMonth(): string {
  const p = new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Cairo", year: "numeric", month: "2-digit" }).formatToParts(new Date());
  return `${p.find((x) => x.type === "year")!.value}-${p.find((x) => x.type === "month")!.value}`;
}

/**
 * «الرئيسية» («Night desk»): the office's command center on one screen. The greeting and the quick actions;
 * «محتاج منك دلوقتي» (everything waiting for a person, most pressing first, each with its one action) beside the
 * day's numbers; the day's roll; then the week ahead, this month, the team, what happened lately and where the
 * written services and Urfi cases stand. Each tile shows only what this person may see.
 */
export function HomePage() {
  const { data: me } = useMe();
  const dash = useDashboard();
  const books = can(me, "schedule.manage");
  const reports = can(me, "reports.view");
  const receipts = useQuery({ queryKey: ["payments", "review"], enabled: books, queryFn: () => api<Receipt[]>("GET", "/payments?show=review") });
  const ahead = useAppointmentsAhead(7, books);
  const services = useQuery({ queryKey: ["services", "open"], enabled: can(me, "services.manage"), queryFn: () => api<Order[]>("GET", "/services?show=open") });
  const urf = useQuery({ queryKey: ["urf", "open"], enabled: can(me, "urf.manage"), queryFn: () => api<UrfRow[]>("GET", "/urf?show=open") });
  const month = thisMonth();
  const report = useQuery({ queryKey: ["report", month], enabled: reports, queryFn: () => api<MonthReport>("GET", `/reports?month=${month}`) });

  const now = useNow();
  const todayKey = cairoDate(now);
  const live = (ahead.data ?? []).filter((a) => LIVE.has(a.state)).sort((a, b) => a.start.localeCompare(b.start));
  const today = live.filter((a) => cairoDate(new Date(a.start)) === todayKey);
  const next = live.find((a) => new Date(a.end) > now);
  const waiting = receipts.data?.reduce((s, p) => s + p.amount_due, 0) ?? 0;
  const pressing = (dash.data?.urgent ?? 0) + (dash.data?.overdue ?? 0);

  const greeting = cairoHour(now) < 12 ? "صباح الخير" : "مساء الخير";
  const name = me?.role === "lawyer" ? "أستاذ سعد" : me?.display_name.split(/\s+/)[0] ?? "";
  const pipelines = (services.data ? 1 : 0) + (urf.data ? 1 : 0);

  return (
    <div className="grid gap-5">
      {/* who, when, and the jobs people open the panel for */}
      <header className="flex flex-wrap items-end justify-between gap-x-8 gap-y-4">
        <div className="grid gap-1">
          <p className="text-[14px] font-semibold text-gold-ink">{cairoLongDate(now)} - <span className="tabular">{cairoTime(now)}</span></p>
          <h1 className="text-[30px] leading-tight font-semibold">{greeting} يا {name}</h1>
        </div>
        <nav aria-label="اختصارات" className="flex flex-wrap gap-2">
          <Quick to="/tasks" icon="alert" text="المهام المستعجلة" count={pressing} primary />
          {books ? <Quick to="/receipts" icon="wallet" text="مراجعة الإيصالات" count={receipts.data?.length} /> : null}
          {books ? <Quick to="/appointments" icon="calendar" text="المواعيد" /> : null}
          <Quick to="/chats" icon="chat" text="المحادثات" />
        </nav>
      </header>

      {dash.isError ? <ErrorState message={errorMessage(dash.error)} onRetry={() => void dash.refetch()} /> : null}

      <div className="grid gap-3.5 lg:grid-cols-2 xl:grid-cols-4">
        <Queue tasks={dash.data?.priority} receipts={books ? receipts.data : []} orders={services.data ?? []} cases={urf.data ?? []}
          pending={dash.isPending} now={now} className="lg:col-span-2" />

        <section aria-label="أرقام النهارده" className="grid auto-rows-fr grid-cols-2 gap-3.5 lg:col-span-2">
          {books ? (
            <Stat title="مواعيد النهارده" to="/appointments" tone="blue" value={ahead.isError ? null : ahead.data ? today.length : undefined}
              note={next && cairoDate(new Date(next.start)) === todayKey ? `الجاي الساعة ${cairoTime(new Date(next.start))}` : "مفيش تاني النهارده"} />
          ) : null}
          {books ? (
            <Stat title="إيصالات مستنية" to="/receipts" tone="gold" value={receipts.isError ? null : receipts.data?.length}
              note={waiting > 0 ? `${money(waiting)} جنيه مستنية تتأكد` : "كله اتراجع"} />
          ) : null}
          <Stat title="طلبات عاجلة" to="/tasks" value={dash.data?.urgent} series={dash.data?.urgent_by_day} hot note="آخر 7 أيام" />
          <Stat title="مهام متأخرة" to="/tasks?state=overdue" value={dash.data?.overdue} series={dash.data?.overdue_by_day} hot note="آخر 7 أيام" />
          <Stat title="مش متعيّنة" to="/tasks" tone="gold" value={dash.data?.unassigned} note="محتاجة حد يمسكها" />
          <Stat title="مهام شغالة" to="/tasks" tone="blue" value={dash.data?.active} note="مفتوحة أو حد شغال عليها" />
        </section>

        {books ? <DayRoll items={today} next={next} pending={ahead.isPending} failed={ahead.isError} now={now} className="lg:col-span-2 xl:col-span-4" /> : null}

        {books ? <Week items={live} now={now} className="lg:col-span-2" /> : null}
        {reports ? <MonthCard report={report.data} pending={report.isPending} /> : null}
        {reports ? <Team rows={report.data?.by_staff} /> : null}
        <Activity items={dash.data?.recent} pending={dash.isPending} now={now}
          className={pipelines === 0 && !books ? "lg:col-span-2 xl:col-span-4" : "lg:col-span-2"} />
        {services.data ? (
          <Pipeline title="الخدمات المكتوبة" to="/services" icon="file-text" rows={services.data}
            stages={["collecting", "awaiting_price", "awaiting_payment", "in_progress"]} names={SERVICE_STATES} />
        ) : null}
        {urf.data ? (
          <Pipeline title="حالات العرفي" to="/urf" icon="scale" rows={urf.data}
            stages={["collecting", "documents_pending", "booked", "approved"]} names={URF_STATES} hue="bg-cobalt" />
        ) : null}
      </div>
    </div>
  );
}

/** The time now, ticking every half minute. */
function useNow() {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const t = setInterval(() => setNow(new Date()), 30_000);
    return () => clearInterval(t);
  }, []);
  return now;
}

// ---------- the quick actions and the numbers ----------

/** A shortcut: the pressing one is the gold button, the others quiet. */
function Quick({ to, icon, text, count, primary = false }: { to: string; icon: IconName; text: string; count?: number | undefined; primary?: boolean }) {
  return (
    <Link to={to}
      className={`inline-flex min-h-10 items-center gap-2 rounded-[var(--radius-control)] border px-4 text-[14.5px] font-medium transition-colors ${
        primary ? "btn-primary" : "border-line-strong bg-card text-ink hover:bg-card-2"}`}>
      <Icon name={icon} className="size-[18px]" />
      {text}
      {count ? <span className={`tabular rounded-full px-1.5 text-[12.5px] font-bold ${primary ? "bg-black/15" : "bg-card-2 text-ink-soft"}`}>{count}</span> : null}
    </Link>
  );
}

const TONE = { blue: "text-cobalt-ink", gold: "text-gold-ink", ink: "text-ink" } as const;
const WASH = {
  blue: "!border-cobalt/35 !bg-[color-mix(in_oklab,var(--cobalt)_8%,var(--card))] hover:!border-cobalt/70",
  gold: "!border-gold/40 !bg-[color-mix(in_oklab,var(--gold)_8%,var(--card))] hover:!border-gold/80",
  ink: "hover:border-line-strong",
} as const;

/** One number in its tile, in the color of its job; red when a pressing one is above zero; its last 7 days when there are some. */
function Stat({ title, value, note, to, series, hot = false, tone = "ink" }: {
  title: string; value: number | null | undefined; note: string; to: string; series?: number[] | undefined; hot?: boolean; tone?: keyof typeof TONE;
}) {
  const alarm = hot && typeof value === "number" && value > 0;
  return (
    <Link to={to} className={`surface group grid min-w-0 grid-rows-[auto_auto_auto_1fr] gap-2 rounded-[var(--radius-card)] p-[18px] transition-colors ${
      alarm ? "!border-coral/45 !bg-[color-mix(in_oklab,var(--coral)_11%,var(--card))] hover:!border-coral" : WASH[tone]}`}>
      <span className="flex items-center justify-between gap-2 text-[14.5px] font-semibold text-ink-soft">
        {title}
        <Icon name="chevron-end" className="size-4 shrink-0 opacity-50 transition-transform group-hover:-translate-x-0.5" />
      </span>
      <span className={`display text-[36px] leading-[1.1] font-semibold ${alarm ? "text-red-ink" : TONE[tone]}`}>
        {value === null ? "—" : value === undefined ? <span className="inline-block h-9 w-9 animate-pulse rounded-lg bg-muted align-middle" /> : value}
      </span>
      {series ? <Spark values={series} color={alarm ? "var(--coral)" : "var(--jade)"} /> : <span aria-hidden />}
      <span className="grid self-end text-[12.5px] leading-snug">
        {alarm ? <span className="font-semibold text-red-ink">محتاج حد دلوقتي</span> : null}
        <span className="truncate text-ink-faint">{note}</span>
      </span>
    </Link>
  );
}

/** Seven days, oldest on the reading start (right), today emphasized. */
function Spark({ values, color }: { values: number[]; color: string }) {
  const w = 120, h = 34, pad = 3;
  if (values.length < 2) return null;
  const max = Math.max(1, ...values);
  const pts = values.map((v, i) => [w - pad - (i * (w - 2 * pad)) / (values.length - 1), h - pad - (v / max) * (h - 2 * pad)] as const);
  const line = pts.map(([x, y], i) => `${i ? "L" : "M"}${x.toFixed(1)} ${y.toFixed(1)}`).join(" ");
  const first = pts[0]!, last = pts.at(-1)!;
  const area = `${line} L${last[0].toFixed(1)} ${h} L${first[0].toFixed(1)} ${h} Z`;
  return (
    <svg viewBox={`0 0 ${w} ${h}`} className="h-auto w-full max-w-[150px]" role="img" aria-label={`آخر 7 أيام: ${values.join("، ")}`}>
      <path d={area} fill={color} fillOpacity={0.15} />
      <path d={line} fill="none" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
      <circle cx={last[0]} cy={last[1]} r="3" fill={color} stroke="var(--card)" strokeWidth="2" />
    </svg>
  );
}

// ---------- what needs a person now ----------

type Kind = "all" | "tasks" | "money" | "files";
const KIND_WORD: Record<Exclude<Kind, "all">, string> = { tasks: "مهمة", money: "فلوس", files: "ملف" };
type Item = { key: string; kind: Exclude<Kind, "all">; title: string; sub: string; when: string; late?: boolean; action: string; to: string; aria?: string; rank: number; at: string };

/** Everything waiting for a person, most pressing first, each with the one thing to do about it. */
function Queue({ tasks, receipts, orders, cases, pending, now, className = "" }: {
  tasks?: DashboardTask[] | undefined; receipts?: Receipt[] | undefined; orders: Order[]; cases: UrfRow[]; pending: boolean; now: Date; className?: string;
}) {
  const [show, setShow] = useState<Kind>("all");
  const items: Item[] = [
    ...(tasks ?? []).map((t): Item => {
      const urgent = URGENT.has(t.type);
      return {
        key: `t${t.id}`, kind: "tasks",
        title: label(TASK_TYPES, t.type), sub: `${who(t)}${t.staff_name ? ` - مع ${t.staff_name}` : " - مش متعيّنة"}`,
        when: `${t.late ? "كان المفروض " : "قبل "}${cairoWhen(t.due_at, now)}`, late: t.late,
        action: "افتح المهمة", to: `/tasks/${t.id}`, aria: `افتح مهمة ${who(t)}`, rank: urgent ? 0 : t.late ? 1 : 4, at: t.due_at,
      };
    }),
    ...(receipts ?? []).map((r): Item => ({
      key: `r${r.appointment_id}`, kind: "money", title: "إيصال مستني مراجعة",
      sub: `${who(r)} - ${money(r.amount_due)} جنيه - ${label(BOOKING_SERVICES, r.service)}`, when: `الميعاد ${cairoWhen(r.start, now)}`,
      action: "راجع الإيصال", to: "/receipts", rank: 2, at: r.start,
    })),
    ...orders.filter((o) => o.state === "awaiting_price").map((o): Item => ({
      key: `s${o.id}`, kind: "files", title: "طلب خدمة محتاج سعر", sub: `${who(o)} - ${label(SERVICE_KINDS, o.kind)}`,
      when: `وصل ${timeAgo(o.created_at, now)}`, action: "حدد السعر", to: "/services", rank: 3, at: o.created_at,
    })),
    ...cases.filter((c) => c.state === "booked").map((c): Item => ({
      key: `u${c.id}`, kind: "files", title: "ورق عرفي محتاج مراجعة", sub: `${who(c)} - ${label(URF_KINDS, c.kind)}`,
      when: c.start ? `الميعاد ${cairoWhen(c.start, now)}` : `من ${timeAgo(c.created_at, now)}`, action: "راجع الورق", to: "/urf", rank: 3, at: c.start ?? c.created_at,
    })),
  ].sort((a, b) => a.rank - b.rank || a.at.localeCompare(b.at));
  const shown = show === "all" ? items : items.filter((i) => i.kind === show);
  const count = (k: Exclude<Kind, "all">) => items.filter((i) => i.kind === k).length;

  return (
    <section aria-labelledby="queue-title" className={`surface grid min-w-0 content-start gap-3 rounded-[var(--radius-card)] !border-gold/35 !bg-[color-mix(in_oklab,var(--gold)_7%,var(--card))] p-[18px] ${className}`}>
      <header className="flex flex-wrap items-end justify-between gap-3">
        <h2 id="queue-title" className="flex items-baseline gap-3">
          <span className="display text-[50px] leading-none font-semibold text-gold">{pending ? "…" : items.length}</span>
          <span className="text-[18px] font-semibold">محتاج منك دلوقتي</span>
        </h2>
        <Segmented label="نوع الشغل" value={show} onChange={setShow} options={[
          ["all", "الكل"], ["tasks", `مهام ${count("tasks")}`], ["money", `فلوس ${count("money")}`], ["files", `خدمات وعرفي ${count("files")}`],
        ]} />
      </header>
      {pending ? <Rows n={4} /> : shown.length === 0 ? (
        <p className="py-12 text-center text-[14px] text-ink-soft">مفيش حاجة مستنياك هنا. كل حاجة في ميعادها.</p>
      ) : (
        <ul className="grid divide-y divide-gold/15 border-t border-gold/20">
          {shown.slice(0, 6).map((i) => (
            <li key={i.key} className="flex items-center gap-3 py-3">
              <span className="grid min-w-0 flex-1 gap-0.5 leading-snug">
                <span className="flex flex-wrap items-center gap-2 text-[12.5px] text-ink-faint">
                  {KIND_WORD[i.kind]}
                  {i.rank === 0 ? <Chip tone="red">عاجلة</Chip> : i.rank === 1 ? <Chip tone="red">متأخرة</Chip> : null}
                </span>
                <span className="text-[15px] font-semibold">{i.title}</span>
                <span className="truncate text-[13.5px] text-ink-soft">{i.sub}</span>
              </span>
              <span className={`tabular hidden shrink-0 text-[13px] md:block ${i.late ? "font-medium text-red-ink" : "text-ink-faint"}`}>{i.when}</span>
              <Link to={i.to} aria-label={i.aria}
                className="inline-flex min-h-9 shrink-0 items-center gap-1 rounded-[var(--radius-control)] px-2.5 text-[14px] font-semibold text-accent-ink transition-colors hover:bg-accent-tint">
                {i.action}
                <Icon name="chevron-end" className="size-4" />
              </Link>
            </li>
          ))}
        </ul>
      )}
      {shown.length > 6 ? <p className="border-t border-line pt-3 text-[13.5px] text-ink-soft">و{shown.length - 6} كمان. <Link to="/tasks" className="font-medium text-accent-ink hover:underline">افتح المهام</Link></p> : null}
    </section>
  );
}

// ---------- the day's roll ----------

const OPEN_H = 10; // the office's booking hours (B-24): 10:00 to midnight, Cairo
const CLOSE_H = 24;
const hourOf = (d: Date) => {
  const p = new Intl.DateTimeFormat("en-GB", { timeZone: "Africa/Cairo", hour: "2-digit", minute: "2-digit", hourCycle: "h23" }).format(d).split(":");
  return Number(p[0]) + Number(p[1]) / 60;
};
const hourLabel = (h: number) => (h === 12 ? "12 م" : h === 24 ? "12 ص" : h > 12 ? `${h - 12} م` : `${h} ص`);

/**
 * The day as a court roll: the hours from right to left, each appointment where it really sits. Blue blocks;
 * red for an urgent consultation, gold ring on the next one, hatching while its receipt is awaited.
 */
function DayRoll({ items, next, pending, failed, now, className = "" }: {
  items: Appointment[]; next?: Appointment | undefined; pending: boolean; failed: boolean; now: Date; className?: string;
}) {
  const starts = items.map((a) => hourOf(new Date(a.start)));
  const ends = items.map((a) => hourOf(new Date(a.end)) || 24);
  let from = items.length ? Math.max(OPEN_H, Math.floor(Math.min(...starts)) - 1) : OPEN_H;
  let to = items.length ? Math.min(CLOSE_H, Math.ceil(Math.max(...ends)) + 1) : CLOSE_H;
  if (to - from < 8) {
    to = Math.min(CLOSE_H, from + 8);
    from = Math.max(OPEN_H, to - 8);
  }
  const span = to - from;
  const at = (h: number) => `${((h - from) / span) * 100}%`;
  const nowH = hourOf(now);
  const hours = Array.from({ length: span + 1 }, (_, i) => from + i);

  return (
    <Tile title="رول النهارده" className={className} extra={
      <ul className="flex flex-wrap items-center gap-x-4 gap-y-1 text-[12.5px] text-ink-soft" aria-label="مفتاح الرول">
        <li className="flex items-center gap-1.5"><span className="size-3 rounded-[3px] border border-cobalt bg-cobalt-bg" />ميعاد</li>
        <li className="flex items-center gap-1.5"><span className="size-3 rounded-[3px] border border-coral bg-coral-bg" />عاجلة</li>
        <li className="flex items-center gap-1.5"><span className="size-3 rounded-[3px] border border-cobalt bg-cobalt-bg ring-2 ring-gold ring-inset" />الجاي</li>
        <li className="flex items-center gap-1.5"><span className="hatch size-3 rounded-[3px] border border-cobalt bg-cobalt-bg" />مستني الإيصال</li>
        <li className="flex items-center gap-1.5"><span className="h-3 w-0.5 rounded-full bg-gold" />دلوقتي</li>
      </ul>
    }>
      {pending ? <div className="h-20 animate-pulse rounded-[10px] bg-muted" /> : failed ? <Quiet>مش قادرين نجيب المواعيد دلوقتي.</Quiet> : (
        <div className="-mx-1 overflow-x-auto px-1 pb-1">
          <div className="relative min-w-[36rem]">
            <div className="relative h-[4.5rem] rounded-[12px] border border-line bg-card-2">
              {hours.slice(1, -1).map((h) => (
                <span key={h} className="absolute inset-y-0 w-px bg-line" style={{ insetInlineStart: at(h) }} aria-hidden />
              ))}
              {items.length === 0 ? <p className="absolute inset-0 grid place-items-center text-[14px] text-ink-soft">مفيش مواعيد النهارده.</p> : null}
              {items.map((a) => {
                const s = hourOf(new Date(a.start));
                const e = Math.max(s + 0.5, hourOf(new Date(a.end)) || 24);
                const urgent = a.service === "consultation_urgent";
                const past = new Date(a.end) <= now;
                return (
                  <Link key={a.id} to="/appointments" title={`${cairoTime(new Date(a.start))} - ${who(a)} - ${label(BOOKING_SERVICES, a.service)} (${label(APPOINTMENT_STATES, a.state)})`}
                    aria-label={`${cairoTime(new Date(a.start))} ${who(a)}، ${label(BOOKING_SERVICES, a.service)}، ${label(APPOINTMENT_STATES, a.state)}`}
                    className={`absolute inset-y-2 grid min-w-0 content-center overflow-hidden rounded-[8px] border px-1.5 leading-tight transition-colors hover:border-ink-faint sm:px-2 ${
                      urgent ? "border-coral bg-coral-bg" : "border-cobalt/70 bg-cobalt-bg"} ${next?.id === a.id ? "ring-2 ring-gold ring-inset" : ""} ${past ? "opacity-50" : ""} ${a.state === "pending_payment" ? "hatch" : ""}`}
                    style={{ insetInlineStart: `calc(${at(s)} + 2px)`, width: `calc(${((e - s) / span) * 100}% - 4px)` }}>
                    <span className="truncate text-[12.5px] font-semibold">{who(a)}</span>
                    <span className="display truncate text-[12px] text-ink-soft">{cairoTime(new Date(a.start))}</span>
                  </Link>
                );
              })}
              {nowH >= from && nowH <= to ? (
                <span className="absolute -inset-y-1.5 z-10 w-0.5 rounded-full bg-gold" style={{ insetInlineStart: at(nowH) }} aria-label={`دلوقتي ${cairoTime(now)}`} role="img" />
              ) : null}
            </div>
            <div className="relative mt-2 h-5 text-[12px] text-ink-faint">
              {hours.map((h, i) => (
                <span key={h} style={{ insetInlineStart: at(h) }}
                  className={`absolute whitespace-nowrap ${i === 0 ? "" : i === hours.length - 1 ? "rtl:translate-x-full ltr:-translate-x-full" : "-translate-x-1/2 rtl:translate-x-1/2"}`}>
                  {hourLabel(h)}
                </span>
              ))}
            </div>
          </div>
          {nowH < OPEN_H ? <p className="mt-2 text-[13px] text-ink-soft">اليوم لسه ما بدأش: الحجز من {hourLabel(OPEN_H)}.</p> : null}
        </div>
      )}
    </Tile>
  );
}

// ---------- the overview ----------

function MonthCard({ report: r, pending }: { report?: MonthReport | undefined; pending: boolean }) {
  const pct = r && r.done > 0 ? Math.round((r.done_on_time / r.done) * 100) : null;
  const total = r ? r.received + r.missing + r.refunded : 0;
  return (
    <Tile title="الشهر ده" extra={<MoreLink to="/reports" text="التقارير" />}>
      {pending || !r ? <Rows n={3} /> : (
        <div className="grid gap-4">
          <div className="flex items-baseline gap-2">
            <span className="display text-[32px] leading-none font-semibold text-jade-ink">{r.done}</span>
            <span className="text-[13px] text-ink-soft">خلصت من {r.tasks} مهمة</span>
          </div>
          <div className="grid gap-1.5">
            <div className="flex items-baseline justify-between text-[13px]">
              <span className="text-ink-soft">خلصت في وقتها</span>
              <span className="display font-semibold">{pct === null ? "—" : `${pct}%`}</span>
            </div>
            <div className="h-1.5 overflow-hidden rounded-full bg-muted"><div className="h-full rounded-full bg-jade" style={{ width: `${pct ?? 0}%` }} /></div>
          </div>
          <dl className="grid gap-1.5 text-[13.5px]">
            <div className="flex justify-between gap-2"><dt className="text-ink-soft">عملاء جداد</dt><dd className="display font-semibold">{r.new_clients}</dd></div>
            {([["وصل", r.received, "bg-jade"], ["ناقص", r.missing, "bg-ink-faint"], ["اترد", r.refunded, "bg-coral"]] as const).map(([t, v, c]) => (
              <div key={t} className="flex justify-between gap-2">
                <dt className="flex items-center gap-1.5 text-ink-soft"><span className={`size-2 rounded-[2px] ${c}`} aria-hidden />{t}</dt>
                <dd className="display font-semibold">{money(v)} <span className="text-[11.5px] font-normal text-ink-soft">جنيه</span></dd>
              </div>
            ))}
          </dl>
          {total > 0 ? (
            <div className="flex h-1.5 gap-0.5 overflow-hidden rounded-full bg-muted" aria-hidden>
              <span className="bg-jade" style={{ flexGrow: r.received }} />
              <span className="bg-ink-faint" style={{ flexGrow: r.missing }} />
              <span className="bg-coral" style={{ flexGrow: r.refunded }} />
            </div>
          ) : null}
        </div>
      )}
    </Tile>
  );
}

/** Each staff member's tasks this month: the whole bar is what they hold, the green part what is done. */
function Team({ rows }: { rows?: MonthReport["by_staff"] | undefined }) {
  const list = (rows ?? []).filter((r) => r.staff).sort((a, b) => b.assigned - a.assigned);
  const max = Math.max(1, ...list.map((r) => r.assigned));
  return (
    <Tile title="شغل الفريق" extra={<span className="text-[12.5px] text-ink-faint">الشهر ده</span>}>
      {!rows ? <Rows n={3} /> : list.length === 0 ? <Quiet>مفيش مهام متعيّنة الشهر ده.</Quiet> : (
        <ul className="grid gap-3.5">
          {list.map((r) => (
            <li key={r.staff} className="grid gap-1.5">
              <span className="flex items-baseline justify-between gap-2 text-[14px]">
                <span className="truncate font-medium">{r.staff}</span>
                <span className="shrink-0 text-[12.5px] text-ink-soft"><span className="display text-[14px] font-semibold text-ink">{r.done}</span> من {r.assigned}</span>
              </span>
              <span className="relative h-1.5 overflow-hidden rounded-full bg-muted">
                <span className="absolute inset-y-0 start-0 rounded-full bg-jade/25" style={{ width: `${(r.assigned / max) * 100}%` }} />
                <span className="absolute inset-y-0 start-0 rounded-full bg-jade" style={{ width: `${(r.done / max) * 100}%` }} />
              </span>
            </li>
          ))}
        </ul>
      )}
    </Tile>
  );
}

/** What a recent task was: red when urgent, gold when late, green when done, quiet otherwise. */
function look(t: DashboardTask): { dot: string; title: string } {
  if (URGENT.has(t.type)) return { dot: "bg-coral", title: label(TASK_TYPES, t.type) };
  if (t.late) return { dot: "bg-gold", title: "مهمة متأخرة" };
  if (t.state === "done") return { dot: "bg-jade", title: `${label(TASK_TYPES, t.type)} - خلصت` };
  return { dot: "bg-cobalt", title: label(TASK_TYPES, t.type) };
}

function Activity({ items, pending, now, className = "" }: { items?: DashboardTask[] | undefined; pending: boolean; now: Date; className?: string }) {
  return (
    <Tile title="آخر حاجة حصلت" className={className}>
      {pending ? <Rows n={4} /> : !items?.length ? <Quiet>أي مهمة حازم يفتحها هتظهر هنا.</Quiet> : (
        <ol className="-mx-2 grid sm:grid-cols-2 sm:gap-x-2">
          {items.map((t) => {
            const l = look(t);
            return (
              <li key={t.id} className="min-w-0">
                <Link to={`/tasks/${t.id}`} className="glass-hover flex items-start gap-3 rounded-[10px] px-2 py-2 leading-snug">
                  <span className={`mt-2 size-2 shrink-0 rounded-full ${l.dot}`} aria-hidden />
                  <span className="grid min-w-0 flex-1">
                    <span className="flex items-baseline justify-between gap-2">
                      <span className="truncate font-medium">{l.title}</span>
                      <span className="shrink-0 text-[12px] text-ink-faint">{timeAgo(t.created_at, now)}</span>
                    </span>
                    <span className="truncate text-[13px] text-ink-soft">{who(t)}{t.staff_name ? ` - مع ${t.staff_name}` : ""}</span>
                  </span>
                </Link>
              </li>
            );
          })}
        </ol>
      )}
    </Tile>
  );
}

/** The next seven days as blue bars (appointments), today the full blue (one series: no legend; each bar says its day and count). */
function Week({ items, now, className = "" }: { items: Appointment[]; now: Date; className?: string }) {
  const [hover, setHover] = useState<number | null>(null);
  const days = Array.from({ length: 7 }, (_, i) => {
    const d = new Date(now.getTime() + i * DAY);
    const key = cairoDate(d);
    return {
      key,
      short: i === 0 ? "النهارده" : new Intl.DateTimeFormat("ar-EG-u-nu-latn", { weekday: "short", timeZone: "Africa/Cairo" }).format(d),
      long: new Intl.DateTimeFormat("ar-EG-u-nu-latn", { weekday: "long", day: "numeric", month: "long", timeZone: "Africa/Cairo" }).format(d),
      count: items.filter((a) => cairoDate(new Date(a.start)) === key).length,
    };
  });
  const max = Math.max(1, ...days.map((d) => d.count));
  const total = days.reduce((s, d) => s + d.count, 0);
  return (
    <Tile title="الأسبوع الجاي" className={className} extra={<span className="text-[13px] text-ink-soft"><span className="display font-semibold text-ink">{total}</span> {total === 1 ? "ميعاد" : "مواعيد"}</span>}>
      <div className="grid h-36 grid-cols-7 items-end gap-2 border-b border-line" role="list" aria-label="المواعيد في الأسبوع الجاي">
        {days.map((d, i) => (
          <div key={d.key} role="listitem" tabIndex={0} aria-label={`${d.long}: ${d.count}`}
            onMouseEnter={() => setHover(i)} onMouseLeave={() => setHover(null)} onFocus={() => setHover(i)} onBlur={() => setHover(null)}
            className="relative flex h-full flex-col items-center justify-end outline-none">
            {d.count > 0 ? <span className="display mb-1 text-[12.5px] font-semibold text-ink-soft">{d.count}</span> : null}
            <span className={`w-full max-w-8 rounded-t-[4px] transition-colors ${d.count === 0 ? "h-1 bg-line" : i === 0 || hover === i ? "bg-cobalt" : "bg-cobalt/55"}`}
              style={d.count > 0 ? { height: `${Math.max(8, (d.count / max) * 78)}%` } : undefined} />
            {hover === i ? (
              <span className="pop absolute bottom-full z-10 mb-2 whitespace-nowrap rounded-lg px-2.5 py-1.5 text-[12.5px]">
                {d.long}: <strong className="display font-semibold">{d.count}</strong>
              </span>
            ) : null}
          </div>
        ))}
      </div>
      <div className="grid grid-cols-7 gap-2 text-center text-[12px] text-ink-faint">
        {days.map((d, i) => <span key={d.key} className={i === 0 ? "font-semibold text-ink" : ""}>{d.short}</span>)}
      </div>
    </Tile>
  );
}

/** Where the open orders stand, stage by stage (an ordinal scale: one hue, deeper as the work moves on). */
function Pipeline({ title, to, icon, rows, stages, names, hue = "bg-gold" }: {
  title: string; to: string; icon: IconName; rows: { state: string }[]; stages: string[]; names: Record<string, string>; hue?: string;
}) {
  const counts = stages.map((s) => ({ s, n: rows.filter((r) => r.state === s).length }));
  const shade = [0.3, 0.5, 0.75, 1];
  return (
    <Tile title={title} extra={<MoreLink to={to} text="عرض الكل" />}>
      {rows.length === 0 ? <Quiet>مفيش حاجة مفتوحة دلوقتي.</Quiet> : (
        <div className="grid gap-4">
          <div className="flex items-baseline gap-2">
            <Icon name={icon} className="size-[18px] self-center text-ink-faint" />
            <span className="display text-[32px] leading-none font-semibold">{rows.length}</span>
            <span className="text-[13px] text-ink-soft">مفتوحة</span>
          </div>
          <div className="flex h-1.5 gap-0.5 overflow-hidden rounded-full bg-muted" aria-hidden>
            {counts.filter((c) => c.n > 0).map((c) => (
              <span key={c.s} className={hue} style={{ flexGrow: c.n, opacity: shade[stages.indexOf(c.s)] ?? 1 }} />
            ))}
          </div>
          <ul className="grid gap-1.5">
            {counts.map((c, i) => (
              <li key={c.s} className="flex items-center gap-2 text-[13.5px]">
                <span className={`size-2 shrink-0 rounded-[2px] ${hue}`} style={{ opacity: shade[i] ?? 1 }} aria-hidden />
                <span className="min-w-0 flex-1 truncate text-ink-soft">{label(names, c.s)}</span>
                <span className="display font-semibold">{c.n}</span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </Tile>
  );
}

// ---------- small parts ----------

/** A tile of the desk: a quiet title on the start side, a link or a key on the end side. */
function Tile({ title, extra, className = "", children }: { title: string; extra?: ReactNode; className?: string; children: ReactNode }) {
  const id = useId();
  return (
    <section aria-labelledby={id} className={`surface grid min-w-0 content-start gap-3.5 rounded-[var(--radius-card)] p-[18px] ${className}`}>
      <header className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2">
        <h2 id={id} className="text-[14.5px] font-semibold text-ink-soft">{title}</h2>
        {extra}
      </header>
      {children}
    </section>
  );
}

function MoreLink({ to, text }: { to: string; text: string }) {
  return (
    <Link to={to} className="inline-flex items-center gap-1 text-[13px] font-medium text-accent-ink hover:underline">
      {text}
      <Icon name="chevron-end" className="size-4" />
    </Link>
  );
}

function Quiet({ children }: { children: string }) {
  return <p className="py-6 text-center text-[14px] text-ink-soft">{children}</p>;
}

function Rows({ n }: { n: number }) {
  return (
    <div className="grid gap-2" role="status" aria-label="جاري التحميل">
      {Array.from({ length: n }, (_, i) => <div key={i} className="h-11 animate-pulse rounded-[10px] bg-muted" />)}
    </div>
  );
}
