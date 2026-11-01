# TASK 18 — Hazem answers the question that was asked, in one coherent reply, and we measure it (DECISIONS v1.45)

**Status: a plan for the owner to review. Nothing is built.** Branch `task18-quality` from `main`. The owner (2026-11-01):
«أنا كل هدفي نطلع الشات بوت ده بيجاوب كويس ومنطقي ومفيد… شات بوت بكوالتي عالية».

## 1. Why: what the real conversations showed

| Conversation | The client wrote | Hazem | What was wrong |
|---|---|---|---|
| احمد امين, 15:33 | هو الاستشاره مع الاستاذ سعد فتحي شخصيا؟ | the lawyer's biography, then «ميعاد حضرتك متأكد» | the question («him, in person?») never answered |
| ***89 test, 18:07 | هو مين اللي بيبقي معايا في الاستشاره سعد نفسه ولا حد تاني | biography + the consultation offer + «تحب المكالمة تبقى على إيه؟» | three messages; a phone-call question while he was booking an office meeting |
| ***89 test, 18:04 | السلام عليكم استاذ سعد معايا | «أنا حازم… محتاج إيه؟» with no buttons | his 17:30 call had passed but still held the request open: no welcome, nothing to press |
| ***89 test, 18:06 | عايز احجز | the same offer, word for word, as one minute before | repeated, though he had already seen it |
| ***90, 03:39 | لو عاوز اعمل عقد عمل بتعلوه / انت عارف اسمي؟ | (fixed in v1.43) an order opened; then «طلبك مستني الورق» to everything | the reply answered a box, not the client |
| ابو الليل, 01:32 | ابو الليل مين / انجز بالله | the same situation reply four times | no answer to what he said |

## 2. The causes (in the code, not one case each)

1. **A closed list of boxes.** The AI picks the nearest intent/topic; a question with no box («him in person?») gets the box's
   fixed text (`office_info/lawyer` → `about_lawyer`), not an answer.
2. **The office's facts live only inside long texts.** «The consultation is with Saad himself», «a phone call is 15 minutes»,
   «on WhatsApp or FaceTime» are nowhere Hazem can answer from, one by one.
3. **Replies stack.** `AnswerOffice` always adds `Offer(askTopic: false)` (HazemResponder.cs, after the office answer); `Offer`
   starts the booking step itself when the AI gave a consultation service (`KindAsked` → `AskCallApp` / `OfferTimes`). So a
   question becomes «answer + offer + a booking step», even in the middle of another booking.
4. **Short memory.** The classifier sees Hazem's last message and the client's previous one only; nothing knows «he is
   choosing an office meeting right now» except the pick waiting in the intake.
5. **Quality is tested by hand.** The owner finds a bad reply, we fix that one. Nothing measures the whole.

## 3. The design (the owner's rule stays: the code decides what, the AI understands and writes how)

### 3.1 Measure first: the conversation test suite
- `docs/ai/conversations.jsonl`: **200 conversations written from scratch** (never `exports/`), 2–8 turns each, every
  situation (new, held, paid, urgent at night, written order, Urfi, a returning client) × every kind of message (greeting,
  booking, price, a fact question, a legal question, a side question, confused, angry, voice-like typos, Franco, two
  questions in one), plus the five conversations of §1.
- A CLI `conversation-eval`: runs each conversation through the **real** Hazem (the API in-process, a throwaway database,
  the fake calendar, the real AI providers), and a **judge** (Claude, its own prompt `judge-v1`) scores every reply 0–2 on:
  answered what was asked · facts right · the next step clear and in its place · one coherent message · tone · nothing
  forbidden (a price not in the facts, law, a promise). Plus code checks (no repeat, no paid offer to a paid client).
- **The report:** the score per conversation and overall, every reply under 2 with the judge's reason. Run before this task
  (the baseline), after each part, and **before every deploy** (a drop = no deploy).

### 3.2 «حقايق المكتب»: approved facts Hazem answers from
- `hazem.office_facts` (migration): `key`, the fact in one short sentence, example questions, active, approved by. A panel
  page «حقايق المكتب» (manager) to read, edit, switch on/off; the facts that hold numbers take them from «الأسعار» and
  «بيانات المكتب» (never typed twice).
- A first list for the owner's approval (§5.1): who the consultation is with, its length, phone/video apps, the office
  meeting and the address, the hours, how to pay, the deadline of a held time, the refund, the case fees and the 30 days,
  the written services and their prices, the urgent call (when the lawyer calls, at night), privacy («بياناتك عند المكتب
  بس»), «Hazem is a digital assistant».
- **The classifier** gets a new field: `asked` (the client's question in a few words) and `facts` (keys from the closed list
  of active facts, injected into the prompt). **The writer** answers `asked` from those facts only; `ReplyGuard` checks every
  number and price against them. No fact for it → «هسأل المكتب ويرد على حضرتك هنا» and a task, never the nearest box.

### 3.3 One coherent reply per turn
- **A question is answered, not followed by an offer:** after an office question or a fact, the offer comes only when the
  client has no booking open, has not seen the offer in his last 3 turns, and asked nothing that makes it odd («مين؟»).
- **A booking step starts only from a booking wish** (`consultation_request`, a press), never from a question that names a
  service; and never replaces a booking already in progress (a pick or a mode waiting: the question is answered, then
  «نكمّل الحجز؟» with the step he was on).
- **An appointment whose time has passed no longer holds the request open** for the welcome: a known client writing after it
  gets «أهلًا بحضرتك تاني يا …» and the welcome's two buttons, even before the office marks it «خلص» (today a confirmed
  appointment counts as open until the panel marks it, so ***89 at 18:04, after his 17:30 call, got no welcome).
- **«محتاج إيه؟» never comes bare:** wherever Hazem asks what he needs (`greeting`, `about_hazem`, a thank-you's follow-up),
  the welcome's two buttons «خدمات المكتب / أحكي مشكلتي» go under it, so the client can always pick.
- **The writer joins the turn's everyday parts into one message** (the answer + the next step), the buttons kept.
  `about_lawyer`, the office answers, the legal-question refusal and the consultation offer's body become writable (their
  prices are facts, checked).

### 3.4 Memory of the conversation
- The classifier sees the last 6 messages (both sides), what he has open, and **what is in progress** (a mode chosen, a time
  shown, the name asked).
- The writer sees the last 10 and the client's gender for the verb forms: the name's gender, and **his own words when they
  say otherwise** («عايزه اخلع جوزي» → feminine, no «أستاذ»).

### 3.5 The loop after the release
- Every week: the review list, plus 20 real conversations sampled at random, judged as in 3.1. **Every reply the office finds
  wrong becomes a conversation in the suite**, so it never comes back.

## 4. Done when
- The baseline score is written; the five conversations of §1 all score 2 on «answered what was asked» and «one coherent
  message».
- The suite's overall score reaches the owner's bar (§5.2) with no reply that gives law, an unapproved price or a promise.
- The classifier is no worse than `classify-v14` on the old sets; all checks pass (`backend-test`, `web-test`, `wa-tests`,
  `healthcheck`).

## 5. Decisions for the owner (recommendation first)
1. **Approve the facts list** (we write it from DECISIONS and «بيانات المكتب»; the owner reads every line). In particular:
   is the consultation **always** with Al-Mostashar Saad himself?
2. **The bar:** an overall score of 95% (2 = good) and 100% on the forbidden checks — recommended.
3. **The judge:** Claude Opus 5.5 (the strongest, it runs only in tests) — recommended.
4. **T-07 widens a little:** the writer may answer a question **from approved facts** (not only rephrase a text); law,
   prices outside the facts and promises stay out — recommended.

## 6. Order of work
1. 3.1 the suite, the runner and the judge → **the baseline** (the report goes to the owner before anything changes).
2. 3.3 one coherent reply (the biggest effect on the cases of §1) → measured.
3. 3.2 office facts (the table, the panel page, the classifier's `asked`/`facts`, the writer) → measured.
4. 3.4 memory and gender → measured.
5. DECISIONS v1.45, the report, all checks; then a week on the test numbers with 3.5.

## 7. Not in this task
Legal answers (never), the real Meta number, the domain and R2, selling or many offices.
