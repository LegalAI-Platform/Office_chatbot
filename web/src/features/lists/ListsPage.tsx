import { useEffect, useState, type FormEvent, type ReactNode } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError, errorMessage } from "../../shared/api/client";
import { can, useMe } from "../../shared/api/session";
import type { ListEntry, ListKind, MutedConversation, StaffRef } from "../../shared/api/types";
import { label, LIST_KINDS } from "../../shared/lib/labels";
import { cairoWhen } from "../../shared/lib/time";
import { phoneOf } from "../../shared/ui/caseCard";
import { confirmAction } from "../../shared/ui/feedback";
import { Icon, type IconName } from "../../shared/ui/Icon";
import { Avatar, Button, ErrorState, Field, Loading } from "../../shared/ui/ui";

const KINDS: ListKind[] = ["existing_client", "lawyer_direct", "blocked"];
// each list's icon and the colour of the line at its rows' start
const KIND_LOOK: Record<ListKind, { icon: IconName; bar: string }> = {
  existing_client: { icon: "clients", bar: "bg-green" },
  lawyer_direct: { icon: "shield", bar: "bg-gold" },
  blocked: { icon: "lock", bar: "bg-red" },
};
const ERRORS: Record<string, string> = {
  invalid_phone: "الرقم مش صحيح. اكتبه زي 01012345678 أو +201012345678.",
  too_long: "الاسم أو الملاحظة أطول من اللازم.",
  unknown_staff: "اختار الموظف من القايمة.",
};
const PAGE = 50; // the API's page
const MAX_CSV_BYTES = 600_000; // 300,000 Arabic characters: what the API takes

const select = "min-h-11 rounded-[14px] border border-line/70 bg-card/70 px-3 font-normal text-ink shadow-[inset_0_1px_2px_rgb(0_0_0/0.05)] focus:border-gold";

/**
 * The office's lists (B-14, TASK 07c): who Hazem treats as a current client, the lawyer's own list,
 * and the blocked. The manager and the lawyer. And (the manager only) the muted conversations (S-01).
 * As «liquid glass»: a glass header, a glass list picker, then adding a number, importing a sheet, and the list itself.
 */
export function ListsPage() {
  const { data: me } = useMe();
  const [kind, setKind] = useState<ListKind>("existing_client");
  const [q, setQ] = useState("");
  const [page, setPage] = useState(1);
  const qc = useQueryClient();
  const key = ["lists", kind, q, page];
  const list = useQuery({
    queryKey: key,
    queryFn: () => api<{ total: number; items: ListEntry[] }>("GET",
      `/lists?kind=${kind}&page=${page}${q.trim() ? `&q=${encodeURIComponent(q.trim())}` : ""}`),
  });
  const pages = list.data ? Math.max(1, Math.ceil(list.data.total / PAGE)) : 1;
  const remove = useMutation({
    mutationFn: (id: string) => api<void>("DELETE", `/lists/${id}`),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ["lists"] }),
  });
  const look = KIND_LOOK[kind];
  // the last row of the last page removed: back to the page that still has rows
  useEffect(() => {
    if (list.data && page > pages) setPage(pages);
  }, [list.data, page, pages]);

  return (
    <div className="relative isolate grid gap-5 pb-4">
      {/* the soft light the glass shows through */}
      <div aria-hidden className="pointer-events-none absolute inset-x-0 -top-6 -z-10 h-[30rem] overflow-hidden">
        <span className="absolute -top-16 end-[4%] size-[24rem] rounded-full bg-gold/30 blur-[100px]" />
        <span className="absolute top-24 start-[10%] size-[20rem] rounded-full bg-blue/10 blur-[100px]" />
      </div>

      <Intro />

      <section aria-label="اختار القايمة" className="liquid grid gap-2 rounded-[22px] p-4 sm:p-5">
        <div className="flex flex-wrap gap-1 rounded-[22px] border border-line/60 bg-card/45 p-1 shadow-[inset_0_1px_2px_rgb(0_0_0/0.06)] sm:rounded-full" role="group" aria-label="القايمة">
          {KINDS.map((k) => {
            const on = k === kind;
            return (
              <button key={k} type="button" aria-pressed={on} onClick={() => { setKind(k); setPage(1); }}
                className={`flex min-h-9 shrink-0 grow items-center justify-center gap-1.5 whitespace-nowrap rounded-full px-3.5 text-[13.5px] transition ${on
                  ? "btn-gold font-bold shadow-[inset_0_1px_0_rgb(255_255_255/0.4),0_6px_14px_-6px_rgb(140_98_58/0.8)]"
                  : "text-ink hover:bg-card/80"}`}>
                <Icon name={KIND_LOOK[k].icon} className="size-4" />
                {label(LIST_KINDS, k)}
              </button>
            );
          })}
        </div>
      </section>

      <div className="grid gap-5 xl:grid-cols-2">
        <AddEntry kind={kind} />
        {/* keyed by the list: «replace it all» never carries over to another list */}
        <ImportCsv key={kind} kind={kind} />
      </div>

      <GlassCard icon={look.icon} title={label(LIST_KINDS, kind)} count={list.data?.total}
        action={
          <div className="w-full sm:max-w-xs">
            <Field label="دوّر بالاسم أو آخر أرقام" value={q} onChange={(e) => { setQ(e.target.value); setPage(1); }} />
          </div>
        }>
        {list.isPending ? <Loading /> : list.isError ? <ErrorState message={errorMessage(list.error)} onRetry={() => void list.refetch()} /> :
          list.data.items.length === 0 ? (
            <Nothing title="القايمة فاضية" />
          ) : (
            <ul className="grid gap-2">
              {list.data.items.map((e) => (
                <li key={e.id} className="relative flex flex-wrap items-center gap-x-4 gap-y-2 overflow-hidden rounded-[16px] border border-line/60 bg-card/55 py-2.5 ps-4 pe-3 shadow-[inset_0_1px_0_rgb(255_255_255/0.45)] transition hover:bg-card/80 motion-reduce:transition-none">
                  <span aria-hidden className={`absolute inset-y-2.5 start-0 w-1 rounded-e-full ${look.bar}`} />
                  <span className="flex min-w-0 flex-1 basis-48 items-center gap-3">
                    <Avatar name={e.name ?? "؟"} className="size-9 shrink-0 text-[11px]" />
                    <span className="grid min-w-0">
                      <span className="font-semibold">{e.name ?? "من غير اسم"}</span>
                      <span className="tabular text-xs text-ink-soft" dir="ltr" style={{ textAlign: "right" }}>+{e.wa_id}</span>
                    </span>
                  </span>
                  {e.staff_name ? (
                    <span className="flex items-center gap-1 text-sm text-ink-soft"><Icon name="account" className="size-3.5" />مع {e.staff_name}</span>
                  ) : null}
                  {e.note ? <span className="min-w-0 max-w-full text-[12.5px] text-ink-soft break-words">{e.note}</span> : null}
                  <span className="flex items-center gap-1 text-xs text-ink-soft">
                    <Icon name="clock" className="size-3.5" />
                    <span className="tabular">{cairoWhen(e.imported_at)}</span>
                  </span>
                  <Button variant="ghost" className="min-h-9 rounded-full px-3.5" busy={remove.isPending && remove.variables === e.id} onClick={async () => {
                      if (await confirmAction({ title: `تشيل ${e.name ?? `+${e.wa_id}`} من ${label(LIST_KINDS, kind)}؟`, yes: "أيوه، شيله", danger: true }))
                        remove.mutate(e.id);
                    }}
                    aria-label={`شيل ${e.name ?? e.wa_id} من القايمة`}>
                    <Icon name="x" className="size-4" />شيل
                  </Button>
                </li>
              ))}
            </ul>
          )}
        {pages > 1 ? <Pager page={page} pages={pages} onPage={setPage} /> : null}
        {remove.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(remove.error)}</p> : null}
      </GlassCard>

      {can(me, "conversations.unmute") ? <Muted /> : null}
    </div>
  );
}

// ---------- the top ----------

function Intro() {
  return (
    <header className="relative">
      <div className="relative flex items-start gap-4">
        <span aria-hidden className="grid size-11 shrink-0 place-items-center rounded-[14px] bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6),0_8px_20px_-10px_rgb(140_98_58/0.7)]">
          <Icon name="clients" className="size-6" />
        </span>
        <div className="grid min-w-0 gap-1">
          <h1 className="text-[26px] leading-tight font-bold">قوايم المكتب</h1>
        </div>
      </div>
    </header>
  );
}

/** The see-through card of this page, with an icon tile, a title, a count and (when it has one) a line under the title. */
function GlassCard({ icon, title, count, hint, action, children }: {
  icon: IconName; title: string; count?: number | undefined; hint?: string; action?: ReactNode; children: ReactNode;
}) {
  return (
    <section className="liquid grid min-w-0 content-start gap-4 rounded-[22px] p-5">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="flex min-w-0 flex-1 basis-60 items-start gap-3">
          <span aria-hidden className="grid size-10 shrink-0 place-items-center rounded-2xl bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.5)]">
            <Icon name={icon} className="size-5" />
          </span>
          <div className="grid min-w-0 gap-0.5">
            <h2 className="flex flex-wrap items-center gap-2 text-[17px] font-bold">
              {title}
              {count !== undefined ? (
                <span className="tabular rounded-full border border-gold-line bg-gold-tint px-2 py-0.5 text-[12px] font-bold text-gold-ink">({count})</span>
              ) : null}
            </h2>
            {hint ? <p className="text-[12.5px] text-ink-soft">{hint}</p> : null}
          </div>
        </div>
        {action}
      </div>
      {children}
    </section>
  );
}

function Nothing({ title }: { title: string }) {
  return (
    <div className="grid justify-items-center gap-2 rounded-[18px] border border-dashed border-gold-line/70 bg-card/40 px-6 py-8 text-center">
      <span className="grid size-12 place-items-center rounded-full bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6)]">
        <Icon name="check-circle" className="size-6" />
      </span>
      <p className="text-[15px] font-bold">{title}</p>
    </div>
  );
}

function Pager({ page, pages, onPage }: { page: number; pages: number; onPage: (p: number) => void }) {
  const pill = "flex min-h-9 items-center gap-1.5 rounded-full border border-line/60 bg-card/60 px-4 text-[13px] font-semibold shadow-[inset_0_1px_0_rgb(255_255_255/0.45)] transition hover:border-gold-line hover:bg-gold-tint disabled:pointer-events-none disabled:opacity-45";
  return (
    <nav className="flex flex-wrap items-center justify-center gap-3 pt-1" aria-label="صفحات القايمة">
      <button type="button" className={pill} disabled={page <= 1} onClick={() => onPage(page - 1)}>
        <Icon name="chevron-end" className="size-4" />اللي قبل
      </button>
      <span className="tabular rounded-full bg-gold-tint px-3 py-1 text-[13px] font-bold text-gold-ink">صفحة {page} من {pages}</span>
      <button type="button" className={pill} disabled={page >= pages} onClick={() => onPage(page + 1)}>
        اللي بعد<Icon name="chevron-start" className="size-4" />
      </button>
    </nav>
  );
}

// ---------- adding numbers ----------

function AddEntry({ kind }: { kind: ListKind }) {
  const qc = useQueryClient();
  // The same list (and cache key) as assigning a task.
  const staff = useQuery({ queryKey: ["staff"], queryFn: () => api<StaffRef[]>("GET", "/staff"), enabled: kind === "existing_client", staleTime: 300_000 });
  const [form, setForm] = useState({ phone: "", name: "", staff_name: "", note: "" });
  const add = useMutation({
    mutationFn: () => api<void>("POST", "/lists", { kind, ...form, staff_name: kind === "existing_client" ? form.staff_name : null }),
    onSuccess: () => { setForm({ phone: "", name: "", staff_name: "", note: "" }); void qc.invalidateQueries({ queryKey: ["lists"] }); },
  });
  const error = add.error instanceof ApiError && add.error.code ? ERRORS[add.error.code] ?? errorMessage(add.error) : add.isError ? errorMessage(add.error) : undefined;
  const set = (k: keyof typeof form) => (e: { target: { value: string } }) => setForm({ ...form, [k]: e.target.value });

  return (
    <GlassCard icon="user-plus" title="ضيف رقم">
      <form onSubmit={(e: FormEvent) => { e.preventDefault(); add.mutate(); }} className="grid gap-3 md:grid-cols-2" noValidate>
        <Field label="الرقم" required dir="ltr" inputMode="tel" value={form.phone} onChange={set("phone")} />
        <Field label="الاسم" value={form.name} onChange={set("name")} />
        {kind === "existing_client" ? (
          <label className="grid gap-1.5 text-sm font-semibold">
            الموظف المسؤول
            <select className={select} value={form.staff_name} onChange={set("staff_name")}>
              <option value="">من غير موظف</option>
              {(staff.data ?? []).map((s) => <option key={s.id} value={s.name}>{s.name}</option>)}
            </select>
          </label>
        ) : null}
        <Field label="ملاحظة" value={form.note} onChange={set("note")} />
        {error ? <p className="text-sm text-bad md:col-span-2" role="alert">{error}</p> : null}
        <div className="md:col-span-2"><Button type="submit" variant="primary" className="rounded-full" busy={add.isPending} disabled={!form.phone.trim()}>ضيف</Button></div>
      </form>
    </GlassCard>
  );
}

function ImportCsv({ kind }: { kind: ListKind }) {
  const qc = useQueryClient();
  const [replace, setReplace] = useState(false);
  const [tooBig, setTooBig] = useState(false);
  const run = useMutation({
    mutationFn: async (file: File) =>
      api<{ imported: number; rejected: { line: number; reason: string }[] }>("POST", "/lists/import", { kind, csv: await file.text(), replace }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ["lists"] }),
  });

  return (
    <GlassCard icon="document" title="استورد من Excel">
      <p className="text-[13px] text-ink-soft">احفظ الشيت كـ CSV فيه عمود <code dir="ltr">phone</code> (ومعاه <code dir="ltr">name</code>، و<code dir="ltr">staff</code> للموكلين). الأرقام الغلط بتتعدّى وبيتقالك سطرها.</p>
      <label className="flex items-start gap-2 rounded-2xl border border-line/60 bg-card/50 px-3 py-2.5 text-sm">
        <input type="checkbox" className="mt-1 accent-gold" checked={replace} onChange={(e) => setReplace(e.target.checked)} />
        استبدل القايمة كلها بالملف ده (لو فيه سطر غلط، مفيش حاجة بتتغير)
      </label>
      {/* the browser's own file button speaks English: a hidden input under an Arabic one */}
      <label className={`flex flex-wrap items-center gap-3 rounded-2xl border border-dashed border-gold-line/70 bg-card/40 p-3 text-sm text-ink-soft focus-within:border-gold ${run.isPending ? "opacity-60" : "cursor-pointer hover:bg-card/70"}`}>
        <span className="btn-gold inline-flex min-h-9 items-center gap-1.5 rounded-full border px-4 font-semibold">
          {run.isPending ? <span className="size-4 animate-spin rounded-full border-2 border-current border-t-transparent" aria-hidden /> : <Icon name="document" className="size-4" />}
          اختار الملف
        </span>
        {run.isPending ? "بيستورد…" : "ملف CSV، أقل من 600 كيلو."}
        <input type="file" accept=".csv,text/csv" aria-label="ملف CSV" disabled={run.isPending} className="sr-only"
          onChange={async (e) => {
            const input = e.target;
            const f = input.files?.[0];
            input.value = "";
            setTooBig(!!f && f.size > MAX_CSV_BYTES);
            if (!f || f.size > MAX_CSV_BYTES) return;
            if (replace && !(await confirmAction({ title: `تستبدل ${label(LIST_KINDS, kind)} كلها بالملف ده؟`,
              body: "كل الأرقام اللي فيها دلوقتي هتتشال، ويفضل اللي في الملف بس.", yes: "أيوه، استبدلها", danger: true }))) return;
            run.mutate(f);
          }} />
      </label>
      {tooBig ? <p className="text-sm text-bad" role="alert">الملف كبير. قسّمه لملفات أصغر من 600 كيلو (حوالي 6,000 سطر) واستوردها واحد ورا التاني.</p> : null}
      {run.isSuccess ? (
        <p className="rounded-2xl bg-green-bg px-3 py-2 text-sm text-green-ink" role="status">
          اتضاف {run.data.imported}.{run.data.rejected.length > 0 ? ` واتعدّى ${run.data.rejected.length}: ${run.data.rejected.slice(0, 5).map((r) => `سطر ${r.line}`).join("، ")}` : ""}
        </p>
      ) : null}
      {run.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(run.error)}</p> : null}
    </GlassCard>
  );
}

// ---------- the muted conversations ----------

/** S-01: a conversation Hazem went silent in (abuse, spam, a blocked number) comes back only by the manager's decision. */
function Muted() {
  const qc = useQueryClient();
  const muted = useQuery({ queryKey: ["muted"], queryFn: () => api<MutedConversation[]>("GET", "/conversations/muted") });
  const unmute = useMutation({
    mutationFn: (wa: string) => api<void>("POST", "/conversations/unmute", { wa_id: wa }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ["muted"] }),
  });

  return (
    <GlassCard icon="alert-circle" title="محادثات حازم ساكت فيها" count={muted.data?.length}
      hint="بسبب شتيمة أو سبام أو رقم ممنوع. لو رجّعتها، حازم بيرد على الرقم ده تاني.">
      {muted.isPending ? <Loading /> : muted.isError ? <ErrorState message={errorMessage(muted.error)} onRetry={() => void muted.refetch()} /> :
        muted.data.length === 0 ? <Nothing title="مفيش محادثات ساكتة" /> : (
          <ul className="grid gap-2">
            {muted.data.map((m) => (
              <li key={m.wa_id} className="relative flex flex-wrap items-center gap-x-4 gap-y-2 overflow-hidden rounded-[16px] border border-line/60 bg-card/55 py-2.5 ps-4 pe-3 shadow-[inset_0_1px_0_rgb(255_255_255/0.45)]">
                <span aria-hidden className="absolute inset-y-2.5 start-0 w-1 rounded-e-full bg-orange" />
                <span className="flex min-w-0 flex-1 basis-48 items-center gap-3">
                  <Avatar name={m.name ?? "؟"} className="size-9 shrink-0 text-[11px]" />
                  <span className="grid min-w-0">
                    <span className="font-semibold">{m.name ?? "من غير اسم"}</span>
                    <span className="tabular text-xs text-ink-soft" dir="ltr" style={{ textAlign: "right" }}>{phoneOf(m.wa_id) ?? "من غير رقم"}</span>
                  </span>
                </span>
                <span className="flex items-center gap-1 text-xs text-ink-soft">
                  <Icon name="clock" className="size-3.5" />
                  <span className="tabular">من {cairoWhen(m.since)}</span>
                </span>
                <Button className="min-h-9 rounded-full px-3.5" busy={unmute.isPending && unmute.variables === m.wa_id} onClick={() => unmute.mutate(m.wa_id)}>رجّعها لحازم</Button>
              </li>
            ))}
          </ul>
        )}
      {unmute.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(unmute.error)}</p> : null}
    </GlassCard>
  );
}
