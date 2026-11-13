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
  avatar_at: null,
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
    type(/الرمز/, "000000");
    fireEvent.click(screen.getByRole("button", { name: "تأكيد" }));
    expect((await screen.findByRole("alert")).textContent).toContain("الرمز غير صحيح");

    type(/الرمز/, "123456");
    fireEvent.click(screen.getByRole("button", { name: "تأكيد" }));
    await screen.findByRole("heading", { level: 1, name: /المدير/ });
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
    expect((await screen.findByRole("status")).textContent).toContain("في انتظار تفعيل المدير");

    answer = { status: 403, body: { error: "email_not_verified", email: "mona@example.test" } };
    type(/كلمة السر/, "a long test password 1");
    fireEvent.click(screen.getByRole("button", { name: "دخول" }));
    expect(await screen.findByRole("heading", { name: "تأكيد البريد الإلكتروني" })).toBeTruthy();
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
    await screen.findByRole("button", { name: "إنشاء الحساب" });
    type(/^الاسم/, "منى علي");
    type(/اسم المستخدم/, "Mona");
    type(/البريد الإلكتروني/, "mona@example.test");
    type(/^كلمة السر/, "a long test password 1");
    type(/أعد كتابة كلمة السر/, "a long test password 2");
    expect(screen.getByText("كلمتا السر غير متطابقتين.")).toBeTruthy();
    expect((screen.getByRole("button", { name: "إنشاء الحساب" }) as HTMLButtonElement).disabled).toBe(true);
    type(/أعد كتابة كلمة السر/, "a long test password 1");
    fireEvent.click(screen.getByRole("button", { name: "إنشاء الحساب" }));

    expect(await screen.findByRole("heading", { name: "تأكيد البريد الإلكتروني" })).toBeTruthy();
    expect(calls.find((c) => c.path === "/auth/register")!.body).toEqual({
      display_name: "منى علي", username: "mona", email: "mona@example.test", password: "a long test password 1",
    });
    expect((screen.getByRole("button", { name: /أرسل رمزًا آخر بعد/ }) as HTMLButtonElement).disabled).toBe(true);
    type(/الرمز/, "12a34 56");
    expect((screen.getByLabelText(/الرمز/) as HTMLInputElement).value).toBe("123456");
    fireEvent.click(screen.getByRole("button", { name: "تأكيد" }));
    expect(await screen.findByRole("heading", { name: "تم تأكيد البريد الإلكتروني" })).toBeTruthy();
  });

  it("says sign-up is not available when the server has no mail set up", async () => {
    serve({ "GET /auth/me": { status: 401 }, "GET /auth/signup": { body: { available: false } } });
    renderAt("/register");
    expect((await screen.findByRole("alert")).textContent).toContain("غير متاح بعد");
  });

  it("sends a manager without the authenticator to turn it on first", async () => {
    serve({
      "GET /auth/me": { body: { ...boss, two_factor: false, two_factor_required: true } },
      "GET /auth/2fa": { body: { enabled: false, recovery_codes_left: 0, available: true } },
    });
    const router = renderAt("/tasks");
    expect(await screen.findByRole("heading", { name: "تفعيل التحقق بخطوتين" })).toBeTruthy();
    expect(router.state.location.pathname).toBe("/setup-2fa");
    expect((screen.getByRole("button", { name: "المتابعة إلى اللوحة" }) as HTMLButtonElement).disabled).toBe(true);
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
    const pending = await screen.findByRole("list", { name: "حسابات في انتظار التفعيل" });
    expect(within(pending).getByText("منى علي")).toBeTruthy();
    fireEvent.change(within(pending).getByRole("combobox"), { target: { value: "lawyer" } });
    fireEvent.click(within(pending).getByRole("button", { name: /فعّل الحساب/ }));
    await vi.waitFor(() => expect(calls.some((c) => c.path === "/users/p1/approve")).toBe(true));
    expect(calls.find((c) => c.path === "/users/p1/approve")!.body).toEqual({ role: "lawyer", staff_name: null });
  });

  it("asks before changing an account's role, and keeps the old one on «no»", async () => {
    const sara: PanelUser = {
      id: "s1", username: "sara", display_name: "سارة", email: "sara@example.test", role: "staff", active: true,
      locked: false, must_change_password: false, pending: false, email_verified: true, two_factor: false,
      created_at: "2026-10-20T08:00:00Z",
    };
    const calls = serve({
      "GET /auth/me": { body: boss },
      "GET /users": { body: [sara] },
      "POST /users/s1/role": { body: { ...sara, role: "lawyer" } },
      "GET /dashboard": { body: { urgent: 0, overdue: 0, unassigned: 0, active: 0, urgent_by_day: [], overdue_by_day: [], recent: [], priority: [] } },
    });
    renderAt("/users");
    const role = (await screen.findByRole("combobox", { name: "دور سارة" })) as HTMLSelectElement;
    fireEvent.change(role, { target: { value: "lawyer" } });
    const dialog = await screen.findByRole("alertdialog");
    expect(dialog.textContent).toContain("سيُسجَّل خروجه من اللوحة");
    fireEvent.click(within(dialog).getByRole("button", { name: "إلغاء" }));
    await vi.waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    expect(role.value).toBe("staff");
    expect(calls.some((c) => c.path === "/users/s1/role")).toBe(false);

    fireEvent.change(role, { target: { value: "lawyer" } });
    fireEvent.click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "نعم، غيّره" }));
    await vi.waitFor(() => expect(calls.find((c) => c.path === "/users/s1/role")?.body).toEqual({ role: "lawyer" }));
  });

  it("the first sign-in: two steps, the new password's list ticks itself, and a password can be shown while it is typed", async () => {
    const calls = serve({
      "GET /auth/me": { body: { ...boss, display_name: "منى علي", must_change_password: true } },
      "POST /auth/change-password": { status: 204 },
    });
    renderAt("/change-password");
    expect(await screen.findByText(/مرحبًا منى/)).toBeTruthy();
    const submit = screen.getByRole("button", { name: "غيّر كلمة السر" }) as HTMLButtonElement;
    expect(submit.disabled).toBe(true);
    type(/^كلمة السر المؤقتة/, "Temp-123");
    type(/^كلمة السر الجديدة/, "a long new password");
    expect(submit.disabled).toBe(true); // the repeat is still empty
    const rules = screen.getByRole("list", { name: "شروط كلمة السر الجديدة" });
    expect(within(rules).getByText("12 حرفًا على الأقل").closest("li")!.textContent).toContain("مستوفى");
    expect(within(rules).getByText("الكلمتان متطابقتان").closest("li")!.textContent).toContain("غير مستوفى");
    type(/^أعد كتابتها/, "a long new password");
    expect(submit.disabled).toBe(false);
    fireEvent.click(screen.getByRole("button", { name: "إظهار كلمة السر الجديدة" }));
    expect((screen.getByLabelText(/^كلمة السر الجديدة/) as HTMLInputElement).type).toBe("text");
    fireEvent.click(submit);
    await vi.waitFor(() => expect(calls.find((c) => c.path === "/auth/change-password")?.body)
      .toEqual({ current_password: "Temp-123", new_password: "a long new password" }));
  });

  it("a new account's temporary password comes with the message to send (the address, the user name, the password)", async () => {
    serve({
      "GET /auth/me": { body: boss },
      "GET /users": { body: [] },
      "POST /users": { body: { user: { id: "n1", username: "ahmed.samir", display_name: "أحمد سمير", email: "a@example.test", role: "staff", active: true,
        locked: false, must_change_password: true, pending: false, email_verified: true, two_factor: false, created_at: "2026-11-05T08:00:00Z" },
        temporary_password: "Tmp-Kz9-4pQ-xT7m" } },
      "GET /dashboard": { body: { urgent: 0, overdue: 0, unassigned: 0, active: 0, urgent_by_day: [], overdue_by_day: [], recent: [], priority: [] } },
    });
    renderAt("/users");
    fireEvent.click(await screen.findByRole("button", { name: "إضافة حساب" }));
    type(/الاسم الظاهر/, "أحمد سمير");
    type(/اسم المستخدم/, "ahmed.samir");
    type(/البريد الإلكتروني/, "a@example.test");
    fireEvent.click(screen.getByRole("button", { name: "إنشاء الحساب" }));
    const message = await screen.findByLabelText("الرسالة التي سترسلها");
    expect(message.textContent).toContain("اسم المستخدم: ahmed.samir");
    expect(message.textContent).toContain("كلمة السر المؤقتة: Tmp-Kz9-4pQ-xT7m");
    expect(message.textContent).toContain("مرحبًا أحمد");
    expect(screen.getByRole("button", { name: "انسخ الرسالة كاملة" })).toBeTruthy();
  });

  it("shows a new account's field errors in Arabic, without the general line", async () => {
    serve({
      "GET /auth/me": { body: boss },
      "GET /users": { body: [] },
      "POST /users": { status: 400, body: { error: "validation", errors: { username: ["username_taken"], email: ["invalid"] } } },
      "GET /dashboard": { body: { urgent: 0, overdue: 0, unassigned: 0, active: 0, urgent_by_day: [], overdue_by_day: [], recent: [], priority: [] } },
    });
    renderAt("/users");
    fireEvent.click(await screen.findByRole("button", { name: /إضافة حساب/ }));
    type(/الاسم الظاهر/, "منى");
    type(/اسم المستخدم/, "mona");
    type(/البريد الإلكتروني/, "mona@x");
    fireEvent.click(screen.getByRole("button", { name: "إنشاء الحساب" }));
    expect(await screen.findByText("اسم المستخدم هذا مسجّل من قبل.")).toBeTruthy();
    expect(screen.getByText("اكتب بريدًا إلكترونيًا صحيحًا.")).toBeTruthy();
    expect(screen.queryByText("username_taken")).toBeNull();
    expect(screen.queryByText("بعض البيانات المُدخلة غير صحيحة. راجعها ثم حاول مرة أخرى.")).toBeNull();
  });
});
