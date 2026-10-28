// TASK 10 (DECISIONS v1.14, B-16 / B-17 / S-07): /api/panel/services. docs/panel-api.md.
import { api, putToStorage } from "../../shared/api/client";

export type Show = "open" | "done";

export interface ServiceOrder {
  id: string;
  kind: string;
  state: string;
  name: string | null;
  wa_id: string;
  created_at: string;
  files: number;
  price: number | null;
  price_exception: boolean;
  due_on: string | null;
  delivery: "file" | "call" | null;
  price_until: string | null;
  payment: string | null;
  file_name: string | null;
  delivered_at: string | null;
  closed_reason: string | null;
  appointment_id: string | null;
  notice: "pending" | "sent" | "failed" | null;
  /** v1.28: the failed send's code (OUTSIDE_24H_WINDOW, META_ERROR, …). */
  notice_error: string | null;
}

export interface Upload { url: string; key: string; content_type: string }

export const services = {
  list: (show: Show) => api<ServiceOrder[]>("GET", `/services?show=${show}`),
  price: (id: string, body: { price: number; exception: boolean; due_on: string; delivery: string }) =>
    api<void>("POST", `/services/${id}/price`, body),
  upload: (id: string, filename: string) => api<Upload>("POST", `/services/${id}/upload`, { filename }),
  deliver: (id: string, key?: string, filename?: string) => api<void>("POST", `/services/${id}/deliver`, { key, filename }),
  resend: (id: string) => api<void>("POST", `/services/${id}/resend`),
  close: (id: string) => api<void>("POST", `/services/${id}/close`),
  opinion: (appointmentId: string, body: { due_on: string; delivery: string }) =>
    api<void>("POST", `/booking/appointments/${appointmentId}/opinion`, body),
};

/** A document the client sent, opened in a new tab (never cached, every opening recorded). */
export const fileUrl = (id: string, index: number): string => `/api/panel/services/${id}/files/${index}`;

/**
 * B-16.6 (I1): the file goes from the browser straight to storage with the link the API signed (through the
 * panel's own nginx), then the API is told where it is. The API never receives the file.
 */
export async function deliverFile(id: string, file: File): Promise<void> {
  const up = await services.upload(id, file.name);
  await putToStorage(up.url, file, up.content_type);
  await services.deliver(id, up.key, file.name);
}

/** B-10: the range of each kind from the panel's price list (TASK 06); outside it, only as a confirmed exception. */
export function inRange(kind: string, price: number, p: Record<string, number>): boolean {
  if (kind === "document_review") return price >= p["price.review_min"]! && price <= p["price.review_max"]!;
  if (kind === "contract_drafting") return price >= p["price.drafting_from"]!;
  if (kind === "formal_notice") return price === p["price.formal_notice"];
  return price === p["price.written_opinion"];
}

/** The price a pricing form starts from: the bottom of the kind's range. */
export function startPrice(kind: string, p: Record<string, number>): number {
  if (kind === "document_review") return p["price.review_min"]!;
  if (kind === "contract_drafting") return p["price.drafting_from"]!;
  return kind === "formal_notice" ? p["price.formal_notice"]! : p["price.written_opinion"]!;
}

export const ACCEPT = ".pdf,.doc,.docx";
