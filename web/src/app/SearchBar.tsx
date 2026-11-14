// v1.110 (the owner: «السيرش بار هنا حاسه بيسرش غلط» … «اعمل بحث شامل»): the top bar's search. As it is typed (a quarter of a
// second after the last key) the clients, their appointments and the tasks come under it, by a part of the name in any
// order («احمد» finds «أحمد») or the last digits of the number (Arabic digits and spaces too). Enter opens the first one, Esc
// closes the list, and Ctrl K still opens the quick commands (CommandPalette).
import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router";
import { SEARCH_MIN, useSearch } from "../shared/api/search";
import type { SearchResults } from "../shared/api/types";
import { countWords, shortType } from "../shared/lib/group";
import { APPOINTMENT_STATES, BOOKING_SERVICES, label, TASK_STATES } from "../shared/lib/labels";
import { cairoDate, cairoWhen } from "../shared/lib/time";
import { Icon, type IconName } from "../shared/ui/Icon";
import { CommandPalette } from "./CommandPalette";

const OPEN_TASKS = { one: "مهمة مفتوحة واحدة", two: "مهمتان مفتوحتان", few: "مهام مفتوحة", many: "مهمة مفتوحة" };
const NO_NAME = "رقم بلا اسم";

export interface Hit { key: string; to: string; icon: IconName; title: string; line: string }

/** The results in the order the list shows them: the clients, the appointments, then the tasks (an empty kind left out). */
export function searchGroups(r: SearchResults, now: Date = new Date()): { title: string; hits: Hit[] }[] {
  const ends = (last3: string | null) => (last3 ? `رقمه ينتهي بـ ${last3}` : "");
  const line = (...parts: string[]) => parts.filter(Boolean).join(" · ");
  const day = (iso: string) => `/appointments?day=${cairoDate(new Date(iso))}`;
  return [
    {
      title: "العملاء",
      hits: r.clients.map((c): Hit => ({
        key: `c${c.id}`, icon: "clients", title: c.name ?? NO_NAME,
        // his task (the unfinished first), else the day of his appointment
        to: c.task_id ? `/tasks/${c.task_id}` : c.appointment_at ? day(c.appointment_at) : "/tasks",
        line: line(ends(c.phone_last3), c.open_tasks > 0 ? countWords(c.open_tasks, OPEN_TASKS) : ""),
      })),
    },
    {
      title: "المواعيد",
      hits: r.appointments.map((a): Hit => ({
        key: `a${a.id}`, icon: "calendar", title: a.name ?? NO_NAME, to: day(a.starts_at),
        line: line(label(BOOKING_SERVICES, a.service), cairoWhen(a.starts_at, now), label(APPOINTMENT_STATES, a.state)),
      })),
    },
    {
      title: "المهام",
      hits: r.tasks.map((t): Hit => ({
        key: `t${t.id}`, icon: "tasks", title: shortType(t.type), to: `/tasks/${t.id}`,
        line: line(t.name ?? NO_NAME, ends(t.phone_last3), label(TASK_STATES, t.state)),
      })),
    },
  ].filter((g) => g.hits.length > 0);
}

export function SearchBar() {
  const navigate = useNavigate();
  const form = useRef<HTMLFormElement>(null);
  const listId = useId();
  const [palette, setPalette] = useState(false);
  const [text, setText] = useState("");
  const [term, setTerm] = useState("");
  const [open, setOpen] = useState(false);
  useEffect(() => {
    const t = setTimeout(() => setTerm(text.trim()), 250);
    return () => clearTimeout(t);
  }, [text]);
  useEffect(() => {
    const key = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        setPalette(true);
      }
    };
    document.addEventListener("keydown", key);
    return () => document.removeEventListener("keydown", key);
  }, []);
  useEffect(() => {
    if (!open) return;
    const away = (e: MouseEvent) => {
      if (!form.current?.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", away);
    return () => document.removeEventListener("mousedown", away);
  }, [open]);

  const found = useSearch(term);
  const showing = open && term.length >= SEARCH_MIN;
  const groups = found.data ? searchGroups(found.data) : [];
  const first = groups[0]?.hits[0];
  const go = (to: string) => {
    setOpen(false);
    setText("");
    setTerm("");
    void navigate(to);
  };
  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (first) go(first.to);
  };

  return (
    <form ref={form} role="search" data-tour="search" onSubmit={submit}
      className="group/search relative flex min-h-[2.9rem] min-w-0 flex-1 items-center gap-2.5 rounded-full border border-line/80 bg-card/70 ps-4 pe-2 shadow-[inset_0_1px_2px_rgb(0_0_0/0.04)] transition duration-200
        hover:border-gold-line focus-within:border-gold focus-within:bg-card focus-within:shadow-[0_0_0_4px_rgb(176_133_82/0.16)] lg:max-w-[36rem]">
      <Icon name="search" className="size-[18px] shrink-0 text-ink-soft transition-colors group-focus-within/search:text-gold-ink" />
      {/* a phone shows the start of the hint, ending in «…» */}
      <input type="search" aria-label="بحث" value={text} aria-expanded={showing} aria-controls={listId} autoComplete="off"
        placeholder="ابحث عن عميل أو موعد أو مهمة، بالاسم أو بآخر أرقام الهاتف..."
        onChange={(e) => { setText(e.target.value); setOpen(true); }}
        onFocus={() => setOpen(true)}
        onKeyDown={(e) => { if (e.key === "Escape") setOpen(false); }}
        className="min-h-10 min-w-0 flex-1 bg-transparent text-[14px] outline-none placeholder:text-ink-soft/80 max-lg:text-ellipsis" />
      <button type="button" onClick={() => setPalette(true)} aria-label="اختصارات وبحث (Ctrl K)"
        className="hidden shrink-0 items-center gap-1 rounded-full px-1.5 py-1 font-sans text-[11px] font-semibold text-ink-soft transition hover:bg-card-2 sm:flex" dir="ltr">
        <kbd className="rounded-md border border-line border-b-2 bg-card px-1.5 py-0.5 font-sans">Ctrl</kbd>
        <kbd className="rounded-md border border-line border-b-2 bg-card px-1.5 py-0.5 font-sans">K</kbd>
      </button>
      {showing ? (
        <div id={listId} role="region" aria-label="نتائج البحث"
          className="liquid liquid-strong absolute inset-x-0 top-[calc(100%+0.5rem)] z-30 grid max-h-[70vh] gap-2 overflow-y-auto rounded-2xl p-2">
          {found.isError ? (
            <p className="px-3 py-2 text-sm text-ink-soft">تعذّر البحث الآن. حاول مرة أخرى.</p>
          ) : !found.data ? (
            <p className="px-3 py-2 text-sm text-ink-soft">جارٍ البحث…</p>
          ) : groups.length === 0 ? (
            <p className="px-3 py-2 text-sm text-ink-soft">لا توجد نتائج. جرّب جزءًا من الاسم، أو آخر 3 أرقام من الهاتف.</p>
          ) : (
            groups.map((g) => (
              <section key={g.title} aria-label={g.title} className="grid gap-0.5">
                <h3 className="px-2.5 pt-1 text-[11.5px] font-bold text-ink-soft">{g.title}</h3>
                <ul className="grid gap-0.5">
                  {g.hits.map((h) => (
                    <li key={h.key}>
                      <Link to={h.to} onClick={(e) => { e.preventDefault(); go(h.to); }}
                        className={`flex items-center gap-2.5 rounded-lg px-2.5 py-2 text-start hover:bg-muted ${h === first ? "bg-muted/60" : ""}`}>
                        <Icon name={h.icon} className="size-4 shrink-0 text-gold-ink" />
                        <span className="grid min-w-0 gap-0.5">
                          <span className="truncate text-sm font-bold">{h.title}</span>
                          {h.line ? <span className="truncate text-xs text-ink-soft">{h.line}</span> : null}
                        </span>
                      </Link>
                    </li>
                  ))}
                </ul>
              </section>
            ))
          )}
        </div>
      ) : null}
      <CommandPalette open={palette} onClose={() => setPalette(false)} />
    </form>
  );
}
