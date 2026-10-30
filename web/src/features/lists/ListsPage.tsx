import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError, errorMessage } from "../../shared/api/client";
import { can, useMe } from "../../shared/api/session";
import type { ListEntry, ListKind, MutedConversation, StaffRef } from "../../shared/api/types";
import { label, LIST_KINDS } from "../../shared/lib/labels";
import { cairoWhen } from "../../shared/lib/time";
import { Icon } from "../../shared/ui/Icon";
import { Button, Card, Empty, ErrorState, Field, Loading } from "../../shared/ui/ui";

const KINDS: ListKind[] = ["existing_client", "lawyer_direct", "blocked"];
const ERRORS: Record<string, string> = {
  invalid_phone: "الرقم مش صحيح. اكتبه زي 01012345678 أو +201012345678.",
  too_long: "الاسم أو الملاحظة أطول من اللازم.",
  unknown_staff: "اختار الموظف من القايمة.",
};
const PAGE = 50; // the API's page
const MAX_CSV_BYTES = 600_000; // 300,000 Arabic characters: what the API takes

/**
 * The office's lists (B-14, TASK 07c): who Hazem treats as a current client, the lawyer's own list,
 * and the blocked. The manager and the lawyer. And (the manager only) the muted conversations (S-01).
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

  return (
    <div className="grid gap-4">
      <h1 className="text-2xl">قوايم المكتب</h1>
      <p className="max-w-2xl text-sm text-ink-soft">
        حازم بيستخدم القوايم دي علشان يعرف مين بيكلمه. لو الرقم في أكتر من قايمة: الممنوعين الأول، بعدين قايمة المستشار، بعدين الموكلين الحاليين.
      </p>

      <div className="flex flex-wrap gap-2" role="group" aria-label="القايمة">
        {KINDS.map((k) => (
          <Button key={k} variant={k === kind ? "primary" : "secondary"} aria-pressed={k === kind} onClick={() => { setKind(k); setPage(1); }}>
            {label(LIST_KINDS, k)}
          </Button>
        ))}
      </div>

      <AddEntry kind={kind} />
      <ImportCsv kind={kind} />

      <Card>
        <div className="flex flex-wrap items-end justify-between gap-2">
          <h2 className="text-lg">{label(LIST_KINDS, kind)} {list.data ? <span className="tabular text-sm text-ink-soft">({list.data.total})</span> : null}</h2>
          <div className="w-full max-w-xs">
            <Field label="دوّر بالاسم أو آخر أرقام" value={q} onChange={(e) => { setQ(e.target.value); setPage(1); }} />
          </div>
        </div>
        {list.isPending ? <Loading /> : list.isError ? <ErrorState message={errorMessage(list.error)} onRetry={() => void list.refetch()} /> :
          list.data.items.length === 0 ? <Empty title="القايمة فاضية" /> : (
            <ul className="grid divide-y divide-line">
              {list.data.items.map((e) => (
                <li key={e.id} className="flex flex-wrap items-center gap-3 py-2.5">
                  <span className="grid min-w-0 flex-1">
                    <span className="font-semibold">{e.name ?? "من غير اسم"}</span>
                    <span className="tabular text-xs text-ink-soft" dir="ltr" style={{ textAlign: "right" }}>+{e.wa_id}</span>
                  </span>
                  {e.staff_name ? <span className="text-sm text-ink-soft">مع {e.staff_name}</span> : null}
                  <span className="tabular text-xs text-ink-soft">{cairoWhen(e.imported_at)}</span>
                  <Button variant="ghost" busy={remove.isPending && remove.variables === e.id} onClick={() => remove.mutate(e.id)}
                    aria-label={`شيل ${e.name ?? e.wa_id} من القايمة`}>
                    <Icon name="x" className="size-4" />شيل
                  </Button>
                </li>
              ))}
            </ul>
          )}
        {pages > 1 ? (
          <nav className="flex items-center justify-center gap-3 pt-3" aria-label="صفحات القايمة">
            <Button variant="secondary" disabled={page <= 1} onClick={() => setPage(page - 1)}>اللي قبل</Button>
            <span className="tabular text-sm text-ink-soft">صفحة {page} من {pages}</span>
            <Button variant="secondary" disabled={page >= pages} onClick={() => setPage(page + 1)}>اللي بعد</Button>
          </nav>
        ) : null}
        {remove.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(remove.error)}</p> : null}
      </Card>

      {can(me, "conversations.unmute") ? <Muted /> : null}
    </div>
  );
}

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
    <Card>
      <h2 className="text-lg">ضيف رقم</h2>
      <form onSubmit={(e: FormEvent) => { e.preventDefault(); add.mutate(); }} className="grid gap-3 md:grid-cols-2" noValidate>
        <Field label="الرقم" required dir="ltr" inputMode="tel" value={form.phone} onChange={set("phone")} />
        <Field label="الاسم" value={form.name} onChange={set("name")} />
        {kind === "existing_client" ? (
          <label className="grid gap-1.5 text-sm font-semibold">
            الموظف المسؤول
            <select className="surface min-h-11 rounded-[var(--radius-control)] px-3 font-normal text-ink" value={form.staff_name} onChange={set("staff_name")}>
              <option value="">من غير موظف</option>
              {(staff.data ?? []).map((s) => <option key={s.id} value={s.name}>{s.name}</option>)}
            </select>
          </label>
        ) : null}
        <Field label="ملاحظة" value={form.note} onChange={set("note")} />
        {error ? <p className="text-sm text-bad md:col-span-2" role="alert">{error}</p> : null}
        <div className="md:col-span-2"><Button type="submit" variant="primary" busy={add.isPending} disabled={!form.phone.trim()}>ضيف</Button></div>
      </form>
    </Card>
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
    <Card>
      <h2 className="text-lg">استورد من Excel</h2>
      <p className="text-sm text-ink-soft">احفظ الشيت كـ CSV فيه عمود <code dir="ltr">phone</code> (ومعاه <code dir="ltr">name</code>، و<code dir="ltr">staff</code> للموكلين). الأرقام الغلط بتتعدّى وبيتقالك سطرها.</p>
      <label className="flex items-center gap-2 text-sm">
        <input type="checkbox" checked={replace} onChange={(e) => setReplace(e.target.checked)} />
        استبدل القايمة كلها بالملف ده (لو فيه سطر غلط، مفيش حاجة بتتغير)
      </label>
      <input type="file" accept=".csv,text/csv" aria-label="ملف CSV" disabled={run.isPending}
        onChange={(e) => {
          const f = e.target.files?.[0];
          setTooBig(!!f && f.size > MAX_CSV_BYTES);
          if (f && f.size <= MAX_CSV_BYTES) run.mutate(f);
          e.target.value = "";
        }} />
      {tooBig ? <p className="text-sm text-bad" role="alert">الملف كبير. قسّمه لملفات أصغر من 600 كيلو (حوالي 6,000 سطر) واستوردها واحد ورا التاني.</p> : null}
      {run.isSuccess ? (
        <p className="text-sm" role="status">
          اتضاف {run.data.imported}.{run.data.rejected.length > 0 ? ` واتعدّى ${run.data.rejected.length}: ${run.data.rejected.slice(0, 5).map((r) => `سطر ${r.line}`).join("، ")}` : ""}
        </p>
      ) : null}
      {run.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(run.error)}</p> : null}
    </Card>
  );
}

/** S-01: a conversation Hazem went silent in (abuse, spam, a blocked number) comes back only by the manager's decision. */
function Muted() {
  const qc = useQueryClient();
  const muted = useQuery({ queryKey: ["muted"], queryFn: () => api<MutedConversation[]>("GET", "/conversations/muted") });
  const unmute = useMutation({
    mutationFn: (wa: string) => api<void>("POST", "/conversations/unmute", { wa_id: wa }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ["muted"] }),
  });

  return (
    <Card>
      <h2 className="text-lg">محادثات حازم ساكت فيها</h2>
      <p className="text-sm text-ink-soft">بسبب شتيمة أو سبام أو رقم ممنوع. لو رجّعتها، حازم بيرد على الرقم ده تاني.</p>
      {muted.isPending ? <Loading /> : muted.isError ? <ErrorState message={errorMessage(muted.error)} /> :
        muted.data.length === 0 ? <Empty title="مفيش محادثات ساكتة" /> : (
          <ul className="grid divide-y divide-line">
            {muted.data.map((m) => (
              <li key={m.wa_id} className="flex flex-wrap items-center gap-3 py-2.5">
                <span className="grid min-w-0 flex-1">
                  <span className="font-semibold">{m.name ?? "من غير اسم"}</span>
                  <span className="tabular text-xs text-ink-soft" dir="ltr" style={{ textAlign: "right" }}>+{m.wa_id}</span>
                </span>
                <span className="tabular text-xs text-ink-soft">من {cairoWhen(m.since)}</span>
                <Button busy={unmute.isPending && unmute.variables === m.wa_id} onClick={() => unmute.mutate(m.wa_id)}>رجّعها لحازم</Button>
              </li>
            ))}
          </ul>
        )}
      {unmute.isError ? <p className="text-sm text-bad" role="alert">{errorMessage(unmute.error)}</p> : null}
    </Card>
  );
}
