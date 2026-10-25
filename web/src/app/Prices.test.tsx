import { QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { Me } from "../shared/api/types";
import { makeQueryClient, routes } from "./App";

// TASK 06 (DECISIONS v1.17, B-23): «الأسعار». Everyone sees the list; the lawyer and the manager change it, with a confirmation
// that says the new price is for new requests only, and the whole list goes to the API.
const me = (role: "staff" | "lawyer"): Me => ({
  id: "u1", username: "u", display_name: "x", email: "u@example.test", role,
  permissions: ["tasks.view_own", "tasks.update", "account.self", ...(role === "lawyer" ? ["prices.manage" as const] : [])],
  must_change_password: false, two_factor: false, two_factor_required: false,
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

  it("the lawyer changes a price after confirming, and the whole list is saved", async () => {
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(true);
    const calls = serve({ "GET /auth/me": me("lawyer"), "GET /prices": list, "GET /prices/changes": [] });
    renderAt("/prices");
    fireEvent.change(await screen.findByLabelText("استشارة بالتليفون أو الفيديو (ربع ساعة)"), { target: { value: "600" } });
    fireEvent.click(screen.getByRole("button", { name: "احفظ الأسعار" }));
    await vi.waitFor(() => expect(calls.some((c) => c.method === "PUT")).toBe(true));
    expect(confirm.mock.calls[0]![0]).toContain("للطلبات الجديدة بس");
    expect(calls.find((c) => c.method === "PUT")).toMatchObject({ path: "/prices", body: { values: { "price.phone_video": 600, "price.office": 1000 } } });
  });
});
