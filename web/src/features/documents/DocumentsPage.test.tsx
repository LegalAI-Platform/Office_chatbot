import { QueryClientProvider } from "@tanstack/react-query";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import { makeQueryClient } from "../../app/App";
import type { DocumentFile, DocumentPage } from "./api";
import { DocumentsPage, fileContext, fileTitle } from "./DocumentsPage";

// v1.110 (the owner: «…/documents شوف دي عاوز تعملها» … «اعمل كله مع بعض»)
const picture: DocumentFile = { source: "message", id: "turn1", index: 1, kind: "image", mime: "image/jpeg", name: null, received_at: "2026-11-14T08:00:00Z", task_id: "t1" };
const contract: DocumentFile = { source: "service", id: "f1", index: 0, kind: "document", mime: "application/pdf", name: "عقد إيجار.pdf", received_at: "2026-11-13T08:00:00Z", task_id: null };
const page: DocumentPage = {
  clients: [{ id: "p1", name: "أحمد مصطفى", phone_last3: "713", count: 2, latest_at: "2026-11-14T08:00:00Z", files: [picture, contract] }],
  total: 1, page: 1, page_size: 20,
};

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("«المستندات»", () => {
  it("names each file and the page it came from", () => {
    expect(fileTitle(picture)).toBe("صورة");
    expect(fileTitle(contract)).toBe("عقد إيجار.pdf");
    expect(fileTitle({ ...contract, name: null })).toBe("ملف PDF");
    expect(fileContext(picture)).toEqual({ label: "المهمة", to: "/tasks/t1" });
    expect(fileContext({ ...picture, task_id: null })).toBeNull();
    expect(fileContext(contract)).toEqual({ label: "طلبات الخدمات", to: "/services" });
  });

  it("lists the clients' files, opens each through the panel, and asks again for one kind, the search in the body", async () => {
    const bodies: unknown[] = [];
    vi.spyOn(globalThis, "fetch").mockImplementation(async (url, init) => {
      const path = String(url);
      if (path.endsWith("/auth/csrf")) return Response.json({ token: "t" });
      if (path.endsWith("/documents")) {
        bodies.push(JSON.parse(String(init?.body)));
        return Response.json(page);
      }
      return Response.json({ error: "not_found" }, { status: 404 });
    });
    const router = createMemoryRouter([{ path: "*", element: <DocumentsPage /> }], { initialEntries: ["/documents"] });
    render(<QueryClientProvider client={makeQueryClient()}><RouterProvider router={router} /></QueryClientProvider>);

    expect(await screen.findByRole("heading", { level: 2, name: "أحمد مصطفى" })).toBeTruthy();
    expect(screen.getByText("رقمه ينتهي بـ 713")).toBeTruthy();
    expect(screen.getByRole("link", { name: "صورة" }).getAttribute("href")).toBe("/api/panel/documents/message/turn1/1");
    expect(screen.getByRole("link", { name: "عقد إيجار.pdf" }).getAttribute("target")).toBe("_blank");
    expect(screen.getByRole("link", { name: "المهمة" }).getAttribute("href")).toBe("/tasks/t1");
    expect(bodies[0]).toEqual({ q: null, kind: null, source: null, page: 1 });

    fireEvent.click(screen.getByRole("tab", { name: "المستندات" }));
    await waitFor(() => expect(bodies).toContainEqual({ q: null, kind: "document", source: null, page: 1 }));
    fireEvent.change(screen.getByRole("searchbox", { name: "ابحث في المستندات" }), { target: { value: "٧١٣" } });
    await waitFor(() => expect(bodies).toContainEqual({ q: "٧١٣", kind: "document", source: null, page: 1 }));
    expect(router.state.location.search).not.toContain("٧١٣"); // never in the address
  });
});
