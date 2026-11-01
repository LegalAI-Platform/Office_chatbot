# v1.43 report — 2026-11-01 — asking about a written service is not an order

From the conversation on the test number ***90 at 03:39: «لو عاوز اعمل عقد عمل بتعلوه» opened a contract-drafting order
(03:40:01), and from then on «انت فاهمني طيب؟» and «انت عارف اسمي؟» got «طلب حضرتك (صياغة العقد) مستني الورق». The owner:
«طب اي الحل», then «نفذ». Branch `order-on-start` from `main` (`195961c`).

## What was done

| Part | What |
|---|---|
| Words are a question | a written service named in words (review, drafting, notice) gets its text with «✅ ابدأ الطلب» under it (`btn_start_order`, the services list's row id); no order, no office task |
| The order opens | on that button or the services list's row (the office's `service_request` task with it), or with his papers (an image or a document) right after the service's text (`RespondToPapers`: the order, the files, `service_pricing`) |
| B-28 | an order still waiting for papers answers only «طلبي وصل لفين؟» (my_status); one the office works on (priced, waiting for payment, in progress) keeps answering as before |
| Migration 0044 | the button's text |

## Results

| Check | Result |
|---|---|
| `backend-test` | **801/801** (new: the 03:39 conversation; the papers start the order; the button does; the scenario table's order rows) |
| `healthcheck` | 159/0, 7 expected warnings |

## Not changed

The test order opened at 03:40 on ***90 is still open on the server (`collecting`); it is closed from the panel or left to the
owner. Unknown: whether Meta shows the button under texts longer than its body limit (the texts are short).
