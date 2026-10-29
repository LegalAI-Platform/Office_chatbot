// One shape for every record the office works on (a receipt, a service order, an Urfi case), idea from PR #14 in
// the panel's own colors: a color edge for its state, the person and what it is, the facts in small boxes, the
// client's files, notes, and the actions in a band at the bottom. Learn one page and the others read the same.
import { Children, type ReactNode } from "react";
import { toast } from "./feedback";
import { Icon, type IconName } from "./Icon";
import type { Tone } from "./ui";

// Written out in full so the colors stay with the chips' tones.
const EDGE: Record<Tone, string> = {
  blue: "var(--blue)", orange: "var(--orange)", red: "var(--red)", green: "var(--green)", grey: "var(--line-strong)", gold: "var(--gold)",
};

export function CaseCard({ tone, label, children }: { tone: string; label?: string; children: ReactNode }) {
  return (
    <li>
      <article aria-label={label} className="surface relative grid overflow-hidden rounded-[var(--radius-card)]">
        <span className="absolute inset-y-0 start-0 w-1" style={{ background: EDGE[(tone in EDGE ? tone : "grey") as Tone] }} aria-hidden />
        {children}
      </article>
    </li>
  );
}

/** The person, what the record is (a tinted word), the small facts under it, and the state on the end side. */
export function CaseHead({ title, kind, meta, status }: { title: string; kind?: string; meta: ReactNode[]; status: ReactNode }) {
  return (
    <header className="flex flex-wrap items-start gap-x-4 gap-y-2 px-5 pt-4">
      <div className="grid min-w-0 flex-1 gap-1">
        <h3 className="flex flex-wrap items-center gap-x-2.5 gap-y-1 text-[17px] font-semibold">
          {title}
          {kind ? <span className="rounded-[7px] bg-gold-tint px-2 py-0.5 text-[13px] font-semibold text-gold-ink">{kind}</span> : null}
        </h3>
        <p className="flex flex-wrap items-center gap-x-3 gap-y-1 text-[13.5px] text-ink-soft">
          {meta.filter(Boolean).map((m, i) => <span key={i} className="inline-flex items-center gap-1">{m}</span>)}
        </p>
      </div>
      <div className="flex flex-wrap items-center gap-1.5">{status}</div>
    </header>
  );
}

/** A meta item with its icon. */
export function Meta({ icon, children }: { icon: IconName; children: ReactNode }) {
  return <><Icon name={icon} className="size-3.5" />{children}</>;
}

async function copy(text: string) {
  try {
    await navigator.clipboard.writeText(text);
    toast("الرقم اتنسخ");
  } catch {
    toast("مش قادرين ننسخ: انسخه بإيدك", "bad");
  }
}

/** The client's WhatsApp number and «انسخ» next to it (idea from PR #14): the number is copied many times a day. */
export function Phone({ wa }: { wa: string }) {
  return (
    <span className="inline-flex items-center gap-1.5">
      <span dir="ltr" className="tabular">+{wa}</span>
      <button type="button" onClick={() => void copy(`+${wa}`)} aria-label={`انسخ الرقم +${wa}`}
        className="rounded px-1 text-[12.5px] font-semibold text-gold-ink hover:underline">انسخ</button>
    </span>
  );
}

/** The record's facts, each in its own small box. */
export function Facts({ items }: { items: [string, ReactNode][] }) {
  return (
    <dl className="grid grid-cols-2 gap-2 px-5 pt-3 sm:grid-cols-3 lg:grid-cols-4">
      {items.map(([k, v]) => (
        <div key={k} className="grid gap-0.5 rounded-[10px] bg-card-2 px-3.5 py-2.5">
          <dt className="text-[12.5px] text-ink-soft">{k}</dt>
          <dd className="text-[15px] font-semibold">{v}</dd>
        </div>
      ))}
    </dl>
  );
}

/** The client's files (opened by the API for whoever may see them; every opening is recorded). */
export function Files({ label, count, href, word, icon = "document" }: {
  label: string; count: number; href: (i: number) => string; word: string; icon?: IconName;
}) {
  if (count === 0) return null;
  return (
    <ul className="flex flex-wrap gap-2 px-5 pt-3" aria-label={label}>
      {Array.from({ length: count }, (_, i) => (
        <li key={i}>
          <a href={href(i)} target="_blank" rel="noopener noreferrer"
            className="inline-flex min-h-10 items-center gap-1.5 rounded-[var(--radius-control)] border border-line px-3 text-sm hover:border-gold-line">
            <Icon name={icon} className="size-4" />{word}{count > 1 ? ` ${i + 1}` : ""}
          </a>
        </li>
      ))}
    </ul>
  );
}

/** A line inside the card: what happens next, or (bad) that a message did not reach the client. */
export function CaseNote({ tone = "info", children }: { tone?: "info" | "bad"; children: ReactNode }) {
  return (
    <p className={`mx-5 mt-3 flex items-start gap-2 rounded-[10px] px-3.5 py-2 text-sm ${tone === "bad" ? "bg-red-bg text-red-ink" : "bg-card-2 text-ink-soft"}`}
      role={tone === "bad" ? "status" : undefined}>
      <Icon name={tone === "bad" ? "alert-circle" : "info"} className="mt-0.5 size-4 shrink-0" />
      <span>{children}</span>
    </p>
  );
}

/** The card's actions, in a quiet band at the bottom; with none the card just keeps its bottom padding. */
export function ActionBar({ children }: { children?: ReactNode }) {
  if (Children.toArray(children).length === 0) return <div className="h-4" />;
  return <footer className="mt-4 grid gap-3 border-t border-line bg-card-2/60 px-5 py-3.5">{children}</footer>;
}
