# 2026-11-07 — TASK 21 / v1.90: a client's question is never a wrong answer

Ahmed Amin's session (3omdael3ntel). Plan: `TASK-21-prompt.md`. The owner said «نفذ» and chose the wider scope: the Urfi case +
the booking + the name / number. Branch `v1.90-question-keeps-step`, from `main` at v1.89. Not committed yet.

## Why

The test server's chat with Ahmed Amin (***89, 2026-11-07, `hazem.urf_case_said` on) asked one question three times: «طب هو لازم
يكون في شهود؟ ولا ممكن نعمل العقد بينا إحنا الاتنين بس؟». It never got an answer:
- the first time it got «اكتبلي السن بالأرقام»;
- the second time the code read «77و90» alone and dropped the question;
- the third time it got «المستشار هيوضح».

Reading the code showed the same gap in the booking: an answered question under the times counts as a repeat. A question asked
instead of the name or number lets the picked time go.

## What was done (all behind `hazem.urf_case_said`; switch off = v1.81)

| # | change | where |
|---|---|---|
| A | The code reads the ages alone only when the message is the ages (`UrfAges.IsOnly`). More words go to the AI. If the AI falls back, the code's ages are taken as before. | `Urf.cs`, `HazemResponder` (the ages fast path) |
| B | `witnesses: none`, for asked only. `UrfRules.Try` returns `WitnessesNeeded`, answered with the new text `urf_witnesses_needed`, then the question waiting. A said «none» is ignored. | `classify-v33`, `Urf.cs`, `HazemResponder.AnswerAsked`, migration 0070 |
| C | New AI field `asks`, in the switch-on schema only. In the Urfi flow, a question that nothing answers gets `urf_question_later` plus the question waiting. It is never «again», and never goes to the office. A greeting no longer counts as an answer for v1.89. | `OpenAiIntentClassifier`, `Understanding.cs`, `HazemResponder.AfterUrf` |
| D | The booking: under the times, a question answered in the same reply brings the times back as they are. There is no «علشان نكمّل الحجز…» and it is not counted as a repeat. | `HazemResponder.KeepTheStep` |
| E | The name / number: a question about the office, the price or the law is answered and the picked time keeps waiting. The same ask comes back afterwards and is not counted as one more ask. | `HazemResponder.AnsweredKeepingPick` |

- **Docs:** DECISIONS v1.90, plus the CLAUDE.md and START-HERE pointers and the START-HERE row 1.90.
- **The eval set:** 5 conversations in `docs/ai/conversations.jsonl` (now 236):
  - `urf-q-witnesses-none`
  - `urf-q-ages-and-question`
  - `urf-q-no-witnesses-later`
  - `urf-q-unanswered-twice`
  - `bk-q-under-times`

## Results

- **Unit tests:** `Task90QuestionKeepsStepTests` has 18 cases, including switch-off checks for the Urfi case, the booking and the
  name.
- **`backend-test`:** 1,076 passed, 1 skipped (the paid eval).
- **The local database:** `migrate up` applied 0070. `healthcheck` gave 159 passed, 0 failed.

## Not done / not verified

- **The real-AI eval was not run.** The Anthropic key in `.env` is refused. The owner enters a working one, then runs:
  - `QUALITY_CASE_SAID=1 bash scripts/linux/quality-eval.sh v190 <the 25 urf-* ids + the 5 new>` (about $2–3).
- **`hazem-e2e` and the sweep on a fake number were not run.** The local `api` container still runs the old image.
- **Whether the real AI fills `asks` and `witnesses: none` as the prompt says is not checked.** Only the eval or the sweep shows it.
- **Coordinate with Mohamed Emad before merging.** v1.85–v1.89 are his work in the same files.
