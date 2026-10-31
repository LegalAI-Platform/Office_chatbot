# TASK 16 — Hazem answers the client's situation, not just his message (DECISIONS v1.39)

**Status: approved («شوف احسن حل واعمله», 2026-10-31) and built: DECISIONS v1.39 B-28, report `docs/sessions/2026-10-31-task16.md`.** The owner (2026-10-31), after re-reading the «ابو عماد العنتيل»
conversation: «ما انا مش عاوزك تحل كيس… عاوزك يكون في حل لكل المشاكل من هذا النوع»; then «الـ LLM هي اللي تفهم الأول»
(agreed), and «ما يحسسنيش إن هو شات بوت».

## 1. Why

In that conversation the urgent part was right (under 3 minutes, pay → lawyer's «هكلمه»). Six minutes later the same client
wrote again and Hazem:

| He wrote | Hazem | What was wrong |
|---|---|---|
| السلام عليكم | «أهلًا بحضرتك 👋 تحت أمرك، محتاج إيه؟» | forgot his paid urgent call; no name |
| انت فاكرني؟ | «أنا حازم، المساعد الرقمي…» | answered «who are you», not «do you remember me» |
| خدمات / اي خدمات المكتب | the practice areas + «استشارة بـ500 أو 1,000» (twice, word for word) | not the services list; the same reply repeated |
| عاوز احكي مشكلتي | «استشارة بالتليفون بـ500 أو في المكتب بـ1,000» | **offered a paid consultation to a client who had just paid** |

The cause is general: **every message is answered from its intent alone**, blind to what the client already has open, and
the AI can only pick intents that exist (no «show the services list»).

## 2. The design (the owner's rule: the LLM understands, the code decides)

| Who | Does what |
|---|---|
| **Code first** | button presses, greetings, a number, an age, a date (exact and cheap; as today) |
| **The LLM** | understands any free text and picks **one** choice from a closed list (T-07: it classifies, it never writes a reply) |
| **Code** | decides the reply from the choice **and the client's situation**, from approved texts only, and enforces the forbidden rules |

## 3. The six parts

### 3.1 The client's situation, read before every reply
One summary per turn: his name (as he gave it) and form of address; the newest open item, in this order: an urgent call
paid and waiting · a time held waiting for the transfer · a confirmed appointment still to come · a written service order
(waiting for papers / price / payment / in progress) · an Urfi case · nothing.
**Rule:** a general message (a greeting, «عايز استشارة», «أحكي مشكلتي», an unclear message, «فاكرني؟», «ميعادي امتى؟»)
from a client with an open item gets **his situation first**, never a new offer. A precise request (another service, a price,
cancel, a human) is answered as it is today.

### 3.2 Forbidden rules, enforced by code (each with its own test)
1. Never offer a paid consultation, or ask for a payment, while the client has one held, paid or waiting for a call.
2. Never send the same approved text twice in a row: the second time, the services list (or «تحب تكلم حد من المكتب؟»).
3. Never ask the same question a third time (name, number, topic).

### 3.3 More choices for the LLM (prompt `classify-v12`, measured on the eval sets before use)
- `show_services` — «خدمات»، «عندكم إيه؟»، «إيه اللي بتعملوه؟» → the services list (as the button).
- `tell_problem` — «عاوز احكي مشكلتي»، «عندي مشكلة» without its content → as the «أحكي مشكلتي» button.
- `my_status` — «فاكرني؟»، «ميعادي امتى؟»، «دفعت ولا لأ؟»، «المستشار هيكلمني امتى؟» → his situation (3.1).
- `name_gender` on the name: `male` / `female` / `unknown` (for 3.6), from the name the client typed only.

### 3.4 Conversations to review, flagged automatically
A panel list «محادثات محتاجة مراجعة» (manager and lawyer): a reply repeated, an offer after a payment (should never happen
after 3.2: the alarm that it did), «مش فاهم» / «؟؟؟» / «فاكرني» from the client, two fallbacks in a row, a client who left
after the price. Each one fixed becomes a test.

### 3.5 Scenario tests: every situation × every kind of message
Situations: new · held unpaid · paid urgent waiting · confirmed tomorrow · written service waiting for papers · Urfi case.
Messages: greeting · «عايز استشارة» · a price question · «أحكي مشكلتي» · unclear · «فاكرني؟». Each cell has its right reply
and checks the forbidden rules.

### 3.6 Hazem talks like a person (and stays honest)
- **By name, from the name the client typed** (never the WhatsApp name): «يا أستاذ محمد» / «يا أستاذة منى» from
  `name_gender`; a kunya as written («يا أبو عماد»); unknown → «يا فندم» / «حضرتك» without a name.
- **Not in every message:** the return welcome, the booking confirmed, «المستشار شاف طلبك», the situation replies.
- **2–3 approved wordings** for the replies that repeat most, chosen in turn (never the same twice running).
- **Acknowledge first:** «تمام»، «وصلت»، «فاهم حضرتك» before the answer, where it fits.
- **n8n:** the blue ticks (mark as read) and «بيكتب…» while the reply is prepared (Meta's typing indicator; to be checked on
  the test number).
- **Hazem still says he is «المساعد الرقمي»** in the welcome (v1.32): warm and polite, never pretending to be a person.

## 4. New texts for the owner's approval

`[النداء]` is «يا أستاذ محمد» / «يا أستاذة منى» / «يا أبو عماد», or empty when unknown (then the sentence reads without it).

**The situation replies (3.1)**
1. Urgent paid, waiting for the call:
   > أكيد [النداء] 👋 حضرتك عندك استشارة عاجلة مدفوعة، والمستشار هيكلم حضرتك على الرقم ده [خلال دقايق / حوالي الساعة 10:30]. لو حابب تكتب الموضوع باختصار، اكتبه هنا ويوصل للمستشار.
2. A time held, waiting for the transfer:
   > [النداء]، ميعاد حضرتك ([الخدمة]) [اليوم] الساعة [الساعة] ممسوك لحد الساعة [آخر وقت]، ومستني صورة التحويل بس.
3. A confirmed appointment to come:
   > أهلًا [النداء] 👋 ميعاد حضرتك ([الخدمة]) [اليوم] الساعة [الساعة] متأكد ✅. لو حابب تكتب الموضوع باختصار قبل الميعاد، اكتبه هنا.
4. A written service order:
   > أهلًا [النداء] 👋 طلب حضرتك ([الخدمة]) [الحالة: مستني الورق / الأستاذ بيراجعه ويحدد السعر / مستني التحويل / شغالين عليه]. لو عندك سؤال عنه اكتبه هنا.
5. The topic written for an open item («أحكي مشكلتي» then the story):
   > تمام [النداء]، وصلت ✅ وهتبقى قدام المستشار.

**The forbidden rule 2 (a repeat)**
6. > معلش لو مش واضح، تحب تشوف خدمات المكتب، ولا تكلم حد من المكتب؟  *(with both buttons)*

**More wordings (3.6), each with today's text kept as one of them**
7. «تحت أمرك، محتاج إيه؟» → also «اتفضل [النداء]، أقدر أساعد حضرتك في إيه؟» and «أنا معاك [النداء]، قولّي محتاج إيه.»
8. «تمام، وصلت ✅» → also «وصلت يا فندم ✅» and «تمام [النداء]، وصلت ✅».

## 5. Decisions for the owner (recommendation first)

1. **Approve or edit texts 1–8.**
2. **Address by name only when the gender is clear**; otherwise «يا فندم» — recommended (a wrong «أستاذ» to a woman is worse
   than no name).
3. **Who sees «محادثات محتاجة مراجعة»:** the manager and the lawyer — recommended.
4. **One task, built in this order:** 3.1 + 3.2 (the biggest effect) → 3.3 → 3.6 → 3.5 → 3.4.

## 6. Done when

- The «ابو عماد» conversation, replayed as a scenario test, gets: his situation on «السلام عليكم» and «فاكرني؟», the
  services list on «خدمات», «اكتب الموضوع هنا» on «عاوز احكي مشكلتي», and never a paid offer.
- Every cell of the scenario table passes; the forbidden rules have their own tests.
- `classify-v12` measured on the eval and hold-out sets, no worse than v11 on the old intents.
- All checks pass; the review list shows a seeded bad conversation.

## 7. Not in this task

The two e-mail fixes (urgent e-mail after the payment, no review task for an urgent call, v1.38) go out with this task
(same branch).
