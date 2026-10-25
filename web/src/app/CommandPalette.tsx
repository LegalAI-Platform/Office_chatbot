import { useEffect, useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router";
import { can, useMe } from "../shared/api/session";
import type { Permission } from "../shared/api/types";
import { Icon, type IconName } from "../shared/ui/Icon";

type Command = { id: string; text: string; hint: string; icon: IconName; run: () => void; words: string; permission?: Permission };

/**
 * Ctrl K (or the search in the top bar): one box to go anywhere, do the common jobs, or find a client's
 * tasks by name or the last digits of the number. Arrows move, Enter runs, Esc closes.
 */
export function CommandPalette({ open, onClose }: { open: boolean; onClose: () => void }) {
  const navigate = useNavigate();
  const { data: me } = useMe();
  const [q, setQ] = useState("");
  const [at, setAt] = useState(0);
  const input = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!open) return;
    setQ("");
    setAt(0);
    setTimeout(() => input.current?.focus(), 0);
  }, [open]);

  const go = (to: string) => () => { onClose(); navigate(to); };
  const all: Command[] = useMemo(() => [
    { id: "urgent", text: "المهام المستعجلة", hint: "العاجل والمتأخر", icon: "alert", run: go("/tasks?state=overdue"), words: "عاجل متأخر مستعجل مهام" },
    { id: "receipts", text: "راجع الإيصالات", hint: "الإيصالات المستنية", icon: "wallet", run: go("/receipts"), words: "ايصال إيصال فلوس دفع مراجعة", permission: "schedule.manage" },
    { id: "today", text: "مواعيد النهارده", hint: "الحجوزات", icon: "calendar", run: go("/appointments?tab=bookings"), words: "مواعيد حجز النهارده", permission: "schedule.manage" },
    { id: "close-day", text: "اقفل يوم أو غيّر ساعاته", hint: "تقويم المواعيد", icon: "calendar", run: go("/appointments"), words: "اقفل يوم اجازة ساعات تقويم", permission: "schedule.manage" },
    { id: "price", text: "حدد سعر خدمة", hint: "طلبات الخدمات", icon: "file-text", run: go("/services"), words: "سعر خدمة عقد انذار رأي", permission: "services.price" },
    { id: "urf", text: "راجع ورق العرفي", hint: "حالات العرفي", icon: "scale", run: go("/urf"), words: "عرفي جواز طلاق ورق", permission: "urf.manage" },
    { id: "home", text: "الرئيسية", hint: "صفحة", icon: "home", run: go("/"), words: "رئيسية داشبورد" },
    { id: "tasks", text: "المهام", hint: "صفحة", icon: "tasks", run: go("/tasks"), words: "مهام طلبات" },
    { id: "appointments", text: "المواعيد", hint: "صفحة", icon: "calendar", run: go("/appointments"), words: "مواعيد كالندر", permission: "schedule.manage" },
    { id: "chats", text: "المحادثات", hint: "Chatwoot", icon: "chat", run: go("/chats"), words: "محادثات شات واتساب chatwoot" },
    { id: "reports", text: "التقارير", hint: "صفحة", icon: "reports", run: go("/reports"), words: "تقارير ارقام شهر", permission: "reports.view" },
    { id: "lists", text: "قوايم المكتب", hint: "الإعدادات", icon: "clients", run: go("/lists"), words: "قوايم موكلين ممنوعين", permission: "lists.manage" },
    { id: "texts", text: "نصوص حازم", hint: "الإعدادات", icon: "chat", run: go("/texts"), words: "نصوص رسائل حازم", permission: "texts.manage" },
    { id: "users", text: "الحسابات", hint: "الإعدادات", icon: "users", run: go("/users"), words: "حسابات موظفين مستخدمين", permission: "users.manage" },
    { id: "account", text: "حسابي", hint: "الإعدادات", icon: "account", run: go("/account"), words: "حسابي كلمة السر" },
  ], []);

  const allowed = all.filter((c) => !c.permission || can(me, c.permission));
  const term = q.trim();
  const matches = term ? allowed.filter((c) => `${c.text} ${c.words}`.includes(term)) : allowed.slice(0, 8);
  const list: Command[] = term
    ? [{ id: "search", text: `دوّر في المهام عن «${term}»`, hint: "بالاسم أو آخر أرقام الموبايل", icon: "search", run: go(`/tasks?q=${encodeURIComponent(term)}`), words: "" }, ...matches]
    : matches;
  const pick = Math.min(at, Math.max(0, list.length - 1));

  if (!open) return null;
  return (
    <div className="fixed inset-0 z-50 grid items-start justify-items-center bg-black/55 px-4 pt-[12vh]"
      onMouseDown={(e) => { if (e.target === e.currentTarget) onClose(); }}>
      <div role="dialog" aria-modal="true" aria-label="اختصارات وبحث" className="sheet-in pop grid w-full max-w-xl overflow-hidden rounded-[var(--radius-sheet)]">
        <label className="flex items-center gap-3 border-b border-line px-4">
          <Icon name="search" className="size-5 shrink-0 text-ink-faint" />
          <input ref={input} value={q} onChange={(e) => { setQ(e.target.value); setAt(0); }} aria-label="دوّر أو اختار"
            placeholder="روح لصفحة، أو اكتب اسم عميل أو آخر أرقام موبايله"
            className="min-h-14 min-w-0 flex-1 bg-transparent text-[16px] outline-none placeholder:text-ink-faint"
            onKeyDown={(e) => {
              if (e.key === "ArrowDown") { e.preventDefault(); setAt((i) => Math.min(i + 1, list.length - 1)); }
              else if (e.key === "ArrowUp") { e.preventDefault(); setAt((i) => Math.max(i - 1, 0)); }
              else if (e.key === "Enter") { e.preventDefault(); list[pick]?.run(); }
              else if (e.key === "Escape") { e.preventDefault(); onClose(); }
            }} />
          <kbd className="shrink-0 rounded border border-line bg-card-2 px-1.5 text-[12px] text-ink-faint" dir="ltr">Esc</kbd>
        </label>
        <ul role="listbox" aria-label="النتايج" className="grid max-h-[50vh] gap-0.5 overflow-y-auto p-2">
          {list.length === 0 ? <li className="px-3 py-6 text-center text-[14px] text-ink-soft">مفيش حاجة بالاسم ده.</li> : null}
          {list.map((c, i) => (
            <li key={c.id} role="option" aria-selected={i === pick}>
              <button type="button" onMouseEnter={() => setAt(i)} onClick={c.run}
                className={`flex w-full items-center gap-3 rounded-[10px] px-3 py-2.5 text-start ${i === pick ? "bg-accent-tint text-accent-ink" : "hover:bg-card-2"}`}>
                <Icon name={c.icon} className="size-[18px] shrink-0" />
                <span className="flex-1 truncate text-[15px] font-medium">{c.text}</span>
                <span className="shrink-0 text-[12.5px] text-ink-faint">{c.hint}</span>
              </button>
            </li>
          ))}
        </ul>
        <p className="flex gap-4 border-t border-line bg-card-2/60 px-4 py-2 text-[12px] text-ink-faint">
          <span><kbd dir="ltr">↑ ↓</kbd> تتحرك</span><span><kbd dir="ltr">Enter</kbd> تفتح</span><span><kbd dir="ltr">Ctrl K</kbd> من أي صفحة</span>
        </p>
      </div>
    </div>
  );
}
