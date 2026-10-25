import { useState, type ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ApiError, errorMessage } from "../../shared/api/client";
import { can, useMe } from "../../shared/api/session";
import { APPOINTMENT_STATES, BOOKING_SERVICES, label, matterLabel, PAYMENT_STATES, REMINDER_KINDS, REMINDER_SKIPS } from "../../shared/lib/labels";
import { cairoTime, cairoWhen } from "../../shared/lib/time";
import { Icon } from "../../shared/ui/Icon";
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

/**
 * «المواعيد» (TASK 08, DECISIONS v1.12 B-24): the booking hours Hazem offers, changed from here and
 * seen by Hazem at once. The calendar of the month (close a day, close some hours, other hours that
 * day), the appointments, the default week, the settings and (manager / lawyer) the office's details.
 */
export function BookingPage() {
  const { data: me } = useMe();
  const [tab, setTab] = useState<Tab>("calendar");
  const tabs: [Tab, string][] = [["calendar", "التقويم"], ["bookings", "الحجوزات"], ["week", "المواعيد الثابتة"], ["settings", "إعدادات الحجز"]];
  if (can(me, "office.manage")) tabs.push(["office", "بيانات المكتب"]);

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
  const s = useQuery({ queryKey: ["booking", "settings"], queryFn: booking.settings });
  if (!s.data) return null;
  const missing = [
    !s.data.calendar_key && "مفتاح Google Calendar (صاحب المشروع بيحطه في .env)",
    !s.data.calendar_id && "إيميل كالندر الأستاذ (في إعدادات الحجز)",
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
  const [month, setMonth] = useState(() => monthOf(cairoDate(new Date())));
  const [picked, setPicked] = useState<string | null>(null);
  const days = useQuery({ queryKey: ["booking", "month", month], queryFn: () => booking.month(month) });
  const first = days.data?.[0];
  const lead = first ? WEEK.indexOf(new Date(`${first.day}T12:00:00Z`).getUTCDay()) : 0;
  const day = days.data?.find((d) => d.day === picked);

  return (
    <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_24rem]">
      <Card>
        <div className="flex items-center justify-between">
          <Button variant="ghost" aria-label="الشهر اللي فات" onClick={() => setMonth(shiftMonth(month, -1))}><Icon name="chevron-start" className="size-5" /></Button>
          <h2 className="text-lg tabular">{new Intl.DateTimeFormat("ar-EG-u-nu-latn", { month: "long", year: "numeric", timeZone: "UTC" }).format(new Date(`${month}-15T00:00:00Z`))}</h2>
          <Button variant="ghost" aria-label="الشهر الجاي" onClick={() => setMonth(shiftMonth(month, 1))}><Icon name="chevron-end" className="size-5" /></Button>
        </div>
        {days.isPending ? <Loading /> : days.isError ? <ErrorState message={errorMessage(days.error)} onRetry={() => void days.refetch()} /> : (
          <div className="grid grid-cols-7 gap-1 text-center" role="grid" aria-label="أيام الشهر">
            {WEEK.map((w) => <span key={w} className="py-1 text-xs font-semibold text-ink-soft">{DAY_NAMES[w]!.slice(0, 3)}</span>)}
            {Array.from({ length: lead }, (_, i) => <span key={`x${i}`} />)}
            {days.data.map((d) => (
              <button key={d.day} type="button" role="gridcell" aria-selected={d.day === picked} onClick={() => setPicked(d.day)}
                aria-label={`${d.day}: ${STATUS[d.status].word}${d.bookings ? `، ${d.bookings} حجز` : ""}`}
                className={`grid min-h-14 content-start justify-items-center gap-0.5 rounded-lg border p-1 text-sm ${d.day === picked ? "border-gold bg-gold-tint" : "border-line hover:border-gold-line"} ${d.status === "closed" || d.status === "weekly_off" ? "opacity-60" : ""}`}>
                <span className="tabular font-semibold">{Number(d.day.slice(8))}</span>
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
      {day ? <DaySheet key={day.day} day={day} /> : <Card><Empty title="اختار يوم" icon="calendar">تقفله، أو تقفل جزء منه، أو تديله ساعات مختلفة، وتشوف حازم هيعرض إيه.</Empty></Card>}
    </div>
  );
}

/** One date: its hours, its exceptions, closing it (asks first if it has bookings), and the preview. */
function DaySheet({ day }: { day: Day }) {
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
      <h2 className="flex flex-wrap items-center gap-2 text-lg">{longDay(day.day)} <Chip tone={STATUS[day.status].tone}>{STATUS[day.status].word}</Chip></h2>
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
          {p.data.calendar !== "ok" ? <p className="text-xs text-orange-ink">كالندر الأستاذ {p.data.calendar === "not_configured" ? "مش متوصل" : "مش بيرد"}: دي ساعات اللوحة بس، وحازم مش هيعرض مواعيد لحد ما يتوصل.</p> : null}
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

function Bookings() {
  const qc = useQueryClient();
  const { data: me } = useMe();
  // TASK 10: the last two weeks too, so a consultation that took place can get «مطلوب رأي مكتوب» (B-17).
  const today = cairoDate(new Date(Date.now() - 14 * 86_400_000));
  const to = cairoDate(new Date(Date.now() + 30 * 86_400_000));
  const list = useQuery({ queryKey: ["booking", "appointments", today], queryFn: () => booking.appointments(today, to) });
  const refresh = () => void qc.invalidateQueries({ queryKey: ["booking"] });
  const mark = useMutation({ mutationFn: ({ id, state }: { id: string; state: string }) => booking.mark(id, state), onSuccess: refresh });
  const cancel = useMutation({ mutationFn: booking.cancel, onSuccess: refresh });

  if (list.isPending) return <Loading />;
  if (list.isError) return <ErrorState message={errorMessage(list.error)} onRetry={() => void list.refetch()} />;
  if (list.data.length === 0) return <Empty title="مفيش حجوزات في الشهر الجاي" icon="calendar" />;
  // TASK 12 (B-18): tomorrow's confirmed appointments whose client has not pressed «هحضر ✅» yet (nothing is cancelled for it).
  const tomorrow = cairoDate(new Date(Date.now() + 86_400_000));
  const tomorrows = list.data.filter((a) => a.state === "confirmed" && cairoDate(new Date(a.start)) === tomorrow);
  const unconfirmed = tomorrows.filter((a) => !a.attending).length;
  return (
    <Card>
      {tomorrows.length > 0 ? (
        <p className="pb-2 text-sm" role="status">
          بكرة {tomorrows.length} {tomorrows.length === 1 ? "ميعاد" : "مواعيد"}
          {unconfirmed > 0 ? <>، <span className="font-semibold text-orange-ink">{unconfirmed} ما أكدوش حضورهم</span></> : "، وكلهم أكدوا حضورهم ✅"}
        </p>
      ) : null}
      <ul className="grid divide-y divide-line">
        {list.data.map((a) => (
          <li key={a.id} className="flex flex-wrap items-center gap-3 py-2.5">
            <span className="grid min-w-0 flex-1">
              <span className="font-semibold">{cairoWhen(a.start)} · {a.name ?? "من غير اسم"}</span>
              <span className="text-xs text-ink-soft">
                {label(BOOKING_SERVICES, a.service)} · {matterLabel(a.matter_type ?? undefined)} · <span dir="ltr">+{a.wa_id}</span>{a.reschedules ? " · اتغيّر مرة" : ""}
                {a.payment && a.state !== "pending_payment" ? ` · الدفع: ${label(PAYMENT_STATES, a.payment)}` : ""}
              </span>
              {a.state === "confirmed" && a.reminders.length > 0 ? <Reminders items={a.reminders} /> : null}
            </span>
            {a.state === "confirmed" && a.attending ? <Chip tone="green">أكّد حضوره ✅</Chip> : null}
            <Chip tone={STATE_TONE[a.state] ?? "grey"}>
              {label(APPOINTMENT_STATES, a.state)}{a.state === "pending_payment" && a.hold_until ? ` لحد ${cairoTime(new Date(a.hold_until))}` : ""}
            </Chip>
            {a.state === "completed" && can(me, "services.price") ? <Opinion id={a.id} /> : null}
            {a.state === "confirmed" || a.state === "pending_payment" ? (
              <span className="flex flex-wrap gap-1">
                {a.state === "confirmed" ? (
                  <>
                    <Button variant="ghost" onClick={() => mark.mutate({ id: a.id, state: "completed" })}>خلص</Button>
                    <Button variant="ghost" onClick={() => mark.mutate({ id: a.id, state: "no_show" })}>ماحضرش</Button>
                  </>
                ) : null}
                <Button variant="danger" busy={cancel.isPending && cancel.variables === a.id}
                  onClick={() => { if (window.confirm("إلغاء الحجز ده؟ حازم هيبعت للعميل يختار ميعاد تاني، أو استرداد كامل لو كان دفع.")) cancel.mutate(a.id); }}>
                  إلغاء
                </Button>
              </span>
            ) : null}
          </li>
        ))}
      </ul>
      {mark.isError || cancel.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(mark.error ?? cancel.error)}</p> : null}
    </Card>
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
    onSuccess: () => { setDraft(null); setAffected(null); void qc.invalidateQueries({ queryKey: ["booking"] }); },
    onError: (e) => { if (e instanceof ApiError && e.code === "has_bookings") setAffected(e.data?.appointments as Appointment[]); },
  });
  if (week.isPending) return <Loading />;
  if (week.isError || !days) return <ErrorState message={errorMessage(week.error)} onRetry={() => void week.refetch()} />;
  const edit = (w: number, ranges: WeekDay["ranges"]) => setDraft(days.map((d) => (d.weekday === w ? { ...d, ranges } : d)));

  return (
    <Card>
      <p className="text-sm text-ink-soft">دي المواعيد اللي حازم بيحجز فيها كل أسبوع. اليوم من غير فترات = إجازة. أي يوم معين بيتغيّر من «التقويم».</p>
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

const Row = ({ children }: { children: ReactNode }) => <span className="flex items-center gap-1 rounded-lg bg-card-2 px-2 py-1">{children}</span>;

// ---------- the settings and the office's details ----------

function SettingsForm() {
  const qc = useQueryClient();
  const s = useQuery({ queryKey: ["booking", "settings"], queryFn: booking.settings });
  const [draft, setDraft] = useState<Settings | null>(null);
  const v = draft ?? s.data;
  const save = useMutation({ mutationFn: booking.saveSettings, onSuccess: () => { setDraft(null); void qc.invalidateQueries({ queryKey: ["booking"] }); } });
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
        <Field label="إيميل كالندر الأستاذ (Google)" dir="ltr" value={v.calendar_id} onChange={(e) => setDraft({ ...v, calendar_id: e.target.value })}
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
  const save = useMutation({ mutationFn: booking.saveOffice, onSuccess: () => { setDraft(null); void qc.invalidateQueries({ queryKey: ["booking"] }); } });
  if (o.isPending) return <Loading />;
  if (o.isError || !v) return <ErrorState message={errorMessage(o.error)} onRetry={() => void o.refetch()} />;
  const set = (k: keyof Office) => (e: { target: { value: string } }) => setDraft({ ...v, [k]: e.target.value });

  return (
    <Card>
      <p className="text-sm text-ink-soft">حازم بيكتب البيانات دي في رسالة الحجز المؤقت (فين يحوّل). لو InstaPay أو المحفظة فاضيين، الميعاد بيتمسك للعميل بس الرسالة مش بتتبعت وبتتعمل مهمة.</p>
      <div className="grid gap-3 md:grid-cols-2">
        <Field label="عنوان InstaPay" dir="ltr" value={v.instapay} onChange={set("instapay")} />
        <Field label="رقم المحفظة" dir="ltr" inputMode="tel" value={v.wallet_number} onChange={set("wallet_number")} />
        <Field label="نوع المحفظة" hint="مثلًا فودافون كاش" value={v.wallet_provider} onChange={set("wallet_provider")} />
        <Field label="عنوان المكتب" value={v.address} onChange={set("address")} />
        <Field label="لينك الخريطة" dir="ltr" placeholder="https://maps.app.goo.gl/…" value={v.map_url} onChange={set("map_url")} />
      </div>
      {save.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(save.error)}</p> : null}
      <div><Button variant="primary" busy={save.isPending} disabled={!draft} onClick={() => save.mutate(v)}>احفظ</Button></div>
    </Card>
  );
}
