// v1.104: every new alert is a Windows notification while the panel is not in front, and the bell lets a person allow
// them and try one (the owner, 2026-11-12).
import { QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, renderHook, screen, within } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Me, PanelAlert } from "../shared/api/types";
import { useAlertNotifications } from "../shared/lib/notify";
import { makeQueryClient, routes } from "./App";

// the browser's Notification: what Windows would show (the words without the right-to-left marks, `raw` with them)
const RLM = /‏/g;
const made: { title: string; raw: string; body?: string; icon?: string; tag?: string; stay?: boolean; click: () => void }[] = [];
class FakeNotification {
  static permission: NotificationPermission = "granted";
  static requestPermission = vi.fn(async () => FakeNotification.permission);
  onclick: (() => void) | null = null;
  constructor(title: string, o: NotificationOptions = {}) {
    made.push({ title: title.replace(RLM, ""), raw: title, body: o.body?.replace(RLM, ""), icon: o.icon, tag: o.tag,
      stay: o.requireInteraction, click: () => this.onclick?.() });
  }
  close() {}
}

beforeEach(() => {
  made.length = 0;
  FakeNotification.permission = "granted";
  FakeNotification.requestPermission = vi.fn(async () => FakeNotification.permission);
  vi.stubGlobal("Notification", FakeNotification);
  vi.spyOn(window, "focus").mockImplementation(() => {}); // a click on a notification brings the panel forward (jsdom has none)
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

const alert = (over: Partial<PanelAlert>): PanelAlert => ({
  id: 1, kind: "task_overdue", task_id: "t1", task_type: "complaint", client_name: "سارة", created_at: "2026-11-12T10:00:00Z", read: false, ...over,
});

/** The bell's watcher with a first list; `next` is the list a minute later. */
function watch(first: PanelAlert[]) {
  const open = vi.fn();
  const hook = renderHook(({ items }: { items: PanelAlert[] }) => useAlertNotifications(items, 0, open), { initialProps: { items: first } });
  return { open, next: (items: PanelAlert[]) => hook.rerender({ items }) };
}

const away = () => vi.spyOn(document, "hasFocus").mockReturnValue(false);

describe("v1.104 Windows notifications", () => {
  it("a new alert becomes a notification with the office's seal and its details (never the client's words), and opens its task", () => {
    away();
    const old = alert({ id: 1 });
    const { open, next } = watch([old]);
    expect(made).toHaveLength(0); // what was already there says nothing

    const due = new Date(Date.now() + 3_600_000).toISOString();
    next([alert({ id: 2, task_id: "t2", client_name: "دوت نت", phone_last3: "471", matter_type: "personal_status", staff_name: "منى", due_at: due }), old]);
    expect(made).toHaveLength(1);
    const n = made[0]!;
    // v1.112: a task's notification has its task's tag (told again, it takes the old one's place)
    expect(n).toEqual(expect.objectContaining({ title: "مهمة متأخرة · دوت نت", icon: "/landing/img/logo.png", tag: "hazem-task-t2", stay: false }));
    const [what, when] = n.body!.split("\n");
    expect(what).toBe("شكوى · أحوال شخصية · رقمه ينتهي بـ 471");
    expect(when).toMatch(/^موعدها (اليوم|غدًا)\s\d{1,2}:\d{2}\s[صم] · مُسندة إلى: منى$/);
    expect(n.raw.startsWith("‏") && n.raw.endsWith("‏")).toBe(true); // right to left on an English Windows too
    n.click();
    expect(open).toHaveBeenCalledWith("/tasks/t2");
  });

  it("each kind's details: the title's own kind is not said twice; a system's says what failed; an urgent request stays", () => {
    away();
    const { next } = watch([]);
    next([
      alert({ id: 2, kind: "task_human", task_id: "t2", task_type: "human_requested", client_name: undefined }),
      alert({ id: 3, kind: "task_rating", task_id: "t3", task_type: "rating_low" }),
      alert({ id: 4, kind: "system", task_id: undefined, task_type: undefined, client_name: undefined, system_what: "healthcheck" }),
    ]);
    expect(made.map((m) => [m.title, m.body])).toEqual([
      ["عميل يطلب التحدث مع موظف · رقم بلا اسم", "غير مُسندة لأحد"],
      ["تقييم ضعيف من عميل · سارة", "غير مُسندة لأحد"],
      ["مشكلة في النظام", "اكتشف الفحص اليومي خدمة متوقفة"],
    ]);
    made.length = 0;
    next([alert({ id: 5, kind: "task_urgent", task_type: "safety_alert", staff_name: "منى" })]);
    expect(made).toEqual([expect.objectContaining({ title: "طلب عاجل · سارة", body: "تنبيه أمان\nمُسندة إلى: منى", stay: true })]);
  });

  // v1.105 (the owner: «اجابته لازم موظف يتدخل ليه فيها … الحاجات دي انا عايزها تيجي في النوتيفيكيشن»)
  it("any other task Hazem opened for the office: «عميل محتاج موظف», and the line under it says why", () => {
    away();
    const { next } = watch([]);
    next([alert({ id: 6, kind: "task_new", task_type: "existing_client_message", client_name: "محمد عماد", phone_last3: "589" })]);
    expect(made).toEqual([expect.objectContaining({
      title: "عميل بحاجة إلى متابعة من المكتب · محمد عماد", body: "موكّل حالي يستفسر عن قضيته · رقمه ينتهي بـ 589\nغير مُسندة لأحد", stay: false,
    })]);
  });

  it("nothing while the person looks at the panel, before it is allowed, for a read alert, or on a phone", () => {
    const focus = vi.spyOn(document, "hasFocus").mockReturnValue(true);
    const { next } = watch([]);
    next([alert({ id: 2 })]);
    expect(made).toHaveLength(0); // in front of the panel: the bell is enough

    focus.mockReturnValue(false);
    FakeNotification.permission = "default";
    next([alert({ id: 3 }), alert({ id: 2 })]);
    expect(made).toHaveLength(0); // not allowed yet

    FakeNotification.permission = "granted";
    next([alert({ id: 4, read: true }), alert({ id: 3 }), alert({ id: 2 })]);
    expect(made).toHaveLength(0); // already read elsewhere

    vi.spyOn(navigator, "userAgent", "get").mockReturnValue("Mozilla/5.0 (Linux; Android 14) Chrome/130 Mobile");
    next([alert({ id: 5 }), alert({ id: 4, read: true }), alert({ id: 3 }), alert({ id: 2 })]);
    expect(made).toHaveLength(0); // a phone's browser shows them only from a service worker
  });

  it("many at once: three, then one that says how many more", () => {
    away();
    const { next } = watch([]);
    // five clients' tasks (one client's alerts would be one notification: see «one booking, one notification»)
    const of = (id: number) => alert({ id, task_id: `t${id}`, client_name: `عميل ${id}`, phone_last3: String(100 + id) });
    next([2, 3, 4, 5, 6].map(of));
    expect(made.map((m) => m.title)).toEqual(["مهمة متأخرة · عميل 2", "مهمة متأخرة · عميل 3", "مهمة متأخرة · عميل 4", "تنبيهان جديدان آخران"]);
    made.length = 0;
    next([2, 3, 4, 5, 6, 7, 8, 9, 10].map(of));
    expect(made.map((m) => m.title).at(-1)).toBe("تنبيه جديد آخر");
  });

  // fix/alert-flood (the owner: «لو عميل جه وحجز بتلاقي فيه مليون حاجه»)
  it("one task is one notification: its «new» and its «late» together, the more pressing one speaks", () => {
    away();
    const { next } = watch([]);
    next([alert({ id: 20, kind: "task_new", task_type: "payment_review_due" }), alert({ id: 21, kind: "task_overdue", task_type: "payment_review_due" })]);
    expect(made.map((m) => m.title)).toEqual(["مهمة متأخرة · سارة"]);
  });

  it("one booking, one notification: a client's several tasks at once are named in one, and a click opens the first", () => {
    away();
    const { next, open } = watch([]);
    next([
      alert({ id: 30, kind: "task_new", task_id: "a", task_type: "payment_review_due", phone_last3: "019" }),
      alert({ id: 31, kind: "task_new", task_id: "b", task_type: "booking_message_failed", phone_last3: "019" }),
      alert({ id: 32, kind: "task_urgent", task_id: "c", task_type: "urgent_call_failed", phone_last3: "019" }),
      alert({ id: 33, kind: "task_new", task_id: "d", task_type: "complaint", client_name: "منى", phone_last3: "027" }),
    ]);
    expect(made).toHaveLength(2);
    const [booking, other] = made;
    expect(booking!.title).toBe("3 عناصر جديدة · سارة");
    expect(booking!.body).toContain("رقمه ينتهي بـ 019");
    expect(booking!.stay).toBe(true); // one of them is urgent
    expect(other!.title).toContain("منى");
    booking!.click();
    expect(open).toHaveBeenCalledWith("/tasks/c"); // the urgent one first
  });

  // v1.111 (the owner: «الاشعارات اللي فوق … عاوزك تضيف فيها لو عميل بعت صوره الايصال وعاوز تأكيد الدفع»)
  const receipt = (over: Partial<PanelAlert>) =>
    alert({ kind: "receipt_review", task_id: undefined, task_type: undefined, receipt_for: "consultation_phone_video", amount: 1500, ...over });

  it("a receipt waiting for its review: what it pays for, the amount, the number's end, and a click opens «الإيصالات»", () => {
    away();
    const { next, open } = watch([]);
    next([receipt({ id: 40, client_name: "محمد أحمد", phone_last3: "092" })]);
    expect(made).toEqual([expect.objectContaining({
      title: "إيصال تحويل بانتظار التأكيد · محمد أحمد",
      body: "استشارة هاتفيًا أو بالفيديو · المبلغ المطلوب: 1,500 جنيه · رقمه ينتهي بـ 092\nيمكن تأكيده من صفحة «الإيصالات»",
      stay: false,
    })]);
    made[0]!.click();
    expect(open).toHaveBeenCalledWith("/receipts");
  });

  it("a receipt for a written service or an Urfi contract says which; with the client's task at once, one notification names both", () => {
    away();
    const { next } = watch([]);
    next([receipt({ id: 41, receipt_for: "document_review", amount: 3000, phone_last3: "019" })]);
    expect(made[0]!.body).toContain("مراجعة عقد · المبلغ المطلوب: 3,000 جنيه");
    made.length = 0;
    next([receipt({ id: 43, receipt_for: "urf_marriage_own", phone_last3: "019" }),
      alert({ id: 44, kind: "task_new", task_id: "e", task_type: "booking_message_failed", phone_last3: "019" })]);
    expect(made).toHaveLength(1);
    expect(made[0]!.title).toBe("عنصران جديدان · سارة");
    expect(made[0]!.body).toContain("• إيصال تحويل بانتظار التأكيد");
  });

  // v1.112 (the owner: «ليه في كل النوتيفيكيشنز دي؟ … ملعبكة»): a client's later messages join his open task of the same kind
  it("his next message on the same task is told again in the same notification's place, with how many messages", () => {
    away();
    const { next } = watch([]);
    next([alert({ id: 60, kind: "task_new", task_id: "q", task_type: "service_question", client_name: "أحمد", phone_last3: "589" })]);
    next([alert({ id: 61, kind: "task_new", task_id: "q", task_type: "service_question", client_name: "أحمد", phone_last3: "589", messages: 3 })]);
    expect(made.map((m) => m.tag)).toEqual(["hazem-task-q", "hazem-task-q"]); // one place in Windows
    expect(made[1]!.body!.split("\n")[0]).toBe("3 رسائل · استفسار عميل عن خدمة (هل نقدّمها، أو تفاصيلها): رُدّ عليه · رقمه ينتهي بـ 589");
    expect(made[0]!.body).not.toContain("رسال"); // one message: no count
  });
});

// ---------- the bell ----------

const person: Me = {
  id: "u1", username: "u", display_name: "شخص", email: "u@example.test", role: "staff",
  permissions: ["tasks.view_own", "tasks.update", "account.self"], must_change_password: false, two_factor: false,
  two_factor_required: false, avatar_at: null, tour_seen: true,
};
const emptyDash = { urgent: 0, overdue: 0, unassigned: 0, active: 0, urgent_by_day: [], overdue_by_day: [], recent: [], priority: [] };

let alertsBody: unknown = { unread: 0, items: [] };
let alertsAsked = 0;
let dashAsked = 0;
let bookingAsked = 0;

function openBell(bellName = "التنبيهات", body: unknown = { unread: 0, items: [] }) {
  alertsBody = body;
  alertsAsked = 0;
  dashAsked = 0;
  bookingAsked = 0;
  vi.spyOn(globalThis, "fetch").mockImplementation(async (url) => {
    const path = String(url).replace("/api/panel", "").split("?")[0]!;
    if (path === "/auth/csrf") return Response.json({ token: "t" });
    if (path === "/auth/me") return Response.json(person);
    if (path === "/dashboard") {
      dashAsked++;
      return Response.json(emptyDash);
    }
    if (path === "/booking/appointments") {
      bookingAsked++; // the home's today and next
      return Response.json([]);
    }
    if (path === "/alerts") {
      alertsAsked++;
      return Response.json(alertsBody);
    }
    return Response.json({ error: "not_found" }, { status: 404 });
  });
  const router = createMemoryRouter(routes, { initialEntries: ["/"] });
  render(<QueryClientProvider client={makeQueryClient()}><RouterProvider router={router} /></QueryClientProvider>);
  return (async () => {
    fireEvent.click(await screen.findByRole("button", { name: bellName }));
    return screen.getByRole("dialog", { name: "التنبيهات" });
  })();
}

// the API's stream as the browser opens it
class FakeEventSource {
  static all: FakeEventSource[] = [];
  closed = false;
  private listeners: Record<string, ((e: { data?: string }) => void)[]> = {};
  constructor(public url: string) {
    FakeEventSource.all.push(this);
  }
  addEventListener(type: string, fn: (e: { data?: string }) => void) {
    (this.listeners[type] ??= []).push(fn);
  }
  close() {
    this.closed = true;
  }
  emit(type: string, data?: string) {
    for (const fn of this.listeners[type] ?? []) fn({ data });
  }
}

describe("v1.104 the bell's Windows notifications", () => {
  it("asks once; once allowed it shows a test at once, and «جرّب إشعار الويندوز» shows another", async () => {
    FakeNotification.permission = "default";
    FakeNotification.requestPermission = vi.fn(async () => (FakeNotification.permission = "granted"));
    const bell = await openBell();
    fireEvent.click(within(bell).getByRole("button", { name: "فعّل إشعارات ويندوز لتصلك التنبيهات وأنت في تبويب آخر" }));
    const again = await within(bell).findByRole("button", { name: "جرّب إشعار ويندوز" });
    expect(FakeNotification.requestPermission).toHaveBeenCalledTimes(1);
    expect(made.map((m) => [m.title, m.icon])).toEqual([["تجربة الإشعارات", "/landing/img/logo.png"]]);
    fireEvent.click(again);
    expect(made).toHaveLength(2);
  });

  it("blocked in the browser: says where to allow them", async () => {
    FakeNotification.permission = "denied";
    const bell = await openBell();
    expect(within(bell).getByText(/إشعارات ويندوز معطّلة لهذه اللوحة/)).toBeTruthy();
    expect(within(bell).queryByRole("button", { name: /إشعار/ })).toBeNull();
  });

  it("a phone: neither the button nor the hint", async () => {
    vi.spyOn(navigator, "userAgent", "get").mockReturnValue("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X)");
    const bell = await openBell();
    expect(within(bell).queryByText(/إشعار/)).toBeNull();
  });
});

describe("v1.104 the clients' requests first, the system's problems below", () => {
  const sys = (id: number, what: string, read = false) =>
    alert({ id, kind: "system", task_id: undefined, task_type: undefined, client_name: undefined, system_what: what as PanelAlert["system_what"], read });

  it("the red count is the requests'; the system's problems are folded under them with their own count", async () => {
    const bell = await openBell("تنبيه جديد", {
      unread: 3, unread_system: 2,
      items: [sys(9, "healthcheck"), sys(8, "log_scan"), alert({ id: 7, kind: "task_urgent", task_type: "urgent_request", client_name: "أحمد" }), alert({ id: 6, read: true })],
    });
    const requests = within(bell).getByRole("list", { name: "الطلبات" });
    expect(within(requests).getAllByRole("button").map((b) => b.textContent)).toEqual([
      expect.stringContaining("طلب عاجل"), expect.stringContaining("مهمة متأخرة"),
    ]);
    const folded = within(bell).getByText("مشكلات النظام").closest("details")!;
    expect(folded.open).toBe(false); // the requests come first
    expect(within(folded).getByText("2 جديدة")).toBeTruthy();
    expect(within(within(folded).getByRole("list", { name: "مشكلات النظام" })).getAllByRole("button")).toHaveLength(2);
  });

  it("only system problems: no red count, a gold dot in the bell, and the list open", async () => {
    const bell = await openBell("التنبيهات، وتوجد مشكلات جديدة في النظام", { unread: 1, unread_system: 1, items: [sys(9, "backup")] });
    expect(within(bell).getByText("لا توجد طلبات.")).toBeTruthy();
    expect(within(bell).getByText("مشكلات النظام").closest("details")!.open).toBe(true);
  });

  it("at once over the API's stream: a new alert asks the bell's list again, with no minute's wait", async () => {
    FakeEventSource.all = [];
    vi.stubGlobal("EventSource", FakeEventSource);
    await openBell();
    const stream = FakeEventSource.all.at(-1)!;
    expect(stream.url).toBe("/api/panel/alerts/stream");
    const before = alertsAsked;
    stream.emit("alerts");
    await vi.waitFor(() => expect(alertsAsked).toBe(before + 1));
    cleanup();
    expect(stream.closed).toBe(true); // the panel closed: the stream too
  });

  it("several at once: the requests' notifications before the system's, and a system one does not stay on the screen", () => {
    away();
    const { next } = watch([]);
    next([sys(10, "backup"), alert({ id: 11, kind: "task_urgent", task_type: "urgent_request" }), sys(12, "n8n")]);
    expect(made.map((m) => [m.title, m.stay])).toEqual([["طلب عاجل · سارة", true], ["مشكلة في النظام", false], ["مشكلة في النظام", false]]);
  });
});

// v1.106 (the owner: «الداش بورد كلها ريل تايم ؟» … «عايزين كله يشتغل بنفس الطريقه دي»)
describe("v1.106 every page at once", () => {
  it("the stream's «data» asks again only what its areas show, each once, with no minute's wait", async () => {
    FakeEventSource.all = [];
    vi.stubGlobal("EventSource", FakeEventSource);
    await openBell();
    const stream = FakeEventSource.all.at(-1)!;
    await vi.waitFor(() => expect([dashAsked, bookingAsked]).toEqual([1, 2])); // the home: its numbers, today and next
    const bell = alertsAsked;

    stream.emit("data", "lists"); // an office list: not on the home
    await new Promise((r) => setTimeout(r, 50));
    expect([dashAsked, bookingAsked]).toEqual([1, 2]);

    stream.emit("data", "clients,tasks"); // a new client with a new task: the home's numbers
    await vi.waitFor(() => expect(dashAsked).toBe(2));
    // v1.111: the bell too: a finished task's alert is read for everyone, and the bell says so at once
    await vi.waitFor(() => expect(alertsAsked).toBe(bell + 1));

    stream.emit("data", "appointments,payments"); // both show the appointments: asked once each, not twice
    await vi.waitFor(() => expect(bookingAsked).toBe(4));
    await new Promise((r) => setTimeout(r, 50));
    expect([dashAsked, bookingAsked]).toEqual([2, 4]);
    expect(alertsAsked).toBe(bell + 2); // v1.111: a reviewed receipt's alert is read for everyone too
  });

  it("v1.111: a receipt waiting is in the bell with what it pays for and its client", async () => {
    const bell = await openBell("تنبيه جديد", {
      unread: 1,
      items: [alert({ id: 50, kind: "receipt_review", task_id: undefined, task_type: undefined, client_name: "محمد", receipt_for: "consultation_office", amount: 1000 })],
    });
    const row = within(within(bell).getByRole("list", { name: "الطلبات" })).getByRole("button");
    expect(row.textContent).toContain("إيصال تحويل بانتظار التأكيد");
    expect(row.textContent).toContain("استشارة مقابلة في المكتب · محمد");
  });

  it("v1.112: a task holding several of the client's messages says how many in its line", async () => {
    const bell = await openBell("تنبيه جديد", {
      unread: 1,
      items: [alert({ id: 70, kind: "task_new", task_type: "attached_message", client_name: "أحمد", messages: 2 })],
    });
    const row = within(within(bell).getByRole("list", { name: "الطلبات" })).getByRole("button");
    expect(row.textContent).toContain("رسالتان · رسالة كتبها العميل مع صورة أو اختيار: اقرأها · أحمد");
  });
});
