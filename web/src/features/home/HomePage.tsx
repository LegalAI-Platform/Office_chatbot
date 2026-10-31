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
import { Chip, ErrorState } from "../../shared/ui/ui";

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
            alerts and the tasks on the left. Narrower (a laptop, 110% zoom): the left column goes under the middle one;
            a phone: one column. */}
        <div className="grid gap-5 xl:grid-cols-[20.5rem_minmax(0,1fr)] 2xl:grid-cols-[20.5rem_minmax(0,1.45fr)_minmax(0,1fr)]">
          <CalendarPanel now={now} />
          <div className="grid min-w-0 content-start gap-5">
            <TodayPanel items={todayList} pending={appointments.isPending} failed={appointments.isError} />
            <UpcomingPanel now={now} />
          </div>
          <div className="grid min-w-0 content-start gap-5 xl:col-start-2 2xl:col-start-auto">
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
    <section className={`grid min-h-[8.5rem] grid-cols-[1fr_auto] rounded-[12px] border px-5 pt-4 pb-3 text-kpi-ink shadow-[0_6px_20px_rgb(0_0_0/0.06)] ${k.card}`}>
      <div className="grid content-start">
        <h2 className="text-[18px] leading-snug font-bold">{title}</h2>
        <p className={`tabular text-[38px] leading-tight font-bold ${k.ink}`}>
          {value === null ? "—" : value === undefined ? <span className="inline-block h-9 w-7 animate-pulse rounded bg-white/70" /> : value}
        </p>
      </div>
      <OwnerImg name={icon} className="size-14 object-contain" />
      <div className="col-span-2 flex items-end justify-between gap-2">
        <Link to={to} className={`flex items-center gap-2 text-[15px] font-bold ${k.ink} hover:underline`}>
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
      <svg viewBox={`0 0 ${w} ${h}`} className="h-10 w-32" aria-hidden>
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
    <svg viewBox={`0 0 ${w} ${h}`} className="h-10 w-32" role="img" aria-label={`آخر 7 أيام: ${[...values].join("، ")}`}>
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

function Panel({ title, action, children, className = "", flush = false }: {
  title: string; action?: ReactNode; children: ReactNode; className?: string; flush?: boolean;
}) {
  const id = useId();
  return (
    <section aria-labelledby={id} className={`flex min-w-0 flex-col rounded-[12px] border border-line bg-card shadow-[0_6px_20px_rgb(0_0_0/0.05)] ${className}`}>
      <header className="flex items-center justify-between gap-2 px-4 pt-4 pb-3">
        <h2 id={id} className="text-[20px] font-bold">{title}</h2>
        {action}
      </header>
      <div className={`flex flex-1 flex-col ${flush ? "" : "px-4 pb-4"}`}>{children}</div>
    </section>
  );
}

function SeeAll({ to }: { to: string }) {
  return (
    <Link to={to} className="rounded-[8px] border border-gold-line bg-gold-tint px-3.5 py-1 text-[13px] font-semibold text-gold-ink hover:brightness-95">
      عرض الكل
    </Link>
  );
}

const TH = "px-3 py-2.5 text-start text-[13px] font-semibold text-ink-soft";

// ---------- calendar ----------

const DAY_NAMES: Record<string, string> = { الإثنين: "الإثنين", الاثنين: "الإثنين" };

function CalendarPanel({ now }: { now: Date }) {
  const week = cairoWeek(now);
  const slots = ["09:00", "11:30", "14:00", "16:00", "17:30"];
  return (
    <Panel title="تقويم المواعيد" action={<SeeAll to="/appointments" />} className="self-start" flush>
      <div className="flex items-center justify-between px-5 py-2">
        <button type="button" disabled aria-label="الأسبوع اللي فات" className="text-gold-ink disabled:opacity-50">
          <Icon name="back" className="size-5" />
        </button>
        <p className="text-[18px] font-bold">{cairoMonth(now)}</p>
        <button type="button" disabled aria-label="الأسبوع الجاي" className="text-gold-ink disabled:opacity-50">
          <Icon name="forward" className="size-5" />
        </button>
      </div>
      <ol className="grid grid-cols-7 px-2 pb-3 text-center" aria-label="الأسبوع ده">
        {week.map((d) => (
          <li key={d.key} className="grid justify-items-center gap-2" aria-current={d.today ? "date" : undefined}>
            <span className="text-[11px] text-ink-soft">{DAY_NAMES[d.weekday] ?? d.weekday}</span>
            <span className={`tabular grid size-9 place-items-center rounded-full text-[15px] font-semibold ${d.today ? "bg-[#e3c896] text-[#2a2117] shadow-[0_0_0_4px_rgb(201_169_106/0.25)]" : ""}`}>{d.day}</span>
          </li>
        ))}
      </ol>
      <div className="relative grid border-t border-line px-3 pt-2 pb-4">
        {slots.map((h) => (
          <div key={h} className="flex min-h-[3.25rem] items-center gap-3">
            <span className="tabular w-11 shrink-0 text-[13px] text-ink-soft" dir="ltr">{h}</span>
            <span className="h-11 flex-1 rounded-[8px] border border-dashed border-line" />
          </div>
        ))}
        <div className="absolute inset-0 grid place-items-center ps-14">
          <p className="max-w-[12rem] rounded-lg border border-line bg-card px-3 py-2 text-center text-[12px] text-ink-soft shadow-[var(--shadow)]">
            مفيش مواعيد متسجلة. أول ما الحجز يشتغل هتظهر هنا بألوانها.
          </p>
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
    <Panel title="مواعيد اليوم" action={<SeeAll to="/appointments" />} flush>
      {pending ? <Rows n={4} /> : failed ? (
        <Quiet icon="stat_calendar_blue" title="مواعيد اليوم مش متاحة دلوقتي">جرّب تاني بعد شوية.</Quiet>
      ) : !items?.length ? (
        <Quiet icon="stat_calendar_blue" title="مفيش مواعيد النهارده">مواعيد اليوم وحالة الدفع بتاعتها هتظهر هنا.</Quiet>
      ) : (
        <>
          {/* a phone: one row of two lines per appointment */}
          <ul className="grid grid-cols-[minmax(0,1fr)] divide-y divide-line border-t border-line sm:hidden">
            {items.map((a) => {
              const k = kindOf(a), p = paidOf(a);
              return (
                <li key={a.id} className="grid grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-x-3 gap-y-1 px-4 py-3">
                  <span className="tabular row-span-2 text-[15px] font-bold" dir="ltr">{clock(a.start)}</span>
                  <span className="truncate font-bold">{client(a)}</span>
                  <Chip tone={p.tone}>{p.text}</Chip>
                  <span className="flex min-w-0 items-center gap-1.5 text-[13px] text-ink-soft"><Icon name={k.icon} className="size-4 shrink-0" /><span className="truncate">{k.text}</span></span>
                  <Actions paid={p.paid} />
                </li>
              );
            })}
          </ul>
          {/* wider: the mockup's table */}
          <div className="hidden overflow-x-auto px-3 pb-3 sm:block">
            <table className="w-full min-w-[34rem]">
              <thead>
                <tr className="bg-card-2">{heads.map((h) => <th key={h} className={`${TH} first:rounded-s-lg last:rounded-e-lg`}>{h}</th>)}</tr>
              </thead>
              <tbody className="divide-y divide-line text-[14px]">
                {items.map((a) => {
                  const k = kindOf(a), p = paidOf(a);
                  return (
                    <tr key={a.id} className="hover:bg-card-2">
                      <td className="tabular whitespace-nowrap px-3 py-2.5 font-semibold" dir="ltr">{clock(a.start)}</td>
                      <td className="max-w-[10rem] truncate px-3 py-2.5">{client(a)}</td>
                      <td className="whitespace-nowrap px-3 py-2.5">
                        <span className="flex items-center gap-2"><Icon name={k.icon} className="size-[18px] text-ink-soft" />{k.text}</span>
                      </td>
                      <td className="px-3 py-2.5"><Chip tone={p.tone}>{p.text}</Chip></td>
                      <td className="px-3 py-2.5"><Actions paid={p.paid} /></td>
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
  const button = "grid min-h-8 place-items-center whitespace-nowrap rounded-[8px] border border-line px-3 text-[13px] font-semibold text-ink hover:border-gold-line";
  return (
    <span className="flex items-center justify-end gap-1.5">
      {paid ? <Link to="/appointments" className={button}>التفاصيل</Link>
        : <button type="button" onClick={() => void openChatwoot()} className={button}>التواصل</button>}
      <Link to="/appointments" aria-label="افتح المواعيد" className="grid size-8 place-items-center rounded-[8px] text-ink-soft hover:bg-card-2">
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
    <Panel title="التنبيهات الأخيرة" action={<SeeAll to="/tasks" />} flush>
      {pending ? <Rows n={4} /> : !items?.length ? (
        <Quiet icon="alert_message_blue" title="مفيش تنبيهات جديدة">أي مهمة حازم يفتحها هتظهر هنا.</Quiet>
      ) : (
        <ul className="grid divide-y divide-line border-t border-line">
          {items.map((t) => {
            const a = alertLook(t);
            return (
              <li key={t.id}>
                <Link to={`/tasks/${t.id}`} className="flex items-center gap-3 px-4 py-3.5 hover:bg-card-2">
                  <OwnerImg name={a.icon} className="size-9 shrink-0 object-contain" />
                  <span className="grid min-w-0 flex-1 gap-0.5 leading-snug">
                    <span className="truncate text-[16px] font-bold">{a.title}</span>
                    <span className="truncate text-[13px] text-ink-soft">{who(t)}{t.staff_name ? ` - مع ${t.staff_name}` : ""}</span>
                  </span>
                  <span className="shrink-0 text-[12px] text-ink-soft">{timeAgo(t.created_at, now)}</span>
                  <span className="grid size-8 shrink-0 place-items-center rounded-full border border-line text-ink-soft" aria-hidden>
                    <Icon name="chevron-start" className="size-4" />
                  </span>
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

function priority(t: DashboardTask): { tone: string; text: string; dot: string } {
  if (URGENT.has(t.type)) return { tone: "red", text: "عاجلة", dot: "bg-red" };
  if (t.late) return { tone: "orange", text: "متأخرة", dot: "bg-red" };
  return { tone: "blue", text: "عادية", dot: "bg-orange" };
}

function PriorityPanel({ items, pending, now }: { items?: DashboardTask[] | undefined; pending: boolean; now: Date }) {
  const heads = ["الأولوية", "المهمة", "العميل", "الموعد النهائي", "الحالة", ""];
  return (
    <Panel title="المهام العاجلة والمتأخرة" action={<SeeAll to="/tasks" />} flush>
      {pending ? <Rows n={4} /> : !items?.length ? (
        <Quiet icon="alert_receipt_green" title="مفيش مهام مستنياك">كل المهام اتعملت في وقتها.</Quiet>
      ) : (
        <div className="overflow-x-auto px-3 pb-3">
          <table className="w-full min-w-[30rem]">
            <thead>
              <tr className="bg-card-2">{heads.map((h, i) => <th key={i} className={`${TH} whitespace-nowrap !px-2 first:rounded-s-lg last:rounded-e-lg`}>{h}</th>)}</tr>
            </thead>
            <tbody className="divide-y divide-line text-[14px]">
              {items.map((t) => {
                const p = priority(t);
                const day = cairoDayWord(t.due_at, now);
                return (
                  <tr key={t.id} className="hover:bg-card-2">
                    <td className="px-2 py-2.5">
                      <OwnerImg name={p.tone === "blue" ? "alert_task_orange" : "alert_urgent_red"} className="size-6 object-contain" />
                    </td>
                    <td className="px-2 py-2.5 leading-snug">
                      <span className="flex items-center gap-2"><span className={`size-2 shrink-0 rounded-full ${p.dot}`} aria-hidden />{label(TASK_TYPES, t.type)}</span>
                    </td>
                    <td className="px-2 py-2.5 leading-snug">{who(t)}</td>
                    <td className={`whitespace-nowrap px-2 py-2.5 ${day === "أمس" || (t.late && day !== "اليوم") ? "font-semibold text-red-ink" : ""}`}>{day}</td>
                    <td className="px-2 py-2.5"><Chip tone={p.tone}>{p.text}</Chip></td>
                    <td className="px-2 py-2.5">
                      <Link to={`/tasks/${t.id}`} aria-label={`افتح مهمة ${who(t)}`} className="grid size-8 place-items-center rounded-[8px] border border-line text-ink hover:border-gold-line">
                        <Icon name="more" className="size-5" />
                      </Link>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </Panel>
  );
}

// ---------- the next appointments ----------

/** The next four open appointments after today (two weeks ahead), nearest first. */
function UpcomingPanel({ now }: { now: Date }) {
  const from = cairoDate(new Date(now.getTime() + 86_400_000));
  const to = cairoDate(new Date(now.getTime() + 14 * 86_400_000));
  const next = useQuery({
    queryKey: ["booking", "appointments", "next", from, to],
    queryFn: () => api<Appointment[]>("GET", `/booking/appointments?from=${from}&to=${to}`),
  });
  const items = next.data?.filter(isOpen).sort(byStart).slice(0, 4);
  return (
    <Panel title="أقرب المواعيد القادمة" action={<SeeAll to="/appointments" />} flush>
      {next.isPending ? <Rows n={4} /> : next.isError ? (
        <Quiet icon="top_calendar" title="المواعيد الجاية مش متاحة دلوقتي">جرّب تاني بعد شوية.</Quiet>
      ) : !items?.length ? (
        <Quiet icon="top_calendar" title="مفيش مواعيد جاية">أقرب الاستشارات والمقابلات هتظهر هنا بالترتيب.</Quiet>
      ) : (
        <ul className="grid grid-cols-[minmax(0,1fr)] divide-y divide-line border-t border-line" aria-label="أقرب المواعيد">
          {items.map((a) => {
            const k = kindOf(a);
            return (
              <li key={a.id}>
                <Link to="/appointments" className="flex min-h-[3rem] items-center gap-3 px-4 py-2.5 text-[14px] hover:bg-card-2">
                  <span className="tabular w-16 shrink-0 font-semibold" dir="ltr">{clock(a.start)}</span>
                  <Icon name={k.icon} className="size-[18px] shrink-0 text-ink-soft" />
                  <span className="min-w-0 flex-1 truncate">{k.text} - {client(a)}</span>
                  <span className="shrink-0 text-[12px] text-ink-soft">{cairoDayWord(a.start, now)}</span>
                  <Icon name="chevron-start" className="size-4 shrink-0 text-ink-soft" />
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
