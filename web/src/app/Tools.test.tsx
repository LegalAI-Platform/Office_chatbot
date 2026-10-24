import { QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { Me } from "../shared/api/types";
import { makeQueryClient, routes } from "./App";

const person = (over: Partial<Me>): Me => ({
  id: "u1", username: "u", display_name: "شخص", email: "u@example.test", role: "staff",
  permissions: ["tasks.view_own", "tasks.update", "account.self"], must_change_password: false, two_factor: false,
  two_factor_required: false, ...over,
});
const lawyer = person({ role: "lawyer", permissions: ["tasks.view_own", "tasks.view_all", "tasks.update", "tasks.assign", "account.self", "lists.manage", "texts.manage", "reports.view"] });
const emptyDash = { urgent: 0, overdue: 0, unassigned: 0, active: 0, urgent_by_day: [], overdue_by_day: [], recent: [], priority: [] };

type Answer = { status?: number; body?: unknown };

function serve(table: Record<string, Answer>) {
  const calls: { method: string; path: string; body: unknown }[] = [];
  vi.spyOn(globalThis, "fetch").mockImplementation(async (url, init) => {
    const path = String(url).replace("/api/panel", "").split("?")[0]!;
    const method = init?.method ?? "GET";
    calls.push({ method, path, body: init?.body ? JSON.parse(String(init.body)) : undefined });
    if (path === "/auth/csrf") return Response.json({ token: "t" });
    const a = table[`${method} ${path}`] ?? (path === "/dashboard" ? { body: emptyDash } : path === "/alerts" ? { body: { unread: 0, items: [] } } : { status: 404, body: { error: "not_found" } });
    const status = a.status ?? 200;
    return status === 204 || a.body === undefined ? new Response(null, { status }) : Response.json(a.body, { status });
  });
  return calls;
}

function renderAt(path: string) {
  const router = createMemoryRouter(routes, { initialEntries: [path] });
  render(<QueryClientProvider client={makeQueryClient()}><RouterProvider router={router} /></QueryClientProvider>);
  return router;
}

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("TASK 07c tools", () => {
  it("shows the unread alerts in the bell and opens the task", async () => {
    const calls = serve({
      "GET /auth/me": { body: person({}) },
      "GET /alerts": { body: { unread: 1, items: [{ id: 7, kind: "task_urgent", task_id: "t9", task_type: "urgent_request", client_name: "سارة", due_at: "2026-10-23T10:00:00Z", created_at: "2026-10-23T09:00:00Z", read: false }] } },
      "POST /alerts/read": { status: 204 },
    });
    const router = renderAt("/");
    const bell = await screen.findByRole("button", { name: "1 تنبيهات جديدة" });
    fireEvent.click(bell);
    const dialog = screen.getByRole("dialog", { name: "التنبيهات" });
    fireEvent.click(within(dialog).getByRole("button", { name: /طلب عاجل/ }));
    expect(router.state.location.pathname).toBe("/tasks/t9");
    await vi.waitFor(() => expect(calls.find((c) => c.path === "/alerts/read")?.body).toEqual({ ids: [7] }));
  });

  it("hides the reports from a staff member and shows them to the lawyer", async () => {
    serve({ "GET /auth/me": { body: person({}) } });
    const router = renderAt("/reports");
    await screen.findByRole("heading", { level: 1, name: /يا/ });
    expect(router.state.location.pathname).toBe("/");
    for (const nav of screen.getAllByRole("navigation", { name: "التنقل" })) {
      expect(within(nav).queryByText("التقارير")).toBeNull();
    }
    cleanup();
    vi.restoreAllMocks();
    serve({
      "GET /auth/me": { body: lawyer },
      "GET /reports": { body: { month: "2026-10", tasks: 4, done: 2, done_on_time: 1, late: 1, by_type: [{ key: "urgent_request", count: 2 }], by_staff: [], new_clients: 3, by_source: [{ key: "ad", count: 3 }], by_matter: [], by_client_type: [], by_booking: [], received: 0, missing: 0, refunded: 0 } },
    });
    renderAt("/reports");
    expect(await screen.findByRole("heading", { name: "التقارير" })).toBeTruthy();
    expect(await screen.findAllByText("50%")).toHaveLength(2); // done of all, and on time of done
  });

  it("will not save a text longer than its limit", async () => {
    const calls = serve({
      "GET /auth/me": { body: lawyer },
      "GET /texts": { body: [{ key: "welcome_ask_name", version: 1, text: "أهلًا", max_chars: 10, source: "DECISIONS", updated_at: "2026-10-20T10:00:00Z", versions: 1 }] },
    });
    renderAt("/texts");
    fireEvent.click(await screen.findByRole("button", { name: "عدّل" }));
    fireEvent.change(screen.getByLabelText("نص welcome_ask_name"), { target: { value: "نص طويل أكتر من عشرة" } });
    expect(screen.getByText(/من 10 حرف/).className).toContain("text-bad");
    expect((screen.getByRole("button", { name: "احفظ نسخة جديدة" }) as HTMLButtonElement).disabled).toBe(true);
    expect(calls.some((c) => c.method === "POST" && c.path.startsWith("/texts"))).toBe(false);
  });

  it("links a stored file of the client to the panel's file address", async () => {
    serve({
      "GET /auth/me": { body: person({}) },
      "GET /tasks/t1": { body: { id: "t1", type: "human_requested", state: "open", due_at: "2026-10-23T10:00:00Z", created_at: "2026-10-23T09:00:00Z", client: { phone: "201000000001" }, client_words: [{ type: "text", text: "البطاقة" }, { type: "image", media_status: "stored" }], can: { start: true, done: false, cancel: false, assign: false } } },
    });
    renderAt("/tasks/t1");
    const link = await screen.findByRole("link", { name: /افتح/ });
    expect(link.getAttribute("href")).toBe("/api/panel/tasks/t1/files/1");
    expect(link.getAttribute("rel")).toContain("noopener");
  });
});
