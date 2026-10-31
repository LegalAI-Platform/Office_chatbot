import { QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { Me } from "../shared/api/types";
import { makeQueryClient, routes } from "./App";

// The canvas is not in jsdom: the resize gives back a known small JPEG.
const small = new Blob([new Uint8Array([0xff, 0xd8, 0xff, 0xe0])], { type: "image/jpeg" });
vi.mock("../features/auth/photo", async (original) => ({
  ...(await original<typeof import("../features/auth/photo")>()),
  squareJpeg: vi.fn(async () => small),
}));

const yasmin: Me = {
  id: "u1", username: "yasmin", display_name: "ياسمين علي", email: "yasmin@example.test", role: "staff",
  permissions: ["tasks.view_own", "tasks.update", "account.self"], must_change_password: false, two_factor: false,
  two_factor_required: false, avatar_at: null,
};

type Call = { method: string; path: string; type: string | undefined; body: unknown };

/** The API at the fetch boundary; the photo's time changes with PUT and DELETE like the real one. */
function serve(put: { status: number; body?: unknown } = { status: 200 }) {
  const calls: Call[] = [];
  let at: string | null = null;
  vi.spyOn(globalThis, "fetch").mockImplementation(async (url, init) => {
    const path = String(url).replace("/api/panel", "").split("?")[0]!;
    const method = init?.method ?? "GET";
    calls.push({ method, path, type: (init?.headers as Record<string, string> | undefined)?.["Content-Type"], body: init?.body });
    if (path === "/auth/csrf") return Response.json({ token: "t" });
    if (path === "/auth/me") return Response.json({ ...yasmin, avatar_at: at });
    if (path === "/auth/2fa") return Response.json({ enabled: false, recovery_codes_left: 0, available: false });
    if (method === "PUT" && path === "/account/avatar") {
      if (put.status !== 200) return Response.json(put.body, { status: put.status });
      at = "2026-10-31T12:00:00.123456+00:00";
      return Response.json({ avatar_at: at });
    }
    if (method === "DELETE" && path === "/account/avatar") {
      at = null;
      return new Response(null, { status: 204 });
    }
    return Response.json({ error: "not_found" }, { status: 404 });
  });
  return calls;
}

function renderAt(path: string) {
  render(
    <QueryClientProvider client={makeQueryClient()}>
      <RouterProvider router={createMemoryRouter(routes, { initialEntries: [path] })} />
    </QueryClientProvider>,
  );
}

const photos = () => [...document.querySelectorAll("img")].filter((i) => i.getAttribute("src")?.startsWith("/api/panel/users/u1/avatar?v="));
const pick = (file: File) => fireEvent.change(screen.getByLabelText("ملف الصورة"), { target: { files: [file] } });

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("«الصورة الشخصية» on «حسابي»", () => {
  it("uploads the resized JPEG, then the top bar and the page show it; removing it goes back to the initials", async () => {
    const calls = serve();
    renderAt("/account");
    await screen.findByRole("heading", { name: "الصورة الشخصية" });
    expect(photos()).toHaveLength(0);
    expect(screen.getAllByText("ي ع").length).toBeGreaterThanOrEqual(2); // the top bar and the page
    expect(screen.queryByRole("button", { name: "شيل الصورة" })).toBeNull();

    pick(new File(["x".repeat(5000)], "me.png", { type: "image/png" }));
    await waitFor(() => expect(photos()).toHaveLength(2));
    const put = calls.find((c) => c.method === "PUT")!;
    expect(put.path).toBe("/account/avatar");
    expect(put.type).toBe("image/jpeg");
    expect(put.body).toBe(small);
    expect(photos()[0]!.getAttribute("src")).toBe(`/api/panel/users/u1/avatar?v=${encodeURIComponent("2026-10-31T12:00:00.123456+00:00")}`);

    fireEvent.click(screen.getByRole("button", { name: "شيل الصورة" }));
    await waitFor(() => expect(photos()).toHaveLength(0));
    expect(calls.some((c) => c.method === "DELETE" && c.path === "/account/avatar")).toBe(true);
    expect(screen.queryByRole("button", { name: "شيل الصورة" })).toBeNull();
  });

  it("refuses another type before any upload, and shows the API's refusal in Arabic", async () => {
    const calls = serve({ status: 400, body: { error: "avatar_too_large" } });
    renderAt("/account");
    await screen.findByRole("heading", { name: "الصورة الشخصية" });

    pick(new File(["GIF89a"], "me.gif", { type: "image/gif" }));
    expect((await screen.findByText("الصورة لازم تبقى JPG أو PNG أو WebP.")).getAttribute("role")).toBe("alert");
    expect(calls.some((c) => c.method === "PUT")).toBe(false);

    pick(new File(["x"], "me.jpg", { type: "image/jpeg" }));
    expect((await screen.findByText(/أكبر من 300 كيلو/)).getAttribute("role")).toBe("alert");
    expect(screen.queryByText("الصورة لازم تبقى JPG أو PNG أو WebP.")).toBeNull();
    expect(photos()).toHaveLength(0);
  });
});
