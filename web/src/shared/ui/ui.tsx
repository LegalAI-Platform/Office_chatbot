import type { ButtonHTMLAttributes, InputHTMLAttributes, ReactNode } from "react";
import { useId, useState } from "react";
import { Icon, type IconName } from "./Icon";

// ---------- buttons: one filled action per view, the rest quiet ----------

type Variant = "primary" | "dark" | "secondary" | "danger" | "ghost";
type Size = "sm" | "md";

const VARIANTS: Record<Variant, string> = {
  primary: "btn-primary",
  dark: "btn-primary",
  secondary: "border-line-strong bg-card text-ink hover:bg-card-2",
  danger: "border-red-ink/30 bg-card text-red-ink hover:bg-red-bg",
  ghost: "border-transparent text-ink-soft hover:bg-muted hover:text-ink",
};

const SIZES: Record<Size, string> = {
  sm: "min-h-9 px-3 text-[13.5px]",
  md: "min-h-10 px-4 text-[14.5px]",
};

export function Button({
  variant = "secondary",
  size = "md",
  busy = false,
  className = "",
  children,
  ...rest
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: Variant; size?: Size; busy?: boolean }) {
  return (
    <button
      type="button"
      {...rest}
      disabled={rest.disabled || busy}
      aria-busy={busy || undefined}
      className={`inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-[var(--radius-control)] border font-medium transition-colors disabled:opacity-45 ${SIZES[size]} ${VARIANTS[variant]} ${className}`}
    >
      {busy ? <span className="size-4 animate-spin rounded-full border-2 border-current border-t-transparent" aria-hidden /> : null}
      {children}
    </button>
  );
}

// ---------- form fields ----------

export const inputClass =
  "min-h-10 w-full rounded-[var(--radius-control)] border border-line-strong bg-card px-3 text-[15px] text-ink transition-colors placeholder:text-ink-faint hover:border-ink-faint focus:border-accent focus:outline-none focus:ring-3 focus:ring-accent-tint read-only:bg-card-2 read-only:text-ink-soft aria-[invalid=true]:border-red-ink";

export function Field({
  label,
  error,
  hint,
  className = "",
  ...input
}: InputHTMLAttributes<HTMLInputElement> & { label: string; error?: string | undefined; hint?: string | undefined }) {
  const id = useId();
  const describedBy = [error ? `${id}-error` : null, hint ? `${id}-hint` : null].filter(Boolean).join(" ") || undefined;
  return (
    <div className="grid gap-1.5">
      <label htmlFor={id} className="text-[13.5px] font-medium text-ink">
        {label}
        {input.required ? <span className="text-bad" aria-hidden> *</span> : null}
      </label>
      <input id={id} {...input} aria-invalid={error ? true : undefined} aria-describedby={describedBy} className={`${inputClass} ${className}`} />
      {hint ? (
        <p id={`${id}-hint`} className="text-[13px] text-ink-soft">
          {hint}
        </p>
      ) : null}
      {error ? (
        <p id={`${id}-error`} className="text-[13.5px] text-bad" role="alert">
          {error}
        </p>
      ) : null}
    </div>
  );
}

/** A file picker in the panel's own words (the browser's says "No file chosen" in English). */
export function FileInput({ label, name: ariaName, accept, onFile, hint, disabled = false }: {
  label: string; name?: string; accept?: string; onFile: (file: File | null) => void; hint?: string; disabled?: boolean;
}) {
  const id = useId();
  const [name, setName] = useState<string>();
  return (
    <div className="grid gap-1.5">
      <input
        id={id}
        type="file"
        accept={accept}
        disabled={disabled}
        className="peer sr-only"
        aria-label={ariaName ?? label}
        onChange={(e) => {
          const f = e.target.files?.[0] ?? null;
          setName(f?.name);
          onFile(f);
          e.target.value = ""; // the same file can be picked again
        }}
      />
      <label htmlFor={id} aria-hidden
        className="flex min-h-11 items-center gap-3 rounded-[var(--radius-control)] border border-dashed border-line-strong bg-card-2 px-3 text-[14.5px] transition-colors peer-focus-visible:outline-2 peer-focus-visible:outline-accent peer-disabled:opacity-50 hover:border-accent hover:bg-accent-tint">
        <Icon name="upload" className="size-5 shrink-0 text-accent-ink" />
        <span className="font-medium text-accent-ink">{label}</span>
        <span className="min-w-0 truncate text-ink-soft" dir="auto">{name ?? "مفيش ملف لسه"}</span>
      </label>
      {hint ? <p className="text-[13px] text-ink-soft">{hint}</p> : null}
    </div>
  );
}

// ---------- tabs and filters ----------

/** Sections of one page: an underlined row (role tablist). */
export function Tabs<T extends string>({ label, value, options, onChange }: {
  label: string; value: T; options: readonly (readonly [T, string])[]; onChange: (v: T) => void;
}) {
  return (
    <div role="tablist" aria-label={label} className="flex gap-1 overflow-x-auto overflow-y-hidden border-b border-line [scrollbar-width:none]">
      {options.map(([v, text]) => (
        <button key={v} type="button" role="tab" aria-selected={v === value} onClick={() => onChange(v)}
          className={`relative min-h-11 shrink-0 px-3.5 text-[15px] transition-colors after:absolute after:inset-x-2 after:bottom-0 after:h-0.5 after:rounded-full ${
            v === value ? "font-semibold text-ink after:bg-accent" : "text-ink-soft hover:text-ink"}`}>
          {text}
        </button>
      ))}
    </div>
  );
}

/** Filters on a list: one segmented control (aria-pressed), the chosen one raised. */
export function Segmented<T extends string>({ label, value, options, onChange }: {
  label: string; value: T; options: readonly (readonly [T, string])[]; onChange: (v: T) => void;
}) {
  return (
    <div role="group" aria-label={label} className="inline-flex w-fit max-w-full gap-0.5 justify-self-start overflow-x-auto rounded-[10px] border border-line bg-muted p-0.5 [scrollbar-width:none]">
      {options.map(([v, text]) => (
        <button key={v} type="button" aria-pressed={v === value} onClick={() => onChange(v)}
          className={`min-h-8 shrink-0 rounded-[8px] px-3 text-[13.5px] transition-colors ${
            v === value ? "bg-card font-semibold text-ink shadow-[var(--shadow)] ring-1 ring-line" : "text-ink-soft hover:text-ink"}`}>
          {text}
        </button>
      ))}
    </div>
  );
}

/** The top of every page: the title, one line of what it is for, the page's actions on the end side. */
export function PageHeader({ title, description, actions }: { title: string; description?: ReactNode; actions?: ReactNode; section?: string }) {
  return (
    <header className="flex flex-wrap items-end justify-between gap-x-6 gap-y-3">
      <div className="grid min-w-0 gap-1">
        <h1 className="text-[26px] leading-tight font-semibold">{title}</h1>
        {description ? <p className="max-w-[70ch] text-[14.5px] text-ink-soft">{description}</p> : null}
      </div>
      {actions ? <div className="flex flex-wrap items-center gap-2">{actions}</div> : null}
    </header>
  );
}

// ---------- status chips: a word on a soft tint, never color alone ----------

export type Tone = "blue" | "orange" | "red" | "green" | "grey" | "accent";

const TONES: Record<Tone, string> = {
  blue: "text-blue-ink bg-blue-bg",
  orange: "text-orange-ink bg-orange-bg",
  red: "text-red-ink bg-red-bg",
  green: "text-green-ink bg-green-bg",
  grey: "text-grey-ink bg-grey-bg",
  accent: "text-accent-ink bg-accent-tint",
};

/** Task states and the urgent mark. */
const STATE_TONE: Record<string, Tone> = {
  open: "blue",
  in_progress: "accent",
  overdue: "orange",
  done: "green",
  cancelled: "grey",
  urgent: "red",
};

export function Chip({ tone, children }: { tone: string; children: ReactNode }) {
  const t = (tone in TONES ? tone : STATE_TONE[tone] ?? "grey") as Tone;
  return (
    <span className={`inline-flex items-center gap-1.5 whitespace-nowrap rounded-full px-2.5 py-0.5 text-[12.5px] font-medium ${TONES[t]}`}>
      <span className="size-1.5 rounded-full bg-current" aria-hidden />
      {children}
    </span>
  );
}

// ---------- loading, empty and error states ----------

export function Loading({ label = "جاري التحميل" }: { label?: string }) {
  return (
    <div className="grid gap-2" role="status" aria-live="polite">
      <span className="sr-only">{label}</span>
      {[0, 1, 2].map((i) => (
        <div key={i} className="h-16 animate-pulse rounded-[var(--radius-card)] bg-muted" aria-hidden />
      ))}
    </div>
  );
}

/** Nothing to show: what the list is waiting for, said once, quietly. */
export function Empty({ title, children, icon = "inbox" }: { title: string; children?: ReactNode; icon?: IconName }) {
  return (
    <div className="grid justify-items-center gap-1.5 px-6 py-10 text-center">
      <span className="mb-1 grid size-10 place-items-center rounded-full bg-muted text-ink-soft">
        <Icon name={icon} className="size-5" />
      </span>
      <p className="font-medium">{title}</p>
      {children ? <div className="max-w-sm text-[13.5px] text-ink-soft">{children}</div> : null}
    </div>
  );
}

export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div className="flex flex-wrap items-center gap-3 rounded-[var(--radius-card)] border border-red-ink/25 bg-red-bg px-4 py-3" role="alert">
      <Icon name="alert-circle" className="size-5 shrink-0 text-bad" />
      <p className="flex-1 font-medium text-bad">{message}</p>
      {onRetry ? (
        <Button size="sm" onClick={onRetry}>
          <Icon name="refresh" className="size-4" />
          جرّب تاني
        </Button>
      ) : null}
    </div>
  );
}

/** A content card. */
export function Card({ children, className = "" }: { children: ReactNode; className?: string }) {
  return <section className={`surface grid content-start gap-3 rounded-[var(--radius-card)] p-4 lg:p-5 ${className}`}>{children}</section>;
}

/** A card with a header: the title on the start side, an action on the end side. */
export function Panel({ title, action, children, className = "" }: { title: string; action?: ReactNode; children: ReactNode; className?: string }) {
  const id = useId();
  return (
    <section aria-labelledby={id} className={`surface flex min-w-0 flex-col rounded-[var(--radius-card)] ${className}`}>
      <header className="flex min-h-13 items-center justify-between gap-2 border-b border-line px-4 py-2.5 lg:px-5">
        <h2 id={id} className="text-[15.5px] font-semibold">{title}</h2>
        {action}
      </header>
      <div className="flex min-w-0 flex-1 flex-col gap-3 p-4 lg:px-5">{children}</div>
    </section>
  );
}
