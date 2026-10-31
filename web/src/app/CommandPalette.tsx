import { useEffect, useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router";
import { can, useMe } from "../shared/api/session";
import type { Permission } from "../shared/api/types";
import { Icon, type IconName } from "../shared/ui/Icon";

type Command = { id: string; text: string; hint: string; icon: IconName; to: string; words: string; permission?: Permission };

/** Every place and common job, with the permission its page asks for (App.tsx), so nobody is shown a door he cannot open. */
const COMMANDS: Command[] = [
  { id: "overdue", text: "المهام المتأخرة", hint: "المهام", icon: "alert", to: "/tasks?state=overdue", words: "عاجل متأخر مستعجل مهام" },
  { id: "receipts", text: "راجع الإيصالات والتحويلات", hint: "الإيصالات والمدفوعات", icon: "receipt", to: "/receipts", words: "ايصال إيصال تحويل فلوس دفع مراجعة", permission: "schedule.manage" },
  { id: "bookings", text: "الحجوزات والمواعيد", hint: "المواعيد", icon: "calendar", to: "/appointments", words: "مواعيد حجز حجوزات النهارده بكرة", permission: "schedule.manage" },
  { id: "close-day", text: "اقفل يوم أو غيّر ساعاته", hint: "المواعيد", icon: "calendar", to: "/appointments", words: "اقفل يوم اجازة إجازة ساعات تقويم", permission: "schedule.manage" },
  { id: "services", text: "طلبات الخدمات (عقد، إنذار، رأي)", hint: "الخدمات", icon: "document", to: "/services", words: "سعر خدمة عقد انذار إنذار رأي مراجعة صياغة", permission: "services.manage" },
  { id: "urf", text: "حالات العرفي", hint: "العرفي", icon: "document", to: "/urf", words: "عرفي جواز طلاق ورق", permission: "urf.manage" },
  { id: "prices", text: "الأسعار", hint: "صفحة", icon: "receipt", to: "/prices", words: "اسعار أسعار سعر" },
  { id: "home", text: "الرئيسية", hint: "صفحة", icon: "home", to: "/", words: "رئيسية داشبورد" },
  { id: "tasks", text: "المهام", hint: "صفحة", icon: "tasks", to: "/tasks", words: "مهام طلبات" },
  { id: "chats", text: "المحادثات", hint: "Chatwoot", icon: "chat", to: "/chats", words: "محادثات شات واتساب chatwoot" },
  { id: "reports", text: "التقارير", hint: "صفحة", icon: "reports", to: "/reports", words: "تقارير ارقام أرقام شهر", permission: "reports.view" },
  { id: "lists", text: "قوايم المكتب", hint: "الإعدادات", icon: "clients", to: "/lists", words: "قوايم موكلين ممنوعين", permission: "lists.manage" },
  { id: "texts", text: "نصوص حازم", hint: "الإعدادات", icon: "message", to: "/texts", words: "نصوص رسائل حازم", permission: "texts.manage" },
  { id: "users", text: "الحسابات", hint: "الإعدادات", icon: "users", to: "/users", words: "حسابات موظفين مستخدمين", permission: "users.manage" },
  { id: "settings", text: "الإعدادات", hint: "صفحة", icon: "settings", to: "/settings", words: "اعدادات إعدادات" },
  { id: "booking-settings", text: "إعدادات المواعيد", hint: "المواعيد", icon: "settings", to: "/appointments/settings", words: "ساعات الشغل المواعيد الثابتة أسبوع اسبوع الحجز من واتساب بيانات المكتب الدفع انستاباي InstaPay محفظة", permission: "schedule.manage" },
  { id: "testing", text: "حازم ينسى رقم تجربة", hint: "أدوات التجربة", icon: "refresh", to: "/testing", words: "انسى امسح تجربة اختبار رقم ارقام أرقام", permission: "testing.forget" },
  { id: "account", text: "حسابي", hint: "الإعدادات", icon: "account", to: "/account", words: "حسابي كلمة السر باسورد" },
];

/**
 * Ctrl K (idea from PR #14): one box to go anywhere, do the common jobs, or find a client's tasks by name or the last
 * digits of the number (the first line, whenever something is typed). Arrows move, Enter runs, Esc closes.
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

  const allowed = useMemo(() => COMMANDS.filter((c) => !c.permission || can(me, c.permission)), [me]);
  const term = q.trim();
  const matches = term ? allowed.filter((c) => `${c.text} ${c.words}`.includes(term)) : allowed.slice(0, 8);
  const list: Command[] = term
    ? [{ id: "search", text: `دوّر في المهام عن «${term}»`, hint: "بالاسم أو آخر أرقام الموبايل", icon: "search", to: `/tasks?q=${encodeURIComponent(term)}`, words: "" }, ...matches]
    : matches;
  const pick = Math.min(at, Math.max(0, list.length - 1));
  const run = (c: Command | undefined) => {
    if (!c) return;
    onClose();
    navigate(c.to);
  };

  if (!open) return null;
  return (
    <div className="fixed inset-0 z-50 grid items-start justify-items-center bg-black/50 px-4 pt-[12vh]"
      onMouseDown={(e) => { if (e.target === e.currentTarget) onClose(); }}>
      <div role="dialog" aria-modal="true" aria-label="اختصارات وبحث"
        className="grid w-full max-w-xl overflow-hidden rounded-[var(--radius-control)] border border-line bg-card text-ink shadow-xl">
        <label className="flex items-center gap-3 border-b border-line px-4">
          <Icon name="search" className="size-5 shrink-0 text-ink-soft" />
          <input ref={input} value={q} onChange={(e) => { setQ(e.target.value); setAt(0); }} aria-label="دوّر أو اختار"
            placeholder="روح لصفحة، أو اكتب اسم عميل أو آخر أرقام موبايله"
            className="min-h-14 min-w-0 flex-1 bg-transparent text-[16px] outline-none placeholder:text-ink-soft"
            onKeyDown={(e) => {
              if (e.key === "ArrowDown") { e.preventDefault(); setAt((i) => Math.min(i + 1, list.length - 1)); }
              else if (e.key === "ArrowUp") { e.preventDefault(); setAt((i) => Math.max(i - 1, 0)); }
              else if (e.key === "Enter") { e.preventDefault(); run(list[pick]); }
              else if (e.key === "Escape") { e.preventDefault(); onClose(); }
            }} />
          <kbd className="shrink-0 rounded border border-line bg-card-2 px-1.5 text-[12px] text-ink-soft" dir="ltr">Esc</kbd>
        </label>
        <ul role="listbox" aria-label="النتايج" className="grid max-h-[50vh] gap-0.5 overflow-y-auto p-2">
          {list.length === 0 ? <li className="px-3 py-6 text-center text-[14px] text-ink-soft">مفيش حاجة بالاسم ده.</li> : null}
          {list.map((c, i) => (
            <li key={c.id} role="option" aria-selected={i === pick}>
              <button type="button" onMouseEnter={() => setAt(i)} onClick={() => run(c)}
                className={`flex w-full items-center gap-3 rounded-[10px] px-3 py-2.5 text-start ${i === pick ? "bg-gold-tint text-gold-ink" : "hover:bg-card-2"}`}>
                <Icon name={c.icon} className="size-[18px] shrink-0" />
                <span className="flex-1 truncate text-[15px] font-medium">{c.text}</span>
                <span className="shrink-0 text-[12.5px] text-ink-soft">{c.hint}</span>
              </button>
            </li>
          ))}
        </ul>
        <p className="flex gap-4 border-t border-line bg-card-2 px-4 py-2 text-[12px] text-ink-soft">
          <span><kbd dir="ltr">↑ ↓</kbd> تتحرك</span><span><kbd dir="ltr">Enter</kbd> تفتح</span><span><kbd dir="ltr">Ctrl K</kbd> من أي صفحة</span>
        </p>
      </div>
    </div>
  );
}
