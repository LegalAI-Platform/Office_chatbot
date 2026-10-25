import { useMemo, useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import qrcode from "qrcode-generator";
import { errorMessage } from "../../shared/api/client";
import { meKey } from "../../shared/api/session";
import { Button, Card, Chip, Field, Loading } from "../../shared/ui/ui";
import { newRecoveryCodes, twoFactorDisable, twoFactorEnable, twoFactorKey, twoFactorSetup, twoFactorStatus } from "./api";
import { CodeField, Notice } from "./AuthLayout";

/**
 * The authenticator app (v1.9): scan, type a code to turn it on, keep the ten recovery codes.
 * Turning it off needs the password and a code; a manager can turn it off for a lost phone.
 */
export function TwoFactorCard() {
  const qc = useQueryClient();
  const status = useQuery({ queryKey: twoFactorKey, queryFn: twoFactorStatus });
  const [setup, setSetup] = useState<{ secret: string; uri: string } | null>(null);
  const [codes, setCodes] = useState<string[] | null>(null);
  const [mode, setMode] = useState<null | "off" | "renew">(null);
  const [code, setCode] = useState("");
  const [password, setPassword] = useState("");
  const refresh = () => {
    void qc.invalidateQueries({ queryKey: twoFactorKey });
    void qc.invalidateQueries({ queryKey: meKey });
  };

  const start = useMutation({ mutationFn: twoFactorSetup, onSuccess: (r) => { setSetup(r); setCode(""); } });
  const enable = useMutation({
    mutationFn: () => twoFactorEnable(code),
    onSuccess: (r) => { setSetup(null); setCodes(r.recovery_codes); setCode(""); refresh(); },
  });
  const disable = useMutation({
    mutationFn: () => twoFactorDisable(password, code),
    onSuccess: () => { setMode(null); setCode(""); setPassword(""); refresh(); },
  });
  const renew = useMutation({
    mutationFn: () => newRecoveryCodes(code),
    onSuccess: (r) => { setMode(null); setCode(""); setCodes(r.recovery_codes); refresh(); },
  });

  const header = (
    <div className="flex flex-wrap items-center justify-between gap-2">
      <h2 className="text-[16px] font-semibold">التحقق بخطوتين</h2>
      {status.data ? <Chip tone={status.data.enabled ? "green" : "grey"}>{status.data.enabled ? "شغال" : "مقفول"}</Chip> : null}
    </div>
  );

  if (status.isPending) return <Card>{header}<Loading /></Card>;
  if (status.isError) return <Card>{header}<Notice>{errorMessage(status.error)}</Notice></Card>;
  const s = status.data;

  if (codes) {
    return (
      <Card className="border-warn">
        {header}
        <RecoveryCodes codes={codes} onDone={() => setCodes(null)} />
      </Card>
    );
  }

  if (!s.enabled && setup) {
    return (
      <Card>
        {header}
        <ol className="grid list-decimal gap-1 ps-5 text-sm">
          <li>نزّل Google Authenticator أو Microsoft Authenticator على موبايلك.</li>
          <li>من التطبيق اختار «Scan a QR code» وصوّر المربع ده.</li>
          <li>اكتب الكود اللي هيظهر في التطبيق.</li>
        </ol>
        <div className="grid justify-items-center gap-2">
          <Qr text={setup.uri} />
          <details className="w-full text-sm">
            <summary className="cursor-pointer text-ink-soft">مش عارف تصوّر؟ اكتب المفتاح بإيدك</summary>
            <code className="tabular mt-2 block select-all rounded-[var(--radius-control)] bg-muted px-3 py-2 text-center break-all" dir="ltr">
              {setup.secret.replace(/(.{4})/g, "$1 ").trim()}
            </code>
          </details>
        </div>
        <form onSubmit={(e: FormEvent) => { e.preventDefault(); enable.mutate(); }} className="grid gap-3" noValidate>
          <CodeField label="الكود من التطبيق" value={code} onChange={setCode} />
          {enable.isError ? <Notice>{errorMessage(enable.error)}</Notice> : null}
          <div className="flex flex-wrap gap-2">
            <Button type="submit" variant="primary" busy={enable.isPending} disabled={code.length !== 6}>شغّل</Button>
            <Button variant="ghost" onClick={() => setSetup(null)}>إلغاء</Button>
          </div>
        </form>
      </Card>
    );
  }

  if (!s.enabled) {
    return (
      <Card>
        {header}
        <p className="text-sm text-ink-soft">
          بعد ما تشغّله، الدخول هيحتاج كلمة السر والكود اللي في تطبيق الـ Authenticator على موبايلك. لو حد عرف كلمة السر، برضه مش هيقدر يدخل.
        </p>
        {!s.available ? <Notice>مش متاح لسه على السيرفر. كلّم مدير المكتب.</Notice> : null}
        {start.isError ? <Notice>{errorMessage(start.error)}</Notice> : null}
        <div><Button variant="primary" busy={start.isPending} disabled={!s.available} onClick={() => start.mutate()}>شغّل التحقق بخطوتين</Button></div>
      </Card>
    );
  }

  return (
    <Card>
      {header}
      <p className="text-sm text-ink-soft">
        الدخول محتاج الكود اللي في التطبيق. فاضل <span className="tabular font-semibold text-ink">{s.recovery_codes_left}</span> من أكواد الاسترجاع.
      </p>
      {mode ? (
        <form onSubmit={(e: FormEvent) => { e.preventDefault(); (mode === "off" ? disable : renew).mutate(); }} className="grid gap-3" noValidate>
          {mode === "off" ? (
            <Field label="كلمة السر" type="password" required dir="ltr" autoComplete="current-password" value={password}
              onChange={(e) => setPassword(e.target.value)} />
          ) : null}
          <CodeField label="الكود من التطبيق" value={code} onChange={setCode} allowRecovery={mode === "off"} />
          {(mode === "off" ? disable : renew).isError ? <Notice>{errorMessage((mode === "off" ? disable : renew).error)}</Notice> : null}
          <div className="flex flex-wrap gap-2">
            <Button type="submit" variant={mode === "off" ? "danger" : "primary"} busy={disable.isPending || renew.isPending}
              disabled={code.length < 6 || (mode === "off" && !password)}>
              {mode === "off" ? "اقفل التحقق بخطوتين" : "اعمل أكواد جديدة"}
            </Button>
            <Button variant="ghost" onClick={() => { setMode(null); setCode(""); setPassword(""); }}>إلغاء</Button>
          </div>
        </form>
      ) : (
        <div className="flex flex-wrap gap-2">
          <Button onClick={() => setMode("renew")}>أكواد استرجاع جديدة</Button>
          <Button variant="ghost" onClick={() => setMode("off")}>اقفله</Button>
        </div>
      )}
    </Card>
  );
}

/** Shown once: only hashes are kept. */
function RecoveryCodes({ codes, onDone }: { codes: string[]; onDone: () => void }) {
  const [copied, setCopied] = useState(false);
  const text = codes.join("\n");
  return (
    <div className="grid gap-3">
      <p className="text-sm">
        <strong>أكواد الاسترجاع:</strong> بتظهر المرة دي بس. اكتبها أو احفظها في مكان آمن بعيد عن الموبايل. كل كود بيدخّلك مرة واحدة لو الموبايل ضاع.
      </p>
      <ol className="tabular grid grid-cols-2 gap-2 rounded-[var(--radius-control)] bg-muted p-3 text-center text-base" dir="ltr">
        {codes.map((c) => <li key={c} className="select-all">{c}</li>)}
      </ol>
      <div className="flex flex-wrap gap-2">
        <Button onClick={async () => {
          try { await navigator.clipboard.writeText(text); setCopied(true); } catch { setCopied(false); }
        }}>{copied ? "اتنسخت" : "انسخ"}</Button>
        <Button onClick={() => {
          const a = document.createElement("a");
          a.href = URL.createObjectURL(new Blob([`أكواد استرجاع لوحة المكتب\n\n${text}\n`], { type: "text/plain;charset=utf-8" }));
          a.download = "recovery-codes.txt";
          a.click();
          URL.revokeObjectURL(a.href);
        }}>نزّلها ملف</Button>
        <Button variant="primary" onClick={onDone}>خلاص، حفظتها</Button>
      </div>
    </div>
  );
}

/** The otpauth link as a QR code, drawn as SVG (always dark on white, so phones read it in both themes). */
function Qr({ text }: { text: string }) {
  const { size, path } = useMemo(() => {
    const qr = qrcode(0, "M");
    qr.addData(text);
    qr.make();
    const n = qr.getModuleCount();
    let d = "";
    for (let r = 0; r < n; r++) for (let c = 0; c < n; c++) if (qr.isDark(r, c)) d += `M${c + 4} ${r + 4}h1v1h-1z`;
    return { size: n + 8, path: d };
  }, [text]);
  return (
    <svg viewBox={`0 0 ${size} ${size}`} className="size-52 rounded-lg" role="img" aria-label="رمز QR لتطبيق الـ Authenticator"
      shapeRendering="crispEdges">
      <rect width={size} height={size} fill="#ffffff" />
      <path d={path} fill="#000000" />
    </svg>
  );
}
