# TASK 20 — What the client says about his Urfi case, read one general way (DECISIONS v1.84)

**Status: approved and built (2026-11-06), behind the switch `hazem.urf_case_said` (off = the Urfi case exactly as v1.81).** Branch from `main` (v1.83).

The owner (2026-11-06): «اللي بنعمله ده صح فعلا ولا ترقيع… مش عايز احل كيس كيس، عايز حلول عامه».

## 1. Why

**v1.82 and v1.83 were half a patch.** Each failed chat added one thing for the guardian only:
- `dropped` («اخوها مش هيجي»);
- `urf_guardian_asked` («ينفع ابوها؟»);
- classify-v29 («fill it even with the list waiting»);
- the next failure was two messages in one turn («اخوها مش هيجي / ابوها موجود ينفع؟»): the father was lost and the reply
  said «هوصّله للمكتب».

**The cause is the shape, not the phrasing.** For each Urfi question the AI returns one field with fixed values:
- `urf_wife_past`: first / divorced / khul / widow;
- `urf_guardian`: father / brother / uncle / none;
- `urf_witnesses`: own / office;
- the two ages.

Anything that is not «he states one value» has no place in that shape:
- a question («ينفع…؟»);
- taking an answer back («… مش هييجي»);
- two things in one turn.

It then falls to a general intent (`office_info` / `service_detail`), and from there to the office. The same will happen
tomorrow with:
- «ينفع أجيب الشهود من عندي؟»;
- «لو هي مطلقة ينفع؟»;
- «الشهود مش هييجوا، المكتب يوفر؟»;
- «لو هي 17 سنة ينفع؟».

**And the turn can time out.** Hard turns ran past the AI's 15-second budget:
- 3 of 12 readings with classify-v29 timed out;
- Ahmed Amin's first «ينفع ابوها بدل اخوها ؟» did too (classify-v27).

A timed-out turn falls back to the plain reply, whatever the prompt says.

## 2. The rule

**One list, every Urfi question.** The AI returns `case_said`: everything the client says in this turn about his Urfi case.
Each item has three parts:
- `field`: `wife_past`, `guardian`, `witnesses`, `ages`, `has_copy`, `agreed` or `children`;
- `value`: one of that field's values; for `ages`, the two numbers;
- `kind`: `said`, `asked` or `withdrawn`.

| kind | example | what the code does (one rule for every field) |
|---|---|---|
| **said** | «ابوها موجود»، «هنجيب الشهود»، «هي مطلقة» | the answer, filled or corrected, as v1.60 / v1.74; a correction is said back «تمام، عدّلت…» (v1.75); a refused case opens again (v1.72) |
| **asked** | «ينفع ابوها؟»، «ينفع أجيب الشهود؟»، «لو مطلقة ينفع؟»، «لو 17 ينفع؟» | answered from the office's rules, **without changing the case**: see 2.2 |
| **withdrawn** | «اخوها مش هيجي»، «الشهود مش هييجوا» | that answer is unknown again and its question comes back (only «nobody» refuses, as today) |

### 2.1 Several things in one turn
- Each field takes the latest thing he says about it.
- Different fields are all taken.
- Example, «اخوها مش هيجي / ابوها موجود ينفع؟»:
  - the brother is `withdrawn`, then the father is `asked`;
  - for the guardian, the latest wins: the father is asked about.
- Example, «الشهود هنجيبهم ولو مطلقة ينفع؟»: the witnesses are `said` and the wife's past is `asked`. Both are taken.

### 2.2 An «asked» item is answered by the same rules that decide the case (no new list)
- **How:** the code tries the asked value on a **copy** of the case, through the B-11 rules the case already uses (`Settle`:
  under 18 refuses, no guardian for a first marriage refuses, the divorce rules redirect). The real case does not change.
  - **The copy is refused or redirected** → the «cannot» text for that rule (2.3). Nothing closes: it is a question.
  - **It goes on** → the «can» text, then:
    - **the guardian** (a person who must come): the confirm question, «أيوه» / «لا» (v1.82);
    - **the other fields** (a fact about her, or a choice): the question waiting, with its buttons.
- **Other relatives:** the rules do not know a value outside a field's values (a maternal uncle, a grandfather). It is
  answered as not one of the allowed ones: «cannot», with who is allowed.
- **This answer replaces the office:** an asked item is never «سؤال مهم 👌 هوصّله للمكتب». The office gets only the
  questions no rule answers, as today (the price of something not listed, a legal question → «المستشار هيوضح…»).

### 2.3 The approved texts (new texts: the owner approves the wording)

| key | text | when |
|---|---|---|
| `urf_guardian_can` (v1.82, kept) | أيوه، {{guardian}} ينفع يبقى الولي. هو هيحضر معاكم وموافق؟ | a guardian the rules allow |
| `urf_option_can` (new) | أيوه، {{option}} ينفع ✅ | another field the rules allow; the question waiting follows |
| `urf_guardian_cannot` (new) | للأسف {{who}} مينفعش يبقى الولي. سياسة المكتب: لو دي أول جوازة للزوجة، لازم يكون معاها الأب أو الأخ أو العم. | a relative outside the allowed ones; the guardian question follows |
| `urf_age_cannot` (new) | للأسف لأ، سياسة المكتب إن الزوج والزوجة يكون سنهم 18 سنة أو أكتر. | an asked age under 18 |

- `{{option}}` is filled by the code from the field's own words:
  - «إن الزوجة تكون مطلقة أو مختلعة»;
  - «إن الزوجة تكون أرملة»;
  - «إن حضرتك تجيب الشهود»;
  - «إن المكتب يوفّر الشهود».
- `{{who}}` is his own word for the relative («خالها»), kept short. It is the one word read from his message, never a guess.

### 2.4 Kept as today
- **A time held or paid:** a said or withdrawn change goes to the office (v1.62). An asked item is only answered, and nothing
  changes.
- **The age refusal:** closes, as today.
- **The guardian refusal:** opens again on a guardian said.
- **The approved texts go out word for word.** The writer does not touch the Urfi questions.

### 2.5 The time budget
- The AI's budget per turn goes from 15 to **30 seconds** (`DefaultBudget`). Normal turns take 4–6 seconds and are not
  affected.
- A hard turn is read instead of falling back.
- The fewer fields (one list instead of seven) should also make it lighter.

## 3. The build

- **classify-v30:**
  - one section «case_said» with the three kinds and 2.1, and the examples above in his words;
  - the old sections removed: `urf_wife_past`, `urf_guardian` (+ `dropped`), `urf_witnesses`, `urf_guardian_asked`;
  - the ages stay as two numbers inside an `ages` item.
- **Schema** (`OpenAiIntentClassifier`, and the Anthropic copy of it):
  - `case_said`: an array of `{field, value, kind}`, with each field's values;
  - the five old Urfi fields go.
- **`Understanding.cs`:**
  - `CaseSaid` (a list) replaces `UrfWifePast` / `UrfGuardian` / `UrfWitnesses` / `UrfGuardianAsked` / the two ages;
  - one helper gives the latest item per field.
- **`UrfRules` (`Urf.cs`), all in one place:**
  - `Apply(case, items)` takes the said and withdrawn items (what `ApplyFacts` does today, plus `withdrawn` for every
    field);
  - `Try(case, item)` runs an asked item through `Settle` on a copy and returns can / cannot (which rule) / redirect;
  - `ChangesBooked` uses the same items.
  - `dropped` and `GuardianNo` become the `withdrawn` kind; the `urf:guardian:no` button stays (it means «nothing
    changes»).
- **`HazemResponder`:**
  - `WithUrfFacts`, `UrfFactsSaid`, `AskGuardianBack`, the refused-case reopening and `RespondToOldUrfButton` read the one
    list;
  - an asked item is answered before `AnswerIntent`, so `service_detail` never sends it to the office;
  - every place that read the old fields reads the list instead (no second path kept).
- **Migration `0067`:** the three new texts (2.3) after the owner approves them.
- **`AnthropicIntentClassifier` / `OpenAiIntentClassifier`:** `DefaultBudget` 30 seconds.

## 4. Done when

- **Unit tests:**
  - every field × every kind (said / asked allowed / asked refused / withdrawn);
  - two items for one field (the latest wins);
  - two fields in one turn;
  - asked at a held time (answered, nothing changes);
  - asked after the guardian refusal (the confirm question, then «أيوه» opens the case again);
  - asked under 18 (cannot, nothing closes);
  - a relative outside the list (cannot + the guardian question);
  - an asked item never gives `service_detail` → office.
- **Every existing test stays green.** The v1.60–v1.83 Urfi tests move to the new list one for one.
- **An Urfi test set, not one chat:** 18 new conversations in `docs/ai/conversations.jsonl` (`urf-*`), each a different
  way of saying, asking or taking back each field:
  - the guardian: 5;
  - the wife's past: 3;
  - the witnesses: 3;
  - the ages: 3;
  - two things in one turn: 2;
  - after a refusal: 2.

  Run them with the 7 Urfi conversations already there, through `quality-eval` with those ids only (about 25
  conversations, about $2–3, not the full $15–20).
  - **Done:** 0 forbidden, every new one passes, the old 7 do not drop.
- **The sweep on a fake number:** Ahmed Amin's two chats of 2026-11-06, as he typed them.
- `backend-test`, `healthcheck`; DECISIONS v1.84 and the session report.

## 5. Decisions for the owner (recommendation first)

1. **One list for the whole Urfi case** (said / asked / withdrawn) instead of a field per question. The v1.82–v1.83
   guardian-only pieces go into it.
2. **An asked item is answered by the case's own rules on a copy, never sent to the office**, and never changes the case.
3. **The three new texts** in 2.3, as written or reworded.
4. **The AI's time budget 30 seconds** instead of 15.
5. **Proven on 25 Urfi conversations** (about $2–3), not on one chat.
6. **Later, not in this task:** the same idea for the booking («ينفع ميعاد بالليل؟»، «ينفع أغيّر لفيديو؟») and the written
   services, once this one is proven.
