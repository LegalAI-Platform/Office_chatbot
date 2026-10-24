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
  two_factor_required: false,
};
const settings = { enabled: true, buffer_minutes: 10, min_notice_hours: 2, horizon_days: 14, max_per_day: null, slot_step_minutes: 15, offer_count: 4, calendar_id: "lawyer@example.test", calendar_key: true, texts_approved: true };
const day = (d: string, over: object = {}) => ({ day: d, status: "open", hours: [{ start: 600, end: 1440 }], bookings: 0, exceptions: [], ...over });
const month = Array.from({ length: 30 }, (_, i) => day(`2026-11-${String(i + 1).padStart(2, "0")}`, i === 19 ? { bookings: 1 } : {}));
const appointment = { id: "a1", service: "consultation_phone_video", start: "2026-11-20T10:00:00Z", end: "2026-11-20T10:15:00Z", state: "confirmed", name: "محمد أحمد علي حسن", wa_id: "201000000091", matter_type: null, reschedules: 0, calendar: "synced" };

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
  cleanup();
  vi.restoreAllMocks();
});

describe("TASK 08 booking", () => {
  it("closing a day with a booking asks first, then sends confirm", async () => {
    const calls = serve({
      "GET /auth/me": { body: staff },
      "POST /booking/exceptions": [
        { status: 409, body: { error: "has_bookings", appointments: [appointment] } },
        { body: { cancelled: 1, not_told: 0 } },
      ],
    });
    renderAt("/appointments");
    fireEvent.click(await screen.findByRole("gridcell", { name: /2026-11-20/ }));
    fireEvent.click(screen.getByRole("button", { name: "احفظ" }));
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
    renderAt("/appointments");
    fireEvent.click(await screen.findByRole("tab", { name: "المواعيد الثابتة" }));
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

  it("the office's payment details are not shown to a staff member", async () => {
    serve({ "GET /auth/me": { body: staff } });
    renderAt("/appointments");
    expect(await screen.findByRole("tab", { name: "إعدادات الحجز" })).toBeTruthy();
    expect(screen.queryByRole("tab", { name: "بيانات المكتب" })).toBeNull();
  });
});
