// The office's appointments (TASK 08/09, /api/panel/booking/appointments). Shared because the home
// screen lists today's and the coming days' bookings next to the booking page itself.
import { useQuery } from "@tanstack/react-query";
import { cairoDate } from "../lib/time";
import { api } from "./client";

export interface Appointment {
  id: string; service: string; start: string; end: string; state: string; name: string | null; wa_id: string;
  matter_type: string | null; reschedules: number; calendar: string; hold_until: string | null; payment: string | null;
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
