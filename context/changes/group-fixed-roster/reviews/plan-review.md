<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Group Fixed Roster Implementation Plan

- **Plan**: context/changes/group-fixed-roster/plan.md
- **Mode**: Deep
- **Date**: 2026-10-03
- **Verdict**: REVISE → SOUND (after triage)
- **Findings**: 1 critical, 2 warnings, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | FAIL |
| Blind Spots | WARNING |
| Plan Completeness | WARNING |

## Grounding

16/16 paths ✓, 7/7 symbols ✓ (TryBookAsync, ReturnEntryAsync, MayActOn, ClubTime.StartOfLocalDay, trainerGuard, …), brief↔plan ✓, Progress↔Phase ✓

## Findings

### F1 — Trainer's roster rights bypass the "own classes only" booking rule

- **Severity**: ❌ CRITICAL
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Architectural Fitness
- **Location**: Phase 1 §4–§6 (RosterBooking, RosterAuthorization, POST/DELETE/sync)
- **Detail**: AGENTS.md hard rule (S-16): "a trainer books into the classes they personally instruct", enforced by `BookingAuthorization.MayActOn` (BookingAuthorization.cs:49-51). The plan lets a trainer who instructs ONE upcoming class of a group manage its roster, and RosterBooking takes no principal, so their add/sync books into every upcoming class of the group, other trainers' included, and DELETE releases bookings there too. The plan never says it amends the rule.
- **Fix A ⭐ Recommended**: Limit trainer-triggered booking and release to classes they instruct (acting instructor id passed to RosterBooking and removal; null for admin); other trainers' classes stay gaps.
  - Strength: Keeps S-16 and MayActOn as the one definition; admin triggers unaffected.
  - Tradeoff: A trainer-made add leaves gaps in other trainers' classes; needs a gap reason word.
  - Confidence: HIGH — one comparison already used by every staff booking route.
  - Blind spot: Removal by a trainer leaves bookings in others' classes — must be stated.
- **Fix B**: Amend the hard rule explicitly (S-37 amends S-16).
  - Strength: Simplest model; matches the spreadsheet.
  - Tradeoff: Widens trainer power over other trainers' classes; AGENTS.md edit + tests.
  - Confidence: MED — product decision not asked of the club.
  - Blind spot: Substitutes keep this power while they instruct one upcoming class.
- **Decision**: FIXED (Fix A)

### F2 — Test-data reset will break on the new FK

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 1 §1
- **Detail**: TestDataSeeder.cs:234/:239 ExecuteDelete ClassGroups and Members; restrict FKs from GroupRosterEntries make a Staging Reset fail once any roster exists. The file is not in the plan.
- **Fix**: Add `db.GroupRosterEntries.ExecuteDeleteAsync()` before those deletes in TestDataSeeder (Phase 1).
- **Decision**: FIXED

### F3 — E2E: classes duplicated in the UI are not cleaned up; builder contract is off

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Completeness
- **Location**: Phase 4 §1
- **Detail**: The two copies made through "Powiel" are never registered with Cleanup, so each run leaves two booked scheduled classes 1–2 weeks ahead. `createClass` in club.ts always creates its own group (club.ts:146-156) and takes no classGroupId.
- **Fix**: Capture the duplicate response (waitForResponse) and register each copy with removeClass cleanup; extend createClass with optional `classGroupId`; order createGroup → issuePass → createClass.
  - Strength: Reuses the existing cleanup path.
  - Tradeoff: Slightly more test plumbing.
  - Confidence: HIGH.
  - Blind spot: Roster entry stays behind — acceptable with a unique E2E member.
- **Decision**: FIXED

### F4 — Karnet response reports stale entries after the hook

- **Severity**: OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 2 §2
- **Detail**: IssuePass builds its view with entriesUsed 0 and UpdatePass with a pre-save `used`; after the hook spends entries the response lies. The SPA refetches (member-passes.ts:302-304), so nothing visible breaks.
- **Fix**: Build the view after the batch with `used + report.Booked`.
- **Decision**: FIXED

### F5 — Roster edits on a deactivated group are undecided

- **Severity**: OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 1 §6
- **Detail**: The view carries IsActive but add/sync on an inactive group is not decided; its existing classes remain (FR-006) and the karnet hook still books into them.
- **Fix**: Add refused with `inactive_class_group`; sync and hooks still fill existing classes.
- **Decision**: FIXED
