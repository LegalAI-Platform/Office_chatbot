# 2026-11-03 — v1.49: the urgent call in the offer, and a normal phone call from the office's number (B-23)

## Why
The owner, on the test chats: «عايز ازود فيها ان احنا عندنا استشاره عاجله وانها بتبقي في خلال دقايق»، and on the call-app
question «عايز اضيف ان ممكن نكلمه في ميعاده عادي بس مكالمه هاتفيه»؛ «هيتصل من رقم المكتب». The texts were agreed in chat
(the night one reworded by the owner: «نوضح انها خلال مواعيد العمل»), then «نفذ».

## What changed
| Part | What |
|---|---|
| The offer | `consultation_offer_urgent_day` while the panel's hours are open («…خلال دقايق لو فاضي»), `consultation_offer_urgent_night` before a morning opening («…أول ما مواعيد العمل تبدأ الساعة {{opens}} الصبح»); once in 24 hours (`SituationFacts.UrgentOffered`), then the plain offer. A day off or an opening after noon: the plain offer |
| Buttons | «🚨 استشارة عاجلة» (`btn_book_urgent`, the urgent row) third, in the consultation and case offers, when the urgent call is on |
| The call | `CallApp.Phone` («مكالمة عادية», `bk:v:ph`): «📞 مكالمة عادية» for an Egyptian or hidden number, never abroad; `ask_call_app_phone` with it; four apps (Meet too) go as a list (`btn_apps_list`). The calendar, the reminders and the confirmations say «مكالمة تليفون عادية من رقم المكتب على نفس الرقم ده» |
| Safety | the new choices are used only when all their texts are approved (a fallback or a missing title drops every button); one `OfferKeys` check for the four offers (rule 2, `OfferWouldStack`, `KeepTheStep`) |
| Writer | purposes for the two offers, their prices and hour locked; the length limit is the approved text's + 120 when it is longer (the answer to his question fits) |
| Migration `0050` | the `call_app` check with `phone`, six texts, `office_facts` with the normal call |
| Panel | «مكالمة عادية» in the labels and the home page |
| Judge | `judge-facts.md`: the normal call and the urgent line are the office's policy |

## Results
| Check | Result |
|---|---|
| `backend-test` | 843 passed (1 skipped); new: `Task49OfferAndPhoneTests` |
| `web-test` | 72/72, build OK |
| `quality-eval`, 30 booking/urgent conversations | 88% (the same 30 in v1.45-e: 89.2%), nothing forbidden; every-mark-2 replies 35% → 50% after widening the length limit (the first run, before it: 83.6%) |

## Not done / needs
- Postponed by the owner: the AI reads «رقم المستشار الشخصي» in any wording (instead of the words rule).
- «مرة في اليوم» is tested by the store query only in integration; a unit test cannot set it.
