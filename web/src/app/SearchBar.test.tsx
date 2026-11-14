import { QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { createMemoryRouter, RouterProvider, useLocation } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { SearchResults } from "../shared/api/types";
import { makeQueryClient } from "./App";
import { SearchBar, searchGroups } from "./SearchBar";

// v1.110 (the owner: «السيرش بار هنا حاسه بيسرش غلط» … «اعمل بحث شامل»)
const results: SearchResults = {
  clients: [{ id: "p1", name: "أحمد مصطفى", phone_last3: "713", open_tasks: 2, task_id: "t1", appointment_at: null }],
  appointments: [{ id: "a1", name: "أحمد مصطفى", phone_last3: "713", service: "consultation_office", state: "confirmed", starts_at: "2026-11-20T10:00:00Z" }],
  tasks: [{ id: "t1", name: "أحمد مصطفى", phone_last3: "713", type: "complaint", state: "open", due_at: "2026-11-14T12:00:00Z" }],
};

function Where() {
  const l = useLocation();
  return <output data-testid="where">{l.pathname + l.search}</output>;
}

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("the search bar", () => {
  it("lists the clients, the appointments and the tasks, each opening where the panel shows it", () => {
    const groups = searchGroups(results, new Date("2026-11-14T10:00:00Z"));
    expect(groups.map((g) => g.title)).toEqual(["العملاء", "المواعيد", "المهام"]);
    expect(groups[0]!.hits[0]).toMatchObject({ to: "/tasks/t1", title: "أحمد مصطفى", line: "رقمه ينتهي بـ 713 · مهمتان مفتوحتان" });
    expect(groups[1]!.hits[0]!.to).toBe("/appointments?day=2026-11-20");
    expect(groups[2]!.hits[0]).toMatchObject({ to: "/tasks/t1", title: "شكوى" });
    // a client with no task opens on his appointment's day; nothing found, no group
    expect(searchGroups({ ...results, clients: [{ ...results.clients[0]!, task_id: null, appointment_at: "2026-11-20T10:00:00Z" }] })[0]!.hits[0]!.to)
      .toBe("/appointments?day=2026-11-20");
    expect(searchGroups({ clients: [], appointments: [], tasks: [] })).toEqual([]);
  });

  it("asks as it is typed, with the term in the body and never in the address, and Enter opens the first one", async () => {
    const calls: { url: string; body?: string }[] = [];
    vi.spyOn(globalThis, "fetch").mockImplementation(async (url, init) => {
      const path = String(url);
      calls.push({ url: path, body: typeof init?.body === "string" ? init.body : undefined });
      if (path.endsWith("/auth/csrf")) return Response.json({ token: "t" });
      if (path.endsWith("/auth/me")) {
        return Response.json({ id: "u1", username: "yasmin", display_name: "ياسمين", email: "yasmin@example.test", role: "staff",
          permissions: ["tasks.view_own", "tasks.update", "account.self"], must_change_password: false, two_factor: false,
          two_factor_required: false, avatar_at: null, tour_seen: true });
      }
      if (path.endsWith("/search")) return Response.json(results);
      return Response.json({ error: "not_found" }, { status: 404 });
    });
    const router = createMemoryRouter([{ path: "*", element: <><SearchBar /><Where /></> }], { initialEntries: ["/"] });
    render(<QueryClientProvider client={makeQueryClient()}><RouterProvider router={router} /></QueryClientProvider>);

    const input = screen.getByRole("searchbox", { name: "بحث" });
    fireEvent.change(input, { target: { value: "احمد ٧١٣" } });
    expect(await screen.findByRole("region", { name: "نتائج البحث" })).toBeTruthy();
    expect(await screen.findByText("رقمه ينتهي بـ 713 · مهمتان مفتوحتان")).toBeTruthy();
    const search = calls.find((c) => c.url.endsWith("/search"))!;
    expect(search.url).not.toContain("٧١٣");
    expect(JSON.parse(search.body!)).toEqual({ q: "احمد ٧١٣" });

    fireEvent.submit(input.closest("form")!);
    expect(screen.getByTestId("where").textContent).toBe("/tasks/t1");
    expect(screen.queryByRole("region", { name: "نتائج البحث" })).toBeNull();
  });
});
