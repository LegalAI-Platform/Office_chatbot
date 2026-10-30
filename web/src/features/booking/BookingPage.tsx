import { useState, type ReactNode } from "react";
import { queryOptions, useMutation, useQuery, useQueryClient, type QueryClient } from "@tanstack/react-query";
import { ApiError, errorMessage } from "../../shared/api/client";
import { can, useMe } from "../../shared/api/session";
import { APPOINTMENT_STATES, BOOKING_SERVICES, CALL_APPS, label, matterLabel, PAYMENT_STATES, REMINDER_KINDS, REMINDER_SKIPS } from "../../shared/lib/labels";
import { cairoTime, cairoWhen } from "../../shared/lib/time";
import { useUrlState } from "../../shared/lib/url";
import { Phone } from "../../shared/ui/caseCard";
import { Icon } from "../../shared/ui/Icon";
import { confirmAction, toast } from "../../shared/ui/feedback";
import { Button, Card, Chip, Empty, ErrorState, Field, Loading } from "../../shared/ui/ui";
import { booking, cairoDate, hhmm, monthOf, shiftMonth, TIMES, type Appointment, type Day, type Done, type Office, type Reminder, type Settings, type WeekDay } from "./api";

type Tab = "calendar" | "bookings" | "week" | "settings" | "office";
// The office week starts on Saturday; the API's weekday is 0 = Sunday … 6 = Saturday.
const WEEK = [6, 0, 1, 2, 3, 4, 5];
const DAY_NAMES = ["الأحد", "الاتنين", "التلات", "الأربع", "الخميس", "الجمعة", "السبت"];
const STATUS: Record<Day["status"], { word: string; tone: string }> = {
  open: { word: "شغال", tone: "green" },
  weekly_off: { word: "إجازة", tone: "grey" },
  closed: { word: "مقفول", tone: "red" },
  custom_hours: { word: "ساعات مختلفة", tone: "blue" },
  partly_closed: { word: "جزء مقفول", tone: "orange" },
};
// Written out in full so Tailwind keeps them.
const DOT: Record<string, string> = { green: "bg-green-ink", grey: "bg-grey-ink", red: "bg-red-ink", blue: "bg-blue-ink", orange: "bg-orange-ink" };
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

/**
 * «المواعيد» (TASK 08, DECISIONS v1.12 B-24): the booking hours Hazem offers, changed from here and
 * seen by Hazem at once. The calendar of the month (close a day, close some hours, other hours that
 * day), the appointments, the default week, the settings and (manager / lawyer) the office's details.
 */
export function BookingPage() {
  const { data: me } = useMe();
  const tabs: [Tab, string][] = [["calendar", "التقويم"], ["bookings", "الحجوزات"], ["week", "المواعيد الثابتة"], ["settings", "إعدادات الحجز"]];
  if (can(me, "office.manage")) tabs.push(["office", "بيانات المكتب"]);
  const [tab, setTab] = useUrlState<Tab>("tab", "calendar", tabs.map((t) => t[0]));

  return (
    <div className="grid gap-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-2xl">المواعيد</h1>
        <Undo />
      </div>
      <Status />
      <div className="flex flex-wrap gap-2" role="tablist" aria-label="أقسام المواعيد">
        {tabs.map(([t, text]) => (
          <Button key={t} role="tab" aria-selected={t === tab} variant={t === tab ? "primary" : "secondary"} onClick={() => setTab(t)}>{text}</Button>
        ))}
      </div>
      {tab === "calendar" ? <Calendar /> : tab === "bookings" ? <Bookings /> : tab === "week" ? <Week /> : tab === "settings" ? <SettingsForm /> : <OfficeForm />}
    </div>
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
  return (
    <p role="status" className={`rounded-[var(--radius-card)] px-4 py-3 text-sm ${s.data.enabled && missing.length === 0 ? "bg-green-bg text-green-ink" : "bg-orange-bg text-orange-ink"}`}>
      {s.data.enabled ? "حازم بيحجز من واتساب." : "الحجز من واتساب مقفول: حازم بيعرض الاستشارة من غير مواعيد."}
      {missing.length > 0 ? ` ناقص: ${missing.join("، ")}.` : ""}
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
      <span className="flex items-center gap-2 text-sm text-ink-soft">
        آخر تعديل: {last.by ?? "—"}، {cairoWhen(last.at)}
        <Button variant="ghost" busy={undo.isPending && !affected} onClick={() => undo.mutate(false)}><Icon name="back" className="size-4" />تراجع</Button>
        {undo.isError && !affected ? <span className="text-bad" role="alert">{errorMessage(undo.error)}</span> : null}
      </span>
      {affected ? <Confirm list={affected} busy={undo.isPending} onYes={() => undo.mutate(true)} onNo={() => setAffected(null)} /> : null}
    </div>
  );
}

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

  return (
    <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_24rem]">
      <Card>
        <div className="flex items-center justify-between">
          <Button variant="ghost" aria-label="الشهر اللي فات" onClick={() => setMonth(shiftMonth(month, -1))}><Icon name="chevron-start" className="size-5" /></Button>
          <h2 className="flex items-center gap-2 text-lg tabular">
            {new Intl.DateTimeFormat("ar-EG-u-nu-latn", { month: "long", year: "numeric", timeZone: "UTC" }).format(new Date(`${month}-15T00:00:00Z`))}
            {month !== monthOf(today) || picked !== today ? (
              <Button variant="secondary" className="!min-h-8 !px-2.5 text-sm" onClick={() => { setMonth(monthOf(today)); setPicked(today); }}>النهارده</Button>
            ) : null}
          </h2>
          <Button variant="ghost" aria-label="الشهر الجاي" onClick={() => setMonth(shiftMonth(month, 1))}><Icon name="chevron-end" className="size-5" /></Button>
        </div>
        {days.isPending ? <Loading /> : days.isError ? <ErrorState message={errorMessage(days.error)} onRetry={() => void days.refetch()} /> : (
          <div className="grid grid-cols-7 gap-1 text-center" role="grid" aria-label="أيام الشهر">
            {WEEK.map((w) => <span key={w} className="py-1 text-xs font-semibold text-ink-soft">{DAY_NAMES[w]!.slice(0, 3)}</span>)}
            {Array.from({ length: lead }, (_, i) => <span key={`x${i}`} />)}
            {days.data.map((d) => (
              <button key={d.day} type="button" role="gridcell" aria-selected={d.day === picked} onClick={() => setPicked(d.day)}
                aria-label={`${d.day}: ${STATUS[d.status].word}${d.bookings ? `، ${d.bookings} حجز` : ""}`}
                aria-current={d.day === today ? "date" : undefined}
                className={`grid min-h-14 content-start justify-items-center gap-0.5 rounded-lg border p-1 text-sm ${d.day === picked ? "border-gold bg-gold-tint" : "border-line hover:border-gold-line"} ${d.status === "closed" || d.status === "weekly_off" ? "opacity-60" : ""}`}>
                <span className={`tabular font-semibold ${d.day === today ? "rounded-full bg-gold px-1.5 text-white" : ""}`}>{Number(d.day.slice(8))}</span>
                <span className={`size-2 rounded-full ${DOT[STATUS[d.status].tone]}`} aria-hidden />
                {d.bookings ? <span className="tabular text-[11px] text-ink-soft">{d.bookings} حجز</span> : null}
              </button>
            ))}
          </div>
        )}
        <ul className="flex flex-wrap gap-2 text-xs" aria-label="مفتاح الألوان">
          {Object.values(STATUS).map((s) => <li key={s.word}><Chip tone={s.tone}>{s.word}</Chip></li>)}
        </ul>
      </Card>
      {/* keyed by the date: nothing typed or asked about one day carries over to another */}
      {day ? <DaySheet key={day.day} day={day} isToday={day.day === today} /> : <Card><Empty title="اختار يوم" icon="calendar">تقفله، أو تقفل جزء منه، أو تديله ساعات مختلفة، وتشوف حازم هيعرض إيه.</Empty></Card>}
    </div>
  );
}

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
    <Card>
      <h2 className="flex flex-wrap items-center gap-2 text-lg">{isToday ? "النهارده · " : ""}{longDay(day.day)} <Chip tone={STATUS[day.status].tone}>{STATUS[day.status].word}</Chip></h2>
      {day.bookings ? <DayBookings day={day.day} /> : null}
      <p className="text-sm">
        {day.hours.length === 0 ? "مفيش حجز اليوم ده." : `حازم بيحجز: ${day.hours.map((h) => `${hhmm(h.start)}–${hhmm(h.end)}`).join("، ")}`}
      </p>
      {day.exceptions.length > 0 ? (
        <ul className="grid gap-1.5">
          {day.exceptions.map((e) => (
            <li key={e.id} className="flex flex-wrap items-center gap-2 rounded-lg bg-card-2 px-3 py-2 text-sm">
              <span className="flex-1">
                {e.kind === "closed_day" ? "اليوم مقفول" : `${e.kind === "closed_hours" ? "مقفول" : "ساعات"} ${hhmm(e.start!)}–${hhmm(e.end!)}`}
                {e.reason ? ` · ${e.reason}` : ""}{e.by ? ` · ${e.by}` : ""}
              </span>
              <Button variant="ghost" busy={remove.isPending && remove.variables?.id === e.id} onClick={() => remove.mutate({ id: e.id, confirm: false })}><Icon name="x" className="size-4" />شيل</Button>
            </li>
          ))}
        </ul>
      ) : null}

      <fieldset className="grid gap-2">
        <legend className="mb-1 text-sm font-semibold">غيّر اليوم ده</legend>
        {([["closed_day", "اقفل اليوم كله"], ["closed_hours", "اقفل جزء منه"], ["custom_hours", "ساعات مختلفة لليوم ده"]] as const).map(([k, t]) => (
          <label key={k} className="flex items-center gap-2 text-sm"><input type="radio" name="kind" checked={kind === k} onChange={() => setKind(k)} />{t}</label>
        ))}
        {kind !== "closed_day" ? (
          <div className="flex items-center gap-2 text-sm">
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
    </Card>
  );
}

/** The clients are told by Hazem within seconds (the worker); one he cannot reach becomes a task. */
const Told = ({ n }: { n: number }) => (
  <p className="text-sm" role="status">اتلغى {n} حجز، وحازم هيبلّغ العملاء خلال ثواني (اللي ماتوصلوش الرسالة بتتعمله مهمة).</p>
);

/** «الحجوزات دي هتتلغي»: the office cancels and Hazem tells each client (B-22: another time or a full refund). */
function Confirm({ list, busy, onYes, onNo }: { list: Appointment[]; busy: boolean; onYes: () => void; onNo: () => void }) {
  return (
    <div className="grid gap-2 rounded-lg border border-orange-ink/40 bg-orange-bg p-3 text-sm" role="alertdialog" aria-label="فيه حجوزات">
      <p className="font-semibold text-orange-ink">التعديل ده فيه {list.length} حجز (مؤكد أو مستني الإيصال). لو كمّلت، الحجوزات دي هتتلغي وحازم هيبعت لكل عميل يختار ميعاد تاني، أو استرداد كامل لو كان دفع.</p>
      <ul>{list.map((a) => <li key={a.id}>{cairoWhen(a.start)} · {a.name ?? "من غير اسم"} · {label(BOOKING_SERVICES, a.service)}</li>)}</ul>
      <div className="flex gap-2">
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
    <section aria-label="حجوزات اليوم" className="grid gap-1.5 border-t border-line pt-3">
      <h3 className="font-semibold">الحجوزات ({items.length})</h3>
      <ul className="grid divide-y divide-line">
        {items.map((a) => (
          <li key={a.id} className="flex flex-wrap items-center gap-x-3 gap-y-1 py-2 text-sm">
            <span className="tabular font-semibold">{cairoTime(new Date(a.start))}</span>
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
    <div className="grid gap-2 border-t border-line pt-3">
      <div className="flex flex-wrap items-center gap-2">
        <h3 className="font-semibold">حازم هيعرض إيه</h3>
        <select aria-label="الخدمة" className="surface min-h-9 rounded-[var(--radius-control)] px-2 text-sm" value={service} onChange={(e) => setService(e.target.value)}>
          <option value="pv">{BOOKING_SERVICES.consultation_phone_video}</option>
          <option value="of">{BOOKING_SERVICES.consultation_office}</option>
          <option value="ur">{BOOKING_SERVICES.consultation_urgent}</option>
        </select>
      </div>
      {p.isPending ? <Loading /> : p.isError ? <ErrorState message={errorMessage(p.error)} /> : (
        <>
          {p.data.calendar !== "ok" ? <p className="text-xs text-orange-ink">كالندر المستشار {p.data.calendar === "not_configured" ? "مش متوصل" : "مش بيرد"}: دي ساعات اللوحة بس، وحازم مش هيعرض مواعيد لحد ما يتوصل.</p> : null}
          {p.data.slots.length === 0 ? <p className="text-sm text-ink-soft">مفيش مواعيد فاضية اليوم ده.</p> : (
            <ul className="flex flex-wrap gap-1.5">{p.data.slots.map((s) => <li key={s} className="tabular rounded-md bg-card-2 px-2 py-1 text-xs">{cairoTime(new Date(s))}</li>)}</ul>
          )}
        </>
      )}
    </div>
  );
}

function TimeSelect({ value, onChange, label: aria, min = 0 }: { value: number; onChange: (m: number) => void; label: string; min?: number }) {
  return (
    <select aria-label={aria} className="surface tabular min-h-10 rounded-[var(--radius-control)] px-2" value={value} onChange={(e) => onChange(Number(e.target.value))}>
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

  const row = (a: Appointment) => (
    <li key={a.id} className="flex flex-wrap items-center gap-3 py-2.5">
      <span className="grid min-w-0 flex-1 gap-0.5">
        <span className="font-semibold">
          {cairoTime(new Date(a.start))} · {a.name ?? "من غير اسم"}
          <span className="ms-2 text-xs font-normal text-ink-soft">{lengthWords(minutesOf(a))}{isLive(a) ? ` · ${fromNow(a, now)}` : ""}</span>
        </span>
        <span className="text-xs text-ink-soft">
          {label(BOOKING_SERVICES, a.service)}{a.call_app ? ` (${label(CALL_APPS, a.call_app)})` : ""} · {matterLabel(a.matter_type ?? undefined)} · <Phone wa={a.wa_id} />{a.reschedules ? " · اتغيّر مرة" : ""}
          {a.payment && a.state !== "pending_payment" ? ` · الدفع: ${label(PAYMENT_STATES, a.payment)}` : ""}
        </span>
        {a.state === "confirmed" && a.reminders.length > 0 ? <Reminders items={a.reminders} /> : null}
        {notesOf(a, now).length > 0 ? (
          <span className="flex flex-wrap gap-1.5 pt-1">
            {notesOf(a, now).map((n) => (
              <span key={n.text} className="inline-flex items-center gap-1 text-xs font-semibold">
                <Icon name="alert-circle" className={`size-3.5 ${n.tone === "orange" ? "text-orange-ink" : "text-grey-ink"}`} />{n.text}
              </span>
            ))}
          </span>
        ) : null}
      </span>
      {a.state === "confirmed" && a.attending ? <Chip tone="green">أكّد حضوره ✅</Chip> : null}
      <Chip tone={STATE_TONE[a.state] ?? "grey"}>
        {label(APPOINTMENT_STATES, a.state)}{a.state === "pending_payment" && a.hold_until ? ` لحد ${cairoTime(new Date(a.hold_until))}` : ""}
      </Chip>
      {a.state === "completed" && can(me, "services.price") ? <Opinion id={a.id} /> : null}
      {isLive(a) ? (
        <span className="flex flex-wrap gap-1">
          {a.state === "confirmed" ? (
            <>
              <Button variant="ghost" busy={mark.isPending && mark.variables?.id === a.id && mark.variables.state === "completed"}
                onClick={() => mark.mutate({ id: a.id, state: "completed" })}>خلص</Button>
              <Button variant="ghost" busy={mark.isPending && mark.variables?.id === a.id && mark.variables.state === "no_show"}
                onClick={() => mark.mutate({ id: a.id, state: "no_show" })}>ماحضرش</Button>
            </>
          ) : null}
          <Button variant="danger" busy={cancel.isPending && cancel.variables === a.id}
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
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex gap-1" role="group" aria-label="أنهي حجوزات">
          {([["ahead", "الجاية (30 يوم)"], ["past", "اللي فاتت (أسبوعين)"]] as const).map(([v, t]) => (
            <Button key={v} variant={when === v ? "dark" : "secondary"} aria-pressed={when === v} onClick={() => setWhen(v)} className="!min-h-9 !px-3 text-sm">{t}</Button>
          ))}
        </div>
        <label className="relative w-full sm:w-72">
          <span className="sr-only">دوّر بالاسم أو الرقم</span>
          <Icon name="search" className="pointer-events-none absolute start-3 top-1/2 size-4 -translate-y-1/2 text-ink-soft" />
          <input type="search" value={q} onChange={(e) => setQ(e.target.value)} placeholder="دوّر بالاسم أو الرقم"
            className="min-h-9 w-full rounded-[var(--radius-control)] border border-line-strong bg-card ps-9 pe-3 text-sm text-ink" />
        </label>
      </div>

      <section aria-label="ملخص الحجوزات" className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        {tiles.map((t) => (
          <Card key={t.label}>
            <span className={`text-[26px] leading-tight font-semibold ${t.tone === "text-red-ink" && !t.value ? "text-ink" : t.tone}`}>{t.value === undefined ? "…" : t.value}</span>
            <span className="text-[13.5px] text-ink-soft">{t.label}</span>
          </Card>
        ))}
      </section>

      {next ? (
        <Card>
          <span className="text-[13px] font-semibold text-gold-ink">{fromNow(next, now) === "دلوقتي" ? "شغال دلوقتي" : "الميعاد الجاي"}</span>
          <span className="flex flex-wrap items-baseline gap-x-3">
            <span className="text-[26px] leading-tight font-semibold">{cairoTime(new Date(next.start))}</span>
            <span className="text-sm text-ink-soft">{cairoWhen(next.start)}{fromNow(next, now) !== "دلوقتي" ? ` · ${fromNow(next, now)}` : ""}</span>
          </span>
          <span className="font-semibold">{next.name ?? "من غير اسم"}</span>
          <span className="text-sm text-ink-soft">
            {label(BOOKING_SERVICES, next.service)}{next.call_app ? ` (${label(CALL_APPS, next.call_app)})` : ""} · {lengthWords(minutesOf(next))} · <Phone wa={next.wa_id} />
          </span>
        </Card>
      ) : null}

      {tomorrows.length > 0 ? (
        <p className="text-sm" role="status">
          بكرة {tomorrows.length} {tomorrows.length === 1 ? "ميعاد" : "مواعيد"}
          {unconfirmed > 0 ? <>، <span className="font-semibold text-orange-ink">{unconfirmed} ما أكدوش حضورهم</span></> : "، وكلهم أكدوا حضورهم ✅"}
        </p>
      ) : null}

      <div className="flex flex-wrap gap-1" role="group" aria-label="نوع الميعاد">
        {([["all", `الكل ${all.length}`], ["phone", "تليفون أو فيديو"], ["office", "في المكتب"], ["urgent", "عاجلة"], ["urf", "عرفي"]] as const).map(([v, t]) => (
          <Button key={v} variant={kind === v ? "dark" : "secondary"} aria-pressed={kind === v} onClick={() => setKind(v)} className="!min-h-9 !px-3 text-sm">{t}</Button>
        ))}
      </div>

      {list.isPending ? <Loading /> : list.isError ? <ErrorState message={errorMessage(list.error)} onRetry={() => void list.refetch()} /> : days.length === 0 ? (
        all.length > 0
          ? <Empty title="مفيش حجوزات بالشكل ده" icon="search">جرّب نوع تاني أو امسح البحث.</Empty>
          : <Empty title={when === "ahead" ? "مفيش حجوزات في الشهر الجاي" : "مفيش حجوزات في الأسبوعين اللي فاتوا"} icon="calendar" />
      ) : days.map((g) => {
        const live = g.items.filter(isLive);
        const held = g.items.filter((a) => a.state === "pending_payment").length;
        return (
          <Card key={g.day}>
            <h3 className="flex flex-wrap items-baseline gap-x-3 border-b border-line pb-2 font-semibold">
              {g.day === todayKey ? "النهارده" : g.day === tomorrowKey ? "بكرة" : ""} <span className="text-sm font-normal text-ink-soft">{longDay(g.day)}</span>
              <span className="ms-auto flex flex-wrap gap-x-3 text-xs font-normal text-ink-soft">
                <span>{g.items.length} {g.items.length === 1 ? "ميعاد" : "مواعيد"}</span>
                {live.length ? <span>{lengthWords(live.reduce((s, a) => s + minutesOf(a), 0))} محجوزة</span> : null}
                {held ? <span className="text-orange-ink">{held} مستني الإيصال</span> : null}
              </span>
            </h3>
            <ul className="grid divide-y divide-line">{g.items.map(row)}</ul>
          </Card>
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
    <span className="text-xs text-ink-soft">
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
  if (!open) return <Button variant="ghost" onClick={() => setOpen(true)}>مطلوب رأي مكتوب</Button>;
  return (
    <span className="flex flex-wrap items-end gap-2">
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

  return (
    <Card>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <p className="max-w-xl text-sm text-ink-soft">دي المواعيد اللي حازم بيحجز فيها كل أسبوع. اليوم من غير فترات = إجازة. أي يوم معين بيتغيّر من «التقويم».</p>
        <p className="flex gap-4 text-sm text-ink-soft" role="status">
          <span><strong className="tabular font-semibold text-ink">{hoursWords(total)}</strong> في الأسبوع</span>
          <span><strong className="tabular font-semibold text-ink">{working}</strong> {working === 1 ? "يوم" : "أيام"} شغل</span>
        </p>
      </div>
      {draft ? <p role="status" className="rounded-lg bg-gold-tint px-3 py-2 text-sm font-semibold text-gold-ink">فيه تعديلات لسه ما اتحفظتش.</p> : null}
      <ul className="grid divide-y divide-line">
        {WEEK.map((w) => {
          const d = days.find((x) => x.weekday === w) ?? { weekday: w, ranges: [] };
          return (
            <li key={w} className="flex flex-wrap items-center gap-2 py-2.5">
              <span className="w-20 font-semibold">{DAY_NAMES[w]}</span>
              {d.ranges.length === 0 ? <Chip tone="grey">إجازة</Chip> : null}
              {d.ranges.map((r, i) => (
                <Row key={i}>
                  <TimeSelect label={`بداية فترة ${i + 1} يوم ${DAY_NAMES[w]}`} value={r.start} onChange={(v) => edit(w, d.ranges.map((x, j) => (j === i ? { ...x, start: v } : x)))} />
                  –
                  <TimeSelect label={`نهاية فترة ${i + 1} يوم ${DAY_NAMES[w]}`} value={r.end} min={15} onChange={(v) => edit(w, d.ranges.map((x, j) => (j === i ? { ...x, end: v } : x)))} />
                  <Button variant="ghost" aria-label="شيل الفترة" onClick={() => edit(w, d.ranges.filter((_, j) => j !== i))}><Icon name="x" className="size-4" /></Button>
                </Row>
              ))}
              <Button variant="ghost" onClick={() => edit(w, [...d.ranges, { start: d.ranges.at(-1)?.end ?? 600, end: 1440 }])}>+ فترة</Button>
              {d.ranges.length ? <span className="ms-auto tabular text-xs text-ink-soft">{hoursWords(minutesIn(d))}</span> : null}
            </li>
          );
        })}
      </ul>
      {affected ? <Confirm list={affected} busy={save.isPending} onYes={() => save.mutate(true)} onNo={() => setAffected(null)} /> : null}
      {save.isError && !affected ? <p className="text-sm text-bad" role="alert">{errorMessage(save.error)}</p> : null}
      <div className="flex gap-2">
        <Button variant="primary" busy={save.isPending && !affected} disabled={!draft} onClick={() => save.mutate(false)}>احفظ الأسبوع</Button>
        {draft ? <Button onClick={() => setDraft(null)}>ارجع زي ما كان</Button> : null}
      </div>
    </Card>
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

const Row = ({ children }: { children: ReactNode }) => <span className="flex items-center gap-1 rounded-lg bg-card-2 px-2 py-1">{children}</span>;

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
    <Card>
      <label className="flex items-center gap-2 font-semibold">
        <input type="checkbox" checked={v.enabled} onChange={(e) => setDraft({ ...v, enabled: e.target.checked })} />
        حازم يحجز من واتساب
      </label>
      <div className="grid gap-3 md:grid-cols-2">
        <Field label="الوقت بين كل ميعاد والتاني (دقيقة)" type="number" min={0} max={240} value={v.buffer_minutes} onChange={num("buffer_minutes")} />
        <Field label="أقل مدة قبل الحجز (ساعة)" hint="ما بتنطبقش على العاجل." type="number" min={0} max={168} value={v.min_notice_hours} onChange={num("min_notice_hours")} />
        <Field label="أبعد يوم ينفع يتحجز (يوم من النهارده)" type="number" min={1} max={180} value={v.horizon_days} onChange={num("horizon_days")} />
        <Field label="أقصى عدد مواعيد في اليوم" hint="فاضي = من غير حد." type="number" min={1} max={100} value={v.max_per_day ?? ""} onChange={num("max_per_day")} />
        <label className="grid gap-1.5 text-sm font-semibold">
          الميعاد بيبدأ كل
          <select className="surface min-h-11 rounded-[var(--radius-control)] px-3 font-normal" value={v.slot_step_minutes} onChange={num("slot_step_minutes")}>
            {[5, 10, 15, 20, 30, 60].map((m) => <option key={m} value={m}>{m} دقيقة</option>)}
          </select>
        </label>
        <Field label="عدد المواعيد اللي حازم يعرضها مرة واحدة" type="number" min={1} max={9} value={v.offer_count} onChange={num("offer_count")} />
        <Field label="إيميل كالندر المستشار (Google)" dir="ltr" value={v.calendar_id} onChange={(e) => setDraft({ ...v, calendar_id: e.target.value })}
          hint={v.calendar_key ? "مفتاح Google موجود." : "مفتاح Google لسه مش متحط (صاحب المشروع)."} />
      </div>
      <fieldset className="grid gap-3 border-t border-line pt-3">
        <legend className="text-sm font-semibold">الحجز المؤقت لحد صورة التحويل</legend>
        <p className="text-xs text-ink-soft">العميل يختار ميعاد ← الميعاد يتمسك له لحد ساعة محددة. صورة التحويل توصل ← الحجز يتأكد. ما وصلتش ← الميعاد يتفك ويرجع فاضي.</p>
        <div className="grid gap-3 md:grid-cols-2">
          <Field label="مدة الحجز المؤقت (دقيقة)" type="number" min={15} max={1440} value={v.hold_minutes} onChange={num("hold_minutes")} />
          <Field label="المدة لو الميعاد قريب (دقيقة)" type="number" min={15} max={1440} value={v.hold_short_minutes} onChange={num("hold_short_minutes")} />
          <Field label="«قريب» يعني بعد أقل من (ساعة)" type="number" min={0} max={72} value={v.hold_short_within_hours} onChange={num("hold_short_within_hours")} />
          <Field label="الحجز المؤقت يخلص قبل الميعاد بـ (دقيقة)" type="number" min={0} max={240} value={v.hold_gap_minutes} onChange={num("hold_gap_minutes")} />
        </div>
      </fieldset>
      <fieldset className="grid gap-3 border-t border-line pt-3">
        <legend className="text-sm font-semibold">العرفي</legend>
        <div className="grid gap-3 md:grid-cols-2">
          <Field label="مدة ميعاد العرفي في المكتب (دقيقة)" type="number" min={15} max={480} value={v.urf_minutes} onChange={num("urf_minutes")} />
        </div>
      </fieldset>
      {save.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(save.error)}</p> : null}
      <div><Button variant="primary" busy={save.isPending} disabled={!draft} onClick={() => save.mutate(v)}>احفظ</Button></div>
    </Card>
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

  return (
    <Card>
      <p className="text-sm text-ink-soft">حازم بيكتب البيانات دي في رسالة الحجز المؤقت (فين يحوّل). لو InstaPay أو المحفظة فاضيين، الميعاد بيتمسك للعميل بس الرسالة مش بتتبعت وبتتعمل مهمة.</p>
      <p className="text-sm text-ink-soft">لينك Google Meet: لو فاضي، حازم مش بيعرض Meet على العميل. والعنوان والخريطة ورقم التليفون بيتكتبوا في تأكيد المقابلة في المكتب بعد مراجعة التحويل.</p>
      <div className="grid gap-3 md:grid-cols-2">
        <Field label="عنوان InstaPay" dir="ltr" value={v.instapay} onChange={set("instapay")} />
        <Field label="رقم المحفظة" dir="ltr" inputMode="tel" value={v.wallet_number} onChange={set("wallet_number")} />
        <Field label="نوع المحفظة" hint="مثلًا فودافون كاش" value={v.wallet_provider} onChange={set("wallet_provider")} />
        <Field label="عنوان المكتب" value={v.address} onChange={set("address")} />
        <Field label="لينك الخريطة" dir="ltr" placeholder="https://maps.app.goo.gl/…" value={v.map_url} onChange={set("map_url")} />
        <Field label="لينك Google Meet بتاع المستشار" dir="ltr" placeholder="https://meet.google.com/…" value={v.meet_link ?? ""} onChange={set("meet_link")} />
        <Field label="رقم تليفون المكتب" dir="ltr" inputMode="tel" value={v.phone ?? ""} onChange={set("phone")} />
      </div>
      {save.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(save.error)}</p> : null}
      <div><Button variant="primary" busy={save.isPending} disabled={!draft} onClick={() => save.mutate(v)}>احفظ</Button></div>
    </Card>
  );
}
