import { useEffect, type ReactNode } from "react";
import type { QueryClient } from "@tanstack/react-query";
import { Link, useLocation, useSearchParams } from "react-router";
import { errorMessage } from "../../shared/api/client";
import { can, useMe } from "../../shared/api/session";
import type { Me, TaskListItem } from "../../shared/api/types";
import { label, matterLabel, TASK_STATES, TASK_TYPES, URGENT_TYPES } from "../../shared/lib/labels";
import { cairoWhen } from "../../shared/lib/time";
import { useUrlState } from "../../shared/lib/url";
import { Icon, type IconName } from "../../shared/ui/Icon";
import { Avatar, Chip, ErrorState, Loading } from "../../shared/ui/ui";
import { tasksQuery, useTasks, type Scope, type StateFilter } from "./api";

const SCOPES: { value: Scope; label: string; needsAll?: boolean }[] = [
  { value: "mine", label: "مهامي" },
  { value: "unassigned", label: "غير مُسندة" },
  { value: "all", label: "كل المسؤولين", needsAll: true },
];

const STATES: { value: StateFilter; label: string; icon: IconName }[] = [
  { value: "active", label: "تحتاج إلى إجراء", icon: "play" },
  { value: "urgent", label: "عاجلة", icon: "alert-circle" },
  { value: "overdue", label: "متأخرة", icon: "clock" },
  { value: "done", label: "مكتملة", icon: "check-circle" },
  { value: "cancelled", label: "ملغاة", icon: "x" },
];

export const isUrgent = (t: { type: string }) => URGENT_TYPES.has(t.type);

/** The list the page opens on, fetched while the pointer is still on the link (idea from PR #14). */
export function prefetchData(qc: QueryClient, me: Me | null | undefined) {
  void qc.prefetchQuery(tasksQuery({ scope: can(me, "tasks.view_all") ? "all" : "mine", state: "active", page: 1 }));
}

/**
 * «المهام» as the mockup's table (the owner): the numbers on top, a search and a «who» menu, the state tabs with their counts,
 * and every task a row of a table: what they need, the client, who has it, by when, how urgent and where it stands. Under a wide
 * screen the same row becomes a small card (every value with its name).
 */
export function TasksPage() {
  const { data: me } = useMe();
  const seesAll = can(me, "tasks.view_all");
  // the filters live in the address (idea from PR #14): a refresh, the back button from a task or a shared link keep them
  const scopes = SCOPES.filter((s) => !s.needsAll || seesAll);
  const scopeFallback: Scope = seesAll ? "all" : "mine";
  const [scope] = useUrlState<Scope>("scope", scopeFallback, scopes.map((s) => s.value));
  const [state] = useUrlState<StateFilter>("state", "active", STATES.map((s) => s.value));
  const [params, setParams] = useSearchParams();
  const q = params.get("q")?.trim() || undefined;
  // the page too (?page=2), so «رجوع» from a task lands on the same page; page 1 is left out
  const page = Math.max(1, Number.parseInt(params.get("page") ?? "", 10) || 1);
  const tasks = useTasks({ scope, state, page, ...(q ? { q } : {}) });
  // the numbers of the cards and the tabs: how many tasks each state has for the same people (and the same search)
  const counts = {
    active: useTasks({ scope, state: "active", page: 1, ...(q ? { q } : {}) }).data?.total,
    urgent: useTasks({ scope, state: "urgent", page: 1, ...(q ? { q } : {}) }).data?.total,
    overdue: useTasks({ scope, state: "overdue", page: 1, ...(q ? { q } : {}) }).data?.total,
    done: useTasks({ scope, state: "done", page: 1, ...(q ? { q } : {}) }).data?.total,
    cancelled: useTasks({ scope, state: "cancelled", page: 1, ...(q ? { q } : {}) }).data?.total,
  } satisfies Record<StateFilter, number | undefined>;

  // one change of the address at a time: two setParams in the same click would drop the first
  const change = (edit: (next: URLSearchParams) => void) =>
    setParams((p) => { const next = new URLSearchParams(p); edit(next); return next; }, { replace: true });
  const setPage = (n: number) => change((next) => (n > 1 ? next.set("page", String(n)) : next.delete("page")));
  // a new filter or search starts again from page 1
  const pick = (key: string, fallback: string) => (v: string) =>
    change((next) => { if (v === fallback) next.delete(key); else next.set(key, v); next.delete("page"); });

  // past the last page (fewer tasks than when the link was made): go to the last one that has tasks
  const data = tasks.data;
  const lastPage = data && data.total > 0 && data.items.length === 0 && !tasks.isPlaceholderData ? Math.max(1, Math.ceil(data.total / data.page_size)) : null;
  const outOfRange = lastPage !== null && page > lastPage;
  useEffect(() => {
    if (outOfRange && lastPage !== null) setPage(lastPage);
  }, [outOfRange, lastPage]); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <div className="relative isolate grid gap-5 pb-4">
      {/* the soft light the glass shows through */}
      <div aria-hidden className="pointer-events-none absolute inset-x-0 -top-6 -z-10 h-[30rem] overflow-hidden">
        <span className="absolute -top-16 end-[4%] size-[24rem] rounded-full bg-gold/30 blur-[100px]" />
        <span className="absolute top-24 start-[10%] size-[20rem] rounded-full bg-blue/10 blur-[100px]" />
      </div>

      <Intro total={tasks.data?.total} />

      {/* the numbers: each one opens its tab */}
      <section aria-label="أرقام المهام" className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <Stat tone="gold" icon="play" title="تحتاج إلى إجراء" value={counts.active} on={state === "active"} onClick={() => pick("state", "active")("active")} />
        <Stat tone="red" icon="alert-circle" title="عاجلة" value={counts.urgent} on={state === "urgent"} onClick={() => pick("state", "active")("urgent")} />
        <Stat tone="orange" icon="clock" title="متأخرة" value={counts.overdue} on={state === "overdue"} onClick={() => pick("state", "active")("overdue")} />
        <Stat tone="green" icon="check-circle" title="مكتملة" value={counts.done} on={state === "done"} onClick={() => pick("state", "active")("done")} />
      </section>

      <section aria-label="البحث والتصفية" className="liquid flex flex-wrap items-center gap-3 rounded-[22px] p-3 sm:p-4">
        <form role="search" className="relative min-w-[14rem] flex-1" onSubmit={(e) => {
          e.preventDefault();
          const v = String(new FormData(e.currentTarget).get("q") ?? "").trim();
          change((next) => { if (v) next.set("q", v); else next.delete("q"); next.delete("page"); });
        }}>
          <Icon name="search" className="pointer-events-none absolute inset-y-0 start-3.5 my-auto size-[18px] text-ink-soft" />
          <input key={q ?? ""} name="q" type="search" defaultValue={q ?? ""} placeholder="ابحث في المهام (اسم العميل أو آخر أرقام هاتفه)..." aria-label="ابحث في المهام"
            className="min-h-10 w-full rounded-full border max-lg:text-ellipsis border-line/60 bg-card/60 ps-10 pe-4 text-[14px] shadow-[inset_0_1px_2px_rgb(0_0_0/0.06)] outline-none focus:border-gold-line" />
        </form>
        <label className="relative">
          <span className="sr-only">المسؤول</span>
          <select aria-label="المسؤول" value={scope} onChange={(e) => pick("scope", scopeFallback)(e.target.value)}
            className="min-h-10 appearance-none rounded-full border border-line/60 bg-card/60 ps-4 pe-9 text-[14px] font-semibold shadow-[inset_0_1px_0_rgb(255_255_255/0.45)] outline-none focus:border-gold-line">
            {scopes.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
          </select>
          <Icon name="chevron-down" className="pointer-events-none absolute inset-y-0 end-3 my-auto size-4 text-ink-soft" />
        </label>
      </section>

      {/* the state tabs, with the number of tasks of each (a phone: they slide sideways, with no grey bar under them) */}
      <div className="liquid flex gap-1 overflow-x-auto rounded-[20px] p-1.5 max-lg:[scrollbar-width:none] max-lg:[&::-webkit-scrollbar]:hidden" role="group" aria-label="الحالة">
        {STATES.map((s) => {
          const on = state === s.value;
          const n = counts[s.value];
          return (
            <button key={s.value} type="button" aria-pressed={on} onClick={() => pick("state", "active")(s.value)}
              className={`flex min-h-11 shrink-0 grow items-center justify-center gap-2 whitespace-nowrap rounded-[16px] px-4 text-[14px] transition ${on
                ? "btn-gold font-bold shadow-[inset_0_1px_0_rgb(255_255_255/0.4),0_6px_14px_-6px_rgb(140_98_58/0.8)]"
                : "text-ink hover:bg-card/80"}`}>
              <Icon name={s.icon} className="size-4" />
              {s.label}
              {n !== undefined ? <span className="tabular text-[12px] opacity-80">({n})</span> : null}
            </button>
          );
        })}
      </div>

      {q ? (
        <p className="liquid flex flex-wrap items-center gap-2 rounded-full px-4 py-2 text-sm">
          <Icon name="search" className="size-4 text-gold-ink" />
          نتائج البحث عن <strong className="rounded-md bg-gold-tint px-2 text-gold-ink">{q}</strong>
          <button type="button" onClick={() => change((next) => { next.delete("q"); next.delete("page"); })} className="ms-auto inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-ink-soft hover:bg-card-2 hover:text-ink">
            <Icon name="x" className="size-4" /> مسح البحث
          </button>
        </p>
      ) : null}

      {tasks.isPending || outOfRange ? (
        <Loading />
      ) : tasks.isError ? (
        <ErrorState message={errorMessage(tasks.error)} onRetry={() => void tasks.refetch()} />
      ) : tasks.data.items.length === 0 ? (
        <Nothing state={state} />
      ) : (
        <>
          <div role="list" aria-label="المهام" className="grid gap-2.5">
            {/* the table's head (a wide screen only): the names of the columns */}
            <div aria-hidden className={`hidden gap-x-5 px-5 py-2.5 text-[13px] font-bold text-ink-soft xl:grid ${COLUMNS}`}>
              <span>المهمة</span><span>العميل</span><span>المسؤول</span><span>الموعد النهائي</span><span>الأولوية</span><span>الحالة</span><span />
            </div>
            {tasks.data.items.map((t) => (
              <div key={t.id} role="listitem">
                <TaskRow task={t} />
              </div>
            ))}
          </div>
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
          <span className="tabular rounded-full border border-gold-line bg-gold-tint px-2.5 py-0.5 text-[12.5px] font-bold text-gold-ink">{tasksCount(total)}</span>
        ) : null}
      </div>
    </header>
  );
}

const STAT_TONES = {
  gold: "bg-gold-tint text-gold-ink",
  red: "bg-red-bg text-red-ink",
  orange: "bg-orange-bg text-orange-ink",
  green: "bg-green-bg text-green-ink",
} as const;

/** One of the numbers: a small glass card; pressing it opens that state's tab. */
function Stat({ tone, icon, title, value, on, onClick }: {
  tone: keyof typeof STAT_TONES; icon: IconName; title: string; value: number | undefined; on: boolean; onClick: () => void;
}) {
  return (
    <button type="button" onClick={onClick} aria-pressed={on}
      className={`liquid flex items-center justify-between gap-3 rounded-[20px] px-4 py-3.5 text-start transition hover:-translate-y-0.5 motion-reduce:transition-none motion-reduce:hover:translate-y-0 ${on ? "ring-2 ring-gold-line" : ""}`}>
      <span className="grid gap-0.5">
        <span className="text-[13.5px] font-semibold text-ink-soft">{title}</span>
        <span className="tabular text-[28px] leading-none font-bold">{value === undefined ? "—" : value}</span>
      </span>
      <span aria-hidden className={`grid size-11 shrink-0 place-items-center rounded-full ${STAT_TONES[tone]}`}>
        <Icon name={icon} className="size-6" />
      </span>
    </button>
  );
}

// ---------- one task ----------

const who = (t: TaskListItem) => t.client_name ?? "رقم بلا اسم";

/** Arabic number agreement: مهمة واحدة، مهمتان، 3 مهام، 11 مهمة. */
const tasksCount = (n: number) => {
  const r = n % 100;
  return n === 1 ? "مهمة واحدة" : n === 2 ? "مهمتان" : r >= 3 && r <= 10 ? `${n} مهام` : `${n} مهمة`;
};

// the table's columns on a wide screen: what they need, the client, who has it, by when, how urgent, where it stands, the arrow
const COLUMNS = "xl:grid-cols-[minmax(14rem,2.2fr)_minmax(9rem,1.1fr)_minmax(8rem,1fr)_minmax(9rem,1fr)_5.5rem_7rem_2.25rem]";

// the line at a row's start and the tint of its state
const LOOK: Record<string, { bar: string; ring: string }> = {
  urgent: { bar: "bg-red", ring: "hover:border-red/40" },
  overdue: { bar: "bg-orange", ring: "hover:border-orange/40" },
  in_progress: { bar: "bg-gold", ring: "hover:border-gold-line" },
  open: { bar: "bg-blue", ring: "hover:border-blue/40" },
  done: { bar: "bg-green", ring: "hover:border-green/40" },
  cancelled: { bar: "bg-ink-soft/50", ring: "hover:border-line-strong" },
};

/** A task as a row of the table: what they need (and about what), the client, who has it, by when, how urgent, where it stands. */
function TaskRow({ task: t }: { task: TaskListItem }) {
  const live = t.state !== "done" && t.state !== "cancelled";
  const urgent = isUrgent(t) && live;
  const look = LOOK[urgent ? "urgent" : t.state] ?? LOOK.open!;
  const late = t.state === "overdue";
  // the list's filters and page go along, for the task's «رجوع لكل المهام»
  const { search } = useLocation();
  return (
    // a phone: two values side by side too (what they need on its own line), so a task is half as tall
    <Link to={`/tasks/${t.id}`} state={{ from: search }}
      className={`group relative grid items-center gap-x-5 gap-y-3 overflow-hidden rounded-[18px] border border-line/60 bg-card/60 py-3.5 ps-5 pe-4 shadow-[inset_0_1px_0_rgb(255_255_255/0.45),0_6px_18px_-12px_rgb(60_44_20/0.35)] backdrop-blur-md transition hover:-translate-y-px hover:bg-card/85 hover:shadow-[0_14px_30px_-14px_rgb(60_44_20/0.4)] motion-reduce:transition-none motion-reduce:hover:translate-y-0 max-sm:grid-cols-2 max-sm:gap-x-3 max-sm:gap-y-2.5 sm:grid-cols-2 ${COLUMNS} ${look.ring}`}>
      <span aria-hidden className={`absolute inset-y-3 start-0 w-1 rounded-e-full ${look.bar}`} />

      {/* what they need */}
      <Cell term="المهمة" className="max-sm:col-span-2">
        <span className="font-bold">{label(TASK_TYPES, t.type)}</span>
        {t.matter_type ? (
          <span className="ms-2 inline-block rounded-full border border-line/70 bg-card/70 px-2 py-0.5 align-middle text-[11.5px] font-normal text-ink-soft">{matterLabel(t.matter_type)}</span>
        ) : null}
      </Cell>

      {/* the client */}
      <Cell term="العميل">
        <span className="flex min-w-0 items-center gap-2.5">
          <Avatar name={who(t)} className="size-9 shrink-0 text-[12px]" />
          <span className="grid min-w-0">
            <span className="truncate font-semibold">{who(t)}</span>
            <span className="tabular text-[12px] text-ink-soft" dir="ltr" style={{ textAlign: "right" }}>…{t.phone_last3}</span>
          </span>
        </span>
      </Cell>

      {/* who has it */}
      <Cell term="المسؤول">
        {t.staff ? <span className="font-semibold">{t.staff.name}</span>
          : <span className="font-semibold text-orange-ink">{live ? "لم يستلمها أحد بعد" : "غير مُسندة"}</span>}
      </Cell>

      {/* by when */}
      <Cell term={late ? "كان موعدها النهائي" : "الموعد النهائي"}>
        <span className={`tabular font-semibold ${late ? "text-red-ink" : ""}`}>{cairoWhen(t.due_at)}</span>
      </Cell>

      {/* how urgent */}
      <Cell term="الأولوية">
        <Chip tone={urgent ? "urgent" : "gold"}>{urgent ? "عاجلة" : "عادية"}</Chip>
      </Cell>

      {/* where it stands */}
      <span className="flex items-center justify-between gap-3 max-sm:col-span-2 sm:col-span-2 xl:col-span-2 xl:grid xl:grid-cols-[7rem_2.25rem]">
        <Chip tone={t.state}>{label(TASK_STATES, t.state)}</Chip>
        <span aria-hidden className="grid size-8 shrink-0 place-items-center rounded-full border border-line/60 bg-card/70 text-ink-soft transition group-hover:border-gold-line group-hover:bg-gold-tint group-hover:text-gold-ink">
          <Icon name="chevron-start" className="size-4 transition group-hover:-translate-x-0.5" />
        </span>
      </span>
    </Link>
  );
}

/** A value of a row. On a wide screen the table's head names it; on a smaller one it names itself above. */
function Cell({ term, children, className = "" }: { term: string; children: ReactNode; className?: string }) {
  return (
    <span className={`grid min-w-0 gap-0.5 ${className}`}>
      <span className="text-[11.5px] text-ink-soft xl:hidden">{term}</span>
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
      <p className="text-[16px] font-bold">{state === "active" ? "لا توجد مهام تحتاج إلى إجراء الآن" : "لا توجد مهام هنا"}</p>
      {state === "active" ? <p className="max-w-sm text-[13px] text-ink-soft">عندما يفتح حازم مهمة جديدة ستظهر هنا تلقائيًا.</p> : null}
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
        <Icon name="chevron-end" className="size-4" />السابقة
      </button>
      <span className="tabular rounded-full bg-gold-tint px-3 py-1 text-[13px] font-bold text-gold-ink">صفحة {page} من {pages}</span>
      <button type="button" className={pill} disabled={page >= pages} onClick={() => onPage(page + 1)}>
        التالية<Icon name="chevron-start" className="size-4" />
      </button>
    </nav>
  );
}
