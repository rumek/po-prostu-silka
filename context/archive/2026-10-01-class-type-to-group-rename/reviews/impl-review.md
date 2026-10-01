<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Class Type → Group Rename

- **Plan**: context/changes/class-type-to-group-rename/plan.md
- **Scope**: All 3 phases (full plan)
- **Date**: 2026-10-01
- **Verdict**: APPROVED
- **Findings**: 0 critical, 1 warning, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | WARNING |

## Evidence

- `main` (288510e) has the same tree as the tested branch tip 1136f41.
- `dotnet build` passes with 0 warnings.
- The migration contains no `DropTable` or `CreateTable`.
- The leftover greps (backend, SPA, living docs) all return 0 hits.
- `npm run quality:check` passes.
- Tests on the same tree: CI checks passed (run 36841027695). `dotnet test` gave 762/762, `npm test` gave 922/922, and E2E gave 11/11 in the pre-push hook.
- Staging deploy run 36841599576 passed "Apply migrations to Azure SQL" and the deploy step. After it, `/health` answered `Healthy`.
- The migration was run Up → Down → Up on the local data. The 97 groups and 63 classes survived every step, and the object names were correct in both directions.

## Findings

### F1 — Test helpers and locals still say "type"

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: tests/po-prostu-silka.Tests/BookingEndpointTests.cs:153 (and 7 other test files); src/Infrastructure/TestData/TestDataGenerator.cs:378,391
- **Detail**: Plan Phase 1 §6 says "rename their helpers and assertions". But `CreateTypeAsync`, `TypesEndpoint`, `typeResponse`, `otherType`, `noType`, `typeId` and the `type` locals are still in place. That is about 100 hits across 8 test files, plus `activeTypes` and `type` in the seeder's generator. The SPA specs keep the `TYPE` fixture and `types:` options the same way. The plan's grep checks only `ClassType|class-type|class_type`, so it could not see these. Behaviour is unaffected.
- **Fix**: Mechanically rename them to `CreateGroupAsync`, `GroupsEndpoint`, `groupResponse`, `group` and so on, on a short follow-up branch. Then rerun `dotnet test` and `npm test`.
- **Decision**: FIXED — renamed CreateTypeAsync, TypesEndpoint, typeResponse, otherType, noType, typeId, the `type` locals, the `Type` tuple element and five test method names in 8 test files; activeTypes/type in TestDataGenerator; the shorthand comments in 5 E2E files and schedule.spec.ts. The pass-type names (TypeName, invalid_type_name) were left alone on purpose. The SPA spec fixtures had already been renamed in phase 2.

### F2 — README.md still says "class types"

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: README.md:14
- **Detail**: "defines class types and schedules…" is living, user-facing copy. The plan's docs list in Phase 3 §1 did not include README.md, so it was missed. The line at README.md:133 describes prd-v2 as it shipped and is correct as it stands.
- **Fix**: Change line 14 to "defines groups and schedules…".
- **Decision**: FIXED — README.md:14 now reads "defines groups and schedules".

### F3 — Two deviations are not recorded in the plan

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: context/changes/class-type-to-group-rename/plan.md (Phase 3 §1; Phase 2 §2)
- **Detail**: Under the lesson "Record necessary adaptations in the plan", two adaptations live only in a doc note and a commit message:
  - The manual test IDs `TYPE-NN` were kept deliberately. A note in `04-schedule-bookings-attendance.md` says so.
  - PR #13 (the class-type form restyle) was merged in, and the rename was re-applied over its form. Commit 1136f41 records this.
- **Fix**: Add two "**Adapted during implementation.**" notes to the plan: one at Phase 3 §1 (IDs kept) and one at Phase 2 §2 (form restyle merged).
- **Decision**: FIXED — added "Adapted during implementation." notes at Phase 2 §2 (#13 merge) and Phase 3 §1 (TYPE-NN ids kept, README).

### F4 — 3.5 is verified but still unchecked

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: context/changes/class-type-to-group-rename/plan.md:405
- **Detail**: Deploy run 36841599576 succeeded and `/health` answers `Healthy`, but Progress 3.5 is still `[ ]`. That leaves `change.md` short of `implemented`. Protected `main` prevented a direct push.
- **Fix**: Flip 3.5 citing run 36841599576 and set status `implemented`. Land both with `/10x-archive` (or with the F1 follow-up PR).
- **Decision**: FIXED — Progress 3.5 flipped with the merge SHA 288510e (deploy run 36841599576, /health Healthy).
