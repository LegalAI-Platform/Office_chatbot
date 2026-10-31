// TASK 08 (DECISIONS v1.12, B-24): /api/panel/booking and /api/panel/office. docs/panel-api.md.
import { api } from "../../shared/api/client";

export interface Range { start: number; end: number }
export interface WeekDay { weekday: number; ranges: Range[] }
export interface DayException { id: string; day: string; kind: "closed_day" | "closed_hours" | "custom_hours"; start: number | null; end: number | null; reason: string | null; by: string | null }
export interface Day { day: string; status: "open" | "weekly_off" | "closed" | "custom_hours" | "partly_closed"; hours: Range[]; bookings: number; exceptions: DayException[] }
export interface Appointment {
  id: string; service: string; start: string; end: string; state: string; name: string | null; wa_id: string; matter_type: string | null; reschedules: number;
  calendar: string; hold_until: string | null; payment: string | null;
  /** TASK 12 (B-18): «هحضر ✅» for this time, and the reminders of this time. */
  attending: boolean; reminders: Reminder[];
  /** 1.25 (B-23): where the lawyer calls (a phone or video consultation). */
  call_app: "whatsapp" | "facetime" | "meet" | null;
}
export interface Reminder {
  kind: "day_before" | "two_hours"; state: "scheduled" | "sending" | "sent" | "failed" | "skipped" | "cancelled";
  due_at: string | null; sent_at: string | null; channel: "message" | "template" | null; skip_reason: string | null; error_code: string | null;
}
export interface Settings {
  enabled: boolean; buffer_minutes: number; min_notice_hours: number; horizon_days: number; max_per_day: number | null; slot_step_minutes: number; offer_count: number;
  calendar_id: string; calendar_key: boolean; texts_approved: boolean;
  hold_minutes: number; hold_short_minutes: number; hold_short_within_hours: number; hold_gap_minutes: number; urf_minutes: number;
}
export interface Office {
  instapay: string; wallet_number: string; wallet_provider: string; address: string; map_url: string;
  /** 1.25: the lawyer's fixed Google Meet link, and the office's phone in the office confirmation. */
  meet_link: string | null; phone: string | null;
  /** 1.33 (B-09): where the urgent call's alert goes (the lawyer), and the manager when the lawyer does not answer it. */
  lawyer_whatsapp: string | null; manager_whatsapp: string | null;
}
export interface Change { id: number; at: string; what: string; by: string | null; undone: boolean }
export interface Preview { calendar: "ok" | "unavailable" | "not_configured"; slots: string[] }
export interface Done { cancelled: number }

export const booking = {
  week: () => api<WeekDay[]>("GET", "/booking/week"),
  saveWeek: (days: WeekDay[], confirm = false) => api<Done>("PUT", "/booking/week", { days, confirm }),
  month: (month: string) => api<Day[]>("GET", `/booking/month?month=${month}`),
  addException: (body: { day: string; kind: string; start?: number; end?: number; reason?: string; confirm?: boolean }) =>
    api<Done>("POST", "/booking/exceptions", body),
  removeException: (id: string, confirm = false) => api<Done>("DELETE", `/booking/exceptions/${id}${confirm ? "?confirm=true" : ""}`),
  settings: () => api<Settings>("GET", "/booking/settings"),
  saveSettings: (s: Settings) => api<void>("PUT", "/booking/settings", s),
  changes: () => api<Change[]>("GET", "/booking/changes"),
  undo: (confirm = false) => api<Done>("POST", "/booking/undo", { confirm }),
  appointments: (from: string, to: string) => api<Appointment[]>("GET", `/booking/appointments?from=${from}&to=${to}`),
  mark: (id: string, state: string) => api<void>("POST", `/booking/appointments/${id}/state`, { state }),
  cancel: (id: string) => api<Done>("POST", `/booking/appointments/${id}/cancel`),
  /** TASK 10 (B-17): a written opinion after a consultation that took place: an order waiting for its 2,000. */
  opinion: (id: string, dueOn: string, delivery: "file" | "call") =>
    api<void>("POST", `/booking/appointments/${id}/opinion`, { due_on: dueOn, delivery }),
  preview: (day: string, service: string) => api<Preview>("GET", `/booking/preview?day=${day}&service=${service}`),
  office: () => api<Office>("GET", "/office"),
  saveOffice: (o: Office) => api<void>("PUT", "/office", o),
};

/** 600 → «10:00»; 1440 → «24:00» (midnight at the end of the day). */
export const hhmm = (m: number): string => `${String(Math.floor(m / 60)).padStart(2, "0")}:${String(m % 60).padStart(2, "0")}`;

/** Every 15 minutes of a day, 00:00 to 24:00, for the time pickers. */
export const TIMES: number[] = Array.from({ length: 97 }, (_, i) => i * 15);

/** «2026-11»: the month of a «yyyy-mm-dd» date. */
export const monthOf = (day: string): string => day.slice(0, 7);

export function shiftMonth(month: string, by: number): string {
  const [y, m] = month.split("-").map(Number) as [number, number];
  const d = new Date(Date.UTC(y, m - 1 + by, 1));
  return `${d.getUTCFullYear()}-${String(d.getUTCMonth() + 1).padStart(2, "0")}`;
}

/** The Cairo date of an instant, «yyyy-mm-dd». */
export const cairoDate = (d: Date): string =>
  new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Cairo", year: "numeric", month: "2-digit", day: "2-digit" }).format(d);
