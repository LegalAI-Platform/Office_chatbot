# TASK 18 report — 2026-11-01 — Hazem answers the question that was asked, in one reply, and it is measured (DECISIONS v1.45)

The owner: «كل هدفي نطلع الشات بوت ده بيجاوب كويس ومنطقي ومفيد»; «المستشار سعد هو اللي بياخد الاستشارات اه ونفذ يلا»;
after the risks of free writing were explained: «انت شايف اعمل ايه… نفذ». Branch `task18-quality` from `main` (`7f8cb90`).
The plan: `TASK-18-prompt.md`.

## What was done

| Part | What |
|---|---|
| The suite (§3.1) | `docs/ai/conversations.jsonl`: 203 conversations written from scratch (the five real failures among them); `QualityEvalTests` plays them through the real Hazem (in-process API, throwaway database, fake calendar, the real classifier `claude-opus-5-5` and writer `claude-haiku-5-5` as on the test server), and the judge (`claude-opus-5-5`, `docs/ai/judge-v1.md` with `docs/ai/judge-facts.md`) scores every reply. Scripts `quality-eval` (linux + windows); skipped in `backend-test`. Report in `docs/ai/quality/` |
| One reply (§3.3) | no offer stacked after an office answer when the offer or a booking step was the last reply; a booking step (call app, times) only from a booking wish whose words name phone or office; «محتاج إيه؟» always with the two welcome buttons; no separate «قولّي الموضوع» under the offer's buttons (B-25 changed) |
| Office facts (§3.2) | migration `0045_office_facts`: three approved texts (`office_facts`, `office_facts_more`, `office_facts_other`, «proposal TASK 18»), never sent; their prices are the panel's slots, the address the panel's. The writer `write-v2` answers his question from them first, in one message with the next step; it knows what was sent before it in the turn |
| Writer (§3.3/3.4) | writable now: the lawyer's bio, the consultation and case offers, office hours, how to book, refund, fee policy, practice areas, service check, the call-app question, the times sentence. The guard checks numbers and words against the text **and the facts**; «1,000» = «1000»; paying verbs, «بالظبط», «فاهمة» (Hazem of himself) refused. Gender: «woman» from the name or her own words («عايزه»، «جوزي») |
| Bug | a new client's first message «عايز اعمل جواز عرفي» answered 500 (the Urfi case was written before the person). The case is now saved with the turn, after the person and the intake |

Not done from the plan: the panel page «حقايق المكتب» (the facts are approved texts, so the panel's «النصوص» edits them);
the classifier's `asked`/`facts` fields (the writer reads his message itself, so they were not needed); the passed
appointment and the welcome (§3.3, third bullet); the weekly loop (§3.5).

## Results

| Run | Score | Replies with every mark 2 | Forbidden |
|---|---|---|---|
| `baseline` (before) | 82.7% | 37% | 0 (4 flagged were the judge's facts, corrected) |
| `after-3.3` | 84.6% | 40% | 0 |
| `v1.45` (81 conversations) | 86.8% | 48% | 0 |
| `v1.45-c` (the guard: nothing repeated from the message before it, no typo; hours and practice areas fixed again; the payment fact) | 89.9% (the first 81: 91.4%; the 122 new: 88.4%) | 52% | 0 |
| `v1.45-e` (final: the price and the legal refusal written with the offer, prices locked and the refusal kept; «حقك» refused; a written service's price starts it) | **89.7%** (81: 90.5%; 122: 88.9%), coherent 1.82, tone 1.70 | 51% | **0** |
| `v1.45-b` (203 conversations: feminine fixed texts, the urgent/Urfi start button, no topic question with a booking step, the name and «someone from the office» written, a fairer runner) | **89.1%** (the first 81: **91.2%**; the 122 new: 87.2%) | **50%** | 1 (see below) |

«مع سعد نفسه؟», «مدتها قد إيه؟» and two questions in one message: 100%. Every run costs about $5–10 (Opus 5.5 $4/$20 per
million tokens; ~2.1M in / 36K out for Hazem, ~0.57M / 56K for the judge, most of the input cached).

| Check | Result |
|---|---|
| `backend-test` | 817 passed, 1 skipped (the suite itself); new tests: the Urfi first message, the facts and the guard, the gender |
| `quality-eval` | 89.7% on 203 conversations (`v1.45-e`), 0 forbidden; runs vary about ±0.5% |
| `healthcheck` | 159 passed, 0 failed, 7 expected warnings |
| `web-test`, `wa-tests` | not run (no panel or n8n change) |

## Still open, for the 95%
- ~~A contradiction~~ **settled (the owner: «موافق»):** after the transfer's photo the appointment is «محجوز لحضرتك، والمكتب
  بيراجع التحويل» (`situation_review`, migration `0046`) until the office verifies the payment; «متأكد ✅» only after it. The
  line «لو حابب تكتب الموضوع…» became its own text, said only to a client who has not told his matter, and once. The 12 paid
  conversations after it: 93.4%, nothing forbidden.
- Fixed texts stay curt or masculine to a woman (where to pay, the hold): they carry money, so they stayed fixed.
- After the urgent or Urfi price the consultation offer's buttons follow (phone / office), which does not fit.
- The judge is strict about tone; some marks are matters of taste.

## Before deploying
- The owner reads the three facts texts in the migration (or in «النصوص» after it) and approves them.
- Commit, PR, deploy only when the owner asks. On the server `hazem.conversational` is on with `claude-haiku-5-5`, so the
  new writer goes live with the deploy.
