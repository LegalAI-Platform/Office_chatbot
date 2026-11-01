# v1.42 report — 2026-11-01 — the urgent call at night, and one urgent task

From Abu El-Leil's conversation on the test number ***90 (2026-11-01, 01:28). The owner: «مش معترض… نفذه», and on the new
text «اه». Branch `urgent-night-fixes` from `main` (`be730d1`, TASK 17 live). The two fixes were written on 2026-11-01 before
TASK 17 and kept in `git stash` until now (the «1.40» of DECISIONS v1.41; there is no 1.40 document).

## 1. What was wrong

- At 01:30 the call was held at 10:00, the same date: the code compared dates only, so he heard no «الصبح» before paying,
  and after paying «المستشار في مكالمة دلوقتي» (urgent_after) — the lawyer was asleep.
- Every «مستعجل» after paying added an `urgent_request` task, its bell and its e-mail: three in three minutes.

## 2. What was done

| Part | What |
|---|---|
| The night | `Availability.IsOpen` and `BookingDesk.OpenNowAsync`: the panel's hours closed now (or another day) = later. Before paying: `urgent_night`. After the receipt: the new `urgent_later` «وصلت صورة التحويل ✅. المستشار هيكلم حضرتك على الرقم ده [اليوم] الساعة [الساعة].» (booking_paid promised a confirmation that never comes for an urgent call) |
| One task | the situation reply to «مستعجل» from a paid client adds no task; the receipt's task is the one |
| Migration 0043 | the text `urgent_later` |

## 3. Results

| Check | Result |
|---|---|
| `backend-test` | **797/797** |
| `healthcheck` | 159/0, 7 expected warnings |
| New tests | at 01:30: urgent_night before paying, urgent_later after («النهارده الساعة 10:00 ص»), one task; «خليه يكلمني ضروري ناو» after paying makes no second task; the call another day gets urgent_later |

## 4. Next

Commit, PR, merge, deploy (migration 0043; no n8n change).
