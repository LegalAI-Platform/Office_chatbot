# 2026-11-02 — v1.47: typed choices (B-32)

## Why
In Ahmed Amin's test chat (2026-11-01, 23:52–23:59), the list of tomorrow's office times was on the screen:
- «بكره الصبح» got the two kind buttons again.
- «تمام بكره 10 وربع الصبح», a time on that same list, was read `consultation_request` and got the consultation offer from the start.

The cause is general. Only presses were read by code; typed words went to the AI as a new message in every step that waits on buttons or a list.

## What changed
- `SituationFacts.Waiting` holds the buttons or rows that the client's last answered turn ended with, read from `hazem.turns.actions` for up to 24 hours (RatingStore).
- `classify-v16`:
  - the waiting choices (numbered) and today's day and time are in the context;
  - new fields `picked`, `asked_time` and `time_exact`.
- `HazemResponder`:
  - typed words with choices waiting are read once (that reading is reused);
  - a valid `picked` is answered as its press, by replacing the turn's message with the interactive reply;
  - `asked_time` on a list of times:
    - the shown row is answered as its press;
    - a free exact time is held as `Pick`;
    - otherwise `More` brings the free times from that instant.
- `Flow.KeepTheStep`: with times or the call-app question waiting, a reply that would be the consultation offer becomes `booking_step_again` plus that step again. The next time it is `unclear_again` and a task.
- Migration 0048 adds `booking_step_again`, with its writer purpose.
- The suite gains `real-ahmed-typed-time`, for 206 conversations.
- New unit tests: `Task47TypedChoiceTests`. Unit 619/619.

## Not done / needs
- The integration tests (the RatingStore query) run in CI; the local stack was not up.
- `quality-eval` was not run; the owner runs it before deploying.
- This branch builds on `bot-loops` (PR #79); merge that first.
