# TASK 21 — No Urfi question goes unanswered: the whole message is read, and «without it» is a question too (DECISIONS v1.90)

**Status: approved («نفذ», scope: the Urfi case + the booking and the name / number, 2026-11-07) and built as v1.90** on branch `v1.90-question-keeps-step`, from `main` (v1.89). Behind the switch `hazem.urf_case_said`, as
TASK 20 and v1.85–v1.89 (off = the Urfi case exactly as v1.81).

**Coordinate first:** v1.85–v1.89 are Mohamed's work in the same code (`HazemResponder`, `Urf.cs`, `classify-v32`). Agree
with him who builds this before starting, so we don't both work on the same files.

## 1. Why: Ahmed Amin's chat of 2026-11-07, with the switch on

| # | the client | Hazem | what went wrong |
|---|---|---|---|
| 1 | «عايز أسأل عن موضوع جواز عرفي» | السلام + «سن الزوج كام، وسن الزوجة كام؟» | ✅ |
| 2 | «مرتبط ببنت… عايز أعرف الإجراءات» + **«طب هو لازم يكون في شهود؟ ولا ممكن نعمل العقد بينا إحنا الاتنين بس؟»** | «من فضلك اكتبلي السن بالأرقام…» (`urf_ages_again`) | his question was not answered at all |
| 3 | «77و90» + **the same question** | «دي أول جوازة للزوجة…؟» | the question was dropped again |
| 4 | **the same question** | «المستشار هيوضح النقطة دي لحضرتك في الميعاد» + the wife's-past question | a weak answer: the office's own rule answers it |

He asked one clear question three times and never got an answer. The same will happen tomorrow with:
- «30 و 25، وينفع الشهود من عندي؟»;
- «من غير شهود ينفع؟»;
- «لو مفيش شهود خالص؟».

Each one is the same three general gaps, not three cases.

### Gap A — the code reads the ages alone and throws the rest of the message away (turn 3)
- In the ages step, any message with numbers in it is read by the code without the AI.
- The test is only «not a question that is not two ages» (`HazemResponder.cs:365`, `UrfAges.Parse`). «77و90» plus a question
  parses as two ages, so the question never reaches the AI.
- This is **the same gap v1.85 closed for the buttons**: typed words that pick a button now keep everything the AI read in
  them. The ages were left out.

### Gap B — «without witnesses» has no value to be asked with (turns 2 and 4)
- `witnesses` has two values only: own / office.
- v1.86 made «is it needed?» the same question as «may we go without it?». That works for the guardian (`none`), but the
  witnesses have no `none`, so «بينا إحنا الاتنين بس» has nowhere to go.
- The AI then reads a general intent instead:
  - `service_by_name` in turn 2, which became «اكتبلي السن بالأرقام»;
  - `legal_question` in turn 4, which became «المستشار هيوضح».
- But the answer is the office's rule, not a legal opinion: the contract always has two witnesses, the office's (5,000) or his
  (4,000). It is the same as `urf_guardian_needed` (v1.86).

### Gap C — a question nothing answers is met with «repeat your answer» (turn 2)
- While an Urfi question waits, a message that is not its answer gets the «again» reply:
  - «اكتبلي السن بالأرقام…» for the ages;
  - «علشان نكمّل…» for the other questions.
- The second one in a row goes to the office (v1.46).
- That rule is right for an unclear answer, and wrong for a **question**. He did not fail to answer, he asked.
- v1.89 already says «an answered question is not a repeat». An unanswered question needs the same care.

## 2. The rules (switch on only)

### 2.1 The code reads an answer alone only when the message IS the answer (Gap A)
- **The ages step:** the code reads the message without the AI only when it is the two ages and nothing else, for example
  «30 و 25» or «الزوج 30 والزوجة 25».
- **Anything more** (a question, a second message in the same turn, any other words) goes to the AI. The AI reads the ages
  inside it as a `said` item, and every other item as well. Nothing is lost.
- **Same rule as v1.85**, so the ages and the buttons work one way. A button title or its number alone is still read by the
  code without the AI.
- **The test is the shape of the message, not a list of words:** once the ages are parsed, is anything other than numbers,
  the «و» between them, and the husband / wife words left in the message?

### 2.2 Every field may be asked «without it» when the office's rules have an answer for that (Gap B)
- `witnesses` gets one more value, **for `asked` only**: `none` (going without witnesses:
  «لازم يكون في شهود؟»، «ينفع من غير شهود؟»، «العقد بينا إحنا الاتنين بس»).
- It can never be `said`: «we have no witnesses» is not a case the office takes. The office brings them, and that is the
  question «الشهود هتجيبهم حضرتك، ولا المكتب يوفرهم؟».
- `UrfRules.Try` answers witnesses asked none with the rule, through a new answer kind `WitnessesNeeded`:
  1. the new approved text `urf_witnesses_needed` (2.4);
  2. then the question waiting, unchanged. The case does not change.
- **The prompt says it once for every field**, not per field: «a question whether something is needed is the question about
  going without it; use `none` where the field has it».
- **The fields with no «without»:** the wife's past, the ages and the divorce questions have none. Their «is it needed?»
  questions stay legal questions, as today.

### 2.3 A question nothing answers gets «later», never «again» (Gap C)
- While an Urfi question waits, a message the AI reads as asking something may go unanswered: no asked item answers it, and
  its intent has no reply of its own.
- That message gets `urf_question_later` («المستشار هيوضح النقطة دي لحضرتك في الميعاد.», an existing text), then the question
  waiting as it is.
- It is never `urf_ages_again` / «علشان نكمّل…», and it is not counted as a repeat (the v1.89 rule, made general: Hazem
  answered him).
- **What counts as asking is the AI's reading, not a «؟»:**
  - an asked item;
  - or `legal_question`, `service_detail`, `office_info`, `service_by_name`, when his words ask something.

  **To decide:** we may need one field, `asks` (true/false), so the code does not guess. See 5.3.
- **A message that neither answers nor asks** (a greeting, «تمام», words that are not his answer) keeps the v1.46 path:
  «again», then the office.

### 2.4 The new approved text (the owner approves the wording)

| key | text | when |
|---|---|---|
| `urf_witnesses_needed` (new) | أيوه، العقد عندنا لازم يكون فيه شاهدين: المكتب يوفّرهم، أو حضرتك تجيبهم. | witnesses asked none |

- This is the office's policy, not the law. It uses the same words as the witnesses question that comes later.
- It names no price: the price comes with the offer, as today.

### 2.5 Kept as today
- **Switch off:** exactly v1.81.
- **The rest of v1.84–v1.89:** a held or paid time; the corrections («تمام، عدّلت…»); the guardian left out; the quote check
  (an item whose words are not in his messages is not taken).
- **The approved texts go out word for word.**

## 3. The build

- **`HazemResponder.cs:365`:** `urfAges` reads alone only when `UrfAges.Parse` finds the two ages **and nothing else is
  left**, with the switch on. A new `UrfAges.IsOnly(text)` sits next to `Parse`, with its own unit tests.
- **`classify-v33`** (a copy of v32):
  - `witnesses` gets `none` (asked only);
  - the «needed = without it» sentence covers every field;
  - Ahmed Amin's examples go in, in his words;
  - `asks`, if 5.3 is approved.
- **Schema:** `witnesses` values `own`, `office`, `none`. The parser drops a `said` / `withdrawn` witnesses none.
- **`Urf.cs`:**
  - `Try`: `UrfStep.Witnesses when asked.Value == "none"` returns `UrfAskAnswer.WitnessesNeeded`;
  - `UrfAskAnswer.WitnessesNeeded`.
- **`HazemResponder.AnswerAsked`:** `WitnessesNeeded` → `urf_witnesses_needed`, then the question waiting.
- **`AfterUrf`:** an unanswered question (2.3) → `urf_question_later` + the question, with v1.89's «not a repeat».
- **Migration `0070_urf_witnesses_needed.sql`:** the text, after the owner approves it.

## 4. Done when

- **Unit tests:**
  - the ages alone (read by the code, no AI call);
  - the ages plus a question (to the AI: the ages taken AND the question answered);
  - witnesses asked none, at every step (the text, then the question waiting, nothing changed);
  - a said witnesses none is ignored;
  - an unanswered question at the ages step (later + the ages question, not «بالأرقام»);
  - an unanswered question twice in a row (not the office);
  - an unclear non-question twice (the office, as v1.46);
  - switch off: Ahmed Amin's 4 turns give exactly v1.81's replies.
- **Every existing test stays green.**
- **New conversations** in `docs/ai/conversations.jsonl`:
  - Ahmed Amin's chat as he typed it;
  - «30 و 25 وينفع الشهود من عندي؟»;
  - «من غير شهود ينفع؟» at the wife's-past step;
  - «لو مفيش شهود خالص؟» after the ages.

  Run them with the 25 Urfi conversations, `QUALITY_CASE_SAID=1`, those ids only (about $2–3).
  - **Done:** 0 forbidden, the new ones pass, the old ones do not drop.
  - This needs a working Anthropic key in `.env`, entered by the owner. The one there now is refused.
- **The sweep on a fake number:** Ahmed Amin's 4 turns, switch on. All 4 get an answer.
- `backend-test`, `healthcheck`, DECISIONS v1.90 and the session report.

## 5. Decisions for the owner (recommendation first)

1. **The code reads an answer alone only when the message is the answer.** Anything more goes to the AI, the same rule as
   v1.85 for the buttons.
2. **«Without it» is a question for every field that has an answer for it.** Today that adds `witnesses: none`, answered by
   the office's rule.
3. **The text `urf_witnesses_needed`**, as written or reworded.
4. **A question nothing answers gets «المستشار هيوضح…» and the question waiting, never «again» and never the office.**
   - Recommended: a small `asks` field from the AI, so the code does not guess from a «؟».
   - The other way: use the AI's intent only, with no new field. Cheaper, but `service_by_name` would sometimes be a
     question and sometimes not.
5. **Who builds it:** agree with Mohamed first. His v1.85–v1.89 are in the same files.
6. **Later, not in this task:** an upper age limit («77 و 90» passes today; the rules have a minimum only).
