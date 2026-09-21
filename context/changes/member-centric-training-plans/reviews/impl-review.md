<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: A Member's Plan Is Reached Through the Member (S-22)

- **Plan**: context/changes/member-centric-training-plans/plan.md
- **Scope**: Full plan (Phases 1–4 of 4)
- **Date**: 2026-09-21
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 3 warnings, 5 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

### Automated checks run for this review

- `dotnet build po-prostu-silka.slnx` passed with 0 warnings and 0 errors.
- `dotnet test po-prostu-silka.slnx` passed: 612 passed, 0 failed.
- `npm test` passed: 756 tests in 63 files.
  - The machine's default Node (v18.16.0) is too old for the Angular CLI, so this and the next two checks ran on Node v24.15.0.
- `npm run quality:check`: Prettier and ESLint are clean.
- `npx ng build`: the initial total is 520.33 kB, which matches the figure recorded in the plan.
- The `trainer/plans` grep returns only lines the plan's adaptation note already lists: the import path, comments, and paired absence assertions.
- The `GetAssignableMembers|GetTrainingPlans` grep in `src` and `tests` has no source hits. Its only matches are stale Release binaries under `bin/` and `obj/`.
- Every manual item (1.6, 2.4–2.7, 3.5–3.8, 4.6) is unchecked, so it is still pending. None is marked done without evidence.

## Triage (2026-09-21)

All eight findings fixed. After the fixes:

- `npm test` passes 760 of 760.
- `quality:check` is clean.
- `dotnet build` shows 0 warnings.
- The full `dotnet test` suite passes.

## Findings

### F1 — Builder reads the member id once and would go stale if the route is reused

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/app/src/app/features/trainer/plans/plan-builder.ts:157
- **Detail**:
  - `memberId` is read once from `route.snapshot.paramMap`, and `ngOnInit` loads once.
  - Angular reuses the component instance when only `:id` changes, for example going from `/admin/members/A/plan` to `/admin/members/B/plan`. The screen would then show A's plan under B's URL, and a save would send A's id.
  - A PUT would carry A's plan id and A's member id, so `member_changed` would not catch it: the URL and the body disagree, but the body and the stored plan agree.
  - No link can trigger this today, because the member list always sits between two plans. It becomes reachable as soon as a "next member" link or a toast link lands.
- **Fix A ⭐ Recommended**: Derive `memberId` from `route.paramMap` and reload on each emission, resetting `editingId`, the items and the form. This is the same pattern `trainer-members.ts` uses for query params.
  - Strength: removes the hazard, and makes the URL the member's source of truth for real.
  - Tradeoff: touches the load path of a freshly reviewed screen, and the reset needs a spec.
  - Confidence: HIGH — standard Angular route-reuse behaviour.
  - Blind spot: whether the builder has other snapshot-only state, such as the `membersLink` route data, which is identical per mount.
- **Fix B**: Keep the snapshot. Add a docblock line and a spec that pin "one mount per member; navigation between plans must pass through the list".
  - Strength: zero behaviour change.
  - Tradeoff: leaves a trap for the next link someone adds.
  - Confidence: MED — relies on future contributors reading the comment.
  - Blind spot: None significant.
- **Decision**: FIXED via Fix A — `memberId` is a signal fed by `route.paramMap`; `load()` resets `editingId`/form/error when the member has no plan; a create whose member is no longer the URL's does not set `editingId`. New spec: 'follows the URL to another member instead of keeping the previous plan'.

### F2 — `missing_field` still tells the user to pick a member

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: src/app/src/app/core/training/training-plan-failure.ts:17
- **Detail**:
  - The line reads `'Uzupełnij nazwę planu i wybierz członka.'`, but the picker is gone and the member comes from the URL.
  - Phase 2 reworded `member_not_found` and `member_not_active` for exactly this reason, and missed this line.
  - The builder always sends the route's member id, so a real user now sees this message only for a blank name.
- **Fix**: Reword it to the name alone, for example `'Uzupełnij nazwę planu.'`. Also check any spec that pins the old sentence.
- **Decision**: FIXED — `missing_field` now reads 'Uzupełnij nazwę planu.' (no spec pinned the old sentence).

### F3 — The trainer list's spec skips the URL-state cases its template spec covers

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/app/src/app/features/trainer/members/trainer-members.spec.ts
- **Detail**:
  - The plan deliberately accepts `trainer-members.ts` as a second copy of `members.ts`'s URL-state logic, with no shared helper. That makes the spec the only thing keeping the copy honest.
  - The spec leaves out four cases that `members.spec.ts` pins:
    - a stale response resolving after a newer one (`members.spec.ts:620`);
    - junk URL normalization (`:468`);
    - paging pushing a history entry, `replaceUrl: false` (`:394`). Only the search's `replaceUrl: true` is asserted;
    - the search box syncing on Back and Forward (`:437`).
- **Fix**: Port those four cases from `members.spec.ts` into `trainer-members.spec.ts`.
- **Decision**: FIXED — four cases ported to `trainer-members.spec.ts` (replace-vs-push history, Back/Forward box sync, junk URL normalisation, stale response discarded); 15/15 green.

### F4 — The inactive-admin header test now passes without testing anything

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: src/app/src/app/app.spec.ts:147-162
- **Detail**:
  - The plan's goal was that no absence check "stays green while testing nothing". This test was retargeted to `/trainer/members` for a fixture with `isAdmin: true, isTrainer: false, isActive: false`.
  - That link needs `isTrainer && !isAdmin`, so it is absent whatever `isActive` is.
  - The test name and its "must match adminGuard" comment describe a check that no longer exists.
- **Fix**: Either assert something `isActive` actually gates for an admin in the header, or turn the fixture into an inactive admin+trainer and assert both header links are absent. If nothing admin-specific is left in the header, delete the test.
- **Decision**: FIXED — test deleted. The header carries no admin-only link since Plany was retired, and the inactive-trainer case it could have become is already pinned (`app.spec.ts:256`).

### F5 — The claim that a trainer learns only a name is untrue and untested

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Application/Training/GetMemberPlan.cs:12-16 (and plan.md:181)
- **Detail**:
  - The doc and the plan both say that a trainer who reaches a blocked member by a typed id "learns only a name".
  - The response actually carries the full `TrainingPlanDetail`, and `PUT /api/trainer/plans/{id}` has no member-status check, so the trainer can also edit that plan.
  - This is NOT a new exposure: the retired `GET /api/trainer/plans` listed every Active plan regardless of the member's status (`GetActiveAsync` at `d1fa6e8`). Trainers could already see and edit it.
  - The gaps are that the comment misstates what is exposed, and that only the admin case is tested (`TrainerMemberEndpointTests.cs:356`).
- **Fix**: Correct the doc to "a trainer reaching a blocked member learns their name and active plan, as the retired plan list already showed". Add a test that the trainer gets a 200 for a blocked member, so the stance is pinned.
- **Decision**: FIXED — `GetMemberPlan` doc corrected, plan.md:181 carries an "Adapted during implementation" note, and `A_blocked_members_plan_is_readable_by_a_trainer_who_knows_the_id` pins the stance (TrainerMemberEndpointTests 26/26 green).

### F6 — The column-side `ł` fold is still written out twice

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/Infrastructure/Members/MemberQuery.cs:246-253 vs src/Infrastructure/Members/MemberSearch.cs:48-50
- **Detail**:
  - Only the collation constant and the term-side `Fold` are shared.
  - The column expression `EF.Functions.Collate(...).Replace("ł","l").Replace("Ł","L")` still appears in both files.
  - The `MemberSearch` docblock promises the two searches "can never disagree". Today that holds only while the two copies stay identical.
- **Fix**: Expose the name-match predicate once from `MemberSearch`, as an `Expression<Func<Member,string,bool>>` or a helper that ORs in an e-mail clause, and build `MemberQuery.Searched` from it. Or soften the docblock claim.
- **Decision**: FIXED — `MemberSearch.Matches` is the one column-side expression, inlined by an `ExpressionVisitor` into `ByName` and the new `ByNameOrEmail`; `MemberQuery.Searched` delegates to the latter. No new package; build warning-free; MemberAdmin + TrainerMember tests 94/94 green.

### F7 — `AssignableMember`'s parameter docs still describe the picker

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: src/Application/Training/AssignableMember.cs:23-28
- **Detail**:
  - The plan asked for this file's doc comment to be updated. The summary was updated, but the parameter docs were not.
  - The `Id` doc still says "be offered here", and the `HasAccount` doc still says "The picker says so". The picker was retired in Phase 4.
- **Fix**: Reword both parameter docs in terms of the trainer's list and the plan screen.
- **Decision**: FIXED — `Id` and `HasAccount` param docs now speak of the plan screen and its URL, not the picker.

### F8 — The shared member-refusal sentence can name an account that does not exist

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/app/src/app/core/training/training-plan-failure.ts:41-44
- **Detail**:
  - Both `member_not_found` and `member_not_active` say "jej konto jest zablokowane lub nieaktywne".
  - The sentence does not fit two real cases. A blocked member with no account (possible since S-14) has no account to be blocked. And `member_not_found` means the member record is gone.
  - The rewording itself is recorded as "Adapted during implementation", so this is a copy nit, not an unrecorded drift.
- **Fix**: Speak of the person rather than the account, for example `'Tej osobie nie można teraz przypisać planu. Wróć do listy członków.'`.
- **Decision**: FIXED — both now read 'Tej osobie nie można teraz przypisać planu. Wróć do listy członków.'; the builder spec's pinned prefix updated.
