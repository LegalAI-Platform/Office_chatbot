# 2026-11-14 — v1.115: when the office answers, what the client needs, and a second deploy within the hour no longer fails on old log lines

Mohamed Emad's session. Deploying v1.114 rolled back twice:
- The first time, the urgent alert's test switch had been turned on again from the panel at 21:01. It was turned off at 21:43 on his «اقفله ونزّل».
- The second time, the healthcheck's «the logs reach Loki (Alloy sent lines in the last hour, none dropped)» failed.

He asked «هو انا ليه ما اعرفش انزله دلوقتي؟». v1.114 went out at 21:57 with the monitoring stopped for the deploy, on «أيوه نزّلها». On the lasting fix he said «أيوه، صلّحها». Branch `fix/alloy-old-lines` (worktree `K:\wt\cw-username`), from `main` at 679ab45.

## What the server showed (read only)

- About 133 lines were dropped in that hour, all with `reason="ingester_error"`, and all in the 10 minutes of the two deploys.
- Loki's log said why: «entry too far behind, entry timestamp is: …T18:28…, oldest acceptable timestamp is: …T18:46…» for the stream `container="office-chatwoot-init-1"`.
- So the one-shot `chatwoot-init`, started again by each deploy and each rollback, still had its 20:28 run in its log. Alloy sent those lines again, and Loki refused the ones more than an hour behind the stream's newest.
- Alloy dropped only lines older than 6 hours. Lines from 1 to 6 hours old went to Loki and were refused there.

## What changed

| What | Where |
|---|---|
| Only the last 50 minutes of a log go to Loki (`stage.drop`, `older_than = "50m"`, the reason `older_than_50m`), so nothing Alloy sends is ever more than an hour behind. Older lines stay in Docker and are counted apart from the lines Loki lost | `infra/monitoring/alloy/config.alloy` |
| What «entry too far behind» means, and the two ways to deploy when such a drop already sits in the last hour: wait, or stop the monitoring for the deploy | `docs/runbooks/monitoring.md` (LogShippingFailing) |
| Docs | DECISIONS-v1.115, CLAUDE.md, START-HERE |

## Checked

- Docker is not running on this PC. Alloy's config check and the masking test run in the CI («Monitoring config»).
- The change is one stage the config already had, with a shorter window.

## For the owner

- The deploy that brings this restarts the monitoring, because its config changed.
- If a deploy fails this way again before then, stop the monitoring for the deploy, as on 2026-11-14 (the runbook says how).
- After an Alloy outage longer than 50 minutes, the gap's older lines stay in Docker only (`docker compose logs`), not in Grafana.

## The second part: when the office answers, and what the client needs

Before the merge, he asked to read the chat of a client at 21:50 (read only):
- The client sent «السلام عليكم» and a medical report as text. The AI had low confidence, so the reply was `other`, and Hazem welcomed him without a word on the report.
- After «💬 أحكي مشكلتي» and «دا تقرير الحاله بتاعتي» (`tell_problem`, the matter unclear), the flow would have asked him to tell his matter again. v1.111's «never twice in a row» turned that into «معلش لو مش واضح…».
- After «نكلم حد»: «تمام، بلّغت المكتب… وحد من المكتب هيرد على حضرتك هنا», at 21:52, with no time.

He said «هو حد اصلا من المكتب هيرد عليه دلوقتي؟ المفروض يقول له بكره… على حسب المواعيد», and asked «هو ده حل عام ولا انت بتحل الكيس دي بس؟». On the plan, with its texts, he said «تمام نفّذ». The same PR as the Alloy fix, as he asked.

| What | Where |
|---|---|
| B-06 follows the office's hours of the panel (`booking.office_*`, 10:00–20:00 on the server; unset = 10:00–24:00 as before). Within the hour while it answers; in its last hour, after it and before it opens, the next opening | `Office.Domain/Tasks/OfficeHours.cs` (`DueAt(now, hours)`, `NextAnswer`) |
| Every office task's due time with those hours: the conversation's (`Plan.Task`) and the 14 places in the workers and the panel | `HazemResponder.cs` (`Plan.OfficeWindow`), `Office.Infrastructure/Persistence/OfficeDue.cs` |
| A reply that promises a person (`OfficeAnswersKeys`: human_request, handoff_waiting, service_detail, existing_matter, urf_request, the booking's «بلّغت المكتب», rating_low, the received ones), outside those hours, is followed by `office_answers_later`, word for word, with the hours and «بكرة» / «النهارده» | `HazemResponder.OfficeAnswersWhen`, `BookingWords.Clock` |
| Asked to tell his matter, he told something whose need the AI cannot place: `ask_need` with the services and a person, instead of `services_tell` again. A second time it is the same step, so v1.111 applies. The welcome says it got what he wrote about his matter (the writer's purpose) | `HazemResponder.TellOrAskNeed`, `Writing.cs` |
| The two texts | migration `0086_office_answers_and_ask_need.sql` |
| Tests | `V115OfficeAnswersTests.cs` (16). The DECISIONS texts check reads DECISIONS-v1.115 and counts 153 |

### Checked

- `dotnet build` (warnings are errors): clean.
- Unit tests 953 of 953 (16 new). Every earlier test is unchanged: «عاوز احكي مشكلتي» with nothing told before still gets `services_tell` in every situation (Task16SituationTests).
- Migration 0086 and the integration tests run in the CI.
