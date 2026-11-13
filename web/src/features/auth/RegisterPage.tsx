import { useState, type FormEvent } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Link, useNavigate } from "react-router";
import { ApiError, errorMessage } from "../../shared/api/client";
import { Button, Field, Loading } from "../../shared/ui/ui";
import { register, signupAvailable } from "./api";
import { AuthLayout, Notice } from "./AuthLayout";

/**
 * Anyone can ask for an account (v1.9): confirm the e-mail with a code, then the manager approves
 * it and picks the role. Until then the account cannot sign in.
 */
export function RegisterPage() {
  const navigate = useNavigate();
  const available = useQuery({ queryKey: ["signup-available"], queryFn: signupAvailable, retry: false });
  const [form, setForm] = useState({ display_name: "", username: "", email: "", password: "", confirm: "" });
  const mismatch = form.confirm.length > 0 && form.password !== form.confirm;
  const send = useMutation({
    mutationFn: () => register({ display_name: form.display_name.trim(), username: form.username.trim().toLowerCase(),
      email: form.email.trim(), password: form.password }),
    onSuccess: (r) => navigate("/verify-email", { state: { email: form.email.trim(), mailFailed: !r.mail_sent } }),
  });
  const fieldError = (name: string) => {
    const code = send.error instanceof ApiError ? send.error.fields?.[name]?.[0] : undefined;
    if (!code) return undefined;
    if (code === "invalid") {
      return { username: "حروف إنجليزية صغيرة وأرقام ونقطة أو شرطة، من 3 إلى 32 حرفًا.", email: "اكتب بريدًا إلكترونيًا صحيحًا.", display_name: "اكتب اسمك." }[name] ?? "غير صحيح.";
    }
    return errorMessage(new ApiError(400, code, code));
  };
  const set = (k: keyof typeof form) => (e: { target: { value: string } }) => setForm({ ...form, [k]: e.target.value });

  function submit(e: FormEvent) {
    e.preventDefault();
    if (!mismatch) send.mutate();
  }

  if (available.isPending) return <AuthLayout title="حساب جديد"><Loading /></AuthLayout>;
  if (available.data?.available === false) {
    return (
      <AuthLayout title="حساب جديد">
        <Notice>إنشاء الحسابات من هنا غير متاح بعد. تواصل مع مدير المكتب لإنشاء حساب لك.</Notice>
        <Link to="/login" className="text-center text-sm font-semibold text-gold-ink hover:underline">العودة إلى تسجيل الدخول</Link>
      </AuthLayout>
    );
  }

  return (
    <AuthLayout title="حساب جديد" subtitle="للعاملين في المكتب. بعد تأكيد بريدك الإلكتروني، سيفعّل المدير حسابك ويحدد صلاحياتك.">
      <form onSubmit={submit} className="grid gap-4" noValidate>
        <Field label="الاسم" required autoComplete="name" value={form.display_name} onChange={set("display_name")}
          error={fieldError("display_name")} />
        <Field label="اسم المستخدم (للدخول)" required dir="ltr" autoComplete="username" value={form.username}
          hint="حروف إنجليزية صغيرة وأرقام، دون مسافات." onChange={set("username")} error={fieldError("username")} />
        <Field label="البريد الإلكتروني" type="email" required dir="ltr" autoComplete="email" value={form.email}
          hint="سيصلك عليه رمز التأكيد." onChange={set("email")} error={fieldError("email")} />
        <Field label="كلمة السر" type="password" required dir="ltr" autoComplete="new-password" value={form.password}
          hint="12 حرفًا على الأقل. العبارة الطويلة السهلة التذكّر أفضل من الكلمة المعقدة." onChange={set("password")} error={fieldError("password")} />
        <Field label="أعد كتابة كلمة السر" type="password" required dir="ltr" autoComplete="new-password" value={form.confirm}
          onChange={set("confirm")} error={mismatch ? "كلمتا السر غير متطابقتين." : undefined} />
        {send.isError && !(send.error instanceof ApiError && send.error.fields) ? <Notice>{errorMessage(send.error)}</Notice> : null}
        <Button type="submit" variant="primary" busy={send.isPending}
          disabled={!form.display_name || !form.username || !form.email || form.password.length < 12 || form.password !== form.confirm}>
          إنشاء الحساب
        </Button>
        <p className="text-center text-sm text-ink-soft">
          لديك حساب؟ <Link to="/login" className="font-semibold text-gold-ink hover:underline">سجّل الدخول</Link>
        </p>
      </form>
    </AuthLayout>
  );
}
