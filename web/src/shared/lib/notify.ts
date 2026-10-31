// The urgent alerts reach a person who is not looking at the panel (the owner: an urgent request must not wait for
// someone to switch back to the tab): a short sound, a notification from the browser when the tab is hidden (once the
// person allowed it), and the unread count in the tab's title.
import { useEffect, useRef } from "react";
import type { PanelAlert } from "../api/types";
import { ALERT_KINDS, label, TASK_TYPES } from "./labels";

const TITLE = "لوحة حازم";
// the alerts worth a sound: a client's urgent request (urgent_request, safety_alert) and a system failure
const LOUD = new Set(["task_urgent", "system"]);

export const canNotify = () => typeof window !== "undefined" && "Notification" in window;
export const mayAskToNotify = () => canNotify() && Notification.permission === "default";

/** Two soft tones; silent when the browser blocks sound before the person touched the page. */
function chime() {
  try {
    const Ctx = window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
    if (!Ctx) return;
    const ctx = new Ctx();
    [880, 1175].forEach((hz, i) => {
      const osc = ctx.createOscillator();
      const gain = ctx.createGain();
      const at = ctx.currentTime + i * 0.18;
      osc.frequency.value = hz;
      gain.gain.setValueAtTime(0.0001, at);
      gain.gain.exponentialRampToValueAtTime(0.2, at + 0.02);
      gain.gain.exponentialRampToValueAtTime(0.0001, at + 0.35);
      osc.connect(gain).connect(ctx.destination);
      osc.start(at);
      osc.stop(at + 0.4);
    });
    setTimeout(() => void ctx.close(), 1000);
  } catch {
    // no sound here
  }
}

/**
 * Watches the bell's alerts: the first list only sets what is already known; after that each new unread urgent or
 * system alert chimes and, with the tab hidden, shows a notification that opens its task.
 */
export function useAlertNotifications(items: PanelAlert[] | undefined, unread: number, open: (taskId: string) => void) {
  const seen = useRef<Set<number> | null>(null);

  useEffect(() => {
    document.title = unread > 0 ? `(${unread > 9 ? "9+" : unread}) ${TITLE}` : TITLE;
  }, [unread]);
  useEffect(() => () => void (document.title = TITLE), []);

  useEffect(() => {
    if (!items) return;
    if (!seen.current) {
      seen.current = new Set(items.map((a) => a.id));
      return;
    }
    const fresh = items.filter((a) => !a.read && !seen.current!.has(a.id));
    for (const a of items) seen.current.add(a.id);
    const loud = fresh.filter((a) => LOUD.has(a.kind));
    if (!loud.length) return;
    chime();
    if (!document.hidden || !canNotify() || Notification.permission !== "granted") return;
    for (const a of loud) {
      const n = new Notification(label(ALERT_KINDS, a.kind), {
        body: a.kind === "system" ? "افتح اللوحة وشوف التنبيه." : `${label(TASK_TYPES, a.task_type ?? "")} · ${a.client_name ?? "رقم من غير اسم"}`,
        tag: `hazem-alert-${a.id}`,
      });
      n.onclick = () => {
        window.focus();
        if (a.task_id) open(a.task_id);
        n.close();
      };
    }
  }, [items, open]);
}
