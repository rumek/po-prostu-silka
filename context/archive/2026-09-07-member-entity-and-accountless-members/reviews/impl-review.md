<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: S-14 — Member as a first-class entity, independent of the account

- **Plan**: `context/changes/2026-09-07-member-entity-and-accountless-members/plan.md`
- **Scope**: Full plan — Phases 0–9 (commits `8f5bea1..675ed43`, 116 files, +12639/−803)
- **Date**: 2026-09-08
- **Verdict at review**: REJECTED — **after triage: APPROVED**, all 10 findings fixed
- **Findings**: 2 critical, 5 warnings, 3 observations — 10 fixed, 0 skipped

## Verdicts

Left column is the verdict as reviewed; right column is where triage left it.

| Dimension | At review | After triage |
|-----------|-----------|--------------|
| Plan Adherence | WARNING | PASS |
| Scope Discipline | PASS | PASS |
| Safety & Quality | FAIL | PASS |
| Architecture | PASS | PASS |
| Pattern Consistency | WARNING | PASS |
| Success Criteria | WARNING | PASS |

### Success criteria evidence

| Gate | At review | After triage |
|---|---|---|
| `dotnet build` (from `src/`) | PASS — 0 errors, 0 warnings | PASS — 0/0 |
| `dotnet test` (repo root, Testcontainers + real SQL Server) | PASS — 482/482 | PASS — **486/486** (4 new: F2, F4, F3, F10) |
| `npm test` (from `src/app/`, Node 24.15.0) | PASS — 431/431 across 44 files | 429/431 — the 2 failures are `app.spec.ts` and `bottom-nav.spec.ts`, caused by commit `529520f "UI fixes"` (unrelated to this slice; see below) |
| `npm run quality:check` | **FAIL** — exit 1, 49 files (see F7) | **PASS** — exit 0, Prettier clean *and* `ng lint` "All files pass linting", which had never run on this slice |
| Migration reversibility (plan step 4) | **FAIL** for `AllowLegacyUserColumnsToBeNull` (see F1) | **PASS** — fixed and drilled against a real database, rolled back and forward clean |
| `GET /health` against the docker SQL Server (plan step 1) | not run | PASS — 200 Healthy |
| Backfill against restored production copy (plan step 5) | NOT DONE — acknowledged in the plan itself | **STILL NOT DONE** — cannot be done from a dev machine, and still gates the Phase 3 deploy |
| Manual end-to-end (plan step 6) | Not verifiable from here | Not verifiable from here |

### Two failing frontend specs, not from this slice

`npm test` ends 429/431. Both failures trace to commit `529520f "UI fixes"`, which landed after the S-14 phases and is outside this review's scope:

- `app.spec.ts > renders the product name` — the brand went from `<a>Po Prostu Siłka</a>` to `<img alt="Po Prostu Siłka Logo">`, so the shell's text no longer contains the name. Expected `'Po Prostu Siłka'`, received `'Przejdź do treści'`.
- `bottom-nav.spec.ts > gives every icon-only tab an accessible name and hides the icon from it` — a visible `<span class="bottom-nav-tab-label">` was added, so the tabs are no longer icon-only and the spec's premise no longer holds.

Neither file is touched by the S-14 diff or by this triage. Both specs need updating to match the intended new UI — they are asserting the old design, not catching a bug.

### What the review confirmed as sound

Every load-bearing contract the plan is emphatic about holds in code: `MembershipStatus.Active = 0 / Blocked = 1`; all four `Members` indexes with their exact `IS NOT NULL` filters; the migration-14 backfill `CASE WHEN u.[Status] = 2 THEN 1 ELSE 0 END`; all three Member producers (`AdminSeeder`, `AuthEndpoints.RegisterAsync` with its compensating delete, `IntegrationTestFixture.EnsureUserAsync`); all three policies requiring `member_status = Active` with **no** "absent ⇒ Active" fallback; `TryBookAsync` as one method with exactly two callers and one `bookings.Add` in all of `src/`, with the admin race proven by `AdminBookingEndpointTests`; `PushSubscriptions` still keyed on `UserId`; `AspNetUsers.PhoneNumber` retained; both temporary artifacts (`DualWriteTests`, `UserIdOfMemberAsync`) deleted. The layering rule is clean — `Microsoft.EntityFrameworkCore` appears in `Domain`/`Application` only in a doc comment forbidding it. Access-code confidentiality holds: reveal is a separate route, the list ships only a boolean, and the code appears in no log. Registration collapses every claim failure into one `unknown_member_code`.

## Findings

### F1 — `AllowLegacyUserColumnsToBeNull.Down` cannot roll back once an accountless member holds club data

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality (data safety)
- **Location**: `src/Infrastructure/Persistence/Migrations/20260908082943_AllowLegacyUserColumnsToBeNull.cs:111,123,135,147`
- **Detail**: The `Down` reverses four columns from nullable back to `NOT NULL` using EF's scaffolded `defaultValue: ""`. The generated rollback SQL (`dotnet ef migrations script 20260908120501 20260908072222`) shows what that produces, and it is worse than a plain failure:

  ```sql
  -- from DropLegacyUserColumns.Down, lines 56-62
  ALTER TABLE [Bookings] ADD CONSTRAINT [FK_Bookings_AspNetUsers_MemberUserId]
      FOREIGN KEY ([MemberUserId]) REFERENCES [AspNetUsers] ([Id]);
  ...
  -- from AllowLegacyUserColumnsToBeNull.Down, lines 128-164
  UPDATE [Bookings] SET [MemberUserId] = N'' WHERE [MemberUserId] IS NULL;
  ALTER TABLE [Bookings] ALTER COLUMN [MemberUserId] nvarchar(450) NOT NULL;
  ...
  CREATE UNIQUE INDEX [IX_Bookings_Class_Member_Active]
      ON [Bookings] ([ClassId], [MemberUserId]) WHERE [Status] = 0;
  ```

  Two independent failures, both reached only when the club actually has an accountless member with a booking, plan or class — the case this entire slice exists to create:
  1. The FK to `AspNetUsers` is re-added *first* (migration 17's `Down`), and `N''` is not a valid `AspNetUsers.Id`. The `UPDATE` violates the constraint and aborts the transaction.
  2. Even without the FK, the rebuilt unique indexes carry the plain `[Status] = 0` filter with no `IS NOT NULL` term, over columns now full of `''`. Two accountless members with active bookings in the same class — or two with active plans — are duplicates, and `CREATE UNIQUE INDEX` fails.

  Migration 17's `Down` backfills legacy columns from `Members` and is documented as lossy for accountless rows, so the NULLs this `Down` trips over are guaranteed, not hypothetical. The next migration in the same series (`RequireMemberForeignKeys.cs:81-85`) rejects exactly this scaffolded default in its own comment — the reasoning simply was not applied backwards to this file. This breaches AGENTS.md's hard rule that migrations must have a working `Down`.
- **Fix A ⭐ Recommended**: Drop `defaultValue: ""` from all four `AlterColumn` calls and precede them with an explicit `migrationBuilder.Sql` that resolves the unrepresentable rows — delete or reassign bookings/plans/classes belonging to accountless members — then restore the `AND [X] IS NOT NULL` term on the two rebuilt unique index filters.
  - Strength: Produces a `Down` that actually completes, and makes the loss explicit and reviewable instead of hiding it behind an empty-string foreign key that would corrupt the previous release's reads.
  - Tradeoff: The rollback deletes club data. That is the honest cost of unwinding past the schema that made accountless members representable, and it must be stated in the migration's doc comment the way migrations 14 and 17 state theirs.
  - Confidence: HIGH — the failure is proven from the generated SQL, and migration 17's `Down` is the in-repo precedent for a documented-lossy reversal.
  - Blind spot: None remaining — the drill was run (see Decision).
- **Fix B**: Make the `Down` refuse loudly — keep the `AlterColumn`s default-free and open the method with a guard that raises when any of the four columns still holds a NULL.
  - Strength: Smallest change, destroys nothing, and turns a 2am transaction abort into a message that says why.
  - Tradeoff: The migration is then admittedly irreversible in the one state that matters, which conflicts with the deploy workflow's assumption that rollback is always available.
  - Confidence: MEDIUM — safe, but it converts a hard rule into a documented exception.
  - Blind spot: Unclear whether the deploy pipeline surfaces a migration-raised error usefully or just fails the step.
- **Decision**: FIXED via Fix A. `defaultValue: ""` dropped from all four `AlterColumn` calls; an explicit `migrationBuilder.Sql` now runs before them — deleting bookings and plans whose account key is NULL (plan items follow on the cascade), and raising `THROW 50000` for a class with no instructor account, which cannot be deleted without taking everyone's bookings with it. The two rebuilt unique indexes keep the plain `[Status] = 0` filter, which is the faithful pre-migration schema once the NULL rows are gone. A `<summary>` documenting the lossiness was added, matching migrations 14 and 17.

  **Verified against a real database** (scratch `pps-f1-check` on the docker SQL Server, since dropped): applied all migrations, seeded two accountless members holding active bookings in the *same* class plus active plans each — the exact pair that broke both the FK and the unique index — then rolled back to `AddMemberForeignKeys`. It completed. The four accountless rows were deleted, the accounted class survived, all four legacy columns came back `NOT NULL`, and both indexes returned with `([Status]=(0))`. Rolled forward again to `DropLegacyUserColumns` clean. `dotnet build` 0 errors / 0 warnings.

### F2 — Registration answers 500 when the email already sits on a member record

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality (reliability)
- **Location**: `src/Application/Auth/AuthEndpoints.cs:303`
- **Detail**: The duplicate-email pre-check is `userManager.FindByEmailAsync(request.Email)`, which consults `AspNetUsers` only. Both registration branches then write `request.Email` into a `Members` row guarded by the unique `IX_Members_Email` (`MemberConfiguration.cs:86-89`) — the claim branch via `claimed.Email ??= request.Email` (`:400`), and the ordinary branch via `Email = request.Email` on the new `Member` (`:415-420`).

  The ordinary branch is the common case and the one that hurts: an admin records a walk-in at the desk with their email address and no account; that person later registers on their own, without a code, using that same email. Identity's check passes, `CreateAsync` commits the account, `SaveChangesAsync` hits the member-email unique violation, and the compensation runs. The compensation itself is correct — `DiscardChanges()`, re-fetch, `DeleteAsync` (`:457-469`) leaves no orphan — but the caller gets a generic 500 `"Registration could not be completed."`, and every retry creates and deletes a real account. The person cannot register with their own address, and nothing in the SPA tells them why or that a code would fix it.

  `IMemberQuery.EmailExistsAsync` already performs exactly this lookup and is used by the two admin write paths (`MemberAdminEndpoints.cs:307,372`); registration is the one writer that skips it.
- **Fix**: Call `EmailExistsAsync` beside `FindByEmailAsync` at `AuthEndpoints.cs:303` and answer the existing `email_taken` 409, which the SPA already renders with a "Zaloguj się" branch.
  - Strength: Reuses the query and the failure code the rest of the slice already agreed on; turns an unrecoverable 500 into the answer the UI can act on, and stops creating-then-deleting accounts.
  - Tradeoff: `email_taken` then also reveals that the club holds an accountless record for that address. That is the same disclosure the endpoint already makes for accounts, so it is consistent rather than new — but it is worth deciding deliberately rather than inheriting. If the disclosure is unwanted, the alternative is a distinct reason that steers the person to ask the club for a code.
  - Confidence: HIGH — both branches read in full, the unique index confirmed in `MemberConfiguration.cs`, and the helper already exists with two callers.
  - Blind spot: None remaining — the fix shipped with a test.
- **Decision**: FIXED. `AuthEndpoints.RegisterAsync` now takes `IMemberQuery` and the pre-check is `memberQuery.EmailExistsAsync(request.Email, claimed?.Id, …)`, which replaces `FindByEmailAsync` outright — it already checks both tables, so this removes a redundant round trip and leaves one definition of "this address is unavailable". `exceptMemberId` is the claimed member, so a code whose row already holds the submitted address does not collide with itself. New test `RegisterEndpointTests.An_address_held_by_an_accountless_member_is_disclosed_as_email_taken` asserts the 409 *and* that no account was created and rolled back. 33/33 in `RegisterEndpointTests` + `MemberClaimTests`; build 0 warnings.

### F3 — An access code can be issued to a blocked member, and a block never revokes a live one

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (security)
- **Location**: `src/Application/Members/MemberAdminEndpoints.cs:786-796` and `:549-563`
- **Detail**: `IssueAccessCodeAsync` refuses only `member.UserId is not null` (`has_account`); it never inspects `member.Status`, so an admin can mint a live code for a Blocked member. The SPA does gate it (`features/admin/members/members.ts:327` requires `membershipStatus === 'Active'`), but this file states its own rule on that at `:534-539` — "the screen is not the boundary; this is." Separately, `BlockAsync` flips the status and rotates both stamps but leaves `AccessCode`/`AccessCodeExpiresAt` untouched, so a code issued before a block silently becomes live again the day the member is unblocked. Exploitation is blocked downstream — `RegisterAsync:297` correctly refuses a non-Active member's code under the collapsed `unknown_member_code` — so this is defence in depth plus an admin handed a code that cannot work, not an open hole.
- **Fix**: Add a `member.Status != MembershipStatus.Active` → 409 branch to `IssueAccessCodeAsync`, and null both access-code fields in `BlockAsync` alongside the status flip (same unit of work, no extra save).
- **Decision**: FIXED. `IssueAccessCodeAsync` now answers 409 `member_blocked` for a non-Active member, citing the same "the screen is not the boundary" rule `BlockAsync` states about `is_admin`. `BlockAsync` nulls `AccessCode` and `AccessCodeExpiresAt` alongside the status flip, inside the unit of work it already saves — so unblocking restores membership and nothing else. The SPA's `handleCodeFailure` grew a `codeFailureMessage` switch so `member_blocked` reads as "jest zablokowany — odblokuj, zanim wydasz kod" instead of the generic stale-list notice. Pinned by `AdminBookingEndpointTests.Blocking_revokes_a_live_access_code_and_a_blocked_member_cannot_be_issued_one`.

### F4 — The accountless block cascade, the slice's headline behaviour, has no test

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: `tests/po-prostu-silka.Tests/MemberEndpointTests.cs:296`
- **Detail**: The plan's Tests section names, verbatim, `MemberEndpointTests` including "blocking an accountless member cancels their future bookings and touches no account". What exists is `An_accountless_member_can_be_blocked_and_unblocked`, which asserts the status flip only — the file contains no occurrence of "booking" at all. The one cascade test, `BookingEndpointTests.cs:810`, seeds a member *with* an account via `fixture.CreateUserAsync`. So the Phase 5 adaptation note's claim — "the blocked-member booking cascade is keyed on the member, so it finally covers an accountless member, the case it could not reach" — is the change's marquee behaviour and is the one thing in the suite nothing pins. No adaptation note explains the omission.
- **Fix**: Extend `An_accountless_member_can_be_blocked_and_unblocked` (or add a sibling) to seed a future booking for an accountless member via `fixture.CreateMemberAsync`, block, and assert the booking is cancelled and no account was touched.
  - Strength: Closes the gap between what the plan promised and what the suite proves, using fixtures that already exist.
  - Tradeoff: None beyond the time to write it.
  - Confidence: HIGH — both files read; the fixture helper is in place.
  - Blind spot: None significant.
- **Decision**: FIXED. `AdminBookingEndpointTests.Blocking_an_accountless_member_cancels_their_future_bookings` books two accountless members into one class, blocks one, and asserts theirs is `Cancelled` with a `CancelledAt` while the other stays `Active` — the second half being what separates a member-keyed cascade from a class-keyed one. Placed in `AdminBookingEndpointTests` rather than `MemberEndpointTests` where the plan named it: that file owns no scheduling scaffolding, while this one already has the class, the trainer and the accountless booker. The test says so in its own doc comment. "Touches no account" is asserted by there being no account to touch — the block path reaches for an `ApplicationUser` in three places and every one must survive a null `UserId`.

### F5 — `IsAssignableAsync` re-inlines the predicate that `Assignable` documents itself as owning

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `src/Infrastructure/Training/TrainingPlanQuery.cs:58-60` and `:72-86`
- **Detail**: `Assignable(...)` carries the doc comment "The one definition of 'may be assigned a plan', shared by the picker and by the write-side validation so the two cannot drift", and the Phase 5 adaptation note repeats the claim as the reason `FindMemberStatusAsync` became `IsAssignableAsync`. But `IsAssignableAsync` — the write-side validation — re-states the predicate verbatim (`x.Status == MembershipStatus.Active && (x.User == null || x.User.Status == AccountStatus.Active)`) rather than calling `Assignable`. The two agree today, so behaviour is correct; the guarantee the note asserts simply does not exist, and the next edit to one is free to diverge from the other.
- **Fix**: Have `IsAssignableAsync` apply `Assignable` instead of restating it, so the single definition is real.
- **Decision**: FIXED. `IsAssignableAsync` now selects `Assignable(db.Members).Any(a => a.Id == x.Id)` rather than restating the condition, so the picker and the write-side validation read the same definition and cannot drift. It costs a correlated `EXISTS` over the primary key inside the query that was already being issued; the "two questions in one round trip" property and the `null` vs `false` distinction between `member_not_found` and `member_not_active` are unchanged. 65/65 in `TrainingPlanEndpointTests` + `MyPlanEndpointTests`.

### F6 — `change.md` and the roadmap still say S-14 is `planned` after nine shipped phases

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `context/changes/2026-09-07-member-entity-and-accountless-members/change.md:5` and `context/foundation/roadmap.md:98`
- **Detail**: `change.md` carries `status: planned` / `updated: 2026-09-07` with all nine phases committed, and the roadmap's slice table still shows S-14 as `planned` while `## Milestone History` already records M-2 as open around it. Separately, `plan.md` has no `## Progress` section at all, so there are no checkboxes for `/10x-status` or `/10x-archive` to read — completion state currently exists only as commit messages and the per-phase "Adapted during implementation" notes. (This review's own stamp sets `change.md` to `impl_reviewed`, which resolves half of it.)
- **Fix**: Flip the roadmap's S-14 row to `done` and add a `## Progress` section to `plan.md` with the nine phases checked, so the tooling can see what the commits already show.
- **Decision**: FIXED. `roadmap.md:98` now reads `done`. `plan.md` gained a `## Progress` section in the house convention — the nine phases checked with their commit shas, and the six `## Verification` steps recorded as observed rather than as intended: `quality:check` (3b) and the production-restore backfill (5) and the manual end-to-end (6) are left **unchecked**, with 3b and 4 pointing at F7 and F1. Step 1b was verified for it rather than assumed — the app was run and `GET /health` answered 200 Healthy against the docker SQL Server.

### F7 — `npm run quality:check` fails on 49 files — CRLF in the working tree, not style drift

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: `src/app/` (49 files, all of them touched by this change)
- **Detail**: `npm run quality:check` exits 1 at the `prettier --check` step, so `ng lint` never runs at all. Every one of the 49 files is one this change wrote; untouched siblings pass (`features/auth/login/login.ts` is LF on disk, `core/auth/auth.models.ts` is CRLF). The committed blobs are all LF and are Prettier-clean — verified by writing `git show HEAD:...auth.models.ts` back into the project tree and re-running the check, which reports "All matched files use Prettier code style!". So this is a working-tree line-ending artifact under `core.autocrlf=true`, not real formatting drift, and the repo's content is fine. It still means AGENTS.md's "run `quality:check` before committing frontend changes" gate cannot pass on this machine, and the lint half of the gate has been silently skipped for the whole slice.
- **Fix**: Add a `.gitattributes` pinning `* text=auto eol=lf` (or set `endOfLine: "auto"` in `.prettierrc`), renormalise the working tree, then re-run `quality:check` so `ng lint` actually executes for the first time on this change.
- **Decision**: FIXED. New root `.gitattributes` pins `* text=auto eol=lf` with the binary types listed out, so the gate means the same thing on every machine regardless of each one's `core.autocrlf` — which matters here because CI gates the deploy on it. The 49 working-tree files were converted in place rather than via `git add --renormalize`, to leave the index and the unrelated staged `logo.png` alone; `git diff` afterwards reports content changes in `members.ts` only, confirming the conversion was a no-op against the LF blobs. Checked repo-wide before committing to the pattern: every blob sampled across `src/`, `tests/` and `.github/` is already LF, so the attribute matches what is committed and triggers no mass renormalisation.

  **`npm run quality:check` now exits 0** — "All matched files use Prettier code style!" *and* "All files pass linting." This is the first time `ng lint` has run against this slice, and it is clean.

### F8 — Dead `UserManager` round-trip left in `TrainingPlanEndpoints.CreateAsync`

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/Application/Training/TrainingPlanEndpoints.cs:302,310-312`
- **Detail**: The handler still injects `UserManager<ApplicationUser>` solely to compute `var authorUserId = userManager.GetUserId(principal)`, whose only remaining use is the null guard on the next line. It existed to fill `TrainingPlans.AssignedByUserId`, a column dropped in Phase 9. Phase 9's note says entities and configurations were cleaned but "only the endpoints were untouched" — this is what that left behind: an identity lookup per plan creation for a value nothing consumes.
- **Fix**: Drop the `userManager` parameter and `authorUserId`, keeping the `authorMemberId is null` half of the guard.
- **Decision**: FIXED. `CreateAsync` no longer takes `UserManager<ApplicationUser>`; the guard is `authorMemberId is null` alone, with a comment recording that the account id went when Phase 9 dropped `AssignedByUserId`. One fewer Identity round trip per plan creation.

### F9 — Stale and duplicated XML docs referencing columns that no longer exist

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/Application/Scheduling/ClassEndpoints.cs:906-956`, plus four others
- **Detail**: `ValidateInstructorAsync` carries **two** stacked `<summary>` blocks with an orphaned `<returns>` between them; the surviving pre-S-14 block still says "Whether this **account** may be named as an instructor" and "No CancellationToken: UserManager exposes no token overload" — and the method now takes one (`:961`). Alongside it: `BookingEndpoints.cs:43` has a `<see cref="MemberUserId"/>` to a member removed from `ClassBooking`; `TrainingPlanEndpoints.cs:146` references the dropped `TrainingPlan.AssignedByUserId`; `TrainingPlan.cs:9` still describes the filtered unique index as being on `MemberUserId`; `ClassStore.cs:25` still says the `Instructor` navigation points at the previous *account*. Two are broken `cref`s that stay silent only because `GenerateDocumentationFile` is off. In a codebase whose doc comments carry this much of the reasoning, these are the comments most likely to mislead the next reader.
- **Fix**: Delete the superseded `<summary>`/`<returns>` pair on `ValidateInstructorAsync` and correct the four stale references.
- **Decision**: FIXED. `ValidateInstructorAsync` now carries one `<summary>` and one `<returns>`: the S-14 paragraphs kept, the three still-true paragraphs from the old block merged in (the `IsInRoleAsync` normalisation rationale, the collapsed refusal, "returns the account"), and the false "No CancellationToken" paragraph dropped — the method takes one. The four stale references corrected: `MemberUserId` → `MemberId` (`BookingEndpoints.cs:43`), `TrainingPlan.AssignedByUserId` → `AssignedByMemberId` (`TrainingPlanEndpoints.cs:146`, target verified to exist at `TrainingPlan.cs:59`), the index doc on `TrainingPlan.cs:9` → `(MemberId)`, and `ClassStore.cs:25` → the previous **member**, since the write paths now pass a name rather than an `ApplicationUser`.

### F10 — `PushEndpointTests` was listed as changed and never touched

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `tests/po-prostu-silka.Tests/PushEndpointTests.cs`
- **Detail**: The plan's Tests section lists `PushEndpointTests` under "Changed" with the rationale "push stayed account-keyed". The file is absent from `git diff --name-only 8f5bea1^..HEAD`. Nothing is broken — `PushSubscriptionConfiguration` is verifiably still keyed and indexed on `UserId`, never `MemberId` — but the regression pin the plan wanted for that invariant was never written, and it is precisely the invariant a future slice would be tempted to "tidy up" onto `MemberId`. (Also noted, and benign: `MemberClaimTests.cs` is a new file named nowhere in the plan; its content is squarely in Phase 3's scope.)
- **Fix**: Add one assertion to `PushEndpointTests` that a subscription is stored against the account id and survives a member-key lookup returning nothing.
- **Decision**: FIXED. `PushEndpointTests.A_subscription_is_stored_against_the_account_not_the_member` resolves the stored `UserId` through Identity to assert it is the account's, then asserts it differs from that person's member id. A first attempt asserted the account id does not parse as a `Guid`; that was wrong — Identity's default generator is `Guid.NewGuid().ToString()`, so both are Guid-shaped and only identity distinguishes them. The test says so, so the next reader does not reach for the same shortcut.
