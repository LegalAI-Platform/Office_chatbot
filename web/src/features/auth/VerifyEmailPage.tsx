import { useState, type FormEvent } from "react";
import { useMutation } from "@tanstack/react-query";
import { Link, useLocation } from "react-router";
import { errorMessage } from "../../shared/api/client";
import { Button, Field } from "../../shared/ui/ui";
import { resendCode, verifyEmail } from "./api";
import { AuthLayout, CodeField, Notice, useCountdown } from "./AuthLayout";

/** The six digits from the e-mail; after that the account waits for the manager. */
export function VerifyEmailPage() {
  const state = useLocation().state as { email?: unknown; mailFailed?: boolean } | null;
  const [email, setEmail] = useState(typeof state?.email === "string" ? state.email : "");
  const [code, setCode] = useState("");
  const [left, restart] = useCountdown(60, state?.mailFailed ? 0 : 60);
  const verify = useMutation({ mutationFn: () => verifyEmail(email.trim(), code) });
  const resend = useMutation({ mutationFn: () => resendCode(email.trim()), onSuccess: () => { restart(); setCode(""); } });

  function submit(e: FormEvent) {
    e.preventDefault();
    verify.mutate();
  }

  if (verify.isSuccess) {
    return (
      <AuthLayout title="الإيميل اتأكد">
        <Notice tone="good">تمام. حسابك دلوقتي مستني المدير يفعّله ويحدد صلاحياتك، وهيوصلك إيميل أول ما يتفعّل.</Notice>
        <Link to="/login" className="text-center text-sm font-semibold text-gold-ink hover:underline">ارجع لتسجيل الدخول</Link>
      </AuthLayout>
    );
  }

  return (
    <AuthLayout title="أكّد الإيميل" subtitle={email ? <>بعتنا كود من 6 أرقام على <bdi dir="ltr">{email}</bdi>. الكود شغال 10 دقايق.</> : "اكتب الإيميل اللي سجلت بيه والكود اللي وصلك عليه."}>
      <form onSubmit={submit} className="grid gap-5" noValidate>
        {state?.mailFailed ? <Notice>الإيميل ما اتبعتش. اضغط «ابعت كود تاني».</Notice> : null}
        {typeof state?.email !== "string" ? (
          <Field label="الإيميل" type="email" required dir="ltr" autoComplete="email" value={email} onChange={(e) => setEmail(e.target.value)} />
        ) : null}
        <CodeField label="الكود" value={code} onChange={setCode} />
        {verify.isError ? <Notice>{errorMessage(verify.error)}</Notice> : null}
        {resend.isSuccess ? <Notice tone="good">لو الإيميل ده مسجّل ومش متأكد، هيوصلك كود جديد.</Notice> : null}
        <Button type="submit" variant="primary" busy={verify.isPending} disabled={code.length !== 6 || !email}>تأكيد</Button>
        <Button variant="ghost" busy={resend.isPending} disabled={left > 0 || !email} onClick={() => resend.mutate()}>
          {left > 0 ? `ابعت كود تاني بعد ${left} ثانية` : "ابعت كود تاني"}
        </Button>
        <p className="text-center text-xs text-ink-soft">ما لقيتوش؟ بص في الـ Spam أو الرسايل المهمة.</p>
      </form>
    </AuthLayout>
  );
}
