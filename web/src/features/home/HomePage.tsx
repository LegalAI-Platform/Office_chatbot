import { useId, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router";
import { api, errorMessage } from "../../shared/api/client";
import { openChatwoot } from "../../shared/api/chatwoot";
import { useDashboard } from "../../shared/api/dashboard";
import { useMe } from "../../shared/api/session";
import type { DashboardTask } from "../../shared/api/types";
import { BOOKING_SERVICES, label, TASK_TYPES } from "../../shared/lib/labels";
import { cairoDate, cairoDayWord, cairoHour, cairoMonth, cairoWeek, timeAgo } from "../../shared/lib/time";
import { Icon, type IconName } from "../../shared/ui/Icon";
import { OwnerImg, type OwnerImage } from "../../shared/ui/Owner";
import { ErrorState } from "../../shared/ui/ui";

const URGENT = new Set(["urgent_request", "safety_alert"]);

/** The fields of /booking/appointments this page shows (the booking feature has the whole type; features stay apart). */
interface Appointment {
  id: string; service: string; start: string; state: string; name: string | null; payment: string | null;
  call_app: "whatsapp" | "facetime" | "meet" | null;
}
const who = (t: DashboardTask) => t.client_name ?? "رقم من غير اسم";

/**
 * «الرئيسية» exactly as the owner's mockups lay it out. Tasks are real (TASK 07a); appointments and
 * the receipts waiting for a review come from the payments (TASK 09); today's and the next appointments are the booking's.
 */
export function HomePage() {
  const { data: me } = useMe();
  const dash = useDashboard();
  // TASK 09: the receipts waiting for a person's review (the system never confirms a payment).
  const receipts = useQuery({ queryKey: ["payments", "review"], queryFn: () => api<unknown[]>("GET", "/payments?show=review") });
  const now = new Date();
  // TASK 12: today's appointments (held or confirmed) are a real number now.
  const today = cairoDate(now);
  const appointments = useQuery({
    queryKey: ["booking", "appointments", "today", today],
    queryFn: () => api<Appointment[]>("GET", `/booking/appointments?from=${today}&to=${today}`),
  });
  const todayList = appointments.data?.filter(isOpen).sort(byStart);
  const todays = todayList?.length;
  const greeting = cairoHour(now) < 12 ? "صباح الخير" : "مساء الخير";
  const name = me?.role === "lawyer" ? "أستاذ سعد" : me?.display_name.split(/\s+/)[0] ?? "";

  return (
    <div className="-mx-4 -mt-4 grid gap-5 pb-2 lg:-mx-7 lg:-mt-5">
      {/* the banner: the library, the scales and the lamp, fading into the page under the greeting */}
      <section className="relative isolate overflow-hidden">
        <OwnerImg name="hero" className="fade-right absolute inset-y-0 left-0 -z-10 h-full w-auto max-w-[75%] object-cover object-left" />
        <span className="absolute inset-0 -z-10 bg-gradient-to-l from-page via-page/60 to-transparent sm:hidden" aria-hidden />
        <div className="grid min-h-[8.85rem] content-center gap-1 px-4 py-4 lg:px-7">
          <h1 className="text-[26px] leading-tight font-bold text-hello lg:text-[34px]">{greeting} يا {name}</h1>
          <p className="text-[15px] lg:text-[18px]">إليك أهم ما تحتاج إلى معرفته اليوم</p>
        </div>
      </section>

      <div className="grid gap-5 px-4 lg:px-7">
        {dash.isError ? <ErrorState message={errorMessage(dash.error)} onRetry={() => void dash.refetch()} /> : null}

        {/* the four numbers */}
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <Kpi tone="blue" icon="stat_calendar_blue" title="مواعيد اليوم" value={appointments.isError ? null : todays} to="/appointments" more="عرض المواعيد" />
          <Kpi tone="green" icon="stat_card_green" title="إيصالات تحتاج تأكيد" value={receipts.isError ? null : receipts.data?.length} to="/receipts" more="من الإيصالات" />
          <Kpi tone="orange" icon="stat_alert_yellow" title="أمور عاجلة" value={dash.data?.urgent} series={dash.data?.urgent_by_day} to="/tasks" more="عرض التفاصيل" />
          <Kpi tone="red" icon="stat_clock_red" title="مهام متأخرة" value={dash.data?.overdue} series={dash.data?.overdue_by_day} to="/tasks?state=overdue" more="عرض المهام" />
        </div>

        {/* the mockup's three columns: the calendar on the right, today's and the next appointments in the middle, the
            alerts and the tasks on the left, from a laptop's width (1280 px, a 1920 screen at 150% too). A tablet: the
            calendar beside today, the rest under it; a phone: one column. The three columns end on one line: the calendar
            and the left column set the height, and «أقرب المواعيد» scrolls inside what is left (the owner's choice). */}
        <div className="relative isolate grid gap-4 md:grid-cols-[17rem_minmax(0,1fr)] xl:grid-cols-[17.5rem_minmax(0,1.5fr)_minmax(0,1fr)] 2xl:grid-cols-[19rem_minmax(0,1.45fr)_minmax(0,1fr)] 2xl:gap-5">
          {/* the soft light the glass shows through */}
          <div aria-hidden className="pointer-events-none absolute inset-0 -z-10 overflow-hidden">
            <span className="absolute -top-10 end-[2%] size-[22rem] rounded-full bg-gold/25 blur-[100px]" />
            <span className="absolute top-1/3 start-[30%] size-[20rem] rounded-full bg-blue/10 blur-[100px]" />
            <span className="absolute -bottom-10 start-[2%] size-[18rem] rounded-full bg-[#d9b48a]/30 blur-[100px]" />
          </div>
          <CalendarPanel now={now} items={todayList} />
          <div className="grid min-w-0 grid-rows-[auto_1fr] gap-4 xl:h-0 xl:min-h-full xl:grid-rows-[auto_minmax(9rem,1fr)] 2xl:gap-5">
            <TodayPanel items={todayList} pending={appointments.isPending} failed={appointments.isError} />
            <UpcomingPanel now={now} />
          </div>
          <div className="grid min-w-0 gap-4 md:col-span-2 md:grid-cols-2 xl:col-span-1 xl:grid-cols-1 xl:grid-rows-[auto_1fr] 2xl:gap-5">
            <AlertsPanel items={dash.data?.recent} pending={dash.isPending} now={now} />
            <PriorityPanel items={dash.data?.priority} pending={dash.isPending} now={now} />
          </div>
        </div>
      </div>
    </div>
  );
}

// ---------- the four numbers ----------

type KpiTone = "blue" | "green" | "orange" | "red";

// The cards are pastel in both themes (the mockups), so their numbers keep the strong light-theme inks.
const KPI: Record<KpiTone, { card: string; ink: string; stroke: string }> = {
  blue: { card: "bg-kpi-blue border-blue/20", ink: "text-[#2563eb]", stroke: "var(--blue)" },
  green: { card: "bg-kpi-green border-green/20", ink: "text-[#078a5d]", stroke: "var(--green)" },
  orange: { card: "bg-kpi-orange border-orange/25", ink: "text-[#c77700]", stroke: "var(--orange)" },
  red: { card: "bg-kpi-red border-red/20", ink: "text-[#dc2626]", stroke: "var(--red)" },
};

function Kpi({ tone, icon, title, value, series, to, more }: {
  tone: KpiTone; icon: OwnerImage; title: string; value: number | null | undefined; series?: number[] | undefined; to: string; more: string;
}) {
  const k = KPI[tone];
  return (
    <section className={`relative isolate grid min-h-[8.5rem] grid-cols-[1fr_auto] overflow-hidden rounded-[18px] border px-5 pt-4 pb-3 text-kpi-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.7),0_12px_30px_-14px_rgb(60_44_20/0.3)] transition hover:-translate-y-0.5 hover:shadow-[inset_0_1px_0_rgb(255_255_255/0.7),0_18px_36px_-14px_rgb(60_44_20/0.38)] motion-reduce:transition-none motion-reduce:hover:translate-y-0 ${k.card}`}>
      {/* the glass's sheen: light from the top corner, a bright edge */}
      <span aria-hidden className="pointer-events-none absolute inset-0 -z-10 bg-gradient-to-br from-white/55 via-white/10 to-transparent" />
      <span aria-hidden className="pointer-events-none absolute -top-12 -start-10 -z-10 size-36 rounded-full bg-white/50 blur-2xl" />
      <div className="grid content-start">
        <h2 className="text-[18px] leading-snug font-bold">{title}</h2>
        <p className={`tabular text-[38px] leading-tight font-bold ${k.ink}`}>
          {value === null ? "—" : value === undefined ? <span className="inline-block h-9 w-7 animate-pulse rounded bg-white/70" /> : value}
        </p>
      </div>
      <span className="grid size-16 place-items-center rounded-full border border-white/70 bg-white/45 shadow-[inset_0_1px_0_rgb(255_255_255/0.9),0_6px_16px_-8px_rgb(0_0_0/0.25)] backdrop-blur-sm">
        <OwnerImg name={icon} className="size-12 object-contain" />
      </span>
      <div className="col-span-2 flex items-end justify-between gap-2">
        <Link to={to} className={`flex shrink-0 items-center gap-1.5 whitespace-nowrap text-[14px] font-bold ${k.ink} hover:underline`}>
          {more}
          <Icon name="back" className="size-[18px]" />
        </Link>
        <Sparkline values={series} stroke={k.stroke} />
      </div>
    </section>
  );
}

/** The last 7 days as a soft hill, oldest on the right (RTL). A faint dashed line when there is no data yet. */
function Sparkline({ values, stroke }: { values?: number[] | undefined; stroke: string }) {
  const id = useId().replace(/:/g, "");
  const w = 130, h = 40, pad = 4;
  if (!values || values.length < 2) {
    return (
      <svg viewBox={`0 0 ${w} ${h}`} className="h-10 w-20 min-w-0 2xl:w-32" aria-hidden>
        <path d={`M4 ${h - 8} H${w - 4}`} stroke={stroke} strokeOpacity="0.35" strokeDasharray="3 4" strokeWidth="1.6" fill="none" />
      </svg>
    );
  }
  const max = Math.max(1, ...values);
  const pts = values.map((v, i) => [w - pad - (i * (w - 2 * pad)) / (values.length - 1), h - pad - (v / max) * (h - 2 * pad - 6)] as const);
  let d = `M${pts[0]![0]} ${pts[0]![1]}`;
  for (let i = 1; i < pts.length; i++) {
    const [x0, y0] = pts[i - 1]!;
    const [x1, y1] = pts[i]!;
    const mx = (x0 + x1) / 2;
    d += ` C${mx} ${y0} ${mx} ${y1} ${x1} ${y1}`;
  }
  const area = `${d} L${pts.at(-1)![0]} ${h} L${pts[0]![0]} ${h} Z`;
  return (
    <svg viewBox={`0 0 ${w} ${h}`} className="h-10 w-20 min-w-0 2xl:w-32" role="img" aria-label={`آخر 7 أيام: ${[...values].join("، ")}`}>
      <defs>
        <linearGradient id={`f${id}`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor={stroke} stopOpacity="0.25" />
          <stop offset="1" stopColor={stroke} stopOpacity="0" />
        </linearGradient>
      </defs>
      <path d={area} fill={`url(#f${id})`} />
      <path d={d} stroke={stroke} strokeWidth="1.6" fill="none" strokeLinecap="round" />
    </svg>
  );
}

// ---------- the mockup's panel ----------

/**
 * Every card of the page as «liquid glass» (the owner: modern, clearly worked on): see-through over the page's soft
 * light, a warm glow inside, a lit top edge, and its icon in a small gold tile beside the title (and the count, if any).
 */
function Panel({ title, icon, count, action, children, className = "", flush = false }: {
  title: string; icon: IconName; count?: number | undefined; action?: ReactNode; children: ReactNode; className?: string; flush?: boolean;
}) {
  const id = useId();
  return (
    <section aria-labelledby={id} className={`liquid relative isolate flex min-w-0 flex-col overflow-hidden rounded-[18px] ${className}`}>
      <span aria-hidden className="pointer-events-none absolute -top-16 -end-10 -z-10 size-48 rounded-full bg-gold/20 blur-3xl" />
      <span aria-hidden className="pointer-events-none absolute inset-x-6 top-0 h-px bg-gradient-to-l from-transparent via-gold/70 to-transparent" />
      <header className="flex items-center justify-between gap-2 px-3.5 pt-3.5 pb-2.5">
        <span className="flex min-w-0 items-center gap-2">
          <span aria-hidden className="grid size-7 shrink-0 place-items-center rounded-xl bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.5),0_2px_6px_-2px_rgb(60_44_20/0.2)]">
            <Icon name={icon} className="size-4" />
          </span>
          <h2 id={id} className="min-w-0 truncate text-[16px] font-bold 2xl:text-[19px]">{title}</h2>
          {count ? (
            <span className="tabular shrink-0 rounded-full border border-gold-line bg-gold-tint px-2 py-0.5 text-[11px] font-bold text-gold-ink">{count}</span>
          ) : null}
        </span>
        {action}
      </header>
      <div className={`flex min-h-0 flex-1 flex-col ${flush ? "" : "px-4 pb-4"}`}>{children}</div>
    </section>
  );
}

function SeeAll({ to }: { to: string }) {
  return (
    <Link to={to} className="shrink-0 whitespace-nowrap rounded-full border border-gold-line bg-card/60 px-2.5 py-1 text-[12px] font-semibold text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.5)] backdrop-blur-sm transition hover:bg-gold-tint">
      عرض الكل
    </Link>
  );
}

// a row of a card's list: a small glass card of its own that lifts a little under the pointer
const ROW = "group relative flex items-center gap-2.5 overflow-hidden rounded-[14px] border border-line/60 bg-card/55 shadow-[inset_0_1px_0_rgb(255_255_255/0.45),0_4px_14px_-8px_rgb(60_44_20/0.25)] transition hover:-translate-y-px hover:border-gold-line hover:bg-card/80 hover:shadow-[0_10px_24px_-12px_rgb(60_44_20/0.35)] motion-reduce:transition-none motion-reduce:hover:translate-y-0";
const LIST = "grid min-h-0 grid-cols-[minmax(0,1fr)] content-start gap-2 px-3 pb-3";

const TH = "px-2 py-2 text-start text-[12px] font-semibold text-ink-soft";

// ---------- calendar ----------

// the week's short day names, so all seven fit a narrow column whole
const DAY_NAMES: Record<string, string> = {
  السبت: "سبت", الأحد: "أحد", الإثنين: "اتنين", الاثنين: "اتنين", الثلاثاء: "تلات", الأربعاء: "أربع", الخميس: "خميس", الجمعة: "جمعة",
};
// a dot per kind of appointment in the day's list (the mockup's colors)
const DOT: Partial<Record<IconName, string>> = { phone: "bg-red", video: "bg-blue", office: "bg-orange" };

/** The week with today marked, then today's appointments in order, and «عرض كل المواعيد». */
function CalendarPanel({ now, items }: { now: Date; items?: Appointment[] | undefined }) {
  const week = cairoWeek(now);
  return (
    <Panel title="تقويم المواعيد" icon="calendar" action={<SeeAll to="/appointments" />} flush>
      <div className="flex items-center justify-between px-4 py-1.5">
        <button type="button" disabled aria-label="الأسبوع اللي فات" className="grid size-8 place-items-center rounded-full border border-line/60 bg-card/55 text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.45)] disabled:opacity-50">
          <Icon name="back" className="size-5" />
        </button>
        <p className="text-[16px] font-bold">{cairoMonth(now)}</p>
        <button type="button" disabled aria-label="الأسبوع الجاي" className="grid size-8 place-items-center rounded-full border border-line/60 bg-card/55 text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.45)] disabled:opacity-50">
          <Icon name="forward" className="size-5" />
        </button>
      </div>
      <ol className="mx-3 mb-3 grid grid-cols-7 rounded-[16px] border border-line/60 bg-card/45 px-1 py-2 text-center shadow-[inset_0_1px_0_rgb(255_255_255/0.45)]" aria-label="الأسبوع ده">
        {week.map((d) => (
          <li key={d.key} className="grid min-w-0 justify-items-center gap-1.5" aria-current={d.today ? "date" : undefined}>
            <span className={`max-w-full truncate text-[10.5px] ${d.today ? "font-bold text-gold-ink" : "text-ink-soft"}`}>{DAY_NAMES[d.weekday] ?? d.weekday}</span>
            <span className={`tabular grid size-8 place-items-center rounded-full text-[14px] font-semibold ${d.today ? "bg-gradient-to-b from-[#dcb98c] to-[#a87a48] text-[#2a2117] shadow-[inset_0_1px_0_rgb(255_255_255/0.6),0_0_0_4px_rgb(176_133_82/0.22),0_6px_14px_-4px_rgb(176_133_82/0.7)]" : ""}`}>{d.day}</span>
          </li>
        ))}
      </ol>
      <div className="flex flex-1 flex-col">
        <h3 className="px-4 pb-2 text-[14px] font-bold">مواعيد اليوم{items ? ` (${items.length})` : ""}</h3>
        {!items?.length ? (
          <p className="px-4 pt-1 pb-3 text-[13px] text-ink-soft">{items ? "مفيش مواعيد النهارده." : "…"}</p>
        ) : (
          <ul className={`${LIST} gap-1.5 pb-0`}>
            {items.map((a) => {
              const k = kindOf(a);
              return (
                <li key={a.id}>
                  <Link to="/appointments" className={`${ROW} gap-2 rounded-[12px] px-2.5 py-2 text-[12.5px]`}>
                    <span className={`size-2 shrink-0 rounded-full ${DOT[k.icon] ?? "bg-blue"}`} aria-hidden />
                    <span className="tabular shrink-0 font-semibold" dir="ltr">{clock(a.start)}</span>
                    <span className="min-w-0 flex-1 truncate">{k.text} - {client(a)}</span>
                    <Icon name="chevron-start" className="size-3.5 shrink-0 text-ink-soft transition group-hover:-translate-x-0.5 group-hover:text-gold-ink" />
                  </Link>
                </li>
              );
            })}
          </ul>
        )}
        {/* at the foot of the column, level with the other two */}
        <div className="mt-auto p-3">
          <Link to="/appointments" className="btn-gold flex min-h-10 items-center justify-center gap-2 rounded-full border text-[14px] font-semibold shadow-[inset_0_1px_0_rgb(255_255_255/0.4),0_8px_18px_-8px_rgb(140_98_58/0.8)] transition hover:brightness-105">
            <Icon name="calendar" className="size-4" />
            عرض كل المواعيد
          </Link>
        </div>
      </div>
    </Panel>
  );
}

// ---------- today's appointments ----------

const isOpen = (a: Appointment) => a.state === "confirmed" || a.state === "pending_payment";
const byStart = (a: Appointment, b: Appointment) => a.start.localeCompare(b.start);
const client = (a: Appointment) => a.name ?? "رقم من غير اسم";
// «09:00» as in the mockup (24 hours, Cairo), the same in both languages' directions
const clockFmt = new Intl.DateTimeFormat("en-GB", { timeZone: "Africa/Cairo", hour: "2-digit", minute: "2-digit", hourCycle: "h23" });
const clock = (iso: string) => clockFmt.format(new Date(iso));

/** The kind of appointment as the mockup draws it: the icon, then the words. */
function kindOf(a: Appointment): { icon: IconName; text: string } {
  if (a.service === "consultation_office" || a.service.startsWith("urf_")) return { icon: "office", text: label(BOOKING_SERVICES, a.service) };
  if (a.service === "consultation_urgent") return { icon: "phone", text: "استشارة عاجلة" };
  if (a.call_app === "meet" || a.call_app === "facetime") return { icon: "video", text: "استشارة فيديو" };
  return { icon: "phone", text: "استشارة هاتفية" };
}

/** The payment in the office's words (never «مدفوع» before a person checked the transfer). */
function paidOf(a: Appointment): { tone: string; text: string; paid: boolean } {
  switch (a.payment) {
    case "verified": return { tone: "green", text: "مدفوع", paid: true };
    case "proof_received": case "duplicate_suspected": return { tone: "blue", text: "بانتظار المراجعة", paid: false };
    case "partial": return { tone: "orange", text: "مدفوع جزئيًا", paid: false };
    case "not_received": return { tone: "red", text: "لم يتم الدفع", paid: false };
    case "refund_requested": case "refunded": return { tone: "grey", text: "استرداد", paid: false };
    default: return { tone: "orange", text: "بانتظار الدفع", paid: false };
  }
}

function TodayPanel({ items, pending, failed }: { items?: Appointment[] | undefined; pending: boolean; failed: boolean }) {
  const heads = ["الوقت", "العميل", "النوع", "حالة الدفع", "الإجراءات"];
  return (
    <Panel title="مواعيد اليوم" icon="clock" count={items?.length} action={<SeeAll to="/appointments" />} flush>
      {pending ? <Rows n={4} /> : failed ? (
        <Quiet icon="stat_calendar_blue" title="مواعيد اليوم مش متاحة دلوقتي">جرّب تاني بعد شوية.</Quiet>
      ) : !items?.length ? (
        <Quiet icon="stat_calendar_blue" title="مفيش مواعيد النهارده">مواعيد اليوم وحالة الدفع بتاعتها هتظهر هنا.</Quiet>
      ) : (
        <>
          {/* a phone: one row of two lines per appointment */}
          <ul className={`${LIST} sm:hidden`}>
            {items.map((a) => {
              const k = kindOf(a), p = paidOf(a);
              return (
                <li key={a.id} className={`${ROW} grid grid-cols-[auto_minmax(0,1fr)_auto] gap-x-3 gap-y-1 px-3 py-2.5 hover:translate-y-0`}>
                  <span className="tabular row-span-2 rounded-[10px] bg-gold-tint px-2 py-1 text-[14px] font-bold text-gold-ink" dir="ltr">{clock(a.start)}</span>
                  <span className="truncate font-bold">{client(a)}</span>
                  <Pill tone={p.tone}>{p.text}</Pill>
                  <span className="flex min-w-0 items-center gap-1.5 text-[13px] text-ink-soft"><Icon name={k.icon} className="size-4 shrink-0" /><span className="truncate">{k.text}</span></span>
                  <Actions paid={p.paid} />
                </li>
              );
            })}
          </ul>
          {/* wider: the mockup's table */}
          <div className="hidden overflow-x-auto px-3 pb-3 sm:block">
            <table className="w-full border-separate border-spacing-y-0.5 text-[12.5px] 2xl:text-[13.5px]">
              <thead>
                <tr>{heads.map((h) => <th key={h} className={`${TH} border-y border-line/60 bg-card/55 first:rounded-s-[12px] first:border-s last:rounded-e-[12px] last:border-e`}>{h}</th>)}</tr>
              </thead>
              <tbody>
                {items.map((a) => {
                  const k = kindOf(a), p = paidOf(a), t = TINT[k.icon] ?? TINT.phone!;
                  return (
                    <tr key={a.id} className="[&>td]:border-y [&>td]:border-transparent [&>td]:transition [&>td:first-child]:rounded-s-[12px] [&>td:last-child]:rounded-e-[12px] hover:[&>td]:border-gold-line/60 hover:[&>td]:bg-gold-tint/45">
                      <td className="whitespace-nowrap px-2 py-1" dir="ltr">
                        <span className="tabular inline-block rounded-full border border-line/70 bg-card/70 px-2 py-0.5 font-bold">{clock(a.start)}</span>
                      </td>
                      <td className="max-w-[7rem] truncate px-2 py-1 font-semibold 2xl:max-w-[10rem]">{client(a)}</td>
                      <td className="whitespace-nowrap px-2 py-1">
                        <span className="flex items-center gap-1.5">
                          <span className={`grid size-6 place-items-center rounded-lg bg-gradient-to-b ${t.tile} ${t.ink}`}><Icon name={k.icon} className="size-3.5" /></span>
                          {k.text}
                        </span>
                      </td>
                      <td className="px-2 py-1"><Pill tone={p.tone}>{p.text}</Pill></td>
                      <td className="px-2 py-1"><Actions paid={p.paid} /></td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </>
      )}
    </Panel>
  );
}

/** «التفاصيل» for a paid one, «التواصل» (the office's chats, in Chatwoot) for one still waiting for the money; ⋮ the day. */
function Actions({ paid }: { paid: boolean }) {
  const button = "grid min-h-7 place-items-center whitespace-nowrap rounded-full border border-line/70 bg-card/70 px-3 text-[12px] font-semibold text-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.5)] transition hover:border-gold-line hover:bg-gold-tint hover:text-gold-ink";
  return (
    <span className="flex items-center justify-end gap-1">
      {paid ? <Link to="/appointments" className={button}>التفاصيل</Link>
        : <button type="button" onClick={() => void openChatwoot()} className={button}>التواصل</button>}
      <Link to="/appointments" aria-label="افتح المواعيد" className="grid size-7 place-items-center rounded-full text-ink-soft transition hover:bg-gold-tint hover:text-gold-ink">
        <Icon name="more" className="size-5" />
      </Link>
    </span>
  );
}

// ---------- alerts ----------

function alertLook(t: DashboardTask): { icon: OwnerImage; title: string } {
  if (URGENT.has(t.type)) return { icon: "alert_urgent_red", title: label(TASK_TYPES, t.type) };
  if (t.late) return { icon: "alert_task_orange", title: "مهمة متأخرة" };
  if (t.state === "done") return { icon: "alert_receipt_green", title: `${label(TASK_TYPES, t.type)} - خلصت` };
  return { icon: "alert_message_blue", title: label(TASK_TYPES, t.type) };
}

function AlertsPanel({ items, pending, now }: { items?: DashboardTask[] | undefined; pending: boolean; now: Date }) {
  return (
    <Panel title="التنبيهات الأخيرة" icon="bell" count={items?.length} action={<SeeAll to="/tasks" />} flush>
      {pending ? <Rows n={4} /> : !items?.length ? (
        <Quiet icon="alert_message_blue" title="مفيش تنبيهات جديدة">أي مهمة حازم يفتحها هتظهر هنا.</Quiet>
      ) : (
        <ul className={LIST}>
          {items.map((t) => {
            const a = alertLook(t);
            return (
              <li key={t.id}>
                <Link to={`/tasks/${t.id}`} className={`${ROW} px-2.5 py-2`}>
                  <span className="grid size-9 shrink-0 place-items-center rounded-full border border-line/60 bg-card/70 shadow-[inset_0_1px_0_rgb(255_255_255/0.6)]">
                    <OwnerImg name={a.icon} className="size-7 object-contain" />
                  </span>
                  <span className="grid min-w-0 flex-1 gap-0.5 leading-snug">
                    <span className="truncate text-[13.5px] font-bold">{a.title}</span>
                    <span className="truncate text-[12px] text-ink-soft">{who(t)}{t.staff_name ? ` - مع ${t.staff_name}` : ""}</span>
                  </span>
                  <span className="shrink-0 rounded-full bg-card-2/70 px-2 py-0.5 text-[10.5px] text-ink-soft">{timeAgo(t.created_at, now)}</span>
                </Link>
              </li>
            );
          })}
        </ul>
      )}
    </Panel>
  );
}

// ---------- urgent and late tasks ----------

// the thin line at a task's start, in its priority's color
const BAR: Record<string, string> = { orange: "bg-orange", red: "bg-red", blue: "bg-blue" };

function priority(t: DashboardTask): { tone: string; text: string } {
  if (URGENT.has(t.type)) return { tone: "orange", text: "عاجلة" };
  if (t.late) return { tone: "red", text: "متأخرة" };
  return { tone: "blue", text: "عادية" };
}

function PriorityPanel({ items, pending, now }: { items?: DashboardTask[] | undefined; pending: boolean; now: Date }) {
  return (
    <Panel title="المهام العاجلة والمتأخرة" icon="tasks" count={items?.length} action={<SeeAll to="/tasks" />} flush>
      {pending ? <Rows n={4} /> : !items?.length ? (
        <Quiet icon="alert_receipt_green" title="مفيش مهام مستنياك">كل المهام اتعملت في وقتها.</Quiet>
      ) : (
        <ul className={LIST}>
          {items.map((t) => {
            const p = priority(t);
            return (
              <li key={t.id}>
                <Link to={`/tasks/${t.id}`} aria-label={`افتح مهمة ${who(t)}`} className={`${ROW} px-3 py-2.5 text-[13px]`}>
                  <span aria-hidden className={`absolute inset-y-2 start-0 w-[3px] rounded-full ${BAR[p.tone] ?? "bg-blue"}`} />
                  <span className="grid size-[18px] shrink-0 place-items-center rounded-[6px] border border-ink-soft/40 bg-card/70 shadow-[inset_0_1px_0_rgb(255_255_255/0.6)] transition group-hover:border-gold" aria-hidden />
                  <span className="min-w-0 flex-1 truncate">{label(TASK_TYPES, t.type)}{t.client_name ? ` - ${t.client_name}` : ""}</span>
                  <Pill tone={p.tone}>{p.text}</Pill>
                  <span className="w-14 shrink-0 text-end text-[11.5px] text-ink-soft">{cairoDayWord(t.due_at, now)}</span>
                </Link>
              </li>
            );
          })}
        </ul>
      )}
    </Panel>
  );
}

// ---------- the next appointments ----------

// the date tile of a row: the day's number over its name, in Cairo
const dayNum = new Intl.DateTimeFormat("en-US", { timeZone: "Africa/Cairo", day: "numeric" });
const dayName = new Intl.DateTimeFormat("ar-EG", { timeZone: "Africa/Cairo", weekday: "long" });
// each kind's own glass tint: the tile, its icon and the thin line at the row's start
const TINT: Partial<Record<IconName, { tile: string; ink: string; bar: string }>> = {
  phone: { tile: "from-gold/30 to-gold-tint", ink: "text-gold-ink", bar: "bg-gold" },
  video: { tile: "from-blue/25 to-blue-bg", ink: "text-blue-ink", bar: "bg-blue" },
  office: { tile: "from-orange/25 to-orange-bg", ink: "text-orange-ink", bar: "bg-orange" },
};

/**
 * The next four open appointments after today (two weeks ahead), nearest first: each one a small glass card with its
 * date tile; the list scrolls inside the row with a thin gold bar.
 */
function UpcomingPanel({ now }: { now: Date }) {
  const from = cairoDate(new Date(now.getTime() + 86_400_000));
  const to = cairoDate(new Date(now.getTime() + 14 * 86_400_000));
  const next = useQuery({
    queryKey: ["booking", "appointments", "next", from, to],
    queryFn: () => api<Appointment[]>("GET", `/booking/appointments?from=${from}&to=${to}`),
  });
  const items = next.data?.filter(isOpen).sort(byStart).slice(0, 4);
  return (
    <Panel title="أقرب المواعيد القادمة" icon="calendar" count={items?.length} action={<SeeAll to="/appointments" />} className="min-h-0" flush>
      {next.isPending ? <Rows n={4} /> : next.isError ? (
        <Quiet icon="top_calendar" title="المواعيد الجاية مش متاحة دلوقتي">جرّب تاني بعد شوية.</Quiet>
      ) : !items?.length ? (
        <Quiet icon="top_calendar" title="مفيش مواعيد جاية">أقرب الاستشارات والمقابلات هتظهر هنا بالترتيب.</Quiet>
      ) : (
        <ul className={`${LIST} soft-scroll overflow-y-auto`} aria-label="أقرب المواعيد">
          {items.map((a) => {
            const k = kindOf(a);
            const t = TINT[k.icon] ?? TINT.phone!;
            const at = new Date(a.start);
            return (
              <li key={a.id}>
                <Link to="/appointments"
                  className={`${ROW} gap-3 py-2 ps-2 pe-3`}>
                  <span aria-hidden className={`absolute inset-y-2 start-0 w-[3px] rounded-full ${t.bar}`} />
                  <span className={`grid w-12 shrink-0 place-items-center rounded-[11px] bg-gradient-to-b ${t.tile} py-1 leading-none shadow-[inset_0_1px_0_rgb(255_255_255/0.5)]`}>
                    <span className="tabular text-[17px] font-bold">{dayNum.format(at)}</span>
                    <span className="mt-0.5 max-w-full truncate px-0.5 text-[9.5px] text-ink-soft">{dayName.format(at)}</span>
                  </span>
                  <span className="grid min-w-0 flex-1 gap-0.5">
                    <span className="truncate text-[13.5px] font-bold 2xl:text-[14.5px]">{client(a)}</span>
                    <span className={`flex min-w-0 items-center gap-1.5 text-[12px] ${t.ink}`}>
                      <Icon name={k.icon} className="size-3.5 shrink-0" />
                      <span className="truncate">{k.text}</span>
                    </span>
                  </span>
                  <span className="grid shrink-0 justify-items-end gap-0.5">
                    <span className="tabular rounded-full border border-line/70 bg-card/70 px-2.5 py-0.5 text-[12.5px] font-bold" dir="ltr">{clock(a.start)}</span>
                    <span className="text-[11px] text-ink-soft">{cairoDayWord(a.start, now)}</span>
                  </span>
                  <Icon name="chevron-start" className="size-4 shrink-0 text-ink-soft transition group-hover:-translate-x-0.5 group-hover:text-gold-ink" />
                </Link>
              </li>
            );
          })}
        </ul>
      )}
    </Panel>
  );
}

// ---------- small parts ----------

const PILL: Record<string, string> = {
  green: "text-green-ink bg-green-bg", blue: "text-blue-ink bg-blue-bg", orange: "text-orange-ink bg-orange-bg",
  red: "text-red-ink bg-red-bg", grey: "text-grey-ink bg-grey-bg",
};

/** The mockup's small status chip (the shared Chip is wider, for the tables of the other pages). */
function Pill({ tone, children }: { tone: string; children: ReactNode }) {
  return <span className={`inline-block whitespace-nowrap rounded-[6px] px-2 py-0.5 text-[11.5px] font-semibold ${PILL[tone] ?? PILL.grey}`}>{children}</span>;
}

function Quiet({ icon, title, children }: { icon: OwnerImage; title: string; children: string }) {
  return (
    <div className="grid flex-1 place-items-center px-4 pt-6 pb-8 text-center">
      <span className="grid justify-items-center gap-1.5">
        <OwnerImg name={icon} className="size-10 object-contain" />
        <span className="font-bold">{title}</span>
        <span className="text-[13px] text-ink-soft">{children}</span>
      </span>
    </div>
  );
}

function Rows({ n }: { n: number }) {
  return (
    <div className="grid gap-2 px-4 pb-4" role="status" aria-label="جاري التحميل">
      {Array.from({ length: n }, (_, i) => <div key={i} className="h-12 animate-pulse rounded-lg bg-card-2" />)}
    </div>
  );
}
