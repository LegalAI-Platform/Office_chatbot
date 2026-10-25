import { useState, type ReactNode } from "react";
import { queryOptions, useMutation, useQuery, useQueryClient, type QueryClient } from "@tanstack/react-query";
import { ApiError, errorMessage } from "../../shared/api/client";
import { can, useMe } from "../../shared/api/session";
import { APPOINTMENT_STATES, BOOKING_SERVICES, label, matterLabel, PAYMENT_STATES } from "../../shared/lib/labels";
import { cairoDayWord, cairoTime, cairoWhen } from "../../shared/lib/time";
import { Link } from "react-router";
import { Icon, type IconName } from "../../shared/ui/Icon";
import { Button, Card, Chip, Empty, ErrorState, Field, inputClass, Loading, PageHeader, Segmented, Tabs } from "../../shared/ui/ui";
import { confirmAction, toast } from "../../shared/ui/feedback";
import { useUrlState } from "../../shared/lib/url";
import { booking, cairoDate, hhmm, monthOf, shiftMonth, TIMES, type Appointment, type Day, type Done, type Office, type Settings, type WeekDay } from "./api";

type Tab = "bookings" | "calendar" | "week" | "settings" | "office";
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
// A day cell names its state only when it is not an ordinary open day. Written out in full so Tailwind keeps them.
const CELL_NOTE: Partial<Record<Day["status"], string>> = {
  weekly_off: "text-ink-faint", closed: "text-red-ink", custom_hours: "text-blue-ink", partly_closed: "text-orange-ink",
};
const longDay = (day: string) =>
  new Intl.DateTimeFormat("ar-EG-u-nu-latn", { weekday: "long", day: "numeric", month: "long", timeZone: "UTC" }).format(new Date(`${day}T12:00:00Z`));
const STATE_TONE: Record<string, string> = {
  pending_payment: "orange", expired: "grey", confirmed: "blue", completed: "green", no_show: "orange", cancelled_by_client: "grey", cancelled_by_office: "grey",
};
const DAY = 86_400_000;
const range = (h: { start: number; end: number }) => `${hhmm(h.start)} - ${h.end === 1440 ? "12 بالليل" : hhmm(h.end)}`;

/**
 * «المواعيد» (TASK 08, DECISIONS v1.12 B-24). It opens on who is coming; then the month (close a day, close
 * some hours, other hours that day, seen by Hazem at once), the default week, the settings and (manager /
 * lawyer) the office's details.
 */
export function BookingPage() {
  const { data: me } = useMe();
  const tabs: [Tab, string][] = [["bookings", "الحجوزات"], ["calendar", "التقويم"], ["week", "المواعيد الثابتة"], ["settings", "إعدادات الحجز"]];
  if (can(me, "office.manage")) tabs.push(["office", "بيانات المكتب"]);
  const [tab, setTab] = useUrlState<Tab>("tab", "bookings", tabs.map((t) => t[0]));

  return (
    <div className="grid gap-5">
      <PageHeader section="/appointments" title="المواعيد" description="مين جاي، والساعات اللي حازم بيحجز فيها. أي تعديل هنا حازم بيشوفه على طول." actions={<Undo />} />
      <Status onSettings={() => setTab("settings")} />
      <Tabs label="أقسام المواعيد" value={tab} options={tabs} onChange={setTab} />
      {tab === "bookings" ? <Bookings /> : tab === "calendar" ? <Calendar /> : tab === "week" ? <Week /> : tab === "settings" ? <SettingsForm /> : <OfficeForm />}
    </div>
  );
}

/** Whether Hazem books from WhatsApp now: one line, and what is missing if not. */
function Status({ onSettings }: { onSettings: () => void }) {
  const s = useQuery({ queryKey: ["booking", "settings"], queryFn: booking.settings });
  if (!s.data) return null;
  const missing = [
    !s.data.calendar_key && "ربط كالندر جوجل بالنظام (بيعمله المسؤول التقني)",
    !s.data.calendar_id && "إيميل كالندر الأستاذ (في «إعدادات الحجز»)",
    !s.data.texts_approved && "اعتماد نصوص الحجز (من «نصوص حازم»)",
  ].filter((m): m is string => Boolean(m));
  const ok = s.data.enabled && missing.length === 0;
  if (ok) {
    return (
      <p role="status" className="flex items-center gap-2 text-[14px] text-ink-soft">
        <span className="size-2 rounded-full bg-jade" aria-hidden />حازم بيحجز المواعيد من واتساب.
      </p>
    );
  }
  return (
    <div role="status" className="flex flex-wrap items-start gap-x-3 gap-y-1 rounded-[var(--radius-card)] border border-orange-ink/25 bg-orange-bg px-4 py-2.5 text-[14px]">
      <Icon name="info" className="mt-0.5 size-[18px] shrink-0 text-orange-ink" />
      <p className="min-w-0 flex-1">
        <span className="font-semibold">{s.data.enabled ? "الحجز من واتساب لسه مش شغال." : "الحجز من واتساب مقفول: حازم بيعرض الاستشارة من غير مواعيد."}</span>
        {missing.length > 0 ? <span className="text-ink-soft"> ناقص: {missing.join("، ")}.</span> : null}
      </p>
      <button type="button" onClick={onSettings} className="shrink-0 font-semibold text-accent-ink hover:underline">إعدادات الحجز</button>
    </div>
  );
}

/** Puts back the last change; when that would leave bookings outside the hours it asks first, like any change. */
function Undo() {
  const qc = useQueryClient();
  const changes = useQuery({ queryKey: ["booking", "changes"], queryFn: booking.changes });
  const [affected, setAffected] = useState<Appointment[] | null>(null);
  const undo = useMutation({
    mutationFn: (confirm: boolean) => booking.undo(confirm),
    onSuccess: () => { setAffected(null); void qc.invalidateQueries({ queryKey: ["booking"] }); toast("آخر تعديل اترجع"); },
    onError: (e) => { if (e instanceof ApiError && e.code === "has_bookings") setAffected(e.data?.appointments as Appointment[]); },
  });
  const last = changes.data?.find((c) => !c.undone);
  if (!last) return null;
  return (
    <div className="grid gap-2">
      <span className="flex flex-wrap items-center gap-2 text-[13px] text-ink-soft">
        <Button size="sm" busy={undo.isPending && !affected} onClick={() => undo.mutate(false)}><Icon name="refresh" className="size-4" />تراجع عن آخر تعديل</Button>
        <span>{last.by ?? "—"}، {cairoWhen(last.at)}</span>
        {undo.isError && !affected ? <span className="text-bad" role="alert">{errorMessage(undo.error)}</span> : null}
      </span>
      {affected ? <Confirm list={affected} busy={undo.isPending} onYes={() => undo.mutate(true)} onNo={() => setAffected(null)} /> : null}
    </div>
  );
}

// ---------- the appointments ----------

/** «النهارده» / «بكرة» / «امبارح», else the full date. */
function dayTitle(iso: string, day: string, now: Date) {
  const w = cairoDayWord(iso, now);
  return w === "اليوم" ? "النهارده" : w === "غداً" ? "بكرة" : w === "أمس" ? "امبارح" : longDay(day);
}

const minutes = (a: Appointment) => Math.max(0, Math.round((new Date(a.end).getTime() - new Date(a.start).getTime()) / 60_000));
const span = (m: number) => (m < 60 ? `${m} دقيقة` : m % 60 === 0 ? (m === 60 ? "ساعة" : m === 120 ? "ساعتين" : `${m / 60} ساعات`) : `${Math.floor(m / 60)} س و${m % 60} د`);
const isLive = (a: Appointment) => a.state === "confirmed" || a.state === "pending_payment";
const isUrf = (a: Appointment) => a.service.startsWith("urf_");

/** How far it is from now, for the next day or so: «بعد 40 دقيقة», «دلوقتي», «خلص من ساعتين». */
function fromNow(a: Appointment, now: Date): string | null {
  const s = new Date(a.start).getTime(), e = new Date(a.end).getTime(), t = now.getTime();
  if (s <= t && t < e) return "دلوقتي";
  const m = Math.round(Math.abs(s - t) / 60_000);
  if (m > 24 * 60) return null;
  const words = m < 60 ? `${Math.max(1, m)} دقيقة` : span(Math.round(m / 60) * 60);
  return s > t ? `بعد ${words}` : `من ${words}`;
}

/** Where it happens, with its icon. */
function place(a: Appointment): { icon: IconName; text: string } {
  if (a.service === "consultation_office") return { icon: "clients", text: "في المكتب" };
  if (a.service === "consultation_urgent") return { icon: "alert", text: "عاجلة: الأستاذ بيكلّم العميل" };
  if (isUrf(a)) return { icon: "scale", text: "في المكتب (عرفي)" };
  return { icon: "chat", text: "تليفون أو فيديو: الأستاذ بيكلّم العميل" };
}

/** Where the appointment stands in the lawyer's Google Calendar. */
const CALENDAR: Record<string, { text: string; warn: boolean }> = {
  synced: { text: "متسجل في كالندر الأستاذ", warn: false },
  create: { text: "لسه بيتسجل في كالندر الأستاذ", warn: true },
  update: { text: "التعديل لسه بيتسجل في الكالندر", warn: true },
  wait: { text: "هيتسجل في الكالندر لما يتأكد", warn: false },
  remove: { text: "بيتشال من الكالندر", warn: false },
  removed: { text: "اتشال من الكالندر", warn: false },
};

type Kind = "all" | "office" | "phone" | "urgent" | "urf";
const KIND_TEST: Record<Kind, (a: Appointment) => boolean> = {
  all: () => true,
  office: (a) => a.service === "consultation_office",
  phone: (a) => a.service === "consultation_phone_video",
  urgent: (a) => a.service === "consultation_urgent",
  urf: isUrf,
};

async function copy(text: string) {
  try {
    await navigator.clipboard.writeText(text);
    toast("الرقم اتنسخ");
  } catch {
    toast("مش قادرين ننسخ: انسخه بإيدك");
  }
}

/** The bookings between two Cairo dates, refreshed every minute; the same key wherever it is read or fetched ahead. */
const bookingsQuery = (from: string, to: string) =>
  queryOptions({ queryKey: ["booking", "appointments", from, to], queryFn: () => booking.appointments(from, to), refetchInterval: 60_000 });

/** «الحجوزات الجاية» (the tab the page opens on), fetched while the pointer is still on the link. */
export function prefetchData(qc: QueryClient) {
  const now = new Date();
  void qc.prefetchQuery(bookingsQuery(cairoDate(now), cairoDate(new Date(now.getTime() + 30 * DAY))));
}

function Bookings() {
  const qc = useQueryClient();
  const { data: me } = useMe();
  const [when, setWhen] = useUrlState<"ahead" | "past">("when", "ahead", ["ahead", "past"]);
  const [kind, setKind] = useUrlState<Kind>("kind", "all", ["all", "office", "phone", "urgent", "urf"]);
  const [q, setQ] = useState("");
  const now = new Date();
  // Ahead: today and the next 30 days. Past (TASK 10): the last two weeks, so a consultation that took place can get
  // «مطلوب رأي مكتوب» (B-17), most recent first.
  const from = when === "ahead" ? cairoDate(now) : cairoDate(new Date(now.getTime() - 14 * DAY));
  const to = when === "ahead" ? cairoDate(new Date(now.getTime() + 30 * DAY)) : cairoDate(new Date(now.getTime() - DAY));
  const list = useQuery(bookingsQuery(from, to));
  const refresh = () => { void qc.invalidateQueries({ queryKey: ["booking"] }); void qc.invalidateQueries({ queryKey: ["appointments"] }); };
  const mark = useMutation({
    mutationFn: ({ id, state }: { id: string; state: string }) => booking.mark(id, state),
    onSuccess: (_, v) => { refresh(); toast(v.state === "completed" ? "اتعلّم إن الميعاد خلص" : "اتعلّم إن العميل ماحضرش"); },
  });
  const cancel = useMutation({ mutationFn: booking.cancel, onSuccess: () => { refresh(); toast("الحجز اتلغى، وحازم هيبلّغ العميل"); } });

  const all = (list.data ?? []).slice().sort((a, b) => (when === "ahead" ? 1 : -1) * a.start.localeCompare(b.start));
  const needle = q.trim().replace(/^\+/, "");
  const items = all.filter((a) => KIND_TEST[kind](a) && (!needle || (a.name ?? "").includes(needle) || a.wa_id.includes(needle)));
  const days = [...new Set(items.map((a) => cairoDate(new Date(a.start))))].map((day) => ({ day, items: items.filter((a) => cairoDate(new Date(a.start)) === day) }));
  const todayKey = cairoDate(now);
  const weekEnd = cairoDate(new Date(now.getTime() + 6 * DAY));
  const next = when === "ahead" ? all.find((a) => isLive(a) && new Date(a.end) > now) : undefined;
  const count = (f: (a: Appointment) => boolean) => (list.data ? all.filter(f).length : undefined);
  const tiles: TileSpec[] = when === "ahead" ? [
    { label: "النهارده", value: count((a) => isLive(a) && cairoDate(new Date(a.start)) === todayKey), tone: "blue" },
    { label: "الأسبوع ده", value: count((a) => isLive(a) && cairoDate(new Date(a.start)) <= weekEnd), tone: "blue" },
    { label: "مستني الإيصال", value: count((a) => a.state === "pending_payment"), tone: "gold" },
    { label: "عاجلة", value: count((a) => isLive(a) && a.service === "consultation_urgent"), tone: "red" },
  ] : [
    { label: "خلصت", value: count((a) => a.state === "completed"), tone: "green" },
    { label: "ماحضروش", value: count((a) => a.state === "no_show"), tone: "gold" },
    { label: "اتلغت", value: count((a) => a.state.startsWith("cancelled")), tone: "ink" },
    { label: "اتفكت (الإيصال ما وصلش)", value: count((a) => a.state === "expired"), tone: "ink" },
  ];

  const actions = (a: Appointment) => (
    <BookingActions a={a} now={now} canOpinion={can(me, "services.price")} marking={mark.isPending && mark.variables?.id === a.id}
      cancelling={cancel.isPending && cancel.variables === a.id}
      onMark={(state) => mark.mutate({ id: a.id, state })}
      onCancel={async () => {
        if (await confirmAction({ title: "تلغي الحجز ده؟", yes: "الغي الحجز", danger: true, body: "حازم هيبعت للعميل يختار ميعاد تاني، أو استرداد كامل لو كان دفع." })) cancel.mutate(a.id);
      }} />
  );

  return (
    <div className="grid gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <Segmented label="أنهي حجوزات" value={when} onChange={setWhen} options={[["ahead", "الجاية (30 يوم)"], ["past", "اللي فاتت (أسبوعين)"]] as const} />
        <label className="relative w-full sm:w-72">
          <span className="sr-only">دوّر بالاسم أو الرقم</span>
          <Icon name="search" className="pointer-events-none absolute start-3 top-1/2 size-4 -translate-y-1/2 text-ink-faint" />
          <input type="search" value={q} onChange={(e) => setQ(e.target.value)} placeholder="دوّر بالاسم أو الرقم" className={`${inputClass} !min-h-9 ps-9 text-[14px]`} />
        </label>
      </div>

      <section aria-label="ملخص الحجوزات" className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        {tiles.map((t) => <CountTile key={t.label} {...t} />)}
      </section>

      {next ? <NextUp a={next} now={now}>{actions(next)}</NextUp> : null}

      <Segmented label="نوع الميعاد" value={kind} onChange={setKind}
        options={[["all", `الكل ${all.length}`], ["office", "في المكتب"], ["phone", "تليفون أو فيديو"], ["urgent", "عاجلة"], ["urf", "عرفي"]] as const} />

      {list.isPending ? <Loading /> : list.isError ? <ErrorState message={errorMessage(list.error)} onRetry={() => void list.refetch()} /> : days.length === 0 ? (
        <div className="surface rounded-[var(--radius-card)]">
          {all.length > 0
            ? <Empty title="مفيش حجوزات بالشكل ده" icon="search">جرّب نوع تاني أو امسح البحث.</Empty>
            : when === "ahead"
              ? <Empty title="مفيش حجوزات في الشهر الجاي" icon="calendar">أي حجز من واتساب هيظهر هنا باليوم.</Empty>
              : <Empty title="مفيش حجوزات في الأسبوعين اللي فاتوا" icon="calendar" />}
        </div>
      ) : days.map((g) => {
        const live = g.items.filter(isLive);
        const title = dayTitle(g.items[0]!.start, g.day, now);
        return (
          <section key={g.day} aria-label={longDay(g.day)} className="surface overflow-hidden rounded-[var(--radius-card)]">
            <header className="flex flex-wrap items-baseline gap-x-3 gap-y-1 border-b border-line px-5 py-3">
              <h3 className="text-[15.5px] font-semibold">{title}</h3>
              {title !== longDay(g.day) ? <span className="text-[13px] text-ink-soft">{longDay(g.day)}</span> : null}
              <span className="ms-auto flex flex-wrap gap-x-3 text-[12.5px] text-ink-faint">
                <span>{g.items.length} {g.items.length === 1 ? "ميعاد" : "مواعيد"}</span>
                {live.length ? <span>{span(live.reduce((s, a) => s + minutes(a), 0))} محجوزة</span> : null}
                {g.items.some((a) => a.state === "pending_payment") ? <span className="text-gold-ink">{g.items.filter((a) => a.state === "pending_payment").length} مستني الإيصال</span> : null}
              </span>
            </header>
            <ul className="grid divide-y divide-line">
              {g.items.map((a) => <BookingRow key={a.id} a={a} now={now} next={next?.id === a.id}>{actions(a)}</BookingRow>)}
            </ul>
          </section>
        );
      })}
      {mark.isError || cancel.isError ? <p className="text-[14px] text-bad" role="alert">{errorMessage(mark.error ?? cancel.error)}</p> : null}
    </div>
  );
}

type TileSpec = { label: string; value: number | undefined; tone: "blue" | "gold" | "red" | "green" | "ink" };
// Written out in full so Tailwind keeps them.
const TILE_INK = { blue: "text-cobalt-ink", gold: "text-gold-ink", red: "text-red-ink", green: "text-jade-ink", ink: "text-ink" } as const;

/** One count of the list, in the color of its job (red only when there is one). */
function CountTile({ label: text, value, tone }: TileSpec) {
  const ink = tone === "red" && !value ? "text-ink" : TILE_INK[tone];
  return (
    <div className="surface grid gap-0.5 rounded-[var(--radius-card)] px-4 py-3">
      <span className={`display text-[28px] leading-tight font-semibold ${ink}`}>{value === undefined ? "…" : value}</span>
      <span className="text-[13.5px] text-ink-soft">{text}</span>
    </div>
  );
}

/** The next appointment that has not ended: who, when, how long from now, and its actions. */
function NextUp({ a, now, children }: { a: Appointment; now: Date; children: ReactNode }) {
  const p = place(a);
  const rel = fromNow(a, now);
  return (
    <section aria-label="الميعاد الجاي" className="surface grid gap-3 rounded-[var(--radius-card)] !border-gold/40 !bg-[color-mix(in_oklab,var(--gold)_7%,var(--card))] p-4 lg:p-5">
      <div className="flex flex-wrap items-center gap-x-5 gap-y-3">
        <div className="grid">
          <span className="text-[13px] font-semibold text-gold-ink">{rel === "دلوقتي" ? "شغال دلوقتي" : "الميعاد الجاي"}</span>
          <span className="display text-[30px] leading-tight font-semibold">{cairoTime(new Date(a.start))}</span>
          <span className="text-[13px] text-ink-soft">{dayTitle(a.start, cairoDate(new Date(a.start)), now)}{rel && rel !== "دلوقتي" ? ` - ${rel}` : ""}</span>
        </div>
        <div className="grid min-w-[14rem] flex-1 gap-1 border-line ps-0 sm:border-s sm:ps-5">
          <span className="text-[17px] font-semibold">{a.name ?? "من غير اسم"}</span>
          <span className="flex flex-wrap items-center gap-x-3 gap-y-1 text-[13.5px] text-ink-soft">
            <span className="flex items-center gap-1.5"><Icon name={p.icon} className="size-4" />{label(BOOKING_SERVICES, a.service)}</span>
            <span className="flex items-center gap-1.5"><Icon name="clock" className="size-4" />{span(minutes(a))}</span>
            {a.matter_type ? <span>{matterLabel(a.matter_type)}</span> : null}
          </span>
          <span className="text-[13px] text-ink-faint">{p.text}{a.service !== "consultation_office" && !isUrf(a) ? ` على +${a.wa_id}` : ""}</span>
        </div>
        <Chip tone={STATE_TONE[a.state] ?? "grey"}>{label(APPOINTMENT_STATES, a.state)}</Chip>
      </div>
      <div className="flex flex-wrap items-center gap-2 border-t border-gold/20 pt-3">{children}</div>
    </section>
  );
}

/** One booking: the time and how long, who, what, its flags, its actions, and the full details on demand. */
function BookingRow({ a, now, next, children }: { a: Appointment; now: Date; next: boolean; children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const live = isLive(a);
  const urgent = a.service === "consultation_urgent";
  const rel = fromNow(a, now);
  const p = place(a);
  const cal = CALENDAR[a.calendar];
  const holdLeft = a.state === "pending_payment" && a.hold_until ? Math.round((new Date(a.hold_until).getTime() - now.getTime()) / 60_000) : null;
  const flags: { text: string; tone: "gold" | "red" }[] = [];
  if (holdLeft !== null) flags.push(holdLeft > 0 ? { text: `الحجز المؤقت بيخلص بعد ${span(holdLeft)} لو الإيصال ما وصلش`, tone: "gold" } : { text: "الحجز المؤقت خلص وقته", tone: "red" });
  if (a.payment === "proof_received" || a.payment === "duplicate_suspected") flags.push({ text: "وصل إيصال: محتاج مراجعة", tone: "gold" });
  if (a.state === "confirmed" && cal?.warn) flags.push({ text: cal.text, tone: "gold" });
  if (a.state === "confirmed" && new Date(a.end) < now) flags.push({ text: "الميعاد عدّى: علّمه خلص أو ماحضرش", tone: "gold" });

  return (
    <li className={`grid gap-2 px-5 py-3.5 ${next ? "bg-gold/5" : ""}`}>
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <span className="grid w-28 shrink-0 leading-tight">
          <span className={`display text-[17px] font-semibold ${!live ? "text-ink-faint line-through decoration-1" : urgent ? "text-red-ink" : "text-cobalt-ink"}`}>{cairoTime(new Date(a.start))}</span>
          <span className="whitespace-nowrap text-[12px] text-ink-faint">{span(minutes(a))}{rel && live ? ` - ${rel}` : ""}</span>
        </span>
        <span className="grid min-w-[12rem] flex-1 gap-0.5 leading-snug">
          <span className={`truncate font-semibold ${live ? "" : "text-ink-soft"}`}>{a.name ?? "من غير اسم"}</span>
          <span className="flex flex-wrap items-center gap-x-3 gap-y-0.5 text-[13px] text-ink-soft">
            <span className={`flex items-center gap-1.5 ${urgent ? "font-semibold text-red-ink" : ""}`}><Icon name={p.icon} className="size-3.5" />{label(BOOKING_SERVICES, a.service)}</span>
            {a.matter_type ? <span>{matterLabel(a.matter_type)}</span> : null}
            <span dir="ltr" className="text-ink-faint">+{a.wa_id}</span>
          </span>
        </span>
        <Chip tone={STATE_TONE[a.state] ?? "grey"}>
          {label(APPOINTMENT_STATES, a.state)}{a.state === "pending_payment" && a.hold_until ? ` لحد ${cairoTime(new Date(a.hold_until))}` : ""}
        </Chip>
        <span className="ms-auto flex flex-wrap items-center gap-1">
          {children}
          <Button size="sm" variant="ghost" aria-expanded={open} onClick={() => setOpen(!open)}>
            التفاصيل<Icon name="chevron-down" className={`size-4 transition-transform ${open ? "rotate-180" : ""}`} />
          </Button>
        </span>
      </div>
      {flags.length > 0 && live ? (
        <ul className="flex flex-wrap gap-1.5 ps-0 sm:ps-[8rem]">
          {flags.map((f) => (
            <li key={f.text} className={`flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-[12.5px] font-medium ${f.tone === "red" ? "bg-red-bg text-red-ink" : "bg-gold-bg text-gold-ink"}`}>
              <Icon name="info" className="size-3.5" />{f.text}
            </li>
          ))}
        </ul>
      ) : null}
      {open ? (
        <dl className="sheet-in grid gap-x-6 gap-y-2 rounded-[12px] border border-line bg-card-2 p-4 text-[13.5px] sm:grid-cols-2 sm:ms-[8rem]">
          <Detail term="الميعاد">{longDay(cairoDate(new Date(a.start)))}، {cairoTime(new Date(a.start))} - {cairoTime(new Date(a.end))} ({span(minutes(a))})</Detail>
          <Detail term="فين">{p.text}</Detail>
          <Detail term="الخدمة">{label(BOOKING_SERVICES, a.service)}</Detail>
          <Detail term="الموضوع">{a.matter_type ? matterLabel(a.matter_type) : "ما اتحددش"}</Detail>
          <Detail term="رقم واتساب">
            <span className="flex items-center gap-2">
              <span dir="ltr" className="tabular">+{a.wa_id}</span>
              <button type="button" onClick={() => void copy(`+${a.wa_id}`)} className="text-[12.5px] font-semibold text-accent-ink hover:underline">انسخ</button>
            </span>
          </Detail>
          <Detail term="الحالة">{label(APPOINTMENT_STATES, a.state)}</Detail>
          <Detail term="الدفع">{a.payment ? label(PAYMENT_STATES, a.payment) : "مفيش دفع على الميعاد ده"}</Detail>
          {a.hold_until ? <Detail term="الحجز المؤقت لحد">{cairoWhen(a.hold_until)}</Detail> : null}
          <Detail term="كالندر الأستاذ">{cal?.text ?? "—"}</Detail>
          <Detail term="اتغيّر">{a.reschedules ? `${a.reschedules} ${a.reschedules === 1 ? "مرة" : "مرات"}` : "ولا مرة"}</Detail>
          {a.payment === "proof_received" || a.payment === "duplicate_suspected" ? (
            <div className="sm:col-span-2"><Link to="/receipts" className="text-[13px] font-semibold text-accent-ink hover:underline">راجع الإيصال في «الإيصالات»</Link></div>
          ) : null}
        </dl>
      ) : null}
    </li>
  );
}

const Detail = ({ term, children }: { term: string; children: ReactNode }) => (
  <div className="grid gap-0.5">
    <dt className="text-[12.5px] text-ink-faint">{term}</dt>
    <dd className="font-medium">{children}</dd>
  </div>
);

/** What can be done with a booking now: done / no show once it has started, the written opinion after, cancel while it is live. */
function BookingActions({ a, now, canOpinion, marking, cancelling, onMark, onCancel }: {
  a: Appointment; now: Date; canOpinion: boolean; marking: boolean; cancelling: boolean; onMark: (state: string) => void; onCancel: () => void;
}) {
  if (a.state === "completed") return canOpinion ? <Opinion id={a.id} /> : null;
  if (!isLive(a)) return null;
  return (
    <>
      {a.state === "confirmed" && new Date(a.start) <= now ? (
        <>
          <Button size="sm" busy={marking} onClick={() => onMark("completed")}><Icon name="check" className="size-4" />خلص</Button>
          <Button size="sm" variant="ghost" disabled={marking} onClick={() => onMark("no_show")}>ماحضرش</Button>
        </>
      ) : null}
      <Button size="sm" variant="ghost" className="text-bad" busy={cancelling} onClick={onCancel}>إلغاء</Button>
    </>
  );
}

// ---------- the month ----------

function Calendar() {
  const today = cairoDate(new Date());
  const [month, setMonth] = useState(() => monthOf(today));
  const [picked, setPicked] = useState<string | null>(today);
  const days = useQuery({ queryKey: ["booking", "month", month], queryFn: () => booking.month(month) });
  const first = days.data?.[0];
  const lead = first ? WEEK.indexOf(new Date(`${first.day}T12:00:00Z`).getUTCDay()) : 0;
  const day = days.data?.find((d) => d.day === picked);

  return (
    <div className="grid items-start gap-4 lg:grid-cols-[minmax(0,1fr)_25rem]">
      <Card>
        <div className="flex items-center gap-2">
          <h2 className="tabular flex-1 text-[17px] font-semibold">{new Intl.DateTimeFormat("ar-EG-u-nu-latn", { month: "long", year: "numeric", timeZone: "UTC" }).format(new Date(`${month}-15T00:00:00Z`))}</h2>
          {month !== monthOf(today) ? <Button size="sm" onClick={() => { setMonth(monthOf(today)); setPicked(today); }}>النهارده</Button> : null}
          <Button size="sm" variant="ghost" aria-label="الشهر اللي فات" onClick={() => setMonth(shiftMonth(month, -1))}><Icon name="chevron-start" className="size-5" /></Button>
          <Button size="sm" variant="ghost" aria-label="الشهر الجاي" onClick={() => setMonth(shiftMonth(month, 1))}><Icon name="chevron-end" className="size-5" /></Button>
        </div>
        {days.isPending ? <Loading /> : days.isError ? <ErrorState message={errorMessage(days.error)} onRetry={() => void days.refetch()} /> : (
          <div className="grid grid-cols-7 gap-1.5" role="grid" aria-label="أيام الشهر">
            {WEEK.map((w) => <span key={w} className="pb-1 text-center text-[12.5px] font-medium text-ink-faint">{DAY_NAMES[w]}</span>)}
            {Array.from({ length: lead }, (_, i) => <span key={`x${i}`} />)}
            {days.data.map((d) => {
              const off = d.status === "closed" || d.status === "weekly_off";
              const past = d.day < today;
              const on = d.day === picked;
              return (
                <button key={d.day} type="button" role="gridcell" aria-selected={on} onClick={() => setPicked(d.day)}
                  aria-label={`${d.day}: ${STATUS[d.status].word}${d.bookings ? `، ${d.bookings} حجز` : ""}`}
                  className={`grid min-h-[4.5rem] content-between gap-1 rounded-[var(--radius-control)] border p-2 text-start transition-colors ${
                    on ? "border-accent ring-2 ring-accent" : d.day === today ? "border-gold/60" : "border-line hover:border-ink-faint"} ${
                    d.status === "closed" ? "hatch bg-card" : off ? "bg-card-2" : "bg-card"} ${past && !on ? "opacity-50" : ""}`}>
                  <span className="flex items-center justify-between gap-1">
                    <span className={`tabular text-[15px] font-semibold ${d.day === today ? "text-gold-ink" : off ? "text-ink-faint" : ""}`}>{Number(d.day.slice(8))}</span>
                    {d.bookings ? <span className="tabular rounded-full bg-cobalt px-1.5 text-[11.5px] font-bold text-white">{d.bookings}</span> : null}
                  </span>
                  {CELL_NOTE[d.status] ? <span className={`truncate text-[11.5px] font-medium ${CELL_NOTE[d.status]}`}>{STATUS[d.status].word}</span> : <span aria-hidden />}
                </button>
              );
            })}
          </div>
        )}
        <p className="flex flex-wrap items-center gap-x-4 gap-y-1 border-t border-line pt-3 text-[12.5px] text-ink-soft">
          <span className="flex items-center gap-1.5"><span className="tabular rounded-full bg-cobalt px-1.5 text-[11px] font-bold text-white">2</span>عدد الحجوزات</span>
          <span className="flex items-center gap-1.5"><span className="hatch size-3 rounded-[3px] border border-line-strong" />مقفول</span>
          <span className="flex items-center gap-1.5"><span className="size-3 rounded-[3px] border border-line bg-card-2" />إجازة</span>
          <span>اختار يوم عشان تشوف حجوزاته أو تغيّر ساعاته.</span>
        </p>
      </Card>
      {/* keyed by the date: nothing typed or asked about one day carries over to another */}
      {day ? <DaySheet key={day.day} day={day} /> : <Card><Empty title="اختار يوم" icon="calendar">تشوف حجوزاته، أو تقفله، أو تقفل جزء منه، أو تديله ساعات مختلفة.</Empty></Card>}
    </div>
  );
}

/** One date: its hours, its bookings, its exceptions, changing it (asks first if it has bookings), and the preview. */
function DaySheet({ day }: { day: Day }) {
  const qc = useQueryClient();
  const [open, setOpen] = useState(false);
  const [kind, setKind] = useState<"closed_day" | "closed_hours" | "custom_hours">("closed_day");
  const [from, setFrom] = useState(600);
  const [to, setTo] = useState(840);
  const [reason, setReason] = useState("");
  // A change that would cancel bookings: the list, and the same change again with confirm.
  const [ask, setAsk] = useState<{ list: Appointment[]; again: () => void } | null>(null);
  const [done, setDone] = useState<Done | null>(null);
  const booked = useQuery({ queryKey: ["booking", "appointments", day.day, day.day], queryFn: () => booking.appointments(day.day, day.day), enabled: day.bookings > 0 });
  const finished = (r: Done) => { setAsk(null); setDone(r); setOpen(false); refresh(); };
  const asked = (e: Error, again: () => void) => {
    if (e instanceof ApiError && e.code === "has_bookings") setAsk({ list: e.data?.appointments as Appointment[], again });
  };
  const refresh = () => void qc.invalidateQueries({ queryKey: ["booking"] });
  const add = useMutation({
    mutationFn: (confirm: boolean) => booking.addException({ day: day.day, kind, ...(kind === "closed_day" ? {} : { start: from, end: to }), reason, confirm }),
    onSuccess: (r) => { finished(r); setReason(""); toast("اليوم اتعدّل، وحازم شايف التعديل على طول"); },
    onError: (e) => asked(e, () => add.mutate(true)),
  });
  const remove = useMutation({
    mutationFn: ({ id, confirm }: { id: string; confirm: boolean }) => booking.removeException(id, confirm),
    onSuccess: (r) => { finished(r); toast("التعديل اتشال"); },
    onError: (e, v) => asked(e, () => remove.mutate({ id: v.id, confirm: true })),
  });
  const live = (booked.data ?? []).filter((a) => a.state === "confirmed" || a.state === "pending_payment" || a.state === "completed").sort((a, b) => a.start.localeCompare(b.start));

  return (
    <Card>
      <h2 className="flex flex-wrap items-center gap-2 text-[17px] font-semibold">{longDay(day.day)} <Chip tone={STATUS[day.status].tone}>{STATUS[day.status].word}</Chip></h2>

      <Part title="حازم بيحجز">
        {day.hours.length === 0 ? <p className="text-[14px] text-ink-soft">مفيش حجز اليوم ده.</p> : (
          <ul className="flex flex-wrap gap-1.5">{day.hours.map((h) => <li key={h.start} className="tabular rounded-[8px] bg-cobalt-bg px-2.5 py-1 text-[13.5px] font-semibold text-cobalt-ink">{range(h)}</li>)}</ul>
        )}
      </Part>

      <Part title={`الحجوزات${day.bookings ? ` (${day.bookings})` : ""}`}>
        {day.bookings === 0 ? <p className="text-[14px] text-ink-soft">مفيش حجوزات اليوم ده.</p> : booked.isPending ? <Loading /> : (
          <ul className="grid divide-y divide-line">
            {live.map((a) => (
              <li key={a.id} className="flex items-center gap-3 py-2 text-[14px]">
                <span className={`display w-14 shrink-0 font-semibold ${a.service === "consultation_urgent" ? "text-red-ink" : "text-cobalt-ink"}`}>{cairoTime(new Date(a.start))}</span>
                <span className="grid min-w-0 flex-1 leading-snug">
                  <span className="truncate font-medium">{a.name ?? "من غير اسم"}</span>
                  <span className="truncate text-[12.5px] text-ink-soft">{label(BOOKING_SERVICES, a.service)}</span>
                </span>
                <Chip tone={STATE_TONE[a.state] ?? "grey"}>{label(APPOINTMENT_STATES, a.state)}</Chip>
              </li>
            ))}
          </ul>
        )}
      </Part>

      {day.exceptions.length > 0 ? (
        <Part title="تعديلات اليوم ده">
          <ul className="grid gap-1.5">
            {day.exceptions.map((e) => (
              <li key={e.id} className="flex items-center gap-2 rounded-[10px] border border-line px-3 py-1.5 text-[14px]">
                <span className="grid min-w-0 flex-1 leading-snug">
                  <span className="font-medium">{e.kind === "closed_day" ? "اليوم مقفول" : `${e.kind === "closed_hours" ? "مقفول" : "ساعات"} ${range({ start: e.start!, end: e.end! })}`}</span>
                  {e.reason || e.by ? <span className="truncate text-[12.5px] text-ink-soft">{[e.reason, e.by].filter(Boolean).join(" - ")}</span> : null}
                </span>
                <Button size="sm" variant="ghost" busy={remove.isPending && remove.variables?.id === e.id} onClick={() => remove.mutate({ id: e.id, confirm: false })}><Icon name="x" className="size-4" />شيل</Button>
              </li>
            ))}
          </ul>
        </Part>
      ) : null}

      {open ? (
        <fieldset className="grid gap-3 rounded-[12px] border border-line bg-card-2 p-3">
          <legend className="sr-only">غيّر اليوم ده</legend>
          <Segmented label="نوع التغيير" value={kind} onChange={setKind}
            options={[["closed_day", "اقفل اليوم كله"], ["closed_hours", "اقفل جزء منه"], ["custom_hours", "ساعات مختلفة"]] as const} />
          {kind !== "closed_day" ? (
            <div className="flex items-center gap-2 text-[14px]">
              من <TimeSelect value={from} onChange={setFrom} label="من" /> لـ <TimeSelect value={to} onChange={setTo} label="لـ" />
            </div>
          ) : null}
          <Field label="السبب (للمكتب بس)" value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} />
          <div className="flex gap-2">
            <Button variant="primary" busy={add.isPending && !ask} onClick={() => add.mutate(false)}>احفظ التغيير</Button>
            <Button variant="ghost" onClick={() => { setOpen(false); setAsk(null); }}>لأ</Button>
          </div>
        </fieldset>
      ) : (
        <div><Button onClick={() => setOpen(true)}><Icon name="calendar" className="size-4" />غيّر ساعات اليوم ده</Button></div>
      )}
      {ask ? (
        <Confirm list={ask.list} busy={add.isPending || remove.isPending} onYes={ask.again} onNo={() => setAsk(null)} />
      ) : add.isError || remove.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(add.error ?? remove.error)}</p> : null}
      {done && done.cancelled > 0 ? <Told n={done.cancelled} /> : null}
      <PreviewOf day={day.day} />
    </Card>
  );
}

/** A small titled part of the day sheet. */
const Part = ({ title, children }: { title: string; children: ReactNode }) => (
  <div className="grid gap-2 border-t border-line pt-3">
    <h3 className="text-[13.5px] font-semibold text-ink-soft">{title}</h3>
    {children}
  </div>
);

/** The clients are told by Hazem within seconds (the worker); one he cannot reach becomes a task. */
const Told = ({ n }: { n: number }) => (
  <p className="text-sm" role="status">اتلغى {n} حجز، وحازم هيبلّغ العملاء خلال ثواني (اللي ماتوصلوش الرسالة بتتعمله مهمة).</p>
);

/** «الحجوزات دي هتتلغي»: the office cancels and Hazem tells each client (B-22: another time or a full refund). */
function Confirm({ list, busy, onYes, onNo }: { list: Appointment[]; busy: boolean; onYes: () => void; onNo: () => void }) {
  return (
    <div className="sheet-in grid gap-2 rounded-[12px] border border-saffron/40 bg-saffron-bg p-4 text-[14px]" role="alertdialog" aria-label="فيه حجوزات">
      <p className="font-semibold text-saffron-ink">التعديل ده فيه {list.length} حجز (مؤكد أو مستني الإيصال). لو كمّلت، الحجوزات دي هتتلغي وحازم هيبعت لكل عميل يختار ميعاد تاني، أو استرداد كامل لو كان دفع.</p>
      <ul className="grid gap-1">{list.map((a) => <li key={a.id} className="rounded-[8px] bg-card px-3 py-1.5">{cairoWhen(a.start)} - {a.name ?? "من غير اسم"} - {label(BOOKING_SERVICES, a.service)}</li>)}</ul>
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
  const [all, setAll] = useState(false);
  const p = useQuery({ queryKey: ["booking", "preview", day, service], queryFn: () => booking.preview(day, service) });
  return (
    <div className="grid gap-2 border-t border-line pt-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-[13.5px] font-semibold text-ink-soft">حازم هيعرض على العميل</h3>
        <select aria-label="الخدمة" className={`${inputClass} !min-h-9 !w-auto text-[14px]`} value={service} onChange={(e) => setService(e.target.value)}>
          <option value="pv">{BOOKING_SERVICES.consultation_phone_video}</option>
          <option value="of">{BOOKING_SERVICES.consultation_office}</option>
          <option value="ur">{BOOKING_SERVICES.consultation_urgent}</option>
        </select>
      </div>
      {p.isPending ? <Loading /> : p.isError ? <ErrorState message={errorMessage(p.error)} /> : (
        <>
          {p.data.calendar !== "ok" ? <p className="text-[12.5px] text-orange-ink">كالندر الأستاذ {p.data.calendar === "not_configured" ? "مش متوصل" : "مش بيرد"}: دي ساعات اللوحة بس، وحازم مش هيعرض مواعيد لحد ما يتوصل.</p> : null}
          {p.data.slots.length === 0 ? <p className="text-sm text-ink-soft">مفيش مواعيد فاضية اليوم ده.</p> : (
            <div className="grid justify-items-start gap-2">
              <ul className="flex flex-wrap gap-1.5">{(all ? p.data.slots : p.data.slots.slice(0, 10)).map((s) => <li key={s} className="tabular rounded-[8px] border border-line px-2.5 py-1 text-[13px] font-semibold">{cairoTime(new Date(s))}</li>)}</ul>
              {p.data.slots.length > 10 ? (
                <button type="button" onClick={() => setAll(!all)} className="text-[13px] font-semibold text-accent-ink hover:underline">
                  {all ? "اعرض أقل" : `و ${p.data.slots.length - 10} ميعاد كمان`}
                </button>
              ) : null}
            </div>
          )}
        </>
      )}
    </div>
  );
}

function TimeSelect({ value, onChange, label: aria, min = 0 }: { value: number; onChange: (m: number) => void; label: string; min?: number }) {
  return (
    <select aria-label={aria} className={`${inputClass} tabular !w-auto px-2`} value={value} onChange={(e) => onChange(Number(e.target.value))}>
      {TIMES.filter((t) => t >= min).map((t) => <option key={t} value={t}>{t === 1440 ? "12 بالليل" : hhmm(t)}</option>)}
    </select>
  );
}

/** B-17 (TASK 10): «مطلوب رأي مكتوب» with its delivery day: Hazem asks the client for the 2,000. */
function Opinion({ id }: { id: string }) {
  const qc = useQueryClient();
  const [open, setOpen] = useState(false);
  const [due, setDue] = useState(cairoDate(new Date(Date.now() + 7 * 86_400_000)));
  const ask = useMutation({
    mutationFn: () => booking.opinion(id, due, "file"),
    onSuccess: () => { setOpen(false); void qc.invalidateQueries({ queryKey: ["services"] }); toast("طلب الرأي المكتوب اتبعت، وهو في «طلبات الخدمات»"); },
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
    onSuccess: () => { setDraft(null); setAffected(null); void qc.invalidateQueries({ queryKey: ["booking"] }); toast("الأسبوع اتحفظ"); },
    onError: (e) => { if (e instanceof ApiError && e.code === "has_bookings") setAffected(e.data?.appointments as Appointment[]); },
  });
  if (week.isPending) return <Loading />;
  if (week.isError || !days) return <ErrorState message={errorMessage(week.error)} onRetry={() => void week.refetch()} />;
  const edit = (w: number, ranges: WeekDay["ranges"]) => setDraft(days.map((d) => (d.weekday === w ? { ...d, ranges } : d)));
  const hoursOf = (d: WeekDay) => d.ranges.reduce((s, r) => s + Math.max(0, r.end - r.start), 0);
  const total = days.reduce((s, d) => s + hoursOf(d), 0);
  const working = days.filter((d) => d.ranges.length > 0).length;

  return (
    <Card>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <p className="max-w-xl text-sm text-ink-soft">دي المواعيد اللي حازم بيحجز فيها كل أسبوع. اليوم من غير فترات = إجازة. أي يوم معين بيتغيّر من «التقويم».</p>
        <p className="flex gap-4 text-[13.5px] text-ink-soft">
          <span><strong className="display font-semibold text-cobalt-ink">{span(total)}</strong> في الأسبوع</span>
          <span><strong className="display font-semibold text-ink">{working}</strong> أيام شغل</span>
        </p>
      </div>
      {draft ? <p role="status" className="rounded-[10px] bg-gold-bg px-3 py-2 text-[13.5px] font-medium text-gold-ink">فيه تعديلات لسه ما اتحفظتش.</p> : null}
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
              <Button size="sm" variant="ghost" onClick={() => edit(w, [...d.ranges, { start: d.ranges.at(-1)?.end ?? 600, end: 1440 }])}><Icon name="plus" className="size-4" />فترة</Button>
              {d.ranges.length ? <span className="ms-auto text-[12.5px] text-ink-faint">{span(hoursOf(d))}</span> : null}
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

const Row = ({ children }: { children: ReactNode }) => <span className="flex items-center gap-1 rounded-[10px] bg-cobalt-bg px-1.5 py-1">{children}</span>;

// ---------- the settings and the office's details ----------

function SettingsForm() {
  const qc = useQueryClient();
  const { data: me } = useMe();
  const s = useQuery({ queryKey: ["booking", "settings"], queryFn: booking.settings });
  const [draft, setDraft] = useState<Settings | null>(null);
  const v = draft ?? s.data;
  const save = useMutation({ mutationFn: booking.saveSettings, onSuccess: () => { setDraft(null); void qc.invalidateQueries({ queryKey: ["booking"] }); toast("إعدادات الحجز اتحفظت"); } });
  if (s.isPending) return <Loading />;
  if (s.isError || !v) return <ErrorState message={errorMessage(s.error)} onRetry={() => void s.refetch()} />;
  const num = (k: keyof Settings) => (e: { target: { value: string } }) => setDraft({ ...v, [k]: e.target.value === "" ? null : Number(e.target.value) });

  return (
    <Card>
      <label className="flex items-center justify-between gap-3 rounded-[12px] bg-card-2 px-4 py-3">
        <span className="grid leading-snug"><span className="font-semibold">حازم يحجز من واتساب</span><span className="text-[13px] text-ink-soft">لما يتقفل، حازم بيعرض الاستشارة من غير مواعيد.</span></span>
        <input type="checkbox" className="size-5" checked={v.enabled} onChange={(e) => setDraft({ ...v, enabled: e.target.checked })} />
      </label>
      <div className="grid gap-3 md:grid-cols-2">
        <Field label="الوقت بين كل ميعاد والتاني (دقيقة)" type="number" min={0} max={240} value={v.buffer_minutes} onChange={num("buffer_minutes")} />
        <Field label="أقل مدة قبل الحجز (ساعة)" hint="ما بتنطبقش على العاجل." type="number" min={0} max={168} value={v.min_notice_hours} onChange={num("min_notice_hours")} />
        <Field label="أبعد يوم ينفع يتحجز (يوم من النهارده)" type="number" min={1} max={180} value={v.horizon_days} onChange={num("horizon_days")} />
        <Field label="أقصى عدد مواعيد في اليوم" hint="فاضي = من غير حد." type="number" min={1} max={100} value={v.max_per_day ?? ""} onChange={num("max_per_day")} />
        <label className="grid gap-1.5 text-sm font-semibold">
          الميعاد بيبدأ كل
          <select className={`${inputClass} font-normal`} value={v.slot_step_minutes} onChange={num("slot_step_minutes")}>
            {[5, 10, 15, 20, 30, 60].map((m) => <option key={m} value={m}>{m} دقيقة</option>)}
          </select>
        </label>
        <Field label="عدد المواعيد اللي حازم يعرضها مرة واحدة" type="number" min={1} max={9} value={v.offer_count} onChange={num("offer_count")} />
        {/* every appointment goes to this calendar (names and numbers): only the manager and the lawyer change it */}
        <Field label="إيميل كالندر الأستاذ (Google)" dir="ltr" value={v.calendar_id} readOnly={!can(me, "office.manage")}
          onChange={(e) => setDraft({ ...v, calendar_id: e.target.value })}
          hint={`${v.calendar_key ? "مفتاح Google موجود." : "مفتاح Google لسه مش متحط (صاحب المشروع)."}${can(me, "office.manage") ? "" : " بيغيّره المدير أو الأستاذ بس."}`} />
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
  const save = useMutation({ mutationFn: booking.saveOffice, onSuccess: () => { setDraft(null); void qc.invalidateQueries({ queryKey: ["booking"] }); toast("بيانات المكتب اتحفظت"); } });
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
