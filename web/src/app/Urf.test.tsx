import { QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { Me } from "../shared/api/types";
import { makeQueryClient, routes } from "./App";

// TASK 11 (DECISIONS v1.15): «حالات العرفي». Every role sees the cases and their documents; only the lawyer and the
// manager approve or refuse one before the appointment.
const me = (role: "staff" | "lawyer"): Me => ({
  id: "u1", username: "u", display_name: "x", email: "u@example.test", role,
  permissions: ["tasks.view_own", "tasks.update", "account.self", "urf.manage", ...(role === "lawyer" ? ["urf.review" as const] : [])],
  must_change_password: false, two_factor: false, two_factor_required: false,
});
const urfCase = (over: object = {}) => ({
  id: "c1", kind: "marriage", state: "booked", name: "محمد أحمد علي حسن", wa_id: "201000000094", created_at: "2026-11-19T10:00:00Z",
  husband_age: 30, wife_age: 25, wife_past: "khul", guardian: "brother", witnesses: "office", has_copy: null, agreed: null, children: null,
  documents: "بطاقة الزوج، وبطاقة الزوجة، وبطاقة الولي (الأخ)، وحكم الخلع", files: 2, appointment_id: "a1", start: "2026-11-20T10:00:00Z",
  appointment: "confirmed", payment: "proof_received", decided_by: null, decided_at: null, notice: null, ...over,
});

function serve(table: Record<string, unknown>) {
  const calls: { method: string; path: string; body: unknown }[] = [];
  vi.spyOn(globalThis, "fetch").mockImplementation(async (url, init) => {
    const full = String(url).replace("/api/panel", "");
    const path = full.split("?")[0]!;
    const method = init?.method ?? "GET";
    calls.push({ method, path: full, body: init?.body ? JSON.parse(String(init.body)) : undefined });
    if (path === "/auth/csrf") return Response.json({ token: "t" });
    if (method === "POST") return new Response(null, { status: 204 });
    const body = table[`${method} ${full}`] ?? table[`${method} ${path}`];
    if (body !== undefined) return Response.json(body);
    return path === "/dashboard" || path === "/alerts" ? Response.json({ unread: 0, items: [], overdue: 0 }) : Response.json({ error: "not_found" }, { status: 404 });
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

describe("TASK 11 Urfi cases", () => {
  it("the staff see the answers and the documents, but not the review", async () => {
    serve({ "GET /auth/me": me("staff"), "GET /urf?show=open": [urfCase()] });
    renderAt("/urf");
    const card = (await screen.findByText(/محمد أحمد علي حسن/)).closest("li")!;
    expect(within(card).getByText("الأخ")).toBeTruthy();
    expect(within(card).getByText("اختلعت قبل كده")).toBeTruthy();
    expect(within(card).getByRole("link", { name: /مستند 2/ }).getAttribute("href")).toBe("/api/panel/urf/c1/files/1");
    expect(within(card).queryByRole("button", { name: /موافق/ })).toBeNull();
  });

  it("the lawyer refuses after confirming: the appointment is cancelled and paid back", async () => {
    const calls = serve({ "GET /auth/me": me("lawyer"), "GET /urf?show=open": [urfCase()] });
    renderAt("/urf");
    const card = (await screen.findByText(/محمد أحمد علي حسن/)).closest("li")!;
    fireEvent.click(within(card).getByRole("button", { name: /رفض/ }));
    fireEvent.click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "ارفض" }));
    await vi.waitFor(() => expect(calls.some((c) => c.method === "POST")).toBe(true));
    expect(calls.find((c) => c.method === "POST")).toMatchObject({ path: "/urf/c1/review", body: { decision: "rejected" } });
  });
});
