import { QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { Me } from "../shared/api/types";
import { makeQueryClient, routes } from "./App";

// TASK 09 (DECISIONS v1.13): «الإيصالات والمدفوعات». The staff review too (P1) and record a refund (P2).
const staff: Me = {
  id: "u1", username: "u", display_name: "ياسمين", email: "u@example.test", role: "staff",
  permissions: ["tasks.view_own", "tasks.update", "account.self", "schedule.manage", "payments.verify", "payments.refund"],
  must_change_password: false, two_factor: false, two_factor_required: false,
};
const payment = (over: object = {}) => ({
  appointment_id: "a1", service: "consultation_phone_video", start: "2026-11-20T10:00:00Z", appointment: "confirmed", name: "محمد أحمد علي حسن",
  wa_id: "201000000091", state: "proof_received", amount_due: 500, amount_received: null, proof_at: "2026-11-19T10:00:00Z", receipts: 1,
  duplicate: false, reviewed_by: null, reviewed_at: null, refund_due: null, refund_reason: null, refunded_by: null, refunded_at: null, refund_method: null,
  ...over,
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

describe("TASK 09 payments", () => {
  it("a receipt waiting for the review: open it, mark «ناقص» with the amount after confirming", async () => {
    const calls = serve({ "GET /auth/me": staff, "GET /payments?show=review": [payment()] });
    renderAt("/receipts");
    const card = (await screen.findByText(/محمد أحمد علي حسن/)).closest("li")!;
    expect(within(card).getByText("وصل إيصال، مستني المراجعة")).toBeTruthy();
    expect(within(card).getByRole("link", { name: /فتح الإيصال/ }).getAttribute("href")).toBe("/api/panel/payments/a1/receipts/0");

    fireEvent.change(within(card).getByLabelText(/المبلغ اللي وصل/), { target: { value: "300" } });
    expect((within(card).getByRole("button", { name: "تمام" }) as HTMLButtonElement).disabled).toBe(true); // less than due
    fireEvent.click(within(card).getByRole("button", { name: "ناقص" }));
    const sure = within(card).getByRole("alertdialog", { name: "تأكيد" });
    expect(within(sure).getByText(/يكمّل 200 جنيه/)).toBeTruthy();
    fireEvent.click(within(sure).getByRole("button", { name: "أيوه، أكّد" }));
    await vi.waitFor(() => expect(calls.some((c) => c.method === "POST")).toBe(true));
    expect(calls.find((c) => c.method === "POST")).toMatchObject({ path: "/payments/a1/review", body: { state: "partial", received: 300 } });
  });

  it("a refund to make: the person writes how and confirms they transferred it", async () => {
    const calls = serve({
      "GET /auth/me": staff,
      "GET /payments?show=refunds": [payment({ state: "refund_requested", refund_due: 500, refund_reason: "client_within_24h", reviewed_by: "الأستاذ" })],
      "GET /payments?show=review": [],
    });
    renderAt("/receipts");
    fireEvent.click(await screen.findByRole("tab", { name: "استرداد مطلوب" }));
    const card = (await screen.findByText(/استرداد 500 جنيه/)).closest("li")!;
    expect(within(card).getByText(/العميل لغى خلال 24 ساعة/)).toBeTruthy();
    expect((within(card).getByRole("button", { name: "اتحوّل" }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.change(within(card).getByLabelText("حوّلته على إيه"), { target: { value: "فودافون كاش" } });
    fireEvent.click(within(card).getByRole("button", { name: "اتحوّل" }));
    fireEvent.click(within(within(card).getByRole("alertdialog", { name: "تأكيد" })).getByRole("button", { name: "أيوه، أكّد" }));
    await vi.waitFor(() => expect(calls.some((c) => c.method === "POST")).toBe(true));
    expect(calls.find((c) => c.method === "POST")).toMatchObject({ path: "/payments/a1/refunded", body: { method: "فودافون كاش" } });
  });
});
