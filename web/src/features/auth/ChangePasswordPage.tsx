import { useState, type FormEvent } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router";
import { api, errorMessage, resetCsrf } from "../../shared/api/client";
import { meKey, useMe, useLogout } from "../../shared/api/session";
import { Icon } from "../../shared/ui/Icon";
import { Button, Field } from "../../shared/ui/ui";
import { AuthLayout, Notice } from "./AuthLayout";

export const MIN_PASSWORD = 12;

/**
 * First sign-in (a temporary password) or any time from «حسابي». In the sign-in's layout (the office's black panel and
 * a card), with how long the new one is.
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

  const tooShort = next.length > 0 && next.length < MIN_PASSWORD;
  const mismatch = repeat.length > 0 && repeat !== next;

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (next.length < MIN_PASSWORD || next !== repeat) return;
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
        <p className="flex items-start gap-2 rounded-[14px] border border-orange/30 bg-orange-bg px-3 py-2.5 text-sm text-orange-ink">
          <Icon name="key" className="mt-0.5 size-4 shrink-0" />
          دي كلمة سر مؤقتة من المدير. غيّرها الأول عشان تكمّل.
        </p>
      ) : null}
      <form onSubmit={submit} className="grid gap-4" noValidate>
        <Field label="كلمة السر الحالية" type="password" autoComplete="current-password" dir="ltr" required
          value={current} onChange={(e) => setCurrent(e.target.value)} />
        <Field label="كلمة السر الجديدة" type="password" autoComplete="new-password" dir="ltr" required
          hint={`${MIN_PASSWORD} حرف على الأقل. جملة طويلة سهل تفتكرها أحسن من كلمة معقدة.`}
          error={tooShort ? `لسه ${MIN_PASSWORD - next.length} حرف.` : undefined}
          value={next} onChange={(e) => setNext(e.target.value)} />
        <Strength length={next.length} />
        <Field label="اكتبها تاني" type="password" autoComplete="new-password" dir="ltr" required
          error={mismatch ? "مش زي اللي فوق." : undefined}
          value={repeat} onChange={(e) => setRepeat(e.target.value)} />
        {error ? <Notice>{error}</Notice> : null}
        <div className="flex flex-wrap gap-2">
          <Button type="submit" variant="primary" className="rounded-full" busy={busy}
            disabled={!current || next.length < MIN_PASSWORD || next !== repeat}>
            غيّر كلمة السر
          </Button>
          {forced ? (
            <Button variant="ghost" className="rounded-full" onClick={async () => { await logout(); navigate("/login", { replace: true }); }}>
              خروج
            </Button>
          ) : (
            <Button variant="ghost" className="rounded-full" onClick={() => navigate(-1)}>رجوع</Button>
          )}
        </div>
      </form>
    </AuthLayout>
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
