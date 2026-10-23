import { useState } from "react";
import { Link, useSearchParams } from "react-router";
import { errorMessage } from "../../shared/api/client";
import { can, useMe } from "../../shared/api/session";
import type { TaskListItem } from "../../shared/api/types";
import { label, matterLabel, TASK_STATES, TASK_TYPES } from "../../shared/lib/labels";
import { cairoWhen } from "../../shared/lib/time";
import { Icon } from "../../shared/ui/Icon";
import { Avatar, Button, Chip, Empty, ErrorState, Loading } from "../../shared/ui/ui";
import { useTasks, type Scope, type StateFilter } from "./api";

const SCOPES: { value: Scope; label: string; needsAll?: boolean }[] = [
  { value: "mine", label: "مهامي" },
  { value: "unassigned", label: "مش متعيّنة" },
  { value: "all", label: "الكل", needsAll: true },
];

const STATES: { value: StateFilter; label: string }[] = [
  { value: "active", label: "محتاجة شغل" },
  { value: "overdue", label: "متأخرة" },
  { value: "done", label: "خلصت" },
  { value: "cancelled", label: "اتلغت" },
];

export const isUrgent = (t: { type: string }) => t.type === "urgent_request" || t.type === "safety_alert";

export function TasksPage() {
  const { data: me } = useMe();
  const seesAll = can(me, "tasks.view_all");
  const [scope, setScope] = useState<Scope>(seesAll ? "all" : "mine");
  const [params, setParams] = useSearchParams();
  const q = params.get("q")?.trim() || undefined;
  const [state, setState] = useState<StateFilter>(() => (STATES.some((s) => s.value === params.get("state")) ? (params.get("state") as StateFilter) : "active"));
  const [page, setPage] = useState(1);
  const tasks = useTasks({ scope, state, page, ...(q ? { q } : {}) });

  const pick = <T,>(set: (v: T) => void) => (v: T) => {
    set(v);
    setPage(1);
  };

  return (
    <div className="grid gap-4">
      <div className="flex flex-wrap items-end justify-between gap-2">
        <h1 className="text-2xl">المهام</h1>
        {tasks.data ? <p className="tabular text-sm text-ink-soft">{tasks.data.total} مهمة</p> : null}
      </div>

      {q ? (
        <p className="flex flex-wrap items-center gap-2 text-sm">
          نتايج البحث عن <strong className="rounded-md bg-gold-tint px-2 text-gold-ink">{q}</strong>
          <button type="button" onClick={() => setParams({})} className="inline-flex items-center gap-1 text-ink-soft hover:text-ink">
            <Icon name="x" className="size-4" /> امسح البحث
          </button>
        </p>
      ) : null}

      <FilterRow label="مين" value={scope} onChange={pick(setScope)}
        options={SCOPES.filter((s) => !s.needsAll || seesAll)} />
      <FilterRow label="الحالة" value={state} onChange={pick(setState)} options={STATES} />

      {tasks.isPending ? (
        <Loading />
      ) : tasks.isError ? (
        <ErrorState message={errorMessage(tasks.error)} onRetry={() => void tasks.refetch()} />
      ) : tasks.data.items.length === 0 ? (
        <Empty title={state === "active" ? "مفيش مهام محتاجة شغل دلوقتي" : "مفيش مهام هنا"}>
          {state === "active" ? "أول ما حازم يفتح مهمة جديدة هتظهر هنا لوحدها." : null}
        </Empty>
      ) : (
        <>
          <ul className="grid gap-3 lg:hidden" aria-label="المهام">
            {tasks.data.items.map((t) => (
              <li key={t.id}>
                <TaskCard task={t} />
              </li>
            ))}
          </ul>
          <TaskTable items={tasks.data.items} />
          <Pager page={tasks.data.page} pageSize={tasks.data.page_size} total={tasks.data.total} onPage={setPage} />
        </>
      )}
    </div>
  );
}

function FilterRow<T extends string>({ label: name, value, options, onChange }: {
  label: string; value: T; options: { value: T; label: string }[]; onChange: (v: T) => void;
}) {
  return (
    <div className="flex gap-2 overflow-x-auto pb-1" role="group" aria-label={name}>
      {options.map((o) => (
        <button key={o.value} type="button" aria-pressed={value === o.value} onClick={() => onChange(o.value)}
          className={`min-h-9 shrink-0 rounded-full border px-4 text-sm ${value === o.value ? "border-gold bg-gold-tint font-semibold text-gold-ink" : "border-line-strong bg-card text-ink hover:border-gold-line"}`}>
          {o.label}
        </button>
      ))}
    </div>
  );
}

function StateChips({ task }: { task: TaskListItem }) {
  return (
    <span className="flex flex-wrap gap-1.5">
      {isUrgent(task) && task.state !== "done" && task.state !== "cancelled" ? <Chip tone="urgent">عاجل</Chip> : null}
      <Chip tone={task.state}>{label(TASK_STATES, task.state)}</Chip>
    </span>
  );
}

const who = (t: TaskListItem) => t.client_name ?? "رقم من غير اسم";

function TaskCard({ task }: { task: TaskListItem }) {
  return (
    <Link to={`/tasks/${task.id}`}
      className={`surface grid gap-2 rounded-[var(--radius-card)] p-4 hover:bg-muted ${task.state === "overdue" ? "border-s-4 border-s-bad" : ""}`}>
      <span className="flex items-start justify-between gap-2">
        <span className="font-semibold">{who(task)}</span>
        <StateChips task={task} />
      </span>
      <span className="text-sm text-ink-soft">
        {label(TASK_TYPES, task.type)}
        {task.matter_type ? ` · ${matterLabel(task.matter_type)}` : ""}
      </span>
      <span className="tabular flex justify-between gap-2 text-xs text-ink-soft">
        <span>{task.state === "overdue" ? "كان المفروض " : "المفروض "}{cairoWhen(task.due_at)}</span>
        <span>{task.staff?.name ?? "مش متعيّنة"} · <span dir="ltr">…{task.phone_last3}</span></span>
      </span>
    </Link>
  );
}

function TaskTable({ items }: { items: TaskListItem[] }) {
  return (
    <div className="surface hidden overflow-x-auto rounded-[var(--radius-card)] lg:block">
      <table className="w-full text-sm">
        <thead className="text-xs text-ink-soft">
          <tr className="bg-card-2 text-start">
            <th className="p-3 text-start font-medium">العميل</th>
            <th className="p-3 text-start font-medium">المهمة</th>
            <th className="p-3 text-start font-medium">الموضوع</th>
            <th className="p-3 text-start font-medium">الموظف</th>
            <th className="p-3 text-start font-medium">المفروض</th>
            <th className="p-3 text-start font-medium">الحالة</th>
          </tr>
        </thead>
        <tbody>
          {items.map((t) => (
            <tr key={t.id} className={`border-b border-line last:border-0 hover:bg-muted `}>
              <td className="p-3">
                <span className="flex flex-wrap items-baseline gap-x-3 gap-y-0.5">
                  <span className="flex items-center gap-2"><Avatar name={who(t)} className="size-7 text-[10px]" /><Link to={`/tasks/${t.id}`} className="font-semibold underline-offset-4 hover:underline">{who(t)}</Link></span>
                  <span className="tabular text-xs text-ink-soft" dir="ltr">…{t.phone_last3}</span>
                </span>
              </td>
              <td className="p-3">{label(TASK_TYPES, t.type)}</td>
              <td className="p-3">{t.matter_type ? matterLabel(t.matter_type) : "—"}</td>
              <td className="p-3">{t.staff?.name ?? "مش متعيّنة"}</td>
              <td className="tabular p-3">{cairoWhen(t.due_at)}</td>
              <td className="p-3"><StateChips task={t} /></td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function Pager({ page, pageSize, total, onPage }: { page: number; pageSize: number; total: number; onPage: (p: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  if (pages === 1) return null;
  return (
    <nav className="flex items-center justify-center gap-3" aria-label="الصفحات">
      <Button disabled={page <= 1} onClick={() => onPage(page - 1)}>اللي قبلها</Button>
      <span className="tabular text-sm text-ink-soft">صفحة {page} من {pages}</span>
      <Button disabled={page >= pages} onClick={() => onPage(page + 1)}>اللي بعدها</Button>
    </nav>
  );
}
