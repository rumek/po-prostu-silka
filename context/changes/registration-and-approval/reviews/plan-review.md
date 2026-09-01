<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Registration and approval (S-01)

- **Plan**: `context/changes/registration-and-approval/plan.md`
- **Mode**: Deep
- **Date**: 2026-09-01
- **Verdict**: REVISE → SOUND after triage (4 fixed, 1 accepted)
- **Findings**: 1 critical, 2 warnings, 2 observations

## Verdicts

| Dimension | At review | After triage |
|-----------|-----------|--------------|
| End-State Alignment | WARNING (F1's consequence) | PASS |
| Lean Execution | PASS | PASS |
| Architectural Fitness | PASS | PASS |
| Blind Spots | FAIL | PASS (F3 accepted, not fixed) |
| Plan Completeness | WARNING | PASS |

## Grounding

15/15 paths ✓, 8/8 line anchors ✓, brief↔plan ✓, Progress↔Phase ✓ (36 rows at review time, 12/13/11 across three phases; no checkboxes outside `## Progress`). **After triage: 39 rows, 15/13/11** — F1 added 1.3 and 1.9, F2 added 1.10.

## Findings

### F1 — Approval does not refresh the member's `account_status` claim

- **Severity**: ❌ CRITICAL
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Blind Spots (with an End-State Alignment consequence)
- **Location**: Phase 1 — approve endpoint; Phase 2 — D11 awaiting screen; Phase 3 — criterion 3.9
- **Detail**:
  `ActiveMember` and `Admin` check the `account_status` **cookie claim**, not the database
  (`AuthorizationPolicies.cs:36-44`). That claim is minted by `AppUserClaimsPrincipalFactory` at
  sign-in and re-minted only when the security-stamp validator refreshes — every **30 minutes**
  (`Program.cs:118-119`).

  The approve endpoint writes `Status = Active` to the database and stops. The member's cookie still
  says `Pending` for up to 30 minutes. Meanwhile `/me` calls `userManager.GetUserAsync(principal)`,
  which reads the **database**, so it correctly returns `"status":"Active"`. So D11's refresh button
  reports success, `activeMemberGuard` sees `isActive()` and routes to `/` — and every
  `ActiveMember`-protected API call returns 403 until the stamp interval elapses.

  The plan's own success criterion 3.9 ("the approved member refreshes and reaches the app") **would
  pass while this is broken**, because `Home` is a placeholder that makes no API calls (stated in
  Open Risks). The defect ships green and detonates in S-03 when `Home` becomes the schedule.

  The same mechanism is what D2's rationale already invokes for blocked members — the plan reasons
  about claim staleness for revocation but not for approval, which is the direction this slice
  actually exercises.
- **Fix A ⭐ Recommended**: Add `POST /api/auth/refresh` calling `signInManager.RefreshSignInAsync(user)`, returning `CurrentUser`. The awaiting screen's button calls it instead of `/me`.
  - Strength: Re-mints the principal from the current entity through the existing claims factory, so status and roles are both correct immediately. Keeps the session, which is what D11 specifies. The button already exists — this only changes which endpoint it calls.
  - Tradeoff: One more endpoint on the auth surface, and it must be `RequireAuthorization()` (not `ActiveMember`), or a pending member cannot call the very thing that un-pends them.
  - Confidence: HIGH — `RefreshSignInAsync` regenerates claims via `AppUserClaimsPrincipalFactory`, the same path sign-in uses; verified against `AuthEndpoints.cs:72` and the factory at `AppUserClaimsPrincipalFactory.cs:26`.
  - Blind spot: Behaviour when the member was blocked between sign-in and refresh is unverified — likely correct (re-minted as Blocked, then policy-refused), but untested.
- **Fix B**: Call `UserManager.UpdateSecurityStampAsync(user)` in the approve handler.
  - Strength: One line, no new endpoint. Invalidates the stale cookie at the moment of approval.
  - Tradeoff: A stamp mismatch makes `SecurityStampValidator` **reject** the principal and sign the member out — so the awaiting screen's button finds them logged out and bounces to `/login`. That is a coherent flow, but it contradicts D11 as written and must be designed rather than stumbled into.
  - Confidence: MEDIUM — the sign-out consequence is Identity's documented behaviour, but the exact interaction with the awaiting screen's redirect is unverified.
  - Blind spot: Whether the member sees a confusing "session expired" bounce right after being told they were approved.
- **Decision**: FIXED via Fix A — `POST /api/auth/refresh` added in Phase 1; D11 corrected to call it; regression test pinned to `/test/active-member`

### F2 — A third existing test breaks, and the plan accounts for only two

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1 — Tests; plan's "Current state → Tests" section
- **Detail**:
  The plan states that `AuthEndpointTests.cs:38` is the one test to rewrite and `:52` stays green.
  There is a third: `PushEndpointTests.Pending_member_can_also_subscribe` (lines 61-72) logs in as
  `TestUsers.PendingMemberEmail` and asserts `Unauthorized`.

  Its own comment says: *"A pending member cannot log in at all, so they subscribe only after
  approval in practice. Assert the current contract rather than a hoped-for one."* D1 makes the
  hoped-for contract real. This test is not collateral damage — it is a placeholder written for
  exactly this slice, and it should be **completed** (pending member logs in, subscribes, gets
  `204`), not merely repaired.

  This also strengthens the plan's own argument: it is a fourth F-02/F-03 accommodation for
  authenticated-pending members, alongside the three the Overview lists.
- **Fix**: Add a Phase 1 task to complete `PushEndpointTests.Pending_member_can_also_subscribe` into a real subscribe assertion, and correct the "Current state → Tests" section to name all three affected tests.
- **Decision**: FIXED — Phase 1 task 1.10 added; "Current state → Tests" now names all three affected tests

### F3 — A role-less user is unrecoverable, and the plan's fallback can itself fail

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 1 — `RegisterAsync`, the `AddToRoleAsync` failure path
- **Detail**:
  `CreateAsync` and `AddToRoleAsync` are two separate saves; there is no transaction across them.
  The plan's fallback is "delete the user and return 500" — but the delete is a third save that can
  fail for the same reason the role assignment did (a DB blip on the 5-DTU tier).

  The residue is a `Pending` user with no role. `ActiveMember` requires
  `RequireClaim(status, Active)` **and** `RequireRole(ApplicationRoles.All)`
  (`AuthorizationPolicies.cs:38-40`), so approving them produces an account that passes the status
  check, fails the role check, and is refused everywhere — with no admin surface to repair it, since
  D5 ships approve only.

  F-02's review already skipped a related finding (role-seeding failures logged as success), so this
  is the second time role assignment has been the weak link.
- **Fix**: Make the approve endpoint self-healing — `if (!await userManager.IsInRoleAsync(user, ApplicationRoles.User)) await userManager.AddToRoleAsync(...)` before flipping status. Keep the register-time delete as the primary path; this makes the one operation that matters idempotent in the role dimension too, at the one point an admin is already acting on the account.
  - Strength: Single insertion at the exact chokepoint every account must pass through; no new surface, no schema, no admin UI.
  - Tradeoff: Puts a repair concern inside a business action — needs a comment explaining why, or it reads as redundant.
  - Confidence: HIGH — `IsInRoleAsync`/`AddToRoleAsync` are already used by `AdminSeeder`, so the pattern exists in-repo.
  - Blind spot: Does not help a role-less member who is never approved; they stay stuck, which is the correct outcome anyway.
- **Decision**: ACCEPTED — recorded in Open Risks with the manual repair and a note that a third occurrence on this mechanism should prompt a real fix

### F4 — Phase Success-Criteria headings deviate from the Progress format contract

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: All three phases — "### Success criteria"
- **Detail**:
  The `## Progress` section is correct and parses cleanly (`#### Automated` / `#### Manual`, 36 rows,
  no stray checkboxes). But the phase bodies use bold `**Automated**` / `**Manual**` rather than the
  `#### Automated Verification:` / `#### Manual Verification:` headings the format reference
  specifies. `/10x-archive` reads the Progress section and is unaffected; `/10x-impl-review` looks
  for Automated/Manual bullets in Phase blocks and may not find them.
- **Fix**: Promote the bold labels in the three phase bodies to `#### Automated Verification:` / `#### Manual Verification:` headings.
- **Decision**: FIXED — all six phase-body labels promoted to `#### Automated Verification:` / `#### Manual Verification:`

### F5 — An active member can navigate to the awaiting-approval screen

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 2 — route table, `/pending`
- **Detail**:
  `/pending` carries `authGuard` only, deliberately, so a Pending member can reach it. But nothing
  stops an **Active** member from typing `/pending` and seeing a screen telling them to wait for
  approval they already have. Harmless, and D2 means a Blocked member can never get there at all.
- **Fix**: Have the awaiting-approval component redirect to `/` when `isActive()` on load — one line in the component that already reads status.
- **Decision**: FIXED — awaiting screen redirects to `/` when `isActive()` on load; covered in `pending.spec.ts`
