# Booking Invariants — Plan Brief

> Full plan: `context/changes/testing-booking-invariants/plan.md`

## What & Why

Rollout Phase 1 of `context/foundation/test-plan.md`: protect "no overbooking" and "the karnet
decides who trains" (risks #1, #2). The headline races are already tested; this change closes the
gaps where a regression would pass green today — before S-18 moves the booking handler.

## Starting Point

Capacity races, the cross-class entry race, release/shrink races, pass range boundaries and trainer
ownership all have database-asserted integration tests. But every test class starts at 10:00 UTC, the
boundary tests compute their expected date with production code, and the block cascade and pass edit
have no test on the entry pool.

## Desired End State

A UTC-date regression in the karnet gate turns tests red in summer and winter; no boundary test
shares its oracle with the code under test; blocking a member provably returns their entries; the
two pass-stamp rotations are pinned. G5 is a recorded product question, and §6.1 tells the next
person how to add a booking invariant test.

## Key Decisions Made

| Decision | Choice | Why |
|---|---|---|
| Entries consumed by a club-cancelled class (G5) | Raise as a roadmap question, no test | Pinning it would cement what may be a member-harming bug |
| Date oracle | Literal dates in new tests; rewrite the 3 existing boundary tests | A test computing its expectation with `ClubTime` cannot catch a bug in `ClubTime` |
| Pass edit vs booking | Deterministic stamp-rotation test | The losing interleaving cannot be forced from the API |
| Block cascade | Behavioural (re-book after unblock) + stamp assertion | Proves the entry is back and that the race mechanism is present |
| Location | `AdminBookingEndpointTests.cs`, pass edit in `MembershipPassEndpointTests.cs` | Existing helpers; suites own their routes |
| Research | Grounded inline during planning, no `research.md` | User went straight to planning |

## Scope

**In scope:** midnight-crossing tests (UTC+2 and UTC+1, both directions); de-oracling three boundary
tests; cascade entry return; pass-edit stamp rotation; roadmap question; §6.1 cookbook.

**Out of scope:** production code (including G5); new races for already-covered cases; pass-edit race
test; SPA; e2e.

## Phases at a Glance

| Phase | What it delivers | Key risk |
|---|---|---|
| 1. Club-local date boundary | 4 midnight-crossing tests + 3 de-oracled tests | Fixed 2038 instants colliding on the club-wide overlap rule |
| 2. Entry pool's other writers | Cascade entry-return test + pass-edit stamp test | Cascade test passing for an arrangement reason |
| 3. Question + cookbook | Roadmap Q7, test-plan §6.1/§6.6 | — |

**Prerequisites:** Docker running (Testcontainers SQL Server).
**Estimated effort:** one session, three short phases.

## Open Risks & Assumptions

- Assumes the admin unblock route exists alongside block (FR-004); the implementer confirms it.
- `roadmap.md` has uncommitted edits in the working tree; Phase 3 appends only.

## Success Criteria (Summary)

- Each phase's mutation check turns its named tests red, and reverting turns them green.
- `dotnet test` is green at the end.
