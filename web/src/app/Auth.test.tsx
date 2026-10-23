import { QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { Me, PanelUser } from "../shared/api/types";
import { makeQueryClient, routes } from "./App";

const boss: Me = {
  id: "b1",
  username: "boss",
  display_name: "المدير",
  email: "boss@example.test",
  role: "manager",
  permissions: ["tasks.view_own", "tasks.view_all", "tasks.update", "tasks.assign", "users.manage", "account.self"],
  must_change_password: false,
  two_factor: true,
  two_factor_required: false,
};

type Answer = { status?: number; body?: unknown };
type Calls = { method: string; path: string; body: unknown }[];

/** The API at the fetch boundary: each path answers from the table; every call is recorded. */
function serve(table: Record<string, Answer | ((body: unknown) => Answer)>) {
  const calls: Calls = [];
  vi.spyOn(globalThis, "fetch").mockImplementation(async (url, init) => {
    const path = String(url).replace("/api/panel", "").split("?")[0]!;
    const method = init?.method ?? "GET";
    const body = init?.body ? JSON.parse(String(init.body)) : undefined;
    calls.push({ method, path, body });
    if (path === "/auth/csrf") return Response.json({ token: "t" });
    const entry = table[`${method} ${path}`];
    const a = typeof entry === "function" ? entry(body) : entry ?? { status: 404, body: { error: "not_found" } };
    const status = a.status ?? 200;
    return status === 204 || a.body === undefined ? new Response(null, { status }) : Response.json(a.body, { status });
  });
  return calls;
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

const type = (label: RegExp | string, value: string) => fireEvent.change(screen.getByLabelText(label), { target: { value } });

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("sign-in, sign-up and the authenticator (v1.9)", () => {
  it("asks for the authenticator code after the right password, before any session", async () => {
    const calls = serve({
      "GET /auth/me": { status: 401 },
      "POST /auth/login": { body: { two_factor: true, ticket: "tk" } },
      "POST /auth/login/2fa": (b) => ((b as { code: string }).code === "123456" ? { body: boss } : { status: 401, body: { error: "invalid_code" } }),
      "GET /dashboard": { body: { urgent: 0, overdue: 0, unassigned: 0, active: 0, urgent_by_day: [], overdue_by_day: [], recent: [], priority: [] } },
    });
    const router = renderAt("/login");
    await screen.findByRole("button", { name: "دخول" });
    type(/اسم المستخدم/, "boss");
    type(/كلمة السر/, "a long test password 1");
    fireEvent.click(screen.getByRole("button", { name: "دخول" }));

    expect(await screen.findByRole("heading", { name: "التحقق بخطوتين" })).toBeTruthy();
    type(/الكود/, "000000");
    fireEvent.click(screen.getByRole("button", { name: "تأكيد" }));
    expect((await screen.findByRole("alert")).textContent).toContain("الكود غلط");

    type(/الكود/, "123456");
    fireEvent.click(screen.getByRole("button", { name: "تأكيد" }));
    await screen.findByRole("heading", { level: 1, name: /يا/ });
    expect(router.state.location.pathname).toBe("/");
    expect(calls.filter((c) => c.path === "/auth/login/2fa").map((c) => (c.body as { ticket: string }).ticket)).toEqual(["tk", "tk"]);
  });

  it("tells a registered account that waits for the manager, and sends an unconfirmed one to the code page", async () => {
    let answer: Answer = { status: 403, body: { error: "pending_approval" } };
    serve({ "GET /auth/me": { status: 401 }, "POST /auth/login": () => answer });
    const router = renderAt("/login");
    await screen.findByRole("button", { name: "دخول" });
    type(/اسم المستخدم/, "mona");
    type(/كلمة السر/, "a long test password 1");
    fireEvent.click(screen.getByRole("button", { name: "دخول" }));
    expect((await screen.findByRole("status")).textContent).toContain("مستني المدير");

    answer = { status: 403, body: { error: "email_not_verified", email: "mona@example.test" } };
    type(/كلمة السر/, "a long test password 1");
    fireEvent.click(screen.getByRole("button", { name: "دخول" }));
    expect(await screen.findByRole("heading", { name: "أكّد الإيميل" })).toBeTruthy();
    expect(router.state.location.pathname).toBe("/verify-email");
    expect(screen.getByText("mona@example.test")).toBeTruthy();
  });

  it("registers, then confirms the e-mail with the six digits", async () => {
    const calls = serve({
      "GET /auth/me": { status: 401 },
      "GET /auth/signup": { body: { available: true } },
      "POST /auth/register": { status: 202, body: { mail_sent: true } },
      "POST /auth/register/verify": { status: 204 },
    });
    renderAt("/register");
    await screen.findByRole("button", { name: "اعمل الحساب" });
    type(/^الاسم/, "منى علي");
    type(/اسم المستخدم/, "Mona");
    type(/الإيميل/, "mona@example.test");
    type(/^كلمة السر/, "a long test password 1");
    type(/اكتب كلمة السر تاني/, "a long test password 2");
    expect(screen.getByText("كلمتين السر مش زي بعض.")).toBeTruthy();
    expect((screen.getByRole("button", { name: "اعمل الحساب" }) as HTMLButtonElement).disabled).toBe(true);
    type(/اكتب كلمة السر تاني/, "a long test password 1");
    fireEvent.click(screen.getByRole("button", { name: "اعمل الحساب" }));

    expect(await screen.findByRole("heading", { name: "أكّد الإيميل" })).toBeTruthy();
    expect(calls.find((c) => c.path === "/auth/register")!.body).toEqual({
      display_name: "منى علي", username: "mona", email: "mona@example.test", password: "a long test password 1",
    });
    expect((screen.getByRole("button", { name: /ابعت كود تاني بعد/ }) as HTMLButtonElement).disabled).toBe(true);
    type(/الكود/, "12a34 56");
    expect((screen.getByLabelText(/الكود/) as HTMLInputElement).value).toBe("123456");
    fireEvent.click(screen.getByRole("button", { name: "تأكيد" }));
    expect(await screen.findByRole("heading", { name: "الإيميل اتأكد" })).toBeTruthy();
  });

  it("says sign-up is not available when the server has no mail set up", async () => {
    serve({ "GET /auth/me": { status: 401 }, "GET /auth/signup": { body: { available: false } } });
    renderAt("/register");
    expect((await screen.findByRole("alert")).textContent).toContain("لسه مش متاح");
  });

  it("sends a manager without the authenticator to turn it on first", async () => {
    serve({
      "GET /auth/me": { body: { ...boss, two_factor: false, two_factor_required: true } },
      "GET /auth/2fa": { body: { enabled: false, recovery_codes_left: 0, available: true } },
    });
    const router = renderAt("/tasks");
    expect(await screen.findByRole("heading", { name: "شغّل التحقق بخطوتين" })).toBeTruthy();
    expect(router.state.location.pathname).toBe("/setup-2fa");
    expect((screen.getByRole("button", { name: "كمّل للوحة" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("lets the manager approve a waiting account with a role", async () => {
    const waiting: PanelUser = {
      id: "p1", username: "mona", display_name: "منى علي", email: "mona@example.test", role: null, active: false,
      locked: false, must_change_password: false, pending: true, email_verified: true, two_factor: false,
      created_at: "2026-10-23T08:00:00Z",
    };
    const calls = serve({
      "GET /auth/me": { body: boss },
      "GET /users": { body: [waiting] },
      "POST /users/p1/approve": { body: { ...waiting, role: "lawyer", active: true, pending: false } },
      "GET /dashboard": { body: { urgent: 0, overdue: 0, unassigned: 0, active: 0, urgent_by_day: [], overdue_by_day: [], recent: [], priority: [] } },
    });
    renderAt("/users");
    const pending = await screen.findByRole("list", { name: "حسابات مستنية التفعيل" });
    expect(within(pending).getByText("منى علي")).toBeTruthy();
    fireEvent.change(within(pending).getByRole("combobox"), { target: { value: "lawyer" } });
    fireEvent.click(within(pending).getByRole("button", { name: /فعّل الحساب/ }));
    await vi.waitFor(() => expect(calls.some((c) => c.path === "/users/p1/approve")).toBe(true));
    expect(calls.find((c) => c.path === "/users/p1/approve")!.body).toEqual({ role: "lawyer", staff_name: null });
  });
});
