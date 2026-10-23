import { useState, type FormEvent } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router";
import { api, errorMessage, resetCsrf } from "../../shared/api/client";
import { meKey, useMe, useLogout } from "../../shared/api/session";
import { Button, Card, Field } from "../../shared/ui/ui";

export const MIN_PASSWORD = 12;

/** First sign-in (a temporary password) or any time from «حسابي». */
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
    <main className="mx-auto grid w-full max-w-md gap-4 px-4 py-6">
      <h1 className="text-2xl">تغيير كلمة السر</h1>
      {forced ? (
        <p className="rounded-[var(--radius-control)] bg-warn-bg px-3 py-2 text-sm text-warn">
          دي كلمة سر مؤقتة من المدير. غيّرها الأول عشان تكمّل.
        </p>
      ) : null}
      <Card>
        <form onSubmit={submit} className="grid gap-4" noValidate>
          <Field label="كلمة السر الحالية" type="password" autoComplete="current-password" dir="ltr" required
            value={current} onChange={(e) => setCurrent(e.target.value)} />
          <Field label="كلمة السر الجديدة" type="password" autoComplete="new-password" dir="ltr" required
            hint={`${MIN_PASSWORD} حرف على الأقل. جملة طويلة سهل تفتكرها أحسن من كلمة معقدة.`}
            error={tooShort ? `لسه ${MIN_PASSWORD - next.length} حرف.` : undefined}
            value={next} onChange={(e) => setNext(e.target.value)} />
          <Field label="اكتبها تاني" type="password" autoComplete="new-password" dir="ltr" required
            error={mismatch ? "مش زي اللي فوق." : undefined}
            value={repeat} onChange={(e) => setRepeat(e.target.value)} />
          {error ? <p className="text-sm text-bad" role="alert">{error}</p> : null}
          <div className="flex flex-wrap gap-2">
            <Button type="submit" variant="primary" busy={busy}
              disabled={!current || next.length < MIN_PASSWORD || next !== repeat}>
              غيّر كلمة السر
            </Button>
            {forced ? (
              <Button variant="ghost" onClick={async () => { await logout(); navigate("/login", { replace: true }); }}>
                خروج
              </Button>
            ) : (
              <Button variant="ghost" onClick={() => navigate(-1)}>رجوع</Button>
            )}
          </div>
        </form>
      </Card>
    </main>
  );
}
