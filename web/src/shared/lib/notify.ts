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
import { ALERT_KINDS, BOOKING_SERVICES, label, matterLabel, SERVICE_KINDS, SYSTEM_WHAT, TASK_TYPES } from "./labels";
import { cairoWhen } from "./time";
import { countWords, MESSAGES, MORE_ALERTS, NEW_THINGS } from "./group";

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

/** v1.111: what a receipt pays for: a written service, or an appointment's service («استشارة هاتفيًا أو بالفيديو»). */
export const receiptFor = (code?: string): string =>
  !code ? "" : code in SERVICE_KINDS ? label(SERVICE_KINDS, code)
    : code.startsWith("consultation_") ? `استشارة ${label(BOOKING_SERVICES, code)}` : label(BOOKING_SERVICES, code);

/** The page a click on an alert opens: its task, or «الإيصالات» for a receipt's (v1.111); none for the system's. */
export const alertTarget = (a: PanelAlert): string | undefined =>
  a.task_id ? `/tasks/${a.task_id}` : a.kind === "receipt_review" ? "/receipts" : undefined;

/** v1.112: «3 رسائل» when a task holds more than one of the client's messages (his later ones join it); else nothing. */
export const messagesOf = (a: PanelAlert): string => (a.messages && a.messages > 1 ? countWords(a.messages, MESSAGES) : "");

/**
 * The line under an alert's title, as the bell shows it: the system's problem, the receipt's, or the task's kind and client
 * (v1.112: how many messages first, when his later ones joined the task: the line is cut at its end).
 */
export const alertLine = (a: PanelAlert): string =>
  a.kind === "system"
    ? label(SYSTEM_WHAT, a.system_what) || "افتح اللوحة لعرض التنبيه."
    : a.kind === "receipt_review"
      ? [receiptFor(a.receipt_for), a.client_name ?? "رقم بلا اسم"].filter(Boolean).join(" · ")
      : [messagesOf(a), label(TASK_TYPES, a.task_type ?? ""), a.client_name ?? "رقم بلا اسم"].filter(Boolean).join(" · ");

/** The task kind each alert's title already names. */
const SAME_AS_TITLE: Record<string, string> = { task_urgent: "urgent_request", task_human: "human_requested", task_rating: "rating_low" };

/** A line that reads right to left in Windows' own notification, whatever the computer's language. */
const rtl = (s: string) => `‏${s}‏`;

/** The notification's title: the kind of alert, and the client for a task. */
export const alertTitle = (a: PanelAlert): string =>
  a.kind === "system" ? label(ALERT_KINDS, a.kind) : `${label(ALERT_KINDS, a.kind)} · ${a.client_name ?? "رقم بلا اسم"}`;

/**
 * Under the title (v1.104, the owner: «ما فيهاش تفاصيل»): what it is about and the number's last 3 digits, then when and
 * who has it. Never the client's words (the owner: «من غير كلامه»). A system alert says what failed.
 */
export function alertDetails(a: PanelAlert, now: Date = new Date()): string[] {
  if (a.kind === "system") return [alertLine(a)];
  if (a.kind === "receipt_review") {
    // v1.111: what it pays for, the amount due and the number's end; then where to confirm it
    const what = [receiptFor(a.receipt_for), a.amount != null ? `المبلغ المطلوب: ${a.amount.toLocaleString("en-US")} جنيه` : "",
      a.phone_last3 ? `رقمه ينتهي بـ ${a.phone_last3}` : ""];
    return [what.filter(Boolean).join(" · "), "يمكن تأكيده من صفحة «الإيصالات»"].filter(Boolean);
  }
  // the task's kind, unless the title already says it (an urgent request's, a request for someone's, a low rating's)
  const type = a.task_type && a.task_type !== SAME_AS_TITLE[a.kind] ? label(TASK_TYPES, a.task_type) : "";
  const what = [messagesOf(a), type, a.matter_type ? matterLabel(a.matter_type) : "", a.phone_last3 ? `رقمه ينتهي بـ ${a.phone_last3}` : ""];
  const when = [a.due_at ? `موعدها ${cairoWhen(a.due_at, now)}` : "", a.staff_name ? `مُسندة إلى: ${a.staff_name}` : "غير مُسندة لأحد"];
  return [what, when].map((line) => line.filter(Boolean).join(" · ")).filter(Boolean);
}

/** One Windows notification: its title, its lines, its tag, whether it stays, and the page a click opens. */
export interface Note { title: string; body: string; tag: string; stay: boolean; to?: string }

// which of a task's alerts speaks for it: the urgent one, then a client asking for someone, then the late, then the new
// (v1.111: a receipt waiting, with the new ones)
const RANK: Record<string, number> = { task_urgent: 0, task_human: 1, task_rating: 2, task_overdue: 3, task_new: 4, receipt_review: 4 };

/** What one alert of a client's several says in their notification: the task's kind, or «a receipt waiting». */
const whatOf = (a: PanelAlert) => (a.kind === "receipt_review" ? label(ALERT_KINDS, a.kind) : label(TASK_TYPES, a.task_type ?? ""));
const rank = (a: PanelAlert) => RANK[a.kind] ?? 5;

/**
 * fix/alert-flood (the owner: «لو عميل جه وحجز بتلاقي فيه مليون حاجه»): the new alerts as few notifications as can say them.
 * - A task's alerts are one notification (its «new» and its «late» together, say): the most pressing one speaks.
 * - A client with several tasks at once (a booking: the receipt, the message that did not go…) is one notification that
 *   names them, and opens the first.
 * - The system's problems stay one each, after the clients'.
 */
export function groupAlerts(fresh: PanelAlert[]): Note[] {
  const byTask = new Map<string, PanelAlert>();
  const system: PanelAlert[] = [];
  for (const a of fresh) {
    if (a.kind === "system") { system.push(a); continue; }
    // v1.111: a receipt's alert has no task: it is its own, and joins its client's like a task's
    const key = a.task_id ?? `alert-${a.id}`;
    const had = byTask.get(key);
    if (!had || rank(a) < rank(had)) byTask.set(key, a);
  }
  const byClient = new Map<string, PanelAlert[]>();
  for (const a of [...byTask.values()].sort((x, y) => rank(x) - rank(y))) {
    const key = `${a.client_name ?? ""}|${a.phone_last3 ?? a.task_id ?? a.id}`;
    byClient.set(key, [...(byClient.get(key) ?? []), a]);
  }
  const notes: Note[] = [...byClient.values()].map((list) => {
    const first = list[0]!;
    if (list.length === 1) {
      // v1.112: a task's notification has its task's tag: told again when his next message joins it, it takes the old one's
      // place in Windows instead of a second one beside it
      const tag = first.task_id ? `hazem-task-${first.task_id}` : `hazem-alert-${first.id}`;
      return { title: rtl(alertTitle(first)), body: alertDetails(first).map(rtl).join("\n"), tag, stay: first.kind === "task_urgent", to: alertTarget(first) };
    }
    const who = first.client_name ?? "رقم بلا اسم";
    const what = [...new Set(list.map(whatOf).filter(Boolean))];
    return {
      title: rtl(`${countWords(list.length, NEW_THINGS)} · ${who}`),
      body: [...what.map((w) => `• ${w}`), first.phone_last3 ? `رقمه ينتهي بـ ${first.phone_last3}` : ""].filter(Boolean).map(rtl).join("\n"),
      tag: `hazem-alert-${first.id}`,
      stay: list.some((a) => a.kind === "task_urgent"),
      to: alertTarget(first),
    };
  });
  return [...notes, ...system.map((a) => ({ title: rtl(alertTitle(a)), body: alertDetails(a).map(rtl).join("\n"), tag: `hazem-alert-${a.id}`, stay: false }))];
}

/** Not looking at the panel: another tab, another program, or the window minimised. */
const away = () => document.hidden || !document.hasFocus();

function show(title: string, body: string, tag: string, opts: { stay?: boolean; onClick?: () => void } = {}): boolean {
  try {
    // v1.112: renotify, so one that takes an older one's place (the same task's tag) is still heard and shown
    const options = { body, tag, icon: ICON, lang: "ar", dir: "rtl", requireInteraction: !!opts.stay, renotify: true } as NotificationOptions;
    const n = new Notification(title, options);
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
  return show("تجربة الإشعارات", "بهذا ستصلك التنبيهات الجديدة هنا، حتى وأنت في تبويب أو برنامج آخر.", "hazem-test");
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
 *   - Clicking a task's notification opens the task; a receipt's, «الإيصالات» (v1.111): `open` gets the page.
 *   - An urgent request's stays on the screen until it is closed.
 * - An urgent request also chimes.
 * `unread` is the tab title's count: the requests only.
 */
export function useAlertNotifications(items: PanelAlert[] | undefined, unread: number, open: (to: string) => void) {
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
    const notes = groupAlerts(fresh);
    for (const n of notes.slice(0, AT_ONCE)) {
      const to = n.to;
      show(n.title, n.body, n.tag, { stay: n.stay, onClick: to ? () => open(to) : undefined });
    }
    const more = notes.length - AT_ONCE;
    if (more > 0) show(countWords(more, MORE_ALERTS), "افتح اللوحة لمراجعتها في قائمة التنبيهات.", "hazem-alert-more");
  }, [items, open]);
}
