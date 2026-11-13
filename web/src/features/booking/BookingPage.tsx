import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient, type QueryClient } from "@tanstack/react-query";
import { ApiError, errorMessage } from "../../shared/api/client";
import { can, useMe } from "../../shared/api/session";
import { APPOINTMENT_STATES, BOOKING_SERVICES, CALL_APPS, label, matterLabel, PAYMENT_STATES, REMINDER_KINDS, REMINDER_SKIPS } from "../../shared/lib/labels";
import { cairoTime, cairoWhen } from "../../shared/lib/time";
import { useUrlState } from "../../shared/lib/url";
import { Phone } from "../../shared/ui/caseCard";
import { Icon, type IconName } from "../../shared/ui/Icon";
import { confirmAction, toast } from "../../shared/ui/feedback";
import { Button, Chip, Empty, ErrorState, Field, Loading } from "../../shared/ui/ui";
import { booking, cairoDate, hhmm, monthOf, shiftMonth, type Appointment, type Day, type Done, type Reminder } from "./api";
import { Confirm, Dialog, Glow, longDay, monthQuery, Segmented, SELECT, settingsQuery, STATUS, TimeSelect, TitleTile, Told, WEEK } from "./parts";

// the calendar's column heads: one or two letters, so the month fits a narrow column whole
const SHORT_DAYS = ["ح", "ن", "ث", "ر", "خ", "ج", "س"];
const STATE_TONE: Record<string, string> = {
  pending_payment: "orange", expired: "grey", confirmed: "blue", completed: "green", no_show: "orange", cancelled_by_client: "grey", cancelled_by_office: "grey",
};
// the dot on the timeline, by the tone of the booking's state (written out in full so Tailwind keeps them)
const DOT: Record<string, string> = {
  green: "bg-green border-green", grey: "bg-card border-ink-soft/50", blue: "bg-blue border-blue", orange: "bg-orange border-orange", red: "bg-red border-red",
};
const SERVICE_ICON = (service: string): IconName =>
  service === "consultation_office" ? "office" : service === "consultation_urgent" ? "alert" : service.startsWith("urf_") ? "document" : "video";
/** «حجز واحد»، «حجزان»، «3 حجوزات»، «11 حجزًا». */
const bookingsWords = (n: number) => (n === 1 ? "حجز واحد" : n === 2 ? "حجزان" : n <= 10 ? `${n} حجوزات` : `${n} حجزًا`);
const meetingsWords = (n: number) => (n === 1 ? "موعد واحد" : n === 2 ? "موعدان" : n <= 10 ? `${n} مواعيد` : `${n} موعدًا`);

const dayQuery = (day: string) => ({ queryKey: ["booking", "appointments", day, day], queryFn: () => booking.appointments(day, day), refetchInterval: 60_000 });

/** This month's calendar, today's bookings and the settings, fetched while the pointer is still on the link (idea from PR #14). */
export function prefetchData(qc: QueryClient) {
  const today = cairoDate(new Date());
  void qc.prefetchQuery(monthQuery(monthOf(today)));
  void qc.prefetchQuery(dayQuery(today));
  void qc.prefetchQuery(settingsQuery);
}

type View = "day" | "all";
const VIEWS: { value: View; label: string; icon: IconName }[] = [
  { value: "day", label: "جدول اليوم", icon: "calendar" },
  { value: "all", label: "كل المواعيد", icon: "clients" },
];

const shiftDay = (day: string, by: number) => {
  const d = new Date(`${day}T12:00:00Z`);
  d.setUTCDate(d.getUTCDate() + by);
  return d.toISOString().slice(0, 10);
};

/**
 * «المواعيد» (TASK 08, DECISIONS v1.12 B-24): the office's daily screen. The picked day's schedule (today when the page
 * opens) is the page; a small month beside it picks another day. «تعديل هذا اليوم» changes that one day in a small
 * window, and what is set once (the week, the booking settings, the office's details) lives in «إعدادات المواعيد».
 */
export function BookingPage() {
  const { data: me } = useMe();
  // opens on today (idea from PR #14): the first question every morning is «who is coming today»
  const today = cairoDate(new Date());
  // ?day=2026-11-05 (the home page's links) opens on that day
  const asked = useSearchParams()[0].get("day");
  const [picked, setPicked] = useState(asked && /^\d{4}-\d{2}-\d{2}$/.test(asked) ? asked : today);
  const [view, setView] = useUrlState<View>("show", "day", ["day", "all"]);
  const mayChange = can(me, "schedule.manage");
  const [monthOpen, setMonthOpen] = useState(false);
  const pick = (d: string) => { setPicked(d); setView("day"); };

  return (
    <div className="@container relative isolate grid gap-4 pb-4">
      <Glow />
      <header className="flex flex-wrap items-center gap-x-4 gap-y-3">
        <div className="flex min-w-0 flex-1 basis-72 flex-wrap items-center gap-x-4 gap-y-2">
          <TitleTile icon="calendar">المواعيد</TitleTile>
          <Status />
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Segmented label="العرض" value={view} onChange={setView} options={VIEWS} small />
          {mayChange || can(me, "office.manage") ? (
            <Link to="/appointments/settings" aria-label="إعدادات المواعيد" title="إعدادات المواعيد"
              className="flex min-h-10 items-center gap-2 rounded-full border border-line/60 bg-card/60 px-3.5 text-[13.5px] font-semibold text-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.45)] transition hover:border-gold-line hover:bg-gold-tint hover:text-gold-ink motion-reduce:transition-none">
              <Icon name="settings" className="size-[18px] text-gold-ink" /><span>إعدادات المواعيد</span>
            </Link>
          ) : null}
        </div>
      </header>

      {view === "day" ? (
        <div className="grid items-start gap-4 @3xl:grid-cols-[minmax(0,1fr)_19.5rem]">
          <aside className="grid gap-2 @3xl:sticky @3xl:top-4 @3xl:col-start-2 @3xl:row-start-1" aria-label="اختر يومًا">
            {/* a phone shows the month only when asked: the day's arrows are enough most of the time */}
            <button type="button" aria-expanded={monthOpen} onClick={() => setMonthOpen(!monthOpen)}
              className="liquid flex min-h-11 items-center gap-2 rounded-full px-4 text-[13.5px] font-semibold @3xl:hidden">
              <Icon name="calendar" className="size-4 text-gold-ink" /><span className="flex-1 text-start">{monthOpen ? "إخفاء الشهر" : "اختر يومًا من الشهر"}</span>
              <Icon name="chevron-down" className={`size-4 transition ${monthOpen ? "rotate-180" : ""}`} />
            </button>
            <div className={monthOpen ? "" : "hidden @3xl:block"}>
              <Calendar today={today} picked={picked} onPick={(d) => { pick(d); setMonthOpen(false); }} />
            </div>
          </aside>
          <div className="min-w-0 @3xl:col-start-1 @3xl:row-start-1">
            <DayPanel key={picked} day={picked} today={today} mayChange={mayChange} onPick={pick} />
          </div>
        </div>
      ) : <Bookings />}
    </div>
  );
}

/** Whether Hazem books from WhatsApp now, and what is missing if not. */
function Status() {
  const s = useQuery(settingsQuery);
  if (!s.data) return null;
  const missing = [
    !s.data.calendar_key && "مفتاح Google Calendar (يضيفه صاحب المشروع في ملف .env)",
    !s.data.calendar_id && "البريد الإلكتروني لتقويم المستشار (في «الحجز من واتساب»)",
    !s.data.texts_approved && "اعتماد نصوص الحجز (من «نصوص حازم»)",
  ].filter(Boolean);
  const ok = s.data.enabled && missing.length === 0;
  return (
    <p role="status" className={`flex w-fit items-start gap-1.5 rounded-full border px-3 py-1 text-[12.5px] font-semibold leading-relaxed ${ok ? "border-green/30 bg-green-bg text-green-ink" : "rounded-[14px] border-orange/30 bg-orange-bg text-orange-ink"}`}>
      {ok ? <span aria-hidden className="mt-[0.45rem] size-2 shrink-0 animate-pulse rounded-full bg-green motion-reduce:animate-none" /> : <Icon name="alert-circle" className="mt-0.5 size-4 shrink-0" />}
      <span>
        {s.data.enabled ? "يحجز حازم المواعيد عبر واتساب." : "الحجز عبر واتساب متوقف: يعرض حازم الاستشارة دون مواعيد."}
        {missing.length > 0 ? ` لم يُضبط بعد: ${missing.join("، ")}.` : ""}
      </span>
    </p>
  );
}

// ---------- the month: small, only to pick a day ----------

function Calendar({ today, picked, onPick }: { today: string; picked: string; onPick: (day: string) => void }) {
  const [month, setMonth] = useState(() => monthOf(picked || today));
  // picking a day from the day's arrows moves the month with it
  useEffect(() => { setMonth(monthOf(picked)); }, [picked]);
  const days = useQuery(monthQuery(month));
  const first = days.data?.[0];
  const lead = first ? WEEK.indexOf(new Date(`${first.day}T12:00:00Z`).getUTCDay()) : 0;
  const nav = "grid size-8 place-items-center rounded-full text-ink-soft transition hover:bg-gold-tint hover:text-gold-ink motion-reduce:transition-none";

  return (
    <section className="liquid relative grid min-w-0 gap-2 overflow-hidden rounded-[20px] p-3">
      <div className="flex items-center gap-1">
        <h3 className="flex-1 ps-1 text-[15px] font-bold tabular" aria-live="polite">
          {new Intl.DateTimeFormat("ar-EG-u-nu-latn", { month: "long", year: "numeric", timeZone: "UTC" }).format(new Date(`${month}-15T00:00:00Z`))}
        </h3>
        {month !== monthOf(today) || picked !== today ? (
          <button type="button" className="me-1 rounded-full border border-gold-line bg-gold-tint px-2.5 py-0.5 text-[12px] font-bold text-gold-ink transition hover:brightness-105" onClick={() => { setMonth(monthOf(today)); onPick(today); }}>اليوم</button>
        ) : null}
        <button type="button" className={nav} aria-label="الشهر السابق" onClick={() => setMonth(shiftMonth(month, -1))}><Icon name="chevron-start" className="size-4" /></button>
        <button type="button" className={nav} aria-label="الشهر التالي" onClick={() => setMonth(shiftMonth(month, 1))}><Icon name="chevron-end" className="size-4" /></button>
      </div>
      {days.isPending ? <Loading /> : days.isError ? <ErrorState message={errorMessage(days.error)} onRetry={() => void days.refetch()} /> : (
        <div className="grid grid-cols-7 gap-0.5 text-center" role="grid" aria-label="أيام الشهر">
          {WEEK.map((w) => <span key={w} className="pb-1 text-[11px] font-bold text-ink-soft">{SHORT_DAYS[w]}</span>)}
          {Array.from({ length: lead }, (_, i) => <span key={`x${i}`} />)}
          {days.data.map((d) => {
            const on = d.day === picked;
            const isToday = d.day === today;
            const off = d.status === "closed" || d.status === "weekly_off";
            const past = d.day < today;
            return (
              <button key={d.day} type="button" role="gridcell" aria-selected={on} onClick={() => onPick(d.day)}
                aria-label={`${d.day}: ${STATUS[d.status].word}${d.bookings ? `، ${bookingsWords(d.bookings)}` : ""}`}
                aria-current={isToday ? "date" : undefined} title={`${STATUS[d.status].word}${d.bookings ? ` · ${bookingsWords(d.bookings)}` : ""}`}
                className={`relative grid h-11 min-w-0 cursor-pointer place-items-center rounded-[10px] text-[13.5px] tabular transition motion-reduce:transition-none ${on
                  ? "btn-gold font-bold shadow-[0_6px_14px_-6px_rgb(140_98_58/0.8)]"
                  : isToday ? "bg-gold-tint font-bold text-gold-ink ring-1 ring-gold-line" : off ? "text-ink-soft/70 hover:bg-card/80" : `hover:bg-card/90 ${past ? "text-ink-soft" : "font-semibold"}`}`}>
                <span className={d.status === "closed" && !on ? "text-red-ink line-through decoration-2" : ""}>{Number(d.day.slice(8))}</span>
                {/* how full the day is: one dot per booking, up to three */}
                {d.bookings && !off ? (
                  <span aria-hidden className="absolute bottom-1 flex gap-0.5">
                    {Array.from({ length: Math.min(3, d.bookings) }, (_, i) => <span key={i} className={`size-1 rounded-full ${on ? "bg-white" : "bg-gold"}`} />)}
                  </span>
                ) : null}
                {d.status === "custom_hours" || d.status === "partly_closed" ? (
                  <span aria-hidden className={`absolute top-1 end-1 size-1.5 rounded-full ${d.status === "custom_hours" ? "bg-blue" : "bg-orange"}`} />
                ) : null}
              </button>
            );
          })}
        </div>
      )}
      <ul className="grid grid-cols-2 gap-x-3 gap-y-1 border-t border-line/60 pt-2 text-[11.5px] text-ink-soft" aria-label="مفتاح الألوان">
        <li className="flex items-center gap-1.5"><span aria-hidden className="flex gap-0.5"><span className="size-1 rounded-full bg-gold" /><span className="size-1 rounded-full bg-gold" /></span>توجد حجوزات</li>
        <li className="flex items-center gap-1.5"><span aria-hidden className="font-bold text-red-ink line-through">12</span>{STATUS.closed.word}</li>
        <li className="flex items-center gap-1.5"><span aria-hidden className="size-1.5 rounded-full bg-orange" />{STATUS.partly_closed.word}</li>
        <li className="flex items-center gap-1.5"><span aria-hidden className="size-1.5 rounded-full bg-blue" />{STATUS.custom_hours.word}</li>
        <li className="flex items-center gap-1.5"><span aria-hidden className="text-ink-soft/70">12</span>{STATUS.weekly_off.word}</li>
      </ul>
    </section>
  );
}

// ---------- one day ----------

const DAY_MS = 86_400_000;
/** Still on the office's day: held, or confirmed and not marked. */
const isLive = (a: Appointment) => a.state === "confirmed" || a.state === "pending_payment";
const isGone = (a: Appointment) => a.state === "expired" || a.state.startsWith("cancelled");
const needsMark = (a: Appointment, now: Date) => a.state === "confirmed" && new Date(a.end).getTime() < now.getTime();
const paymentToReview = (a: Appointment) => a.state === "confirmed" && (a.payment === "proof_received" || a.payment === "duplicate_suspected");
const minutesOf = (a: Appointment) => Math.round((new Date(a.end).getTime() - new Date(a.start).getTime()) / 60_000);
const lengthWords = (m: number) => (m === 15 ? "ربع ساعة" : m === 30 ? "نصف ساعة" : m === 60 ? "ساعة" : m === 120 ? "ساعتان" : `${m} دقيقة`);

/** The picked day: its name and hours, what needs the office, «تعديل هذا اليوم», and its bookings on a timeline. */
function DayPanel({ day, today, mayChange, onPick }: { day: string; today: string; mayChange: boolean; onPick: (d: string) => void }) {
  const { data: me } = useMe();
  const month = useQuery(monthQuery(monthOf(day)));
  const info = month.data?.find((d) => d.day === day);
  const list = useQuery(dayQuery(day));
  const [editing, setEditing] = useState(false);
  const [done, setDone] = useState<Done | null>(null);
  const now = new Date();
  const items = (list.data ?? []).slice().sort((a, b) => a.start.localeCompare(b.start));
  const off = info && (info.status === "closed" || info.status === "weekly_off");
  const counts = {
    all: items.filter((a) => !isGone(a)).length,
    done: items.filter((a) => a.state === "completed").length,
    held: items.filter((a) => a.state === "pending_payment").length,
    gone: items.filter(isGone).length,
  };
  const toMark = items.filter((a) => needsMark(a, now)).length;
  const toReview = items.filter(paymentToReview).length;
  const arrow = "grid size-10 shrink-0 place-items-center rounded-full border border-line/60 bg-card/60 text-ink transition hover:border-gold-line hover:bg-gold-tint hover:text-gold-ink motion-reduce:transition-none";

  return (
    <div className="grid min-w-0 content-start gap-3">
      <section className="liquid relative grid gap-3 overflow-hidden rounded-[22px] p-4 sm:p-5">
        <span aria-hidden className="pointer-events-none absolute inset-x-6 top-0 h-px bg-gradient-to-l from-transparent via-gold/70 to-transparent" />
        <div className="flex flex-wrap items-center gap-3">
          <button type="button" className={arrow} aria-label="اليوم السابق" onClick={() => onPick(shiftDay(day, -1))}><Icon name="chevron-start" className="size-5" /></button>
          <div className="grid min-w-0 flex-1 basis-48 gap-0.5">
            <span className="text-[12.5px] font-bold text-gold-ink">{day === today ? "اليوم" : day === shiftDay(today, 1) ? "غدًا" : day === shiftDay(today, -1) ? "أمس" : day < today ? "يوم سابق" : "يوم قادم"}</span>
            <h2 className="flex flex-wrap items-center gap-2 text-[22px] leading-tight font-bold">
              {longDay(day)}
              {info && info.status !== "open" ? <Chip tone={STATUS[info.status].tone}>{STATUS[info.status].word}</Chip> : null}
            </h2>
            {info ? (
              <p className="flex items-center gap-1.5 text-[13px] text-ink-soft">
                <Icon name="clock" className="size-4 shrink-0 text-gold-ink" />
                <span className="tabular">{info.hours.length === 0 ? "لا يوجد حجز في هذا اليوم" : `الحجز ${info.hours.map((h) => `من ${hhmm(h.start)} إلى ${hhmm(h.end)}`).join(" و")}`}</span>
              </p>
            ) : null}
          </div>
          <button type="button" className={arrow} aria-label="اليوم التالي" onClick={() => onPick(shiftDay(day, 1))}><Icon name="chevron-end" className="size-5" /></button>
        </div>

        <div className="flex flex-wrap items-center gap-2 border-t border-line/60 pt-3">
          <ul className="flex min-w-0 flex-1 flex-wrap gap-1.5 text-[12.5px]" aria-label="ملخص اليوم">
            <Count word={meetingsWords(counts.all)} tone="ink" />
            {counts.done ? <Count word={`تم: ${counts.done}`} tone="green" /> : null}
            {counts.held ? <Count word={`في انتظار الإيصال: ${counts.held}`} tone="orange" /> : null}
            {counts.gone ? <Count word={`أُلغي: ${counts.gone}`} tone="grey" /> : null}
          </ul>
          {/* a past day can't be changed (the API answers past_day) */}
          {mayChange && info && day >= today ? (
            <Button variant="secondary" className="!min-h-10 !rounded-full !px-4 text-[13.5px]" onClick={() => setEditing(true)}>
              <Icon name="settings" className="size-4 text-gold-ink" />تعديل هذا اليوم
            </Button>
          ) : null}
        </div>
      </section>

      {done && done.cancelled > 0 ? <Told n={done.cancelled} /> : null}

      {toMark || toReview ? (
        <section aria-label="يحتاج إلى إجراء" className="grid gap-1.5 rounded-[18px] border border-orange/35 bg-orange-bg px-4 py-3 text-[13.5px] text-orange-ink">
          <p className="flex items-center gap-2 font-bold"><Icon name="alert-circle" className="size-4 shrink-0" />يحتاج إلى إجراء</p>
          <ul className="grid gap-1 ps-6">
            {toMark ? <li>{meetingsWords(toMark)} في انتظار تسجيل النتيجة: سجّل لكل موعد «تم» أو «لم يحضر» أدناه.</li> : null}
            {toReview ? (
              <li>
                {toReview === 1 ? "تحويل واحد" : toReview === 2 ? "تحويلان" : toReview <= 10 ? `${toReview} تحويلات` : `${toReview} تحويلًا`} في انتظار المراجعة{" "}
                {can(me, "schedule.manage") ? <Link to="/receipts" className="font-bold underline underline-offset-2">افتح الإيصالات</Link> : null}
              </li>
            ) : null}
          </ul>
        </section>
      ) : null}

      <section aria-label="حجوزات اليوم" className="liquid relative grid min-w-0 gap-2 overflow-hidden rounded-[22px] p-3 sm:p-4">
        {list.isPending ? <Loading /> : list.isError ? <ErrorState message={errorMessage(list.error)} onRetry={() => void list.refetch()} /> : items.length === 0 ? (
          <Empty title={off ? "لا يعمل المكتب في هذا اليوم" : day < today ? "لم تكن هناك حجوزات في هذا اليوم" : "لا توجد حجوزات بعد"} icon="calendar">
            {off ? "لن يعرض حازم مواعيد في هذا اليوم." : day >= today ? "يحجز حازم للعملاء عبر واتساب، وسيظهر أي حجز جديد هنا تلقائيًا." : null}
          </Empty>
        ) : <Timeline items={items} now={now} showNow={day === today} />}
      </section>

      {editing && info ? <DayDialog day={info} isToday={day === today} onClose={() => setEditing(false)} onDone={setDone} /> : null}
    </div>
  );
}

const COUNT_TONE: Record<string, string> = {
  ink: "border-line/70 bg-card/70 text-ink font-bold", green: "border-transparent bg-green-bg text-green-ink", orange: "border-transparent bg-orange-bg text-orange-ink", grey: "border-transparent bg-grey-bg text-grey-ink",
};
const Count = ({ word, tone }: { word: string; tone: string }) => (
  <li className={`rounded-full border px-3 py-1 font-semibold ${COUNT_TONE[tone]}`}>{word}</li>
);

/** The bookings in time order on one line, with «الآن» where the clock is (today only). */
function Timeline({ items, now, showNow }: { items: Appointment[]; now: Date; showNow: boolean }) {
  const nowAt = showNow ? items.findIndex((a) => new Date(a.start).getTime() > now.getTime()) : -2;
  const nowLine = (
    <li key="now" aria-label={`الآن ${cairoTime(now)}`} className="relative flex items-center gap-2 py-1 ps-[4.25rem] sm:ps-[6.25rem]">
      <span aria-hidden className="absolute start-[5.4rem] hidden size-3 -translate-x-1/2 rounded-full bg-red ring-4 ring-red/20 sm:block rtl:translate-x-1/2" />
      <span className="rounded-full bg-red px-2 py-0.5 text-[11.5px] font-bold text-white tabular">الآن {cairoTime(now)}</span>
      <span aria-hidden className="h-0.5 flex-1 rounded-full bg-red/50" />
    </li>
  );
  return (
    <ul className="relative grid gap-2">
      <span aria-hidden className="absolute inset-y-4 start-[5.4rem] hidden w-px bg-line sm:block" />
      {items.flatMap((a, i) => [...(i === nowAt ? [nowLine] : []), <BookingCard key={a.id} a={a} now={now} />])}
      {nowAt === -1 ? nowLine : null}
    </ul>
  );
}

const KINDS = [
  ["closed_day", "إغلاق اليوم بالكامل", "لا حجز في هذا اليوم إطلاقًا (إجازة أو ظرف طارئ).", "lock"],
  ["closed_hours", "إغلاق جزء من اليوم", "لا حجز في ساعات محددة، ويبقى باقي اليوم كالمعتاد.", "clock"],
  ["custom_hours", "ساعات مختلفة لهذا اليوم", "يعمل المكتب في هذا اليوم فقط بساعات غير معتادة.", "calendar"],
] as const;

/** «تعديل هذا اليوم»: the day's changes, closing it (asks first if it has bookings), other hours, and the preview. */
function DayDialog({ day, isToday, onClose, onDone }: { day: Day; isToday: boolean; onClose: () => void; onDone: (r: Done) => void }) {
  const qc = useQueryClient();
  const [kind, setKind] = useState<"closed_day" | "closed_hours" | "custom_hours">("closed_day");
  const [from, setFrom] = useState(600);
  const [to, setTo] = useState(840);
  const [reason, setReason] = useState("");
  // A change that would cancel bookings: the list, and the same change again with confirm.
  const [ask, setAsk] = useState<{ list: Appointment[]; again: () => void } | null>(null);
  const [done, setDone] = useState<Done | null>(null);
  const refresh = () => void qc.invalidateQueries({ queryKey: ["booking"] });
  const finished = (r: Done) => { setAsk(null); setDone(r); onDone(r); refresh(); };
  const asked = (e: Error, again: () => void) => {
    if (e instanceof ApiError && e.code === "has_bookings") setAsk({ list: e.data?.appointments as Appointment[], again });
  };
  const add = useMutation({
    mutationFn: (confirm: boolean) => booking.addException({ day: day.day, kind, ...(kind === "closed_day" ? {} : { start: from, end: to }), reason, confirm }),
    onSuccess: (r) => { finished(r); setReason(""); toast("تم تعديل اليوم ✅"); onClose(); },
    onError: (e) => asked(e, () => add.mutate(true)),
  });
  const remove = useMutation({
    mutationFn: ({ id, confirm }: { id: string; confirm: boolean }) => booking.removeException(id, confirm),
    onSuccess: finished,
    onError: (e, v) => asked(e, () => remove.mutate({ id: v.id, confirm: true })),
  });

  return (
    <Dialog title={<>تعديل {isToday ? "اليوم · " : ""}{longDay(day.day)}</>} onClose={onClose}>
      {day.exceptions.length > 0 ? (
        <div className="grid gap-1.5">
          <p className="text-[12.5px] font-bold text-ink-soft">التعديلات الحالية</p>
          <ul className="grid gap-1.5">
            {day.exceptions.map((e) => (
              <li key={e.id} className="flex flex-wrap items-center gap-2 rounded-[14px] border border-line/60 bg-card/60 py-1.5 ps-3 pe-1.5 text-sm">
                <span aria-hidden className={`size-2 shrink-0 rounded-full ${e.kind === "custom_hours" ? "bg-blue" : "bg-red"}`} />
                <span className="min-w-0 flex-1">
                  <b>{e.kind === "closed_day" ? "اليوم مغلق" : `${e.kind === "closed_hours" ? "مغلق" : "ساعات"} ${hhmm(e.start!)}–${hhmm(e.end!)}`}</b>
                  <span className="text-ink-soft">{e.reason ? ` · ${e.reason}` : ""}{e.by ? ` · ${e.by}` : ""}</span>
                </span>
                <Button variant="ghost" className="!min-h-9 !px-3" busy={remove.isPending && remove.variables?.id === e.id} onClick={() => remove.mutate({ id: e.id, confirm: false })}><Icon name="x" className="size-4" />إزالة</Button>
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      <fieldset className="grid gap-2">
        <legend className="mb-1 text-[12.5px] font-bold text-ink-soft">ما التعديل المطلوب؟</legend>
        {KINDS.map(([k, t, hint, icon]) => (
          <label key={k} className="flex cursor-pointer items-start gap-3 rounded-[16px] border border-line/60 bg-card/45 px-3.5 py-3 text-sm transition focus-within:border-gold has-checked:border-gold has-checked:bg-gold-tint motion-reduce:transition-none">
            <input type="radio" name="kind" aria-label={t} className="sr-only" checked={kind === k} onChange={() => setKind(k)} />
            <span aria-hidden className="grid size-9 shrink-0 place-items-center rounded-[12px] bg-card/80 text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.5)]"><Icon name={icon} className="size-[18px]" /></span>
            <span className="grid min-w-0 flex-1 gap-0.5">
              <span className="font-bold">{t}</span>
              <span className="text-[12.5px] leading-relaxed text-ink-soft">{hint}</span>
            </span>
            <span aria-hidden className={`mt-1 grid size-5 shrink-0 place-items-center rounded-full border-2 ${kind === k ? "border-gold bg-gold text-white" : "border-line-strong"}`}>
              {kind === k ? <Icon name="check" className="size-3" /> : null}
            </span>
          </label>
        ))}
        {kind !== "closed_day" ? (
          <div className="flex flex-wrap items-center gap-2 rounded-[14px] bg-card/50 px-3 py-2 text-sm font-semibold">
            من <TimeSelect value={from} onChange={setFrom} label="من" /> إلى <TimeSelect value={to} onChange={setTo} label="إلى" min={from + 15} />
          </div>
        ) : null}
        <Field label="السبب (للمكتب فقط)" value={reason} maxLength={200} placeholder="مثلًا: لدى المستشار جلسة" onChange={(e) => setReason(e.target.value)} />
        <div className="flex flex-wrap gap-2 pt-1">
          <Button variant="primary" busy={add.isPending && !ask} onClick={() => add.mutate(false)}>حفظ</Button>
          <Button onClick={onClose}>إلغاء</Button>
        </div>
      </fieldset>
      {ask ? (
        <Confirm list={ask.list} busy={add.isPending || remove.isPending} onYes={ask.again} onNo={() => { setAsk(null); add.reset(); remove.reset(); }} />
      ) : add.isError || remove.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(add.error ?? remove.error)}</p> : null}
      {done && done.cancelled > 0 ? <Told n={done.cancelled} /> : null}
      <PreviewOf day={day.day} />
    </Dialog>
  );
}

/** What Hazem would offer that day now (the same rules as the conversation). */
function PreviewOf({ day }: { day: string }) {
  const [service, setService] = useState("pv");
  const p = useQuery({ queryKey: ["booking", "preview", day, service], queryFn: () => booking.preview(day, service) });
  return (
    <div className="grid gap-2 rounded-[16px] border border-line/60 bg-card/40 p-3">
      <div className="flex flex-wrap items-center gap-2">
        <h3 className="flex flex-1 items-center gap-2 text-[14px] font-bold"><Icon name="whatsapp" className="size-4 text-gold-ink" />المواعيد المتاحة التي سيعرضها حازم</h3>
        <select aria-label="الخدمة" className={`${SELECT} !min-h-9 text-sm`} value={service} onChange={(e) => setService(e.target.value)}>
          <option value="pv">{BOOKING_SERVICES.consultation_phone_video}</option>
          <option value="of">{BOOKING_SERVICES.consultation_office}</option>
          <option value="ur">{BOOKING_SERVICES.consultation_urgent}</option>
        </select>
      </div>
      {p.isPending ? <Loading /> : p.isError ? <ErrorState message={errorMessage(p.error)} /> : (
        <>
          {p.data.calendar !== "ok" ? <p className="text-xs text-orange-ink">تقويم المستشار {p.data.calendar === "not_configured" ? "غير مربوط" : "لا يستجيب"}: هذه ساعات اللوحة فقط، ولن يعرض حازم مواعيد حتى يُربط التقويم.</p> : null}
          {p.data.slots.length === 0 ? <p className="text-sm text-ink-soft">لا توجد مواعيد متاحة في هذا اليوم.</p> : (
            <ul className="flex flex-wrap gap-1.5">{p.data.slots.map((s) => <li key={s} className="tabular rounded-full border border-gold-line bg-gold-tint px-2.5 py-1 text-xs font-semibold text-gold-ink">{cairoTime(new Date(s))}</li>)}</ul>
          )}
        </>
      )}
    </div>
  );
}

// ---------- one booking ----------

/** «بعد 40 دقيقة»، «بعد 3 ساعات»، «بعد يومين»، «الآن»، «منذ ساعتين»: how far the appointment is. */
function fromNow(a: Appointment, now: Date): string {
  const start = new Date(a.start).getTime();
  const end = new Date(a.end).getTime();
  const t = now.getTime();
  if (t >= start && t < end) return "الآن";
  const ahead = start > t;
  const min = Math.round(Math.abs((ahead ? start : end) - t) / 60_000);
  // after «بعد» / «منذ»: «دقيقتين»، «3 ساعات»، «11 يومًا»
  const count = (n: number, one: string, two: string, few: string, many: string) => (n === 1 ? one : n === 2 ? two : n <= 10 ? `${n} ${few}` : `${n} ${many}`);
  const words = min < 60 ? count(min, "دقيقة", "دقيقتين", "دقائق", "دقيقة") : min < 24 * 60 ? count(Math.round(min / 60), "ساعة", "ساعتين", "ساعات", "ساعة")
    : count(Math.round(min / 1440), "يوم", "يومين", "أيام", "يومًا");
  return ahead ? `بعد ${words}` : `منذ ${words}`;
}

const LATE = "انتهى وقته: سجّل «تم» أو «لم يحضر»";

/** What the office should look at on a booking (PR #14's notes): nothing when all is well. */
function notesOf(a: Appointment, now: Date): { text: string; tone: string }[] {
  const out: { text: string; tone: string }[] = [];
  const t = now.getTime();
  if (a.state === "pending_payment" && a.hold_until && new Date(a.hold_until).getTime() - t < 30 * 60_000) {
    out.push({ text: "قاربت مهلة الحجز المبدئي على الانتهاء", tone: "orange" });
  }
  if (paymentToReview(a)) out.push({ text: "التحويل في انتظار المراجعة (الإيصالات والمدفوعات)", tone: "orange" });
  if (a.state === "confirmed" && a.calendar !== "synced" && new Date(a.end).getTime() > t) {
    out.push({ text: "لم يُضف إلى تقويم المستشار بعد", tone: "grey" });
  }
  if (needsMark(a, now)) out.push({ text: LATE, tone: "orange" });
  return out;
}

/** «11:00 ص» → the time big and «ص» small. */
function Clock({ at, className = "" }: { at: string; className?: string }) {
  const [hm, ap] = cairoTime(new Date(at)).split(" ");
  return <span className={`tabular whitespace-nowrap font-bold ${className}`}>{hm}{ap ? <span className="ms-0.5 text-[0.6em] font-semibold text-ink-soft">{ap}</span> : null}</span>;
}

const ACT = "!min-h-10 !gap-1.5 !rounded-full !px-4 text-[13.5px]";

/** One booking: the time on the line, then the client, the kind, the state, what to look at, and what the office can do. */
function BookingCard({ a, now }: { a: Appointment; now: Date }) {
  const qc = useQueryClient();
  const { data: me } = useMe();
  const refresh = () => void qc.invalidateQueries({ queryKey: ["booking"] });
  const mark = useMutation({
    mutationFn: (state: string) => booking.mark(a.id, state),
    onSuccess: (_, state) => { refresh(); toast(state === "completed" ? "سُجّلت نتيجة الموعد: تم ✅" : "سُجّلت نتيجة الموعد: لم يحضر العميل"); },
  });
  const cancel = useMutation({ mutationFn: () => booking.cancel(a.id), onSuccess: () => { refresh(); toast("تم إلغاء الحجز، وسيُبلغ حازم العميل."); } });
  const allNotes = notesOf(a, now);
  const late = allNotes.find((n) => n.text === LATE);
  const notes = allNotes.filter((n) => n !== late);
  // marking can't be taken back («خلص» starts the client's rating clock): only once the appointment has started
  const started = new Date(a.start).getTime() <= now.getTime();
  const acting = mark.isPending || cancel.isPending;
  const tone = STATE_TONE[a.state] ?? "grey";
  const happening = isLive(a) && fromNow(a, now) === "الآن";
  const markAs = async (state: "completed" | "no_show") => {
    const ok = await confirmAction(state === "completed"
      ? { title: "هل تم الموعد؟", body: "لا يمكن التراجع عن ذلك، وسيطلب حازم من العميل تقييم الخدمة.", yes: "نعم، تم" }
      : { title: "تسجيل أن العميل لم يحضر؟", body: "لا يمكن التراجع عن ذلك.", yes: "نعم، لم يحضر" });
    if (ok) mark.mutate(state);
  };
  const canMark = a.state === "confirmed" && started;
  const opinion = a.state === "completed" && can(me, "services.price");
  const hasActions = isLive(a) || a.correctable || opinion;

  return (
    <li className="relative grid grid-cols-[3.75rem_minmax(0,1fr)] items-start gap-2 sm:grid-cols-[4.5rem_minmax(0,1fr)] sm:gap-3">
      <span className={`grid justify-items-end pt-3 leading-tight ${isGone(a) ? "opacity-50" : ""}`}>
        <Clock at={a.start} className="text-[16px] sm:text-[18px]" />
        <span className="text-[11.5px] text-ink-soft">{lengthWords(minutesOf(a))}</span>
      </span>
      <span aria-hidden className={`absolute start-[5.4rem] top-4 hidden size-3 rounded-full border-2 sm:block ${DOT[tone]} -translate-x-1/2 rtl:translate-x-1/2`} />
      <article className={`grid min-w-0 gap-2 rounded-[16px] border p-3 transition motion-reduce:transition-none sm:ms-4 sm:p-3.5 ${happening
        ? "border-gold bg-gold-tint/70 shadow-[0_10px_24px_-14px_rgb(140_98_58/0.8)]"
        : isGone(a) ? "border-dashed border-line bg-card/30 opacity-70" : "border-line/70 bg-card/80 shadow-[0_4px_14px_-10px_rgb(60_44_20/0.35)] hover:border-gold-line"}`}>
        <div className="flex flex-wrap items-start gap-x-3 gap-y-1.5">
          <span className="grid min-w-0 flex-1 basis-48 gap-0.5">
            <span className={`text-[16px] leading-snug font-bold ${isGone(a) ? "line-through decoration-ink-soft/60" : ""}`}>{a.name ?? "بدون اسم"}</span>
            <span className="flex flex-wrap items-center gap-x-1.5 text-[13px] text-ink-soft">
              <Icon name={SERVICE_ICON(a.service)} className="size-4 shrink-0 text-gold-ink" />
              <span className="font-semibold text-ink">{label(BOOKING_SERVICES, a.service)}{a.call_app ? ` (${label(CALL_APPS, a.call_app)})` : ""}</span>
              <span aria-hidden>·</span>{matterLabel(a.matter_type ?? undefined)}
            </span>
          </span>
          <span className="flex flex-wrap items-center gap-1.5">
            {happening ? <span className="rounded-full bg-gold px-2.5 py-0.5 text-[12px] font-bold text-white">جارٍ الآن</span>
              : isLive(a) ? <span className="text-[12px] font-semibold text-ink-soft">{fromNow(a, now)}</span> : null}
            {a.state === "confirmed" && a.attending ? <Chip tone="green">أكّد حضوره ✅</Chip> : null}
            <Chip tone={tone}>
              {label(APPOINTMENT_STATES, a.state)}{a.state === "completed" && a.completed_by === "auto" ? " (تلقائي)" : ""}{a.state === "pending_payment" && a.hold_until ? ` حتى ${cairoTime(new Date(a.hold_until))}` : ""}
            </Chip>
          </span>
        </div>

        <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-[12.5px] text-ink-soft">
          <span className="inline-flex items-center gap-1"><Icon name="phone" className="size-3.5 shrink-0" /><Phone wa={a.wa_id} /></span>
          {a.payment && a.state !== "pending_payment" ? <span className="inline-flex items-center gap-1"><Icon name="receipt" className="size-3.5 shrink-0" />الدفع: {label(PAYMENT_STATES, a.payment)}</span> : null}
          {a.reschedules ? <span className="inline-flex items-center gap-1"><Icon name="refresh" className="size-3.5 shrink-0" />غُيّر الموعد مرة</span> : null}
        </div>
        {a.state === "confirmed" && a.reminders.length > 0 ? <Reminders items={a.reminders} /> : null}

        {notes.length > 0 ? (
          <span className="flex flex-wrap gap-1.5">
            {notes.map((n) => (
              <span key={n.text} className={`inline-flex items-center gap-1 rounded-full px-2.5 py-1 text-[12px] font-semibold ${n.tone === "orange" ? "bg-orange-bg text-orange-ink" : "bg-grey-bg text-grey-ink"}`}>
                <Icon name="alert-circle" className="size-3.5" />{n.text}
              </span>
            ))}
          </span>
        ) : null}

        {hasActions ? (
          <div className="flex flex-wrap items-center gap-2 border-t border-line/60 pt-2.5">
            {late ? <span className="flex w-full items-center gap-1.5 text-[12.5px] font-semibold text-orange-ink"><Icon name="alert-circle" className="size-3.5 shrink-0" />{late.text}</span> : null}
            {canMark ? (
              <>
                <Button variant="ghost" className={`${ACT} !border-[#078a5d] !bg-[#078a5d] !text-white hover:!opacity-90`} busy={mark.isPending && mark.variables === "completed"} disabled={acting} onClick={() => void markAs("completed")}>
                  <Icon name="check-circle" className="size-4" />تم
                </Button>
                <Button variant="ghost" className={`${ACT} !border-orange/40 !bg-orange-bg !text-orange-ink hover:!bg-orange/15`} busy={mark.isPending && mark.variables === "no_show"} disabled={acting} onClick={() => void markAs("no_show")}>
                  <Icon name="alert-circle" className="size-4" />لم يحضر
                </Button>
              </>
            ) : null}
            {a.correctable ? (
              // v1.50 (TASK 19): «خلص (تلقائي)» was wrong: «ماحضرش» until the rating went out
              <Button variant="ghost" className={`${ACT} !border-orange/40 !bg-orange-bg !text-orange-ink hover:!bg-orange/15`} busy={mark.isPending} disabled={acting}
                onClick={async () => {
                  if (await confirmAction({ title: "تسجيل أن العميل لم يحضر؟", body: "سُجّل الموعد «تم» تلقائيًا. سيتغيّر إلى «لم يحضر»، ولن يُرسل طلب التقييم.", yes: "نعم، لم يحضر" })) mark.mutate("no_show");
                }}>
                <Icon name="alert-circle" className="size-4" />لم يحضر
              </Button>
            ) : null}
            {opinion ? <Opinion id={a.id} /> : null}
            {isLive(a) ? (
              <Button variant="ghost" className="ms-auto !min-h-10 !gap-1.5 !rounded-full !px-3 text-[13px] !text-red-ink hover:!bg-red-bg" busy={cancel.isPending} disabled={acting}
                onClick={async () => {
                  if (await confirmAction({ title: "إلغاء هذا الحجز؟", body: "سيرسل حازم إلى العميل ليختار موعدًا آخر، أو استردادًا كاملًا إن كان قد دفع.", yes: "نعم، ألغِ الحجز", danger: true })) cancel.mutate();
                }}>
                <Icon name="x" className="size-4" />إلغاء الحجز
              </Button>
            ) : null}
          </div>
        ) : null}
        {mark.isError || cancel.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(mark.error ?? cancel.error)}</p> : null}
      </article>
    </li>
  );
}

/** TASK 12 (B-18): what happened to each reminder of the appointment's current time. */
function Reminders({ items }: { items: Reminder[] }) {
  const word = (r: Reminder) =>
    r.state === "scheduled" && r.due_at ? `سيُرسل ${cairoWhen(r.due_at)}`
    : r.state === "sending" ? "جارٍ الإرسال الآن"
    : r.state === "sent" ? `تم الإرسال ✓${r.channel === "template" ? " (قالب واتساب)" : ""}`
    : r.state === "failed" ? "لم يصل ✗ (أُنشئت مهمة للتواصل معه)"
    : r.state === "skipped" ? `لم يُرسل: ${label(REMINDER_SKIPS, r.skip_reason ?? "")}`
    : null;
  const shown = items.filter((r) => word(r) !== null);
  if (shown.length === 0) return null;
  return (
    <span className="flex flex-wrap items-center gap-1.5 text-[12px] text-ink-soft">
      <Icon name="bell" className="size-3.5 shrink-0" />
      {shown.map((r) => (
        <span key={r.kind} className={`rounded-full px-2 py-0.5 ${r.state === "failed" ? "bg-red-bg text-red-ink" : r.state === "sent" ? "bg-green-bg text-green-ink" : "bg-grey-bg"}`}>
          {label(REMINDER_KINDS, r.kind)}: {word(r)}
        </span>
      ))}
    </span>
  );
}

/** B-17 (TASK 10): «مطلوب رأي مكتوب» with its delivery day: Hazem asks the client for the 2,000. */
function Opinion({ id }: { id: string }) {
  const qc = useQueryClient();
  const [open, setOpen] = useState(false);
  const [due, setDue] = useState(cairoDate(new Date(Date.now() + 7 * 86_400_000)));
  const ask = useMutation({
    mutationFn: () => booking.opinion(id, due, "file"),
    onSuccess: () => { setOpen(false); void qc.invalidateQueries({ queryKey: ["services"] }); toast("تم إرسال طلب الرأي المكتوب ✅ (في «طلبات الخدمات»)"); },
  });
  // once sent the order exists: asking again would only get not_allowed
  if (ask.isSuccess) return <span className="text-sm text-green-ink" role="status">تم طلب رأي مكتوب ✓</span>;
  if (!open) return <Button variant="secondary" className="!min-h-10 !rounded-full !px-4 text-[13.5px]" onClick={() => setOpen(true)}><Icon name="document" className="size-4 text-gold-ink" />مطلوب رأي مكتوب</Button>;
  return (
    <span className="flex w-full flex-wrap items-end gap-2 rounded-[14px] border border-gold-line bg-gold-tint/60 p-2.5">
      <Field label="يوم التسليم" type="date" min={cairoDate(new Date())} value={due} onChange={(e) => setDue(e.target.value)} />
      <Button variant="primary" busy={ask.isPending} onClick={() => ask.mutate()}>أرسل طلب دفع 2,000 جنيه</Button>
      <Button onClick={() => setOpen(false)}>إلغاء</Button>
      {ask.isError ? <span className="text-sm text-bad" role="alert">{errorMessage(ask.error)}</span> : null}
    </span>
  );
}

// ---------- كل المواعيد: the appointments ahead (or past) ----------

type Kind = "all" | "phone" | "office" | "urgent" | "urf";
const KIND_TEST: Record<Kind, (a: Appointment) => boolean> = {
  all: () => true,
  phone: (a) => a.service === "consultation_phone_video",
  office: (a) => a.service === "consultation_office",
  urgent: (a) => a.service === "consultation_urgent",
  urf: (a) => a.service.startsWith("urf_"),
};
const WHENS: { value: "ahead" | "past"; label: string; icon: IconName }[] = [
  { value: "ahead", label: "القادمة (30 يومًا)", icon: "forward" },
  { value: "past", label: "السابقة (آخر أسبوعين)", icon: "back" },
];
const KIND_ICON: Record<Kind, IconName> = { all: "calendar", phone: "video", office: "office", urgent: "alert", urf: "document" };

function Tile({ label: name, value, tone, icon }: { label: string; value: number | undefined; tone: string; icon: IconName }) {
  return (
    <div className="liquid flex items-center gap-3 rounded-[18px] px-3.5 py-3">
      <span aria-hidden className={`grid size-9 shrink-0 place-items-center rounded-[12px] bg-card/80 ${tone}`}><Icon name={icon} className="size-[18px]" /></span>
      <span className="grid min-w-0">
        <span className={`tabular text-[22px] leading-tight font-bold ${value ? tone : "text-ink"}`}>{value === undefined ? "…" : value}</span>
        <span className="text-[12px] leading-snug text-ink-soft">{name}</span>
      </span>
    </div>
  );
}

function Bookings() {
  const [when, setWhen] = useUrlState<"ahead" | "past">("when", "ahead", ["ahead", "past"]);
  const [kind, setKind] = useUrlState<Kind>("kind", "all", ["all", "phone", "office", "urgent", "urf"]);
  const [q, setQ] = useState("");
  // Ahead: today and the next 30 days. Past (TASK 10): the last two weeks, so a consultation that took place can get
  // «مطلوب رأي مكتوب» (B-17), most recent first.
  const now = new Date();
  const from = when === "ahead" ? cairoDate(now) : cairoDate(new Date(now.getTime() - 14 * DAY_MS));
  const to = when === "ahead" ? cairoDate(new Date(now.getTime() + 30 * DAY_MS)) : cairoDate(new Date(now.getTime() - DAY_MS));
  const list = useQuery({ queryKey: ["booking", "appointments", from, to], queryFn: () => booking.appointments(from, to), refetchInterval: 60_000 });

  const all = (list.data ?? []).slice().sort((a, b) => (when === "ahead" ? 1 : -1) * a.start.localeCompare(b.start));
  const needle = q.trim().replace(/^\+/, "");
  const items = all.filter((a) => KIND_TEST[kind](a) && (!needle || (a.name ?? "").includes(needle) || a.wa_id.includes(needle)));
  const days = [...new Set(items.map((a) => cairoDate(new Date(a.start))))].map((day) => ({ day, items: items.filter((a) => cairoDate(new Date(a.start)) === day) }));
  const todayKey = cairoDate(now);
  const weekEnd = cairoDate(new Date(now.getTime() + 6 * DAY_MS));
  const next = when === "ahead" ? all.find((a) => isLive(a) && new Date(a.end) > now) : undefined;
  // TASK 12 (B-18): tomorrow's confirmed appointments whose client has not pressed «هحضر ✅» yet (nothing is cancelled for it).
  const tomorrowKey = cairoDate(new Date(now.getTime() + DAY_MS));
  const tomorrows = when === "ahead" ? all.filter((a) => a.state === "confirmed" && cairoDate(new Date(a.start)) === tomorrowKey) : [];
  const unconfirmed = tomorrows.filter((a) => !a.attending).length;
  const count = (f: (a: Appointment) => boolean) => (list.data ? all.filter(f).length : undefined);
  const tiles: { label: string; value: number | undefined; tone: string; icon: IconName }[] = when === "ahead" ? [
    { label: "اليوم", value: count((a) => isLive(a) && cairoDate(new Date(a.start)) === todayKey), tone: "text-blue-ink", icon: "clock" },
    { label: "هذا الأسبوع", value: count((a) => isLive(a) && cairoDate(new Date(a.start)) <= weekEnd), tone: "text-blue-ink", icon: "calendar" },
    { label: "في انتظار الإيصال", value: count((a) => a.state === "pending_payment"), tone: "text-orange-ink", icon: "receipt" },
    { label: "تحويلات في انتظار المراجعة", value: count(paymentToReview), tone: "text-red-ink", icon: "alert-circle" },
  ] : [
    { label: "تمت", value: count((a) => a.state === "completed"), tone: "text-green-ink", icon: "check-circle" },
    { label: "لم يحضروا", value: count((a) => a.state === "no_show"), tone: "text-orange-ink", icon: "alert-circle" },
    { label: "أُلغيت", value: count((a) => a.state.startsWith("cancelled")), tone: "text-ink", icon: "x" },
    { label: "انتهت (لم يصل الإيصال)", value: count((a) => a.state === "expired"), tone: "text-ink", icon: "clock" },
  ];
  const kinds: { value: Kind; label: string; icon: IconName }[] = ([["all", `الكل ${all.length}`], ["phone", "هاتف أو فيديو"], ["office", "في المكتب"], ["urgent", "عاجلة"], ["urf", "عرفي"]] as const)
    .map(([v, t]) => ({ value: v, label: t, icon: KIND_ICON[v] }));

  return (
    <div className="grid gap-3">
      <section aria-label="تصفية الحجوزات" className="liquid grid gap-2.5 rounded-[22px] p-3">
        <div className="grid gap-2.5 @2xl:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
          <Segmented label="أي الحجوزات" value={when} onChange={setWhen} options={WHENS} small />
          <label className="relative w-full">
            <span className="sr-only">ابحث بالاسم أو الرقم</span>
            <Icon name="search" className="pointer-events-none absolute start-3 top-1/2 size-4 -translate-y-1/2 text-ink-soft" />
            <input type="search" value={q} onChange={(e) => setQ(e.target.value)} placeholder="ابحث بالاسم أو الرقم"
              className="min-h-10 w-full rounded-full border border-line/70 bg-card/60 ps-9 pe-3 text-sm text-ink shadow-[inset_0_1px_2px_rgb(0_0_0/0.06)] focus:border-gold" />
          </label>
        </div>
        <Segmented label="نوع الموعد" value={kind} onChange={setKind} options={kinds} small />
      </section>

      <section aria-label="ملخص الحجوزات" className="grid grid-cols-2 gap-2 @3xl:grid-cols-4">
        {tiles.map((t) => <Tile key={t.label} {...t} />)}
      </section>

      {next ? (
        <div className="relative flex flex-wrap items-center gap-x-4 gap-y-1 overflow-hidden rounded-[18px] border border-gold-line bg-gold-tint/70 px-4 py-3 ps-6">
          <span aria-hidden className="absolute inset-y-3 start-0 w-1.5 rounded-e-full bg-gradient-to-b from-[#dcb98c] to-[#a87a48]" />
          <span className="flex items-center gap-1.5 text-[13px] font-bold text-gold-ink"><Icon name="clock" className="size-4" />{fromNow(next, now) === "الآن" ? "جارٍ الآن" : "الموعد التالي"}</span>
          <Clock at={next.start} className="text-[20px] leading-tight" />
          <span className="grid min-w-0 flex-1 basis-48">
            <span className="font-bold">{next.name ?? "بدون اسم"}</span>
            <span className="text-xs text-ink-soft">
              {cairoWhen(next.start)}{fromNow(next, now) !== "الآن" ? ` · ${fromNow(next, now)}` : ""} · {label(BOOKING_SERVICES, next.service)}{next.call_app ? ` (${label(CALL_APPS, next.call_app)})` : ""} · <Phone wa={next.wa_id} />
            </span>
          </span>
        </div>
      ) : null}

      {tomorrows.length > 0 ? (
        <p className={`flex flex-wrap items-center gap-2 rounded-full border px-4 py-2 text-sm ${unconfirmed > 0 ? "border-orange/35 bg-orange-bg" : "border-green/30 bg-green-bg text-green-ink"}`} role="status">
          <Icon name={unconfirmed > 0 ? "bell" : "check-circle"} className={`size-4 shrink-0 ${unconfirmed > 0 ? "text-orange-ink" : ""}`} />
          غدًا: {meetingsWords(tomorrows.length)}
          {unconfirmed > 0 ? <>، <span className="font-semibold text-orange-ink">لم يؤكد الحضور: {unconfirmed}</span></> : "، وأكّد الجميع حضورهم ✅"}
        </p>
      ) : null}

      {list.isPending ? <Loading /> : list.isError ? <ErrorState message={errorMessage(list.error)} onRetry={() => void list.refetch()} /> : days.length === 0 ? (
        <div className="liquid rounded-[22px] p-4 sm:p-5">
          {all.length > 0
            ? <Empty title="لا توجد حجوزات مطابقة" icon="search">جرّب نوعًا آخر أو امسح البحث.</Empty>
            : <Empty title={when === "ahead" ? "لا توجد حجوزات في الثلاثين يومًا القادمة" : "لا توجد حجوزات في الأسبوعين الماضيين"} icon="calendar" />}
        </div>
      ) : days.map((g) => {
        const held = g.items.filter((a) => a.state === "pending_payment").length;
        return (
          <section key={g.day} className="liquid relative grid gap-2.5 overflow-hidden rounded-[22px] p-3 sm:p-4">
            <span aria-hidden className="pointer-events-none absolute inset-x-6 top-0 h-px bg-gradient-to-l from-transparent via-gold/70 to-transparent" />
            <h3 className="flex flex-wrap items-center gap-x-2.5 gap-y-1 px-1">
              {g.day === todayKey || g.day === tomorrowKey ? <span className="rounded-full bg-gold-tint px-2.5 py-0.5 text-[12.5px] font-bold text-gold-ink">{g.day === todayKey ? "اليوم" : "غدًا"}</span> : null}
              <span className="text-[16px] font-bold">{longDay(g.day)}</span>
              <span className="ms-auto flex flex-wrap gap-1.5 text-xs font-normal">
                <span className="rounded-full border border-line/60 bg-card/60 px-2.5 py-0.5 text-ink-soft">{meetingsWords(g.items.length)}</span>
                {held ? <span className="rounded-full bg-orange-bg px-2.5 py-0.5 text-orange-ink">في انتظار الإيصال: {held}</span> : null}
              </span>
            </h3>
            <Timeline items={g.items} now={now} showNow={g.day === todayKey && when === "ahead"} />
          </section>
        );
      })}
    </div>
  );
}
