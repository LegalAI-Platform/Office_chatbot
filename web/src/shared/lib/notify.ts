// The alerts reach a person who is not looking at the panel. First the urgent ones: the owner said an urgent request must
// not wait for someone to switch back to the tab. Since v1.104 every new alert counts: the owner, 2026-11-12, «عايز
// النوتيفيكيشن دي تبقى مرتبطة بنوتيفيكيشن الويندوز … تيجي حتى لو أنا كنت مش واقف على نفس التاب».
// - Each new alert becomes a notification from the browser. Windows shows it in the screen's corner like any app's, while
//   the panel is open in another tab or window, once the person allowed it in the bell.
// - The clients' requests come first. The system's problems come after them and make no sound (the owner: «أنا عايز
//   الطلبات تبقى يعني ده أهم حاجة عنده … مشاكل فيه، هو يشوفها عادي، بس ده مش أهم حاجة»).
// - An urgent request also chimes.
// - The unread requests are counted in the tab's title.
import { useEffect, useRef } from "react";
import type { PanelAlert } from "../api/types";
import { ALERT_KINDS, label, matterLabel, SYSTEM_WHAT, TASK_TYPES } from "./labels";
import { cairoWhen } from "./time";

const TITLE = "لوحة حازم";
// the alert worth a sound: a client's urgent request (urgent_request, safety_alert). v1.104: a system failure no more.
const LOUD = new Set(["task_urgent"]);
/** The office's seal beside the words (the landing page's, v1.103). */
const ICON = "/landing/img/logo.png";
/** More new alerts at once than this: these, then one notification that says how many more. */
const AT_ONCE = 3;

export type NotifyState = "unsupported" | "default" | "granted" | "denied";

/**
 * A computer's browser. A phone's shows a page's notifications only from a service worker, which the panel does not
 * have (Chrome on Android refuses `new Notification`).
 */
export const canNotify = () =>
  typeof window !== "undefined" && "Notification" in window && !/Android|iPhone|iPad|iPod/i.test(navigator.userAgent);
export const notifyState = (): NotifyState => (canNotify() ? Notification.permission : "unsupported");

/** The line under an alert's title, as the bell shows it: the system's problem, or the task's kind and client. */
export const alertLine = (a: PanelAlert): string =>
  a.kind === "system"
    ? label(SYSTEM_WHAT, a.system_what) || "افتح اللوحة وشوف التنبيه."
    : `${label(TASK_TYPES, a.task_type ?? "")} · ${a.client_name ?? "رقم من غير اسم"}`;

/** The task kind each alert's title already names. */
const SAME_AS_TITLE: Record<string, string> = { task_urgent: "urgent_request", task_human: "human_requested", task_rating: "rating_low" };

/** A line that reads right to left in Windows' own notification, whatever the computer's language. */
const rtl = (s: string) => `‏${s}‏`;

/** The notification's title: the kind of alert, and the client for a task. */
export const alertTitle = (a: PanelAlert): string =>
  a.kind === "system" ? label(ALERT_KINDS, a.kind) : `${label(ALERT_KINDS, a.kind)} · ${a.client_name ?? "رقم من غير اسم"}`;

/**
 * Under the title (v1.104, the owner: «ما فيهاش تفاصيل»): what it is about and the number's last 3 digits, then when and
 * who has it. Never the client's words (the owner: «من غير كلامه»). A system alert says what failed.
 */
export function alertDetails(a: PanelAlert, now: Date = new Date()): string[] {
  if (a.kind === "system") return [alertLine(a)];
  // the task's kind, unless the title already says it (an urgent request's, a request for someone's, a low rating's)
  const type = a.task_type && a.task_type !== SAME_AS_TITLE[a.kind] ? label(TASK_TYPES, a.task_type) : "";
  const what = [type, a.matter_type ? matterLabel(a.matter_type) : "", a.phone_last3 ? `موبايله آخره ${a.phone_last3}` : ""];
  const when = [a.due_at ? `ميعادها ${cairoWhen(a.due_at, now)}` : "", a.staff_name ? `على: ${a.staff_name}` : "مش متعينة لحد"];
  return [what, when].map((line) => line.filter(Boolean).join(" · ")).filter(Boolean);
}

/** Not looking at the panel: another tab, another program, or the window minimised. */
const away = () => document.hidden || !document.hasFocus();

function show(title: string, body: string, tag: string, opts: { stay?: boolean; onClick?: () => void } = {}): boolean {
  try {
    const n = new Notification(title, { body, tag, icon: ICON, lang: "ar", dir: "rtl", requireInteraction: !!opts.stay });
    n.onclick = () => {
      window.focus();
      opts.onClick?.();
      n.close();
    };
    return true;
  } catch {
    return false;
  }
}

/** The bell's «جرّب»: one notification now, so the person sees where they come. */
export function testNotification(): boolean {
  if (notifyState() !== "granted") return false;
  return show("تجربة الإشعارات", "كده التنبيهات الجديدة هتوصلك هنا، حتى وإنت في تاب أو برنامج تاني.", "hazem-test");
}

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
 * Watches the bell's alerts. The first list only records what is already known. After that:
 * - Each new unread alert, of any kind, becomes a notification while the person is away from the panel.
 *   - The clients' requests come before the system's problems.
 *   - Clicking a task's notification opens the task.
 *   - An urgent request's stays on the screen until it is closed.
 * - An urgent request also chimes.
 * `unread` is the tab title's count: the requests only.
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
    const fresh = items
      .filter((a) => !a.read && !seen.current!.has(a.id))
      .sort((a, b) => Number(a.kind === "system") - Number(b.kind === "system")); // the requests first, then the system's
    for (const a of items) seen.current.add(a.id);
    if (!fresh.length) return;
    if (fresh.some((a) => LOUD.has(a.kind))) chime();
    if (!away() || notifyState() !== "granted") return;
    for (const a of fresh.slice(0, AT_ONCE)) {
      const task = a.task_id;
      show(rtl(alertTitle(a)), alertDetails(a).map(rtl).join("\n"), `hazem-alert-${a.id}`, {
        stay: a.kind === "task_urgent",
        onClick: task ? () => open(task) : undefined,
      });
    }
    const more = fresh.length - AT_ONCE;
    if (more > 0) show(more === 1 ? "تنبيه جديد تاني" : `${more} تنبيهات جديدة تانية`, "افتح اللوحة وشوفهم في الجرس.", "hazem-alert-more");
  }, [items, open]);
}
