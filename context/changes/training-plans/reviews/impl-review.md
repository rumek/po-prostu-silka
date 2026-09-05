<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Training plans authored by trainers for their members

- **Plan**: `context/changes/training-plans/plan.md`
- **Scope**: Phases 1-3 of 3 (all implemented; several manual criteria still pending)
- **Date**: 2026-09-05
- **Commits**: `1de6369` (p1), `f91c91e` (p2), `26f781b` (p3)
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 5 warnings, 4 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | WARNING |

**Architecture PASS** — no `using Microsoft.EntityFrameworkCore` in `Domain` or `Application`; the
`ITrainingPlanQuery` / `ITrainingPlanStore` seams are declared in Application and implemented in
Infrastructure, matching `IBookingQuery`/`IBookingStore`; EF config lives in two auto-discovered
`IEntityTypeConfiguration<T>` classes and `OnModelCreating` was not touched; the migration's `Down`
drops items before plans in correct FK order.

**Scope Discipline PASS** — every "What We're NOT Doing" boundary holds: the plan is flat, there is
no archived-plan screen, no trainer-to-member relationship, no notifications, no DELETE (pinned by a
test), no member-facing library browsing, no keyboard reorder, no Home change, no progress tracking.

## Findings

### F1 — An omitted `items` array is a 500, not a 400

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `src/Application/Training/TrainingPlanEndpoints.cs:475`
- **Detail**: `ValidateShape` dereferenced `request.Items` without a null check.
  `TrainingPlanRequest.Items` is a non-nullable `IReadOnlyList<>` on a positional record, but
  System.Text.Json does not honour nullable reference annotations, and nothing in `src/Program.cs`
  configures `RespectNullableAnnotations`, `ConfigureHttpJsonOptions` or `UseExceptionHandler`. A body
  of `{"name":"Masa","memberUserId":"<id>"}` bound `Items` to null, passed the first two checks, and
  threw a `NullReferenceException` at `request.Items.Count` — escaping as a 500 for what is ordinary
  bad input. This is the same class as the "unguarded bound reaches SQL Server as a 500" hazard the
  file's own doc comment calls the most repeated finding in this repo's history.
  `TrainingPlanRequest` is the first request body in the codebase carrying a collection, so no
  existing sibling covered it.
- **Fix**: Guard with `request.Items is null || request.Items.Count == 0` → `no_items` 400, and add
  a test posting a raw body with `items` omitted.
  - Strength: One line, on the path every write already takes; the reason is one the SPA union
    already carries, so no contract changes.
  - Tradeoff: None.
  - Confidence: HIGH — reproduced by reading the binder path; no JSON options are configured.
  - Blind spot: The test is written but has not been executed — see F9.
- **Decision**: FIXED

### F2 — SPA model comment contradicts the endpoint it is a contract for

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/app/src/app/core/training/training-plan.models.ts:80`
- **Detail**: The comment said "`memberUserId` is ignored by the edit endpoint". It is not ignored:
  `UpdateAsync` (`TrainingPlanEndpoints.cs:403-406`) compares it and returns 409 `member_changed`,
  `plan-builder.ts` depends on that (it uses `getRawValue()` rather than `value()` precisely so the
  disabled control's id is still sent), and `TrainingPlanEndpointTests.cs:374` pins the 409. A future
  edit trusting the comment and dropping `memberUserId` from the payload would 409 on every save.
- **Fix**: Rewrite the comment to say the field is validated and a mismatch is refused with
  `member_changed`.
- **Decision**: FIXED

### F3 — The plan's failure-reason list never matched what shipped

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `context/changes/training-plans/plan.md:396-399`
- **Detail**: The plan lists a single `too_long`; the implementation splits it into
  `name_too_long` / `reps_too_long` / `note_too_long`, because one reason gives the builder nothing to
  attach to a control — the whole point of the union being closed. `invalid_reps` does not exist
  (reps is free text with only a length bound). `not_found` is not a body reason: the three 404s
  return a bare `Results.NotFound()`, matching every other 404 in the codebase. Code, tests and the
  SPA union are all consistent with the shipped set; only the plan text was stale — and
  `context/foundation/lessons.md` has a standing rule that exactly this must be recorded in the plan.
- **Fix**: Add an "**Adapted during implementation.**" note under the contract, per `lessons.md`.
- **Decision**: FIXED

### F4 — The edit path's member picker renders blank for a blocked member

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Pattern Consistency
- **Location**: `src/app/src/app/features/trainer/plans/plan-builder.html:41-45`
- **Detail**: On edit, the member `<select>` was populated from `members()`, which carries only
  ACTIVE accounts. Blocking a member deliberately leaves their plan standing — a state the backend
  supports and `MyPlanEndpointTests.cs:333` pins — so for such a plan no `<option>` matched the
  control's value and the select rendered blank. The submit still worked (`getRawValue()` returns the
  id), but the trainer could not see whose plan they were editing, which is exactly what the
  "Disabled, not hidden" comment said the control existed to show.
- **Fix**: Render `TrainingPlanDetail.memberDisplayName` as static text on the edit path instead of a
  disabled picker; the value is already loaded, so no extra request.
  - Strength: Correct for every member state, and it drops a form control that could never be
    edited anyway.
  - Tradeoff: Two shapes for one field (caption + text on edit, label + select on create); the
    template now branches on `editingId()` in one more place.
  - Confidence: HIGH — `memberDisplayName` is on the detail DTO the builder already fetches.
  - Blind spot: Not exercised against a genuinely blocked member on the environment — F9.
- **Decision**: FIXED

### F5 — Backend suite not re-run: Docker unavailable

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Success Criteria
- **Location**: `context/changes/training-plans/plan.md` Progress 2.2, 2.3, 2.4
- **Detail**: The integration suite runs against SQL Server started by Testcontainers.
  Docker Desktop is not running on this machine and starting it returned
  `Operacja została anulowana przez użytkownika` — an elevation prompt that cannot be answered from
  this session. The suite was green at 366 tests before the manual 2.5 index experiment, and
  everything since is doc comments plus the F1 guard and its new test; `dotnet build` on both `src/`
  and `tests/` is warning-free. But 2.2, 2.3 and 2.4 have NOT been observed passing and their boxes
  stay unchecked.
- **Fix**: Start Docker Desktop and run `dotnet test` from the repo root, then
  `dotnet test --filter Concurrent` three times for 2.3.
  - Strength: Restores the only success criteria in this change that rest on unobserved evidence.
  - Tradeoff: Needs a human at the machine.
  - Confidence: HIGH — the blocker is identified precisely.
  - Blind spot: The new F1 test has never executed; if `ArrangeAsync`'s tuple shape or `Endpoint`
    differ from what was assumed, it fails to compile — though `dotnet build` on the test project
    passes, which covers that.
- **Decision**: PENDING — belongs to the human's environment pass

### F6 — A malformed exercise id reads as a load failure, not "not in your plan"

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `src/app/src/app/features/my-plan/plan-exercise-detail.ts:82`
- **Detail**: The Angular route `my-plan/exercises/:id` accepts any string while the API route is
  `{exerciseId:guid}`-constrained, so a non-guid matches no endpoint and falls through to
  `MapFallbackToFile("index.html")` — returning 200 with HTML. `HttpClient` fails to parse it, the
  status is not 404, and the member sees a retry button that can never succeed. Pre-existing pattern:
  `admin/exercises/:id` has the same shape, so this is not introduced by this change.
- **Fix**: Treat a parse failure on a syntactically invalid id as `notFound`, or constrain the
  Angular route with a guid matcher — as a follow-up across both screens, not just this one.
- **Decision**: ACCEPTED — pre-existing, and fixing it on one screen only would make the two diverge

### F7 — Assignable members are not filtered to member-facing roles

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `src/Infrastructure/Training/TrainingPlanQuery.cs:33-40`
- **Detail**: `GetAssignableMembersAsync` returns every `AccountStatus.Active` account with no filter
  on holding a member-facing role, and `CreateAsync` never checks it either. A plan assigned to a
  hypothetical Trainer-only account could never be read by its recipient, since `/api/plans` requires
  `ApplicationRoles.MemberFacing`. Harmless today — nothing creates a Trainer-only account, which
  `IntegrationTestFixture.cs:92-101` documents — but it is an unenforced assumption.
- **Fix**: Filter the picker (and optionally `ValidateMemberAsync`) to accounts holding a
  member-facing role.
- **Decision**: ACCEPTED — unreachable in the current role model; revisit if Trainer-only accounts
  ever become creatable

### F8 — `WeightKg` scale is not checked, so a value is silently rounded

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `src/Application/Training/TrainingPlanEndpoints.cs` (`MaxWeightKg`) /
  `src/Infrastructure/Persistence/Configurations/TrainingPlanItemConfiguration.cs:28`
- **Detail**: Weight is range-checked (0 … 999.99) but not scale-checked. `decimal(5,2)` rounds a
  submitted `60.123` to `60.12`; the client's `step="0.5"` is a hint, not a constraint. Not a 500
  risk (rounding, not truncation), but the stored value differs from what was entered with no
  refusal.
- **Fix**: Either reject more than two decimal places, or document the rounding beside
  `MaxWeightKg`.
- **Decision**: SKIPPED — half-kilogram granularity is what a gym floor uses; the rounding is
  invisible in practice

### F9 — `TrainingPlanItem.Id` is not stable across an edit

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Architecture
- **Location**: `src/Infrastructure/Training/TrainingPlanStore.cs:49-59`
- **Detail**: `ReplaceItems` deletes every item row and re-inserts with fresh `Guid`s on each save,
  so an item's id changes whenever the trainer edits the plan. The ids are already on the wire
  (`TrainingPlanItemView.Id`, used as the `track` key in `my-plan.html`). Correct today — nothing
  references an item id across requests — but any future feature keyed on one (workout logging,
  per-exercise progress) would silently lose its rows on the next plan edit. This is the direct cost
  of the replace-wholesale write model that makes reordering trivially correct, so it is a tradeoff
  the slice took deliberately, not an accident.
- **Fix**: Record the non-stability in `TrainingPlanItem.Id`'s doc comment so the constraint is
  visible before something depends on it.
- **Decision**: PENDING — worth a doc comment; raise before the first feature that keys on an item

## Areas verified clean

- **Authorization** — both groups apply their policy at the `MapGroup`, never per-route. `/api/plans`
  takes no member id on any route, so cross-member reads are structurally impossible rather than
  check-dependent; the member's exercise read is authorized by a join against their own active plan.
  Widening library reads to `TrainerOrAdmin` was done as a second `MapGroup` over the same path, so
  the one-policy-per-group convention and the `EveryRoute` matrices both survive.
- **The one `bypassSecurityTrustResourceUrl` call** — the `isVideoId` re-check against
  `^[A-Za-z0-9_-]{11}$` sits immediately before the trust call, inside a `computed` rather than a
  getter, and the regression spec proving a `javascript:` id renders no iframe was carried over with
  the component.
- **No data leak** — `AssignableMember` is `(Id, DisplayName)` only, served by its own endpoint
  rather than by loosening the Admin-only member list.
- **Performance** — no N+1 and no load-to-count: a correlated `Items.Count` in the list, one
  `Contains` statement for the whole payload in `FindExerciseStatesAsync`, and `AsNoTracking` on every
  read path (correctly absent from the store, which returns tracked entities by contract).
- **Concurrency** — the retry loop is bounded at 10, makes progress on every iteration, and calls
  `DiscardChanges()` on every non-`Saved` outcome; exhaustion returns a clean 409. `UpdateAsync`
  correctly has no loop. The invariant is enforced at the database by `IX_TrainingPlans_Member_Active`
  — measured, not assumed (see the Phase 2 adaptation note in the plan).
- **Data safety** — nothing deletes a plan; assignment archives. Both `AspNetUsers` FKs are
  `Restrict` (required — two cascading FKs to one table is the multiple-cascade-path error SQL Server
  refuses), and the `Exercises` FK is `Restrict`, which is what makes the library's
  deactivate-instead-of-delete decision load-bearing.
