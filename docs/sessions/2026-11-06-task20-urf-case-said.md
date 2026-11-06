# 2026-11-06 — TASK 20: what the client says about his Urfi case, one general way (v1.84)

## Why
- **The fixes so far:** v1.82 and v1.83 added one thing for the guardian per failed chat (`dropped`, `urf_guardian_asked`,
  classify-v29).
- **The owner:** «مش عايز احل كيس كيس، عايز حلول عامه». He approved `TASK-20-prompt.md` and its texts.

## The switch (the owner: «مش عايز اعمل حاجه تبووظ الشغل القديم»)
- **Everything below sits behind `hazem.urf_case_said`** (migration 0067, off by default). It is switched in the panel:
  «أدوات التجربة» → «طريقة العرفي الجديدة», audited as `testing.urf_case_said`.
- **Off** is the Urfi case exactly as v1.81, Mohamed's last version:
  - `classify-v27`, and v1.81's schema field for field (a unit test pins its property list);
  - the AI's 15 seconds;
  - none of v1.82–v1.84's code paths: the confirm question, the «said back» on a reopened refusal, the «لأ» button, the asked
    items, a question with a number at the ages step;
  - the v1.60–v1.81 Urfi tests are back exactly as they were and pass with the switch off.
- **On** (with `hazem.understanding_v2`): `classify-v30` and its schema, 30 seconds, and what follows.
- **Outside the switch:** n8n's wait for `/api/hazem/turns` is 75 seconds (was 30). It changes no reply; a slow turn is no
  longer cut and retried.
- **The quality runner** runs either way: `QUALITY_CASE_SAID=1` (or `-CaseSaid`) runs it on, so the same conversations can be
  compared off and on.

## Done (with the switch on)
- **classify-v30:** one list, `case_said`, for the whole Urfi case. Each item has a field (`wife_past`, `guardian`,
  `witnesses`, `ages`, `has_copy`, `agreed`, `children`), a value and a kind (`said` / `asked` / `withdrawn`), in his order.
  - It is filled whatever the intent is, and also with buttons waiting.
  - The five old Urfi fields, and `urf_guardian_asked`, are gone from the prompt and the schema.
- **Schema:** the Anthropic copy now converts the nullable fields at every level, so the array items are covered too.
- **`UrfFacts.Of`:** the latest item per field.
- **`UrfRules.ApplyFacts`:** takes `withdrawn` for every field; `ChangesBooked` does too.
- **`UrfRules.Try`:** a question is tried on a copy of the case through `Settle`, the same B-11 rules. It returns can /
  cannot / the guardian confirm.
- **`HazemResponder.AnswerAsked`** (replaces `AskGuardianBack`):
  - what he said with the question is taken first;
  - then the question is answered from the rules, never sent to the office;
  - at a held time it is only answered.
- **A typed question with a number at the ages step** («لو هي 17 سنة ينفع؟») goes to the AI, not to the code's age reading.
- **Migration 0067:** `urf_option_can`, `urf_guardian_cannot`, `urf_age_cannot`, word for word as approved.
  - `{{who}}` is his own word only when it is in his message, else «القريب ده».
- **Time budgets:**
  - the AI's budget per turn is 30 seconds with the switch on (15 off);
  - n8n's wait for `/api/hazem/turns` is 75 seconds (was 30), so a long turn is not cut and retried.
- **`QualityEvalTests`:** `hazem.understanding_v2` and `hazem.info_writer` are on, as on the test server. Before this they
  were off in the runner, so it did not test the server's Hazem.
- **18 new Urfi conversations** in `docs/ai/conversations.jsonl` (`urf-*`), 231 in all.

## Results
- `dotnet build -warnaserror`: passes.
- **Unit tests:** all 816 pass.
  - 18 are new in `Task84UrfCaseSaidTests` (every kind and field, the latest wins, two in one turn, the texts off).
  - The v1.60–v1.81 Urfi tests are as they were (switch off); the v1.82 ones run with it on.
  - Five pin the switch: off is v1.81's prompt, schema, budget and replies.
- **Panel tests** (`web-test`): all 80 pass, one new (the switch card).
- **`backend-test`** (unit + integration, a throwaway database from the real migrations): 1,040 pass, 0 fail, 1 skipped (the
  quality suite). The approved-texts count is now 146, DECISIONS-v1.84 is in the text check, and the panel switch is tested
  (off by default, audited, staff refused).
- **The 25 Urfi conversations (quality-eval): not run yet.** The Anthropic key in the local `.env` is refused
  (`AnthropicUnauthorizedException`), so the run made no AI call. It needs the owner to enter a working key.

## Deviations
- **`{{who}}`:** the plan said «his own word». The code also checks that the word is in his message; if not, it uses «القريب ده».
- **«لو مفيش حد ينفع؟»:** answered with the approved policy text `urf_guardian_closed`, not a new text. The case stays open.
- **The divorce questions:** read in the list, but an asked one has no special answer yet. The buttons work as before.

## Still needs
- **The owner:** a working Anthropic key in the local `.env`. Then run the 25 Urfi conversations (about $2–3).
- **After the merge and deploy:** the sweep of Ahmed Amin's two chats on a fake number.
