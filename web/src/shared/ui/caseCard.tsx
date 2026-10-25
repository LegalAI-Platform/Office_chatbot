// One shape for every record the office works on (a receipt, a service order, an Urfi case, a booking):
// a color edge for its state, the person and what it is, the facts in small boxes, the files, and an
// action bar. Learn one page and every other reads the same.
import type { ReactNode } from "react";
import { FOLDER, type Folder } from "./folders";
import { Icon, type IconName } from "./Icon";

export function CaseCard({ edge, children, label }: { edge: Folder | "grey"; children: ReactNode; label?: string }) {
  const color = edge === "grey" ? "var(--line-strong)" : FOLDER[edge].color;
  return (
    <li>
      <article aria-label={label} className="surface relative grid overflow-hidden rounded-[var(--radius-card)] transition-shadow hover:shadow-[var(--shadow-pop)]">
        <span className="absolute inset-y-0 start-0 w-1" style={{ background: color }} aria-hidden />
        {children}
      </article>
    </li>
  );
}

/** The person, what the record is (a tinted word), when and the number, and the state on the end side. */
export function CaseHead({ title, kind, kindFolder = "cobalt", meta, status }: {
  title: string; kind?: string; kindFolder?: Folder; meta: ReactNode[]; status: ReactNode;
}) {
  const f = FOLDER[kindFolder];
  return (
    <header className="flex flex-wrap items-start gap-x-4 gap-y-2 px-5 pt-4">
      <div className="grid min-w-0 flex-1 gap-1">
        <h3 className="flex flex-wrap items-center gap-x-2.5 gap-y-1 text-[17px] font-semibold">
          {title}
          {kind ? <span className={`rounded-[7px] px-2 py-0.5 text-[13px] font-semibold ${f.bg} ${f.ink}`}>{kind}</span> : null}
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
  return <><Icon name={icon} className="size-3.5 text-ink-faint" />{children}</>;
}

/** The record's facts, each in its own small box. */
export function Facts({ items }: { items: [string, ReactNode, string?][] }) {
  return (
    <dl className="grid grid-cols-2 gap-2 px-5 pt-3 sm:grid-cols-3 lg:grid-cols-4">
      {items.map(([k, v, ink]) => (
        <div key={k} className="grid gap-0.5 rounded-[10px] bg-card-2 px-3.5 py-2.5">
          <dt className="text-[12.5px] text-ink-soft">{k}</dt>
          <dd className={`text-[15.5px] font-semibold ${ink ?? ""}`}>{v}</dd>
        </div>
      ))}
    </dl>
  );
}

/** The client's files (opened by the API for whoever may see them; every opening is recorded). */
export function Files({ label, count, href, word = "مستند", icon = "file-text" }: {
  label: string; count: number; href: (i: number) => string; word?: string; icon?: IconName;
}) {
  if (count === 0) return null;
  return (
    <ul className="flex flex-wrap gap-2 px-5 pt-3" aria-label={label}>
      {Array.from({ length: count }, (_, i) => (
        <li key={i}>
          <a href={href(i)} target="_blank" rel="noopener noreferrer"
            className="inline-flex min-h-9 items-center gap-1.5 rounded-[8px] border border-line-strong bg-card px-3 text-[14px] font-medium transition-colors hover:border-accent hover:bg-accent-tint hover:text-accent-ink">
            <Icon name={icon} className="size-4" />{word}{count > 1 ? ` ${i + 1}` : ""}
          </a>
        </li>
      ))}
    </ul>
  );
}

/** A note inside the card (what happens next, or that a message did not reach the client). */
export function CaseNote({ tone = "info", children }: { tone?: "info" | "bad"; children: ReactNode }) {
  return (
    <p className={`mx-5 mt-3 flex items-start gap-2 rounded-[10px] px-3.5 py-2 text-[14px] ${tone === "bad" ? "bg-coral-bg text-coral-ink" : "bg-cobalt-bg text-cobalt-ink"}`}
      role={tone === "bad" ? "status" : undefined}>
      <Icon name={tone === "bad" ? "alert-circle" : "info"} className="mt-0.5 size-4 shrink-0" />
      <span>{children}</span>
    </p>
  );
}

/** The card's actions: a quiet band at the bottom; an empty band keeps the card's bottom padding. */
export function ActionBar({ children }: { children?: ReactNode }) {
  if (!children) return <div className="h-4" />;
  return <footer className="mt-4 grid gap-3 border-t border-line bg-card-2/60 px-5 py-3.5">{children}</footer>;
}

/** The page's counts by state, as a row of small tinted figures. */
export function Summary({ items }: { items: { label: string; value: number | undefined; folder: Folder }[] }) {
  return (
    <section aria-label="ملخص" className="grid grid-cols-1 gap-3 sm:grid-cols-3">
      {items.map((i) => (
        <div key={i.label} className="liquid-tint grid gap-0.5 rounded-[var(--radius-card)] px-4 py-3" style={{ ["--tint" as string]: FOLDER[i.folder].color }}>
          <span className={`display text-[28px] leading-tight font-bold ${FOLDER[i.folder].ink}`}>{i.value === undefined ? "…" : i.value.toLocaleString("en-US")}</span>
          <span className="text-[14px] font-semibold text-ink-soft">{i.label}</span>
        </div>
      ))}
    </section>
  );
}
