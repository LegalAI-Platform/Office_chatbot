import { useState, type FormEvent, type ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError, errorMessage } from "../../shared/api/client";
import { can, useMe } from "../../shared/api/session";
import type { NewUserResult, PanelUser, Role } from "../../shared/api/types";
import { label, ROLES } from "../../shared/lib/labels";
import { cairoWhen } from "../../shared/lib/time";
import { confirmAction, toast } from "../../shared/ui/feedback";
import { Icon, type IconName } from "../../shared/ui/Icon";
import { Avatar, Button, Chip, ErrorState, Field, Loading } from "../../shared/ui/ui";

const usersKey = ["users"];

const select = "min-h-11 rounded-[14px] border border-line/70 bg-card/70 px-3 text-ink shadow-[inset_0_1px_2px_rgb(0_0_0/0.05)] focus:border-gold";

/**
 * Accounts (users.manage: the manager only). The page is not even in the navigation for others. As «liquid glass»: a
 * glass header, the sign-ups waiting for the manager first, then every account as a glass card with its role, its name in
 * the lists and its last sign-in.
 */
export function UsersPage() {
  const users = useQuery({ queryKey: usersKey, queryFn: () => api<PanelUser[]>("GET", "/users") });
  const [adding, setAdding] = useState(false);
  const [secret, setSecret] = useState<{ name: string; password: string; username?: string } | null>(null);
  const waiting = users.data?.filter((u) => u.pending) ?? [];

  return (
    <div className="relative isolate grid gap-5 pb-4">
      {/* the soft light the glass shows through */}
      <div aria-hidden className="pointer-events-none absolute inset-x-0 -top-6 -z-10 h-[30rem] overflow-hidden">
        <span className="absolute -top-16 end-[4%] size-[24rem] rounded-full bg-gold/30 blur-[100px]" />
        <span className="absolute top-24 start-[10%] size-[20rem] rounded-full bg-blue/10 blur-[100px]" />
      </div>

      <Intro total={users.data?.filter((u) => !u.pending).length}
        action={!adding ? (
          <Button variant="primary" className="rounded-full" onClick={() => { setAdding(true); setSecret(null); }}>
            <Icon name="user-plus" className="size-4" />
            إضافة حساب
          </Button>
        ) : null} />

      {secret ? <TemporaryPassword {...secret} onDone={() => setSecret(null)} /> : null}
      {adding ? (
        <AddUser onCancel={() => setAdding(false)}
          onCreated={(r) => { setAdding(false); setSecret({ name: r.user.display_name, password: r.temporary_password, username: r.user.username }); }} />
      ) : null}

      {users.isPending ? (
        <Loading />
      ) : users.isError ? (
        <ErrorState message={errorMessage(users.error)} onRetry={() => void users.refetch()} />
      ) : users.data.length === 0 ? (
        <Nothing />
      ) : (
        <>
          {waiting.length > 0 ? (
            <section className="grid gap-3" aria-labelledby="pending-title">
              <Heading id="pending-title" icon="user-plus" count={waiting.length} hot>
                في انتظار التفعيل
              </Heading>
              <ul className="grid gap-3 lg:grid-cols-2" aria-label="حسابات في انتظار التفعيل">
                {waiting.map((u) => (
                  <li key={u.id}><PendingCard user={u} /></li>
                ))}
              </ul>
            </section>
          ) : null}
          <section className="grid gap-3" aria-labelledby="accounts-title">
            <Heading id="accounts-title" icon="users" count={users.data.length - waiting.length}>
              حسابات المكتب
            </Heading>
            <ul className="grid gap-3 lg:grid-cols-2" aria-label="الحسابات">
              {users.data.filter((u) => !u.pending).map((u) => (
                <li key={u.id}>
                  <UserCard user={u} onSecret={setSecret} />
                </li>
              ))}
            </ul>
          </section>
        </>
      )}
    </div>
  );
}

// ---------- the top ----------

/** Arabic number agreement: حساب واحد، حسابان، 3 حسابات، 11 حسابًا، 100 حساب. */
const accountsCount = (n: number) => {
  const r = n % 100;
  if (n === 1) return "حساب واحد";
  if (n === 2) return "حسابان";
  if (r >= 3 && r <= 10) return `${n} حسابات`;
  if (r >= 11) return `${n} حسابًا`;
  return `${n} حساب`;
};

function Intro({ total, action }: { total?: number | undefined; action: ReactNode }) {
  return (
    <header className="relative">
      <div className="relative flex flex-wrap items-center gap-4">
        <span aria-hidden className="grid size-11 shrink-0 place-items-center rounded-[14px] bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6),0_8px_20px_-10px_rgb(140_98_58/0.7)]">
          <Icon name="users" className="size-6" />
        </span>
        <div className="flex min-w-0 flex-1 basis-56 flex-wrap items-center gap-2">
          <h1 className="text-[26px] leading-tight font-bold">الحسابات</h1>
          {total !== undefined ? (
            <span className="tabular rounded-full border border-gold-line bg-gold-tint px-2.5 py-0.5 text-[12.5px] font-bold text-gold-ink">{accountsCount(total)}</span>
          ) : null}
        </div>
        {action ? <div className="w-full sm:w-auto">{action}</div> : null}
      </div>
    </header>
  );
}

/** A section's title on the page: an icon tile, the words and a count. */
function Heading({ id, icon, count, hot = false, children }: { id: string; icon: IconName; count: number; hot?: boolean; children: ReactNode }) {
  return (
    <div className="flex items-center gap-3">
      <span aria-hidden className="grid size-10 shrink-0 place-items-center rounded-2xl bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.5)]">
        <Icon name={icon} className="size-5" />
      </span>
      <h2 id={id} className="flex min-w-0 flex-wrap items-center gap-2 text-[17px] font-bold">
        {children}
        <span className={`tabular grid min-w-6 place-items-center rounded-full px-1.5 text-xs font-bold ${hot ? "bg-red text-white" : "border border-gold-line bg-gold-tint text-gold-ink"}`}>
          {count}
        </span>
      </h2>
    </div>
  );
}

/** The see-through card of this page; the line at its start says its state. */
function GlassCard({ bar, className = "", children }: { bar: string; className?: string; children: ReactNode }) {
  return (
    <section className={`relative grid h-full content-start gap-4 overflow-hidden rounded-[20px] border border-line/60 bg-card/60 py-4 ps-5 pe-4 shadow-[inset_0_1px_0_rgb(255_255_255/0.45),0_6px_18px_-12px_rgb(60_44_20/0.35)] backdrop-blur-md transition hover:-translate-y-px hover:shadow-[0_14px_30px_-14px_rgb(60_44_20/0.4)] motion-reduce:transition-none motion-reduce:hover:translate-y-0 ${className}`}>
      <span aria-hidden className={`absolute inset-y-4 start-0 w-1 rounded-e-full ${bar}`} />
      {children}
    </section>
  );
}

function Detail({ icon, term, children }: { icon: IconName; term: string; children: ReactNode }) {
  return (
    <div className="grid min-w-0 gap-0.5 rounded-2xl border border-line/60 bg-card/50 px-3 py-2">
      <dt className="flex items-center gap-1 text-[11.5px] text-ink-soft">
        <Icon name={icon} className="size-3.5" />
        {term}
      </dt>
      <dd className="min-w-0 text-[13.5px] leading-snug font-semibold">{children}</dd>
    </div>
  );
}

function Who({ user, self = false }: { user: PanelUser; self?: boolean }) {
  return (
    <span className="flex min-w-0 items-center gap-3">
      <Avatar name={user.display_name} className="size-11 shrink-0 text-[13px]" />
      <span className="grid min-w-0 gap-0.5">
        <span className="text-[15px] font-bold">{user.display_name}{self ? " (أنت)" : ""}</span>
        <span className="text-xs text-ink-soft break-all" dir="ltr" style={{ textAlign: "right" }}>{user.username} · {user.email}</span>
      </span>
    </span>
  );
}

function Nothing() {
  return (
    <div className="liquid grid justify-items-center gap-2 rounded-[22px] px-6 py-10 text-center">
      <span className="grid size-14 place-items-center rounded-full bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6)]">
        <Icon name="users" className="size-7" />
      </span>
      <p className="text-[16px] font-bold">لا توجد حسابات بعد</p>
    </div>
  );
}

// ---------- a new account, and its password shown once ----------

/** Shown once: the server never returns it again. */
function TemporaryPassword({ name, password, username, onDone }: { name: string; password: string; username?: string | undefined; onDone: () => void }) {
  const [copied, setCopied] = useState<"password" | "message" | null>(null);
  // the message to send to the person (WhatsApp): where to go, the user name, the temporary password and what happens next
  const message = [
    `مرحبًا ${name.split(/\s+/)[0]} 👋 حسابك في لوحة المكتب جاهز.`,
    `الرابط: ${window.location.origin}`,
    username ? `اسم المستخدم: ${username}` : null,
    `كلمة السر المؤقتة: ${password}`,
    "عند أول دخول ستطلب منك اللوحة تغييرها إلى كلمة سر خاصة بك.",
  ].filter(Boolean).join("\n");
  const copy = async (text: string, what: "password" | "message") => {
    try { await navigator.clipboard.writeText(text); setCopied(what); } catch { setCopied(null); }
  };
  return (
    <GlassCard bar="bg-orange" className="border-orange/40">
      <div className="flex items-start gap-3">
        <span aria-hidden className="grid size-10 shrink-0 place-items-center rounded-2xl bg-orange-bg text-orange-ink">
          <Icon name="key" className="size-5" />
        </span>
        <div className="grid gap-0.5">
          <h2 className="text-[17px] font-bold">كلمة السر المؤقتة لحساب {name}</h2>
          <p className="text-[13px] text-ink-soft">
            تظهر هذه المرة فقط. أرسلها إلى صاحب الحساب بنفسك، وسيغيّرها عند أول دخول.
          </p>
        </div>
      </div>
      <code className="tabular select-all rounded-[14px] border border-dashed border-gold-line bg-card/70 px-4 py-3 text-lg break-all" dir="ltr">
        {password}
      </code>
      <pre className="whitespace-pre-wrap rounded-[14px] border border-line/60 bg-card-2/50 px-4 py-3 text-[13px] leading-relaxed font-sans" aria-label="الرسالة التي سترسلها">{message}</pre>
      <div className="flex flex-wrap gap-2">
        <Button className="rounded-full" onClick={() => void copy(message, "message")}>{copied === "message" ? "تم نسخ الرسالة" : "انسخ الرسالة كاملة"}</Button>
        <Button variant="ghost" className="rounded-full" onClick={() => void copy(password, "password")}>{copied === "password" ? "تم النسخ" : "انسخ كلمة السر فقط"}</Button>
        <Button variant="primary" className="rounded-full" onClick={onDone}>تم، أرسلتها إليه</Button>
      </div>
    </GlassCard>
  );
}

function AddUser({ onCancel, onCreated }: { onCancel: () => void; onCreated: (r: NewUserResult) => void }) {
  const qc = useQueryClient();
  const { data: me } = useMe();
  const [form, setForm] = useState({ username: "", display_name: "", email: "", role: "staff" as Role, staff_name: "" });
  const create = useMutation({
    mutationFn: () => api<NewUserResult>("POST", "/users", { ...form, staff_name: form.role === "staff" ? form.staff_name || form.display_name : null }),
    onSuccess: (r) => { void qc.invalidateQueries({ queryKey: usersKey }); onCreated(r); },
  });
  // the API's per-field codes (invalid, username_taken, email_taken) in Arabic, like the sign-up page
  const fieldError = (name: string) => {
    const code = create.error instanceof ApiError ? create.error.fields?.[name]?.[0] : undefined;
    if (!code) return undefined;
    if (code === "invalid") {
      return { username: "حروف إنجليزية صغيرة وأرقام ونقطة أو شرطة، من 3 إلى 32 حرفًا.", email: "اكتب بريدًا إلكترونيًا صحيحًا.",
        display_name: "اكتب الاسم.", staff_name: "هذا الاسم غير صحيح." }[name] ?? "غير صحيح.";
    }
    return errorMessage(new ApiError(400, code, code));
  };
  // the general line only when no field under its input already says what is wrong
  const shown = ["display_name", "username", "email", ...(form.role === "staff" ? ["staff_name"] : [])];
  const onlyFields = create.error instanceof ApiError && Object.keys(create.error.fields ?? {}).length > 0
    && Object.keys(create.error.fields ?? {}).every((k) => shown.includes(k));
  const set = (k: keyof typeof form) => (e: { target: { value: string } }) => setForm({ ...form, [k]: e.target.value });

  function submit(e: FormEvent) {
    e.preventDefault();
    create.mutate();
  }

  return (
    <section className="liquid grid gap-4 rounded-[22px] p-5">
      <div className="flex items-center gap-3">
        <span aria-hidden className="grid size-10 shrink-0 place-items-center rounded-2xl bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.5)]">
          <Icon name="user-plus" className="size-5" />
        </span>
        <h2 className="text-[17px] font-bold">حساب جديد</h2>
      </div>
      <form onSubmit={submit} className="grid gap-4 md:grid-cols-2" noValidate>
        <Field label="الاسم الظاهر" required value={form.display_name} onChange={set("display_name")} error={fieldError("display_name")} />
        <Field label="اسم المستخدم (للدخول)" required dir="ltr" autoComplete="off" value={form.username}
          hint="حروف إنجليزية صغيرة وأرقام، دون مسافات." onChange={set("username")} error={fieldError("username")} />
        <Field label="البريد الإلكتروني" type="email" required dir="ltr" value={form.email}
          hint="سيُستخدم أيضًا لحساب Chatwoot." onChange={set("email")} error={fieldError("email")} />
        <label className="grid gap-1.5">
          <span className="text-sm font-medium">الدور <span className="text-bad">*</span></span>
          <select className={select} value={form.role}
            onChange={(e) => setForm({ ...form, role: e.target.value as Role })}>
            <option value="staff">موظف: مهامه والمهام غير المُسندة</option>
            <option value="lawyer">المستشار: كل المهام، ويوزّعها</option>
            <option value="manager">مدير: كل الصلاحيات، ومنها الحسابات</option>
            {can(me, "maintenance.manage") ? <option value="maintenance">الصيانة: كل الصلاحيات، ومنها نصوص حازم</option> : null}
          </select>
        </label>
        {form.role === "staff" ? (
          <Field label="اسمه في قوائم المكتب" value={form.staff_name} onChange={set("staff_name")}
            hint="كما هو مكتوب في عمود الموظف في قائمة الموكّلين. إذا تُرك فارغًا، يُستخدم الاسم الظاهر أعلاه."
            error={fieldError("staff_name")} />
        ) : null}
        {create.isError && !onlyFields ? (
          <p className="text-sm text-bad md:col-span-2" role="alert">{errorMessage(create.error)}</p>
        ) : null}
        <div className="flex flex-wrap gap-2 md:col-span-2">
          <Button type="submit" variant="primary" className="rounded-full" busy={create.isPending}
            disabled={!form.username || !form.display_name || !form.email}>إنشاء الحساب</Button>
          <Button variant="ghost" className="rounded-full" onClick={onCancel}>إلغاء</Button>
        </div>
      </form>
    </section>
  );
}

// ---------- one account ----------

/**
 * Registered from the sign-up page (v1.9): once the e-mail is confirmed, the manager picks the role
 * and approves it, or refuses it (the registration is deleted and the person gets an e-mail).
 */
function PendingCard({ user }: { user: PanelUser }) {
  const qc = useQueryClient();
  const { data: me } = useMe();
  const [role, setRole] = useState<Role>("staff");
  const [staffName, setStaffName] = useState(user.display_name);
  const [refusing, setRefusing] = useState(false);
  const act = useMutation({
    mutationFn: (path: "approve" | "reject") => api<unknown>("POST", `/users/${user.id}/${path}`,
      path === "approve" ? { role, staff_name: role === "staff" ? staffName.trim() || user.display_name : null } : undefined),
    onSuccess: (_r, path) => {
      void qc.invalidateQueries({ queryKey: usersKey });
      toast(path === "approve" ? `تم تفعيل حساب ${user.display_name} ✅ وسيدخل بكلمة السر التي اختارها` : `تم رفض طلب ${user.display_name}`);
    },
  });

  return (
    <GlassCard bar={user.email_verified ? "bg-gold" : "bg-ink-soft/50"} className="border-gold-line/70">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <Who user={user} />
        {user.email_verified ? <Chip tone="gold">في انتظار التفعيل</Chip> : <Chip tone="grey">لم يؤكد البريد الإلكتروني</Chip>}
      </div>
      <p className="flex items-center gap-1.5 text-[12px] text-ink-soft">
        <Icon name="clock" className="size-3.5" />
        <span className="tabular">سجّل {cairoWhen(user.created_at)}</span>
      </p>

      {user.email_verified ? (
        <div className="grid gap-3 sm:grid-cols-2">
          <label className="grid gap-1.5">
            <span className="text-sm font-semibold">الدور</span>
            <select className={select} value={role}
              onChange={(e) => setRole(e.target.value as Role)}>
              <option value="staff">موظف: مهامه والمهام غير المُسندة</option>
              <option value="lawyer">المستشار: كل المهام، ويوزّعها</option>
              <option value="manager">مدير: كل الصلاحيات، ومنها الحسابات</option>
              {can(me, "maintenance.manage") ? <option value="maintenance">الصيانة: كل الصلاحيات، ومنها نصوص حازم</option> : null}
            </select>
          </label>
          {role === "staff" ? (
            <Field label="اسمه في قوائم المكتب" value={staffName} onChange={(e) => setStaffName(e.target.value)}
              hint="كما هو مكتوب في عمود الموظف في قائمة الموكّلين." />
          ) : null}
          <p className="flex items-start gap-1.5 text-[12.5px] text-ink-soft sm:col-span-2">
            <Icon name="info" className="mt-0.5 size-3.5 shrink-0 text-gold-ink" />
            بعد تفعيله يمكنه الدخول بكلمة السر التي اختارها عند التسجيل (لا يحتاج إلى كلمة سر مؤقتة).
          </p>
        </div>
      ) : (
        <p className="flex items-start gap-1.5 text-[13px] text-ink-soft">
          <Icon name="info" className="mt-0.5 size-3.5 shrink-0 text-gold-ink" />
          لا يمكن تفعيله إلا بعد أن يؤكد بريده الإلكتروني. إذا لم يؤكده خلال يوم، يُحذف الطلب تلقائيًا.
        </p>
      )}

      {refusing ? (
        <div className="flex flex-wrap items-center gap-2 rounded-2xl border border-red/30 bg-red-bg/60 p-2.5">
          <span className="text-sm">هل أنت متأكد؟ سيُحذف الطلب.</span>
          <Button variant="danger" className="rounded-full" busy={act.isPending} onClick={() => act.mutate("reject")}>نعم، ارفض</Button>
          <Button variant="ghost" className="rounded-full" onClick={() => setRefusing(false)}>لا</Button>
        </div>
      ) : (
        <div className="flex flex-wrap gap-2">
          {user.email_verified ? (
            <Button variant="primary" className="rounded-full" busy={act.isPending} onClick={() => act.mutate("approve")}>
              <Icon name="check" className="size-4" />فعّل الحساب
            </Button>
          ) : null}
          <Button variant="ghost" className="rounded-full" onClick={() => setRefusing(true)}>ارفض</Button>
        </div>
      )}
      {act.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(act.error)}</p> : null}
    </GlassCard>
  );
}

function UserCard({ user, onSecret }: { user: PanelUser; onSecret: (s: { name: string; password: string; username?: string }) => void }) {
  const { data: me } = useMe();
  const qc = useQueryClient();
  const self = me?.id === user.id;
  // v1.100: the office sees the maintenance account, never changes it (the API refuses too)
  const maintenance = can(me, "maintenance.manage");
  const fixed = self || (user.role === "maintenance" && !maintenance);
  const roles: Role[] = maintenance ? ["staff", "lawyer", "manager", "maintenance"] : ["staff", "lawyer", "manager"];
  const [confirm, setConfirm] = useState<null | "disable" | "reset" | "reset-2fa">(null);
  const change = useMutation({
    mutationFn: (body: { path: string; payload?: unknown }) => api<PanelUser | { temporary_password: string }>("POST", `/users/${user.id}/${body.path}`, body.payload),
    onSuccess: (r) => {
      setConfirm(null);
      void qc.invalidateQueries({ queryKey: usersKey });
      if ("temporary_password" in r) onSecret({ name: user.display_name, password: r.temporary_password, username: user.username });
    },
  });
  const bar = !user.active ? "bg-ink-soft/50" : user.locked ? "bg-orange" : "bg-green";

  return (
    <GlassCard bar={bar} className={user.active ? "" : "opacity-75"}>
      <div className="flex flex-wrap items-start justify-between gap-2">
        <Who user={user} self={self} />
        <span className="flex flex-wrap justify-end gap-1.5">
          {user.two_factor ? <Chip tone="green">التحقق بخطوتين</Chip> : null}
          {!user.active ? <Chip tone="cancelled">معطّل</Chip> : user.locked ? <Chip tone="overdue">مقفل مؤقتًا</Chip> : <Chip tone="in_progress">نشط</Chip>}
        </span>
      </div>
      <dl className="grid gap-2 sm:grid-cols-[repeat(auto-fit,minmax(9.5rem,1fr))]">
        <Detail icon="shield" term="الدور">
          {fixed ? label(ROLES, user.role ?? "") : (
            <select aria-label={`دور ${user.display_name}`} className="min-h-9 w-full rounded-[12px] border border-line/70 bg-card/70 px-2 font-normal text-ink focus:border-gold"
              value={user.role ?? "staff"} disabled={change.isPending}
              onChange={async (e) => {
                // the select is controlled by user.role: on «no» it stays where it was
                const role = e.target.value as Role;
                const ok = await confirmAction({ title: `هل تريد تغيير دور ${user.display_name} إلى ${label(ROLES, role)}؟`,
                  body: "سيُسجَّل خروجه من اللوحة، وعليه الدخول مرة أخرى.", yes: "نعم، غيّره" });
                if (ok) change.mutate({ path: "role", payload: { role } });
              }}>
              {roles.map((r) => <option key={r} value={r}>{label(ROLES, r)}</option>)}
            </select>
          )}
        </Detail>
        {user.staff ? <Detail icon="clients" term="في القوائم">{user.staff.name}</Detail> : null}
        <Detail icon="clock" term="آخر دخول">
          <span className="tabular">{user.last_login_at ? cairoWhen(user.last_login_at) : "لم يدخل بعد"}</span>
        </Detail>
      </dl>

      {!fixed ? (
        confirm ? (
          <div className="flex flex-wrap items-center gap-2 rounded-2xl border border-red/30 bg-red-bg/60 p-2.5">
            <span className="text-sm">
              {confirm === "disable" ? "هل أنت متأكد؟ سيُسجَّل خروجه من كل الأجهزة." : confirm === "reset-2fa" ? "هل أنت متأكد؟ سيدخل بكلمة السر فقط إلى أن يفعّل التحقق بخطوتين مرة أخرى." : "هل أنت متأكد؟ ستتوقف كلمة السر الحالية عن العمل."}
            </span>
            <Button variant="danger" className="rounded-full" busy={change.isPending}
              onClick={() => change.mutate({ path: confirm === "disable" ? "disable" : confirm === "reset-2fa" ? "reset-2fa" : "reset-password" })}>نعم</Button>
            <Button variant="ghost" className="rounded-full" onClick={() => setConfirm(null)}>لا</Button>
          </div>
        ) : (
          <div className="flex flex-wrap gap-2 border-t border-line/50 pt-3">
            {user.active ? (
              <Button variant="ghost" className="rounded-full" onClick={() => setConfirm("disable")}><Icon name="lock" className="size-4" />عطّل</Button>
            ) : (
              <Button className="rounded-full" onClick={() => change.mutate({ path: "enable" })} busy={change.isPending}>فعّل</Button>
            )}
            <Button variant="ghost" className="rounded-full" onClick={() => setConfirm("reset")}><Icon name="key" className="size-4" />كلمة سر جديدة</Button>
            {user.two_factor ? (
              <Button variant="ghost" className="rounded-full" onClick={() => setConfirm("reset-2fa")}>إيقاف التحقق بخطوتين (فُقد الهاتف)</Button>
            ) : null}
          </div>
        )
      ) : null}
      {change.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(change.error)}</p> : null}
    </GlassCard>
  );
}
