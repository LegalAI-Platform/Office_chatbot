import { useState } from "react";
import { Link } from "react-router";
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
import { Confirm, Dialog, Glow, longDay, monthQuery, ROW, Segmented, SELECT, settingsQuery, STATUS, TimeSelect, TitleTile, Told, WEEK } from "./parts";

// the calendar's column heads: short, so all seven fit a phone whole
const SHORT_DAYS = ["أحد", "اتنين", "تلات", "أربع", "خميس", "جمعة", "سبت"];
// the word a day's state writes in its cell (an open day writes nothing), written out in full so Tailwind keeps them
const CELL_WORD: Record<Day["status"], string> = {
  open: "", weekly_off: "text-grey-ink", closed: "text-red-ink", custom_hours: "text-blue-ink", partly_closed: "text-orange-ink",
};
// the line at a booking's start, by the tone of its state
const BAR: Record<string, string> = { green: "bg-green", grey: "bg-ink-soft/40", blue: "bg-blue", orange: "bg-orange", red: "bg-red" };
const STATE_TONE: Record<string, string> = {
  pending_payment: "orange", expired: "grey", confirmed: "blue", completed: "green", no_show: "orange", cancelled_by_client: "grey", cancelled_by_office: "grey",
};
/** «حجز واحد»، «حجزين»، «3 حجوزات»، «12 حجز». */
const bookingsWords = (n: number) => (n === 1 ? "حجز واحد" : n === 2 ? "حجزين" : n <= 10 ? `${n} حجوزات` : `${n} حجز`);

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
  { value: "day", label: "اليوم ده", icon: "calendar" },
  { value: "all", label: "كل الجاي", icon: "clients" },
];

/**
 * «المواعيد» (TASK 08, DECISIONS v1.12 B-24): the office's daily screen. The month on the start side, the picked day's
 * bookings (today when the page opens) on the other; «تعديل اليوم ده» changes that one day in a small window, and
 * what is set once (the week, the booking settings, the office's details) lives in «إعدادات المواعيد».
 */
export function BookingPage() {
  const { data: me } = useMe();
  // opens on today (idea from PR #14): the first question every morning is «who is coming today»
  const today = cairoDate(new Date());
  const [picked, setPicked] = useState(today);
  const [view, setView] = useUrlState<View>("show", "day", ["day", "all"]);
  const mayChange = can(me, "schedule.manage");

  return (
    <div className="relative isolate grid gap-5 pb-4">
      <Glow />
      <header className="grid items-center gap-3 lg:grid-cols-[auto_minmax(0,1fr)_auto]">
        <TitleTile icon="calendar">المواعيد</TitleTile>
        <Status />
        {mayChange || can(me, "office.manage") ? (
          <Link to="/appointments/settings"
            className="flex min-h-10 w-fit items-center gap-2 rounded-full border border-line/60 bg-card/60 px-4 text-[14px] font-semibold text-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.45)] transition hover:border-gold-line hover:bg-gold-tint hover:text-gold-ink motion-reduce:transition-none">
            <Icon name="settings" className="size-[18px] text-gold-ink" />إعدادات المواعيد
          </Link>
        ) : null}
      </header>

      <div className="grid items-start gap-4 xl:grid-cols-[minmax(0,0.95fr)_minmax(0,1.05fr)]">
        <Calendar today={today} picked={view === "day" ? picked : ""} onPick={(d) => { setPicked(d); setView("day"); }} />
        <div className="grid min-w-0 content-start gap-3">
          <Segmented label="العرض" value={view} onChange={setView} options={VIEWS} />
          {view === "day" ? <DayPanel key={picked} day={picked} today={today} mayChange={mayChange} /> : <Bookings />}
        </div>
      </div>
    </div>
  );
}

/** Whether Hazem books from WhatsApp now, and what is missing if not. */
function Status() {
  const s = useQuery(settingsQuery);
  if (!s.data) return <span />;
  const missing = [
    !s.data.calendar_key && "مفتاح Google Calendar (صاحب المشروع بيحطه في .env)",
    !s.data.calendar_id && "إيميل كالندر المستشار (في «الحجز من واتساب»)",
    !s.data.texts_approved && "اعتماد نصوص الحجز (من «نصوص حازم»)",
  ].filter(Boolean);
  const ok = s.data.enabled && missing.length === 0;
  return (
    <p role="status" className={`flex w-fit items-start gap-2 rounded-[16px] border px-3.5 py-2 text-[13px] leading-relaxed lg:justify-self-center ${ok ? "border-green/30 bg-green-bg text-green-ink" : "border-orange/30 bg-orange-bg text-orange-ink"}`}>
      <Icon name={ok ? "check-circle" : "alert-circle"} className="mt-0.5 size-4 shrink-0" />
      <span>
        {s.data.enabled ? "حازم بيحجز من واتساب." : "الحجز من واتساب مقفول: حازم بيعرض الاستشارة من غير مواعيد."}
        {missing.length > 0 ? ` ناقص: ${missing.join("، ")}.` : ""}
      </span>
    </p>
  );
}

// ---------- the month ----------

function Calendar({ today, picked, onPick }: { today: string; picked: string; onPick: (day: string) => void }) {
  const [month, setMonth] = useState(() => monthOf(picked || today));
  const days = useQuery(monthQuery(month));
  const first = days.data?.[0];
  const lead = first ? WEEK.indexOf(new Date(`${first.day}T12:00:00Z`).getUTCDay()) : 0;
  const nav = "grid size-10 place-items-center rounded-full border border-line/60 bg-card/60 text-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.45)] transition hover:border-gold-line hover:bg-gold-tint hover:text-gold-ink motion-reduce:transition-none";

  return (
    <section className="liquid relative grid min-w-0 gap-3 overflow-hidden rounded-[22px] p-3 sm:p-4 xl:sticky xl:top-4">
      <span aria-hidden className="pointer-events-none absolute inset-x-6 top-0 h-px bg-gradient-to-l from-transparent via-gold/70 to-transparent" />
      <div className="flex items-center justify-between gap-2">
        <button type="button" className={nav} aria-label="الشهر اللي فات" onClick={() => setMonth(shiftMonth(month, -1))}><Icon name="chevron-start" className="size-5" /></button>
        <h2 className="flex flex-wrap items-center justify-center gap-2 text-center text-[17px] font-bold tabular">
          {new Intl.DateTimeFormat("ar-EG-u-nu-latn", { month: "long", year: "numeric", timeZone: "UTC" }).format(new Date(`${month}-15T00:00:00Z`))}
          {month !== monthOf(today) || picked !== today ? (
            <button type="button" className="rounded-full border border-gold-line bg-gold-tint px-3 py-0.5 text-[12.5px] font-bold text-gold-ink transition hover:brightness-105" onClick={() => { setMonth(monthOf(today)); onPick(today); }}>النهارده</button>
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
            const isToday = d.day === today;
            const off = d.status === "closed" || d.status === "weekly_off";
            return (
              <button key={d.day} type="button" role="gridcell" aria-selected={on} onClick={() => onPick(d.day)}
                aria-label={`${d.day}: ${STATUS[d.status].word}${d.bookings ? `، ${bookingsWords(d.bookings)}` : ""}`}
                aria-current={isToday ? "date" : undefined}
                className={`grid min-h-[3.75rem] min-w-0 cursor-pointer content-start justify-items-center gap-0.5 rounded-[12px] border px-0.5 py-1 text-sm transition motion-reduce:transition-none sm:min-h-[4.5rem] ${on
                  ? "border-gold bg-gold-tint ring-2 ring-gold/60 shadow-[inset_0_1px_0_rgb(255_255_255/0.5),0_6px_14px_-8px_rgb(140_98_58/0.7)]"
                  : off
                    ? "border-line/50 bg-grey-bg/60 hover:border-gold-line"
                    : `${isToday ? "border-gold-line" : "border-line/60"} bg-card/55 hover:-translate-y-px hover:border-gold-line hover:bg-card/90 motion-reduce:hover:translate-y-0`}`}>
                <span className={`tabular font-semibold ${isToday ? "rounded-full bg-gradient-to-b from-[#dcb98c] to-[#a87a48] px-1.5 text-[#2a2117]" : off ? "text-ink-soft" : ""}`}>{Number(d.day.slice(8))}</span>
                {d.status !== "open" ? <span className={`max-w-full text-[10px] leading-tight font-semibold sm:text-[11px] ${CELL_WORD[d.status]}`}>{STATUS[d.status].word}</span> : null}
                {d.bookings ? <span className="max-w-full text-[10px] leading-tight font-bold text-gold-ink sm:text-[11px]">{bookingsWords(d.bookings)}</span> : null}
              </button>
            );
          })}
        </div>
      )}
    </section>
  );
}

// ---------- one day ----------

/** The picked day: its name and hours, «تعديل اليوم ده», and its bookings as cards. */
function DayPanel({ day, today, mayChange }: { day: string; today: string; mayChange: boolean }) {
  const month = useQuery(monthQuery(monthOf(day)));
  const info = month.data?.find((d) => d.day === day);
  const list = useQuery(dayQuery(day));
  const [editing, setEditing] = useState(false);
  const [done, setDone] = useState<Done | null>(null);
  const items = (list.data ?? []).slice().sort((a, b) => a.start.localeCompare(b.start));

  return (
    <section className="liquid relative grid min-w-0 content-start gap-3 overflow-hidden rounded-[22px] p-3.5 sm:p-4">
      <span aria-hidden className="pointer-events-none absolute inset-x-6 top-0 h-px bg-gradient-to-l from-transparent via-gold/70 to-transparent" />
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
        <div className="grid min-w-0 flex-1 basis-56 gap-1">
          <h2 className="flex flex-wrap items-center gap-2 text-[18px] leading-snug font-bold">
            {day === today ? "النهارده · " : ""}{longDay(day)}
            {info && info.status !== "open" ? <Chip tone={STATUS[info.status].tone}>{STATUS[info.status].word}</Chip> : null}
          </h2>
          {info ? (
            <p className="flex items-center gap-1.5 text-[13px] text-ink-soft">
              <Icon name="clock" className="size-4 shrink-0 text-gold-ink" />
              <span className="tabular">{info.hours.length === 0 ? "مفيش حجز اليوم ده" : info.hours.map((h) => `${hhmm(h.start)}–${hhmm(h.end)}`).join("، ")}</span>
            </p>
          ) : null}
        </div>
        {mayChange && info ? (
          <Button variant="primary" className="!min-h-10" onClick={() => setEditing(true)}><Icon name="calendar" className="size-4" />تعديل اليوم ده</Button>
        ) : null}
      </div>
      {done && done.cancelled > 0 ? <Told n={done.cancelled} /> : null}

      <section aria-label="حجوزات اليوم" className="grid gap-2">
        {list.isPending ? <Loading /> : list.isError ? <ErrorState message={errorMessage(list.error)} onRetry={() => void list.refetch()} /> : items.length === 0 ? (
          <Empty title="مفيش حجوزات اليوم ده" icon="calendar" />
        ) : (
          <ul className="grid gap-2">{items.map((a) => <BookingCard key={a.id} a={a} now={new Date()} />)}</ul>
        )}
      </section>

      {editing && info ? <DayDialog day={info} isToday={day === today} onClose={() => setEditing(false)} onDone={setDone} /> : null}
    </section>
  );
}

const KINDS = [
  ["closed_day", "اقفل اليوم كله"],
  ["closed_hours", "اقفل جزء منه"],
  ["custom_hours", "ساعات مختلفة لليوم ده"],
] as const;

/** «تعديل اليوم ده»: the day's changes, closing it (asks first if it has bookings), other hours, and the preview. */
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
    onSuccess: (r) => { finished(r); setReason(""); toast("اليوم اتعدّل ✅"); onClose(); },
    onError: (e) => asked(e, () => add.mutate(true)),
  });
  const remove = useMutation({
    mutationFn: ({ id, confirm }: { id: string; confirm: boolean }) => booking.removeException(id, confirm),
    onSuccess: finished,
    onError: (e, v) => asked(e, () => remove.mutate({ id: v.id, confirm: true })),
  });

  return (
    <Dialog title={<>تعديل {isToday ? "النهارده · " : ""}{longDay(day.day)}</>} onClose={onClose}>
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

      <fieldset className="grid gap-2">
        <legend className="sr-only">غيّر اليوم ده</legend>
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
        <div className="flex flex-wrap gap-2">
          <Button variant="primary" busy={add.isPending && !ask} onClick={() => add.mutate(false)}>احفظ</Button>
          <Button onClick={onClose}>إلغاء</Button>
        </div>
      </fieldset>
      {ask ? (
        <Confirm list={ask.list} busy={add.isPending || remove.isPending} onYes={ask.again} onNo={() => setAsk(null)} />
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

// ---------- one booking ----------

const DAY_MS = 86_400_000;
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

/** One booking as a card: the time, the client, the kind, the state, what to look at, and what the office can do. */
function BookingCard({ a, now }: { a: Appointment; now: Date }) {
  const qc = useQueryClient();
  const { data: me } = useMe();
  const refresh = () => void qc.invalidateQueries({ queryKey: ["booking"] });
  const mark = useMutation({
    mutationFn: (state: string) => booking.mark(a.id, state),
    onSuccess: (_, state) => { refresh(); toast(state === "completed" ? "اتعلّم إن الميعاد خلص ✅" : "اتعلّم إن العميل ماحضرش"); },
  });
  const cancel = useMutation({ mutationFn: () => booking.cancel(a.id), onSuccess: () => { refresh(); toast("الحجز اتلغى، وحازم هيبلّغ العميل"); } });
  const notes = notesOf(a, now);

  return (
    <li className={`flex flex-wrap items-start gap-3 py-3 ps-4 pe-3 transition hover:bg-card/80 motion-reduce:transition-none ${ROW}`}>
      <span aria-hidden className={`absolute inset-y-3 start-0 w-1 rounded-e-full ${BAR[STATE_TONE[a.state] ?? "grey"]}`} />
      <span className="tabular grid min-w-[4.5rem] shrink-0 place-items-center rounded-[12px] border border-line/60 bg-card/70 px-2 py-2 text-[17px] font-bold shadow-[inset_0_1px_0_rgb(255_255_255/0.45)]">
        {cairoTime(new Date(a.start))}
      </span>
      <span className="grid min-w-0 flex-1 basis-52 gap-1">
        <span className="flex flex-wrap items-baseline gap-x-2">
          <span className="text-[15.5px] font-bold">{a.name ?? "من غير اسم"}</span>
          <span className="text-xs font-normal text-ink-soft">{lengthWords(minutesOf(a))}{isLive(a) ? ` · ${fromNow(a, now)}` : ""}</span>
        </span>
        <span className="text-[13px] text-ink-soft">
          {label(BOOKING_SERVICES, a.service)}{a.call_app ? ` (${label(CALL_APPS, a.call_app)})` : ""} · {matterLabel(a.matter_type ?? undefined)} · <Phone wa={a.wa_id} />{a.reschedules ? " · اتغيّر مرة" : ""}
          {a.payment && a.state !== "pending_payment" ? ` · الدفع: ${label(PAYMENT_STATES, a.payment)}` : ""}
        </span>
        {a.state === "confirmed" && a.reminders.length > 0 ? <Reminders items={a.reminders} /> : null}
        {notes.length > 0 ? (
          <span className="flex flex-wrap gap-1.5 pt-0.5">
            {notes.map((n) => (
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
        <span className="flex w-full flex-wrap justify-end gap-1">
          {a.state === "confirmed" ? (
            <>
              <Button variant="ghost" className="!min-h-9 !px-3" busy={mark.isPending && mark.variables === "completed"} onClick={() => mark.mutate("completed")}>خلص</Button>
              <Button variant="ghost" className="!min-h-9 !px-3" busy={mark.isPending && mark.variables === "no_show"} onClick={() => mark.mutate("no_show")}>ماحضرش</Button>
            </>
          ) : null}
          <Button variant="danger" className="!min-h-9 !px-3" busy={cancel.isPending}
            onClick={async () => {
              if (await confirmAction({ title: "تلغي الحجز ده؟", body: "حازم هيبعت للعميل يختار ميعاد تاني، أو استرداد كامل لو كان دفع.", yes: "أيوه، الغيه", danger: true })) cancel.mutate();
            }}>
            إلغاء
          </Button>
        </span>
      ) : null}
      {mark.isError || cancel.isError ? <p className="w-full text-sm text-bad" role="alert">{errorMessage(mark.error ?? cancel.error)}</p> : null}
    </li>
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

// ---------- كل الجاي: the appointments ahead (or past) ----------

type Kind = "all" | "phone" | "office" | "urgent" | "urf";
const KIND_TEST: Record<Kind, (a: Appointment) => boolean> = {
  all: () => true,
  phone: (a) => a.service === "consultation_phone_video",
  office: (a) => a.service === "consultation_office",
  urgent: (a) => a.service === "consultation_urgent",
  urf: (a) => a.service.startsWith("urf_"),
};
const WHENS: { value: "ahead" | "past"; label: string; icon: IconName }[] = [
  { value: "ahead", label: "الجاية (30 يوم)", icon: "forward" },
  { value: "past", label: "اللي فاتت (أسبوعين)", icon: "back" },
];
const KIND_ICON: Record<Kind, IconName> = { all: "calendar", phone: "video", office: "office", urgent: "alert", urf: "document" };

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

  return (
    <div className="grid gap-3">
      <section aria-label="فلاتر الحجوزات" className="liquid grid gap-2.5 rounded-[22px] p-3">
        <div className="grid gap-2.5 md:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
          <Segmented label="أنهي حجوزات" value={when} onChange={setWhen} options={WHENS} small />
          <label className="relative w-full">
            <span className="sr-only">دوّر بالاسم أو الرقم</span>
            <Icon name="search" className="pointer-events-none absolute start-3 top-1/2 size-4 -translate-y-1/2 text-ink-soft" />
            <input type="search" value={q} onChange={(e) => setQ(e.target.value)} placeholder="دوّر بالاسم أو الرقم"
              className="min-h-10 w-full rounded-full border border-line/70 bg-card/60 ps-9 pe-3 text-sm text-ink shadow-[inset_0_1px_2px_rgb(0_0_0/0.06)] focus:border-gold" />
          </label>
        </div>
        <Segmented label="نوع الميعاد" value={kind} onChange={setKind} options={kinds} small />
      </section>

      <section aria-label="ملخص الحجوزات" className="grid grid-cols-2 gap-2 sm:grid-cols-4">
        {tiles.map((t) => (
          <div key={t.label} className="liquid grid content-start gap-0 rounded-[16px] px-3 py-2.5">
            <span className={`tabular text-[22px] leading-tight font-bold ${t.tone === "text-red-ink" && !t.value ? "text-ink" : t.tone}`}>{t.value === undefined ? "…" : t.value}</span>
            <span className="text-[12px] leading-snug text-ink-soft">{t.label}</span>
          </div>
        ))}
      </section>

      {next ? (
        <div className="liquid relative flex flex-wrap items-center gap-x-4 gap-y-1 overflow-hidden rounded-[18px] px-4 py-3 ps-6">
          <span aria-hidden className="absolute inset-y-3 start-0 w-1.5 rounded-e-full bg-gradient-to-b from-[#dcb98c] to-[#a87a48]" />
          <span className="flex items-center gap-1.5 text-[13px] font-bold text-gold-ink"><Icon name="clock" className="size-4" />{fromNow(next, now) === "دلوقتي" ? "شغال دلوقتي" : "الميعاد الجاي"}</span>
          <span className="tabular text-[20px] leading-tight font-bold">{cairoTime(new Date(next.start))}</span>
          <span className="grid min-w-0 flex-1 basis-48">
            <span className="font-bold">{next.name ?? "من غير اسم"}</span>
            <span className="text-xs text-ink-soft">
              {cairoWhen(next.start)}{fromNow(next, now) !== "دلوقتي" ? ` · ${fromNow(next, now)}` : ""} · {label(BOOKING_SERVICES, next.service)}{next.call_app ? ` (${label(CALL_APPS, next.call_app)})` : ""} · <Phone wa={next.wa_id} />
            </span>
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
          <section key={g.day} className="liquid relative grid gap-2.5 overflow-hidden rounded-[22px] p-3 sm:p-3.5">
            <span aria-hidden className="pointer-events-none absolute inset-x-6 top-0 h-px bg-gradient-to-l from-transparent via-gold/70 to-transparent" />
            <h3 className="flex flex-wrap items-center gap-x-3 gap-y-1 px-1 font-bold">
              {g.day === todayKey ? "النهارده" : g.day === tomorrowKey ? "بكرة" : ""} <span className="text-sm font-normal text-ink-soft">{longDay(g.day)}</span>
              <span className="ms-auto flex flex-wrap gap-1.5 text-xs font-normal">
                <span className="rounded-full border border-line/60 bg-card/60 px-2.5 py-0.5 text-ink-soft">{g.items.length} {g.items.length === 1 ? "ميعاد" : "مواعيد"}</span>
                {live.length ? <span className="rounded-full border border-line/60 bg-card/60 px-2.5 py-0.5 text-ink-soft">{lengthWords(live.reduce((s, a) => s + minutesOf(a), 0))} محجوزة</span> : null}
                {held ? <span className="rounded-full bg-orange-bg px-2.5 py-0.5 text-orange-ink">{held} مستني الإيصال</span> : null}
              </span>
            </h3>
            <ul className="grid gap-2">{g.items.map((a) => <BookingCard key={a.id} a={a} now={now} />)}</ul>
          </section>
        );
      })}
    </div>
  );
}
