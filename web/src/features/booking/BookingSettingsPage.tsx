import { useState, type ReactNode } from "react";
import { Link } from "react-router";
import { useMutation, useQuery, useQueryClient, type QueryClient } from "@tanstack/react-query";
import { ApiError, errorMessage } from "../../shared/api/client";
import { can, useMe } from "../../shared/api/session";
import { cairoWhen } from "../../shared/lib/time";
import { useUrlState } from "../../shared/lib/url";
import { Icon, type IconName } from "../../shared/ui/Icon";
import { confirmAction, toast } from "../../shared/ui/feedback";
import { Button, Chip, ErrorState, Field, Loading } from "../../shared/ui/ui";
import { booking, type Appointment, type Office, type Settings, type WeekDay } from "./api";
import { Confirm, DAY_NAMES, GOLD_ON, Glow, ROW, SaveBar, SELECT, settingsQuery, Sheet, TimeSelect, TitleTile, WEEK } from "./parts";

const weekQuery = { queryKey: ["booking", "week"], queryFn: booking.week };

/** The section the page opens on (the week) and the settings, fetched while the pointer is still on the link. */
export function prefetchData(qc: QueryClient) {
  void qc.prefetchQuery(weekQuery);
  void qc.prefetchQuery(settingsQuery);
}

type Section = "hours" | "booking" | "office";
const SECTIONS: { value: Section; label: string; icon: IconName; needsOffice?: boolean }[] = [
  { value: "hours", label: "ساعات الشغل", icon: "clock" },
  { value: "booking", label: "الحجز من واتساب", icon: "whatsapp" },
  { value: "office", label: "بيانات المكتب والدفع", icon: "office", needsOffice: true },
];

/**
 * «إعدادات المواعيد»: what is set once and rarely touched, away from the daily screen. The week's hours, how Hazem
 * books from WhatsApp, and (manager / lawyer) the office's details, each picked from the menu at the side.
 */
export function BookingSettingsPage() {
  const { data: me } = useMe();
  const sections = SECTIONS.filter((s) => !s.needsOffice || can(me, "office.manage"));
  const [section, setSection] = useUrlState<Section>("section", "hours", sections.map((s) => s.value));

  return (
    <div className="relative isolate grid gap-5 pb-4">
      <Glow />
      <header className="grid items-center gap-3 lg:grid-cols-[minmax(0,1fr)_auto]">
        <div className="grid gap-1.5">
          <Link to="/appointments" className="flex w-fit items-center gap-1.5 rounded-full px-1 text-[13.5px] font-semibold text-gold-ink transition hover:underline">
            <Icon name="chevron-start" className="size-4" />رجوع للمواعيد
          </Link>
          <TitleTile icon="settings">إعدادات المواعيد</TitleTile>
        </div>
        <Undo />
      </header>

      <div className="grid items-start gap-4 lg:grid-cols-[14rem_minmax(0,1fr)]">
        <nav className="liquid rounded-[22px] p-1.5 lg:sticky lg:top-4">
          <div role="tablist" aria-label="أقسام الإعدادات" aria-orientation="vertical" className="flex flex-wrap gap-1 lg:grid">
            {sections.map((s) => {
              const on = s.value === section;
              return (
                <button key={s.value} type="button" role="tab" aria-selected={on} onClick={() => setSection(s.value)}
                  className={`flex min-h-11 grow basis-[10rem] items-center gap-2 whitespace-nowrap rounded-[16px] px-3.5 text-[14px] transition motion-reduce:transition-none lg:basis-auto ${on ? GOLD_ON : "text-ink hover:bg-card/80"}`}>
                  <Icon name={s.icon} className="size-[18px] shrink-0" />
                  {s.label}
                </button>
              );
            })}
          </div>
        </nav>
        <div className="min-w-0" role="tabpanel" aria-label={sections.find((s) => s.value === section)?.label}>
          {section === "hours" ? <Week /> : section === "booking" ? <SettingsForm /> : <OfficeForm />}
        </div>
      </div>
    </div>
  );
}

// what each change in the record was (hazem.booking_changes.what)
const CHANGE_WORDS: Record<string, string> = {
  weekly: "ساعات الأسبوع", exception_add: "تعديل يوم", exception_remove: "شيل تعديل يوم", settings: "إعدادات الحجز", office: "بيانات المكتب",
};

/** Puts back the last change; when that would leave bookings outside the hours it asks first, like any change. */
function Undo() {
  const qc = useQueryClient();
  const changes = useQuery({ queryKey: ["booking", "changes"], queryFn: booking.changes });
  const [affected, setAffected] = useState<Appointment[] | null>(null);
  const undo = useMutation({
    mutationFn: (confirm: boolean) => booking.undo(confirm),
    onSuccess: () => { setAffected(null); void qc.invalidateQueries({ queryKey: ["booking"] }); toast("رجع زي ما كان ✅"); },
    onError: (e) => { if (e instanceof ApiError && e.code === "has_bookings") setAffected(e.data?.appointments as Appointment[]); },
  });
  const last = changes.data?.find((c) => !c.undone);
  if (!last) return null;
  const what = CHANGE_WORDS[last.what] ?? last.what;
  return (
    <div className="grid gap-2">
      <span className="flex flex-wrap items-center gap-2 rounded-[16px] border border-line/60 bg-card/55 py-1 ps-3.5 pe-1 text-[13px] text-ink-soft shadow-[inset_0_1px_0_rgb(255_255_255/0.45)]">
        <Icon name="clock" className="size-4 shrink-0 text-gold-ink" />
        آخر تعديل: {what} · {last.by ?? "—"}، {cairoWhen(last.at)}
        <Button variant="ghost" className="!min-h-9 !px-3" busy={undo.isPending && !affected}
          onClick={async () => {
            if (await confirmAction({ title: `ترجّع «${what}» زي ما كان؟`, body: `التعديل ده عمله ${last.by ?? "—"}، ${cairoWhen(last.at)}.`, yes: "أيوه، رجّعه" })) undo.mutate(false);
          }}>
          <Icon name="back" className="size-4" />تراجع
        </Button>
        {undo.isError && !affected ? <span className="text-bad" role="alert">{errorMessage(undo.error)}</span> : null}
      </span>
      {affected ? <Confirm list={affected} busy={undo.isPending} onYes={() => undo.mutate(true)} onNo={() => { setAffected(null); undo.reset(); }} /> : null}
    </div>
  );
}

// ---------- ساعات الشغل: the default week ----------

function Week() {
  const qc = useQueryClient();
  const week = useQuery(weekQuery);
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
      <Sheet icon="clock" title="ساعات الشغل كل أسبوع" hint="اليوم من غير فترات = إجازة."
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
                {/* a period that ends at midnight leaves nothing after it */}
                {(d.ranges.at(-1)?.end ?? 0) < 1440 ? (
                  <Button variant="ghost" className="!min-h-9 !px-3 text-gold-ink" onClick={() => edit(w, [...d.ranges, { start: d.ranges.at(-1)?.end ?? 600, end: 1440 }])}>+ فترة</Button>
                ) : null}
                {d.ranges.length ? <span className="ms-auto tabular rounded-full bg-card/70 px-2.5 py-0.5 text-xs text-ink-soft">{hoursWords(minutesIn(d))}</span> : null}
              </li>
            );
          })}
        </ul>
        {affected ? <Confirm list={affected} busy={save.isPending} onYes={() => save.mutate(true)} onNo={() => { setAffected(null); save.reset(); }} /> : null}
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

// ---------- الحجز من واتساب: the settings ----------

function SettingsForm() {
  const qc = useQueryClient();
  const { data: me } = useMe();
  // the lawyer's calendar receives every appointment: only office.manage points it elsewhere (calendar_id_forbidden)
  const mayCalendar = can(me, "office.manage");
  const s = useQuery(settingsQuery);
  const [draft, setDraft] = useState<Settings | null>(null);
  const v = draft ?? s.data;
  const save = useMutation({ mutationFn: (x: Settings) => booking.saveSettings(mayCalendar || !s.data ? x : { ...x, calendar_id: s.data.calendar_id }), onSuccess:() => { setDraft(null); void qc.invalidateQueries({ queryKey: ["booking"] }); toast("إعدادات الحجز اتحفظت ✅"); } });
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
              <Field label="إيميل كالندر المستشار (Google)" dir="ltr" value={v.calendar_id} readOnly={!mayCalendar}
                onChange={(e) => { if (mayCalendar) setDraft({ ...v, calendar_id: e.target.value }); }}
                hint={`${v.calendar_key ? "مفتاح Google موجود." : "مفتاح Google لسه مش متحط (صاحب المشروع)."}${mayCalendar ? "" : " بيغيّره المدير أو المستشار بس."}`} />
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

// ---------- بيانات المكتب والدفع ----------

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
