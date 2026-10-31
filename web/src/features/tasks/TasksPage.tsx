import { useState, type ReactNode } from "react";
import type { QueryClient } from "@tanstack/react-query";
import { Link, useSearchParams } from "react-router";
import { errorMessage } from "../../shared/api/client";
import { can, useMe } from "../../shared/api/session";
import type { Me, TaskListItem } from "../../shared/api/types";
import { label, matterLabel, TASK_STATES, TASK_TYPES } from "../../shared/lib/labels";
import { cairoWhen } from "../../shared/lib/time";
import { useUrlState } from "../../shared/lib/url";
import { Icon, type IconName } from "../../shared/ui/Icon";
import { Avatar, Chip, ErrorState, Loading } from "../../shared/ui/ui";
import { tasksQuery, useTasks, type Scope, type StateFilter } from "./api";

const SCOPES: { value: Scope; label: string; icon: IconName; needsAll?: boolean }[] = [
  { value: "mine", label: "مهامي", icon: "account" },
  { value: "unassigned", label: "مش متعيّنة", icon: "user-plus" },
  { value: "all", label: "الكل", icon: "users", needsAll: true },
];

const STATES: { value: StateFilter; label: string; icon: IconName }[] = [
  { value: "active", label: "محتاجة شغل", icon: "play" },
  { value: "overdue", label: "متأخرة", icon: "clock" },
  { value: "done", label: "خلصت", icon: "check-circle" },
  { value: "cancelled", label: "اتلغت", icon: "x" },
];

export const isUrgent = (t: { type: string }) => t.type === "urgent_request" || t.type === "safety_alert";

/** The list the page opens on, fetched while the pointer is still on the link (idea from PR #14). */
export function prefetchData(qc: QueryClient, me: Me | null | undefined) {
  void qc.prefetchQuery(tasksQuery({ scope: can(me, "tasks.view_all") ? "all" : "mine", state: "active", page: 1 }));
}

/**
 * «المهام» as «liquid glass»: the filters as glass pills, and every task a glass card that reads as a sentence (who, what
 * they need, who has it, by when, where it stands). How to use the panel is for a first-login dialog, not this page.
 */
export function TasksPage() {
  const { data: me } = useMe();
  const seesAll = can(me, "tasks.view_all");
  // the filters live in the address (idea from PR #14): a refresh, the back button from a task or a shared link keep them
  const scopes = SCOPES.filter((s) => !s.needsAll || seesAll);
  const [scope, setScope] = useUrlState<Scope>("scope", seesAll ? "all" : "mine", scopes.map((s) => s.value));
  const [state, setState] = useUrlState<StateFilter>("state", "active", STATES.map((s) => s.value));
  const [params, setParams] = useSearchParams();
  const q = params.get("q")?.trim() || undefined;
  const [page, setPage] = useState(1);
  const tasks = useTasks({ scope, state, page, ...(q ? { q } : {}) });

  const pick = <T,>(set: (v: T) => void) => (v: T) => {
    set(v);
    setPage(1);
  };

  return (
    <div className="relative isolate grid gap-5 pb-4">
      {/* the soft light the glass shows through */}
      <div aria-hidden className="pointer-events-none absolute inset-x-0 -top-6 -z-10 h-[30rem] overflow-hidden">
        <span className="absolute -top-16 end-[4%] size-[24rem] rounded-full bg-gold/30 blur-[100px]" />
        <span className="absolute top-24 start-[10%] size-[20rem] rounded-full bg-blue/10 blur-[100px]" />
      </div>

      <Intro total={tasks.data?.total} />

      {q ? (
        <p className="liquid flex flex-wrap items-center gap-2 rounded-full px-4 py-2 text-sm">
          <Icon name="search" className="size-4 text-gold-ink" />
          نتايج البحث عن <strong className="rounded-md bg-gold-tint px-2 text-gold-ink">{q}</strong>
          <button type="button" onClick={() => setParams((p) => { const next = new URLSearchParams(p); next.delete("q"); return next; }, { replace: true })} className="ms-auto inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-ink-soft hover:bg-card-2 hover:text-ink">
            <Icon name="x" className="size-4" /> امسح البحث
          </button>
        </p>
      ) : null}

      <section aria-label="الفلاتر" className="liquid grid gap-3 rounded-[22px] p-3 sm:p-4 2xl:grid-cols-2">
        <FilterRow label="مين" value={scope} onChange={pick(setScope)} options={scopes} />
        <FilterRow label="الحالة" value={state} onChange={pick(setState)} options={STATES} />
      </section>

      {tasks.isPending ? (
        <Loading />
      ) : tasks.isError ? (
        <ErrorState message={errorMessage(tasks.error)} onRetry={() => void tasks.refetch()} />
      ) : tasks.data.items.length === 0 ? (
        <Nothing state={state} />
      ) : (
        <>
          <ul className="grid gap-2.5" aria-label="المهام">
            {tasks.data.items.map((t) => (
              <li key={t.id}>
                <TaskCard task={t} />
              </li>
            ))}
          </ul>
          <Pager page={tasks.data.page} pageSize={tasks.data.page_size} total={tasks.data.total} onPage={setPage} />
        </>
      )}
    </div>
  );
}

// ---------- the top ----------

function Intro({ total }: { total?: number | undefined }) {
  return (
    <header className="relative">
      <div className="relative flex flex-wrap items-center gap-3">
        <span aria-hidden className="grid size-11 shrink-0 place-items-center rounded-[14px] bg-gradient-to-br from-gold-tint to-card text-gold-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6),0_8px_20px_-10px_rgb(140_98_58/0.7)]">
          <Icon name="tasks" className="size-6" />
        </span>
        <h1 className="text-[26px] leading-tight font-bold">المهام</h1>
        {total !== undefined ? (
          <span className="tabular rounded-full border border-gold-line bg-gold-tint px-2.5 py-0.5 text-[12.5px] font-bold text-gold-ink">{total} مهمة</span>
        ) : null}
      </div>
    </header>
  );
}

// ---------- the filters ----------

function FilterRow<T extends string>({ label: name, value, options, onChange }: {
  label: string; value: T; options: { value: T; label: string; icon: IconName }[]; onChange: (v: T) => void;
}) {
  return (
    <div className="flex gap-1 overflow-x-auto rounded-full border border-line/60 bg-card/45 p-1 shadow-[inset_0_1px_2px_rgb(0_0_0/0.06)]" role="group" aria-label={name}>
        {options.map((o) => {
          const on = value === o.value;
          return (
            <button key={o.value} type="button" aria-pressed={on} onClick={() => onChange(o.value)}
              className={`flex min-h-9 shrink-0 grow items-center justify-center gap-1.5 whitespace-nowrap rounded-full px-3.5 text-[13.5px] transition ${on
                ? "btn-gold font-bold shadow-[inset_0_1px_0_rgb(255_255_255/0.4),0_6px_14px_-6px_rgb(140_98_58/0.8)]"
                : "text-ink hover:bg-card/80"}`}>
              <Icon name={o.icon} className="size-4" />
              {o.label}
            </button>
          );
        })}
    </div>
  );
}

// ---------- one task ----------

const who = (t: TaskListItem) => t.client_name ?? "رقم من غير اسم";

// the line at a card's start and the tint of its state
const LOOK: Record<string, { bar: string; ring: string }> = {
  urgent: { bar: "bg-red", ring: "hover:border-red/40" },
  overdue: { bar: "bg-orange", ring: "hover:border-orange/40" },
  in_progress: { bar: "bg-gold", ring: "hover:border-gold-line" },
  open: { bar: "bg-blue", ring: "hover:border-blue/40" },
  done: { bar: "bg-green", ring: "hover:border-green/40" },
  cancelled: { bar: "bg-ink-soft/50", ring: "hover:border-line-strong" },
};

function StateChips({ task }: { task: TaskListItem }) {
  return (
    <span className="flex flex-wrap justify-end gap-1.5">
      {isUrgent(task) && task.state !== "done" && task.state !== "cancelled" ? <Chip tone="urgent">عاجل</Chip> : null}
      <Chip tone={task.state}>{label(TASK_STATES, task.state)}</Chip>
    </span>
  );
}

/** A task as one sentence: who, what they need (and about what), who has it, by when, and where it stands. */
function TaskCard({ task: t }: { task: TaskListItem }) {
  const live = t.state !== "done" && t.state !== "cancelled";
  const look = LOOK[isUrgent(t) && live ? "urgent" : t.state] ?? LOOK.open!;
  const late = t.state === "overdue";
  return (
    <Link to={`/tasks/${t.id}`}
      className={`group relative grid items-center gap-x-5 gap-y-3 overflow-hidden rounded-[18px] border border-line/60 bg-card/60 py-3.5 ps-5 pe-4 shadow-[inset_0_1px_0_rgb(255_255_255/0.45),0_6px_18px_-12px_rgb(60_44_20/0.35)] backdrop-blur-md transition hover:-translate-y-px hover:bg-card/85 hover:shadow-[0_14px_30px_-14px_rgb(60_44_20/0.4)] motion-reduce:transition-none motion-reduce:hover:translate-y-0 sm:grid-cols-2 xl:grid-cols-[minmax(12rem,1.1fr)_minmax(0,2fr)_minmax(9rem,0.9fr)_minmax(9rem,0.9fr)_auto] ${look.ring}`}>
      <span aria-hidden className={`absolute inset-y-3 start-0 w-1 rounded-e-full ${look.bar}`} />

      {/* who */}
      <span className="flex min-w-0 items-center gap-3">
        <Avatar name={who(t)} className="size-10 shrink-0 text-[12px]" />
        <span className="grid min-w-0">
          <span className="truncate text-[15px] font-bold">{who(t)}</span>
          <span className="tabular text-[12px] text-ink-soft" dir="ltr" style={{ textAlign: "right" }}>…{t.phone_last3}</span>
        </span>
      </span>

      {/* what they need */}
      <Field icon="message" term="المطلوب">
        <span className="font-semibold">{label(TASK_TYPES, t.type)}</span>
        {t.matter_type ? (
          <span className="ms-2 inline-block rounded-full border border-line/70 bg-card/70 px-2 py-0.5 align-middle text-[11.5px] font-normal text-ink-soft">{matterLabel(t.matter_type)}</span>
        ) : null}
      </Field>

      {/* who has it */}
      <Field icon={t.staff ? "account" : "user-plus"} term="مع مين">
        {t.staff ? <span className="font-semibold">{t.staff.name}</span>
          : <span className="font-semibold text-orange-ink">{live ? "محدش استلمها لسه" : "مش متعيّنة"}</span>}
      </Field>

      {/* by when */}
      <Field icon="clock" term={late ? "كان المفروض" : "المفروض تخلص"}>
        <span className={`tabular font-semibold ${late ? "text-red-ink" : ""}`}>{cairoWhen(t.due_at)}</span>
      </Field>

      {/* where it stands */}
      <span className="flex items-center justify-between gap-3 xl:justify-end">
        <StateChips task={t} />
        <span aria-hidden className="grid size-8 shrink-0 place-items-center rounded-full border border-line/60 bg-card/70 text-ink-soft transition group-hover:border-gold-line group-hover:bg-gold-tint group-hover:text-gold-ink">
          <Icon name="chevron-start" className="size-4 transition group-hover:-translate-x-0.5" />
        </span>
      </span>
    </Link>
  );
}

function Field({ icon, term, children }: { icon: IconName; term: string; children: ReactNode }) {
  return (
    <span className="grid min-w-0 gap-0.5">
      <span className="flex items-center gap-1 text-[11.5px] text-ink-soft">
        <Icon name={icon} className="size-3.5" />
        {term}
      </span>
      <span className="min-w-0 text-[13.5px] leading-snug">{children}</span>
    </span>
  );
}

// ---------- nothing to show, and the pages ----------

function Nothing({ state }: { state: StateFilter }) {
  return (
    <div className="liquid grid justify-items-center gap-2 rounded-[22px] px-6 py-10 text-center">
      <span className="grid size-14 place-items-center rounded-full bg-gradient-to-br from-green-bg to-card text-green-ink shadow-[inset_0_1px_0_rgb(255_255_255/0.6)]">
        <Icon name="check-circle" className="size-7" />
      </span>
      <p className="text-[16px] font-bold">{state === "active" ? "مفيش مهام محتاجة شغل دلوقتي" : "مفيش مهام هنا"}</p>
      {state === "active" ? <p className="max-w-sm text-[13px] text-ink-soft">أول ما حازم يفتح مهمة جديدة هتظهر هنا لوحدها.</p> : null}
    </div>
  );
}

function Pager({ page, pageSize, total, onPage }: { page: number; pageSize: number; total: number; onPage: (p: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  if (pages === 1) return null;
  const pill = "flex min-h-9 items-center gap-1.5 rounded-full border border-line/60 bg-card/60 px-4 text-[13px] font-semibold shadow-[inset_0_1px_0_rgb(255_255_255/0.45)] transition hover:border-gold-line hover:bg-gold-tint disabled:pointer-events-none disabled:opacity-45";
  return (
    <nav className="flex items-center justify-center gap-3" aria-label="الصفحات">
      <button type="button" className={pill} disabled={page <= 1} onClick={() => onPage(page - 1)}>
        <Icon name="chevron-end" className="size-4" />اللي قبلها
      </button>
      <span className="tabular rounded-full bg-gold-tint px-3 py-1 text-[13px] font-bold text-gold-ink">صفحة {page} من {pages}</span>
      <button type="button" className={pill} disabled={page >= pages} onClick={() => onPage(page + 1)}>
        اللي بعدها<Icon name="chevron-start" className="size-4" />
      </button>
    </nav>
  );
}
