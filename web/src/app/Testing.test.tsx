import { QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { Me } from "../shared/api/types";
import { makeQueryClient, routes } from "./App";

// «أدوات التجربة» (the owner, 2026-10-30): Hazem forgets a test number from the panel, the manager and the lawyer only; and
// (v1.34) the switch for the urgent alert inside the 24-hour window.
const person = (over: Partial<Me>): Me => ({
  id: "u1", username: "u", display_name: "شخص", email: "u@example.test", role: "staff",
  permissions: ["tasks.view_own", "tasks.update", "account.self"], must_change_password: false, two_factor: false,
  two_factor_required: false, avatar_at: null, ...over,
});
const lawyer = person({ role: "lawyer", permissions: ["tasks.view_own", "tasks.view_all", "account.self", "testing.forget"] });
const numbers = { numbers: [{ key: "aaaaaaaaaaaaaaaa", masked: "*********589" }, { key: "bbbbbbbbbbbbbbbb", masked: "*********471" }] };
const emptyDash = { urgent: 0, overdue: 0, unassigned: 0, active: 0, urgent_by_day: [], overdue_by_day: [], recent: [], priority: [] };

type Answer = { status?: number; body?: unknown };

function serve(table: Record<string, Answer>) {
  const calls: { method: string; path: string; body: unknown }[] = [];
  vi.spyOn(globalThis, "fetch").mockImplementation(async (url, init) => {
    const path = String(url).replace("/api/panel", "").split("?")[0]!;
    const method = init?.method ?? "GET";
    const body = init?.body ? JSON.parse(String(init.body)) : undefined;
    calls.push({ method, path, body });
    if (path === "/auth/csrf") return Response.json({ token: "t" });
    if (method === "POST" && path === "/testing/forget") {
      const n = numbers.numbers.find((x) => x.key === body.key)!;
      return Response.json({ masked: n.masked, rows: 12, calendar_deleted: 0, calendar_by_hand: [], chatwoot: "ok", chatwoot_conversations: 1 });
    }
    const a = table[`${method} ${path}`] ?? (path === "/dashboard" ? { body: emptyDash } : path === "/alerts" ? { body: { unread: 0, items: [] } } : { status: 404, body: { error: "not_found" } });
    const status = a.status ?? 200;
    return status === 204 || a.body === undefined ? new Response(null, { status }) : Response.json(a.body, { status });
  });
  return calls;
}

function renderAt(path: string) {
  render(<QueryClientProvider client={makeQueryClient()}><RouterProvider router={createMemoryRouter(routes, { initialEntries: [path] })} /></QueryClientProvider>);
}

/** The panel's own «أكيد؟»: its question, then «yes». */
async function confirmYes(): Promise<string> {
  const dialog = await screen.findByRole("alertdialog");
  const text = dialog.textContent ?? "";
  fireEvent.click(within(dialog).getAllByRole("button").at(-1)!);
  return text;
}

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("أدوات التجربة", () => {
  it("the lawyer forgets one test number after asking, then both at once", async () => {
    const calls = serve({ "GET /auth/me": { body: lawyer }, "GET /testing/numbers": { body: numbers } });
    renderAt("/testing");
    const list = await screen.findByRole("list", { name: "أرقام التجربة" });
    expect(within(list).getByText("589")).toBeTruthy(); // the end of the number only
    expect(within(list).getAllByRole("button", { name: "انسى الرقم ده" })).toHaveLength(2);

    fireEvent.click(within(list).getAllByRole("button", { name: "انسى الرقم ده" })[0]!);
    expect(await confirmYes()).toContain("الرقم اللي آخره 589");
    expect(await screen.findByText("حازم نسي الرقم اللي آخره 589 ✅")).toBeTruthy();
    expect(calls.filter((c) => c.path === "/testing/forget").map((c) => c.body)).toEqual([{ key: "aaaaaaaaaaaaaaaa" }]);

    fireEvent.click(screen.getByRole("button", { name: "انسى كل الأرقام (2)" }));
    expect(await confirmYes()).toContain("589، 471");
    await vi.waitFor(() => expect(calls.filter((c) => c.path === "/testing/forget")).toHaveLength(3));
  });

  it("v1.34: the urgent alert inside the 24-hour window is switched on after asking, and says it is for testing", async () => {
    const calls = serve({
      "GET /auth/me": { body: lawyer }, "GET /testing/numbers": { body: numbers },
      "GET /testing/urgent-window": { body: { on: false } }, "PUT /testing/urgent-window": { body: { on: true } },
    });
    renderAt("/testing");
    expect(await screen.findByText("تنبيه العاجل جوه الـ 24 ساعة")).toBeTruthy();
    expect(screen.getByText("مقفول")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "شغّله" }));
    expect(await confirmYes()).toContain("للتجربة بس");
    await vi.waitFor(() => expect(calls.filter((c) => c.method === "PUT").map((c) => c.body)).toEqual([{ on: true }]));
    expect(await screen.findByText("تنبيه العاجل هيوصل رسالة عادية جوه الـ 24 ساعة ✅")).toBeTruthy();
  });

  it("says the tools are off outside the test environment", async () => {
    serve({ "GET /auth/me": { body: lawyer }, "GET /testing/numbers": { status: 404, body: { error: "not_found" } } });
    renderAt("/testing");
    expect(await screen.findByText("الأدوات دي مقفولة")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "انسى الرقم ده" })).toBeNull();
  });

  it("is in the settings for the lawyer, not for the staff", async () => {
    serve({ "GET /auth/me": { body: lawyer } });
    renderAt("/settings");
    expect(await screen.findByText("أدوات التجربة")).toBeTruthy();
    cleanup();
    vi.restoreAllMocks();
    serve({ "GET /auth/me": { body: person({}) } });
    renderAt("/settings");
    expect(await screen.findByText("حسابي")).toBeTruthy();
    expect(screen.queryByText("أدوات التجربة")).toBeNull();
  });
});
