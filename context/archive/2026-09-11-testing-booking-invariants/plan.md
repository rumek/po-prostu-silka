# Booking Invariants — Test Rollout Phase 1 Implementation Plan

## Overview

Rollout Phase 1 of `context/foundation/test-plan.md` (risks #1 and #2). The booking suite already
defends the headline races; this change adds tests only where a regression would currently pass
green — the club-local date boundary, and the two writers other than the booking itself that touch
the karnet entry pool. No production code changes.

## Current State Analysis

Grounded directly against the code (no `research.md` was produced for this change; the grounding
below replaces it).

Already covered, with database-level assertions rather than HTTP counts:

- Capacity race on both routes — `BookingEndpointTests.cs:607`, `AdminBookingEndpointTests.cs:723`.
- Entry-pool race across N+1 classes — `AdminBookingEndpointTests.cs:574`.
- Release racing a booking — `BookingEndpointTests.cs:642`; capacity shrink racing a booking —
  `BookingEndpointTests.cs:711`; class stamp rotation on capacity edit — `BookingEndpointTests.cs:684`.
- Pass range day-before / day-after / exact-day — `AdminBookingEndpointTests.cs:442,462,482`;
  exhausting entries — `:499`; release returns the entry — `:521`; trainer ownership — `:324`.

Gaps where a regression passes today:

- **G1 — club-local vs UTC date.** Every test class starts at 10:00 UTC
  (`AdminBookingEndpointTests.cs:59-61`), where the UTC date and the Warsaw date always agree. The
  gate derives the date from `ClubTime.ToClubLocal(entity.StartsAt)` (`BookingEndpoints.cs:320`); a
  change to `DateOnly.FromDateTime(entity.StartsAt.UtcDateTime)` — plausible during S-18's move of
  this handler — fails no test.
- **G1b — oracle problem.** The three boundary tests compute the expected date with `DateOf`, which
  calls `ClubTime.ToClubLocal` (`AdminBookingEndpointTests.cs:~419-420`). They share the conversion
  under test and cannot catch a bug in it.
- **G2 — block cascade returns entries.** `CancelActiveFutureForMemberAsync` cancels future bookings
  and rotates each touched pass's stamp (`BookingStore.cs:92-104`). The cascade tests
  (`AdminBookingEndpointTests.cs:768`, `BookingEndpointTests.cs:914`) assert booking status only;
  neither the returned entry nor the rotation is asserted.
- **G4 — pass edit rotates the pass stamp.** `UpdatePassAsync` rotates `pass.ConcurrencyStamp`
  because lowering `EntryCount` changes the pool a concurrent booking counts against
  (`MembershipPassEndpoints.cs:326-330`). Nothing asserts it; the class-side twin
  (`BookingEndpointTests.cs:684`) shows the pattern.

Deliberately not tested — **G5**: a cancelled class keeps its bookings Active, so the entries stay
consumed. S-16 recorded this as existing behaviour (`context/archive/2026-09-09-membership-pass-and-staff-booking/plan.md:88-90`),
but it reads as a member losing an entry for a class the club cancelled. Decision: raise it as a
product question; do not pin it with a test.

## Desired End State

- A booking against a class whose UTC date differs from its Warsaw date is admitted or refused on
  the Warsaw date, in summer (UTC+2) and winter (UTC+1), in both directions; reintroducing a UTC-date
  derivation turns at least two tests red.
- No boundary test derives its expected date from `ClubTime`.
- Blocking a member provably returns the entries their cancelled future bookings held.
- Removing the pass-stamp rotation from `UpdatePassAsync` or from the block cascade turns a test red.
- G5 is recorded as an open roadmap question; `test-plan.md` §6.1 describes how to add a booking
  invariant test.

### Key Discoveries:

- The date gate: `BookingEndpoints.cs:320-326`; the entry gate: `:331-335`; both stamps rotate in
  one save: `:352-361`.
- Test DB access pattern for DB-level assertions: `AdminBookingEndpointTests.cs:63-65,181-186,225-232`.
- `fixture.IssuePassAsync(memberId, entryCount, validFrom, validTo)` writes through the DbContext
  (`IntegrationTestFixture.cs:263-287`) — use it for arrangement, never the pass API under test.
- The overlap rule is club-wide and all booking suites share one database
  (`AdminBookingEndpointTests.cs:23-26`) — fixed-instant classes must use a year no other suite uses.

## What We're NOT Doing

- No production code change, including for G5.
- No new race test for pass edits (the losing interleaving cannot be forced from the API — same
  reasoning as `BookingEndpointTests.cs:674-681`).
- No unit test extracted for the date derivation — it is inline in the handler, and extracting it is
  a production change; the integration test covers it at the real boundary.
- No SPA tests; no e2e; no changes to the member-route suite beyond what the plan names.
- No re-test of already-covered races (list above).

## Implementation Approach

All new tests go into `AdminBookingEndpointTests.cs`, which already owns the staff route, the
passless-member helper, the DB-read helpers and a karnet-aware arrangement. Fixed-instant classes live
in **2038**, unused by any other suite. Every expected date in a boundary test is a literal written
from the requirement ("a 00:30 Warsaw class on the 15th is covered by a pass valid on the 15th"), not
computed.

## Critical Implementation Details

- **Offsets are the oracle, so write them down.** Europe/Warsaw is UTC+2 in July and UTC+1 in
  January. `2038-07-14T22:30Z` is 00:30 on 15 July in Warsaw; `2038-01-20T23:30Z` is 00:30 on
  21 January. Put that arithmetic in a comment beside each literal so a reader can check it without
  running code.
- **Each fixed-instant class needs its own slot.** Two tests using the same 2038 instant collide on
  the club-wide overlap rule; give every test a distinct day.

## Phase 1: Club-local date boundary

### Overview

Close G1 and G1b.

### Changes Required:

#### 1. Fixed-instant class helper

**File**: `tests/po-prostu-silka.Tests/AdminBookingEndpointTests.cs`

**Intent**: Arrange a class at an exact UTC instant (instead of `NextSlot()`), so a test can place a
class across the UTC/Warsaw date line.

**Contract**: a private helper taking `HttpClient admin, DateTimeOffset startsAt` and returning
`ClassBody`, created through the admin class API exactly like `AnotherClassAsync`.

#### 2. Midnight-crossing tests

**File**: `tests/po-prostu-silka.Tests/AdminBookingEndpointTests.cs`

**Intent**: Prove the gate reads the Warsaw date. For each season, two tests: a single-day pass on
the **Warsaw** date admits the booking; a single-day pass on the **UTC** date refuses it with
`no_valid_pass` and writes no row.

**Contract**: four facts (or one theory with four rows):
- summer, class `2038-07-14T22:30Z`: pass 2038-07-15..2038-07-15 → 200; pass 2038-07-14..2038-07-14 → 409 `no_valid_pass`.
- winter, class `2038-01-20T23:30Z`: pass 2038-01-21..2038-01-21 → 200; pass 2038-01-20..2038-01-20 → 409 `no_valid_pass`.
Each uses a fresh passless member and its own class.

#### 3. Remove the oracle from the existing boundary tests

**File**: `tests/po-prostu-silka.Tests/AdminBookingEndpointTests.cs`

**Intent**: Rewrite the three existing boundary tests (`:442`, `:462`, `:482`) to use fixed-instant
classes and literal pass dates, then delete `DateOf` if nothing else uses it.

**Contract**: same three test names and the same assertions; only the arrangement changes. No test
in the file references `ClubTime` afterwards.

### Success Criteria:

#### Automated Verification:

- The file builds warning-free: `dotnet build tests/po-prostu-silka.Tests`
- The booking suites pass: `dotnet test --filter "FullyQualifiedName~AdminBookingEndpointTests|FullyQualifiedName~BookingEndpointTests"`
- Mutation check: temporarily change `BookingEndpoints.cs:320` to derive the date from `entity.StartsAt.UtcDateTime`; at least two of the new tests fail; revert (the revert is confirmed by `git diff --stat src/` being empty)

#### Manual Verification:

- Each literal instant has a comment stating its Warsaw reading, and the arithmetic checks out

**Implementation Note**: After completing this phase and all automated verification passes, pause
for manual confirmation before proceeding.

---

## Phase 2: The entry pool's other writers

### Overview

Close G2 and G4.

### Changes Required:

#### 1. Block cascade returns the entry

**File**: `tests/po-prostu-silka.Tests/AdminBookingEndpointTests.cs`

**Intent**: Prove that blocking a member gives back the entries their cancelled future bookings held
— behaviourally, and through the stamp that makes it race-safe.

**Contract**: one test. Arrange a passless member with a one-entry pass (fixture), book them into a
future class, read the pass's stamp from the DB, block them through the admin API. Assert: zero
active bookings carry the pass id (DB), and the pass stamp changed. Then unblock through the admin
API and book the same member into a second class — it succeeds, which is only possible if the entry
came back.

#### 2. Pass edit rotates the pass stamp

**File**: `tests/po-prostu-silka.Tests/MembershipPassEndpointTests.cs`

**Intent**: The pass-side twin of `Lowering_capacity_rotates_the_class_stamp` — deterministic,
because the interleaving it guards cannot be forced from the API.

**Contract**: one test. Issue a pass, read its stamp from the DB, lower `EntryCount` through the
admin edit route, assert 200 and a different stamp. The doc comment names the line whose removal
turns it red, in the style of `BookingEndpointTests.cs:672-682`.

### Success Criteria:

#### Automated Verification:

- The file set builds warning-free: `dotnet build tests/po-prostu-silka.Tests`
- The touched suites pass: `dotnet test --filter "FullyQualifiedName~AdminBookingEndpointTests|FullyQualifiedName~MembershipPassEndpointTests"`
- Mutation check: temporarily remove `pass.ConcurrencyStamp = …` from `MembershipPassEndpoints.cs:330` and the rotation loop in `BookingStore.cs:101-104`; the two new tests fail; revert (`git diff --stat src/` empty)

#### Manual Verification:

- The cascade test fails for the right reason under the mutation (stamp assertion), not because of arrangement

**Implementation Note**: After completing this phase and all automated verification passes, pause
for manual confirmation before proceeding.

---

## Phase 3: Record the open question and the cookbook entry

### Overview

Carry G5 to its owner and leave the pattern behind for the next booking test.

### Changes Required:

#### 1. G5 as an open roadmap question

**File**: `context/foundation/roadmap.md`

**Intent**: Add question 7 to `## Open Roadmap Questions`: a class the club cancels keeps its
bookings Active, so each booked member loses a karnet entry — intended, or should cancelling a class
return entries? Owner: user. Block: none. Cite the S-16 plan note.

**Contract**: one appended numbered item; nothing else in the roadmap changes.

**Adapted during implementation.** The question landed in the working tree but is NOT in this
change's commits: `roadmap.md` already carried the user's own uncommitted edits, so staging it would
have swept those in. The user commits it. Separately, Phases 1 and 2 share one commit because both
edit `AdminBookingEndpointTests.cs` and cannot be split by path; their Progress rows carry the same
SHA.

#### 2. Cookbook §6.1 and a §6.6 note

**File**: `context/foundation/test-plan.md`

**Intent**: Replace the §6.1 placeholder with the booking-invariant recipe this change shipped, and
append a 2–3 line §6.6 note about fixed-instant classes and literal dates.

**Contract**: §6.1 carries Location (`tests/po-prostu-silka.Tests/`, the suite owning the route),
Pattern (arrange through the fixture, act through the API, assert on the DB; races via separate
clients and `Task.WhenAll`; stamp rotation asserted deterministically where the race cannot be
forced), Oracle rule (literal dates and counts, never `ClubTime` or a production count), Reference
tests (the concurrency test at `AdminBookingEndpointTests.cs:574` and the new midnight-crossing
tests), Run command (`dotnet test --filter FullyQualifiedName~AdminBookingEndpointTests`). §1–§5 are
not edited.

### Success Criteria:

#### Automated Verification:

- The full suite passes from the repo root: `dotnet test`

#### Manual Verification:

- §6.1 is enough for someone who has not read this plan to add the next booking invariant test

---

## Testing Strategy

### Integration Tests:

- Midnight-crossing admit/refuse, summer and winter (Phase 1).
- Existing boundary tests re-arranged onto literal dates (Phase 1).
- Block cascade returns an entry (Phase 2); pass edit rotates the stamp (Phase 2).

### Manual Testing Steps:

1. Run each phase's mutation check and see the named tests go red, then revert.
2. Read the literal-instant comments against a Warsaw clock.

## Performance Considerations

Seven or eight new container-backed tests; each is a handful of HTTP calls. Negligible next to the
existing suite.

## References

- Test plan: `context/foundation/test-plan.md` §2 risks #1–#2, §3 Phase 1
- S-16 plan (entries on class cancel): `context/archive/2026-09-09-membership-pass-and-staff-booking/plan.md:88-90`
- Class-side stamp test to mirror: `tests/po-prostu-silka.Tests/BookingEndpointTests.cs:672-704`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Club-local date boundary

#### Automated

- [x] 1.1 The file builds warning-free — 832b1c6
- [x] 1.2 The booking suites pass — 832b1c6
- [x] 1.3 Mutation check: UTC-date derivation turns at least two new tests red — 832b1c6

#### Manual

- [x] 1.4 Literal-instant comments state the Warsaw reading and check out — 832b1c6

### Phase 2: The entry pool's other writers

#### Automated

- [x] 2.1 The file set builds warning-free — 832b1c6
- [x] 2.2 The touched suites pass — 832b1c6
- [x] 2.3 Mutation check: removing either pass-stamp rotation turns the new tests red — 832b1c6

#### Manual

- [x] 2.4 The cascade test fails for the right reason under the mutation — 832b1c6

### Phase 3: Record the open question and the cookbook entry

#### Automated

- [x] 3.1 The full suite passes — f830720

#### Manual

- [x] 3.2 §6.1 is enough to add the next booking invariant test — f830720
