// The panel's two kinds of feedback, so every page answers the same way (ideas taken from PR #14, in the panel's own look):
// - toast(): a short line after an action worked («اتبعت للعميل»), gone by itself after a few seconds;
// - confirmAction(): asks before something the client will see or that cannot be taken back, in the panel's own
//   dialog (never the browser's window.confirm, which a phone may block), and resolves true or false.
// Both are small stores read by <Toaster /> and <ConfirmHost />, mounted once in the layout.
import { useEffect, useRef, useSyncExternalStore } from "react";
import { Icon } from "./Icon";

type Toast = { id: number; text: string; tone: "good" | "bad" };
type Ask = { title: string; body?: string; yes: string; danger?: boolean; resolve: (ok: boolean) => void };

let toasts: Toast[] = [];
let ask: Ask | null = null;
let seq = 0;
const listeners = new Set<() => void>();
const emit = () => listeners.forEach((l) => l());
const subscribe = (l: () => void) => {
  listeners.add(l);
  return () => {
    listeners.delete(l);
  };
};

/** A short line after an action worked (or failed), gone by itself; at most three at a time. */
export function toast(text: string, tone: Toast["tone"] = "good") {
  const t = { id: ++seq, text, tone };
  toasts = [...toasts.slice(-2), t];
  emit();
  setTimeout(() => {
    toasts = toasts.filter((x) => x.id !== t.id);
    emit();
  }, tone === "bad" ? 6000 : 3500);
}

/** The panel's own «أكيد؟»: true when the person chose `yes`, false on «لأ، ارجع», Esc or a click outside. */
export function confirmAction(o: { title: string; body?: string; yes: string; danger?: boolean }): Promise<boolean> {
  return new Promise((resolve) => {
    ask?.resolve(false); // one question at a time: an older one is answered «no»
    ask = { ...o, resolve };
    emit();
  });
}

function answer(ok: boolean) {
  ask?.resolve(ok);
  ask = null;
  emit();
}

export function Toaster() {
  const list = useSyncExternalStore(subscribe, () => toasts);
  return (
    <div className="pointer-events-none fixed inset-x-0 bottom-20 z-50 grid justify-items-center gap-2 px-4 lg:bottom-6" aria-live="polite" role="status">
      {list.map((t) => (
        <div key={t.id}
          className="pointer-events-auto flex max-w-md items-center gap-2.5 rounded-[var(--radius-control)] border border-line-strong bg-card px-4 py-2.5 text-[14px] font-medium text-ink shadow-lg">
          <Icon name={t.tone === "good" ? "check-circle" : "alert-circle"} className={`size-5 shrink-0 ${t.tone === "good" ? "text-green-ink" : "text-red-ink"}`} />
          {t.text}
        </div>
      ))}
    </div>
  );
}

export function ConfirmHost() {
  const current = useSyncExternalStore(subscribe, () => ask);
  const yes = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    if (!current) return;
    yes.current?.focus();
    const key = (e: KeyboardEvent) => { if (e.key === "Escape") answer(false); };
    document.addEventListener("keydown", key);
    return () => document.removeEventListener("keydown", key);
  }, [current]);
  if (!current) return null;
  return (
    <div className="fixed inset-0 z-50 grid place-items-center bg-black/50 p-4" onMouseDown={(e) => { if (e.target === e.currentTarget) answer(false); }}>
      <div role="alertdialog" aria-modal="true" aria-labelledby="confirm-title" aria-describedby={current.body ? "confirm-body" : undefined}
        className="grid w-full max-w-md gap-4 rounded-[var(--radius-control)] border border-line bg-card p-5 text-ink shadow-xl">
        <div className="grid gap-1.5">
          <h2 id="confirm-title" className="text-[17px] font-semibold">{current.title}</h2>
          {current.body ? <p id="confirm-body" className="whitespace-pre-line text-[14.5px] text-ink-soft">{current.body}</p> : null}
        </div>
        <div className="flex flex-wrap justify-end gap-2">
          <button type="button" onClick={() => answer(false)}
            className="inline-flex min-h-10 items-center rounded-[var(--radius-control)] border border-line-strong bg-card px-4 text-[14.5px] font-medium hover:border-gold-line">
            لأ، ارجع
          </button>
          <button ref={yes} type="button" onClick={() => answer(true)}
            className={`inline-flex min-h-10 items-center rounded-[var(--radius-control)] border px-4 text-[14.5px] font-semibold ${current.danger ? "border-red-ink/40 bg-red-bg text-red-ink hover:opacity-90" : "btn-gold hover:brightness-105"}`}>
            {current.yes}
          </button>
        </div>
      </div>
    </div>
  );
}
