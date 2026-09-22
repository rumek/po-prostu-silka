<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Test Environment Seed Data

- **Plan**: context/changes/test-environment-seed-data/plan.md
- **Scope**: Phases 1–2 of 2 (full plan; manual checks still open)
- **Date**: 2026-09-22
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 5 warnings, 5 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | WARNING |

The code matches every planned contract and every "Adapted during implementation" note. The wipe
covers every table that references a member or a user, and the booking generator mirrors
`BookingProtocol`. Club-local dates, DST, capacity, pass entries and blocked members are all handled
correctly. The findings are at the edges: two ways into the Staging app, one unguarded first-seed
path, and one claim the code does not keep.

## Findings

### F1 — Seeded claim codes can be computed from the repo

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/Infrastructure/TestData/TestDataGenerator.cs:165-169, :233-249
- **Detail**: About 20 accountless members get a live access code from `new Random(TestDataSeeder.Seed)`, with a constant seed (`TestDataSeeder.cs:29`). Anyone with the source and the seed date can compute every valid code on the public Staging URL. A code stays valid for up to 14 days, and since S-17 it is the only way into `/register`. `MemberAccessCode.Generate`'s own doc comment rules out `Random` for exactly this reason. No test depends on the code values. `MemberAccessCode.Alphabet` was made public only for this reader.
- **Fix**: Set `member.AccessCode = MemberAccessCode.Generate()`. Keep one dummy `_random` draw per code so later ids stay stable, and revert `Alphabet` to `private`. Also update the plan's "Adapted" note.
  - Strength: Restores the S-17 guarantee and removes the only new reader of `Alphabet`.
  - Tradeoff: The codes differ between reseeds. Nobody relies on them: the tests compare ids only, and the admin reads the codes from the UI.
  - Confidence: HIGH — the generation path is a single call site.
  - Blind spot: Whether the repo is public. If it is private, the exposure is limited to people with repo access.
- **Decision**: FIXED. `NewAccessCode` now uses `MemberAccessCode.Generate()` and advances the seeded Random `Length` times outside the retry loop. `MemberAccessCode.cs` is restored to its pre-d3d40a1 version, with `Alphabet` private again. The plan carries an "Adapted after impl review (F1)" note. The build is green.

### F2 — The shared password bypasses Identity's password policy

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Infrastructure/TestData/TestDataSeeder.cs:95-96
- **Detail**: `IPasswordHasher.HashPassword` never runs `UserManager.PasswordValidators`, so `RequiredLength = 8` (`Program.cs:85`) is not enforced. A short `TestDataSeed__Password` would be accepted for 164 accounts on a public URL, including 2 admins whose e-mails are listed in `deploy-plan.md`. `AdminSeeder` goes through `CreateAsync`, which does validate.
- **Fix**: Before hashing, run the registered password validators once. On failure, log the error codes (never the password) and refuse, the same way AdminSeeder refuses.
- **Decision**: FIXED. The policy runs in the gate, before any wipe, and a failure logs only the error codes. New test `Refuses_a_password_the_policy_rejects`, so the file now has 10 cases. The build is green. The test has not run yet, because Docker was not available.

### F3 — `Enabled` without `Reset` seeds on top of existing data

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/Infrastructure/TestData/TestDataSeeder.cs:70-77; context/deployment/deploy-plan.md (first-run command)
- **Detail**: The only guard is the sentinel account, and the runbook's first-run command sets `Enabled=true` without `Reset`. On a database that already holds data, either of two things happens:
  - It collides with an existing active class type, exercise name or member e-mail. The single `SaveChanges` then rolls back, which is safe but confusing.
  - Nothing collides, and the seed silently breaks the club-wide rule that classes never overlap. Seeded classes are placed without checking `HasTimeConflictAsync` (`CreateClass.cs:64`).

  The Azure first seed ran with `Reset=true`, so this is latent.
- **Fix A ⭐ Recommended**: Without `Reset`, the seeder refuses when any domain table holds rows beyond the AdminSeed member, and logs "set Reset to seed a populated database".
  - Strength: Fails closed, which matches the seeder's own stance ("FAILS CLOSED") and the unconditional environment gate.
  - Tradeoff: One more check and one more test case.
  - Confidence: HIGH — a set of `AnyAsync` queries on tables already in the context.
  - Blind spot: Local developers with hand-made data must now pass `Reset` once, and that wipe is what they would have needed anyway.
- **Fix B**: Change only the runbook, so the first seed uses `Reset=true`.
  - Strength: No code change.
  - Tradeoff: The guard lives in a document, and the next person to enable seeding locally would not read it.
  - Confidence: MEDIUM — it depends on operator discipline.
  - Blind spot: The local development path is not covered by the Azure runbook.
- **Decision**: FIXED via Fix A. After the sentinel check, the seeder refuses when there is more than one member or any class type, class, pass, exercise or plan. New test `Refuses_to_seed_over_existing_data_without_reset`, and one sentence under the runbook's first-run command. The build is green. The test has not run yet, because Docker was not available.

### F4 — "Deterministic per club-local day" is not what the code does

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: src/Infrastructure/TestData/TestDataGenerator.cs:396-397, :452, :499; doc comment :28-31
- **Detail**: The plan (lines 96 and 205) and the class comment promise identical rows for a reseed on the same day. `AddBookings` splits past from future with `c.StartsAt <= _now`, which depends on the time of day, so the number and order of `Random` draws changes. Bookings, their ids and `CreatedAt`, exercise ids and `VideoId` picks, and plan ids, owners and items all differ between a reseed at 08:00 and one at 20:00. Members, passes, class types and classes are stable, and that is all `Reset_reproduces…` checks, a few seconds apart.
- **Fix A ⭐ Recommended**: Split on a fixed club-local instant for the day, for example today at 00:00, using `ClubTime`.
  - Strength: Makes the documented promise true for the whole data set, with a change of a few lines.
  - Tradeoff: Today's classes all count as "future" for booking purposes, which is harmless.
  - Confidence: HIGH — `_today` and `At()` already exist.
  - Blind spot: The test would need a second seed with a different `now` on the same day to pin this down.
- **Fix B**: Reword the comment and the plan so that only people, passes and classes are promised to be stable per day.
  - Strength: No behaviour change.
  - Tradeoff: Weaker reproducibility for bug reports ("plan X on member Y").
  - Confidence: HIGH.
  - Blind spot: None significant.
- **Decision**: FIXED via Fix A, adjusted: the split is the start of TOMORROW, club-local, rather than today at 00:00. This keeps the full and cancelled classes truly in the future at any seed hour. The `_now` field is gone. The new pure test `TestDataGeneratorTests.Same_club_local_day_produces_the_same_data_at_any_hour` passes, and it fails when the split is set back to `now`. The plan carries an "Adapted after impl review (F4)" note.

### F5 — The runbook contradicts itself on `log tail` (uncommitted edit)

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: context/deployment/deploy-plan.md, "Reseed procedure" step 2
- **Detail**: The new paragraph says `az webapp log tail` "joins too late to see a startup". Step 2, right below it, still says to watch `log tail` for the seed lines.
- **Fix**: Point step 2 at the Kudu `/api/logs/docker` file, the `*_default_docker.log`.
- **Decision**: FIXED. Step 2 now says to wait for `/health`, then read the newest `*_default_docker.log` through Kudu, with an explicit "do not rely on `log tail`".

### F6 — Both seeders share one DI scope and one DbContext

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Api/Program.cs:334-361
- **Detail**: If `AdminSeeder`'s `SaveChanges` throws, its `Added` entities stay tracked, and `TestDataSeeder`'s `SaveChanges` tries to insert them again. That contradicts the comment that a failure in one seeder "neither masks nor is masked by" the other.
- **Fix**: Give `TestDataSeeder` its own `CreateScope()`.
- **Decision**: FIXED. `TestDataSeeder` runs in its own `CreateScope()` after the AdminSeeder block, and the comment says why. The build is green.

### F7 — No test for the reset refusal that protects the admins

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: tests/po-prostu-silka.Tests/TestDataSeederTests.cs; src/Infrastructure/TestData/TestDataSeeder.cs:146-150, :167-172
- **Detail**: The branch that stops a wipe of every admin runs when `AdminSeed:Email` is unset or its account is missing. No test covers it. It is the most destructive path the seeder guards.
- **Fix**: Add a case with `Reset=true` and an `AdminSeed:Email` that matches no account. Assert that the row counts are unchanged.
- **Decision**: FIXED. New test `Reset_refuses_when_the_admin_seed_account_is_missing`, and the `SeedAsync` helper takes an optional `adminSeedEmail`. The build is green. The test has not run yet, because Docker was not available.

### F8 — Some members are created after their own passes and bookings

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Infrastructure/TestData/TestDataGenerator.cs:163, :178 vs :287, :297, :320
- **Detail**: A member's `CreatedAt` can be as recent as 10 or 30 days ago, but passes are issued up to about 49 days back and bookings reach about 34 days back. The result is a member whose pass or booking predates the member. It is cosmetic, but visible in the admin screens.
- **Fix**: Clamp member `CreatedAt` to at least `WindowDays + 60` days back. This moves later random draws, so do it together with F4 if that is taken.
- **Decision**: FIXED. A new `MemberMinAgeDays = 90` constant is the lower bound for both member kinds, because the oldest pass is issued about 61 days back. The new pure test `No_pass_or_booking_predates_its_member` passes.

### F9 — Stale numbers in the plan, and a formatting nit

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: plan.md:51, :295, :384, :429; src/Application/Members/MemberAccessCode.cs:44
- **Detail**: These places still say 204 members, where the adapted note correctly says 205:
  - Desired End State (plan.md:51)
  - Manual 1.6, in both the phase criteria and Progress (plan.md:295 and :429)

  Testing Strategy (plan.md:384) says "eight tests", where there are 9. Separately, `Alphabet ="…"` in `MemberAccessCode.cs` is missing a space. The roadmap edit in d3d40a1, which is not listed in the plan, is routine S-24 bookkeeping and benign.
- **Fix**: Correct the three numbers in the plan, and fix the spacing, or the whole line goes away with F1.
- **Decision**: FIXED. The plan now says 205 in Desired End State and in the manual criterion. Testing Strategy describes the 12 seeder cases and the 2 generator tests. The §5 contract has an "Adapted after impl review (F2, F3, F7)" note. The Progress step title 1.6 was left alone, because the convention is not to rename step titles. The spacing went away with F1.

### F10 — Tests could not be re-run in this session; one manual check is ready to tick

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: N/A
- **Detail**:
  - `dotnet build po-prostu-silka.slnx` passed with 0 warnings and 0 errors.
  - `dotnet test` failed 553 of 622 tests locally, all because Docker Desktop was not running. Testcontainers could not reach `//./pipe/dockerDesktopLinuxEngine`. This is an environment failure, not a code failure.
  - The CI status could not be checked either, because `gh` is not authenticated. Progress 2.1 records the deploy workflow as green, and that workflow runs `dotnet test`.
  - Progress 2.3 ("log stream shows seed counts") is unchecked, but the runbook records the counts from the Azure run. The evidence exists.
  - Manual checks 1.4–1.7, 2.4 and 2.5 are pending.
- **Fix**: Start Docker Desktop and re-run `dotnet test po-prostu-silka.slnx` before archiving. Tick 2.3.
- **Decision**: FIXED. Progress 2.3 is ticked. With Docker up, `dotnet test po-prostu-silka.slnx` passed 627 of 627 tests after all the triage fixes: 622 existing, plus 3 new seeder cases and 2 generator tests.

## Triage summary

| Outcome | Findings |
|---------|----------|
| Fixed | F1, F2, F3 (Fix A), F4 (Fix A, split at the start of tomorrow), F5, F6, F7, F8, F9, F10 |
| Skipped | none |

After triage, the build has 0 warnings and `dotnet test` passes 627 of 627. Manual checks 1.4–1.7, 2.4 and 2.5 remain open. The reseeded data differs from what is on Azure now, because the member dates, the booking split and the codes all changed. The next `Reset` brings Staging in line.
