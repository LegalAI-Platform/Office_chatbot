import { QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { Dashboard, Me, TaskPage } from "../shared/api/types";
import { makeQueryClient, routes } from "./App";

const me = (over: Partial<Me> = {}): Me => ({
  id: "u1",
  username: "yasmin",
  display_name: "ياسمين",
  email: "yasmin@example.test",
  role: "staff",
  permissions: ["tasks.view_own", "tasks.update", "account.self"],
  must_change_password: false,
  two_factor: false,
  two_factor_required: false,
  avatar_at: null,
  ...over,
});

const emptyTasks: TaskPage = { items: [], total: 0, page: 1, page_size: 25 };

const dashboard: Dashboard = {
  urgent: 3,
  overdue: 2,
  unassigned: 1,
  active: 6,
  urgent_by_day: [0, 1, 0, 2, 0, 0, 0],
  overdue_by_day: [0, 0, 1, 0, 1, 0, 0],
  recent: [],
  priority: [
    { id: "t1", type: "urgent_request", state: "open", due_at: "2026-10-23T08:00:00Z", created_at: "2026-10-23T07:00:00Z", client_name: "سارة محمد", late: true },
  ],
};

function serve(user: Me | null) {
  vi.spyOn(globalThis, "fetch").mockImplementation(async (url) => {
    const path = String(url);
    if (path.endsWith("/auth/me")) {
      return user ? Response.json(user) : new Response(null, { status: 401 });
    }
    if (path.includes("/tasks")) return Response.json(emptyTasks);
    if (path.endsWith("/dashboard")) return Response.json(dashboard);
    return new Response(null, { status: 404 });
  });
}

function renderAt(path: string) {
  const router = createMemoryRouter(routes, { initialEntries: [path] });
  render(
    <QueryClientProvider client={makeQueryClient()}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
  return router;
}

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("routing and permissions", () => {
  it("sends a visitor who is not signed in to the login page", async () => {
    serve(null);
    renderAt("/tasks");
    expect(await screen.findByRole("button", { name: "دخول" })).toBeTruthy();
  });

  it("sends a temporary password to the change-password page first", async () => {
    serve(me({ must_change_password: true }));
    const router = renderAt("/tasks");
    expect(await screen.findByRole("heading", { name: "تغيير كلمة السر" })).toBeTruthy();
    expect(router.state.location.pathname).toBe("/change-password");
  });

  it("shows a staff member no accounts page, in the menu or by its address", async () => {
    serve(me());
    const router = renderAt("/users");
    expect(await screen.findByRole("heading", { level: 1, name: /الخير يا/ })).toBeTruthy();
    expect(router.state.location.pathname).toBe("/");
    for (const nav of screen.getAllByRole("navigation", { name: "التنقل" })) {
      expect(within(nav).queryByText("الحسابات")).toBeNull();
    }
  });

  it("shows the manager the accounts page in the settings", async () => {
    serve(me({ role: "manager", permissions: ["tasks.view_own", "tasks.view_all", "tasks.update", "tasks.assign", "users.manage", "account.self"] }));
    renderAt("/settings");
    await screen.findByRole("heading", { name: "الإعدادات" });
    expect(screen.getByRole("link", { name: /الحسابات/ })).toBeTruthy();
  });

  it("hides the accounts card in the settings from a staff member", async () => {
    serve(me());
    renderAt("/settings");
    await screen.findByRole("heading", { name: "الإعدادات" });
    expect(screen.queryByRole("link", { name: /الحسابات/ })).toBeNull();
  });

  it("shows the real task numbers on the home screen and the urgent task first", async () => {
    serve(me());
    renderAt("/");
    const urgent = await screen.findByRole("heading", { name: "أمور عاجلة" });
    expect(await within(urgent.closest("section")!).findByText("3")).toBeTruthy();
    const late = screen.getByRole("heading", { name: "مهام متأخرة" });
    expect(within(late.closest("section")!).getByText("2")).toBeTruthy();
    const table = screen.getByRole("heading", { name: "المهام العاجلة والمتأخرة" }).closest("section")!;
    expect(within(table).getByText("عاجلة")).toBeTruthy();
    expect(within(table).getByRole("link", { name: "افتح مهمة سارة محمد" }).getAttribute("href")).toBe("/tasks/t1");
  });

  it("opens the urgent tasks from «أمور عاجلة», and the calendar's arrows move the week", async () => {
    serve(me());
    renderAt("/");
    const urgent = await screen.findByRole("heading", { name: "أمور عاجلة" });
    expect(within(urgent.closest("section")!).getByRole("link").getAttribute("href")).toBe("/tasks?state=urgent");
    const calendar = screen.getByRole("heading", { name: "تقويم المواعيد" }).closest("section")!;
    expect(within(calendar).getByRole("list", { name: "الأسبوع ده" })).toBeTruthy();
    fireEvent.click(within(calendar).getByRole("button", { name: "الأسبوع الجاي" }));
    expect(within(calendar).getByRole("list", { name: "أسبوع تاني" })).toBeTruthy();
    fireEvent.click(within(calendar).getByRole("button", { name: "ارجع للأسبوع ده" }));
    expect(within(calendar).getByRole("list", { name: "الأسبوع ده" })).toBeTruthy();
  });

  it("opens the overdue tasks from a link", async () => {
    serve(me());
    renderAt("/tasks?state=overdue");
    await screen.findByRole("heading", { name: "المهام" });
    const state = screen.getByRole("group", { name: "الحالة" });
    expect(within(state).getByRole("button", { name: "متأخرة" }).getAttribute("aria-pressed")).toBe("true");
  });

  it("offers «الكل» only to people who may see every task", async () => {
    serve(me());
    renderAt("/tasks");
    await screen.findByRole("heading", { name: "المهام" });
    const who = screen.getByRole("group", { name: "مين" });
    expect(within(who).queryByRole("button", { name: "الكل" })).toBeNull();
    expect(within(who).getByRole("button", { name: "مهامي" }).getAttribute("aria-pressed")).toBe("true");
  });
});
