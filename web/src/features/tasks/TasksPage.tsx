import { useEffect, useState } from "react";
import type { QueryClient } from "@tanstack/react-query";
import { Link, useSearchParams } from "react-router";
import { errorMessage } from "../../shared/api/client";
import { useDashboard } from "../../shared/api/dashboard";
import { can, useMe } from "../../shared/api/session";
import type { Me, TaskListItem } from "../../shared/api/types";
import { label, matterLabel, TASK_STATES, TASK_TYPES } from "../../shared/lib/labels";
import { cairoDayWord, cairoWhen } from "../../shared/lib/time";
import { useUrlState } from "../../shared/lib/url";
import { toast } from "../../shared/ui/feedback";
import { FOLDER, type Folder } from "../../shared/ui/folders";
import { Icon } from "../../shared/ui/Icon";
import { Button, Chip, Empty, ErrorState, inputClass, Loading, PageHeader, Segmented } from "../../shared/ui/ui";
import { tasksQuery, useTaskAction, useTasks, type Scope, type StateFilter } from "./api";

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

/** The list, read the way a person works it: what is urgent, what is late, what is due today, the rest. */
const GROUPS: { key: string; title: string; folder: Folder; test: (t: TaskListItem, now: Date) => boolean }[] = [
  { key: "urgent", title: "عاجل", folder: "coral", test: (t) => isUrgent(t) && t.state !== "done" && t.state !== "cancelled" },
  { key: "late", title: "متأخر", folder: "coral", test: (t) => t.state === "overdue" },
  { key: "today", title: "مطلوب النهارده", folder: "cobalt", test: (t, now) => cairoDayWord(t.due_at, now) === "اليوم" },
  { key: "later", title: "بعدين", folder: "ink", test: () => true },
];

/**
 * «المهام»: what Hazem opened for the office. Four numbers that are also filters, a search by the client's
 * name or the last digits, the list in groups (urgent, late, due today, later), and the next step on
 * each row («هاشتغل عليها», «خلصت») without opening the task.
 */
/** The list the page opens on (all or mine, the open ones, page 1), fetched while the pointer is on its link. */
export function prefetchData(qc: QueryClient, me: Me | null | undefined) {
  void qc.prefetchQuery(tasksQuery({ scope: can(me, "tasks.view_all") ? "all" : "mine", state: "active", page: 1 }));
}

export function TasksPage() {
  const { data: me } = useMe();
  const seesAll = can(me, "tasks.view_all");
  const dash = useDashboard();
  // the filters live in the address: back from a task lands on the same list
  const [scope, setScope] = useUrlState<Scope>("who", seesAll ? "all" : "mine", SCOPES.filter((s) => !s.needsAll || seesAll).map((s) => s.value));
  const [params, setParams] = useSearchParams();
  const q = params.get("q")?.trim() || undefined;
  const [state, setState] = useUrlState<StateFilter>("state", "active", STATES.map((s) => s.value));
  const [page, setPage] = useState(1);
  const tasks = useTasks({ scope, state, page, ...(q ? { q } : {}) });
  const now = new Date();

  const pick = <T,>(set: (v: T) => void) => (v: T) => {
    set(v);
    setPage(1);
  };
  const search = (v: string) => setParams((p) => {
    const n = new URLSearchParams(p);
    if (v.trim()) n.set("q", v.trim());
    else n.delete("q");
    return n;
  }, { replace: true });

  const groups = state === "active" && tasks.data
    ? GROUPS.map((g) => ({ ...g, items: [] as TaskListItem[] }))
    : null;
  if (groups && tasks.data) {
    for (const t of tasks.data.items) groups.find((g) => g.test(t, now))!.items.push(t);
  }

  return (
    <div className="grid gap-5">
      <PageHeader title="المهام" description="كل اللي حازم فتحه للمكتب: مين يكلّم العميل، وإمتى، وإيه اللي اتعمل." />

      {/* four numbers that are also filters */}
      <section aria-label="أرقام المهام" className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <Filter folder="coral" text="عاجل" value={dash.data?.urgent} on={false} onClick={() => { pick(setState)("active"); }} />
        <Filter folder="coral" text="متأخرة" value={dash.data?.overdue} on={state === "overdue"} onClick={() => pick(setState)("overdue")} />
        <Filter folder="orchid" text="مش متعيّنة" value={dash.data?.unassigned} on={scope === "unassigned"} onClick={() => pick(setScope)("unassigned")} />
        <Filter folder="cobalt" text="شغالة" value={dash.data?.active} on={state === "active" && scope !== "unassigned"} onClick={() => { pick(setState)("active"); if (seesAll) pick(setScope)("all"); }} />
      </section>

      <div className="flex flex-wrap items-center gap-2">
        <SearchBox value={q ?? ""} onChange={search} />
        <Segmented label="مين" value={scope} onChange={pick(setScope)}
          options={SCOPES.filter((s) => !s.needsAll || seesAll).map((s) => [s.value, s.label] as const)} />
        <Segmented label="الحالة" value={state} onChange={pick(setState)} options={STATES.map((s) => [s.value, s.label] as const)} />
        {tasks.data ? <span className="tabular ms-auto text-[13.5px] text-ink-soft">{tasks.data.total} مهمة</span> : null}
      </div>

      {tasks.isPending ? (
        <Loading />
      ) : tasks.isError ? (
        <ErrorState message={errorMessage(tasks.error)} onRetry={() => void tasks.refetch()} />
      ) : tasks.data.items.length === 0 ? (
        <div className="surface rounded-[var(--radius-card)]">
          <Empty title={q ? `مفيش مهام لـ «${q}»` : state === "active" ? "مفيش مهام محتاجة شغل دلوقتي" : "مفيش مهام هنا"} icon="tasks">
            {q ? "جرّب جزء من الاسم، أو آخر 3 أرقام من الموبايل." : state === "active" ? "أول ما حازم يفتح مهمة جديدة هتظهر هنا لوحدها." : null}
          </Empty>
        </div>
      ) : (
        <>
          {groups ? (
            groups.filter((g) => g.items.length > 0).map((g) => (
              <section key={g.key} aria-label={g.title} className="surface overflow-hidden rounded-[var(--radius-card)]">
                <h2 className="flex items-center gap-2 border-b border-line bg-card-2/60 px-5 py-2.5 text-[15px] font-semibold">
                  <span className="size-2.5 rounded-full" style={{ background: FOLDER[g.folder].color }} aria-hidden />
                  {g.title}
                  <span className="tabular text-[13px] font-normal text-ink-soft">{g.items.length}</span>
                </h2>
                <ul className="grid divide-y divide-line" aria-label="المهام">{g.items.map((t) => <TaskRow key={t.id} t={t} folder={g.folder} now={now} />)}</ul>
              </section>
            ))
          ) : (
            <section className="surface overflow-hidden rounded-[var(--radius-card)]">
              <ul className="grid divide-y divide-line" aria-label="المهام">
                {tasks.data.items.map((t) => <TaskRow key={t.id} t={t} folder={t.state === "done" ? "jade" : t.state === "overdue" ? "coral" : "ink"} now={now} />)}
              </ul>
            </section>
          )}
          <Pager page={tasks.data.page} pageSize={tasks.data.page_size} total={tasks.data.total} onPage={setPage} />
        </>
      )}
    </div>
  );
}

/** A number that filters the list when pressed. */
function Filter({ folder, text, value, on, onClick }: { folder: Folder; text: string; value: number | undefined; on: boolean; onClick: () => void }) {
  const f = FOLDER[folder];
  return (
    <button type="button" aria-pressed={on} onClick={onClick}
      className={`surface grid gap-0.5 rounded-[var(--radius-card)] px-4 py-3 text-start transition-colors hover:border-line-strong ${on ? "ring-2 ring-gold ring-inset" : ""}`}>
      <span className="text-[14px] font-semibold text-ink-soft">{text}</span>
      <span className={`display text-[28px] leading-tight font-semibold ${folder === "coral" && value ? f.ink : "text-ink"}`}>{value ?? "…"}</span>
    </button>
  );
}

/** Search as you type (a short pause), kept in the address so back returns to the same results. */
function SearchBox({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  const [text, setText] = useState(value);
  useEffect(() => setText(value), [value]);
  useEffect(() => {
    if (text === value) return;
    const t = setTimeout(() => onChange(text), 350);
    return () => clearTimeout(t);
  }, [text, value, onChange]);
  return (
    <label className="relative block w-full sm:w-72">
      <Icon name="search" className="pointer-events-none absolute inset-y-0 start-3 my-auto size-[18px] text-ink-faint" />
      <input type="search" aria-label="دوّر في المهام" placeholder="اسم العميل أو آخر أرقام الموبايل" value={text}
        onChange={(e) => setText(e.target.value)} className={`${inputClass} ps-10`} />
    </label>
  );
}

const who = (t: TaskListItem) => t.client_name ?? "رقم من غير اسم";

/** One task: who, what, by when and with whom, and the next step right on the row. */
function TaskRow({ t, folder, now }: { t: TaskListItem; folder: Folder; now: Date }) {
  const act = useTaskAction(t.id);
  const open = t.state === "open" || t.state === "overdue";
  const late = t.state === "overdue";
  return (
    <li className="flex flex-wrap items-center gap-x-4 gap-y-2 px-5 py-3.5 transition-colors hover:bg-card-2/60">
      <span className="w-1 self-stretch rounded-full" style={{ background: FOLDER[folder].color }} aria-hidden />
      <Link to={`/tasks/${t.id}`} className="grid min-w-0 flex-1 gap-0.5 leading-snug">
        <span className="flex flex-wrap items-center gap-x-2.5 gap-y-1">
          <span className="text-[15.5px] font-semibold hover:text-accent-ink">{who(t)}</span>
          <span className="tabular text-[12.5px] text-ink-faint" dir="ltr">…{t.phone_last3}</span>
          {isUrgent(t) && t.state !== "done" && t.state !== "cancelled" ? <Chip tone="urgent">عاجل</Chip> : null}
          <Chip tone={t.state}>{label(TASK_STATES, t.state)}</Chip>
        </span>
        <span className="truncate text-[14px] text-ink-soft">
          {label(TASK_TYPES, t.type)}{t.matter_type ? ` - ${matterLabel(t.matter_type)}` : ""} - {t.staff?.name ?? "مش متعيّنة"}
        </span>
      </Link>
      <span className={`tabular hidden shrink-0 text-[13.5px] md:block ${late ? "font-medium text-coral-ink" : "text-ink-soft"}`}>
        {late ? "كان المفروض " : "قبل "}{cairoWhen(t.due_at, now)}
      </span>
      <span className="flex shrink-0 gap-1.5">
        {open ? (
          <Button size="sm" busy={act.isPending} onClick={() => act.mutate("start", { onError: (e) => toast(errorMessage(e), "bad") })}>
            <Icon name="play" className="size-4" />هاشتغل عليها
          </Button>
        ) : t.state === "in_progress" ? (
          <Button size="sm" variant="primary" busy={act.isPending} onClick={() => act.mutate("done", { onError: (e) => toast(errorMessage(e), "bad") })}>
            <Icon name="check" className="size-4" />خلصت
          </Button>
        ) : null}
        <Link to={`/tasks/${t.id}`} aria-label={`افتح مهمة ${who(t)}`}
          className="inline-flex min-h-9 items-center gap-1 rounded-[8px] border border-line-strong bg-card px-3 text-[13.5px] font-medium hover:border-accent hover:text-accent-ink">
          افتح<Icon name="chevron-end" className="size-4" />
        </Link>
      </span>
    </li>
  );
}

function Pager({ page, pageSize, total, onPage }: { page: number; pageSize: number; total: number; onPage: (p: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  if (pages === 1) return null;
  return (
    <nav className="flex items-center justify-center gap-3" aria-label="الصفحات">
      <Button disabled={page <= 1} onClick={() => onPage(page - 1)}>اللي قبلها</Button>
      <span className="tabular text-[13.5px] text-ink-soft">صفحة {page} من {pages}</span>
      <Button disabled={page >= pages} onClick={() => onPage(page + 1)}>اللي بعدها</Button>
    </nav>
  );
}
