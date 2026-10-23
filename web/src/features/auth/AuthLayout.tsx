import { useEffect, useState, type ReactNode } from "react";
import { OwnerImg } from "../../shared/ui/Owner";
import { Field } from "../../shared/ui/ui";

/** The pages before a session (sign-in, sign-up, a forgotten password): the office's black panel and a card. */
export function AuthLayout({ title, subtitle, children }: { title: string; subtitle?: ReactNode; children: ReactNode }) {
  return (
    <main className="grid min-h-dvh lg:grid-cols-[minmax(0,1fr)_minmax(0,1.1fr)]">
      {/* the office's black panel with the seal (on a phone: a band over the form) */}
      <section className="relative grid place-items-center overflow-hidden bg-side px-6 py-10 text-center text-side-ink lg:min-h-dvh">
        <OwnerImg name="sidebar_art" className="fade-all pointer-events-none absolute bottom-0 left-0 w-56 opacity-80 lg:w-80" />
        <div className="relative grid justify-items-center gap-3">
          <OwnerImg name="logo_emblem" className="size-28 lg:size-36" />
          <p className="text-xl font-bold lg:text-2xl">مكتب سعد فتحي سعد</p>
          <p className="text-sm text-side-soft">للمحاماة والاستشارات القانونية</p>
        </div>
      </section>

      <div className="grid place-items-center px-4 py-10">
        <div className="surface grid w-full max-w-sm gap-5 rounded-[var(--radius-sheet)] p-6 lg:p-8">
          <div className="grid gap-1">
            <h1 className="text-2xl">{title}</h1>
            {subtitle ? <p className="text-sm text-ink-soft">{subtitle}</p> : null}
          </div>
          {children}
        </div>
      </div>
    </main>
  );
}

/** A six-digit code from an e-mail or the authenticator app. */
export function CodeField({ label, value, onChange, hint, allowRecovery = false }: {
  label: string; value: string; onChange: (v: string) => void; hint?: string; allowRecovery?: boolean;
}) {
  return (
    <Field
      label={label}
      name="code"
      dir="ltr"
      required
      autoComplete="one-time-code"
      inputMode={allowRecovery ? "text" : "numeric"}
      maxLength={allowRecovery ? 9 : 6}
      className="tabular text-center text-xl tracking-[0.4em]"
      value={value}
      hint={hint}
      onChange={(e) => onChange(allowRecovery ? e.target.value.trim().toLowerCase() : e.target.value.replace(/\D/g, ""))}
    />
  );
}

export function Notice({ tone = "bad", children }: { tone?: "bad" | "good"; children: ReactNode }) {
  return (
    <p className={`rounded-[var(--radius-control)] px-3 py-2 text-sm ${tone === "bad" ? "bg-bad-bg text-bad" : "bg-ok-bg text-ok"}`}
      role={tone === "bad" ? "alert" : "status"}>
      {children}
    </p>
  );
}

/** Seconds left before another code may be asked for (the server waits a minute too). */
export function useCountdown(seconds: number, startAt = seconds) {
  const [left, setLeft] = useState(startAt);
  useEffect(() => {
    if (left <= 0) return;
    const t = setTimeout(() => setLeft((s) => s - 1), 1000);
    return () => clearTimeout(t);
  }, [left]);
  return [left, () => setLeft(seconds)] as const;
}
