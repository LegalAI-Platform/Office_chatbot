import { useState, type ReactNode } from "react";
import { queryOptions, useMutation, useQuery, useQueryClient, type QueryClient } from "@tanstack/react-query";
import { ApiError, errorMessage } from "../../shared/api/client";
import { can, useMe } from "../../shared/api/session";
import { APPOINTMENT_STATES, BOOKING_SERVICES, CALL_APPS, label, matterLabel, PAYMENT_STATES, REMINDER_KINDS, REMINDER_SKIPS } from "../../shared/lib/labels";
import { cairoTime, cairoWhen } from "../../shared/lib/time";
import { useUrlState } from "../../shared/lib/url";
import { Phone } from "../../shared/ui/caseCard";
import { Icon, type IconName } from "../../shared/ui/Icon";
import { confirmAction, toast } from "../../shared/ui/feedback";
import { Button, Chip, Empty, ErrorState, Field, Loading } from "../../shared/ui/ui";
import { booking, cairoDate, hhmm, monthOf, shiftMonth, TIMES, type Appointment, type Day, type Done, type Office, type Reminder, type Settings, type WeekDay } from "./api";

type Tab = "calendar" | "bookings" | "week" | "settings" | "office";
// The office week starts on Saturday; the API's weekday is 0 = Sunday … 6 = Saturday.
const WEEK = [6, 0, 1, 2, 3, 4, 5];
const DAY_NAMES = ["الأحد", "الاتنين", "التلات", "الأربع", "الخميس", "الجمعة", "السبت"];
// the calendar's column heads: short, so all seven fit a phone whole
const SHORT_DAYS = ["أحد", "اتنين", "تلات", "أربع", "خميس", "جمعة", "سبت"];
const STATUS: Record<Day["status"], { word: string; tone: string }> = {
  open: { word: "شغال", tone: "green" },
  weekly_off: { word: "إجازة", tone: "grey" },
  closed: { word: "مقفول", tone: "red" },
  custom_hours: { word: "ساعات مختلفة", tone: "blue" },
  partly_closed: { word: "جزء مقفول", tone: "orange" },
};
// Written out in full so Tailwind keeps them.
const DOT: Record<string, string> = { green: "bg-green-ink", grey: "bg-grey-ink", red: "bg-red-ink", blue: "bg-blue-ink", orange: "bg-orange-ink" };
// the line at a booking's start, by the tone of its state
const BAR: Record<string, string> = { green: "bg-green", grey: "bg-ink-soft/40", blue: "bg-blue", orange: "bg-orange", red: "bg-red" };
const longDay = (day: string) =>
  new Intl.DateTimeFormat("ar-EG-u-nu-latn", { weekday: "long", day: "numeric", month: "long", timeZone: "UTC" }).format(new Date(`${day}T12:00:00Z`));
const STATE_TONE: Record<string, string> = {
  pending_payment: "orange", expired: "grey", confirmed: "blue", completed: "green", no_show: "orange", cancelled_by_client: "grey", cancelled_by_office: "grey",
};

const monthQuery = (month: string) => queryOptions({ queryKey: ["booking", "month", month], queryFn: () => booking.month(month) });
const settingsQuery = queryOptions({ queryKey: ["booking", "settings"], queryFn: booking.settings });

/** This month's calendar (the tab the page opens on) and the settings, fetched while the pointer is still on the link (idea from PR #14). */
export function prefetchData(qc: QueryClient) {
  void qc.prefetchQuery(monthQuery(monthOf(cairoDate(new Date()))));
  void qc.prefetchQuery(settingsQuery);
}

const TABS: { value: Tab; label: string; icon: IconName; needsOffice?: boolean }[] = [
  { value: "calendar", label: "التقويم", icon: "calendar" },
  { value: "bookings", label: "الحجوزات", icon: "clients" },
  { value: "week", label: "المواعيد الثابتة", icon: "clock" },
  { value: "settings", label: "إعدادات الحجز", icon: "settings" },
  { value: "office", label: "بيانات المكتب", icon: "office", needsOffice: true },
];

/**
 * «المواعيد» (TASK 08, DECISIONS v1.12 B-24) as «liquid glass»: the booking hours Hazem offers, changed from here and
 * seen by Hazem at once. A glass header, the sections as a glass segmented control, and each section glass cards:
 * the calendar of the month (close a day, close some hours, other hours that day), the appointments, the default week,
 * the settings and (manager / lawyer) the office's details.
 */
export function BookingPage() {
  const { data: me } = useMe();
  const tabs = TABS.filter((t) => !t.needsOffice || can(me, "office.manage"));
  const [tab, setTab] = useUrlState<Tab>("tab", "calendar", tabs.map((t) => t.value));

  return (
    <div className="relative isolate grid gap-5 pb-4">
      {/* the soft light the glass shows through */}
      <div aria-hidden className="pointer-events-none absolute inset-x-0 -top-6 -z-10 h-[30rem] overflow-hidden">
        <span className="absolute -top-16 end-[4%] size-[24rem] rounded-full bg-gold/30 blur-[100px]" />
        <span className="absolute top-24 start-[10%] size-[20rem] rounded-full bg-blue/10 blur-[100px]" />
      </div>

      <Intro />

      <section aria-label="الأقسام" className="liquid grid rounded-[22px] p-2 sm:p-2.5">
        <div className="flex flex-wrap gap-1 rounded-[20px] border border-line/60 bg-card/45 p-1 shadow-[inset_0_1px_2px_rgb(0_0_0/0.06)]" role="tablist" aria-label="أقسام المواعيد">
          {tabs.map((t) => {
            const on = t.value === tab;
            return (
              <button key={t.value} type="button" role="tab" aria-selected={on} onClick={() => setTab(t.value)}
                className={`flex min-h-10 grow basis-[9rem] items-center justify-center gap-1.5 whitespace-nowrap rounded-[16px] px-3.5 text-[13.5px] transition motion-reduce:transition-none ${on
                  ? "btn-gold font-bold shadow-[inset_0_1px_0_rgb(255_255_255/0.4),0_6px_14px_-6px_rgb(140_98_58/0.8)]"
                  : "text-ink hover:bg-card/80"}`}>
                <Icon name={t.icon} className="size-4" />
                {t.label}
              </button>
            );
          })}
        </div>
      </section>

      {tab === "calendar" ? <Calendar /> : tab === "bookings" ? <Bookings /> : tab === "week" ? <Week /> : tab === "settings" ? <SettingsForm /> : <OfficeForm />}
    </div>
  );
}

// ---------- the top ----------

function Intro() {
  return (
    <header className="liquid relative overflow-hidden rounded-[26px] p-5 sm:p-6">
      <span aria-hidden className="absolute -top-20 -start-16 size-64 rounded-full bg-gradient-to-br from-gold/30 to-transparent blur-2xl" />
      <span aria-hidden className="absolute inset-x-8 top-0 h-px bg-gradient-to-l from-transparent via-gold to-transparent opacity-70" />
      <div className="relative grid gap-4">
        <div className="flex items-center gap-4">
          <span aria-hidden className="grid size-14 shrink-0 place-items-center rounded-[18px] bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6),0_8px_20px_-10px_rgb(140_98_58/0.7)]">
            <Icon name="calendar" className="size-7" />
          </span>
          <h1 className="text-[26px] leading-tight font-bold">المواعيد</h1>
        </div>
        <div className="grid gap-3 border-t border-line/60 pt-4 lg:grid-cols-[minmax(0,1fr)_auto] lg:items-start">
          <Status />
          <Undo />
        </div>
      </div>
    </header>
  );
}

/** Whether Hazem books from WhatsApp now, and what is missing if not. */
function Status() {
  const s = useQuery(settingsQuery);
  if (!s.data) return null;
  const missing = [
    !s.data.calendar_key && "مفتاح Google Calendar (صاحب المشروع بيحطه في .env)",
    !s.data.calendar_id && "إيميل كالندر المستشار (في إعدادات الحجز)",
    !s.data.texts_approved && "اعتماد نصوص الحجز (من «نصوص حازم»)",
  ].filter(Boolean);
  const ok = s.data.enabled && missing.length === 0;
  return (
    <p role="status" className={`flex items-start gap-2 rounded-[16px] border px-3.5 py-2.5 text-[13.5px] leading-relaxed ${ok ? "border-green/30 bg-green-bg text-green-ink" : "border-orange/30 bg-orange-bg text-orange-ink"}`}>
      <Icon name={ok ? "check-circle" : "alert-circle"} className="mt-0.5 size-4 shrink-0" />
      <span>
        {s.data.enabled ? "حازم بيحجز من واتساب." : "الحجز من واتساب مقفول: حازم بيعرض الاستشارة من غير مواعيد."}
        {missing.length > 0 ? ` ناقص: ${missing.join("، ")}.` : ""}
      </span>
    </p>
  );
}

/** Puts back the last change; when that would leave bookings outside the hours it asks first, like any change. */
function Undo() {
  const qc = useQueryClient();
  const changes = useQuery({ queryKey: ["booking", "changes"], queryFn: booking.changes });
  const [affected, setAffected] = useState<Appointment[] | null>(null);
  const undo = useMutation({
    mutationFn: (confirm: boolean) => booking.undo(confirm),
    onSuccess: () => { setAffected(null); void qc.invalidateQueries({ queryKey: ["booking"] }); },
    onError: (e) => { if (e instanceof ApiError && e.code === "has_bookings") setAffected(e.data?.appointments as Appointment[]); },
  });
  const last = changes.data?.find((c) => !c.undone);
  if (!last) return null;
  return (
    <div className="grid gap-2">
      <span className="flex flex-wrap items-center gap-2 rounded-[16px] border border-line/60 bg-card/55 py-1 ps-3.5 pe-1 text-[13px] text-ink-soft shadow-[inset_0_1px_0_rgb(255_255_255/0.45)]">
        <Icon name="clock" className="size-4 shrink-0 text-gold-ink" />
        آخر تعديل: {last.by ?? "—"}، {cairoWhen(last.at)}
        <Button variant="ghost" className="!min-h-9 !px-3" busy={undo.isPending && !affected} onClick={() => undo.mutate(false)}><Icon name="back" className="size-4" />تراجع</Button>
        {undo.isError && !affected ? <span className="text-bad" role="alert">{errorMessage(undo.error)}</span> : null}
      </span>
      {affected ? <Confirm list={affected} busy={undo.isPending} onYes={() => undo.mutate(true)} onNo={() => setAffected(null)} /> : null}
    </div>
  );
}

// ---------- the glass pieces every tab is built from ----------

/** A glass card with an icon tile, a title and an optional line under it. */
function Sheet({ icon, title, hint, aside, children, className = "" }: {
  icon: IconName; title: ReactNode; hint?: ReactNode; aside?: ReactNode; children: ReactNode; className?: string;
}) {
  return (
    <section className={`liquid relative grid min-w-0 content-start gap-4 overflow-hidden rounded-[22px] p-4 sm:p-5 ${className}`}>
      <span aria-hidden className="pointer-events-none absolute inset-x-6 top-0 h-px bg-gradient-to-l from-transparent via-gold/70 to-transparent" />
      <div className="flex flex-wrap items-start gap-3">
        <span aria-hidden className="grid size-10 shrink-0 place-items-center rounded-2xl bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.5)]">
          <Icon name={icon} className="size-5" />
        </span>
        <div className="grid min-w-0 flex-1 basis-56 gap-0.5">
          <h2 className="flex flex-wrap items-center gap-2 text-[17px] leading-snug font-bold">{title}</h2>
          {hint ? <p className="text-[12.5px] leading-relaxed text-ink-soft">{hint}</p> : null}
        </div>
        {aside}
      </div>
      {children}
    </section>
  );
}

/** Glass segmented pills. */
function Segmented<T extends string>({ label: name, value, options, onChange }: {
  label: string; value: T; options: { value: T; label: string; icon: IconName }[]; onChange: (v: T) => void;
}) {
  return (
    <div className="grid min-w-0 content-start">
      <div className="flex flex-wrap gap-1 rounded-[20px] border border-line/60 bg-card/45 p-1 shadow-[inset_0_1px_2px_rgb(0_0_0/0.06)]" role="group" aria-label={name}>
        {options.map((o) => {
          const on = value === o.value;
          return (
            <button key={o.value} type="button" aria-pressed={on} onClick={() => onChange(o.value)}
              className={`flex min-h-9 grow items-center justify-center gap-1.5 whitespace-nowrap rounded-[16px] px-3.5 text-[13.5px] transition motion-reduce:transition-none ${on
                ? "btn-gold font-bold shadow-[inset_0_1px_0_rgb(255_255_255/0.4),0_6px_14px_-6px_rgb(140_98_58/0.8)]"
                : "text-ink hover:bg-card/80"}`}>
              <Icon name={o.icon} className="size-4" />
              {o.label}
            </button>
          );
        })}
      </div>
    </div>
  );
}

/** The save row at the bottom of a form: «not saved yet» while there is a draft, then the buttons. */
function SaveBar({ dirty = false, children }: { dirty?: boolean; children: ReactNode }) {
  return (
    <div className="liquid flex flex-wrap items-center gap-3 rounded-[18px] px-4 py-3">
      {dirty ? (
        <p className="flex min-w-0 flex-1 basis-60 items-center gap-1.5 text-[13px] font-semibold text-gold-ink" role="status">
          <Icon name="alert-circle" className="size-4 shrink-0" />
          فيه تعديلات لسه ما اتحفظتش.
        </p>
      ) : null}
      <div className="flex flex-wrap gap-2">{children}</div>
    </div>
  );
}

const ROW = "relative overflow-hidden rounded-[14px] border border-line/60 bg-card/55 shadow-[inset_0_1px_0_rgb(255_255_255/0.45),0_4px_14px_-8px_rgb(60_44_20/0.25)]";
const SELECT = "min-h-10 rounded-[12px] border border-line-strong bg-card/80 px-2 text-ink";

// ---------- the month ----------

function Calendar() {
  // opens on today (idea from PR #14): the first question every morning is «who is coming today»
  const today = cairoDate(new Date());
  const [month, setMonth] = useState(() => monthOf(today));
  const [picked, setPicked] = useState<string | null>(today);
  const days = useQuery(monthQuery(month));
  const first = days.data?.[0];
  const lead = first ? WEEK.indexOf(new Date(`${first.day}T12:00:00Z`).getUTCDay()) : 0;
  const day = days.data?.find((d) => d.day === picked);
  const nav = "grid size-10 place-items-center rounded-full border border-line/60 bg-card/60 text-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.45)] transition hover:border-gold-line hover:bg-gold-tint hover:text-gold-ink motion-reduce:transition-none";

  return (
    <div className="grid items-start gap-4 xl:grid-cols-[minmax(0,1fr)_26rem]">
      <section className="liquid relative grid min-w-0 gap-4 overflow-hidden rounded-[22px] p-4 sm:p-5">
        <span aria-hidden className="pointer-events-none absolute inset-x-6 top-0 h-px bg-gradient-to-l from-transparent via-gold/70 to-transparent" />
        <div className="flex items-center justify-between gap-2">
          <button type="button" className={nav} aria-label="الشهر اللي فات" onClick={() => setMonth(shiftMonth(month, -1))}><Icon name="chevron-start" className="size-5" /></button>
          <h2 className="flex flex-wrap items-center justify-center gap-2 text-center text-[17px] font-bold tabular">
            {new Intl.DateTimeFormat("ar-EG-u-nu-latn", { month: "long", year: "numeric", timeZone: "UTC" }).format(new Date(`${month}-15T00:00:00Z`))}
            {month !== monthOf(today) || picked !== today ? (
              <button type="button" className="rounded-full border border-gold-line bg-gold-tint px-3 py-0.5 text-[12.5px] font-bold text-gold-ink transition hover:brightness-105" onClick={() => { setMonth(monthOf(today)); setPicked(today); }}>النهارده</button>
            ) : null}
          </h2>
          <button type="button" className={nav} aria-label="الشهر الجاي" onClick={() => setMonth(shiftMonth(month, 1))}><Icon name="chevron-end" className="size-5" /></button>
        </div>
        {days.isPending ? <Loading /> : days.isError ? <ErrorState message={errorMessage(days.error)} onRetry={() => void days.refetch()} /> : (
          <div className="grid grid-cols-7 gap-1 text-center sm:gap-1.5" role="grid" aria-label="أيام الشهر">
            {WEEK.map((w) => <span key={w} className="py-1 text-[11.5px] font-bold text-ink-soft">{SHORT_DAYS[w]}</span>)}
            {Array.from({ length: lead }, (_, i) => <span key={`x${i}`} />)}
            {days.data.map((d) => {
              const on = d.day === picked;
              return (
                <button key={d.day} type="button" role="gridcell" aria-selected={on} onClick={() => setPicked(d.day)}
                  aria-label={`${d.day}: ${STATUS[d.status].word}${d.bookings ? `، ${d.bookings} حجز` : ""}`}
                  aria-current={d.day === today ? "date" : undefined}
                  className={`grid min-h-14 min-w-0 content-start justify-items-center gap-0.5 rounded-[12px] border p-1 text-sm transition motion-reduce:transition-none sm:min-h-16 ${on
                    ? "border-gold bg-gold-tint shadow-[inset_0_1px_0_rgb(255_255_255/0.5),0_6px_14px_-8px_rgb(140_98_58/0.7)]"
                    : "border-line/60 bg-card/45 hover:-translate-y-px hover:border-gold-line hover:bg-card/80 motion-reduce:hover:translate-y-0"} ${d.status === "closed" || d.status === "weekly_off" ? "opacity-60" : ""}`}>
                  <span className={`tabular font-semibold ${d.day === today ? "rounded-full bg-gradient-to-b from-[#dcb98c] to-[#a87a48] px-1.5 text-[#2a2117]" : ""}`}>{Number(d.day.slice(8))}</span>
                  <span className={`size-2 rounded-full ${DOT[STATUS[d.status].tone]}`} aria-hidden />
                  {d.bookings ? <span className="tabular text-[10.5px] leading-tight text-ink-soft sm:text-[11px]">{d.bookings} حجز</span> : null}
                </button>
              );
            })}
          </div>
        )}
        <ul className="flex flex-wrap gap-2 border-t border-line/60 pt-3 text-xs" aria-label="مفتاح الألوان">
          {Object.values(STATUS).map((s) => <li key={s.word}><Chip tone={s.tone}>{s.word}</Chip></li>)}
        </ul>
      </section>
      {/* keyed by the date: nothing typed or asked about one day carries over to another */}
      {day ? <DaySheet key={day.day} day={day} isToday={day.day === today} /> : (
        <section className="liquid rounded-[22px] p-4 sm:p-5">
          <Empty title="اختار يوم" icon="calendar">تقفله، أو تقفل جزء منه، أو تديله ساعات مختلفة، وتشوف حازم هيعرض إيه.</Empty>
        </section>
      )}
    </div>
  );
}

const KINDS = [
  ["closed_day", "اقفل اليوم كله"],
  ["closed_hours", "اقفل جزء منه"],
  ["custom_hours", "ساعات مختلفة لليوم ده"],
] as const;

/** One date: its hours, its exceptions, closing it (asks first if it has bookings), and the preview. */
function DaySheet({ day, isToday }: { day: Day; isToday: boolean }) {
  const qc = useQueryClient();
  const [kind, setKind] = useState<"closed_day" | "closed_hours" | "custom_hours">("closed_day");
  const [from, setFrom] = useState(600);
  const [to, setTo] = useState(840);
  const [reason, setReason] = useState("");
  // A change that would cancel bookings: the list, and the same change again with confirm.
  const [ask, setAsk] = useState<{ list: Appointment[]; again: () => void } | null>(null);
  const [done, setDone] = useState<Done | null>(null);
  const finished = (r: Done) => { setAsk(null); setDone(r); refresh(); };
  const asked = (e: Error, again: () => void) => {
    if (e instanceof ApiError && e.code === "has_bookings") setAsk({ list: e.data?.appointments as Appointment[], again });
  };
  const refresh = () => void qc.invalidateQueries({ queryKey: ["booking"] });
  const add = useMutation({
    mutationFn: (confirm: boolean) => booking.addException({ day: day.day, kind, ...(kind === "closed_day" ? {} : { start: from, end: to }), reason, confirm }),
    onSuccess: (r) => { finished(r); setReason(""); },
    onError: (e) => asked(e, () => add.mutate(true)),
  });
  const remove = useMutation({
    mutationFn: ({ id, confirm }: { id: string; confirm: boolean }) => booking.removeException(id, confirm),
    onSuccess: finished,
    onError: (e, v) => asked(e, () => remove.mutate({ id: v.id, confirm: true })),
  });

  return (
    <Sheet icon="calendar" title={<>{isToday ? "النهارده · " : ""}{longDay(day.day)} <Chip tone={STATUS[day.status].tone}>{STATUS[day.status].word}</Chip></>}>
      {day.bookings ? <DayBookings day={day.day} /> : null}
      <p className={`flex items-start gap-2 px-3.5 py-2.5 text-[13.5px] ${ROW}`}>
        <Icon name="clock" className="mt-0.5 size-4 shrink-0 text-gold-ink" />
        <span>{day.hours.length === 0 ? "مفيش حجز اليوم ده." : `حازم بيحجز: ${day.hours.map((h) => `${hhmm(h.start)}–${hhmm(h.end)}`).join("، ")}`}</span>
      </p>
      {day.exceptions.length > 0 ? (
        <div className="grid gap-1.5">
          <p className="text-[12px] font-bold text-ink-soft">تغييرات اليوم ده</p>
          <ul className="grid gap-1.5">
            {day.exceptions.map((e) => (
              <li key={e.id} className={`flex flex-wrap items-center gap-2 py-1.5 ps-4 pe-1.5 text-sm ${ROW}`}>
                <span aria-hidden className={`absolute inset-y-2 start-0 w-1 rounded-e-full ${e.kind === "custom_hours" ? "bg-blue" : "bg-red"}`} />
                <span className="min-w-0 flex-1">
                  {e.kind === "closed_day" ? "اليوم مقفول" : `${e.kind === "closed_hours" ? "مقفول" : "ساعات"} ${hhmm(e.start!)}–${hhmm(e.end!)}`}
                  {e.reason ? ` · ${e.reason}` : ""}{e.by ? ` · ${e.by}` : ""}
                </span>
                <Button variant="ghost" className="!min-h-9 !px-3" busy={remove.isPending && remove.variables?.id === e.id} onClick={() => remove.mutate({ id: e.id, confirm: false })}><Icon name="x" className="size-4" />شيل</Button>
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      <fieldset className="grid gap-2 border-t border-line/60 pt-3">
        <legend className="float-start mb-1 w-full pt-3 text-[14px] font-bold">غيّر اليوم ده</legend>
        {KINDS.map(([k, t]) => (
          <label key={k} className="flex cursor-pointer items-center gap-2.5 rounded-[14px] border border-line/60 bg-card/45 px-3 py-2.5 text-sm transition focus-within:border-gold has-checked:border-gold has-checked:bg-gold-tint motion-reduce:transition-none">
            <input type="radio" name="kind" className="accent-[#a87a48]" checked={kind === k} onChange={() => setKind(k)} />
            <span className="font-semibold">{t}</span>
          </label>
        ))}
        {kind !== "closed_day" ? (
          <div className="flex flex-wrap items-center gap-2 text-sm">
            من <TimeSelect value={from} onChange={setFrom} label="من" /> لـ <TimeSelect value={to} onChange={setTo} label="لـ" />
          </div>
        ) : null}
        <Field label="السبب (للمكتب بس)" value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} />
        <Button variant="primary" busy={add.isPending && !ask} onClick={() => add.mutate(false)}>احفظ</Button>
      </fieldset>
      {ask ? (
        <Confirm list={ask.list} busy={add.isPending || remove.isPending} onYes={ask.again} onNo={() => setAsk(null)} />
      ) : add.isError || remove.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(add.error ?? remove.error)}</p> : null}
      {done && done.cancelled > 0 ? <Told n={done.cancelled} /> : null}
      <PreviewOf day={day.day} />
    </Sheet>
  );
}

/** The clients are told by Hazem within seconds (the worker); one he cannot reach becomes a task. */
const Told = ({ n }: { n: number }) => (
  <p className="flex items-start gap-2 rounded-[14px] border border-green/30 bg-green-bg px-3.5 py-2.5 text-sm text-green-ink" role="status">
    <Icon name="check-circle" className="mt-0.5 size-4 shrink-0" />
    <span>اتلغى {n} حجز، وحازم هيبلّغ العملاء خلال ثواني (اللي ماتوصلوش الرسالة بتتعمله مهمة).</span>
  </p>
);

/** «الحجوزات دي هتتلغي»: the office cancels and Hazem tells each client (B-22: another time or a full refund). */
function Confirm({ list, busy, onYes, onNo }: { list: Appointment[]; busy: boolean; onYes: () => void; onNo: () => void }) {
  return (
    <div className="grid gap-2.5 rounded-[16px] border border-orange-ink/40 bg-orange-bg p-3.5 text-sm shadow-[inset_0_1px_0_rgb(255_255_255/0.35)]" role="alertdialog" aria-label="فيه حجوزات">
      <p className="flex items-start gap-2 font-semibold text-orange-ink">
        <Icon name="alert" className="mt-0.5 size-4 shrink-0" />
        <span>التعديل ده فيه {list.length} حجز (مؤكد أو مستني الإيصال). لو كمّلت، الحجوزات دي هتتلغي وحازم هيبعت لكل عميل يختار ميعاد تاني، أو استرداد كامل لو كان دفع.</span>
      </p>
      <ul className="grid gap-1">{list.map((a) => <li key={a.id} className="rounded-[10px] bg-card/60 px-3 py-1.5">{cairoWhen(a.start)} · {a.name ?? "من غير اسم"} · {label(BOOKING_SERVICES, a.service)}</li>)}</ul>
      <div className="flex flex-wrap gap-2">
        <Button variant="danger" busy={busy} onClick={onYes}>كمّل وبلّغ العملاء</Button>
        <Button onClick={onNo}>لأ، سيبه</Button>
      </div>
    </div>
  );
}

/** The day's bookings, in time order (the calendar opens on today with them, idea from PR #14). */
function DayBookings({ day }: { day: string }) {
  const list = useQuery({ queryKey: ["booking", "appointments", day, day], queryFn: () => booking.appointments(day, day), refetchInterval: 60_000 });
  if (list.isPending) return <Loading />;
  if (list.isError) return <ErrorState message={errorMessage(list.error)} onRetry={() => void list.refetch()} />;
  const items = list.data.slice().sort((a, b) => a.start.localeCompare(b.start));
  if (items.length === 0) return null;
  return (
    <section aria-label="حجوزات اليوم" className="grid gap-2">
      <h3 className="flex items-center gap-2 text-[14px] font-bold">
        <Icon name="clients" className="size-4 text-gold-ink" />
        الحجوزات ({items.length})
      </h3>
      <ul className="grid gap-1.5">
        {items.map((a) => (
          <li key={a.id} className={`flex flex-wrap items-center gap-x-3 gap-y-1 py-2 ps-4 pe-2.5 text-sm ${ROW}`}>
            <span aria-hidden className={`absolute inset-y-2 start-0 w-1 rounded-e-full ${BAR[STATE_TONE[a.state] ?? "grey"]}`} />
            <span className="tabular font-bold">{cairoTime(new Date(a.start))}</span>
            <span className="min-w-0 flex-1">
              {a.name ?? "من غير اسم"}
              <span className="block text-xs text-ink-soft">{label(BOOKING_SERVICES, a.service)}{a.call_app ? ` (${label(CALL_APPS, a.call_app)})` : ""} · <Phone wa={a.wa_id} /></span>
            </span>
            <Chip tone={STATE_TONE[a.state] ?? "grey"}>{label(APPOINTMENT_STATES, a.state)}</Chip>
          </li>
        ))}
      </ul>
    </section>
  );
}

/** What Hazem would offer that day now (the same rules as the conversation). */
function PreviewOf({ day }: { day: string }) {
  const [service, setService] = useState("pv");
  const p = useQuery({ queryKey: ["booking", "preview", day, service], queryFn: () => booking.preview(day, service) });
  return (
    <div className="grid gap-2 border-t border-line/60 pt-3">
      <div className="flex flex-wrap items-center gap-2">
        <h3 className="flex items-center gap-2 text-[14px] font-bold"><Icon name="whatsapp" className="size-4 text-gold-ink" />حازم هيعرض إيه</h3>
        <select aria-label="الخدمة" className={`${SELECT} !min-h-9 text-sm`} value={service} onChange={(e) => setService(e.target.value)}>
          <option value="pv">{BOOKING_SERVICES.consultation_phone_video}</option>
          <option value="of">{BOOKING_SERVICES.consultation_office}</option>
          <option value="ur">{BOOKING_SERVICES.consultation_urgent}</option>
        </select>
      </div>
      {p.isPending ? <Loading /> : p.isError ? <ErrorState message={errorMessage(p.error)} /> : (
        <>
          {p.data.calendar !== "ok" ? <p className="text-xs text-orange-ink">كالندر المستشار {p.data.calendar === "not_configured" ? "مش متوصل" : "مش بيرد"}: دي ساعات اللوحة بس، وحازم مش هيعرض مواعيد لحد ما يتوصل.</p> : null}
          {p.data.slots.length === 0 ? <p className="text-sm text-ink-soft">مفيش مواعيد فاضية اليوم ده.</p> : (
            <ul className="flex flex-wrap gap-1.5">{p.data.slots.map((s) => <li key={s} className="tabular rounded-full border border-gold-line bg-gold-tint px-2.5 py-1 text-xs font-semibold text-gold-ink">{cairoTime(new Date(s))}</li>)}</ul>
          )}
        </>
      )}
    </div>
  );
}

function TimeSelect({ value, onChange, label: aria, min = 0 }: { value: number; onChange: (m: number) => void; label: string; min?: number }) {
  return (
    <select aria-label={aria} className={`${SELECT} tabular`} value={value} onChange={(e) => onChange(Number(e.target.value))}>
      {TIMES.filter((t) => t >= min).map((t) => <option key={t} value={t}>{t === 1440 ? "12 بالليل" : hhmm(t)}</option>)}
    </select>
  );
}

// ---------- the appointments ----------

const DAY_MS = 86_400_000;
type Kind = "all" | "phone" | "office" | "urgent" | "urf";
const KIND_TEST: Record<Kind, (a: Appointment) => boolean> = {
  all: () => true,
  phone: (a) => a.service === "consultation_phone_video",
  office: (a) => a.service === "consultation_office",
  urgent: (a) => a.service === "consultation_urgent",
  urf: (a) => a.service.startsWith("urf_"),
};
/** Still on the office's day: held, or confirmed and not marked. */
const isLive = (a: Appointment) => a.state === "confirmed" || a.state === "pending_payment";
const minutesOf = (a: Appointment) => Math.round((new Date(a.end).getTime() - new Date(a.start).getTime()) / 60_000);
const lengthWords = (m: number) => (m === 15 ? "ربع ساعة" : m === 30 ? "نص ساعة" : m === 60 ? "ساعة" : `${m} دقيقة`);

/** «بعد 40 دقيقة»، «بعد 3 ساعات»، «بعد يومين»، «دلوقتي»، «من ساعتين»: how far the appointment is. */
function fromNow(a: Appointment, now: Date): string {
  const start = new Date(a.start).getTime();
  const end = new Date(a.end).getTime();
  const t = now.getTime();
  if (t >= start && t < end) return "دلوقتي";
  const ahead = start > t;
  const min = Math.round(Math.abs((ahead ? start : end) - t) / 60_000);
  const words = min < 60 ? `${min} دقيقة` : min < 24 * 60 ? (Math.round(min / 60) === 1 ? "ساعة" : Math.round(min / 60) === 2 ? "ساعتين" : `${Math.round(min / 60)} ساعات`)
    : Math.round(min / 1440) === 1 ? "يوم" : Math.round(min / 1440) === 2 ? "يومين" : `${Math.round(min / 1440)} أيام`;
  return ahead ? `بعد ${words}` : `من ${words}`;
}

/** What the office should look at on a booking (PR #14's notes): nothing when all is well. */
function notesOf(a: Appointment, now: Date): { text: string; tone: string }[] {
  const out: { text: string; tone: string }[] = [];
  const t = now.getTime();
  if (a.state === "pending_payment" && a.hold_until && new Date(a.hold_until).getTime() - t < 30 * 60_000) {
    out.push({ text: "الحجز المبدئي قرّب يخلص", tone: "orange" });
  }
  if (a.state === "confirmed" && (a.payment === "proof_received" || a.payment === "duplicate_suspected")) {
    out.push({ text: "التحويل محتاج مراجعة (الإيصالات والمدفوعات)", tone: "orange" });
  }
  if (a.state === "confirmed" && a.calendar !== "synced" && new Date(a.end).getTime() > t) {
    out.push({ text: "لسه مش في كالندر المستشار", tone: "grey" });
  }
  if (a.state === "confirmed" && new Date(a.end).getTime() < t) {
    out.push({ text: "عدّى: علّم «خلص» أو «ماحضرش»", tone: "orange" });
  }
  return out;
}

const WHENS: { value: "ahead" | "past"; label: string; icon: IconName }[] = [
  { value: "ahead", label: "الجاية (30 يوم)", icon: "forward" },
  { value: "past", label: "اللي فاتت (أسبوعين)", icon: "back" },
];
const KIND_ICON: Record<Kind, IconName> = { all: "calendar", phone: "video", office: "office", urgent: "alert", urf: "document" };
const TILE_ICON: Record<string, IconName> = {
  "النهارده": "clock", "الأسبوع ده": "calendar", "مستني الإيصال": "receipt", "تحويل محتاج مراجعة": "alert-circle",
  "خلصت": "check-circle", "ماحضروش": "user-plus", "اتلغت": "x", "اتفكت (الإيصال ما وصلش)": "receipt",
};

function Bookings() {
  const qc = useQueryClient();
  const { data: me } = useMe();
  const [when, setWhen] = useUrlState<"ahead" | "past">("when", "ahead", ["ahead", "past"]);
  const [kind, setKind] = useUrlState<Kind>("kind", "all", ["all", "phone", "office", "urgent", "urf"]);
  const [q, setQ] = useState("");
  // Ahead: today and the next 30 days. Past (TASK 10): the last two weeks, so a consultation that took place can get
  // «مطلوب رأي مكتوب» (B-17), most recent first.
  const now = new Date();
  const from = when === "ahead" ? cairoDate(now) : cairoDate(new Date(now.getTime() - 14 * DAY_MS));
  const to = when === "ahead" ? cairoDate(new Date(now.getTime() + 30 * DAY_MS)) : cairoDate(new Date(now.getTime() - DAY_MS));
  const list = useQuery({ queryKey: ["booking", "appointments", from, to], queryFn: () => booking.appointments(from, to), refetchInterval: 60_000 });
  const refresh = () => void qc.invalidateQueries({ queryKey: ["booking"] });
  const mark = useMutation({
    mutationFn: ({ id, state }: { id: string; state: string }) => booking.mark(id, state),
    onSuccess: (_, v) => { refresh(); toast(v.state === "completed" ? "اتعلّم إن الميعاد خلص ✅" : "اتعلّم إن العميل ماحضرش"); },
  });
  const cancel = useMutation({ mutationFn: booking.cancel, onSuccess: () => { refresh(); toast("الحجز اتلغى، وحازم هيبلّغ العميل"); } });

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
  const tiles: { label: string; value: number | undefined; tone: string }[] = when === "ahead" ? [
    { label: "النهارده", value: count((a) => isLive(a) && cairoDate(new Date(a.start)) === todayKey), tone: "text-blue-ink" },
    { label: "الأسبوع ده", value: count((a) => isLive(a) && cairoDate(new Date(a.start)) <= weekEnd), tone: "text-blue-ink" },
    { label: "مستني الإيصال", value: count((a) => a.state === "pending_payment"), tone: "text-orange-ink" },
    { label: "تحويل محتاج مراجعة", value: count((a) => a.state === "confirmed" && (a.payment === "proof_received" || a.payment === "duplicate_suspected")), tone: "text-red-ink" },
  ] : [
    { label: "خلصت", value: count((a) => a.state === "completed"), tone: "text-green-ink" },
    { label: "ماحضروش", value: count((a) => a.state === "no_show"), tone: "text-orange-ink" },
    { label: "اتلغت", value: count((a) => a.state.startsWith("cancelled")), tone: "text-ink" },
    { label: "اتفكت (الإيصال ما وصلش)", value: count((a) => a.state === "expired"), tone: "text-ink" },
  ];
  const kinds: { value: Kind; label: string; icon: IconName }[] = ([["all", `الكل ${all.length}`], ["phone", "تليفون أو فيديو"], ["office", "في المكتب"], ["urgent", "عاجلة"], ["urf", "عرفي"]] as const)
    .map(([v, t]) => ({ value: v, label: t, icon: KIND_ICON[v] }));

  const row = (a: Appointment) => (
    <li key={a.id} className={`flex flex-wrap items-center gap-3 py-2.5 ps-4 pe-3 transition hover:bg-card/80 motion-reduce:transition-none ${ROW}`}>
      <span aria-hidden className={`absolute inset-y-2.5 start-0 w-1 rounded-e-full ${BAR[STATE_TONE[a.state] ?? "grey"]}`} />
      <span className="tabular grid min-w-[4.25rem] shrink-0 place-items-center rounded-[12px] border border-line/60 bg-card/70 px-2 py-1.5 text-[15px] font-bold shadow-[inset_0_1px_0_rgb(255_255_255/0.45)]">
        {cairoTime(new Date(a.start))}
      </span>
      <span className="grid min-w-0 flex-1 basis-60 gap-0.5">
        <span className="flex flex-wrap items-baseline gap-x-2">
          <span className="font-bold">{a.name ?? "من غير اسم"}</span>
          <span className="text-xs font-normal text-ink-soft">{lengthWords(minutesOf(a))}{isLive(a) ? ` · ${fromNow(a, now)}` : ""}</span>
        </span>
        <span className="text-xs text-ink-soft">
          {label(BOOKING_SERVICES, a.service)}{a.call_app ? ` (${label(CALL_APPS, a.call_app)})` : ""} · {matterLabel(a.matter_type ?? undefined)} · <Phone wa={a.wa_id} />{a.reschedules ? " · اتغيّر مرة" : ""}
          {a.payment && a.state !== "pending_payment" ? ` · الدفع: ${label(PAYMENT_STATES, a.payment)}` : ""}
        </span>
        {a.state === "confirmed" && a.reminders.length > 0 ? <Reminders items={a.reminders} /> : null}
        {notesOf(a, now).length > 0 ? (
          <span className="flex flex-wrap gap-1.5 pt-1">
            {notesOf(a, now).map((n) => (
              <span key={n.text} className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-semibold ${n.tone === "orange" ? "bg-orange-bg text-orange-ink" : "bg-grey-bg text-grey-ink"}`}>
                <Icon name="alert-circle" className="size-3.5" />{n.text}
              </span>
            ))}
          </span>
        ) : null}
      </span>
      <span className="flex flex-wrap items-center gap-2">
        {a.state === "confirmed" && a.attending ? <Chip tone="green">أكّد حضوره ✅</Chip> : null}
        <Chip tone={STATE_TONE[a.state] ?? "grey"}>
          {label(APPOINTMENT_STATES, a.state)}{a.state === "pending_payment" && a.hold_until ? ` لحد ${cairoTime(new Date(a.hold_until))}` : ""}
        </Chip>
      </span>
      {a.state === "completed" && can(me, "services.price") ? <Opinion id={a.id} /> : null}
      {isLive(a) ? (
        <span className="flex flex-wrap gap-1">
          {a.state === "confirmed" ? (
            <>
              <Button variant="ghost" className="!min-h-9 !px-3" busy={mark.isPending && mark.variables?.id === a.id && mark.variables.state === "completed"}
                onClick={() => mark.mutate({ id: a.id, state: "completed" })}>خلص</Button>
              <Button variant="ghost" className="!min-h-9 !px-3" busy={mark.isPending && mark.variables?.id === a.id && mark.variables.state === "no_show"}
                onClick={() => mark.mutate({ id: a.id, state: "no_show" })}>ماحضرش</Button>
            </>
          ) : null}
          <Button variant="danger" className="!min-h-9 !px-3" busy={cancel.isPending && cancel.variables === a.id}
            onClick={async () => {
              if (await confirmAction({ title: "تلغي الحجز ده؟", body: "حازم هيبعت للعميل يختار ميعاد تاني، أو استرداد كامل لو كان دفع.", yes: "أيوه، الغيه", danger: true })) cancel.mutate(a.id);
            }}>
            إلغاء
          </Button>
        </span>
      ) : null}
    </li>
  );

  return (
    <div className="grid gap-4">
      <section aria-label="فلاتر الحجوزات" className="liquid grid gap-4 rounded-[22px] p-4 sm:p-5 2xl:grid-cols-[minmax(0,1fr)_minmax(0,1.6fr)]">
        <div className="grid content-start gap-4">
          <Segmented label="أنهي حجوزات" value={when} onChange={setWhen} options={WHENS} />
          <label className="relative w-full">
            <span className="sr-only">دوّر بالاسم أو الرقم</span>
            <Icon name="search" className="pointer-events-none absolute start-3 top-1/2 size-4 -translate-y-1/2 text-ink-soft" />
            <input type="search" value={q} onChange={(e) => setQ(e.target.value)} placeholder="دوّر بالاسم أو الرقم"
              className="min-h-10 w-full rounded-full border border-line/70 bg-card/60 ps-9 pe-3 text-sm text-ink shadow-[inset_0_1px_2px_rgb(0_0_0/0.06)] focus:border-gold" />
          </label>
        </div>
        <Segmented label="نوع الميعاد" value={kind} onChange={setKind} options={kinds} />
      </section>

      <section aria-label="ملخص الحجوزات" className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        {tiles.map((t) => (
          <div key={t.label} className="liquid relative grid content-start gap-0.5 overflow-hidden rounded-[18px] p-4">
            <span aria-hidden className="absolute end-3 top-3 grid size-8 place-items-center rounded-xl bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.5)]">
              <Icon name={TILE_ICON[t.label] ?? "calendar"} className="size-4" />
            </span>
            <span className={`tabular text-[28px] leading-tight font-bold ${t.tone === "text-red-ink" && !t.value ? "text-ink" : t.tone}`}>{t.value === undefined ? "…" : t.value}</span>
            <span className="text-[13px] text-ink-soft">{t.label}</span>
          </div>
        ))}
      </section>

      {next ? (
        <div className="liquid relative grid gap-1 overflow-hidden rounded-[22px] p-4 ps-6 sm:p-5 sm:ps-7">
          <span aria-hidden className="absolute inset-y-4 start-0 w-1.5 rounded-e-full bg-gradient-to-b from-[#dcb98c] to-[#a87a48]" />
          <span aria-hidden className="pointer-events-none absolute -top-16 -end-10 size-48 rounded-full bg-gold/20 blur-3xl" />
          <span className="flex items-center gap-1.5 text-[13px] font-bold text-gold-ink"><Icon name="clock" className="size-4" />{fromNow(next, now) === "دلوقتي" ? "شغال دلوقتي" : "الميعاد الجاي"}</span>
          <span className="flex flex-wrap items-baseline gap-x-3">
            <span className="tabular text-[28px] leading-tight font-bold">{cairoTime(new Date(next.start))}</span>
            <span className="text-sm text-ink-soft">{cairoWhen(next.start)}{fromNow(next, now) !== "دلوقتي" ? ` · ${fromNow(next, now)}` : ""}</span>
          </span>
          <span className="font-bold">{next.name ?? "من غير اسم"}</span>
          <span className="text-sm text-ink-soft">
            {label(BOOKING_SERVICES, next.service)}{next.call_app ? ` (${label(CALL_APPS, next.call_app)})` : ""} · {lengthWords(minutesOf(next))} · <Phone wa={next.wa_id} />
          </span>
        </div>
      ) : null}

      {tomorrows.length > 0 ? (
        <p className={`liquid flex flex-wrap items-center gap-2 rounded-full px-4 py-2 text-sm ${unconfirmed > 0 ? "" : "text-green-ink"}`} role="status">
          <Icon name={unconfirmed > 0 ? "bell" : "check-circle"} className={`size-4 shrink-0 ${unconfirmed > 0 ? "text-orange-ink" : ""}`} />
          بكرة {tomorrows.length} {tomorrows.length === 1 ? "ميعاد" : "مواعيد"}
          {unconfirmed > 0 ? <>، <span className="font-semibold text-orange-ink">{unconfirmed} ما أكدوش حضورهم</span></> : "، وكلهم أكدوا حضورهم ✅"}
        </p>
      ) : null}

      {list.isPending ? <Loading /> : list.isError ? <ErrorState message={errorMessage(list.error)} onRetry={() => void list.refetch()} /> : days.length === 0 ? (
        <div className="liquid rounded-[22px] p-4 sm:p-5">
          {all.length > 0
            ? <Empty title="مفيش حجوزات بالشكل ده" icon="search">جرّب نوع تاني أو امسح البحث.</Empty>
            : <Empty title={when === "ahead" ? "مفيش حجوزات في الشهر الجاي" : "مفيش حجوزات في الأسبوعين اللي فاتوا"} icon="calendar" />}
        </div>
      ) : days.map((g) => {
        const live = g.items.filter(isLive);
        const held = g.items.filter((a) => a.state === "pending_payment").length;
        return (
          <section key={g.day} className="liquid relative grid gap-3 overflow-hidden rounded-[22px] p-3.5 sm:p-4">
            <span aria-hidden className="pointer-events-none absolute inset-x-6 top-0 h-px bg-gradient-to-l from-transparent via-gold/70 to-transparent" />
            <h3 className="flex flex-wrap items-center gap-x-3 gap-y-1 px-1 font-bold">
              <span aria-hidden className="grid size-8 place-items-center rounded-xl bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.5)]"><Icon name="calendar" className="size-4" /></span>
              {g.day === todayKey ? "النهارده" : g.day === tomorrowKey ? "بكرة" : ""} <span className="text-sm font-normal text-ink-soft">{longDay(g.day)}</span>
              <span className="ms-auto flex flex-wrap gap-1.5 text-xs font-normal">
                <span className="rounded-full border border-line/60 bg-card/60 px-2.5 py-0.5 text-ink-soft">{g.items.length} {g.items.length === 1 ? "ميعاد" : "مواعيد"}</span>
                {live.length ? <span className="rounded-full border border-line/60 bg-card/60 px-2.5 py-0.5 text-ink-soft">{lengthWords(live.reduce((s, a) => s + minutesOf(a), 0))} محجوزة</span> : null}
                {held ? <span className="rounded-full bg-orange-bg px-2.5 py-0.5 text-orange-ink">{held} مستني الإيصال</span> : null}
              </span>
            </h3>
            <ul className="grid gap-2">{g.items.map(row)}</ul>
          </section>
        );
      })}
      {mark.isError || cancel.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(mark.error ?? cancel.error)}</p> : null}
    </div>
  );
}

/** TASK 12 (B-18): what happened to each reminder of the appointment's current time. */
function Reminders({ items }: { items: Reminder[] }) {
  const word = (r: Reminder) =>
    r.state === "scheduled" && r.due_at ? `هيتبعت ${cairoWhen(r.due_at)}`
    : r.state === "sending" ? "بيتبعت دلوقتي"
    : r.state === "sent" ? `اتبعت ✓${r.channel === "template" ? " (قالب واتساب)" : ""}`
    : r.state === "failed" ? "ما وصلش ✗ (فيه مهمة تكلّمه)"
    : r.state === "skipped" ? `ما اتبعتش: ${label(REMINDER_SKIPS, r.skip_reason ?? "")}`
    : null;
  const shown = items.filter((r) => word(r) !== null);
  if (shown.length === 0) return null;
  return (
    <span className="flex flex-wrap items-center gap-x-1 text-xs text-ink-soft">
      <Icon name="bell" className="size-3.5 shrink-0" />
      {shown.map((r, i) => <span key={r.kind}>{i > 0 ? " · " : ""}{label(REMINDER_KINDS, r.kind)}: {word(r)}</span>)}
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
    onSuccess: () => { setOpen(false); void qc.invalidateQueries({ queryKey: ["services"] }); },
  });
  if (!open) return <Button variant="ghost" className="!min-h-9 !px-3" onClick={() => setOpen(true)}>مطلوب رأي مكتوب</Button>;
  return (
    <span className="flex w-full flex-wrap items-end gap-2 rounded-[14px] border border-gold-line bg-gold-tint/60 p-2.5">
      <Field label="يوم التسليم" type="date" min={cairoDate(new Date())} value={due} onChange={(e) => setDue(e.target.value)} />
      <Button variant="primary" busy={ask.isPending} onClick={() => ask.mutate()}>ابعت طلب الـ 2,000</Button>
      <Button onClick={() => setOpen(false)}>لأ</Button>
      {ask.isError ? <span className="text-sm text-bad" role="alert">{errorMessage(ask.error)}</span> : null}
      {ask.isSuccess ? <span className="text-sm" role="status">اتبعت. الطلب في «طلبات الخدمات».</span> : null}
    </span>
  );
}

// ---------- the default week ----------

function Week() {
  const qc = useQueryClient();
  const week = useQuery({ queryKey: ["booking", "week"], queryFn: booking.week });
  const [draft, setDraft] = useState<WeekDay[] | null>(null);
  const [affected, setAffected] = useState<Appointment[] | null>(null);
  const days = draft ?? week.data;
  const save = useMutation({
    mutationFn: (confirm: boolean) => booking.saveWeek(days!, confirm),
    onSuccess: () => { setDraft(null); setAffected(null); void qc.invalidateQueries({ queryKey: ["booking"] }); toast("المواعيد الثابتة اتحفظت ✅"); },
    onError: (e) => { if (e instanceof ApiError && e.code === "has_bookings") setAffected(e.data?.appointments as Appointment[]); },
  });
  if (week.isPending) return <Loading />;
  if (week.isError || !days) return <ErrorState message={errorMessage(week.error)} onRetry={() => void week.refetch()} />;
  const edit = (w: number, ranges: WeekDay["ranges"]) => setDraft(days.map((d) => (d.weekday === w ? { ...d, ranges } : d)));
  // idea from PR #14: the hours of each day and of the week, so the full and the light days show at a glance
  const minutesIn = (d: WeekDay) => d.ranges.reduce((s, r) => s + Math.max(0, r.end - r.start), 0);
  const total = days.reduce((s, d) => s + minutesIn(d), 0);
  const working = days.filter((d) => d.ranges.length > 0).length;
  const chip = "rounded-full border border-gold-line bg-gold-tint px-3 py-1 text-gold-ink";

  return (
    <div className="grid gap-4">
      <Sheet icon="clock" title="ساعات الحجز كل أسبوع"
        hint="دي المواعيد اللي حازم بيحجز فيها كل أسبوع. اليوم من غير فترات = إجازة. أي يوم معين بيتغيّر من «التقويم»."
        aside={
          <p className="flex flex-wrap gap-2 text-[13px]" role="status">
            <span className={chip}><strong className="tabular font-bold">{hoursWords(total)}</strong> في الأسبوع</span>
            <span className={chip}><strong className="tabular font-bold">{working}</strong> {working === 1 ? "يوم" : "أيام"} شغل</span>
          </p>
        }>
        {draft ? (
          <p role="status" className="flex items-center gap-2 rounded-[14px] border border-gold-line bg-gold-tint px-3.5 py-2 text-sm font-semibold text-gold-ink">
            <Icon name="alert-circle" className="size-4 shrink-0" />فيه تعديلات لسه ما اتحفظتش.
          </p>
        ) : null}
        <ul className="grid gap-2">
          {WEEK.map((w) => {
            const d = days.find((x) => x.weekday === w) ?? { weekday: w, ranges: [] };
            return (
              <li key={w} className={`flex flex-wrap items-center gap-2 py-2 ps-4 pe-2.5 ${ROW}`}>
                <span aria-hidden className={`absolute inset-y-2 start-0 w-1 rounded-e-full ${d.ranges.length ? "bg-gold" : "bg-ink-soft/40"}`} />
                <span className="w-20 font-bold">{DAY_NAMES[w]}</span>
                {d.ranges.length === 0 ? <Chip tone="grey">إجازة</Chip> : null}
                {d.ranges.map((r, i) => (
                  <Row key={i}>
                    <TimeSelect label={`بداية فترة ${i + 1} يوم ${DAY_NAMES[w]}`} value={r.start} onChange={(v) => edit(w, d.ranges.map((x, j) => (j === i ? { ...x, start: v } : x)))} />
                    –
                    <TimeSelect label={`نهاية فترة ${i + 1} يوم ${DAY_NAMES[w]}`} value={r.end} min={15} onChange={(v) => edit(w, d.ranges.map((x, j) => (j === i ? { ...x, end: v } : x)))} />
                    <Button variant="ghost" className="!min-h-9 !px-2" aria-label="شيل الفترة" onClick={() => edit(w, d.ranges.filter((_, j) => j !== i))}><Icon name="x" className="size-4" /></Button>
                  </Row>
                ))}
                <Button variant="ghost" className="!min-h-9 !px-3 text-gold-ink" onClick={() => edit(w, [...d.ranges, { start: d.ranges.at(-1)?.end ?? 600, end: 1440 }])}>+ فترة</Button>
                {d.ranges.length ? <span className="ms-auto tabular rounded-full bg-card/70 px-2.5 py-0.5 text-xs text-ink-soft">{hoursWords(minutesIn(d))}</span> : null}
              </li>
            );
          })}
        </ul>
        {affected ? <Confirm list={affected} busy={save.isPending} onYes={() => save.mutate(true)} onNo={() => setAffected(null)} /> : null}
        {save.isError && !affected ? <p className="text-sm text-bad" role="alert">{errorMessage(save.error)}</p> : null}
      </Sheet>
      <SaveBar>
        <Button variant="primary" busy={save.isPending && !affected} disabled={!draft} onClick={() => save.mutate(false)}>احفظ الأسبوع</Button>
        {draft ? <Button onClick={() => setDraft(null)}>ارجع زي ما كان</Button> : null}
      </SaveBar>
    </div>
  );
}

/** 480 → «8 ساعات»، 90 → «ساعة و30 دقيقة». */
function hoursWords(minutes: number): string {
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  const hours = h === 0 ? "" : h === 1 ? "ساعة" : h === 2 ? "ساعتين" : h <= 10 ? `${h} ساعات` : `${h} ساعة`;
  if (m === 0) return hours || "0 ساعة";
  return hours ? `${hours} و${m} دقيقة` : `${m} دقيقة`;
}

const Row = ({ children }: { children: ReactNode }) => (
  <span className="flex items-center gap-1 rounded-[12px] border border-line/60 bg-card/60 px-1.5 py-1 shadow-[inset_0_1px_0_rgb(255_255_255/0.4)]">{children}</span>
);

// ---------- the settings and the office's details ----------

function SettingsForm() {
  const qc = useQueryClient();
  const s = useQuery(settingsQuery);
  const [draft, setDraft] = useState<Settings | null>(null);
  const v = draft ?? s.data;
  const save = useMutation({ mutationFn: booking.saveSettings, onSuccess: () => { setDraft(null); void qc.invalidateQueries({ queryKey: ["booking"] }); toast("إعدادات الحجز اتحفظت ✅"); } });
  if (s.isPending) return <Loading />;
  if (s.isError || !v) return <ErrorState message={errorMessage(s.error)} onRetry={() => void s.refetch()} />;
  const num = (k: keyof Settings) => (e: { target: { value: string } }) => setDraft({ ...v, [k]: e.target.value === "" ? null : Number(e.target.value) });

  return (
    <div className="grid gap-4">
      <Sheet icon="whatsapp" title="الحجز من واتساب">
        <label className={`flex cursor-pointer items-center gap-3 px-4 py-3 font-semibold transition has-checked:border-gold-line has-checked:bg-gold-tint motion-reduce:transition-none ${ROW}`}>
          <input type="checkbox" className="size-4 accent-[#a87a48]" checked={v.enabled} onChange={(e) => setDraft({ ...v, enabled: e.target.checked })} />
          حازم يحجز من واتساب
        </label>
      </Sheet>
      <div className="grid items-start gap-4 2xl:grid-cols-2">
        <Sheet icon="calendar" title="المواعيد اللي حازم بيعرضها">
          <div className="grid gap-3 md:grid-cols-2">
            <Field label="الوقت بين كل ميعاد والتاني (دقيقة)" type="number" min={0} max={240} value={v.buffer_minutes} onChange={num("buffer_minutes")} />
            <Field label="أقل مدة قبل الحجز (ساعة)" hint="ما بتنطبقش على العاجل." type="number" min={0} max={168} value={v.min_notice_hours} onChange={num("min_notice_hours")} />
            <Field label="أبعد يوم ينفع يتحجز (يوم من النهارده)" type="number" min={1} max={180} value={v.horizon_days} onChange={num("horizon_days")} />
            <Field label="أقصى عدد مواعيد في اليوم" hint="فاضي = من غير حد." type="number" min={1} max={100} value={v.max_per_day ?? ""} onChange={num("max_per_day")} />
            <label className="grid gap-1.5 text-sm font-semibold">
              الميعاد بيبدأ كل
              <select className={`${SELECT} !min-h-11 px-3 font-normal`} value={v.slot_step_minutes} onChange={num("slot_step_minutes")}>
                {[5, 10, 15, 20, 30, 60].map((m) => <option key={m} value={m}>{m} دقيقة</option>)}
              </select>
            </label>
            <Field label="عدد المواعيد اللي حازم يعرضها مرة واحدة" type="number" min={1} max={9} value={v.offer_count} onChange={num("offer_count")} />
            <div className="md:col-span-2">
              <Field label="إيميل كالندر المستشار (Google)" dir="ltr" value={v.calendar_id} onChange={(e) => setDraft({ ...v, calendar_id: e.target.value })}
                hint={v.calendar_key ? "مفتاح Google موجود." : "مفتاح Google لسه مش متحط (صاحب المشروع)."} />
            </div>
          </div>
        </Sheet>
        <div className="grid gap-4">
          <Sheet icon="receipt" title="الحجز المؤقت لحد صورة التحويل"
            hint="العميل يختار ميعاد ← الميعاد يتمسك له لحد ساعة محددة. صورة التحويل توصل ← الحجز يتأكد. ما وصلتش ← الميعاد يتفك ويرجع فاضي.">
            <div className="grid gap-3 md:grid-cols-2">
              <Field label="مدة الحجز المؤقت (دقيقة)" type="number" min={15} max={1440} value={v.hold_minutes} onChange={num("hold_minutes")} />
              <Field label="المدة لو الميعاد قريب (دقيقة)" type="number" min={15} max={1440} value={v.hold_short_minutes} onChange={num("hold_short_minutes")} />
              <Field label="«قريب» يعني بعد أقل من (ساعة)" type="number" min={0} max={72} value={v.hold_short_within_hours} onChange={num("hold_short_within_hours")} />
              <Field label="الحجز المؤقت يخلص قبل الميعاد بـ (دقيقة)" type="number" min={0} max={240} value={v.hold_gap_minutes} onChange={num("hold_gap_minutes")} />
            </div>
          </Sheet>
          <Sheet icon="document" title="العرفي">
            <div className="grid gap-3 md:grid-cols-2">
              <Field label="مدة ميعاد العرفي في المكتب (دقيقة)" type="number" min={15} max={480} value={v.urf_minutes} onChange={num("urf_minutes")} />
            </div>
          </Sheet>
        </div>
      </div>
      {save.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(save.error)}</p> : null}
      <SaveBar dirty={!!draft}>
        <Button variant="primary" busy={save.isPending} disabled={!draft} onClick={() => save.mutate(v)}>احفظ</Button>
      </SaveBar>
    </div>
  );
}

function OfficeForm() {
  const qc = useQueryClient();
  const o = useQuery({ queryKey: ["booking", "office"], queryFn: booking.office });
  const [draft, setDraft] = useState<Office | null>(null);
  const v = draft ?? o.data;
  const save = useMutation({ mutationFn: booking.saveOffice, onSuccess: () => { setDraft(null); void qc.invalidateQueries({ queryKey: ["booking"] }); toast("بيانات المكتب اتحفظت ✅"); } });
  if (o.isPending) return <Loading />;
  if (o.isError || !v) return <ErrorState message={errorMessage(o.error)} onRetry={() => void o.refetch()} />;
  const set = (k: keyof Office) => (e: { target: { value: string } }) => setDraft({ ...v, [k]: e.target.value });
  // 1.33 (B-09): a WhatsApp number as the API takes it (digits, spaces, dashes, a leading +; 8 to 15 digits), or empty.
  const badNumber = (raw: string | null) => {
    const t = (raw ?? "").trim();
    const digits = t.replace(/[^0-9]/g, "").length;
    return t !== "" && (!/^\+?[0-9 -]{6,20}$/.test(t) || digits < 8 || digits > 15) ? "رقم واتساب مش صحيح (مثلًا 01xxxxxxxxx)" : undefined;
  };
  const lawyerBad = badNumber(v.lawyer_whatsapp);
  const managerBad = badNumber(v.manager_whatsapp);

  return (
    <div className="grid gap-4">
      <div className="grid items-start gap-4 2xl:grid-cols-2">
        <Sheet icon="receipt" title="فين العميل يحوّل"
          hint="حازم بيكتب البيانات دي في رسالة الحجز المؤقت (فين يحوّل). لو InstaPay أو المحفظة فاضيين، الميعاد بيتمسك للعميل بس الرسالة مش بتتبعت وبتتعمل مهمة.">
          <div className="grid gap-3 md:grid-cols-2">
            <Field label="عنوان InstaPay" dir="ltr" value={v.instapay} onChange={set("instapay")} />
            <Field label="رقم المحفظة" dir="ltr" inputMode="tel" value={v.wallet_number} onChange={set("wallet_number")} />
            <Field label="نوع المحفظة" hint="مثلًا فودافون كاش" value={v.wallet_provider} onChange={set("wallet_provider")} />
          </div>
        </Sheet>
        <Sheet icon="office" title="المكتب والمقابلة"
          hint="لينك Google Meet: لو فاضي، حازم مش بيعرض Meet على العميل. والعنوان والخريطة ورقم التليفون بيتكتبوا في تأكيد المقابلة في المكتب بعد مراجعة التحويل.">
          <div className="grid gap-3 md:grid-cols-2">
            <Field label="عنوان المكتب" value={v.address} onChange={set("address")} />
            <Field label="لينك الخريطة" dir="ltr" placeholder="https://maps.app.goo.gl/…" value={v.map_url} onChange={set("map_url")} />
            <Field label="لينك Google Meet بتاع المستشار" dir="ltr" placeholder="https://meet.google.com/…" value={v.meet_link ?? ""} onChange={set("meet_link")} />
            <Field label="رقم تليفون المكتب" dir="ltr" inputMode="tel" value={v.phone ?? ""} onChange={set("phone")} />
          </div>
        </Sheet>
      </div>
      <Sheet icon="bell" title="تنبيه الاستشارة العاجلة"
        hint="الاستشارة العاجلة: بعد صورة التحويل، المستشار بيوصله تنبيه واتساب على رقمه بزرار «شوف التحويل» (صورة التحويل، وبعدها «التحويل تمام، هكلمه» أو «فيه مشكلة»). لو ما ردّش في 10 دقايق، التنبيه بيروح لرقم المدير. لو رقم المستشار فاضي، التنبيه في اللوحة بس.">
        <div className="grid gap-3 md:grid-cols-2">
          <Field label="رقم واتساب المستشار (تنبيه الاستشارة العاجلة)" dir="ltr" inputMode="tel" placeholder="01xxxxxxxxx" value={v.lawyer_whatsapp ?? ""} error={lawyerBad} onChange={set("lawyer_whatsapp")} />
          <Field label="رقم واتساب المدير (لو المستشار ما ردّش)" dir="ltr" inputMode="tel" placeholder="01xxxxxxxxx" value={v.manager_whatsapp ?? ""} error={managerBad} onChange={set("manager_whatsapp")} />
        </div>
      </Sheet>
      {save.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(save.error)}</p> : null}
      <SaveBar dirty={!!draft}>
        <Button variant="primary" busy={save.isPending} disabled={!draft || !!lawyerBad || !!managerBad} onClick={() => save.mutate(v)}>احفظ</Button>
      </SaveBar>
    </div>
  );
}
