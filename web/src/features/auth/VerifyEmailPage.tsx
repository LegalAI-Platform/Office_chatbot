import { useState, type FormEvent } from "react";
import { useMutation } from "@tanstack/react-query";
import { Link, useLocation } from "react-router";
import { errorMessage } from "../../shared/api/client";
import { Button, Field } from "../../shared/ui/ui";
import { resendCode, verifyEmail } from "./api";
import { AuthLayout, CodeField, Notice, secondsText, useCountdown } from "./AuthLayout";

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
      <AuthLayout title="تم تأكيد البريد الإلكتروني">
        <Notice tone="good">حسابك الآن في انتظار أن يفعّله المدير ويحدد صلاحياتك، وستصلك رسالة على بريدك الإلكتروني فور تفعيله.</Notice>
        <Link to="/login" className="text-center text-sm font-semibold text-gold-ink hover:underline">العودة إلى تسجيل الدخول</Link>
      </AuthLayout>
    );
  }

  return (
    <AuthLayout title="تأكيد البريد الإلكتروني" subtitle={email ? <>أرسلنا رمزًا من 6 أرقام إلى <bdi dir="ltr">{email}</bdi>. الرمز صالح لمدة 10 دقائق.</> : "اكتب البريد الإلكتروني الذي سجّلت به والرمز الذي وصلك عليه."}>
      <form onSubmit={submit} className="grid gap-5" noValidate>
        {state?.mailFailed ? <Notice>تعذّر إرسال الرسالة إلى بريدك الإلكتروني. اضغط «أرسل رمزًا آخر».</Notice> : null}
        {typeof state?.email !== "string" ? (
          <Field label="البريد الإلكتروني" type="email" required dir="ltr" autoComplete="email" value={email} onChange={(e) => setEmail(e.target.value)} />
        ) : null}
        <CodeField label="الرمز" value={code} onChange={setCode} />
        {verify.isError ? <Notice>{errorMessage(verify.error)}</Notice> : null}
        {resend.isSuccess ? <Notice tone="good">إذا كان هذا البريد الإلكتروني مسجّلًا ولم يُؤكَّد بعد، فسيصلك رمز جديد.</Notice> : null}
        <Button type="submit" variant="primary" busy={verify.isPending} disabled={code.length !== 6 || !email}>تأكيد</Button>
        <Button variant="ghost" busy={resend.isPending} disabled={left > 0 || !email} onClick={() => resend.mutate()}>
          {left > 0 ? `أرسل رمزًا آخر بعد ${secondsText(left)}` : "أرسل رمزًا آخر"}
        </Button>
        <p className="text-center text-xs text-ink-soft">لم تجده؟ راجع مجلد الرسائل غير المرغوب فيها (Spam) أو الرسائل المهمة.</p>
      </form>
    </AuthLayout>
  );
}
