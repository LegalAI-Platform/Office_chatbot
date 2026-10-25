import { QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { Me } from "../shared/api/types";
import { makeQueryClient, routes } from "./App";

// TASK 10 (DECISIONS v1.14): «طلبات الخدمات». Only the lawyer and the manager price (b); the file goes from the
// browser straight to storage with the link the API signed, and the API only hears where it is (a).
const me = (role: "staff" | "lawyer"): Me => ({
  id: "u1", username: "u", display_name: "x", email: "u@example.test", role,
  permissions: ["tasks.view_own", "tasks.update", "account.self", "services.manage", ...(role === "lawyer" ? ["services.price" as const] : [])],
  must_change_password: false, two_factor: false, two_factor_required: false,
});
const order = (over: object = {}) => ({
  id: "o1", kind: "document_review", state: "awaiting_price", name: "محمد أحمد علي حسن", wa_id: "201000000093",
  created_at: "2026-11-19T10:00:00Z", files: 2, price: null, price_exception: false, due_on: null, delivery: null, price_until: null,
  payment: null, file_name: null, delivered_at: null, closed_reason: null, appointment_id: null, notice: null, ...over,
});

function serve(table: Record<string, unknown>) {
  const calls: { method: string; path: string; body: unknown }[] = [];
  vi.spyOn(globalThis, "fetch").mockImplementation(async (url, init) => {
    const full = String(url).replace("/api/panel", "");
    const path = full.split("?")[0]!;
    const method = init?.method ?? "GET";
    calls.push({ method, path: full, body: init?.body instanceof Blob ? "file" : init?.body ? JSON.parse(String(init.body)) : undefined });
    if (path === "/auth/csrf") return Response.json({ token: "t" });
    const body = table[`${method} ${full}`] ?? table[`${method} ${path}`];
    if (body !== undefined) return Response.json(body);
    if (method !== "GET") return new Response(null, { status: method === "PUT" ? 200 : 204 });
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

describe("TASK 10 service orders", () => {
  it("the staff see the client's documents but no price form", async () => {
    serve({ "GET /auth/me": me("staff"), "GET /services?show=open": [order()] });
    renderAt("/services");
    const card = (await screen.findByText(/محمد أحمد علي حسن/)).closest("li")!;
    expect(within(card).getByRole("link", { name: /مستند 2/ }).getAttribute("href")).toBe("/api/panel/services/o1/files/1");
    expect(within(card).queryByRole("button", { name: "ابعت السعر للعميل" })).toBeNull();
  });

  it("the lawyer prices: outside the range only as an exception", async () => {
    const calls = serve({ "GET /auth/me": me("lawyer"), "GET /services?show=open": [order()] });
    renderAt("/services");
    const card = (await screen.findByText(/محمد أحمد علي حسن/)).closest("li")!;
    fireEvent.change(within(card).getByLabelText("السعر (جنيه)"), { target: { value: "5000" } });
    const send = within(card).getByRole("button", { name: "ابعت السعر للعميل" }) as HTMLButtonElement;
    expect(send.disabled).toBe(true);
    fireEvent.click(within(card).getByLabelText(/ده استثناء/));
    fireEvent.change(within(card).getByLabelText("يوم التسليم"), { target: { value: "2026-11-25" } });
    fireEvent.click(send);
    fireEvent.click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "ابعت السعر" }));
    await vi.waitFor(() => expect(calls.some((c) => c.method === "POST")).toBe(true));
    expect(calls.find((c) => c.method === "POST")).toMatchObject({
      path: "/services/o1/price", body: { price: 5000, exception: true, due_on: "2026-11-25", delivery: "file" },
    });
  });

  it("a delivered file that did not reach the client is said, and can be sent again", async () => {
    const calls = serve({
      "GET /auth/me": me("staff"),
      "GET /services?show=open": [],
      "GET /services?show=done": [order({ state: "delivered", delivery: "file", file_name: "مراجعة.pdf", delivered_at: "2026-11-20T10:00:00Z", notice: "failed" })],
    });
    renderAt("/services");
    fireEvent.click(await screen.findByRole("tab", { name: "اتسلّمت أو اتقفلت" }));
    const card = (await screen.findByText(/محمد أحمد علي حسن/)).closest("li")!;
    expect(within(card).getByText(/آخر رسالة ما وصلتش للعميل/)).toBeTruthy();
    fireEvent.click(within(card).getByRole("button", { name: /ابعت الملف تاني/ }));
    fireEvent.click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "ابعته" }));
    await vi.waitFor(() => expect(calls.some((c) => c.path === "/services/o1/resend")).toBe(true));
  });

  it("the file goes to storage with the signed link, then the API is told where it is", async () => {
    const url = "/storage/office-sensitive/deliverables/o1/x.pdf?X-Amz-Signature=s";
    const calls = serve({
      "GET /auth/me": me("staff"),
      "GET /services?show=open": [order({ state: "in_progress", price: 2500, due_on: "2026-11-25", delivery: "file", payment: "verified" })],
      "POST /services/o1/upload": { url, key: "deliverables/o1/x.pdf", content_type: "application/pdf" },
    });
    renderAt("/services");
    const card = (await screen.findByText(/محمد أحمد علي حسن/)).closest("li")!;
    fireEvent.change(within(card).getByLabelText("الملف"), { target: { files: [new File(["%PDF"], "مراجعة.pdf", { type: "application/pdf" })] } });
    fireEvent.click(within(card).getByRole("button", { name: "ابعت الملف للعميل" }));
    fireEvent.click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "ابعت الملف" }));
    await vi.waitFor(() => expect(calls.some((c) => c.path === "/services/o1/deliver")).toBe(true));
    expect(calls.filter((c) => c.method !== "GET").map((c) => `${c.method} ${c.path}`)).toEqual([
      "POST /services/o1/upload", `PUT ${url}`, "POST /services/o1/deliver",
    ]);
    expect(calls.find((c) => c.path === "/services/o1/deliver")).toMatchObject({ body: { key: "deliverables/o1/x.pdf", filename: "مراجعة.pdf" } });
  });
});
