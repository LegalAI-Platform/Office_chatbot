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
| `HazemResponder` | `human_request` whose words ask for a number (the lawyer's own: «رقم» with «الأستاذ / المستشار / الشخصي…», never «المكتب»; his own masked number does not count) → `no_personal_number` with «خدمات المكتب» / «أكلم حد من المكتب»: no handoff, no task (the second button hands him over) |
| Migration `0049_handoff_waiting` | `handoff_waiting`, `no_personal_number`, `human_request` v2 («تمام، بلّغت المكتب… وحد من المكتب هيرد على حضرتك هنا.») |
| Writer `write-v3` | `from_facts`: a `service_detail` question «حقايق المكتب» answer («هقابل المستشار سعد نفسه؟») is answered from them and its `service_question` task is dropped; the answer and «هوصّله للمكتب» together are refused. The owner: «اعمل كده ونفذه» |
| DECISIONS | v1.48 (on top of Mohamed Emad's v1.47) |

## Results
| Check | Result |
|---|---|
| `backend-test` | 836 passed (1 skipped) |
| `quality-eval` (quick) | 15 conversations 90.4%, then 12 on the facts 90%, nothing forbidden; «مع سعد نفسه؟» 40–80% → 100% |

## Not done / needs
- The texts are the ones the owner approved in chat on 2026-11-02 («نفذ»).
- A woman's long message about Saad himself still got the v1.46 text once (the written reply was refused); worth a look in the next full run.
- The conversation on ***89 is still in `human_handoff` on the server: a staff member resolves it in Chatwoot, or B-12's
  12 hours hand it back.
