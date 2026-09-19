<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Compiler-enforced layer boundaries and thin endpoint classes

- **Plan**: `context/changes/backend-layer-boundaries/plan.md`
- **Mode**: Deep
- **Date**: 2026-09-18
- **Verdict**: REVISE → **SOUND** after triage (all 6 findings fixed)
- **Findings**: 1 critical, 3 warnings, 2 observations

## Verdicts

| Dimension | Verdict | After fixes |
|-----------|---------|-------------|
| End-State Alignment | FAIL | PASS |
| Lean Execution | WARNING | PASS |
| Architectural Fitness | PASS | PASS |
| Blind Spots | WARNING | PASS |
| Plan Completeness | WARNING | PASS |

## Grounding

8/8 paths ✓, 6/6 symbols ✓, brief↔plan ✓, Progress↔Phase 7/7 phases and 45/45 criteria mapped ✓,
no checkboxes outside the `## Progress` section ✓.

## Findings

### F1 — Phase 7 does not compile: 49 DTOs ride the endpoint files into Api, and 13 of them create a reference cycle

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: End-State Alignment
- **Location**: Phase 2 §2 vs. Phase 7 §1
- **Detail**: The decision table said "DTOs stay in Application" but no phase made it true. Phase 2
  moved only two cross-context DTOs; the other 47 stayed inside the `*Endpoints.cs` files that phase 7
  `git mv`s into `Api`. Verified: 49 `record` declarations across the thirteen files (Auth 10,
  MemberAdmin 9, TrainingPlan 7, Class 5, Booking 4, …). Two breaks follow. (1) Application → Api:
  `LoginFailure` is declared at `AuthEndpoints.cs:47` and constructed by the login handler at `:198`,
  which phases 3–6 move into `Application`. (2) Infrastructure → Api, an actual **cycle** — `Api`
  already references `Infrastructure`, so no added reference fixes it. Thirteen records are consumed
  from `Infrastructure` because the query implementations project into them: `ClassScheduleQuery.cs:15,82`
  returns and constructs `ScheduledClass`, declared at `ClassEndpoints.cs:52`. Also `MemberSummary`,
  `MemberDetail`, `MembershipPassView`, `TrainerSummary`, `MyBooking`, `ClassBooking`,
  `ClassTypeSummary`, `ExerciseSummary`, `TrainingPlanSummary`, `TrainingPlanItemView`,
  `TrainingPlanDetail`, `AssignableMember`. Research §D enumerated the ports as the cross-context web
  and stopped there; the DTOs those ports return are the same problem and were missed.
- **Fix ⭐ Recommended**: Widen phase 2 from "cross-context DTOs" to all 49 records, each to its own file
  under `Application`; add a phase-2 success criterion that the endpoint files declare zero records and
  zero interfaces; state the precondition explicitly in phase 7.
  - Strength: Uses the phase already designed for this class of coupling; keeps phase 7 a pure move.
  - Tradeoff: Phase 2 grows from ~11 files to ~50, though it remains declarations-only.
  - Confidence: HIGH — the cycle is demonstrated from code, not inferred.
  - Blind spot: Whether some records are genuinely file-private and could stay with their handler; the
    plan now notes they must leave the `*Endpoints.cs` file regardless.
- **Decision**: FIXED — applied to plan (phase 2 overview + §2, phase 2 criteria, decision table,
  phase 7 §1 precondition) and to plan-brief (decision row, phases table, scope, architecture).

### F2 — Testing Strategy names a gate that explicitly cannot see phase 5's sharpest risk

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Testing Strategy; Phase 5 §1 point 2
- **Detail**: The plan called `EndpointAuthorizationTests` "the primary gate for phases 3–7". Its own doc
  comment (`EndpointAuthorizationTests.cs:27-29`) names what it cannot see: "the inline instructor check
  on staff booking" — exactly the `MayActOn` check phase 5 flags as losing its only warning comment.
  `AdminBookingEndpointTests.cs` is the suite that exercises it over HTTP.
- **Fix**: Name `AdminBookingEndpointTests` as the gate for the `MayActOn` move in phase 5, and record
  both stated blind spots in Testing Strategy.
- **Decision**: FIXED

### F3 — Phase 7 invites the DI refactor it then warns against

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Lean Execution
- **Location**: Phase 7 §4 "DI registration"
- **Detail**: The item's Intent was "keep Program.cs readable" and floated `AddInfrastructure()` /
  `AddApplication()` extensions — then spent its Contract warning that this is the phase's sharpest trap
  (`IntegrationTestFixture.cs:416-423` matches `ImplementationType` with `.Single`) and closed with "if
  in doubt, leave Program.cs as it is". Nothing in the Desired End State required the extraction.
- **Fix A ⭐ Recommended**: Cut the extraction; retitle to "DI registration — what must not change",
  keeping only the `AddHostedService` warning and the `Program.cs:399`/`:402` note.
  - Strength: Removes the only optional work offered inside the riskiest phase.
  - Tradeoff: `Program.cs` stays long — as it already is.
  - Confidence: HIGH — grounded in the plan's own scope rules.
  - Blind spot: None significant.
- **Fix B**: Keep the extraction as a follow-up change after S-18 lands.
- **Decision**: FIXED via Fix A — also added to "What We're NOT Doing" so it cannot return.

### F4 — No phase captures the route-literal baseline the gates compare against

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phases 3–7 Automated Verification
- **Detail**: Four phases asserted "route-literal diff empty … matches the pre-phase capture" and phase 7
  "against the phase-1 capture", but phase 1 captured only the migration baseline, no phase said where
  the files live, and two different names described one check.
- **Fix**: Capture the route literals in phase 1's first step alongside the migration baseline
  (`<scratch>/routes-before.txt`, 36 routes) and point every later phase at that one file.
- **Decision**: FIXED — phase criteria and Progress titles 3.4–7.4 aligned.

### F5 — Brief names the wrong mechanism for Identity resolution

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Plan Completeness
- **Location**: Phase 1 §2 (Application csproj); brief Open Risks
- **Detail**: Both documents said `UserManager` reaches `Application` transitively through `Domain`'s
  `Microsoft.Extensions.Identity.Stores`, and told the implementer to verify that. `Application` uses
  `UserManager` ×28 and `SignInManager` ×5; both ship in the ASP.NET Core shared framework and resolve
  from the `FrameworkReference` the same csproj already declares. The conclusion (no `PackageReference`)
  was right; the reason was wrong, so the implementer would verify the wrong thing.
- **Fix**: Attribute both types to the FrameworkReference; state that `Application` carries no
  `PackageReference` at all, and that `Microsoft.AspNetCore.Identity.EntityFrameworkCore` belongs to
  `Infrastructure`.
- **Decision**: FIXED — in plan and brief.

### F6 — "five places" is four

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Plan Completeness
- **Location**: Phase 1 §1 (Directory.Build.props)
- **Detail**: The props file sits at `src/`, so it covers the four new projects;
  `tests/po-prostu-silka.Tests.csproj` keeps its own `TargetFramework`, `Nullable` and `ImplicitUsings`.
- **Fix**: Say four, and note the test project is deliberately outside its scope.
- **Decision**: FIXED

## Note on provenance

This review was run by the same session that wrote the plan. F1 is a defect in that plan's own research
inheritance: `research.md` §D enumerated the cross-context **ports** and the plan treated that list as
the complete set of cross-file declarations, without checking the DTOs those ports return. The cost
would have landed in phase 7, after thirteen files had already moved.
