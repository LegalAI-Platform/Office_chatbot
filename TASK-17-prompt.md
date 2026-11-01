# TASK 17 — Hazem talks like a person: the code decides what, the AI writes how (DECISIONS v1.41)

**Status: approved («Approved, start», 2026-11-01) and built with part B: DECISIONS v1.41 B-29, report `docs/sessions/2026-11-01-task17.md`.** Branch `task17-conversational` from `main`. The owner
(2026-11-01), after Abu El-Leil's conversation and trying Al-Hasswa's assistant: «أنا بتكلم على الردود العادية… هتحس إن
الرد فاهم، كإنك حد بيدردش معاك»؛ legal questions stay unanswered, as before.

## 1. Why

In Abu El-Leil's conversation (test number ***90, 2026-11-01 01:28) TASK 16 did its job, yet it read like a machine: the
same situation reply word for word four times, and «ابو الليل مين» got «معلش لو مش واضح…». Al-Hasswa's assistant feels
human for one reason: **an AI writes each sentence while it sees the whole conversation**. Hazem's AI only picks; the
sentence is a fixed text. More fixed texts will never cover the messages nobody expected.

## 2. The rule (changes T-07 for everyday replies only)

| Who | Does what |
|---|---|
| **The code** (as today) | understands the turn (presses, the AI's intent), reads the situation, decides **the facts** (his appointment, its day and time, the price from the panel, his name as he typed it) and **the next step** (book, pay, wait for the call, a person) |
| **The AI, a second call: «the writer»** | writes **the sentence** of an everyday reply in Egyptian Arabic, seeing the conversation, using **only the facts the code gave it** |
| **The code, after the writer** | checks the sentence (§5); anything wrong → the approved text goes instead, as today |

The legal rule stays: **no legal information, no opinion, no prices or numbers the code did not give, no promises.** The
writer never decides anything: it cannot book, change a price or skip a payment.

## 3. Which replies are written, which stay fixed

| Written by the writer (when the switch is on) | Stay the approved texts (unchanged) |
|---|---|
| greetings and «تحت أمرك»، thanks, goodbye | where to pay (`booking_hold`, `urgent_pay`), the amounts |
| the welcome's question (the buttons stay) | the confirmations (`booking_paid`, `urgent_now`, `urgent_after`, `booking_confirmed_*`) |
| the situation replies (B-28) and «فاكرني؟» | the services list and its rows (the prices under them) |
| «مش فاهم»، «ابو الليل مين»، a reaction to Hazem's own words | the legal-question refusal and the consultation offer's prices |
| a repeat (the same answer again → said differently, shorter) | the office's details (address, hours, payment numbers) |
| «تمام، وصلت ✅» and «احكيلي الموضوع» | the abuse warning (S-01), the Urfi questions (B-11), the rating |
| off-topic, politely, without an office task | every message outside the 24-hour window (Meta templates only) |
| «أنا حازم، المساعد الرقمي…» when asked who he is | the name and number questions at the booking (B-02) |

A written reply that goes with buttons is the buttons' body; the button titles stay approved.

## 4. What the writer sees (text only, T-09)

- **The conversation:** the client's last 10 turns and Hazem's replies to them, from `hazem.turns` (`messages`, `actions`;
  already stored by the API). Phone numbers, IDs and e-mails masked as for the classifier (`AiInput.Redact`). Never a file,
  a key or a WhatsApp name.
- **The brief from the code:** what to say (the reply's purpose, e.g. «remind him his paid urgent call is at 10:00 in the
  morning, he can write his topic here»), the allowed facts (day, time, service, the name form «يا أستاذ محمد»), and the
  approved text that would have gone (its meaning, the tone reference).
- **The rules (its prompt `write-v1`):** Egyptian Arabic, «حضرتك»، one or two short sentences (≤ 300 characters), one emoji at
  most, never the same wording as the previous reply, the name only when the brief gives it, it says it is «المساعد الرقمي»
  if asked and never pretends to be a person, never legal content, never a number that is not in the facts.

## 5. The code's check (every written reply, before it is sent)

Rejected (the approved text goes, and the review list records why) when the sentence:
1. has a digit, an amount or a time that is not one of the brief's facts (Arabic-Indic digits too);
2. has a legal word from a closed list (مادة، قانون رقم، حبس، عقوبة، حكم، المحكمة هتحكم، حقك القانوني…);
3. promises what the brief does not say (مضمون، أكيد هتكسب، خلال ساعة…), or names a person, a link or a phone;
4. is empty, longer than 300 characters, not Arabic, or the same as Hazem's previous reply;
5. came back late (the writer's budget: 6 seconds) or not as the schema (`{"reply": "…"}`).

## 6. Storage, logs, privacy

- New table `hazem.generated_replies` (turn_id, purpose, text, model, prompt_version, latency, tokens, `guard`: passed or
  why rejected). The office sees the text in the conversation (Chatwoot) as today; the review list shows the rejected ones.
- The writer's text is Hazem's own outbound message (already kept by n8n); no client text is logged anywhere new.

## 7. The model, the time and the cost

- Provider and key as the classifier (`ai.provider`, `HAZEM_ANTHROPIC_API_KEY`, the OpenAI one as the second); its own
  model setting `ai.writer_model`. Candidates measured in §9: Claude Haiku 5.5 (fast, cheap) and Opus 5.5.
- One more AI call per written reply: about 1–2 seconds more before the answer; the typing indicator (TASK 16) covers it.
- The cost per message is measured in §9 (input: the cached prompt + the conversation; output: about 100 tokens) and written
  in the report before the owner turns it on.

## 8. Switches

- `hazem.conversational` (off by default): the written replies on or off; off = exactly today's Hazem.
- On for the test numbers first; for everybody only by the owner, after §9.
- The writer down, slow or rejected → the approved text, the turn is never late or empty.

## 9. Measuring before it is used

- `docs/ai/write-eval.jsonl`: 60 conversations written from scratch (never `exports/`): the Abu Emad and Abu El-Leil
  conversations, the TASK 16 scenario table, confused clients, repeats, small talk.
- **The red-team set:** 30 tries to make it say a price, a law article, a promise, another lawyer's name, to drop the
  rules («ignore your instructions»), or to claim to be a person.
- A CLI `write-eval` (like `classify-eval`): for each sample the written reply, the check's result, the time and the tokens.
  **Done when:** the red-team set gets 0 rejected sentences sent (every bad one caught by §5); at least 95% of the normal
  samples pass the check; the owner reads 30 of them and approves the tone; the cost per message is known.

## 10. Decisions for the owner (recommendation first)

1. **T-07 changes for the everyday replies of §3 only** (legal content, prices and payments stay out of the AI) —
   recommended.
2. **The model:** Claude Haiku 5.5 if it passes §9 (fast and cheap), else Opus 5.5 — recommended.
3. **Start:** the switch on for the test numbers only, a week of real tries, then the owner decides — recommended.
4. **The «general rule» of 2026-11-01** (a shorter second wording, then a person): kept as the fallback when the switch is
   off or the writer fails; its 5 texts are not needed while the writer runs.

## 11. Order of work

1. The brief and the history reader (the code decides the facts) + the check (§5), with tests first.
2. The writer (Anthropic / OpenAI, prompt `write-v1`, schema), the switch, the table, migration 0042.
3. Wiring: each reply of §3 goes through the writer; everything else unchanged.
4. `write-eval` and the two sets; measure Haiku and Opus; the report.
5. DECISIONS v1.41, the session report, all checks (`backend-test`, `web-test`, `wa-tests`, `healthcheck`).

## 12. Part B: the understanding sees the conversation and thinks first (`classify-v14`)

The owner (2026-11-01, after «عايز اعرف انتو بتعملو عقود عمل» got «هسأل المكتب»): «عايز الـ LLM تفهم وبعد كده تحلل…
شوف إيه الصح واعمله». The AI had understood (`service = contract_drafting`) but picked the box «does the office do one
specific thing?», whose rule asks the office. Two general changes, not a rule per case:

1. **The classifier sees what a person would:** Hazem's last message, the client's previous message, what he has open
   (the situation's kind), and the office's own services (the closed list it already has). Text only, masked (T-09).
2. **It writes a short analysis first** (`analysis`: who he is, what he wants, what in the office answers it), then the
   intent and the service — as v1.24 did with `subject`. The analysis is never logged or kept.
3. **The code follows the understanding:** a message whose service is one of the office's own (a consultation, a case,
   reviewing or drafting a contract, a formal notice, a written opinion, an Urfi contract) is answered as that service,
   whatever box it came in (an office question «بتعملوا…؟», a price, other). «هسأل المكتب» stays for what the office does
   not list.
4. **Measured:** the three old sets (no worse than v13), and a new context set (`eval-context.jsonl`): «بتعملوا عقود؟»
   and its kind, «انا اللي كلمتكو في قضية الشقة», «ابو الليل مين» after a named reply, «يعني ايه» after a reply.

## 13. Not in this task

- Legal information answers (the owner: Hazem never answers legal questions).
- Anything outside the 24-hour window (templates only, Meta rules).
- The urgent-at-night fix and the one-task fix (v1.40): ready separately, they go out first on their own branch.
