import { useEffect, useId, useRef, type KeyboardEvent, type ReactNode } from "react";
import { queryOptions } from "@tanstack/react-query";
import { BOOKING_SERVICES, label } from "../../shared/lib/labels";
import { cairoWhen } from "../../shared/lib/time";
import { Icon, type IconName } from "../../shared/ui/Icon";
import { Button } from "../../shared/ui/ui";
import { booking, hhmm, TIMES, type Appointment, type Day } from "./api";

// The pieces both booking pages (the day and its settings) are built from.

// The office week starts on Saturday; the API's weekday is 0 = Sunday … 6 = Saturday.
export const WEEK = [6, 0, 1, 2, 3, 4, 5];
export const DAY_NAMES = ["الأحد", "الاتنين", "التلات", "الأربع", "الخميس", "الجمعة", "السبت"];
export const STATUS: Record<Day["status"], { word: string; tone: string }> = {
  open: { word: "شغال", tone: "green" },
  weekly_off: { word: "إجازة", tone: "grey" },
  closed: { word: "مقفول", tone: "red" },
  custom_hours: { word: "ساعات مختلفة", tone: "blue" },
  partly_closed: { word: "جزء مقفول", tone: "orange" },
};
export const longDay = (day: string) =>
  new Intl.DateTimeFormat("ar-EG-u-nu-latn", { weekday: "long", day: "numeric", month: "long", timeZone: "UTC" }).format(new Date(`${day}T12:00:00Z`));

export const monthQuery = (month: string) => queryOptions({ queryKey: ["booking", "month", month], queryFn: () => booking.month(month) });
export const settingsQuery = queryOptions({ queryKey: ["booking", "settings"], queryFn: booking.settings });

export const ROW = "relative overflow-hidden rounded-[14px] border border-line/60 bg-card/55 shadow-[inset_0_1px_0_rgb(255_255_255/0.45),0_4px_14px_-8px_rgb(60_44_20/0.25)]";
export const SELECT = "min-h-10 rounded-[12px] border border-line-strong bg-card/80 px-2 text-ink";
export const GOLD_ON = "btn-gold font-bold shadow-[inset_0_1px_0_rgb(255_255_255/0.4),0_6px_14px_-6px_rgb(140_98_58/0.8)]";

/** The soft light the glass shows through. */
export const Glow = () => (
  <div aria-hidden className="pointer-events-none absolute inset-x-0 -top-6 -z-10 h-[30rem] overflow-hidden">
    <span className="absolute -top-16 end-[4%] size-[24rem] rounded-full bg-gold/30 blur-[100px]" />
    <span className="absolute top-24 start-[10%] size-[20rem] rounded-full bg-blue/10 blur-[100px]" />
  </div>
);

/** The page's icon tile and title. */
export const TitleTile = ({ icon, children }: { icon: IconName; children: ReactNode }) => (
  <div className="flex items-center gap-3">
    <span aria-hidden className="grid size-11 shrink-0 place-items-center rounded-[14px] bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6),0_8px_20px_-10px_rgb(140_98_58/0.7)]">
      <Icon name={icon} className="size-6" />
    </span>
    <h1 className="text-[26px] leading-tight font-bold">{children}</h1>
  </div>
);

/** A glass card with an icon tile, a title and an optional line under it. */
export function Sheet({ icon, title, hint, aside, children, className = "" }: {
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
export function Segmented<T extends string>({ label: name, value, options, onChange, small = false }: {
  label: string; value: T; options: { value: T; label: string; icon: IconName }[]; onChange: (v: T) => void; small?: boolean;
}) {
  return (
    <div className="flex min-w-0 flex-wrap gap-1 rounded-[20px] border border-line/60 bg-card/45 p-1 shadow-[inset_0_1px_2px_rgb(0_0_0/0.06)]" role="group" aria-label={name}>
      {options.map((o) => {
        const on = value === o.value;
        return (
          <button key={o.value} type="button" aria-pressed={on} onClick={() => onChange(o.value)}
            className={`flex grow items-center justify-center gap-1.5 whitespace-nowrap rounded-[16px] transition motion-reduce:transition-none ${small ? "min-h-8 px-2.5 text-[12.5px]" : "min-h-9 px-3.5 text-[13.5px]"} ${on
              ? GOLD_ON
              : "text-ink hover:bg-card/80"}`}>
            <Icon name={o.icon} className={small ? "size-3.5" : "size-4"} />
            {o.label}
          </button>
        );
      })}
    </div>
  );
}

/** The save row at the bottom of a form: «not saved yet» while there is a draft, then the buttons. */
export function SaveBar({ dirty = false, children }: { dirty?: boolean; children: ReactNode }) {
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

export function TimeSelect({ value, onChange, label: aria, min = 0 }: { value: number; onChange: (m: number) => void; label: string; min?: number }) {
  return (
    <select aria-label={aria} className={`${SELECT} tabular`} value={value} onChange={(e) => onChange(Number(e.target.value))}>
      {TIMES.filter((t) => t >= min).map((t) => <option key={t} value={t}>{t === 1440 ? "12 بالليل" : hhmm(t)}</option>)}
    </select>
  );
}

/** «الحجوزات دي هتتلغي»: the office cancels and Hazem tells each client (B-22: another time or a full refund). */
export function Confirm({ list, busy, onYes, onNo }: { list: Appointment[]; busy: boolean; onYes: () => void; onNo: () => void }) {
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

/** The clients are told by Hazem within seconds (the worker); one he cannot reach becomes a task. */
export const Told = ({ n }: { n: number }) => (
  <p className="flex items-start gap-2 rounded-[14px] border border-green/30 bg-green-bg px-3.5 py-2.5 text-sm text-green-ink" role="status">
    <Icon name="check-circle" className="mt-0.5 size-4 shrink-0" />
    <span>اتلغى {n} حجز، وحازم هيبلّغ العملاء خلال ثواني (اللي ماتوصلوش الرسالة بتتعمله مهمة).</span>
  </p>
);

const FOCUSABLE = "button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex='-1'])";

/** A small window over the page: labelled by its title, closes with Escape or «اقفل», keeps the focus inside, and gives it back. */
export function Dialog({ title, onClose, children }: { title: ReactNode; onClose: () => void; children: ReactNode }) {
  const id = useId();
  const box = useRef<HTMLDivElement>(null);
  const close = useRef(onClose);
  close.current = onClose;
  useEffect(() => {
    const opener = document.activeElement as HTMLElement | null;
    box.current?.querySelector<HTMLElement>(FOCUSABLE)?.focus();
    return () => opener?.focus?.();
  }, []);
  const keys = (e: KeyboardEvent) => {
    if (e.key === "Escape") { e.stopPropagation(); close.current(); return; }
    if (e.key !== "Tab" || !box.current) return;
    const all = [...box.current.querySelectorAll<HTMLElement>(FOCUSABLE)];
    const first = all[0], last = all.at(-1);
    if (!first || !last) return;
    if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
    else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
  };
  return (
    <div className="fixed inset-0 z-[60] grid place-items-center bg-black/45 p-4" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose(); }}>
      <div ref={box} role="dialog" aria-modal="true" aria-labelledby={id} onKeyDown={keys}
        className="liquid menu-glass soft-scroll relative grid max-h-[calc(100dvh-2rem)] w-full max-w-lg content-start gap-4 overflow-y-auto rounded-[22px] p-4 shadow-2xl sm:p-5">
        <div className="flex items-start gap-3">
          <h2 id={id} className="flex min-w-0 flex-1 flex-wrap items-center gap-2 text-[17px] leading-snug font-bold">{title}</h2>
          <button type="button" aria-label="اقفل" onClick={onClose}
            className="grid size-9 shrink-0 place-items-center rounded-full border border-line/60 bg-card/60 text-ink transition hover:border-gold-line hover:bg-gold-tint hover:text-gold-ink motion-reduce:transition-none">
            <Icon name="x" className="size-4" />
          </button>
        </div>
        {children}
      </div>
    </div>
  );
}
