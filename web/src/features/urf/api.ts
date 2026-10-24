// TASK 11 (DECISIONS v1.15, B-11 / S-06): /api/panel/urf. docs/panel-api.md.
import { api } from "../../shared/api/client";

export type Show = "open" | "done";

export interface UrfCase {
  id: string;
  kind: "marriage" | "divorce";
  state: string;
  name: string | null;
  wa_id: string;
  created_at: string;
  husband_age: number | null;
  wife_age: number | null;
  wife_past: string | null;
  guardian: string | null;
  witnesses: string | null;
  has_copy: boolean | null;
  agreed: boolean | null;
  children: boolean | null;
  documents: string;
  files: number;
  appointment_id: string | null;
  start: string | null;
  appointment: string | null;
  payment: string | null;
  decided_by: string | null;
  decided_at: string | null;
  notice: "pending" | "sent" | "failed" | null;
}

export const urf = {
  list: (show: Show) => api<UrfCase[]>("GET", `/urf?show=${show}`),
  review: (id: string, decision: "approved" | "rejected") => api<void>("POST", `/urf/${id}/review`, { decision }),
};

/** A document the client sent (an ID card), opened in a new tab (never cached, every opening recorded). */
export const fileUrl = (id: string, index: number): string => `/api/panel/urf/${id}/files/${index}`;
