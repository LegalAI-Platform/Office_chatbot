# 2026-11-03 — TASK 19 / v1.50: a passed appointment nobody marked is marked «خلص» by itself

## Why
`completed` / `no_show` came only from a person in the panel. Unmarked, an appointment stayed `confirmed`: no rating, and
Hazem could still treat it as to come (***89, 2026-11-01). The owner: «"خلص" تتعلّم لوحدها لو عدّى على الميعاد وقت معيّن…
ويبقى ممكن يتعدّل بعدها لو العميل ماحضرش»؛ on the plan (`TASK-19-prompt.md`): «نفذ TASK 19».

## What changed
| Part | What |
|---|---|
| Migration `0051_auto_complete` | `appointments.completed_by` (`staff` / `auto`; old completed rows `staff`), `booking.auto_complete_hours` = 3, the task type `appointment_unmarked`, the function `hazem.auto_complete_appointments(now, hours)` |
| Worker (`BookingNotices`, every minute) | marks every confirmed appointment that ended N hours ago with no payment, or a verified one; for one still under review it opens `appointment_unmarked`, once (key `booking:<id>:unmarked`). A move shifts the same row, so N counts from the new time |
| `PanelBooking.MarkAsync` | a staff mark records `staff`; an automatic `completed` may become `no_show` while its rating has not gone out (its waiting rating is removed; audit `appointment.auto_corrected`) |
| `RatingNotices` | after an automatic mark the rating is due at the next 10:00 Cairo, and at least 2 hours later (`RatingRules.DueAfterAuto`) |
| Panel | «خلص (تلقائي)»; «ماحضرش» on it while correctable; the hours in the booking settings («"خلص" لوحده»); the task's label |
| DECISIONS | v1.50 (S-03, B-26) |

## Results
| Check | Result |
|---|---|
| `backend-test` | 850 passed (1 skipped); TASK 19's 7 integration tests: due / not yet / no payment / under review once / moved / off / correction / rating times |
| `healthcheck` | 159 passed, 0 failed |
| `web-test` | 72/72, build OK |

## Deviation from the plan
- «رجّعه لـ متأكد» to move it afterwards was left out: back in `confirmed` with its end passed, the worker would mark it
  again within a minute. The correction is to «ماحضرش» only. A time moved by phone must be moved in the panel first.

## Not done / needs
- `quality-eval` not run: no reply changed (a completed appointment was already no open item, B-28).
