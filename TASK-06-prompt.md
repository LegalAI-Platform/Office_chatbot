# TASK 06 — الأسعار والأسئلة الشائعة عن المكتب (DECISIONS v1.16 → v1.17, B-20 / B-21 / B-23)

Branch `task06-faq-prices` from `main` (`4cd75e3`, after TASK 12). The panel stays the current one (PR #14, Nehad's
panel, is on hold by the owner's decision). Read DECISIONS v1.16 B-20, B-21, B-22, B-23, `docs/ai/classification.md`,
and the TASK 08–12 reports first.

## The problem today

- B-23's prices are written in three places: the code (`BookingService`: 500 / 1,000 / 1,000 / 5,000 / 4,000), the SQL
  (`price_service_order`: 1,500–3,000, from 3,000, 2,000; `open_written_opinion`: 2,000) and 10 approved texts
  («بالتليفون بـ500 أو في المكتب بـ1,000», `price_*`, `urgent_price`, …). A new price needs a developer, and a text
  edited alone in «نصوص حازم» makes Hazem say one price and ask for another.
- «المكتب فين؟» or «بتشتغلوا لحد إمتى؟» get «مش واضح»: the AI has no intent for them, although B-20 allows Hazem the
  office's operational information (the prices, the hours, the address, how to book and pay, the refund policy).

## The owner's decisions (the chat, 2026-10-26)

| # | Decision |
|---|---|
| 1 | The prices are data, edited from the panel by **the lawyer and the manager** only (as pricing a service, TASK 10) |
| 2 | A new price applies to **new requests only**: a hold, a booking, an order or an Urfi case made before keeps its amount |
| 3 | Hazem answers: the address and the map, the working hours, how to book, how to pay, the refund policy, phone/video or office, and the price list. «مباشرة قضية» is never priced (B-23) |
| 4 | **The prices are B-23's** (500 / 1,000 / 1,000 …). The websites' prices (200 / 500) are old; the website is updated by the office, not here |
| 5 | New texts are proposals (inactive) until approved; texts already approved keep their words |

**Proposed in this prompt (the owner confirms or changes them in the review):**

| # | Proposal |
|---|---|
| P1 | **The working hours Hazem says come from the booking's default week in the panel** (one source, no second number). If the office's hours differ from the consultations', the owner writes them and they become a setting instead |
| P2 | **The fee policy** from saadfathy.com: «المبلغ المدفوع في الاستشارة بيتخصم من الأتعاب لو المكتب مسك القضية» — answered when asked |
| P3 | **«بتاخدوا قضايا إيه؟»**: the practice areas as listed on the office's websites (criminal and drugs, civil, family and personal status, commercial, real estate, companies, cassation, constitutional, labor, administrative, contracts, Urfi), with no legal opinion, then the offer. Plus «المكتب بيقبل قضايا من كل المحافظات» |

## Facts taken from the office's websites (saadfathy.com, saadfathysaad.net, elmo7amy.tv)

- Address: «183 شارع التحرير، عمارة الاستراند، باب اللوق، وسط البلد، القاهرة». Map:
  `https://maps.google.com/?q=30.044390,31.242317`. **The owner writes both in «بيانات المكتب»** (they are data, not code).
- InstaPay and the wallet on the website are real payment numbers: **the owner enters them** in «بيانات المكتب» after
  checking they are current. Nothing real is written into code, tests or docs.
- 01211171704 on the websites is **the office's real WhatsApp number**: never used in tests (CLAUDE.md).
- The working hours are on no website (→ P1).

## What to build

1. **Migration 0018** (`database/migrations/0018_prices_faq.sql`):
   - `hazem.service_prices`: one row per B-23 line (code, Arabic name, kind `fixed` / `range` / `from` / `never`, amount,
     min, max, minutes where the booking needs them, who changed it and when), seeded with B-23 exactly;
     `hazem.service_price_changes` (old → new, who, when).
   - `hazem.price_service_order` and `hazem.open_written_opinion` read their ranges and the 2,000 from the table.
   - The 10 texts with a number get a new version with slots (`{{price_phone}}` …) that renders **the same words** with
     B-23's numbers (so they stay approved); the word-for-word test maps the slots.
   - The new FAQ texts (proposals, inactive); staff task `office_info_missing`; audit target `price`; permission data.
2. **.NET**:
   - `Office.Application/Prices`: the price list read once per turn (as the settings); `BookingService` amounts come from
     it (`BookingSettings.Fit` gives the amount as it gives the Urfi minutes); the panel's pricing range check too.
   - **The AI** (`classify-v7.md`, a new `PromptVersion`): a new intent `office_info` with a field `info_topic`
     (`address`, `hours`, `how_to_book`, `how_to_pay`, `refund`, `mode`, `prices`, `fee_policy`, `practice_areas`).
     New samples in `eval-set.jsonl` **and** new hold-out samples, written **before** the first v7 run; «انتو بتشتغلو
     قضايا ايه» moves from `other` to `office_info / practice_areas` in the expected labels (with the owner's P3).
   - `HazemResponder`: each topic → its approved text, filled from the price list, «بيانات المكتب» and the booking
     week; the booking ones (`how_to_book`, `mode`, `prices`) end with the offer and its buttons. A text whose data is
     missing (no address yet) is never sent half: the unclear-free fallback is the offer, and a task `office_info_missing`.
3. **Panel**: «الإعدادات ← الأسعار»: B-23's table, edit with a confirmation that says it applies to new requests only,
   the history of changes. Permission `prices.manage` (manager, lawyer).
4. **Tests**: every B-23 line from the table; a price change → a new hold uses it, an old hold keeps its amount; the
   pricing range from the table; every FAQ topic (and the missing address); the word-for-word check; RBAC; healthcheck
   (the table complete, a WARN while the address or the map is empty); hazem-e2e step 16 («المكتب فين؟» → the address,
   a price changed in the panel → the next hold's amount).
5. **classify-eval**: needs the owner's OpenAI key in `.env` (`HAZEM_OPENAI_API_KEY`, entered by the owner). Without it
   the new intent is tested with the stub only, and the report says v7 is **not measured**.
6. **Docs**: DECISIONS v1.17, `docs/ai/classification.md`, `panel-api.md`, `runbooks/prices.md`,
   `approved-texts-proposal.md`, the session report.

## Not in this task

The websites (old prices), real Meta, PR #14 (on hold), new services (B-23's list stays as it is).
