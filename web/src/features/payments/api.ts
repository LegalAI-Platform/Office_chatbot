// TASK 09 (DECISIONS v1.13, B-05 / B-22 / S-04): /api/panel/payments. docs/panel-api.md.
import { api } from "../../shared/api/client";

export type Show = "review" | "refunds" | "all";

export interface Payment {
  appointment_id: string;
  service: string;
  start: string;
  appointment: string;
  name: string | null;
  wa_id: string;
  state: string;
  amount_due: number;
  amount_received: number | null;
  proof_at: string | null;
  receipts: number;
  duplicate: boolean;
  reviewed_by: string | null;
  reviewed_at: string | null;
  refund_due: number | null;
  refund_reason: string | null;
  refunded_by: string | null;
  refunded_at: string | null;
  refund_method: string | null;
}

export type Review = "verified" | "partial" | "not_received";

export const payments = {
  list: (show: Show) => api<Payment[]>("GET", `/payments?show=${show}`),
  review: (id: string, state: Review, received?: number) => api<void>("POST", `/payments/${id}/review`, { state, received }),
  refunded: (id: string, method: string, note?: string) => api<void>("POST", `/payments/${id}/refunded`, { method, note }),
};

/** A receipt image, opened in a new tab like a client's file (never cached, every opening recorded). */
export const receiptUrl = (id: string, index: number): string => `/api/panel/payments/${id}/receipts/${index}`;

/** «1,000». */
export const pounds = (n: number): string => n.toLocaleString("en-US");
