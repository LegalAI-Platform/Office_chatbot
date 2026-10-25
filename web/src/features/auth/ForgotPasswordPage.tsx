import { useState, type FormEvent } from "react";
import { useMutation } from "@tanstack/react-query";
import { Link, useNavigate } from "react-router";
import { ApiError, errorMessage } from "../../shared/api/client";
import { Button, Field } from "../../shared/ui/ui";
import { forgotPassword, resetPassword } from "./api";
import { AuthLayout, CodeField, Notice, useCountdown } from "./AuthLayout";

/** A code to the account's e-mail, then a new password; every old session ends. */
export function ForgotPasswordPage() {
  const navigate = useNavigate();
  const [login, setLogin] = useState("");
  const [sent, setSent] = useState(false);
  const [code, setCode] = useState("");
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [left, restart] = useCountdown(60, 0);
  const ask = useMutation({ mutationFn: () => forgotPassword(login.trim()), onSuccess: () => { setSent(true); restart(); } });
  const reset = useMutation({
    mutationFn: () => resetPassword(login.trim(), code, password),
    onSuccess: () => navigate("/login", { replace: true, state: { notice: "كلمة السر اتغيرت. ادخل بالجديدة." } }),
  });
  const weak = reset.error instanceof ApiError && reset.error.code === "weak_password";

  function submitAsk(e: FormEvent) {
    e.preventDefault();
    ask.mutate();
  }

  function submitReset(e: FormEvent) {
    e.preventDefault();
    if (password === confirm) reset.mutate();
  }

  if (!sent) {
    return (
      <AuthLayout title="نسيت كلمة السر" subtitle="اكتب اسم المستخدم أو الإيميل، وهنبعتلك كود على إيميل الحساب.">
        <form onSubmit={submitAsk} className="grid gap-5" noValidate>
          <Field label="اسم المستخدم أو الإيميل" required dir="ltr" autoComplete="username" value={login}
            onChange={(e) => setLogin(e.target.value)} />
          {ask.isError ? <Notice>{errorMessage(ask.error)}</Notice> : null}
          <Button type="submit" variant="primary" busy={ask.isPending} disabled={!login.trim()}>ابعت الكود</Button>
          <Link to="/login" className="text-center text-sm font-semibold text-accent-ink hover:underline">ارجع لتسجيل الدخول</Link>
        </form>
      </AuthLayout>
    );
  }

  return (
    <AuthLayout title="كلمة سر جديدة" subtitle="لو الحساب موجود، وصلك كود من 6 أرقام على إيميله. الكود شغال 10 دقايق.">
      <form onSubmit={submitReset} className="grid gap-4" noValidate>
        <CodeField label="الكود" value={code} onChange={setCode} />
        <Field label="كلمة السر الجديدة" type="password" required dir="ltr" autoComplete="new-password" value={password}
          hint="12 حرف على الأقل." onChange={(e) => setPassword(e.target.value)} error={weak ? errorMessage(reset.error) : undefined} />
        <Field label="اكتبها تاني" type="password" required dir="ltr" autoComplete="new-password" value={confirm}
          onChange={(e) => setConfirm(e.target.value)} error={confirm && password !== confirm ? "كلمتين السر مش زي بعض." : undefined} />
        {reset.isError && !weak ? <Notice>{errorMessage(reset.error)}</Notice> : null}
        <Button type="submit" variant="primary" busy={reset.isPending}
          disabled={code.length !== 6 || password.length < 12 || password !== confirm}>غيّر كلمة السر</Button>
        <Button variant="ghost" busy={ask.isPending} disabled={left > 0} onClick={() => ask.mutate()}>
          {left > 0 ? `ابعت كود تاني بعد ${left} ثانية` : "ابعت كود تاني"}
        </Button>
      </form>
    </AuthLayout>
  );
}
