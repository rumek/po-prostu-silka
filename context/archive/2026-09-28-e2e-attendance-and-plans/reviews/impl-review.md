<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: E2E: attendance and a trainer's plan

- **Plan**: context/changes/e2e-attendance-and-plans/plan.md
- **Scope**: Phases 1–3 of 3 (full plan)
- **Date**: 2026-09-29
- **Verdict**: APPROVED
- **Findings**: 0 critical, 1 warning, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

Notes:
- **Plan Adherence.** Every planned change is present. The three deviations (no toast assertion,
  `uniqueSuffix()`, a tall viewport) are recorded in the plan as "Adapted during implementation"
  under Phase 3, as `lessons.md` requires.
- **Scope Discipline.** The attendance spec was also touched in the p3 commit, to switch to
  `uniqueSuffix()`. It is documented. No production code changed.
- **Success Criteria.** Last verified runs, on code identical to HEAD:
  - full suite 11/11;
  - both new specs, `--repeat-each=5`: 12/12 twice;
  - `quality:check` clean;
  - both mutation checks red at the intended line (63 and 80).

  The re-run during this review was blocked: Docker Desktop answered 500, and the local SQL Server
  was unreachable.

## Findings

### F1 — e2e/CLAUDE.md still prescribes `Date.now()` names

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/app/e2e/CLAUDE.md (the "Members and accounts cannot be deleted" bullet)
- **Detail**: The rules file tells authors to name members `E2E <purpose> <Date.now()>`. This change
  showed that suffix colliding under `--repeat-each=5`: two workers in the same millisecond created
  two members with one name, and the link locator hit a strict-mode violation. Both new specs use
  `uniqueSuffix()`, but the next author following the rules file would reintroduce the collision.
- **Fix**: Say `E2E <purpose> <uniqueSuffix()>` (from `support/club.ts`) in that bullet, with the
  reason in one clause.
- **Decision**: FIXED

### F2 — S-31 specs keep the latent `Date.now()` collision

- **Severity**: 🔍 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/app/e2e/staff-booking-reaches-member.spec.ts:24, booking-without-karnet-is-refused.spec.ts, invitation-claim-carries-karnet-and-booking.spec.ts
- **Detail**: The same `const suffix = Date.now()` sits in the three S-31 specs. They run once per
  suite, so a collision needs two of them in the same millisecond, but their names differ by purpose
  (`E2E zapis`, …). The risk shows only under `--repeat-each`. The plan records it as out of scope.
- **Fix**: Switch the three specs to `uniqueSuffix()` (a one-line change each), or leave as recorded.
- **Decision**: FIXED

### F3 — Started classes accumulate in the rolling past week

- **Severity**: 🔍 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/app/e2e/support/club.ts (`startClass`, `removeClass`)
- **Detail**: Each attendance run leaves a Scheduled class in a random past slot for up to 6 days.
  - The window holds 336 start slots.
  - A 30-minute class blocks about 3 of them.
  - `startClass` tries 20 slots.
  - At ~100 runs in 6 days, one attempt collides about 90% of the time, so all 20 fail about 12% of
    the time. This session alone left about 20.
  - Nothing breaks at normal pre-push volume. Heavy `--repeat-each` sessions push towards the limit,
    which then surfaces as "No free past slot".
- **Fix**: Add one sentence to `e2e/CLAUDE.md`: the past-slot pool is shared by every run in the
  last 6 days, so avoid large `--repeat-each` runs of the attendance spec.
- **Decision**: FIXED

### F4 — Two loose locators

- **Severity**: 🔍 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/app/e2e/absence-returns-karnet-entry.spec.ts (`/Nieobecni: 1/`), src/app/e2e/trainer-plan-reaches-member.spec.ts (`heading 'Opis'`, `'Wykonanie'`)
- **Detail**: `/Nieobecni: 1/` also matches `Nieobecni: 10`. The section headings are matched as
  case-insensitive substrings. Neither can misfire with today's data (one booking; no other heading
  contains those words), but both are looser than the intent.
- **Fix**: `/Nieobecni: 1 ·/` and `exact: true` on the two headings.
- **Decision**: FIXED
