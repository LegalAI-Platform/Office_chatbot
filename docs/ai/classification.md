# Hazem's understanding: intent classification (TASK 04, DECISIONS T-07 / T-08 / T-09)

The AI does **one** thing: it says what the client wants, from a closed list, and pulls out three
fields. It never writes a word the client sees: every reply is an approved text
(`hazem.approved_texts`). Code: `backend/src/Office.Application/Understanding/`,
`backend/src/Office.Infrastructure/Ai/`.

## The closed list

| intent | meaning | Hazem's answer in TASK 04 |
|---|---|---|
| `greeting` | a greeting only | greeting text (proposed) → else the consultation offer |
| `provide_name` | the client's name | taken as the name (B-02) |
| `consultation_request` | wants a consultation / appointment | consultation offer (B-11 text, owner's decision) |
| `service_by_name` | names a service (with `service`) | the service's text (B-16, B-17, B-11) |
| `urgent` | urgent | B-09 first sentence + consultation offer + task (owner's decision) |
| `price_question` | asks a price (with `service` when said) | the B-23 price text (proposed) → else the offer |
| `legal_question` | a legal question | B-20 text + offer |
| `existing_matter` | follows a matter the office handles | text (proposed) + task |
| `human_request` | wants a person | text (proposed) + task (no handoff until TASK 05) |
| `reschedule_or_cancel` | change or cancel | text (proposed) + task (booking: TASK 08) |
| `thanks_or_closing` | thanks / bye | thanks text (proposed) → else nothing |
| `other` | anything else, or the fallback | "unclear" (approved, 0007); the 2nd in a row → task |
| `describe_matter` (05a) | tells the problem without asking | the offer (+ the B-25 question when the topic is still unknown); after that question: «تمام، وصلت ✅» |
| `abusive` (05a) | insults, threats, spam | once: `abuse_warning` (proposed) + task `complaint`; then muted for good |
| `office_info` (06) | a question about the office itself, with `info_topic` | its approved text filled from the panel (address, hours, payment, prices…), then the offer; a detail not written yet → the offer and a task `office_info_missing` |

`service`: `consultation_phone_video`, `consultation_office`, `consultation_urgent`,
`document_review`, `contract_drafting`, `formal_notice`, `urf_marriage`, `urf_divorce`,
`urf_proof`, `urf_unclear`, `written_opinion`, `case_representation` (B-23 + the B-11 paths).
`consultation_mode`: `phone_video`, `office`, `unknown`. `full_name`: only the client's own name.
`confidence`: 0..1. The intent is decided for the **whole turn** (all grouped messages).

## The call

- OpenAI Chat Completions, **Structured Outputs** (`response_format: json_schema`, `strict: true`,
  `additionalProperties: false`, every field required, enums = the lists above), `temperature: 0`.
- Prompt: `backend/src/Office.Infrastructure/Ai/prompts/classify-v11.md` (v1..v10 kept as history), embedded in the build.
  Its version (`classify-v11`) is saved with every classification. **Change the prompt = a new
  file (`classify-v12.md` next) and a new `PromptVersion`**, then run `classify-eval` again.
- Model: `OPENAI_MODEL`. Empty = `gpt-4.1-mini`, **a starting point only**. The final model is
  chosen by the owner after `classify-eval` (T-08).
- Key: `HAZEM_OPENAI_API_KEY` in `.env` — the .NET key, separate from n8n's voice key. It may be
  empty (classification off, healthcheck WARN). Never printed, logged or committed; the
  HttpClient that carries it has no loggers.
- Base URL: `OPENAI_BASE_URL` (empty = `https://api.openai.com/v1`).
- 15 seconds for the whole call, one retry for a temporary error (429, 5xx, network).

## Safety (T-09)

- `AiInput.Build` is the **only** builder of what the AI sees: typed text, captions, finished
  voice transcripts, and the titles of buttons the client pressed. Never a media object, bucket,
  key, file name, mime type, sha256 or location. Unit test + integration test + the e2e stub log.
- Before the AI: a 14-digit number → `[رقم_قومي]`, 8 or more digits (Arabic-Indic too, spaces or
  dashes allowed) → `[رقم]`, an e-mail → `[ايميل]`.
- A turn with no words (a photo only, a file only) **never reaches the AI**; it is answered by
  its type.

## Fallback: no message is left without an answer because of the AI

The answer becomes intent `other` (and the "unclear" answer) when: the key is empty (`no_key`),
the call fails (`error`), it takes longer than 15 s (`timeout`), the output is not the schema
(`invalid_output`), a value is outside the lists (`unknown_value`), or the confidence is under
`hazem.settings ai.min_confidence` (0.6, `low_confidence`).

## The log: `hazem.classifications`

One row per classified turn: `turn_id`, `intent`, `confidence`, `service`, `consultation_mode`,
`full_name_present` (true/false only), `model`, `prompt_version`, `latency_ms`, `input_tokens`,
`output_tokens`, `fallback_reason`. **No client text and no name**, by design (no such column;
the healthcheck checks it).

## classify-eval: choosing the model (owner only)

`docs/ai/eval-set.jsonl`: 156 messages written from scratch in Egyptian Arabic (never from
`exports/`), every intent, spelling mistakes, Franco-Arabic, and two requests in one message,
each with its correct intent (and service when there is one).

With `HAZEM_OPENAI_API_KEY` set in `.env` and the stack up (`up`):

```powershell
# Copy the file into the api container (piping Arabic text from Windows PowerShell breaks it)
docker compose cp docs/ai/eval-set.jsonl api:/tmp/eval-set.jsonl
docker compose exec api dotnet /app/cli/Office.Cli.dll classify-eval --file /tmp/eval-set.jsonl
```

On Linux, `docker compose exec -T api dotnet /app/cli/Office.Cli.dll classify-eval < docs/ai/eval-set.jsonl` works too.

Options: `--model gpt-…` (try another model without changing `.env`), `--limit 20` (a quick try),
`--input-price 0.4 --output-price 1.6` (USD per 1M tokens, from OpenAI's price page) to get the
cost. The output: accuracy overall and per intent, the confusion matrix, the mistakes by sample
id, fallbacks, average latency and tokens. The real key is used; the test stub is ignored.

The output also lists `service_mistakes` (expected vs. got service, by sample id).

### Results (2026-10-22, `gpt-4.1-mini`, 156 samples, real key)

| Prompt | Intent accuracy | Service accuracy | Avg latency | Tokens in / out | Fallbacks |
|---|---|---|---|---|---|
| `classify-v1` | 94.9% (8 wrong) | 73.3% | 895 ms | 146k / 4.3k | 0 |
| `classify-v2` | 98.7% (2 wrong) | 83.3% | 868 ms | 235k / 4.3k | 0 |
| **`classify-v3`** (in use) | **100%** | **100%** | 918 ms | 260k / 4.3k | 0 |

**Hold-out set** (`docs/ai/eval-holdout.jsonl`, 40 new messages written after v2, in other words
than the first set, never used to tune a prompt): v2 = 97.5% intent / 100% service, v3 = 97.5% /
100%. The one miss in both: «انتو بتشتغلو قضايا ايه» → `legal_question` (expected `other`); the
reply is the B-20 sentence + the offer, which is acceptable. **Do not tune prompts on the
hold-out set**, or it stops measuring anything; add new hold-out samples instead.

### TASK 05a: the topic (B-25), the sender role (B-14.4), two new intents

`describe_matter` (the client tells the problem without asking) and `abusive` (S-01); in the same
call `matter_type` / `matter_subtype` (the B-25 list) and `sender_role`. 40 new samples in the set
(196 in all) and 20 new hold-out samples (60), written before any v4 run.

| Prompt | Set | Intent | Service | Topic | Sender role | Tokens in (per turn) |
|---|---|---|---|---|---|---|
| `classify-v4` | 196 | 97.4% | 97.1% | 100% | 77.8% | ≈2,470 |
| `classify-v5` | 196 | 99.5% | 100% | 100% | 100% | ≈2,730 |
| **`classify-v6`** (in use, DECISIONS v1.6) | 204 | **99.0%** | **100%** | 97.1% | **100%** | ≈2,800 |
| `classify-v4` | hold-out 60 | 91.7% | 100% | 100% | 66.7% | |
| `classify-v5` | hold-out 60 | 95.0% | 93.3% | 100% | 100% | |
| **`classify-v6`** | hold-out 60 | **96.7%** | 93.3% | **100%** | **100%** | |

**v6 (DECISIONS v1.6, B-09):** urgent also when the client describes an arrest or detention, the
police at home, or a hearing / investigation / execution today or tomorrow, without saying
"urgent"; a report or an old ruling alone is not urgent. Urgent accuracy: 100% on both sets
(8 new samples 197–204 for the rule). «البوليس جه البيت ومعاه أمر ضبط» is now urgent.

- v5 adds (from the 196-sample misses only): someone introducing themselves as a lawyer, a
  journalist or a company is not giving a client name and is not asking for a person; «…وعايز
  حقوقي» after a story is still telling; «عقد عرفي» alone is `urf_unclear`.
- Hold-out misses: «البوليس جه البيت ومعاه أمر ضبط» → `describe_matter` (labelled urgent; by the
  owner's decision a message is urgent only when the client SAYS so, so this answer follows the
  rule); «انا زعلان عشان محدش رد عليا» → `describe_matter` (expected `other`); «انتو بتشتغلو قضايا
  ايه» → `legal_question`; «نكتب كتاب عرفي» → `contract_drafting` (expected `urf_marriage`).
- Cost per turn grows with the prompt (≈2,700 input tokens); still around a cent per 10 turns or
  less at `gpt-4.1-mini` prices — check OpenAI's price page.

- v3 adds: «الو / مين معايا» are not greetings; «العاجلة» as the name of the service is a price
  question, not urgency; a price question about a named thing always carries its service; with
  two services, the first one written.

- v2 adds explicit rules: every Urfi wish is a service (also when phrased as a question), "take my
  case" is `case_representation` (not an existing matter), urgency wins, "talk to the lawyer about
  my problem" is a consultation, and a synonym table for `service`.
- Remaining misses are harmless for the reply: «الاستشارة العاجلة بكام» → `urgent` (the reply is the
  urgent price + the offer anyway); «الو» → greeting; «الاستشارة بكام» with no service → the offer,
  which carries the prices.
- v2 is about 60% longer (≈1,500 input tokens per turn); still a few cents per 1,000 turns at
  `gpt-4.1-mini` prices — check OpenAI's current price page.
- **Caution:** the samples were written by us and v2's rules were written after seeing v1's
  mistakes, so real clients may score lower. Re-run on real (anonymised, owner-approved) samples
  once Meta is live.

### TASK 06: the office's questions (`office_info`, `classify-v7`)

A new intent `office_info` with `info_topic` (`address`, `hours`, `how_to_book`, `how_to_pay`, `refund`, `mode`, `prices`,
`fee_policy`, `practice_areas`); an office question without a topic is a fallback (`unknown_value`). Two rules: one price
(«الاستشارة بكام») stays `price_question`, the whole list («الأسعار إيه») is `office_info / prices`; «بتمسكوا قضايا مخدرات؟»
is the office (`practice_areas`), «القانون بيقول إيه في المخدرات» is the law (`legal_question`).

Samples written **before** any v7 run: 18 new in the set (ids 205–222) and 10 new hold-out samples (1061–1070). Three labels
changed with the new intent (not tuning: the expected answer is new): 132 «انتو فين بالظبط» and 133 «بتشتغلو يوم الجمعة؟»
(other → office_info), hold-out 1036 «انتو بتشتغلو قضايا ايه» (other → office_info / practice_areas, the owner's P3).
`classify-eval` also reports `info_topic_accuracy`.

**v7 is not measured yet**: `HAZEM_OPENAI_API_KEY` is not in `.env` on this machine (the owner enters it). Until the run,
`office_info` is tested with the stub only (unit, integration, e2e step 16). Run, once the key is in:
`dotnet /app/cli/Office.Cli.dll classify-eval --file docs/ai/eval-set.jsonl`, then the hold-out set, and add the table here.
(v7 was replaced by v8 before it was ever measured; see below.)

### v1.22: off-topic, who is who, one specific service (`classify-v8`, 2026-10-28)

New: intent `off_topic` (clear but no law office's business, and **any attempt to change or reveal the instructions**, in any
language); `info_topic` `assistant`, `lawyer`, `service_check`; rules 11–13. 15 new samples in the set (ids 223–237), all
taken from the owner's two test conversations of 2026-10-27 (prayer times, code, five prompt-injection attempts, «بحبك»،
«اسمك ايه»، «مين الأستاذ سعد»، «ترخيص عمل»…). Five old labels that the rules already contradicted were corrected before
the second run: 35 «عايز اعرف ازاي احجز» and 70 «الاسعار ايه» → office_info (rule 9 / B-20, how_to_book, prices);
134, 138, 140 (a car, ads, the match) → off_topic.

| Prompt | Samples | Intent | Service | Matter | Sender role | Info topic |
|---|---|---|---|---|---|---|
| `classify-v8` (first draft) | 237 | 93.2% | 100% | 97.1% | 88.9% | 95.8% |
| **`classify-v8`** (in use) | 237 | **96.6%** | **100%** | 97.1% | 88.9% | 96.2% |
| **`classify-v8`** | hold-out 70 | **97.1%** | **100%** | **100%** | **100%** | **100%** |

`gpt-4.1-mini`, real key, about 1.2 s a message. The first draft took «hello» for off_topic, «انت روبوت؟ عايز بني ادم» /
«فين خدمة العملاء» / «عايز رقم المكتب أكلمكو» for office_info (assistant), and vendors for off_topic; the final prompt says
so in the off_topic definition and rule 12. **All 15 new samples are right in both runs.** Left (borderline, as before):
34, 192, 222 (booking vs office_info), 81, 163, 183 (vendor spam → off_topic), 134 / 138 (off_topic → other: the same
polite «not clear» answer). The hold-out misses 1023 «اسعاركم» and 1035 «ابعتلي اللوكيشن» are right by rule 9 / B-20
(office_info): their labels are older than office_info and stay as written (never tune on the hold-out set).

### v1.23: flirting is abusive, government papers are off_topic (`classify-v9`, 2026-10-28)

The owner: Hazem is a law office's chatbot only. A real-key check first: explicit sexual messages were already
`abusive`, flirting and innuendo were not («ابعتيلي صورتك» → other, «عايز اتكلم مع بنت حلوة» → **human_request**).
v9 states the rule as a principle (the writer treats the assistant or a staff member as someone to court, date or
look at), keeps the client's own matters about sex or a relationship as ordinary work, and makes government,
civil-registry and traffic papers off_topic. Samples: set 238–245 (flirting, two real matters) and 246–251
(papers, one khula case); hold-out 1071–1077 (flirting written after the first v9 draft failed on them: the first
draft had only examples and caught none of them) and 1078–1081 (papers, alimony); a third batch of 9 never in any
file was used only to check that the principle generalizes. Sample 230 «بحبك» is now labelled abusive (flirting).

| Prompt | Samples | Intent | Service | Matter | Sender role | Info topic |
|---|---|---|---|---|---|---|
| **`classify-v9`** (in use) | 251 | **96.0%** (96.4% with 230 relabelled) | **100%** | 97.3% | 88.9% | 96.2% |
| **`classify-v9`** | hold-out 81 | **93.8%** | **100%** | **100%** | **100%** | **100%** |
| `classify-v9` | unseen batch of 9 | 88.9% (8/9; flirting 5/5) | | | | |

Every government-paper sample is right in both sets, and 4 of the 5 hold-out flirting messages (1072 «عندك كام سنة
وشكلك ايه» → office_info is left). Other misses are the borderline ones of v8 plus 179 (a colleague's existing case →
legal_question), 1056 (a complaint → describe_matter) and 1081 (alimony → service_by_name: the same offer either way).

### v1.24: the subject and the scope come first (`classify-v10`, 2026-10-28)

A test message «عايز أحجز كشف عند دكتور» got a consultation offer (consultation_request, 0.90). The owner: not a fix for
this one case. The answer now starts with `subject` (a few English words: what the turn is about and who is asked to do
it; never read or kept) and `scope` (`office` / `outside`), before the intent. `Classification.From` turns a client's
`outside` into off_topic whatever the intent (not abusive, not a colleague, the press or a vendor); a missing or
unknown scope is `office`. The prompt states principles, not a list of professions: words like «أحجز»، «كشف»، «بكام» are
not the subject; the one who does the service is the one the client names; the office sells and rents nothing; a legal
problem about any field is office; when in doubt, office. A probe showed why v10's first draft still failed: its subject
read «دكتور» as the lawyer; the "one the client names" rule fixed that.

`docs/ai/eval-scope.jsonl` is new: 2001–2040 (25 outside, 15 legal messages that mention a doctor, a bank, a school…),
written before any v10 draft and none in the prompt; 3001–3020 were written after the last prompt change and run once.
`scope` accuracy is scored on every sample but abuse (outside exactly for off_topic samples).

| Prompt | Samples | Intent | Scope | Service | Matter | Sender role | Info topic |
|---|---|---|---|---|---|---|---|
| `classify-v9` | scope 2001–2040 | 77.5% (9 outside missed, 4 of them got an offer or a price) | | | | | |
| **`classify-v10`** (in use) | scope 2001–2040 | **100%** | **100%** | 100% | 100% | | |
| **`classify-v10`** | scope 3001–3020 (run once) | **100%** | **100%** | | | | |
| **`classify-v10`** | set 251 | 95.6% (v9 96.0%) | 99.2% | **100%** | 97.3% | **100%** | 96.2% |
| **`classify-v10`** | hold-out 81 | **93.8%** (= v9) | **100%** | **100%** | **100%** | **100%** | **100%** |

About 2.35 USD per 1000 turns with gpt-4.1-mini, ~1.1 s each. The set's misses are the borderline ones of v9 plus 92, 98
(existing matters) and 138/183 (ads: one asked, one offered). An earlier draft put the press and vendors outside (the
set's 180–183, hold-out 1055): the scope now names anyone writing TO the office as office, and the code guard never
turns a colleague, the press or a vendor away.

### v1.26: Anthropic as a second provider (`classify-v10`, Claude Opus 5.5, 2026-10-28)

OpenAI's credits ran out; the owner wanted Anthropic now and both kept. `hazem.settings ai.provider` (`openai` | `anthropic`)
picks who is asked first; when it gives no answer (no key, no credits, an error, an invalid output) the other one is asked
in the same turn (a timeout is not asked again). Anthropic gets the same prompt and schema through structured outputs
(nullable lists written as `anyOf`, which Anthropic requires), the prompt cached, effort low, no sampling parameters.
Keys: `HAZEM_OPENAI_API_KEY`, `HAZEM_ANTHROPIC_API_KEY`; models: `OPENAI_MODEL`, `ANTHROPIC_MODEL` (default `claude-opus-5-5`).
Measure one provider with `classify-eval --provider anthropic` (no fallback inside the eval).

| Provider / model | Samples | Intent | Scope | Service | Matter | Sender role | Info topic | Latency | Cost / 1000 turns |
|---|---|---|---|---|---|---|---|---|---|
| **Anthropic `claude-opus-5-5`** | set 251 | **98.0%** | 99.6% | 100% | 100% | 100% | 100% | 2.2 s | ~$4 (cached prompt) |
| **Anthropic `claude-opus-5-5`** | hold-out 81 | **97.5%** | 100% | 100% | 100% | 100% | 100% | 2.2 s | |
| **Anthropic `claude-opus-5-5`** | scope 60 | **100%** | 100% | 100% | 100% | | 100% | 2.1 s | |
| Anthropic `claude-haiku-5-5` | set / hold-out / scope | 96.4% / 93.8% / 100% | 99.6% / 100% / 100% | 100% / 93.8% | 100% / 94.1% | 100% | 96.2% / 100% | 1.4 s | ~$0.3 |
| OpenAI `gpt-4.1-mini` (v1.24) | set / hold-out / scope | 95.6% / 93.8% / 100% | | | | | | 1.1 s | $2.35 |

**The owner chose Opus 5.5** (2026-10-28, «نفضل على Opus»). Haiku 5.5 is close on the numbers and ~15× cheaper, but missed
what costs the office most: an arrest told without «urgent» (197 «جوزي في الحجز من امبارح» → describe_matter), «msta3gel
gedan» (57 → other) and a real alimony question in Franco-Arabic put outside (84 → off_topic, a client refused). Opus missed
none of those. A conversation costs about $0.06–0.15 with Opus (the cache is written again after 5 quiet minutes).

The set's misses: 48 and 192 (a service or a booking asked as a question → office_info), 67 (an Urfi price → the service),
138 (an ad question → office_info) and 230 «بحبك» → off_topic (the pending abuse fix). The Anthropic key made on
2026-10-28 expires on 2026-11-27.

### v1.27: abuse before the scope (`classify-v11`, Claude Opus 5.5, 2026-10-28)

v10's scope pulled sexual requests without an explicit word («أنا محتاج واحدة أنام معاها»، «عايز بنت لليلة») to off_topic, and
its off_topic examples still listed «بحبك» as harmless chit-chat although v1.23 made it flirting. v11 judges abuse first
(a request for sex or a partner, sexual content, flirting, insults, threats, spam → abusive whatever the scope) and lists
words of love to the assistant or staff with the flirting. Scope samples 3021–3024 are new (three requests, one real matter).

| Prompt | Samples | Intent | Scope | Service | Matter | Sender role | Info topic |
|---|---|---|---|---|---|---|---|
| `classify-v10` | abuse probe of 7 | 3/7 (the three requests and «بحبك» → off_topic) | | | | | |
| **`classify-v11`** (in use) | abuse probe of 7 | **7/7** | 7/7 | | | | |
| **`classify-v11`** | scope 64 | **100%** | 100% | 100% | 100% | | 100% |
| **`classify-v11`** | hold-out 81 | **97.5%** | 100% | 100% | 100% | 100% | 100% |
| **`classify-v11`** | set 251 | **98.0%** | 99.6% | 100% | 100% | 100% | 100% |

The set's misses: v1.26's minus 230 «بحبك» (now abusive), plus 167 «اتجوزت عرفي والراجل اختفى» → an Urfi service (the same
Urfi offer either way).

## When the AI is down (migration 0032)

If the classifier gives no answer at all (`no_key`, `error`, `timeout`, `invalid_output`; not `low_confidence`),
`Backstop.cs` still reads two things by code: an insult (→ `abusive`: the warning once, then silence, S-01) and a
message that is only a thanks (→ `thanks_or_closing`). Everything else keeps the «unclear» fallback. The
classification log then says `fallback_reason = backstop:<reason>`.

Three messages or more in one hour that the AI could not read make one system alert «الـ AI اللي بيفهم رسايل العملاء
مش شغال» for the managers (the bell and an e-mail, one per hour). It happened on 2026-10-30: the Anthropic key was
deactivated and for twenty minutes nobody knew.
