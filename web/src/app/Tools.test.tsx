import { QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { Me } from "../shared/api/types";
import { makeQueryClient, routes } from "./App";

const person = (over: Partial<Me>): Me => ({
  id: "u1", username: "u", display_name: "شخص", email: "u@example.test", role: "staff",
  permissions: ["tasks.view_own", "tasks.update", "account.self"], must_change_password: false, two_factor: false,
  two_factor_required: false, avatar_at: null, ...over,
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

  // Ctrl K (idea from PR #14): the pages a person may open, and a search in the tasks by what is typed.
  it("Ctrl K opens the quick search: only allowed pages, Enter goes, and a typed name searches the tasks", async () => {
    serve({ "GET /auth/me": { body: person({}) }, "GET /tasks": { body: { items: [], total: 0, page: 1, page_size: 20 } } });
    const router = renderAt("/");
    await screen.findByRole("heading", { level: 1, name: /يا/ });
    fireEvent.keyDown(document, { key: "k", ctrlKey: true });
    const dialog = await screen.findByRole("dialog", { name: "اختصارات وبحث" });
    expect(within(dialog).queryByText("التقارير")).toBeNull(); // a staff member has no reports.view
    const box = within(dialog).getByRole("textbox", { name: "دوّر أو اختار" });
    fireEvent.change(box, { target: { value: "مهام" } });
    fireEvent.keyDown(box, { key: "ArrowDown" }); // past «دوّر في المهام عن…» to the first page found
    fireEvent.keyDown(box, { key: "Enter" });
    expect(router.state.location.pathname).toBe("/tasks");
    fireEvent.keyDown(document, { key: "k", ctrlKey: true });
    const again = within(await screen.findByRole("dialog", { name: "اختصارات وبحث" })).getByRole("textbox", { name: "دوّر أو اختار" });
    fireEvent.change(again, { target: { value: "سارة" } });
    fireEvent.keyDown(again, { key: "Enter" });
    expect(router.state.location.search).toBe(`?q=${encodeURIComponent("سارة")}`);
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
      "GET /reports": { body: { month: "2026-10", tasks: 4, done: 2, done_on_time: 1, late: 1, by_type: [{ key: "urgent_request", count: 2 }], by_staff: [], new_clients: 3, by_source: [{ key: "ad", count: 3 }], by_matter: [], by_client_type: [], by_booking: [], received: 0, missing: 0, refunded: 0,
        ratings: { sent: 3, answered: 2, average: 3.5, by_service: [{ key: "consultation_office", count: 1, average: 5 }, { key: "document_review", count: 1, average: 2 }],
          low: [{ at: "2026-10-12T09:00:00Z", service: "document_review", client_name: "منى سعيد" }] },
        review: [{ at: "2026-10-31T10:00:00Z", client: "ابو عماد", reason: "repeated" },
          { at: "2026-10-30T10:00:00Z", client: null, reason: "offer_after_payment" }],
        free_answers: [{ purpose: "free_answer", at: "2026-10-31T11:00:00Z", client: "ابو عماد", text: "متقلقش يا فندم 🙏 كل كلامك مع المكتب سري.", guard: "passed" },
          { purpose: "services_tell", at: "2026-10-31T10:30:00Z", client: null, text: null, guard: "review_promise" }],
        written: { total: 8, fallbacks: 2, retried: 1, by_reason: [{ key: "review_promise", count: 1 }, { key: "review_unavailable_timeout", count: 1 }] } } },
    });
    renderAt("/reports");
    expect(await screen.findByRole("heading", { name: "التقارير" })).toBeTruthy();
    expect(await screen.findAllByText("50%")).toHaveLength(2); // done of all, and on time of done
    // v1.31 (B-26): «تقييمات العملاء»: the average, sent and answered, by service, and the low ones with the client's name
    const ratings = screen.getByRole("region", { name: "تقييمات العملاء" });
    expect(within(ratings).getByText("3.5")).toBeTruthy();
    expect(within(ratings).getByText("67%")).toBeTruthy();
    expect(within(ratings).getByText("مقابلة في المكتب")).toBeTruthy();
    expect(within(ratings).getByText("منى سعيد")).toBeTruthy();
    expect(within(ratings).getAllByText("مراجعة عقد")).toHaveLength(2);
    // v1.39 (TASK 16, 3.4): «محادثات محتاجة مراجعة», the alarm first
    const review = screen.getByRole("region", { name: "محادثات محتاجة مراجعة" });
    const rows = within(review).getAllByRole("row").slice(1);
    expect(rows[0]!.textContent).toContain("عرض مدفوع لعميل دفع");
    expect(rows[0]!.textContent).toContain("رقم من غير اسم");
    expect(rows[1]!.textContent).toContain("ابو عماد");
    expect(rows[1]!.textContent).toContain("الرد اتكرر");
    // v1.53 (B-34, layer 6): «ردود حازم الحرة», what went and what was refused and why
    const free = screen.getByRole("region", { name: "ردود حازم الحرة" });
    const said = within(free).getAllByRole("row").slice(1);
    expect(said[0]!.textContent).toContain("متقلقش يا فندم");
    expect(said[1]!.textContent).toContain("اترفض: وعد المكتب ما يقدرش يضمنه");
    const summary = within(free).getByTestId("written-summary").textContent ?? "";
    expect(summary).toContain("كتب حازم 8 رد");
    expect(summary).toContain("(25%)"); // 2 of 8 went back to the approved words
    expect(summary).toContain("المراجِع اتأخر (1)");
  });

  it("v1.58: the AI cost by conversation, and by message when a row is opened", async () => {
    const calls = serve({
      "GET /auth/me": { body: lawyer },
      "GET /reports": { body: { month: "2026-11", tasks: 0, done: 0, done_on_time: 0, late: 0, by_type: [], by_staff: [], new_clients: 0, by_source: [], by_matter: [], by_client_type: [], by_booking: [], received: 0, missing: 0, refunded: 0,
        ratings: { sent: 0, answered: 0, by_service: [], low: [] }, ai_cost: { total_usd: 0.42, calls: 30, conversations: 2, input_tokens: 400000, output_tokens: 6000, unpriced: 0 } } },
      "GET /reports/ai-cost": { body: { month: "2026-11", summary: { total_usd: 0.42, calls: 30, conversations: 2, input_tokens: 400000, output_tokens: 6000, unpriced: 0 },
        conversations: [{ person_id: "p1", client: "ابو عماد", turns: 8, calls: 20, input_tokens: 300000, output_tokens: 4000, cost_usd: 0.31, last_at: "2026-11-03T20:00:00Z", unpriced: 0 },
          { person_id: "p2", client: null, turns: 3, calls: 10, input_tokens: 100000, output_tokens: 2000, cost_usd: 0.11, last_at: "2026-11-03T18:00:00Z", unpriced: 1 }] } },
      "GET /reports/ai-cost/p1": { body: { turns: [{ at: "2026-11-03T19:00:00Z", cost_usd: 0.0123, input_tokens: 14500, output_tokens: 210,
        calls: [{ kind: "classify", model: "claude-opus-5-5", input_tokens: 500, cache_read_tokens: 14000, cache_write_tokens: 0, output_tokens: 190, cost_usd: 0.0088 },
          { kind: "write", model: "claude-haiku-5-5", input_tokens: 4400, cache_read_tokens: 0, cache_write_tokens: 0, output_tokens: 20, cost_usd: 0.0005 }] }] } },
    });
    renderAt("/reports");
    const section = await screen.findByRole("region", { name: "تكلفة المحادثات" });
    expect(await within(section).findByText("$0.420")).toBeTruthy(); // the month's spend (once it loaded)
    expect(within(section).getByText("$0.210 للمحادثة")).toBeTruthy();
    const rows = within(section).getAllByRole("row").slice(1);
    expect(rows[0]!.textContent).toContain("ابو عماد");
    expect(rows[0]!.textContent).toContain("$0.310");
    expect(rows[1]!.textContent).toContain("رقم من غير اسم");
    expect(rows[1]!.textContent).toContain("+"); // a call with no known price
    fireEvent.click(within(section).getByRole("button", { name: "ابو عماد" }));
    const turn = await within(section).findByLabelText("رسايل المحادثة");
    expect(turn.textContent).toContain("فهم الرسالة");
    expect(turn.textContent).toContain("كتابة الرد");
    expect(turn.textContent).toContain("$0.0123");
    expect(calls.some((c) => c.path === "/reports/ai-cost/p1")).toBe(true);
  });

  it("shows a month with no rating yet (the API leaves the average out) without breaking", async () => {
    serve({
      "GET /auth/me": { body: lawyer },
      "GET /reports": { body: { month: "2026-11", tasks: 0, done: 0, done_on_time: 0, late: 0, by_type: [], by_staff: [], new_clients: 0, by_source: [], by_matter: [], by_client_type: [], by_booking: [], received: 0, missing: 0, refunded: 0,
        ratings: { sent: 0, answered: 0, by_service: [], low: [] } } },
    });
    renderAt("/reports");
    const ratings = await screen.findByRole("region", { name: "تقييمات العملاء" });
    expect(within(ratings).getByText("—")).toBeTruthy();
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

  it("asks before removing a number from a list", async () => {
    const calls = serve({
      "GET /auth/me": { body: lawyer },
      "GET /lists": { body: { total: 1, items: [{ id: "e1", kind: "existing_client", wa_id: "201000000001", name: "منى", imported_at: "2026-10-20T10:00:00Z" }] } },
      "DELETE /lists/e1": { status: 204 },
    });
    renderAt("/lists");
    fireEvent.click(await screen.findByRole("button", { name: "شيل منى من القايمة" }));
    const dialog = await screen.findByRole("alertdialog");
    expect(dialog.textContent).toContain("تشيل منى");
    fireEvent.click(within(dialog).getByRole("button", { name: "لأ، ارجع" }));
    await vi.waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    expect(calls.some((c) => c.method === "DELETE")).toBe(false);
    fireEvent.click(screen.getByRole("button", { name: "شيل منى من القايمة" }));
    fireEvent.click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "أيوه، شيله" }));
    await vi.waitFor(() => expect(calls.some((c) => c.method === "DELETE" && c.path === "/lists/e1")).toBe(true));
  });
});
