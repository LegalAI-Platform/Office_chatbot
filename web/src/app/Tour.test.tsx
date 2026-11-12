import { QueryClientProvider } from "@tanstack/react-query";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { createMemoryRouter, RouterProvider, useLocation } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import { meKey } from "../shared/api/session";
import type { Me, Permission } from "../shared/api/types";
import { makeQueryClient } from "./App";
import { Tour } from "./Tour";
import { dimPath, holeAround, place } from "./tourPlace";
import { tourSteps } from "./tourSteps";

// The roles' permissions as backend/src/Office.Application/Panel/Permissions.cs gives them.
const STAFF: Permission[] = ["tasks.view_own", "tasks.update", "account.self", "schedule.manage", "payments.verify", "payments.refund",
  "services.manage", "urf.manage"];
const LAWYER: Permission[] = ["tasks.view_own", "tasks.view_all", "tasks.update", "tasks.assign", "account.self", "lists.manage",
  "reports.view", "schedule.manage", "office.manage", "payments.verify", "payments.refund", "services.manage", "services.price",
  "urf.manage", "urf.review", "prices.manage"];
const MANAGER: Permission[] = [...LAWYER, "users.manage", "conversations.unmute"];
const MAINTENANCE: Permission[] = [...MANAGER, "texts.manage", "testing.forget", "maintenance.manage"];

const account = (over: Partial<Me> = {}): Me => ({
  id: "u1", username: "yasmin", display_name: "ياسمين علي", email: "yasmin@example.test", role: "staff", permissions: STAFF,
  must_change_password: false, two_factor: false, two_factor_required: false, avatar_at: null, tour_seen: false, ...over,
});
const lawyer = account({ role: "lawyer", display_name: "أحمد سمير", permissions: LAWYER });
const manager = account({ role: "manager", display_name: "منى", permissions: MANAGER });
const maintenance = account({ role: "maintenance", display_name: "محمد عماد", permissions: MAINTENANCE });

function Where() {
  const l = useLocation();
  return <output data-testid="where">{l.pathname + l.search}</output>;
}

/** The tour on its own over a page, the API at the fetch boundary; the page's root is #root, as in index.html. */
function show(me: Me, path = "/") {
  const calls: string[] = [];
  vi.spyOn(globalThis, "fetch").mockImplementation(async (url, init) => {
    const p = String(url).replace("/api/panel", "");
    calls.push(`${init?.method ?? "GET"} ${p}`);
    if (p === "/auth/csrf") return Response.json({ token: "t" });
    if (p === "/account/tour-seen") return new Response(null, { status: 204 });
    return Response.json({ error: "not_found" }, { status: 404 });
  });
  const qc = makeQueryClient();
  qc.setQueryData(meKey, me);
  const router = createMemoryRouter([{ path: "*", element: <><Tour /><Where /></> }], { initialEntries: [path] });
  const root = document.body.appendChild(document.createElement("div"));
  root.id = "root";
  render(<QueryClientProvider client={qc}><RouterProvider router={router} /></QueryClientProvider>, { container: root });
  return { calls, qc, router, root, saved: () => calls.filter((c) => c === "POST /account/tour-seen").length };
}

const title = () => screen.getByRole("dialog").querySelector("h2")?.textContent;
const wait = (ms: number) => act(() => new Promise((r) => setTimeout(r, ms)));

afterEach(() => {
  cleanup();
  document.getElementById("root")?.remove();
  vi.restoreAllMocks();
});

describe("the tour's steps (the owner's words)", () => {
  const body = (me: Me, id: string, wide = false, target: string | null = null) =>
    tourSteps(me).find((s) => s.id === id)!.body({ wide, target });

  it("shows each role only the pages it can open", () => {
    expect(tourSteps(account()).map((s) => s.id)).toEqual(
      ["welcome", "kpis", "tasks", "appointments", "chats", "money", "bell", "search", "settings", "account"]);
    for (const me of [lawyer, manager, maintenance]) {
      expect(tourSteps(me).map((s) => s.id)).toEqual(
        ["welcome", "kpis", "tasks", "appointments", "chats", "money", "reports", "bell", "search", "settings", "account"]);
    }
  });

  it("says what each role has in «الإعدادات» and in «المهام»", () => {
    expect(body(account(), "settings")).toBe("فيها حسابك، وكلمة السر، والأسعار.");
    expect(body(lawyer, "settings")).toBe("فيها حسابك، وكلمة السر، وقوايم المكتب، والأسعار.");
    expect(body(manager, "settings")).toBe("فيها حسابك، وكلمة السر، وحسابات المكتب، وقوايم المكتب، والأسعار.");
    expect(body(maintenance, "settings")).toBe("فيها حسابك، وكلمة السر، وحسابات المكتب، وقوايم المكتب، ونصوص حازم وأدوات التجربة، والأسعار.");
    expect(body(account(), "tasks")).toBe("لما عميل على واتساب يحتاج حد من المكتب، حازم بيفتح له مهمة هنا. اضغط «هاشتغل عليها»، ولما تخلّص «خلصت».");
    expect(body(lawyer, "tasks")).toBe("لما عميل على واتساب يحتاج حد من المكتب، حازم بيفتح له مهمة هنا. وتقدر تعيّن مين يشتغل عليها.");
  });

  it("greets by the first name, and says Ctrl K only where there is a keyboard", () => {
    expect(tourSteps(maintenance)[0]!.title).toBe("أهلًا بيك يا محمد");
    expect(tourSteps(account({ display_name: " " }))[0]!.title).toBe("أهلًا بيك");
    expect(body(account(), "search")).toBe("دوّر في المهام باسم العميل أو بآخر أرقام موبايله.");
    expect(body(account(), "search", true)).toBe("دوّر في المهام باسم العميل أو بآخر أرقام موبايله. وCtrl K بيفتح اختصارات لكل صفحة.");
  });

  it("says «المزيد» only when it points at «المزيد» (a phone's tab bar)", () => {
    const money = "الإيصالات: تتأكد إن التحويل وصل. طلبات الخدمات: عقد أو إنذار أو رأي. حالات العرفي: الورق والميعاد.";
    expect(body(account(), "money", true, "nav-money")).toBe(money);
    expect(body(account(), "money", false, "nav-more")).toBe(`${money} وتلاقيهم كلهم في «المزيد».`);
    expect(body(account(), "settings", true, "nav-settings")).toBe("فيها حسابك، وكلمة السر، والأسعار.");
    expect(body(account(), "settings", false, "nav-more")).toBe("فيها حسابك، وكلمة السر، والأسعار. وعلى الموبايل هتلاقيها في آخر «المزيد».");
    expect(tourSteps(account()).find((s) => s.id === "settings")!.targets).toEqual(["nav-settings", "nav-more"]);
  });
});

describe("the tour on the screen", () => {
  it("opens by itself on the home page, walks every step, and the end is saved once for the account", async () => {
    const { qc, root, saved } = show(account());
    expect(await screen.findByRole("dialog", { name: "أهلًا بيك يا ياسمين" })).toBeTruthy();
    expect(screen.getByText("1 من 10")).toBeTruthy();
    expect(root.hasAttribute("inert")).toBe(true); // the panel under it cannot be clicked or reached
    expect(screen.queryByRole("button", { name: "السابق" })).toBeNull();

    const titles: (string | null | undefined)[] = [];
    for (let i = 0; i < 10; i++) {
      titles.push(title());
      fireEvent.click(screen.getByRole("button", { name: i === 0 ? "يلا نبدأ" : i === 9 ? "تمام" : "التالي" }));
    }
    expect(titles).toEqual(["أهلًا بيك يا ياسمين", "أرقام النهارده", "الطلبات / المهام", "المواعيد", "المحادثات",
      "الفلوس والخدمات", "التنبيهات", "البحث", "الإعدادات", "حسابك"]);
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(root.hasAttribute("inert")).toBe(false);
    await waitFor(() => expect(saved()).toBe(1));
    expect(qc.getQueryData<Me>(meKey)?.tour_seen).toBe(true);
  });

  it("«تخطّي» ends it at once and saves it; it does not come back by itself", async () => {
    const { saved } = show(lawyer);
    await screen.findByRole("dialog");
    fireEvent.click(screen.getByRole("button", { name: "يلا نبدأ" }));
    expect(screen.getByText("2 من 11")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "تخطّي" }));
    expect(screen.queryByRole("dialog")).toBeNull();
    await waitFor(() => expect(saved()).toBe(1));
    await wait(900);
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("Esc skips too, and the last step has no «تخطّي»", async () => {
    const { saved } = show(account());
    await screen.findByRole("dialog");
    for (let i = 0; i < 9; i++) fireEvent.keyDown(document, { key: "ArrowLeft" });
    expect(title()).toBe("حسابك");
    expect(screen.queryByRole("button", { name: "تخطّي" })).toBeNull();
    fireEvent.keyDown(document, { key: "Escape" });
    expect(screen.queryByRole("dialog")).toBeNull();
    await waitFor(() => expect(saved()).toBe(1));
  });

  it("«السابق» and the arrows move as the page reads (← is the next one)", async () => {
    show(account());
    await screen.findByRole("dialog");
    fireEvent.keyDown(document, { key: "ArrowLeft" });
    expect(screen.getByText("2 من 10")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "السابق" }));
    expect(screen.getByText("1 من 10")).toBeTruthy();
    fireEvent.keyDown(document, { key: "ArrowRight" }); // nothing before the first
    expect(screen.getByText("1 من 10")).toBeTruthy();
    fireEvent.keyDown(document, { key: "ArrowLeft" });
    fireEvent.keyDown(document, { key: "ArrowLeft" });
    fireEvent.keyDown(document, { key: "ArrowRight" });
    expect(title()).toBe("أرقام النهارده");
  });

  it("stays away from an account that has seen it, and from an API too old to say", async () => {
    show(account({ tour_seen: true }));
    await wait(900);
    expect(screen.queryByRole("dialog")).toBeNull();
    cleanup();
    document.getElementById("root")?.remove();
    show(account({ tour_seen: undefined }));
    await wait(900);
    expect(screen.queryByRole("dialog")).toBeNull();
  });

  it("waits for the home page", async () => {
    const { router } = show(account(), "/tasks");
    await wait(900);
    expect(screen.queryByRole("dialog")).toBeNull();
    await act(() => router.navigate("/"));
    expect(await screen.findByRole("dialog", { name: "أهلًا بيك يا ياسمين" })).toBeTruthy();
  });

  it("«اعرض الجولة تاني» opens it any time from the first step, and seeing it again saves nothing", async () => {
    const { saved } = show(account({ tour_seen: true }), "/?tour=1");
    expect(await screen.findByRole("dialog", { name: "أهلًا بيك يا ياسمين" })).toBeTruthy();
    await waitFor(() => expect(screen.getByTestId("where").textContent).toBe("/"));
    fireEvent.click(screen.getByRole("button", { name: "يلا نبدأ" }));
    fireEvent.click(screen.getByRole("button", { name: "تخطّي" }));
    expect(screen.queryByRole("dialog")).toBeNull();
    await wait(100);
    expect(saved()).toBe(0);
  });

  it("closes when the page changes under it (back), without saving", async () => {
    const { router, saved } = show(account());
    await screen.findByRole("dialog");
    await act(() => router.navigate("/tasks"));
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(saved()).toBe(0);
  });
});

describe("where a bubble goes", () => {
  const desk = { width: 1280, height: 760 };
  const phone = { width: 375, height: 812 };
  const bubble = { width: 360, height: 250 };
  const small = { width: 343, height: 260 };

  it("beside a side-bar link, toward the page, its arrow at the link", () => {
    const p = place({ left: 1090, top: 120, width: 180, height: 50 }, bubble, desk, true);
    expect(p).toEqual({ left: 1090 - 14 - 360, top: 20, side: "left", arrow: 125 });
  });

  it("over a phone's tab bar and under its top bar", () => {
    expect(place({ left: 150, top: 760, width: 75, height: 50 }, small, phone).side).toBe("above");
    expect(place({ left: 300, top: 12, width: 48, height: 48 }, small, phone)).toEqual({ left: 16, top: 74, side: "below", arrow: 308 });
  });

  it("in the middle without a target, and over the far half when nothing fits beside it", () => {
    expect(place(null, bubble, desk)).toEqual({ left: 460, top: 255, side: "free", arrow: 0 });
    expect(place({ left: 0, top: 100, width: 375, height: 600 }, small, phone)).toEqual({ left: 16, top: 540, side: "free", arrow: 0 });
  });

  it("lights a little room around the target, kept on the screen, as a rounded hole in the dim", () => {
    expect(holeAround({ left: 0, top: 790, width: 75, height: 22 }, phone)).toEqual({ left: 2, top: 784, width: 79, height: 26 });
    expect(dimPath(phone, null)).toBe("M0 0H375V812H0Z");
    expect(dimPath(phone, { left: 10, top: 20, width: 100, height: 40 })).toBe(
      "M0 0H375V812H0ZM26 20H94A16 16 0 0 1 110 36V44A16 16 0 0 1 94 60H26A16 16 0 0 1 10 44V36A16 16 0 0 1 26 20Z");
  });
});
