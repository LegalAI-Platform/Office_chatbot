# 2026-11-14 — v1.113: a username client's message shows once in Chatwoot

Mohamed Emad's session, on the go-live day: the office's number is on Hazem and Hazem answers every client since 16:58
(v1.112 on the test server, 89c5d78). He asked to read a client's chat, then said «انا كان عندي كمان مشكله تانية، هي ظاهره في
الشات ووت ان الرساله بتاعتها جت اكتر من مره». On the proposed fix he said «أيوه، صلّحه». Branch `fix/chatwoot-username-early`
(worktree `K:\wt\cw-username`), from `main` at 89c5d78.

## What the server showed (read only)

- One text message at 17:41:47 Cairo time. It came in one webhook event, made one row in `app.wa_inbound_messages` and one
  turn, and Hazem sent one reply (delivered).
- In Chatwoot, the same incoming message shows 8 times, from 17:41:48 to 17:42:10 (about every 3 s), then Hazem's reply.
- The contact key is a BSUID (`EG.`, 19 characters): the account has a WhatsApp username, so Meta sent `from_user_id` and no
  `from`. It is the first such client on the server: 1 key, 1 message.
- The API's log has 7 failed `INSERT INTO hazem.chatwoot_inbound` (EF's `CommandError`), at the times of the first 7 copies,
  then «Chatwoot: 1 turn(s) copied» at 17:42:10.
- The live check constraints on `wa_id`:
  - digits only: `hazem.chatwoot_inbound`, `hazem.list_entries`, and `app.waha_menus` (the WAHA test channel);
  - a BSUID too (0039): `hazem.persons`, `hazem.turns`, `hazem.chatwoot_threads`, `hazem.chatwoot_replies`.
- No other conversation repeats a message. The only other pair of equal lines is two different photos, each shown as
  «📷 صورة».

## Why

- 0072 (v1.97) posts a client's words to Chatwoot as soon as n8n stores them. It then records the wamid in
  `hazem.chatwoot_inbound`, so the turn does not post the words again.
- 0072 made that table after 0039, with 0011's digits-only check. So for a BSUID the record failed, and the mirror swallowed
  the exception.
- The next round (`PollSeconds`, about 3 s) found the words still unrecorded and posted them again, until the turn was stored
  (`EarlyMessagesAsync` skips the messages of a stored turn).
- The turn found no record either, so it posted the words an 8th time, before Hazem's reply.

## What changed

| What | Where |
|---|---|
| The check on `hazem.chatwoot_inbound` takes every contact key, with the pattern 0039 gave the other hazem tables | migration `0084_chatwoot_inbound_username_keys.sql` |
| The early post is taken before it is sent (`ClaimEarlyAsync`: `INSERT … ON CONFLICT DO NOTHING`, like `ClaimReplyAsync`). Words that cannot be taken wait for their turn and go once, with Hazem's answer. Words whose post fails are given back to their turn (`ReleaseEarlyAsync`), as before | `Chatwoot.cs` (`ChatwootMirror`, `IChatwootStore`), `ChatwootServices.cs` (`ChatwootStore`) |
| When early posts fail, one warning line a round: where, the error's kind and the first line of its message, never a number or the words | `ChatwootMirror.EarlyFailures`, `ChatwootWorker` |
| Tests | `V113ChatwootEarlyTests.cs` (5 unit tests), `ChatwootApiTests.A_username_clients_words_reach_chatwoot_once` (integration) |
| Docs | `DECISIONS-v1.113.md`, CLAUDE.md, START-HERE |

## Checked

- `dotnet build` (warnings are errors): clean.
- Unit tests: 919 of 919, 5 of them new. With the old order (post, then record) the first new test would get 7 posts.
- Docker is not running on this PC, so migration 0084 and the integration tests run in the CI only.

## For the owner

- After the deploy, a username client's words reach Chatwoot once. Nothing changes for clients with a number.
- The 7 extra copies from 17:41 are still in that Chatwoot conversation. Deleting them is the owner's call.
- Found here, not done:
  - `hazem.list_entries` (the office's lists: blocked, lawyer_direct, existing_client) still takes digits only, so a username
    client cannot be put on a list.
  - «انسى الرقم» (`hazem.forget_wa_id`, last made in 0039) does not delete a number's rows in `hazem.chatwoot_inbound`, which
    0072 made later. On the server, 131 rows belong to 3 numbers that have no client any more.
  - The same chat showed a booking case: «نيجي» named the office meeting and the AI read `consultation_mode = office`, but
    `KindWords` has no «نيجي», so Hazem asked «تليفون ولا مكتب؟» instead of offering the office's times. That fix waits for
    the owner.
