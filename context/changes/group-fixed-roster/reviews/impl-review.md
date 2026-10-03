<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Group Fixed Roster

- **Plan**: context/changes/group-fixed-roster/plan.md
- **Scope**: Phases 1–4 of 4 (all automated items complete; manual items pending)
- **Date**: 2026-10-03
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 3 warnings, 5 observations

## Verification run

| Check | Result |
|---|---|
| `dotnet build po-prostu-silka.slnx -warnaserror` | PASS — 0 warnings, 0 errors |
| `dotnet test po-prostu-silka.slnx` | PASS — 860/860 (4 m 46 s) |
| `npm test` | PASS — 83 files, 1024 tests |
| `npm run quality:check` | PASS — Prettier and ESLint, kit rule included |
| `npm run build` | PASS — initial 586.63 kB (matches AGENTS.md), under 600 kB |
| Migration up/down against local DB | Not re-run in review; `Down` drops the table, FKs restrict (read in the code) |
| E2E (`npm run e2e`) | Not re-run in review (marked done at 1f01f64) |

## Triage (2026-10-03)

Fixed: F1 (Fix A), F2, F3, F4 (Fix A, accepted risk in plan), F5, F6, F7. Accepted: F8.
Re-verified after fixes: `dotnet test` 861/861, `npm test` 1025/1025, `quality:check` clean, build
586.60 kB.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

## Findings

### F1 — A concurrent roster removal can be undone by an in-flight batch

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/Application/Scheduling/RosterBooking.cs:70 (and `SyncRoster.cs`, `BookRosterIntoAsync`, `BookMemberIntoRangeAsync`)
- **Detail**: Roster membership is read once, before the batch (`roster.MemberIdsAsync` / `GroupIdsForMemberAsync`). It is never re-checked inside a booking transaction. Suppose a `RemoveFromRoster` commits while "Uzupełnij zapisy", a duplicate or a karnet hook is mid-loop. The loop then goes on booking the removed member into the group's future classes and spends their entries, and nothing ever releases those bookings. The class stamp only orders the release and the booking; it does not stop a booking after the release. The window is small, but a sync over a large roster is many sequential transactions.
- **Fix A ⭐ Recommended**: Re-check `roster.FindAsync(groupId, memberId)` per member, immediately before that member's classes (the batch would need a group id per target, which `RosterClass` could carry).
  - Strength: One extra seek per member; it shrinks the window from the whole batch to one member's classes.
  - Tradeoff: Does not close the race fully; a removal between the check and a booking still slips through.
  - Confidence: MED — club scale makes the residual window negligible.
  - Blind spot: The karnet hook spans several groups; it needs a group id per target.
- **Fix B**: Record as an accepted risk in the plan's "What We're NOT Doing", like the roster-size cap.
  - Strength: Zero code; consistent with the plan's stance on the soft roster cap.
  - Tradeoff: A removed member can silently keep bookings; staff must notice and release them by hand.
  - Confidence: HIGH — the scenario needs two staff acting on one group within seconds.
  - Blind spot: How often admin and trainer edit the same group at once.
- **Decision**: FIXED (Fix A) — `RosterClass` carries `GroupId`; `IGroupRosterStore.IsInRosterAsync`; `RosterBooking.BookAsync` re-checks membership per (member, group) before booking and skips silently when gone.

### F2 — Batch booking cost grows with roster × classes, and the change tracker is never cleared

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/Application/Scheduling/RosterBooking.cs:68-122
- **Detail**:
  - **Round trips:** each (member, class) costs ~5 queries plus a save: class find with includes, `FindActiveAsync`, `CountActiveAsync`, `FindCoveringAsync`, the consuming-entry count, then `SaveChanges`. That holds even for already-booked pairs, which are sync's main idempotent case.
  - **Change tracker:** it is cleared only on a lost race (`DiscardChanges`), so tracked classes, karnets and bookings pile up across the batch. `ClassStore.FindAsync` is a tracking query, so later reads return the stale tracked copy. Any outside write to that class or karnet during the batch then costs a guaranteed conflict and retry.
  - **Plan vs. scale:** the plan's estimate was ~48 transactions (6 × 8). That holds at club scale, but it is all one request on Azure SQL Basic DTU.
- **Fix**: Call `unitOfWork.DiscardChanges()` after each attempt. Pre-load the (member, class) pairs already actively booked in one query, as `GroupRosterQuery` already does, and skip them before calling the protocol.
  - Strength: The skip removes the bulk of sync's cost; the discard removes the stale-tracking conflicts.
  - Tradeoff: The skip duplicates the protocol's `already_booked` check outside the transaction. That is harmless: the protocol still decides, and a stale "booked" only means one skipped attempt.
  - Confidence: MED — the cost is reasoned from the code, not measured.
  - Blind spot: No timing measured against Basic DTU.
- **Decision**: FIXED — `IGroupRosterStore.ActiveBookingPairsAsync` pre-loads booked pairs (skipped before the protocol); `unitOfWork.DiscardChanges()` after every attempt.

### F3 — Booked count never shown after issuing/editing a karnet or creating from the class form

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: src/app/src/app/features/admin/members/member-passes.ts; src/app/src/app/features/admin/classes/class-form.ts
- **Detail**: Plan §3.2 says "The success count goes in the screen's existing success toast". `member-passes` has no success toast on issue or edit, and `class-form` navigates away without one. On those paths the admin sees only the skips panel, never "Zapisano N osób". Duplicate and the roster screen do show it.
- **Fix**: Add a `success` toast with `report.booked` when it is non-zero, on issue/edit in `member-passes` and on create from the form (or on `classes` when it reads the navigation state). Otherwise record an "Adapted during implementation" note.
- **Decision**: FIXED — `rosterBookedSentence` in `shared/roster-report/roster-lines.ts` (one sentence for every trigger); success toast on overlay create, form create (handed-over report now whole, `withSkips` filters the panel), and karnet issue/edit; specs added.

### F4 — A failure inside the batch answers 500 although the trigger committed

- **Severity**: 🔍 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: AddToRoster.cs, IssuePass.cs, UpdatePass.cs, CreateClass.cs, DuplicateClass.cs
- **Detail**: The batch runs after the triggering write has committed, as the plan requires. If it throws (a transient SQL error, or a `DbUpdateException` other than concurrency or unique), the client gets a 500 even though the karnet, class or roster row exists. A retry is safe: it hits `time_conflict`, the karnet overlap or `already_in_roster`, and "Uzupełnij" fills any gaps. But the user is told the action failed.
- **Fix A ⭐ Recommended**: Record as an accepted risk in the plan's "What We're NOT Doing", naming "Uzupełnij zapisy" as the recovery.
  - Strength: No code; recovery exists and every retry path refuses cleanly.
  - Tradeoff: A misleading error remains possible.
  - Confidence: HIGH — the failure mode is rare and recoverable.
  - Blind spot: None significant.
- **Fix B**: Catch around the batch, log, and answer the trigger's result with an "incomplete" flag the panel can show.
  - Strength: The user is told the truth.
  - Tradeoff: A new response field and a new SPA state across five triggers.
  - Confidence: MED — catching broadly risks masking real bugs.
  - Blind spot: How the SPA would word "incomplete" without a new failure table.
- **Decision**: FIXED (Fix A) — recorded as an accepted risk in plan.md "What We're NOT Doing".

### F5 — Gap view marks every gap "bookable" when entries cover only some

- **Severity**: 🔍 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Infrastructure/Scheduling/GroupRosterQuery.cs:118
- **Detail**: `pass.Left` is computed once per karnet, and every class is compared against it on its own. With 1 entry left and 3 gaps, all three show "można zapisać — uzupełnij zapisy". The sync then books one and reports two as `no_entries_left`. The screen promises more than the protocol delivers.
- **Fix**: Walk the gaps in start order and decrement a per-karnet running count as each is marked bookable, mirroring the batch's order.
- **Decision**: FIXED — running per-karnet count in `GroupRosterQuery`; test `The_gap_view_spends_entries_in_start_order_as_the_sync_would`.

### F6 — Capacity: add block hidden rather than disabled

- **Severity**: 🔍 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: src/app/src/app/features/groups/group-roster.html (`full()` branch)
- **Detail**: Plan §3.3 says "The add block is disabled at capacity with the count 6/6". The code hides the search and shows the hint "Skład jest pełny (6/6)" instead. The intent is kept, but the plan was not annotated (lessons.md: "Record necessary adaptations in the plan").
- **Fix**: Add an "Adapted during implementation" note to plan §3.3.
- **Decision**: FIXED — note added to plan.md Phase 3 §3.

### F7 — Unused `hasGaps` computed

- **Severity**: 🔍 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/app/src/app/features/groups/group-roster.ts:125
- **Detail**: `protected readonly hasGaps = computed(...)` is read nowhere, neither in the template nor in the spec.
- **Fix**: Delete it.
- **Decision**: FIXED — removed.

### F8 — All manual verification items still open

- **Severity**: 🔍 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: plan.md Progress — 1.4, 2.3, 3.4–3.7, 4.3
- **Detail**: Every automated item is `[x]` and verified green in this review. Every manual item is `[ ]`. ROSTER-01..08 in `context/testing/04-schedule-bookings-attendance.md` covers them. That includes the phone/tablet/desk check that lessons.md ("mobile-first but responsive") requires.
- **Fix**: Walk ROSTER-01..08 on the local app before merging, and tick the items.
- **Decision**: ACCEPTED — the user walks ROSTER-01..08 on the deployed environment after the merge.
