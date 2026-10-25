import { useEffect, useState, type ReactNode } from "react";
import { FirmLogo } from "../../shared/ui/Brand";
import { Field } from "../../shared/ui/ui";

/** The pages before a session (sign-in, sign-up, a forgotten password): the office's name and one card. */
export function AuthLayout({ title, subtitle, children }: { title: string; subtitle?: ReactNode; children: ReactNode }) {
  return (
    <main className="grid min-h-dvh place-items-center px-4 py-10">
      <div className="backdrop" aria-hidden />
      <div className="relative z-[1] grid w-full max-w-[25rem] gap-6">
        <div className="flex items-center gap-3">
          <FirmLogo className="size-16" />
          <div className="grid leading-tight">
            <p className="text-[17px] font-semibold">مكتب سعد فتحي سعد</p>
            <p className="text-[13.5px] text-ink-soft">للمحاماة والاستشارات القانونية</p>
          </div>
        </div>
        <div className="surface grid gap-5 rounded-[var(--radius-sheet)] p-6 sm:p-7">
          <div className="grid gap-1">
            <h1 className="text-[22px]">{title}</h1>
            {subtitle ? <p className="text-[14px] text-ink-soft">{subtitle}</p> : null}
          </div>
          {children}
        </div>
        <p className="text-center text-[12.5px] text-ink-faint">لوحة حازم · للعاملين في المكتب بس</p>
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
    <p className={`rounded-[var(--radius-control)] border px-3 py-2 text-[14px] ${tone === "bad" ? "border-red-ink/20 bg-bad-bg text-bad" : "border-green-ink/20 bg-ok-bg text-ok"}`}
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
