// The office's appointments (TASK 08/09, /api/panel/booking/appointments). Shared because the home
// screen lists today's and the coming days' bookings next to the booking page itself.
import { useQuery } from "@tanstack/react-query";
import { cairoDate } from "../lib/time";
import { api } from "./client";

export interface Appointment {
  id: string; service: string; start: string; end: string; state: string; name: string | null; wa_id: string;
  matter_type: string | null; reschedules: number; calendar: string; hold_until: string | null; payment: string | null;
  /** TASK 12 (B-18): «هحضر ✅» for this time, and the reminders of this time. */
  attending: boolean; reminders: Reminder[];
}

export interface Reminder {
  kind: "day_before" | "two_hours"; state: "scheduled" | "sending" | "sent" | "failed" | "skipped" | "cancelled";
  due_at: string | null; sent_at: string | null; channel: "message" | "template" | null; skip_reason: string | null; error_code: string | null;
}

export const fetchAppointments = (from: string, to: string) =>
  api<Appointment[]>("GET", `/booking/appointments?from=${from}&to=${to}`);

/** From today (Cairo) for `days` days, refreshed every minute like the rest of the home screen. */
export function useAppointmentsAhead(days: number, enabled = true) {
  const now = new Date();
  const from = cairoDate(now);
  const to = cairoDate(new Date(now.getTime() + (days - 1) * 86_400_000));
  return useQuery({
    queryKey: ["appointments", from, to],
    queryFn: () => fetchAppointments(from, to),
    enabled,
    refetchInterval: 60_000,
    staleTime: 30_000,
  });
}
