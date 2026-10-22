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

`service`: `consultation_phone_video`, `consultation_office`, `consultation_urgent`,
`document_review`, `contract_drafting`, `formal_notice`, `urf_marriage`, `urf_divorce`,
`urf_proof`, `urf_unclear`, `written_opinion`, `case_representation` (B-23 + the B-11 paths).
`consultation_mode`: `phone_video`, `office`, `unknown`. `full_name`: only the client's own name.
`confidence`: 0..1. The intent is decided for the **whole turn** (all grouped messages).

## The call

- OpenAI Chat Completions, **Structured Outputs** (`response_format: json_schema`, `strict: true`,
  `additionalProperties: false`, every field required, enums = the lists above), `temperature: 0`.
- Prompt: `backend/src/Office.Infrastructure/Ai/prompts/classify-v6.md` (v1..v5 kept as history), embedded in the build.
  Its version (`classify-v6`) is saved with every classification. **Change the prompt = a new
  file (`classify-v7.md` next) and a new `PromptVersion`**, then run `classify-eval` again.
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
