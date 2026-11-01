import { QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { Me } from "../shared/api/types";
import { makeQueryClient, routes } from "./App";

// TASK 06 (DECISIONS v1.17, B-23): «الأسعار». Everyone sees the list; the lawyer and the manager change it, with a confirmation
// that says the new price is for new requests only, and only the changed lines go to the API (a line someone else changed
// meanwhile is never put back).
const me = (role: "staff" | "lawyer"): Me => ({
  id: "u1", username: "u", display_name: "x", email: "u@example.test", role,
  permissions: ["tasks.view_own", "tasks.update", "account.self", ...(role === "lawyer" ? ["prices.manage" as const] : [])],
  must_change_password: false, two_factor: false, two_factor_required: false, avatar_at: null,
});
const list = [
  { key: "price.phone_video", name: "استشارة بالتليفون أو الفيديو (ربع ساعة)", value: 500, default: 500 },
  { key: "price.office", name: "مقابلة في المكتب (نص ساعة)", value: 1000, default: 1000 },
];

function serve(table: Record<string, unknown>) {
  const calls: { method: string; path: string; body: unknown }[] = [];
  vi.spyOn(globalThis, "fetch").mockImplementation(async (url, init) => {
    const path = String(url).replace("/api/panel", "").split("?")[0]!;
    const method = init?.method ?? "GET";
    calls.push({ method, path, body: init?.body ? JSON.parse(String(init.body)) : undefined });
    if (path === "/auth/csrf") return Response.json({ token: "t" });
    if (method !== "GET") return new Response(null, { status: 204 });
    const body = table[`${method} ${path}`];
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

describe("TASK 06 prices", () => {
  it("the staff see the list without changing it", async () => {
    serve({ "GET /auth/me": me("staff"), "GET /prices": list });
    renderAt("/prices");
    expect(await screen.findByText("استشارة بالتليفون أو الفيديو (ربع ساعة)")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "احفظ الأسعار" })).toBeNull();
    expect(screen.queryByRole("spinbutton")).toBeNull();
  });

  it("the lawyer changes a price after confirming, and only that line is saved", async () => {
    const calls = serve({ "GET /auth/me": me("lawyer"), "GET /prices": list, "GET /prices/changes": [] });
    renderAt("/prices");
    fireEvent.change(await screen.findByLabelText("استشارة بالتليفون أو الفيديو (ربع ساعة)"), { target: { value: "600" } });
    fireEvent.click(screen.getByRole("button", { name: "احفظ الأسعار" }));
    const dialog = await screen.findByRole("alertdialog");
    expect(dialog.textContent).toContain("للطلبات الجديدة بس");
    fireEvent.click(within(dialog).getByRole("button", { name: "احفظ الأسعار" }));
    await vi.waitFor(() => expect(calls.some((c) => c.method === "PUT")).toBe(true));
    expect(await screen.findByText("الأسعار اتحفظت ✅")).toBeTruthy(); // the toast
    expect(calls.find((c) => c.method === "PUT")).toEqual({ method: "PUT", path: "/prices", body: { values: { "price.phone_video": 600 } } });
  });

  it("an emptied price or a review range upside down is never saved", async () => {
    const review = [
      { key: "price.review_min", name: "مراجعة عقد أو مستندات: من", value: 1500, default: 1500 },
      { key: "price.review_max", name: "مراجعة عقد أو مستندات: لحد", value: 3000, default: 3000 },
    ];
    serve({ "GET /auth/me": me("lawyer"), "GET /prices": review, "GET /prices/changes": [] });
    renderAt("/prices");
    const save = () => screen.getByRole("button", { name: "احفظ الأسعار" }) as HTMLButtonElement;
    fireEvent.change(await screen.findByLabelText("مراجعة عقد أو مستندات: من"), { target: { value: "" } });
    expect(save().disabled).toBe(true);
    expect(screen.getByText(/رقم صحيح من 1/)).toBeTruthy();
    fireEvent.change(screen.getByLabelText("مراجعة عقد أو مستندات: من"), { target: { value: "4000" } });
    expect(save().disabled).toBe(true);
    expect(screen.getByText(/أقل سعر للمراجعة/)).toBeTruthy();
    fireEvent.change(screen.getByLabelText("مراجعة عقد أو مستندات: من"), { target: { value: "2000" } });
    expect(save().disabled).toBe(false);
  });

  it("the last changes that did not load say so, with a retry", async () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    serve({ "GET /auth/me": me("lawyer"), "GET /prices": list });
    renderAt("/prices");
    expect(await screen.findByText(/آخر التغييرات ما اتحمّلتش/, undefined, { timeout: 5000 })).toBeTruthy();
    expect(screen.getByRole("button", { name: /جرّب تاني/ })).toBeTruthy();
  });
});
