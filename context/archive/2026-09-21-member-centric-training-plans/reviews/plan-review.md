<!-- PLAN-REVIEW-REPORT -->
# Plan Review: A Member's Plan Is Reached Through the Member (S-22)

- **Plan**: context/changes/member-centric-training-plans/plan.md
- **Mode**: Deep
- **Date**: 2026-09-21
- **Verdict**: REVISE → SOUND after triage (all 7 findings fixed in the plan)
- **Findings**: 0 critical, 2 warnings, 5 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | WARNING |
| Blind Spots | WARNING |
| Plan Completeness | WARNING |

## Grounding

- 12/12 paths ✓
- 5/5 symbols ✓: `Assignable`, `notFound` kind, `toast.success`, `PagedResult.cs`, `member-list-failure.ts`
- brief↔plan ✓
- Progress↔Phases ✓

Verified by the sub-agent:
- Nothing else calls the removed endpoints (dashboard, e2e and seeds included).
- `app.routes.server.ts` needs no change (`**` prerender, static output).
- No spec enumerates the route table.
- The `MemberListFailure` table is reusable without registration.

## Findings

### F1 — Phase 4 misses two list reads, one in the suite's core race test

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 4 — Tests
- **Detail**: `TrainingPlanEndpointTests.cs:290` (`Assigning_again_replaces_the_previous_plan`) and `:343` (`Concurrent_assignments_leave_exactly_one_active_plan`, "THE TEST THIS WHOLE SUITE EXISTS FOR") both read `GET /api/trainer/plans`. The plan did not list them. The easy repair, deleting the assertion, would drop the one-active-plan pin, and the member-plan read cannot count plans.
- **Fix**: :290 asserts through the member-plan read. :343 counts active rows through the fixture's DbContext scope.
- **Decision**: FIXED

### F2 — Nav specs will pass vacuously; the defining admin+trainer case is untested

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 3 — Specs
- **Detail**: No admin+trainer fixture exists in `more.spec.ts` or `app.spec.ts`, and `!isAdmin()` exists only for that user.
  - Absence assertions on `/trainer/plans` (`app.spec` :128, :147, :183, :225; `more.spec` :99, :137) would stay green while testing nothing.
  - `app.spec` :164, the `it.each` at :206, and `more.spec` :102/:112 fail outright.
- **Fix**: Retarget the absence assertions to `/trainer/members`, and add an admin+trainer fixture to both specs.
- **Decision**: FIXED

### F3 — Between Phase 2 and Phase 3 a trainer cannot create a plan

- **Severity**: OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Migration Notes
- **Detail**: Phase 2 removes `/trainer/plans/new`, and the bridged list leads only to existing plans. Every commit on `main` deploys.
- **Fix**: Push the Phase 2 and Phase 3 commits to `main` together.
- **Decision**: FIXED

### F4 — The trainer's list includes the trainer and every admin

- **Severity**: OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 1 §4
- **Detail**: `Assignable` has no role filter (`TrainingPlanQuery.cs:58-60`), and every account has a Member row. The set is the same as the picker's, but the plan did not state it.
- **Fix**: Record it as accepted in "What We're NOT Doing", and pin it with a test.
- **Decision**: FIXED

### F5 — Paging helper placement inverts a dependency and was only an "e.g."

- **Severity**: OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Architectural Fitness
- **Location**: Phase 1 §2
- **Detail**: `Application/Paging/PageRequest.cs` would return `MemberListFailure`, so the generic Paging folder would depend on Members.
- **Fix**: Use `Application/Members/MemberListRequest.cs` instead.
- **Decision**: FIXED

### F6 — The SPA has no generic paging envelope, and the search precedent is not app-field

- **Severity**: OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 2 §1, Phase 3 §1
- **Detail**: The only envelope type is `MemberPage` (`member-admin.models.ts:78`). The list search is a `<label>` with a visually-hidden span (`members.html:47-56`).
- **Fix**: Add an explicit `TrainerMemberPage`, and have the search box and pager mirror `members.html`. Watch for `-row` class tokens and use `bp.*` mixins.
- **Decision**: FIXED

### F7 — Comments that go stale are not listed

- **Severity**: OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phases 1, 3 and 4
- **Detail**: The comments that go stale:
  - "both callers": `member-list-failure.ts:7-11` and `member-admin.models.ts:354-355`
  - "shared by the picker": `TrainingPlanQuery.cs:56`
  - the `cref` at `ITrainingPlanQuery.cs:40`
  - `training-plan.service.ts:14,31`
  - the test DTO comments at `TrainingPlanEndpointTests.cs:59,69`
  - `AssignableMember`'s doc
  - `more.ts:17-20`
- **Fix**: Add one line for each to the change entries that already touch these files.
- **Decision**: FIXED
