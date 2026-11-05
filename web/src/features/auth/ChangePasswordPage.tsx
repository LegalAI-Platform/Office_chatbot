import { useId, useState, type FormEvent, type InputHTMLAttributes, type ReactNode } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router";
import { api, errorMessage, resetCsrf } from "../../shared/api/client";
import { meKey, useMe, useLogout } from "../../shared/api/session";
import { Icon } from "../../shared/ui/Icon";
import { Button } from "../../shared/ui/ui";
import { AuthLayout, Notice } from "./AuthLayout";

export const MIN_PASSWORD = 12;

/**
 * First sign-in (a temporary password from the manager) or any time from «حسابي». In the sign-in's layout (the office's
 * black panel and a card). The first time it says what to do in two steps; every password can be shown while it is typed;
 * and what the new one still needs is a list that ticks itself.
 */
export function ChangePasswordPage() {
  const { data: me } = useMe();
  const qc = useQueryClient();
  const navigate = useNavigate();
  const logout = useLogout();
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [repeat, setRepeat] = useState("");
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const forced = !!me?.must_change_password;

  const longEnough = next.length >= MIN_PASSWORD;
  const same = next.length > 0 && repeat === next;
  const differs = next.length > 0 && next !== current;
  const ready = !!current && longEnough && same && differs;
  const first = me?.display_name.split(/\s+/)[0];

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!ready) return;
    setError(undefined);
    setBusy(true);
    try {
      await api<void>("POST", "/auth/change-password", { current_password: current, new_password: next });
      // The server signs every other session out; this one gets a new cookie and token.
      resetCsrf();
      await qc.invalidateQueries({ queryKey: meKey });
      navigate("/", { replace: true });
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <AuthLayout title="تغيير كلمة السر">
      {forced ? (
        <div className="grid gap-2.5 rounded-[16px] border border-gold-line/70 bg-gold-tint/60 px-4 py-3 text-sm">
          <p className="flex items-center gap-2 font-bold text-gold-ink">
            <Icon name="key" className="size-4 shrink-0" />
            {first ? `أهلًا يا ${first} 👋` : "أهلًا بحضرتك 👋"}
          </p>
          <p>دي أول مرة تدخل، وكلمة السر اللي دخلت بيها مؤقتة من المدير. خطوتين بس وتكمّل:</p>
          <ol className="grid gap-1 ps-1">
            <Step n={1}>اكتب كلمة السر المؤقتة اللي المدير ادّاهالك.</Step>
            <Step n={2}>اختار كلمة سر جديدة بتاعتك (محدش غيرك هيعرفها).</Step>
          </ol>
        </div>
      ) : null}
      <form onSubmit={submit} className="grid gap-4" noValidate>
        <PasswordField label={forced ? "كلمة السر المؤقتة" : "كلمة السر الحالية"} autoComplete="current-password"
          value={current} onChange={(e) => setCurrent(e.target.value)} />
        <PasswordField label="كلمة السر الجديدة" autoComplete="new-password"
          value={next} onChange={(e) => setNext(e.target.value)} />
        <Strength length={next.length} />
        <PasswordField label="اكتبها تاني" autoComplete="new-password"
          value={repeat} onChange={(e) => setRepeat(e.target.value)} />

        {/* what the new password still needs: it ticks itself while it is typed */}
        <ul className="grid gap-1.5 rounded-[14px] border border-line/60 bg-card-2/50 px-3.5 py-3 text-[13px]" aria-label="شروط كلمة السر الجديدة">
          <Rule ok={longEnough} started={next.length > 0}>{MIN_PASSWORD} حرف على الأقل{next.length > 0 && !longEnough ? ` (لسه ${MIN_PASSWORD - next.length})` : ""}</Rule>
          <Rule ok={differs} started={next.length > 0}>غير كلمة السر المؤقتة أو الحالية</Rule>
          <Rule ok={same} started={repeat.length > 0}>الاتنين زي بعض</Rule>
          <li className="pt-0.5 text-[12px] text-ink-soft">جملة طويلة سهل تفتكرها أحسن من كلمة معقدة.</li>
        </ul>

        {error ? <Notice>{error}</Notice> : null}
        <div className="flex flex-wrap gap-2">
          <Button type="submit" variant="primary" className="rounded-full" busy={busy} disabled={!ready}>
            غيّر كلمة السر
          </Button>
          {forced ? (
            <Button variant="ghost" className="rounded-full" onClick={async () => { await logout(); navigate("/login", { replace: true }); }}>
              خروج
            </Button>
          ) : (
            <Button variant="ghost" className="rounded-full" onClick={() => {
              // opened straight from a link: no page of the panel to go back to, so «حسابي»
              if ((window.history.state as { idx?: number } | null)?.idx) navigate(-1);
              else navigate("/account");
            }}>رجوع</Button>
          )}
        </div>
      </form>
    </AuthLayout>
  );
}

function Step({ n, children }: { n: number; children: string }) {
  return (
    <li className="flex items-center gap-2">
      <span aria-hidden className="tabular grid size-5 shrink-0 place-items-center rounded-full bg-gold text-[11px] font-bold text-white">{n}</span>
      {children}
    </li>
  );
}

function Rule({ ok, started, children }: { ok: boolean; started: boolean; children: ReactNode }) {
  return (
    <li className={`flex items-center gap-2 ${ok ? "text-green-ink" : started ? "text-ink" : "text-ink-soft"}`}>
      <span aria-hidden className={`grid size-4 shrink-0 place-items-center rounded-full ${ok ? "bg-green-bg" : "border border-line-strong"}`}>
        {ok ? <Icon name="check" className="size-3" /> : null}
      </span>
      <span>{children}</span>
      <span className="sr-only">{ok ? "تمام" : "لسه"}</span>
    </li>
  );
}

/** A password field that can be shown while it is typed (a typo in a new password is the commonest way to lock oneself out). */
function PasswordField({ label, ...input }: InputHTMLAttributes<HTMLInputElement> & { label: string }) {
  const id = useId();
  const [shown, setShown] = useState(false);
  return (
    <div className="grid gap-1.5">
      <label htmlFor={id} className="text-sm font-semibold">
        {label} <span className="text-bad">*</span>
      </label>
      <div className="relative">
        <input id={id} {...input} type={shown ? "text" : "password"} dir="ltr" required
          className="min-h-11 w-full rounded-[var(--radius-control)] border border-line-strong bg-card ps-16 pe-3.5 text-base text-ink focus:border-gold" />
        <button type="button" onClick={() => setShown((v) => !v)} aria-pressed={shown} aria-label={`${shown ? "إخفاء" : "إظهار"} ${label}`}
          className="absolute inset-y-1.5 end-1.5 rounded-[10px] px-2.5 text-[12.5px] font-semibold text-gold-ink hover:bg-gold-tint">
          {shown ? "إخفاء" : "إظهار"}
        </button>
      </div>
    </div>
  );
}

/** How far the new password is from the minimum: a bar that fills, and turns green once it is long enough. */
function Strength({ length }: { length: number }) {
  if (length === 0) return null;
  const share = Math.min(1, length / MIN_PASSWORD);
  const ok = share >= 1;
  return (
    <div aria-hidden className="-mt-2 h-1.5 overflow-hidden rounded-full bg-card-2">
      <span className={`block h-full rounded-full transition-[width] motion-reduce:transition-none ${ok ? "bg-green" : "bg-gradient-to-l from-[#dcb98c] to-[#a87a48]"}`}
        style={{ width: `${share * 100}%` }} />
    </div>
  );
}
