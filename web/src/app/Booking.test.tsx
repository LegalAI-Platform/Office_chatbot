import { QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { Me } from "../shared/api/types";
import { makeQueryClient, routes } from "./App";

// TASK 08 (DECISIONS v1.12, B-24): the «المواعيد» screen.
const staff: Me = {
  id: "u1", username: "u", display_name: "ياسمين", email: "u@example.test", role: "staff",
  permissions: ["tasks.view_own", "tasks.update", "account.self", "schedule.manage"], must_change_password: false, two_factor: false,
  two_factor_required: false, avatar_at: null,
};
const manager: Me = { ...staff, id: "u2", display_name: "حازم", role: "manager", permissions: [...staff.permissions, "office.manage"] };
const settings = { enabled: true, buffer_minutes: 10, min_notice_hours: 2, horizon_days: 14, max_per_day: null, slot_step_minutes: 15, offer_count: 4, calendar_id: "lawyer@example.test", calendar_key: true, texts_approved: true, hold_minutes: 120, hold_short_minutes: 60, hold_short_within_hours: 6, hold_gap_minutes: 15 };
const day = (d: string, over: object = {}) => ({ day: d, status: "open", hours: [{ start: 600, end: 1440 }], bookings: 0, exceptions: [], ...over });
const month = Array.from({ length: 30 }, (_, i) => day(`2026-11-${String(i + 1).padStart(2, "0")}`, i === 19 ? { bookings: 1 } : {}));
const appointment = { id: "a1", service: "consultation_phone_video", start: "2026-11-20T10:00:00Z", end: "2026-11-20T10:15:00Z", state: "confirmed", name: "محمد أحمد علي حسن", wa_id: "201000000091", matter_type: null, reschedules: 0, calendar: "synced", hold_until: null, payment: "proof_received", attending: false, reminders: [], call_app: "facetime" };

type Answer = { status?: number; body?: unknown };

function serve(table: Record<string, Answer | Answer[]>) {
  const calls: { method: string; path: string; body: unknown }[] = [];
  vi.spyOn(globalThis, "fetch").mockImplementation(async (url, init) => {
    const path = String(url).replace("/api/panel", "").split("?")[0]!;
    const method = init?.method ?? "GET";
    calls.push({ method, path, body: init?.body ? JSON.parse(String(init.body)) : undefined });
    if (path === "/auth/csrf") return Response.json({ token: "t" });
    const entry = table[`${method} ${path}`];
    const a = (Array.isArray(entry) ? entry.shift() : entry)
      ?? (path === "/booking/settings" ? { body: settings } : path === "/booking/changes" ? { body: [] } : path === "/booking/month" ? { body: month }
        : path === "/booking/preview" ? { body: { calendar: "ok", slots: [] } } : path === "/dashboard" || path === "/alerts" ? { body: { unread: 0, items: [], overdue: 0 } }
          : { status: 404, body: { error: "not_found" } });
    const status = a.status ?? 200;
    return status === 204 || a.body === undefined ? new Response(null, { status }) : Response.json(a.body, { status });
  });
  return calls;
}

function renderAt(path: string) {
  render(<QueryClientProvider client={makeQueryClient()}><RouterProvider router={createMemoryRouter(routes, { initialEntries: [path] })} /></QueryClientProvider>);
}

afterEach(() => {
  vi.useRealTimers();
  cleanup();
  vi.restoreAllMocks();
});

describe("TASK 08 booking", () => {
  it("closing a day with a booking asks first, then sends confirm", async () => {
    // the panel's clock on a day before the 20th: the calendar opens on November and the 20th can still be changed
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-11-05T10:00:00Z"));
    const calls = serve({
      "GET /auth/me": { body: staff },
      "POST /booking/exceptions": [
        { status: 409, body: { error: "has_bookings", appointments: [appointment] } },
        { body: { cancelled: 1, not_told: 0 } },
      ],
    });
    renderAt("/appointments");
    fireEvent.click(await screen.findByRole("gridcell", { name: /2026-11-20/ }));
    fireEvent.click(await screen.findByRole("button", { name: "تعديل اليوم ده" }));
    const dialog = await screen.findByRole("dialog", { name: /تعديل/ });
    fireEvent.click(within(dialog).getByRole("button", { name: "احفظ" }));
    const ask = await screen.findByRole("alertdialog", { name: "فيه حجوزات" });
    expect(within(ask).getByText(/محمد أحمد علي حسن/)).toBeTruthy();
    fireEvent.click(within(ask).getByRole("button", { name: "كمّل وبلّغ العملاء" }));
    expect(await screen.findByText(/اتلغى 1 حجز/)).toBeTruthy();
    const posts = calls.filter((c) => c.method === "POST" && c.path === "/booking/exceptions").map((c) => c.body as { confirm: boolean; kind: string; day: string });
    expect(posts.map((b) => [b.day, b.kind, b.confirm])).toEqual([["2026-11-20", "closed_day", false], ["2026-11-20", "closed_day", true]]);
  });

  it("the default week: a day off and two ranges are saved as minutes", async () => {
    const week = [0, 1, 2, 3, 4, 5, 6].map((w) => ({ weekday: w, ranges: [{ start: 600, end: 1440 }] }));
    const calls = serve({ "GET /auth/me": { body: staff }, "GET /booking/week": { body: week }, "PUT /booking/week": { body: { cancelled: 0, not_told: 0 } } });
    renderAt("/appointments/settings");
    expect((await screen.findByRole("tab", { name: "ساعات الشغل" })).getAttribute("aria-selected")).toBe("true");
    const friday = (await screen.findByText("الجمعة")).closest("li")!;
    fireEvent.click(within(friday).getByRole("button", { name: "شيل الفترة" }));
    expect(within(friday).getByText("إجازة")).toBeTruthy();
    fireEvent.change(screen.getByLabelText("نهاية فترة 1 يوم السبت"), { target: { value: "840" } });
    fireEvent.click(screen.getByRole("button", { name: "احفظ الأسبوع" }));
    await vi.waitFor(() => expect(calls.some((c) => c.method === "PUT")).toBe(true));
    const sent = calls.find((c) => c.method === "PUT")!.body as { days: { weekday: number; ranges: { start: number; end: number }[] }[] };
    expect(sent.days.find((d) => d.weekday === 5)!.ranges).toEqual([]);
    expect(sent.days.find((d) => d.weekday === 6)!.ranges).toEqual([{ start: 600, end: 840 }]);
  });

  // TASK 12 (B-18): each appointment's reminders and «هحضر ✅», and tomorrow's that are not confirmed yet.
  it("the bookings show the reminders, who will come, and tomorrow's unconfirmed", async () => {
    const tomorrow = new Date(Date.now() + 86_400_000).toISOString();
    const sent = { kind: "day_before", state: "sent", due_at: tomorrow, sent_at: tomorrow, channel: "template", skip_reason: null, error_code: null };
    const later = { kind: "two_hours", state: "skipped", due_at: null, sent_at: null, channel: null, skip_reason: "too_close", error_code: null };
    serve({
      "GET /auth/me": { body: staff },
      "GET /booking/appointments": { body: [
        { ...appointment, id: "a1", start: tomorrow, attending: true, reminders: [sent, later] },
        { ...appointment, id: "a2", name: "سارة محمد علي حسن", start: tomorrow, attending: false, reminders: [{ ...sent, state: "failed", channel: null }] },
      ] },
    });
    renderAt("/appointments");
    fireEvent.click(await screen.findByRole("button", { name: "كل الجاي" }));
    expect((await screen.findByText(/ما أكدوش حضورهم/)).closest("p")!.textContent).toMatch(/بكرة 2 مواعيد، 1 ما أكدوش حضورهم/);
    const first = screen.getAllByText(/محمد أحمد علي حسن/).map((e) => e.closest("li")).find(Boolean)!; // the row, not «الميعاد الجاي»
    expect(within(first).getByText("أكّد حضوره ✅")).toBeTruthy();
    expect(within(first).getByText(/تذكير اليوم اللي قبله: اتبعت ✓ \(قالب واتساب\)/)).toBeTruthy();
    expect(within(first).getByText(/تذكير قبلها بساعتين: ما اتبعتش: الوقت قريب من الميعاد/)).toBeTruthy();
    const second = screen.getAllByText(/سارة محمد علي حسن/).map((e) => e.closest("li")).find(Boolean)!;
    expect(within(second).queryByText("أكّد حضوره ✅")).toBeNull();
    expect(within(second).getByText(/ما وصلش ✗/)).toBeTruthy();
  });

  // PR #14's ideas: the counts, the next appointment, the search, the kind filter and what to look at on a booking.
  it("the bookings page counts, shows the next one, searches, filters and says what to look at", async () => {
    const soon = new Date(Date.now() + 3 * 3_600_000).toISOString();
    const soonEnd = new Date(Date.now() + 3 * 3_600_000 + 15 * 60_000).toISOString();
    const passed = new Date(Date.now() - 3 * 3_600_000).toISOString();
    const passedEnd = new Date(Date.now() - 3 * 3_600_000 + 30 * 60_000).toISOString();
    serve({
      "GET /auth/me": { body: staff },
      "GET /booking/appointments": { body: [
        { ...appointment, id: "a1", start: soon, end: soonEnd, payment: "proof_received", calendar: "synced" },
        { ...appointment, id: "a2", name: "سارة محمد علي حسن", wa_id: "201000000077", service: "consultation_office", start: passed, end: passedEnd, payment: "verified", calendar: "synced" },
      ] },
    });
    renderAt("/appointments");
    fireEvent.click(await screen.findByRole("button", { name: "كل الجاي" }));
    expect(await screen.findByText("الميعاد الجاي")).toBeTruthy(); // the data is in
    const tiles = screen.getByRole("region", { name: "ملخص الحجوزات" });
    expect(within(tiles).getByText("تحويل محتاج مراجعة").previousElementSibling!.textContent).toBe("1");
    const first = screen.getAllByText(/محمد أحمد علي حسن/).map((e) => e.closest("li")).find(Boolean)!;
    expect(within(first).getByText(/التحويل محتاج مراجعة/)).toBeTruthy();
    expect(within(first).getByText(/بعد 3 ساعات/)).toBeTruthy();
    const second = screen.getAllByText(/سارة محمد علي حسن/).map((e) => e.closest("li")).find(Boolean)!;
    expect(within(second).getByText(/عدّى: علّم «خلص» أو «ماحضرش»/)).toBeTruthy();

    fireEvent.change(screen.getByPlaceholderText("دوّر بالاسم أو الرقم"), { target: { value: "0077" } });
    expect(screen.queryAllByText(/محمد أحمد علي حسن/).filter((e) => e.closest("li"))).toHaveLength(0);
    fireEvent.change(screen.getByPlaceholderText("دوّر بالاسم أو الرقم"), { target: { value: "" } });
    fireEvent.click(screen.getByRole("button", { name: "في المكتب" }));
    expect(screen.queryAllByText(/محمد أحمد علي حسن/).filter((e) => e.closest("li"))).toHaveLength(0);
    expect(screen.getAllByText(/سارة محمد علي حسن/).filter((e) => e.closest("li"))).toHaveLength(1);
  });

  // More of PR #14's ideas: the calendar opens on today with its bookings, «انسخ» next to the number, the week's hours.
  it("the calendar opens on today with its bookings, the number copies, and the week shows its hours", async () => {
    const today = new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Cairo" }).format(new Date());
    const soon = new Date(Date.now() + 60 * 60_000).toISOString();
    const write = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, "clipboard", { value: { writeText: write }, configurable: true });
    const week = [0, 1, 2, 3, 4, 5, 6].map((w) => ({ weekday: w, ranges: w === 5 ? [] : [{ start: 600, end: 1080 }] }));
    serve({
      "GET /auth/me": { body: staff },
      "GET /booking/month": { body: [day(today, { bookings: 1 })] },
      "GET /booking/appointments": { body: [{ ...appointment, start: soon, end: soon }] },
      "GET /booking/week": { body: week },
    });
    renderAt("/appointments");
    const list = await screen.findByRole("region", { name: "حجوزات اليوم" });
    expect(await within(list).findByText(/محمد أحمد علي حسن/)).toBeTruthy();
    fireEvent.click(within(list).getByRole("button", { name: /انسخ الرقم/ }));
    await vi.waitFor(() => expect(write).toHaveBeenCalledWith("+201000000091"));
    expect(await screen.findByText("الرقم اتنسخ")).toBeTruthy();

    fireEvent.click(screen.getByRole("link", { name: "إعدادات المواعيد" }));
    expect((await screen.findByText("في الأسبوع")).textContent).toContain("48 ساعة"); // 6 days × 8 hours
    expect(within((await screen.findByText("السبت")).closest("li")!).getByText("8 ساعات")).toBeTruthy();
  });

  it("the page's view, the bookings' filter and the settings' section live in the address", async () => {
    serve({ "GET /auth/me": { body: staff }, "GET /booking/appointments": { body: [appointment] } });
    renderAt("/appointments?show=all&kind=office");
    expect((await screen.findByRole("button", { name: "كل الجاي" })).getAttribute("aria-pressed")).toBe("true");
    expect((await screen.findByRole("button", { name: "في المكتب" })).getAttribute("aria-pressed")).toBe("true");
    cleanup();
    renderAt("/appointments/settings?section=booking");
    expect((await screen.findByRole("tab", { name: "الحجز من واتساب" })).getAttribute("aria-selected")).toBe("true");
    expect(await screen.findByLabelText("حازم يحجز من واتساب")).toBeTruthy();
  });

  it("the daily screen has no tabs, and the day is changed from «تعديل اليوم ده» in a window", async () => {
    const today = new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Cairo" }).format(new Date());
    serve({ "GET /auth/me": { body: staff }, "GET /booking/month": { body: [day(today)] }, "GET /booking/appointments": { body: [] } });
    renderAt("/appointments");
    expect(await screen.findByRole("grid", { name: "أيام الشهر" })).toBeTruthy();
    expect(screen.queryByRole("tablist")).toBeNull();
    expect(screen.queryByRole("radio", { name: "اقفل اليوم كله" })).toBeNull(); // not on the page itself
    expect(screen.getByRole("link", { name: "إعدادات المواعيد" }).getAttribute("href")).toBe("/appointments/settings");
    fireEvent.click(await screen.findByRole("button", { name: "تعديل اليوم ده" }));
    const dialog = await screen.findByRole("dialog", { name: /تعديل/ });
    expect(within(dialog).getByRole("radio", { name: "اقفل اليوم كله" })).toBeTruthy();
    expect(within(dialog).getByRole("radio", { name: "اقفل جزء منه" })).toBeTruthy();
    expect(within(dialog).getByRole("radio", { name: "ساعات مختلفة لليوم ده" })).toBeTruthy();
    expect(within(dialog).getByLabelText("السبب (للمكتب بس)")).toBeTruthy();
    fireEvent.keyDown(dialog, { key: "Escape" });
    expect(screen.queryByRole("dialog")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "تعديل اليوم ده" }));
    fireEvent.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "اقفل" }));
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("«تراجع» says what the last change was and asks first; «لأ، سيبه» leaves no error behind", async () => {
    const week = [0, 1, 2, 3, 4, 5, 6].map((w) => ({ weekday: w, ranges: [{ start: 600, end: 1440 }] }));
    const calls = serve({
      "GET /auth/me": { body: staff }, "GET /booking/week": { body: week },
      "GET /booking/changes": { body: [{ id: 1, at: "2026-11-01T10:00:00Z", what: "office", by: "حازم", undone: false }] },
      "POST /booking/undo": { status: 403, body: { error: "office_forbidden" } },
      "PUT /booking/week": { status: 409, body: { error: "has_bookings", appointments: [appointment] } },
    });
    renderAt("/appointments/settings");
    expect(await screen.findByText(/آخر تعديل: بيانات المكتب · حازم/)).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "تراجع" }));
    const sure = await screen.findByRole("alertdialog");
    expect(calls.some((c) => c.path === "/booking/undo")).toBe(false); // nothing sent before «أيوه»
    fireEvent.click(within(sure).getByRole("button", { name: "أيوه، رجّعه" }));
    expect(await screen.findByRole("alert")).toBeTruthy();
    expect(calls.filter((c) => c.path === "/booking/undo")).toHaveLength(1);

    // a period ending at midnight offers no «+ فترة» after it
    expect(within((await screen.findByText("السبت")).closest("li")!).queryByRole("button", { name: "+ فترة" })).toBeNull();
    fireEvent.change(screen.getByLabelText("نهاية فترة 1 يوم السبت"), { target: { value: "840" } });
    expect(within(screen.getByText("السبت").closest("li")!).getByRole("button", { name: "+ فترة" })).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "احفظ الأسبوع" }));
    fireEvent.click(within(await screen.findByRole("alertdialog", { name: "فيه حجوزات" })).getByRole("button", { name: "لأ، سيبه" }));
    expect(screen.queryByRole("alertdialog", { name: "فيه حجوزات" })).toBeNull();
    expect(screen.getAllByRole("alert")).toHaveLength(1); // only the undo's, the week's error is gone
  });

  it("a staff member sees the calendar's email but cannot change it", async () => {
    serve({ "GET /auth/me": { body: staff } });
    renderAt("/appointments/settings?section=booking");
    expect((await screen.findByLabelText("إيميل كالندر المستشار (Google)") as HTMLInputElement).readOnly).toBe(true);
    cleanup();
    vi.restoreAllMocks();
    serve({ "GET /auth/me": { body: manager }, "GET /booking/week": { body: [] } });
    renderAt("/appointments/settings?section=booking");
    expect((await screen.findByLabelText("إيميل كالندر المستشار (Google)") as HTMLInputElement).readOnly).toBe(false);
  });

  it("«خلص» and «ماحضرش» only once the appointment started, and each asks first; a past day can't be changed", async () => {
    const today = new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Cairo" }).format(new Date());
    const started = new Date(Date.now() - 5 * 60_000).toISOString();
    const later = new Date(Date.now() + 2 * 3_600_000).toISOString();
    const calls = serve({
      "GET /auth/me": { body: staff },
      "GET /booking/month": { body: [day("2020-01-01"), day(today)] },
      "GET /booking/appointments": { body: [
        { ...appointment, id: "a1", start: started, end: later },
        { ...appointment, id: "a2", name: "سارة محمد علي حسن", start: later, end: later },
      ] },
      "POST /booking/appointments/a1/state": { status: 204 },
    });
    renderAt("/appointments");
    const list = await screen.findByRole("region", { name: "حجوزات اليوم" });
    const now = (await within(list).findByText(/محمد أحمد علي حسن/)).closest("li")!;
    const ahead = within(list).getByText(/سارة محمد علي حسن/).closest("li")!;
    expect(within(ahead).queryByRole("button", { name: /خلص/ })).toBeNull();
    expect(within(ahead).queryByRole("button", { name: /ماحضرش/ })).toBeNull();
    fireEvent.click(within(now).getByRole("button", { name: /خلص/ }));
    const sure = await screen.findByRole("alertdialog");
    expect(calls.some((c) => c.path.endsWith("/state"))).toBe(false);
    fireEvent.click(within(sure).getByRole("button", { name: "أيوه، خلص" }));
    await vi.waitFor(() => expect(calls.find((c) => c.path === "/booking/appointments/a1/state")?.body).toEqual({ state: "completed" }));

    expect(screen.getByRole("button", { name: "تعديل اليوم ده" })).toBeTruthy(); // today
    fireEvent.click(screen.getByRole("gridcell", { name: /2020-01-01/ }));
    expect(await screen.findByRole("heading", { level: 2, name: /يناير/ })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "تعديل اليوم ده" })).toBeNull(); // a past day
  });

  it("the settings page: three sections for a manager, the office's payment details hidden from a staff member", async () => {
    serve({ "GET /auth/me": { body: manager }, "GET /booking/week": { body: [] }, "GET /office": { body: { instapay: "hazem@instapay", wallet_number: "", wallet_provider: "", address: "", map_url: "", meet_link: null, phone: null, lawyer_whatsapp: null, manager_whatsapp: null } } });
    renderAt("/appointments/settings");
    const menu = await screen.findByRole("tablist", { name: "أقسام الإعدادات" });
    expect(within(menu).getAllByRole("tab").map((t) => t.textContent)).toEqual(["ساعات الشغل", "الحجز من واتساب", "بيانات المكتب والدفع"]);
    fireEvent.click(within(menu).getByRole("tab", { name: "بيانات المكتب والدفع" }));
    expect(((await screen.findByLabelText("عنوان InstaPay")) as HTMLInputElement).value).toBe("hazem@instapay");
    expect(screen.getByRole("link", { name: "رجوع للمواعيد" }).getAttribute("href")).toBe("/appointments");
    cleanup();
    vi.restoreAllMocks();

    serve({ "GET /auth/me": { body: staff }, "GET /booking/week": { body: [] } });
    renderAt("/appointments/settings?section=office");
    const staffMenu = await screen.findByRole("tablist", { name: "أقسام الإعدادات" });
    expect(within(staffMenu).getByRole("tab", { name: "الحجز من واتساب" })).toBeTruthy();
    expect(within(staffMenu).queryByRole("tab", { name: "بيانات المكتب والدفع" })).toBeNull();
    expect(within(staffMenu).getByRole("tab", { name: "ساعات الشغل" }).getAttribute("aria-selected")).toBe("true");
  });
});
