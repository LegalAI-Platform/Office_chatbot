import { afterEach, describe, expect, it, vi } from "vitest";
import { api, ApiError, errorMessage, resetCsrf } from "./client";

const json = (status: number, body: unknown, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json", ...headers } });

afterEach(() => {
  vi.restoreAllMocks();
  resetCsrf();
});

describe("api client", () => {
  it("talks to /api/panel only and sends no antiforgery header on GET", async () => {
    const fetchMock = vi.spyOn(globalThis, "fetch").mockResolvedValue(json(200, { ok: 1 }));
    await api("GET", "/tasks");
    const [url, init] = fetchMock.mock.calls[0]!;
    expect(url).toBe("/api/panel/tasks");
    expect((init!.headers as Record<string, string>)["X-CSRF-TOKEN"]).toBeUndefined();
    expect(init!.credentials).toBe("same-origin");
  });

  it("fetches the antiforgery token once and sends it on every change", async () => {
    const fetchMock = vi.spyOn(globalThis, "fetch").mockImplementation(async (url) =>
      String(url).endsWith("/auth/csrf") ? json(200, { token: "t-1" }) : json(200, {}),
    );
    await api("POST", "/tasks/1/start");
    await api("POST", "/tasks/1/done");
    const calls = fetchMock.mock.calls.map(([u, i]) => [String(u), (i?.headers as Record<string, string>)?.["X-CSRF-TOKEN"]]);
    expect(calls).toEqual([
      ["/api/panel/auth/csrf", undefined],
      ["/api/panel/tasks/1/start", "t-1"],
      ["/api/panel/tasks/1/done", "t-1"],
    ]);
  });

  it("retries once with a fresh token when the server refuses a stale one", async () => {
    let tokens = 0;
    let posts = 0;
    vi.spyOn(globalThis, "fetch").mockImplementation(async (url) => {
      if (String(url).endsWith("/auth/csrf")) return json(200, { token: `t-${++tokens}` });
      posts++;
      return posts === 1 ? json(400, { error: "antiforgery" }, { "X-Antiforgery-Failed": "1" }) : json(200, { done: true });
    });
    await expect(api("POST", "/tasks/1/start")).resolves.toEqual({ done: true });
    expect(tokens).toBe(2);
  });

  it("turns API errors into Arabic messages with their code and field errors", async () => {
    vi.spyOn(globalThis, "fetch").mockImplementation(async (url) =>
      String(url).endsWith("/auth/csrf")
        ? json(200, { token: "t" })
        : json(400, { error: "username_taken", errors: { username: ["taken"] } }),
    );
    const error = await api("POST", "/users", {}).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).fields?.username).toEqual(["taken"]);
    expect(errorMessage(error)).toBe("اسم المستخدم ده موجود قبل كده.");
  });

  it("takes a success with no body (202 from /auth/forgot, 204) as done, not as a lost connection", async () => {
    vi.spyOn(globalThis, "fetch").mockImplementation(async (url) =>
      String(url).endsWith("/auth/csrf") ? json(200, { token: "t-1" }) : new Response(null, { status: 202 }),
    );
    await expect(api("POST", "/auth/forgot", { login: "x" })).resolves.toBeUndefined();
  });

  it("has a clear message for every common status and for no connection", () => {
    expect(errorMessage(new ApiError(401, "x"))).toContain("سجّل دخول");
    expect(errorMessage(new ApiError(403, "x"))).toContain("مش مسموح");
    expect(errorMessage(new ApiError(404, "x"))).toContain("مش لاقيين");
    expect(errorMessage(new ApiError(429, "x"))).toContain("محاولات");
    expect(errorMessage(new ApiError(503, "x"))).toContain("مشكلة");
    expect(errorMessage(new TypeError("Failed to fetch"))).toContain("نوصل");
  });
});
