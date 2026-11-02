# 2026-11-02 — v1.48: the handoff says it is done, and the client is never left in silence (B-12)

## Why
The owner's test on ***89 (2026-11-02, 03:55–04:07, the test server):
- 04:02 «لو عملت استشاره الاستاذ سعد بنفسه الي هيقابلني؟»: the written offer added «سؤال حضرتك مهم، هوصّله للمكتب، ويردوا على
  حضرتك هنا» on its own: no task was made, nobody was told.
- 04:05 «ممكن رقم الاستاذ الشخصي» was read `human_request`: the conversation went to `human_handoff`, and the written reply
  ended «… وهيردوا عليك هنا. تمام كده؟», as if nothing had happened yet.
- 04:06 and 04:07 «هل مبلغ الاستشاره بيتخصم من الاتعاب» got nothing: Hazem is silent in `human_handoff` (B-12) and no staff
  member was awake. The owner: «كنت بكلم البوت وسكت فجأة»؛ on the fix: «نفذ».

## What changed
| Part | What |
|---|---|
| Writer | `human_request`'s purpose is a statement; a written handoff reply with a question mark is refused (`asked_back`). «هوصّله / هوصله / هبلّغ / هبلغ / هيردوا / هيرد عليك» are promises the writer may not make unless the approved text says them |
| `HazemResponder` | in `human_handoff`, before any staff message, the client's first message gets `handoff_waiting` once; then silence again (until the office answers or B-12's 12 hours) |
| `HazemResponder` | `human_request` whose words ask for a number («رقم»، «نمرة»; his own masked number does not count) → `no_personal_number` with «خدمات المكتب» / «أكلم حد من المكتب»: no handoff, no task (the second button hands him over) |
| Migration `0049_handoff_waiting` | `handoff_waiting`, `no_personal_number`, `human_request` v2 («تمام، بلّغت المكتب… وحد من المكتب هيرد على حضرتك هنا.») |
| DECISIONS | v1.48 (on top of Mohamed Emad's v1.47) |

## Results
| Check | Result |
|---|---|
| Unit tests | 622/622 (new: the waiting reply once, the personal number, the guard) |
| `backend-test` (integration) | see the PR's CI (Docker Desktop was not running on this machine at first) |
| `quality-eval` | to run before the deploy (B-30) |

## Not done / needs
- The texts are the ones the owner approved in chat on 2026-11-02 («نفذ»).
- The conversation on ***89 is still in `human_handoff` on the server: a staff member resolves it in Chatwoot, or B-12's
  12 hours hand it back.
