import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError, errorMessage } from "../../shared/api/client";
import { useMe } from "../../shared/api/session";
import type { NewUserResult, PanelUser, Role } from "../../shared/api/types";
import { label, ROLES } from "../../shared/lib/labels";
import { cairoWhen } from "../../shared/lib/time";
import { Icon } from "../../shared/ui/Icon";
import { Button, Card, Chip, Empty, ErrorState, Field, Loading } from "../../shared/ui/ui";

const usersKey = ["users"];

/** Accounts (users.manage: the manager only). The page is not even in the navigation for others. */
export function UsersPage() {
  const users = useQuery({ queryKey: usersKey, queryFn: () => api<PanelUser[]>("GET", "/users") });
  const [adding, setAdding] = useState(false);
  const [secret, setSecret] = useState<{ name: string; password: string } | null>(null);

  return (
    <div className="grid gap-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-2xl">الحسابات</h1>
        {!adding ? (
          <Button variant="primary" onClick={() => { setAdding(true); setSecret(null); }}>
            <Icon name="user-plus" className="size-4" />
            ضيف حساب
          </Button>
        ) : null}
      </div>

      {secret ? <TemporaryPassword {...secret} onDone={() => setSecret(null)} /> : null}
      {adding ? (
        <AddUser onCancel={() => setAdding(false)}
          onCreated={(r) => { setAdding(false); setSecret({ name: r.user.display_name, password: r.temporary_password }); }} />
      ) : null}

      {users.isPending ? (
        <Loading />
      ) : users.isError ? (
        <ErrorState message={errorMessage(users.error)} onRetry={() => void users.refetch()} />
      ) : users.data.length === 0 ? (
        <Empty title="مفيش حسابات لسه" />
      ) : (
        <>
          {users.data.some((u) => u.pending) ? (
            <section className="grid gap-3" aria-labelledby="pending-title">
              <h2 id="pending-title" className="flex items-center gap-2 text-lg">
                مستنيين التفعيل
                <span className="tabular grid size-6 place-items-center rounded-full bg-red text-xs font-bold text-white">
                  {users.data.filter((u) => u.pending).length}
                </span>
              </h2>
              <ul className="grid gap-3 md:grid-cols-2" aria-label="حسابات مستنية التفعيل">
                {users.data.filter((u) => u.pending).map((u) => (
                  <li key={u.id}><PendingCard user={u} /></li>
                ))}
              </ul>
            </section>
          ) : null}
          <ul className="grid gap-3 md:grid-cols-2" aria-label="الحسابات">
            {users.data.filter((u) => !u.pending).map((u) => (
              <li key={u.id}>
                <UserCard user={u} onSecret={setSecret} />
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  );
}

/** Shown once: the server never returns it again. */
function TemporaryPassword({ name, password, onDone }: { name: string; password: string; onDone: () => void }) {
  const [copied, setCopied] = useState(false);
  return (
    <Card className="border-warn">
      <h2 className="text-lg">كلمة السر المؤقتة لـ {name}</h2>
      <p className="text-sm text-ink-soft">
        بتظهر المرة دي بس. ادّيها لصاحب الحساب بنفسك، وهو هيغيّرها أول ما يدخل.
      </p>
      <code className="tabular select-all rounded-[var(--radius-control)] bg-muted px-3 py-2 text-lg break-all" dir="ltr">
        {password}
      </code>
      <div className="flex flex-wrap gap-2">
        <Button onClick={async () => {
          try { await navigator.clipboard.writeText(password); setCopied(true); } catch { setCopied(false); }
        }}>{copied ? "اتنسخت" : "انسخ"}</Button>
        <Button variant="primary" onClick={onDone}>خلاص، ادّيتها له</Button>
      </div>
    </Card>
  );
}

function AddUser({ onCancel, onCreated }: { onCancel: () => void; onCreated: (r: NewUserResult) => void }) {
  const qc = useQueryClient();
  const [form, setForm] = useState({ username: "", display_name: "", email: "", role: "staff" as Role, staff_name: "" });
  const create = useMutation({
    mutationFn: () => api<NewUserResult>("POST", "/users", { ...form, staff_name: form.role === "staff" ? form.staff_name || form.display_name : null }),
    onSuccess: (r) => { void qc.invalidateQueries({ queryKey: usersKey }); onCreated(r); },
  });
  const fieldError = (name: string) =>
    create.error instanceof ApiError ? create.error.fields?.[name]?.[0] : undefined;
  const set = (k: keyof typeof form) => (e: { target: { value: string } }) => setForm({ ...form, [k]: e.target.value });

  function submit(e: FormEvent) {
    e.preventDefault();
    create.mutate();
  }

  return (
    <Card>
      <h2 className="text-lg">حساب جديد</h2>
      <form onSubmit={submit} className="grid gap-4 md:grid-cols-2" noValidate>
        <Field label="الاسم اللي هيظهر" required value={form.display_name} onChange={set("display_name")} error={fieldError("display_name")} />
        <Field label="اسم المستخدم (للدخول)" required dir="ltr" autoComplete="off" value={form.username}
          hint="حروف إنجليزي صغيرة وأرقام، من غير مسافات." onChange={set("username")} error={fieldError("username")} />
        <Field label="الإيميل" type="email" required dir="ltr" value={form.email}
          hint="هيتستخدم كمان لحساب Chatwoot." onChange={set("email")} error={fieldError("email")} />
        <label className="grid gap-1.5">
          <span className="text-sm font-medium">الدور <span className="text-bad">*</span></span>
          <select className="surface min-h-11 rounded-[var(--radius-control)] px-3 text-ink" value={form.role}
            onChange={(e) => setForm({ ...form, role: e.target.value as Role })}>
            <option value="staff">موظف: مهامه والمهام اللي مش متعيّنة</option>
            <option value="lawyer">الأستاذ: كل المهام، ويوزّعها</option>
            <option value="manager">مدير: كل حاجة، ومنها الحسابات</option>
          </select>
        </label>
        {form.role === "staff" ? (
          <Field label="اسمه في قوايم المكتب" value={form.staff_name} onChange={set("staff_name")}
            hint="زي ما هو مكتوب في عمود الموظف في قايمة الموكلين. لو فاضي، هيتاخد الاسم اللي فوق."
            error={fieldError("staff_name")} />
        ) : null}
        {create.isError && !fieldError("username") && !fieldError("email") ? (
          <p className="text-sm text-bad md:col-span-2" role="alert">{errorMessage(create.error)}</p>
        ) : null}
        <div className="flex flex-wrap gap-2 md:col-span-2">
          <Button type="submit" variant="primary" busy={create.isPending}
            disabled={!form.username || !form.display_name || !form.email}>اعمل الحساب</Button>
          <Button variant="ghost" onClick={onCancel}>إلغاء</Button>
        </div>
      </form>
    </Card>
  );
}

/**
 * Registered from the sign-up page (v1.9): once the e-mail is confirmed, the manager picks the role
 * and approves it, or refuses it (the registration is deleted and the person gets an e-mail).
 */
function PendingCard({ user }: { user: PanelUser }) {
  const qc = useQueryClient();
  const [role, setRole] = useState<Role>("staff");
  const [staffName, setStaffName] = useState(user.display_name);
  const [refusing, setRefusing] = useState(false);
  const act = useMutation({
    mutationFn: (path: "approve" | "reject") => api<unknown>("POST", `/users/${user.id}/${path}`,
      path === "approve" ? { role, staff_name: role === "staff" ? staffName.trim() || user.display_name : null } : undefined),
    onSuccess: () => void qc.invalidateQueries({ queryKey: usersKey }),
  });

  return (
    <Card className="border-gold-line">
      <div className="flex items-start justify-between gap-2">
        <div className="grid min-w-0 gap-0.5">
          <span className="font-semibold">{user.display_name}</span>
          <span className="text-xs text-ink-soft break-all" dir="ltr" style={{ textAlign: "right" }}>{user.username} · {user.email}</span>
          <span className="tabular text-xs text-ink-soft">سجّل {cairoWhen(user.created_at)}</span>
        </div>
        {user.email_verified ? <Chip tone="gold">مستني التفعيل</Chip> : <Chip tone="grey">ما أكدش الإيميل</Chip>}
      </div>

      {user.email_verified ? (
        <div className="grid gap-3 sm:grid-cols-2">
          <label className="grid gap-1.5">
            <span className="text-sm font-semibold">الدور</span>
            <select className="surface min-h-11 rounded-[var(--radius-control)] px-3 text-ink" value={role}
              onChange={(e) => setRole(e.target.value as Role)}>
              <option value="staff">موظف</option>
              <option value="lawyer">الأستاذ</option>
              <option value="manager">مدير</option>
            </select>
          </label>
          {role === "staff" ? (
            <Field label="اسمه في قوايم المكتب" value={staffName} onChange={(e) => setStaffName(e.target.value)} />
          ) : null}
        </div>
      ) : (
        <p className="text-sm text-ink-soft">مش هيقدر تفعّله غير لما يأكد إيميله. لو ما أكدش خلال يوم، الطلب بيتمسح لوحده.</p>
      )}

      {refusing ? (
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-sm">متأكد؟ الطلب هيتمسح.</span>
          <Button variant="danger" busy={act.isPending} onClick={() => act.mutate("reject")}>أيوه، ارفض</Button>
          <Button variant="ghost" onClick={() => setRefusing(false)}>لأ</Button>
        </div>
      ) : (
        <div className="flex flex-wrap gap-2">
          {user.email_verified ? (
            <Button variant="primary" busy={act.isPending} onClick={() => act.mutate("approve")}>
              <Icon name="check" className="size-4" />فعّل الحساب
            </Button>
          ) : null}
          <Button variant="ghost" onClick={() => setRefusing(true)}>ارفض</Button>
        </div>
      )}
      {act.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(act.error)}</p> : null}
    </Card>
  );
}

function UserCard({ user, onSecret }: { user: PanelUser; onSecret: (s: { name: string; password: string }) => void }) {
  const { data: me } = useMe();
  const qc = useQueryClient();
  const self = me?.id === user.id;
  const [confirm, setConfirm] = useState<null | "disable" | "reset" | "reset-2fa">(null);
  const change = useMutation({
    mutationFn: (body: { path: string; payload?: unknown }) => api<PanelUser | { temporary_password: string }>("POST", `/users/${user.id}/${body.path}`, body.payload),
    onSuccess: (r) => {
      setConfirm(null);
      void qc.invalidateQueries({ queryKey: usersKey });
      if ("temporary_password" in r) onSecret({ name: user.display_name, password: r.temporary_password });
    },
  });

  return (
    <Card className={user.active ? "" : "opacity-70"}>
      <div className="flex items-start justify-between gap-2">
        <div className="grid gap-0.5 min-w-0">
          <span className="font-semibold">{user.display_name}{self ? " (إنت)" : ""}</span>
          <span className="text-xs text-ink-soft break-all" dir="ltr" style={{ textAlign: "right" }}>{user.username} · {user.email}</span>
        </div>
        <span className="flex flex-wrap justify-end gap-1.5">
          {user.two_factor ? <Chip tone="green">خطوتين</Chip> : null}
          {!user.active ? <Chip tone="cancelled">متعطّل</Chip> : user.locked ? <Chip tone="overdue">مقفول مؤقتًا</Chip> : <Chip tone="in_progress">شغال</Chip>}
        </span>
      </div>
      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
        <dt className="text-ink-soft">الدور</dt>
        <dd>
          {self ? label(ROLES, user.role ?? "") : (
            <select aria-label={`دور ${user.display_name}`} className="surface min-h-9 rounded-[var(--radius-control)] px-2 text-ink"
              value={user.role ?? "staff"} disabled={change.isPending}
              onChange={(e) => change.mutate({ path: "role", payload: { role: e.target.value } })}>
              {(["staff", "lawyer", "manager"] as Role[]).map((r) => <option key={r} value={r}>{label(ROLES, r)}</option>)}
            </select>
          )}
        </dd>
        {user.staff ? (<><dt className="text-ink-soft">في القوايم</dt><dd>{user.staff.name}</dd></>) : null}
        <dt className="text-ink-soft">آخر دخول</dt>
        <dd className="tabular">{user.last_login_at ? cairoWhen(user.last_login_at) : "لسه ما دخلش"}</dd>
      </dl>

      {!self ? (
        confirm ? (
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-sm">
              {confirm === "disable" ? "متأكد؟ هيخرج من كل الأجهزة." : confirm === "reset-2fa" ? "متأكد؟ هيدخل بكلمة السر بس لحد ما يشغّله تاني." : "متأكد؟ كلمة السر الحالية هتبطل."}
            </span>
            <Button variant="danger" busy={change.isPending}
              onClick={() => change.mutate({ path: confirm === "disable" ? "disable" : confirm === "reset-2fa" ? "reset-2fa" : "reset-password" })}>أيوه</Button>
            <Button variant="ghost" onClick={() => setConfirm(null)}>لأ</Button>
          </div>
        ) : (
          <div className="flex flex-wrap gap-2">
            {user.active ? (
              <Button variant="ghost" onClick={() => setConfirm("disable")}><Icon name="lock" className="size-4" />عطّل</Button>
            ) : (
              <Button onClick={() => change.mutate({ path: "enable" })} busy={change.isPending}>فعّل</Button>
            )}
            <Button variant="ghost" onClick={() => setConfirm("reset")}><Icon name="key" className="size-4" />كلمة سر جديدة</Button>
            {user.two_factor ? (
              <Button variant="ghost" onClick={() => setConfirm("reset-2fa")}>اقفل التحقق بخطوتين (موبايل ضاع)</Button>
            ) : null}
          </div>
        )
      ) : null}
      {change.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(change.error)}</p> : null}
    </Card>
  );
}
