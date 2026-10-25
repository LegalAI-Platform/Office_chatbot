import { useState, type FormEvent } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Link, useLocation, useNavigate } from "react-router";
import { ApiError, errorMessage } from "../../shared/api/client";
import { isSecondStep, login, loginSecondStep, meKey } from "../../shared/api/session";
import type { Me } from "../../shared/api/types";
import { Button, Field } from "../../shared/ui/ui";
import { AuthLayout, CodeField, Notice } from "./AuthLayout";

/** Sign-in; with the authenticator on, the code is a second step before any session (v1.9). */
export function LoginPage() {
  const qc = useQueryClient();
  const navigate = useNavigate();
  const location = useLocation();
  const done = (location.state as { notice?: string } | null)?.notice;
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [ticket, setTicket] = useState<string>();
  const [code, setCode] = useState("");
  const [error, setError] = useState<string>();
  const [waiting, setWaiting] = useState(false);
  const [busy, setBusy] = useState(false);

  function enter(me: Me) {
    qc.setQueryData(meKey, me);
    navigate(me.must_change_password ? "/change-password" : "/", { replace: true });
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    setError(undefined);
    setWaiting(false);
    setBusy(true);
    try {
      const r = await login(username.trim(), password);
      if (isSecondStep(r)) {
        setTicket(r.ticket);
        setCode("");
      } else {
        enter(r);
      }
    } catch (err) {
      if (err instanceof ApiError && err.code === "email_not_verified") {
        navigate("/verify-email", { state: { email: err.data?.email } });
        return;
      }
      if (err instanceof ApiError && err.code === "pending_approval") setWaiting(true);
      else setError(errorMessage(err));
      setPassword("");
    } finally {
      setBusy(false);
    }
  }

  async function submitCode(e: FormEvent) {
    e.preventDefault();
    setError(undefined);
    setBusy(true);
    try {
      enter(await loginSecondStep(ticket!, code));
    } catch (err) {
      if (err instanceof ApiError && err.code === "expired_ticket") {
        setTicket(undefined);
        setPassword("");
      }
      setError(errorMessage(err));
      setCode("");
    } finally {
      setBusy(false);
    }
  }

  if (ticket) {
    return (
      <AuthLayout title="التحقق بخطوتين" subtitle="افتح تطبيق الـ Authenticator واكتب الكود اللي ظاهر لحساب المكتب.">
        <form onSubmit={submitCode} className="grid gap-5" noValidate>
          <CodeField label="الكود" value={code} onChange={setCode} allowRecovery
            hint="الموبايل مش معاك؟ اكتب كود من أكواد الاسترجاع (xxxx-xxxx)." />
          {error ? <Notice>{error}</Notice> : null}
          <Button type="submit" variant="primary" busy={busy} disabled={code.length < 6}>تأكيد</Button>
          <button type="button" className="text-sm font-semibold text-accent-ink hover:underline"
            onClick={() => { setTicket(undefined); setPassword(""); setError(undefined); }}>
            ارجع لكلمة السر
          </button>
        </form>
      </AuthLayout>
    );
  }

  return (
    <AuthLayout title="تسجيل الدخول" subtitle="لوحة المكتب. ادخل باسم المستخدم وكلمة السر بتوعك.">
      <form onSubmit={submit} className="grid gap-5" noValidate>
        {done ? <Notice tone="good">{done}</Notice> : null}
        <Field label="اسم المستخدم" name="username" autoComplete="username" dir="ltr" required
          value={username} onChange={(e) => setUsername(e.target.value)} />
        <div className="grid gap-1.5">
          <Field label="كلمة السر" name="password" type="password" autoComplete="current-password" dir="ltr" required
            value={password} onChange={(e) => setPassword(e.target.value)} />
          <Link to="/forgot-password" className="justify-self-start text-[13px] font-medium text-accent-ink hover:underline">
            نسيت كلمة السر؟
          </Link>
        </div>
        {waiting ? <Notice tone="good">حسابك مستني المدير يفعّله. هيوصلك إيميل أول ما يتفعّل.</Notice> : null}
        {error ? <Notice>{error}</Notice> : null}
        <Button type="submit" variant="primary" busy={busy} disabled={!username || !password}>
          دخول
        </Button>
        <p className="text-center text-sm text-ink-soft">
          معندكش حساب؟{" "}
          <Link to="/register" className="font-semibold text-accent-ink hover:underline">اعمل حساب جديد</Link>
        </p>
      </form>
    </AuthLayout>
  );
}
