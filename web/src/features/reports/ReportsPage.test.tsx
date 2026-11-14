import { QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import { makeQueryClient } from "../../app/App";
import type { MonthReport } from "../../shared/api/types";
import { periodOf, ReportsPage } from "./ReportsPage";

// v1.110 (the owner: «عاوزك تهندل ui بتاعها وتبقي فعلا صفحه تقارير … تقدر تستخرج تقارير pdf templtes … ظاهره للمستشار»)
const report: MonthReport = {
  month: "2026-11", tasks: 10, done: 8, done_on_time: 6, late: 1,
  by_type: [{ key: "human_requested", count: 10 }], by_staff: [{ staff: "ياسمين", assigned: 10, done: 8, on_time: 6 }],
  new_clients: 4, by_source: [], by_matter: [], by_client_type: [],
  by_booking: [{ key: "confirmed", count: 2 }, { key: "completed", count: 3 }, { key: "cancelled", count: 5 }],
  received: 1500, missing: 200, refunded: 0,
  ratings: { sent: 3, answered: 2, average: 4.5, by_service: [], low: [] },
  review: [], free_answers: [], written: { total: 0, fallbacks: 0, retried: 0, by_reason: [] },
  ai_cost: { total_usd: 0, calls: 0, conversations: 0, input_tokens: 0, output_tokens: 0, unpriced: 0 },
};

const lawyer = {
  id: "u2", username: "saad", display_name: "سعد", email: "saad@example.test", role: "lawyer",
  permissions: ["tasks.view_all", "reports.view", "users.manage"], must_change_password: false, two_factor: true, two_factor_required: true, avatar_at: null,
};

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

function open(me: object) {
  const asked: string[] = [];
  vi.spyOn(globalThis, "fetch").mockImplementation(async (url) => {
    const path = String(url);
    asked.push(path);
    if (path.endsWith("/auth/me")) return Response.json(me);
    if (path.includes("/reports/trend")) return Response.json({ months: [] });
    if (path.includes("/reports?")) return Response.json(report);
    return Response.json({ error: "not_found" }, { status: 404 });
  });
  const router = createMemoryRouter([{ path: "*", element: <ReportsPage /> }], { initialEntries: ["/reports"] });
  render(<QueryClientProvider client={makeQueryClient()}><RouterProvider router={router} /></QueryClientProvider>);
  return asked;
}

describe("«التقارير»", () => {
  it("names the period as the report prints it", () => {
    expect(periodOf("2026-11")).toBe("نوفمبر 2026");
    expect(periodOf("2026-11-14")).toBe("14 نوفمبر 2026");
    expect(periodOf("2026-11-01..2026-11-14")).toBe("من 1 نوفمبر 2026 إلى 14 نوفمبر 2026");
  });

  it("shows the lawyer the office's reports, one at a time or all, and none of Hazem's own parts", async () => {
    const asked = open(lawyer);
    expect(await screen.findByRole("tab", { name: "التقرير الشامل" })).toBeTruthy();
    expect(screen.getByRole("tab", { name: "المواعيد والإيرادات" })).toBeTruthy();
    await waitFor(() => expect(screen.getAllByText("الحجوزات المؤكدة").length).toBeGreaterThan(0));
    expect(screen.queryByRole("tab", { name: "أداء حازم (للصيانة)" })).toBeNull();
    expect(screen.queryByText("تكلفة المحادثات")).toBeNull();
    expect(asked.some((p) => p.includes("/reports/ai-cost"))).toBe(false);

    fireEvent.click(screen.getByRole("tab", { name: "المهام وأداء فريق العمل" }));
    expect(screen.getByRole("tab", { name: "المهام وأداء فريق العمل" }).getAttribute("aria-selected")).toBe("true");
    expect(document.querySelector('[data-report="summary"]')?.className).toContain("hidden");
    expect(document.querySelector('[data-report="tasks"]')?.className).not.toMatch(/(^|\s)hidden/);
  });

  it("«تصدير PDF» prints the report picked, on the office's letterhead", async () => {
    const print = vi.fn();
    vi.stubGlobal("print", print);
    open(lawyer);
    fireEvent.click(await screen.findByRole("tab", { name: "المواعيد والإيرادات" }));
    fireEvent.click(screen.getByRole("button", { name: "تصدير PDF" }));
    await waitFor(() => expect(print).toHaveBeenCalledTimes(1));
    expect(document.querySelector("[data-letterhead]")?.textContent).toContain("مكتب سعد فتحي سعد");
    expect(document.querySelector("[data-letterhead] h1")?.textContent).toBe("المواعيد والإيرادات");
    expect(document.querySelector("[data-letterhead]")?.textContent).toContain("الفترة: ");
    expect(document.querySelector('[data-report="bookings"]')?.className).toContain("print:block");
    expect(document.querySelector('[data-report="tasks"]')?.className).toContain("print:hidden");
    window.dispatchEvent(new Event("afterprint"));
    vi.unstubAllGlobals();
  });
});
