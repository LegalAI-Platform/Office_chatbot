# 2026-11-14 — v1.112: one task per client for the same job, answers only from the office's facts, the Urfi documents' task after the payment

Mohamed Emad's session, right after v1.111 went to the test server (6a13026). He sent the bell full of one client's alerts and
asked «انا عايز اعرف حاجة بس، هو ليه في كل النوتيفيكيشنز دي؟ … انت مش حاسس ان الدنيا كده ملعبكة؟». On the proposal he said
«ماشي تمام اعمل كدا», and asked «ليه اصلا اتعمل عميل بحاجه الي متابعه من المكتب» of Mohamed Emad's alert. Branch
`fix/one-task-per-client` (worktree `K:\wt\one-task`), from `main` at 6a13026.

## What the server showed (read only, the chats of 10:07–10:35)

- Ahmed Amin's 23 minutes (10:08–10:31) made 9 tasks, each with its own alert; 4 were needed (his request, two questions that
  needed the office, the lawyer's pricing).
  - 6 `service_question`, one per question. Hazem answered 4 of them himself, from no office fact (the papers a sale contract
    needs), and the writer said `from_facts: false`, so each kept its task.
  - 1 `unclear_message`: the words with his ID photo («دي صوره البطاقه» and the car's details), which the code does not read
    (`WordsToOffice`), beside the photo's own pricing task.
- Mohamed Emad's Urfi time for 13:00 the same day: held 10:51:01, the documents' task 10:51:08, the receipt 10:51:44 (Hazem asks
  for the documents with it). `urf_due()` took a time only held, within 24 hours, at once.
- In the same chat, v1.111's «named again» path answered «ممكن تضيف شرط جزائي؟» with «طلب حضرتك مستني الورق», and the
  question reached nobody; and the privacy line went twice (the written reply, then the approved `office_privacy`).
- The bell itself already groups each client (Nehad's #154): the noise is the number of tasks behind it.

## What changed

| What | Where |
|---|---|
| A client's open task of the same type takes his later message instead of a new task: `staff_task_turns` (the turn joined), `joined_at`; its bell-only alerts (`task_new`, `task_human`, `task_rating`) are taken away and made again, never an e-mailed one. The office's missing detail and the paid urgent call keep their rules. A turn sent again joins nothing | migration `0083_one_task_per_client.sql`, `ConversationStores.cs` (`JoinOpenTaskAsync`), `PanelAlertServices.cs` (`COALESCE(joined_at, created_at)`, `Messages`) |
| The task page lists the words of every turn it holds, oldest first; a file's index counts through them | `PanelStores.cs` (`TurnsOfAsync`, `FileAsync`) |
| The bell's line starts with «N رسائل» when a task holds more than one; a task's Windows notification has the task's tag (`hazem-task-<id>`) with `renotify`, so it takes the old one's place | web `notify.ts`, `group.ts` (`MESSAGES`), `types.ts`; API `AlertDto.Messages` |
| `service_detail` written without the office facts and without saying it reached the office is refused (`answered_alone`, retried, else the approved text): the task is kept exactly when the client is told the office answers | `Writing.cs` (`SaysOfficeAnswers`), `Review.cs` |
| The words with a photo, a file or a press go with the turn's own task; with none, a task of their own, `attached_message` («رسالة كتبها العميل مع صورة أو اختيار: اقرأها») | `HazemResponder.cs` (`WordsToOffice`, `WordsGoWithTheTurn`), `Conversations.cs`, labels, `PanelMails.cs`, the type check in 0083 |
| The Urfi documents' task: the time paid (confirmed), an hour after the receipt, within 24 hours, no file | `urf_due()` in 0083 |
| An open request's details or questions (or its name again) are his words to the office (`service_detail` and a `service_question` task), never its status in their place | `HazemResponder.cs` (`AskService`) |
| The writer is told the approved text that follows its message (`After`) and never says it (`repeats_after`: four words of it, or its privacy line) | `Writing.cs`, `ReplyWriters.cs`, `Review.cs` |
| The integration tests' TRUNCATE lists take `staff_task_turns` with `staff_tasks` (its foreign key) | 17 test files |
| Docs | `DECISIONS-v1.112.md`, CLAUDE.md, START-HERE |

## Checked

- `dotnet build` (warnings are errors): clean. Unit tests 914 of 914 (8 new, `V112OneTaskTests.cs`; 3 updated for the new
  behaviour). The panel: `tsc --noEmit` clean, vitest 132 of 132 (2 new).
- The integration tests run in the CI only: the join through the API (one task, the link, the alert made again, a new task
  after it is done), the task page with a joined turn's words and file and the bell's count, the Urfi documents' timing.

## For the owner

- Hazem now says «وصل للمكتب» where he used to make up an answer. For him to answer «إيه الورق المطلوب», the lawyer writes each
  service's papers in «حقايق المكتب».
- Not done: the bell's title for a new task is still «عميل بحاجة إلى متابعة من المكتب» with the reason in the line under it.
