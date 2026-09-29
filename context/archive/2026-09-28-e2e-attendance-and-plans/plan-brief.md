# E2E: attendance and a trainer's plan — Plan Brief

> Full plan: `context/changes/e2e-attendance-and-plans/plan.md`

## What & Why

Two browser-level specs for roadmap S-32. They protect two flows that cross auth, routing, the API
and the database, and that no single test covers end to end. First, a trainer's "Nieobecny" on a
started class returns the member's karnet entry and shows in Historia. Second, a plan built in the
builder, reordered by drag, reaches the member's Mój plan in that order and opens an exercise's detail.

## Starting Point

S-31 left a support layer (`support/fixtures`, `club` builders with self-registering cleanup,
persona contexts, `openClassBookings`). It cannot yet produce a class that has started, would fail
cleanup on one, and has no way to create exercises.

## Desired End State

`absence-returns-karnet-entry.spec.ts` and `trainer-plan-reaches-member.spec.ts` pass on the local
database, pass again when re-run and in parallel, and run in the pre-push gate. `e2e/CLAUDE.md` tells
the next author how to arrange a started class and an exercise.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Getting a started class | Book a future class, then `PUT` its start into the last 6 days | Deterministic; the API allows correcting a past class, while waiting for a start is the forbidden wait-for-time pattern |
| Cleanup of a started class | Leave it behind as history | The API refuses both delete and cancel, and rewriting attendance history would create data production never makes |
| Karnet assertion | `4 z 5` before the mark, `5 z 5` after | Only the pair proves the absence returned the entry |
| Plan order | Real pointer drag on the cdk handle | The drag exists only in the rendered UI and `onDrop` has no unit test; a unit fallback is used only if it proves unstable |
| Exercise detail | Description + "Wykonanie", no video | The local gate must not depend on YouTube |
| Parameters | One meaningful parameter per row + one note | Proves parameters follow their row through the drag, without re-testing field mapping |
| Booking in the attendance spec | Through the API | S-31 already drives the staff booking UI |

## Scope

**In scope:** support additions (`startClass`, past slots, past-dated karnet, started-class
cleanup, `createExercise`); two specs; `e2e/CLAUDE.md` update.

**Out of scope:** production code; absent→present round trip; ATT-03–06, PLAN-02–06, MBR-07/08;
video playback; phone drag; deleting plans or members.

## Architecture / Approach

Arrange through the admin API (`club`), drive through the UI only where the risk lives. One persona
per browser context: the trainer marks attendance at phone width through `openClassBookings`, and
builds the plan at desktop width. The member reads their own Start, Historia, Mój plan and the
exercise detail.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Support layer | Started classes, past-dated karnet, tolerant cleanup, exercises | Breaking an existing spec through `showWeekOf` or `removeClass` |
| 2. Attendance spec | Absence → Historia + entry back | Past-slot collisions (retry covers it) |
| 3. Plan spec | Builder → Mój plan → detail | cdk drag stability under Playwright |

**Prerequisites:** `docker compose up -d`, migrations applied, S-31's support layer (on `main`).
**Estimated effort:** ~1–2 sessions across 3 phases.

## Open Risks & Assumptions

- Leftover started `E2E` classes pile up in past weeks of the local schedule. They collide only with
  other past slots, which the retry covers.
- The cdk drag may prove unreliable in Playwright. The plan's fallback keeps the E2E at the order
  exercises were added in and adds an `onDrop` unit test.
- The assumption that an unmarked started class counts as a used entry (`4 z 5`) rests on the S-27
  derivation. If it does not hold, step 2 of the attendance spec will say so first.

## Success Criteria (Summary)

- Both specs are green, re-runnable and parallel-safe, without sleeps or fixed times.
- Each spec goes red at its own step when its outcome is broken on purpose.
- The existing E2E suite stays green.
