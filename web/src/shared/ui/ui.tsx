import type { ButtonHTMLAttributes, InputHTMLAttributes, ReactNode } from "react";
import { useId } from "react";
import { Icon } from "./Icon";

// ---------- buttons (the brand sheet: gold, black, outline) ----------

type Variant = "primary" | "dark" | "secondary" | "danger" | "ghost";

const VARIANTS: Record<Variant, string> = {
  primary: "btn-gold hover:brightness-105",
  dark: "bg-side text-side-ink border-side hover:opacity-90",
  secondary: "bg-card text-ink border-line-strong hover:border-gold-line",
  danger: "border-red-ink/40 text-red-ink bg-red-bg hover:opacity-90",
  ghost: "border-transparent text-ink hover:bg-muted",
};

export function Button({
  variant = "secondary",
  busy = false,
  className = "",
  children,
  ...rest
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: Variant; busy?: boolean }) {
  return (
    <button
      type="button"
      {...rest}
      disabled={rest.disabled || busy}
      aria-busy={busy || undefined}
      className={`inline-flex min-h-11 items-center justify-center gap-2 rounded-[var(--radius-control)] border px-5 font-semibold transition disabled:opacity-50 ${VARIANTS[variant]} ${className}`}
    >
      {busy ? <span className="size-4 animate-spin rounded-full border-2 border-current border-t-transparent" aria-hidden /> : null}
      {children}
    </button>
  );
}

// ---------- form fields ----------

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
      <label htmlFor={id} className="text-sm font-semibold">
        {label}
        {input.required ? <span className="text-bad"> *</span> : null}
      </label>
      <input
        id={id}
        {...input}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy}
        className={`min-h-11 rounded-[var(--radius-control)] border border-line-strong bg-card px-3.5 text-base text-ink placeholder:text-ink-soft focus:border-gold ${className}`}
      />
      {hint ? (
        <p id={`${id}-hint`} className="text-xs text-ink-soft">
          {hint}
        </p>
      ) : null}
      {error ? (
        <p id={`${id}-error`} className="text-sm text-bad" role="alert">
          {error}
        </p>
      ) : null}
    </div>
  );
}

// ---------- status chips (the sheet's «حالات البادجات»: a word on a soft tint, never color alone) ----------

export type Tone = "blue" | "orange" | "red" | "green" | "grey" | "gold";

const TONES: Record<Tone, string> = {
  blue: "text-blue-ink bg-blue-bg",
  orange: "text-orange-ink bg-orange-bg",
  red: "text-red-ink bg-red-bg",
  green: "text-green-ink bg-green-bg",
  grey: "text-grey-ink bg-grey-bg",
  gold: "text-gold-ink bg-gold-tint",
};

/** Task states and the urgent mark, in the sheet's colors. */
const STATE_TONE: Record<string, Tone> = {
  open: "blue",
  in_progress: "gold",
  overdue: "orange",
  done: "green",
  cancelled: "grey",
  urgent: "red",
};

export function Chip({ tone, children }: { tone: string; children: ReactNode }) {
  const t = (tone in TONES ? tone : STATE_TONE[tone] ?? "grey") as Tone;
  return (
    <span className={`inline-flex min-w-[4.25rem] items-center justify-center whitespace-nowrap rounded-[7px] px-3 py-1 text-[13px] font-semibold ${TONES[t]}`}>
      {children}
    </span>
  );
}

// ---------- loading, empty and error states ----------

export function Loading({ label = "جاري التحميل" }: { label?: string }) {
  return (
    <div className="grid gap-3" role="status" aria-live="polite">
      <span className="sr-only">{label}</span>
      {[0, 1, 2].map((i) => (
        <div key={i} className="surface h-20 animate-pulse rounded-[var(--radius-card)]" aria-hidden />
      ))}
    </div>
  );
}

export function Empty({ title, children, icon = "check-circle" }: { title: string; children?: ReactNode; icon?: Parameters<typeof Icon>[0]["name"] }) {
  return (
    <div className="grid justify-items-center gap-2 rounded-[var(--radius-card)] border border-dashed border-gold-line bg-card-2 px-6 py-8 text-center">
      <span className="grid size-11 place-items-center rounded-full bg-gold-tint text-gold-ink">
        <Icon name={icon} className="size-5" />
      </span>
      <p className="font-semibold">{title}</p>
      {children ? <div className="max-w-xs text-sm text-ink-soft">{children}</div> : null}
    </div>
  );
}

export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div className="grid justify-items-center gap-3 rounded-[var(--radius-card)] border border-red-ink/30 bg-red-bg px-6 py-8 text-center" role="alert">
      <p className="font-semibold text-bad">{message}</p>
      {onRetry ? (
        <Button onClick={onRetry}>
          <Icon name="refresh" className="size-4" />
          جرّب تاني
        </Button>
      ) : null}
    </div>
  );
}

/** A content card. */
export function Card({ children, className = "" }: { children: ReactNode; className?: string }) {
  return <section className={`surface grid gap-3 rounded-[var(--radius-card)] p-4 ${className}`}>{children}</section>;
}

/** A card with the sheet's header: a bold title on the start side, an action on the end side. */
export function Panel({ title, action, children, className = "" }: { title: string; action?: ReactNode; children: ReactNode; className?: string }) {
  const id = useId();
  return (
    <section aria-labelledby={id} className={`surface flex min-w-0 flex-col gap-3 rounded-[var(--radius-card)] p-4 ${className}`}>
      <header className="flex items-center justify-between gap-2">
        <h2 id={id} className="text-base font-bold">{title}</h2>
        {action}
      </header>
      {children}
    </section>
  );
}

/** Initials in a gold-ringed circle, or an account's photo: shared/ui/Avatar.tsx. */
export { Avatar } from "./Avatar";
