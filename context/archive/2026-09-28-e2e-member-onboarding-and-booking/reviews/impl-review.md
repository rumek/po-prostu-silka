<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: E2E: Invitation Claim and Staff Booking

- **Plan**: context/changes/e2e-member-onboarding-and-booking/plan.md
- **Scope**: All 3 phases (full plan), commits a3defb7..9e1ba8e, merged as 180ba22 (PR #7)
- **Date**: 2026-09-28
- **Verdict**: APPROVED
- **Findings**: 0 critical, 2 warnings, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

Success criteria evidence: `npx playwright test --list` shows 9 tests in 9 files. `npm run quality:check` is clean. On the final code (9e1ba8e) the pre-push hook ran E2E 9/9, `dotnet test` passed 762/762 and `npm test` passed 921/921. The suite was also green twice back to back after Phase 3. Every new spec, and the new integration test, was shown red with its behaviour broken. Manual 2.4, 2.5, 3.4 and 3.5 are backed by those break runs. Manual 1.5, 1.6 and 3.6 are still pending, and they are the user's to check.

## Findings

### F1 — The pre-push hook blocks every push on this machine unless E2E_BASE_URL is set

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: .githooks/pre-push:12, src/app/playwright.config.ts:14
- **Detail**: Without `E2E_BASE_URL`, `baseURL` is `http://localhost:5264`. Here that port falls in a Windows-reserved TCP range (5241–5340), so when no API is running, `webServer` cannot bind it. The suite then fails and the push is refused. The PR #7 push only passed because the variable was set inline for that one command. Nothing in the repo tells a contributor to set it: AGENTS.md's build section does not mention the hook, `core.hooksPath` or the variable.
- **Fix**: Set it once per machine (`setx E2E_BASE_URL http://localhost:5480`). Add one line to AGENTS.md's "Build, test, and dev commands" naming the hook, `git config core.hooksPath .githooks`, and `E2E_BASE_URL` for reserved-port machines.
- **Decision**: FIXED — `setx E2E_BASE_URL http://localhost:5480` applied; AGENTS.md bullet added.

### F2 — Desired End State and manual check 1.6 still describe the pre-decision leftovers

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: context/changes/e2e-member-onboarding-and-booking/plan.md (Desired End State; Phase 1 Manual Verification; Progress 1.6)
- **Detail**: The Desired End State says the only rows left behind are `E2E` members, accounts and the trainer. Check 1.6 reads "No leftover E2E classes". The Phase 2 decision (cancel a booked class, keep its karnet) makes both untrue: cancelled `E2E` classes and karnets on `E2E` members now stay by design. The addendum records this, but the criterion text is what a reader checks against, and as written 1.6 cannot pass.
- **Fix**: Amend the Desired End State and the Phase 1 manual bullet to name cancelled classes and karnets as expected leftovers. Leave the Progress row title unchanged (the progress-format rule), with its meaning carried by the amended bullet.
- **Decision**: FIXED — Desired End State and the Phase 1 manual bullet amended; 1.6's title unchanged.

### F3 — Production code changed in a plan that says "No production code"

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: src/Application/Members/RevokePass.cs:54, src/Application/Scheduling/IBookingStore.cs, src/app/src/app/core/admin/membership-pass-failure.ts:31, playwright.config.ts:39
- **Detail**: Two changes go past the plan's "What We're NOT Doing". The `RevokePass` fix (the new `has_booking_history` reason, which is a new API failure code) and the move of S-30 from CI to a local gate. Both were requested by the user, recorded in the plan's Phase 2 addendum and in `test-plan.md` §6.7, and tested: the integration test fails with 500 without the fix, and the SPA spec keeps the two messages apart. Benign, and recorded here so the archive shows the slice shipped a behaviour change as well as tests.
- **Fix**: None needed; already documented. Optionally add one line to `change.md` Notes.
- **Decision**: FIXED — later decisions recorded in `change.md` Notes.

### F4 — trainer.setup.ts saves a session file nothing reads

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/app/e2e/trainer.setup.ts:58, src/app/e2e/credentials.ts (`trainerAuthFile`)
- **Detail**: The plan's contract was "save `trainerAuthFile`", and it is saved. But both trainer specs sign in again through `signedInContext(browser, trainerCredentials)`, and no spec reads the file. That leaves a dead contract. The next author (S-32) may reasonably use either path, and the two can drift, for example after a password change: the file goes stale while the API sign-in keeps working.
- **Fix**: Drop the save and `trainerAuthFile`. `signedInContext` is the one persona path `e2e/CLAUDE.md` documents.
- **Decision**: FIXED — `trainerAuthFile` removed; trainer.setup.ts still signs in to check the password, but saves nothing.
