# 2026-11-14 — v1.114: an age corrected after the Urfi refusal is taken, as the guardian's is

Mohamed Emad's session, after v1.113 went to the server (1f7e808). He sent a Chatwoot conversation:
- A client asked for an Urfi marriage and gave the wife's age under 18, and Hazem refused.
- In the very next message the client corrected the age to 19 («نسيت»). Hazem answered with the closing text, word for word, twice.
- The client left, saying Hazem did not understand.

He said: «هو عدل السن… المشكله دي انا متاكد ان انا حليتها», and «دور في كل السيشنز». On the fix he said: «صلحها… عشان اكيد في كيسز تانية». Branch `fix/urf-age-correction` (worktree `K:\wt\cw-username`), from `main` at 1f7e808.

## What the search found

- No open or reverted PR touches this:
  - the open ones are chat docs (#129, #123) and the old #14;
  - the reverted #152 was the booking's tasks.
- On the server, «دليل التعامل» and the Urfi texts have not changed since 11-04, and no text was edited in the panel since 11-10.
- The owner's solution is in his chat of 2026-11-05 22:40 (v1.72, in `docs/chats/MohamedEmad-2026-11-09.md` of the open PR #129):
  - after a refusal, Hazem reads what the client says first and then decides («مش قاعده انت هتحطها، لا هو هيفكر ويجاوب»);
  - the session told him the age refusal «بيمشي في نفس الطريق», but that nothing a client says makes an age under 18 acceptable. It missed a correction of a mistyped age.
- What was built: `UrfRules.ApplyFacts` reopened a case refused for no guardian only. Its comment said «The age refusal has nothing to change: it stays».
- So the 24-hour closing text of v1.20 answered the corrected age. The AI's reading of the new age never reached the rules.

## What changed

| What | Where |
|---|---|
| A case refused for the age takes the ages the AI read after it (the last said, v1.74) through the same B-11 rules (`Settle`). 18 or more on both sides: a new case from where it stopped, with the other answers kept; the responder's v1.72 path says it back first («تمام، عدّلت السن: …» v1.75) and asks the next question. Under 18 still: it stays refused, closing text as before | `Office.Application/Urf/Urf.cs` (`ApplyFacts`) |
| One side alone corrects the pair the case has (`AgesOf`): after the refusal, while collecting, and at a held time (`ChangesBooked`: the office's, v1.62). With no ages known, one side alone is still no answer (v1.72) | `Urf.cs` |
| The comment of the reopening path | `HazemResponder.cs` (`Answer`) |
| Tests | `V114UrfAgeCorrectedTests.cs` (4) |
| Docs | DECISIONS-v1.114 (the block, B-11's rule, 1.72's line struck through), CLAUDE.md, START-HERE |

No new text and no migration. The refused case stays as it was. The lawyer or the manager checks the IDs before the time, as for every Urfi case (B-11.7).

## Checked

- `dotnet build` (warnings are errors): clean.
- Unit tests 937 of 937: 4 new, and every earlier Urfi test unchanged (v1.20's closing text, v1.72's guardian, v1.74 and v1.75's corrections).

## For the owner

- A question about another age after the refusal («لو هي 19 ينفع؟») still gets the closing text. Only an age **said** opens the case. Answering such a question would need a new approved text.
