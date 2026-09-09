<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Membership Pass and Staff Booking

- **Plan**: `context/changes/membership-pass-and-staff-booking/plan.md`
- **Scope**: Phases 1–8 of 8 (full plan)
- **Date**: 2026-09-09
- **Verdict**: NEEDS ATTENTION at review time; ALL 9 FINDINGS FIXED during triage
- **Findings**: 0 critical, 4 warnings, 5 observations — 9 fixed, 0 skipped, 0 accepted

## Automated verification (re-run during this review)

| Check | Result |
|-------|--------|
| `dotnet build` from `src/` | 0 warnings, 0 errors |
| `dotnet test` from repo root | 514 passed, 0 failed (3 m 1 s) |
| `npm run quality:check` from `src/app/` | Prettier + ESLint clean |
| `npm test` from `src/app/` | 42 files, 422 tests passed |
| `npm run build` from `src/app/` | initial total 504.02 kB (budget 550 kB), no warning |
| Phase 6 ordering (migration before code) | `a4c31c5` 14:32 precedes `17d9be6` 14:50 |
| Layering (`using Microsoft.EntityFrameworkCore` in Domain/Application) | no matches |
| 7.5 grep — removed member booking routes | clean |
| 8.5 grep — no dead approval references | **not clean** at review time (see F3); clean after triage |

### Re-verified after triage fixes

| Check | Result |
|-------|--------|
| `dotnet build` from `src/` | 0 warnings, 0 errors |
| `dotnet test` from repo root | 514 passed, 0 failed (3 m 15 s) |
| `npm run quality:check` from `src/app/` | Prettier + ESLint clean |
| `npm test` from `src/app/` | 42 files, 421 tests passed (F4 removed one duplicate) |
| 8.5 grep re-run | every remaining hit is past-tense historical narration |

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | WARNING |

## Findings

### F1 — Registration rate limiter partitions on the client-controlled end of X-Forwarded-For

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `src/Application/Auth/RateLimitPolicies.cs:61-79`, wired at `src/Program.cs`
- **Detail**: `PartitionKey` reads `Headers["X-Forwarded-For"].FirstOrDefault()`, splits on `,` and takes the **leftmost** segment. Azure App Service *appends* the observed client IP rather than replacing the header, so a request forged as `X-Forwarded-For: 1.1.1.1` arrives as `1.1.1.1, <real-ip>` and partitions on the attacker's chosen value. Rotating that value per request means never sharing a partition — the cap is not weakened but bypassed. No `UseForwardedHeaders`/`KnownProxies` is registered in `Program.cs`. The method body is unchanged by this slice (pre-existing, written for `/forgot-password`), but Phase 6 §2 made it the **replacement for the approval gate's anti-spam role**, so its load-bearing status is new. The file's own comment concedes the header is attacker-controlled and calls this "a courtesy cap on volume"; taking the rightmost segment would still throttle unsophisticated repeat scripts at no cost.
- **Fix A ⭐ Recommended**: Take the last segment (the platform-appended hop) instead of the first — `Split(',').LastOrDefault()?.Trim()`.
  - Strength: One-line change; restores a real cap against the repeat-script case the limiter exists for, and keeps the existing test shape (a bare address still parses).
  - Tradeoff: Still header-derived, so still not an authorization control — the doc comment stays honest but needs its reasoning updated.
  - Confidence: HIGH — App Service's append behaviour is the documented norm, and the code already handles a comma-joined value.
  - Blind spot: Not verified against this app's actual deployed App Service headers; worth one check of a live request's raw header.
- **Fix B**: Register `ForwardedHeadersMiddleware` with an explicit `ForwardLimit`/`KnownProxies` and read `Connection.RemoteIpAddress` after it.
  - Strength: The framework-sanctioned path; fixes every current and future consumer of the client IP at once.
  - Tradeoff: Wider blast radius — touches request pipeline order in `Program.cs`, and needs the proxy count pinned correctly or it silently does nothing.
  - Confidence: MEDIUM — correct in principle, but the trusted-hop count for this App Service setup is unverified.
  - Blind spot: Interaction with the test fixture's per-client `X-Forwarded-For` (added in Phase 6) is unchecked; the suite may need adjusting.
- **Decision**: FIXED via Fix A — partition key now reads the last (platform-appended) `X-Forwarded-For` segment; the doc comment records why. Verified: build warning-free, 42/42 in `RegisterEndpointTests` + `PasswordEndpointTests`.

### F2 — `ApplicationUser.Status` still defaults to `Pending`, which AGENTS.md now says is never produced

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence / Safety & Quality
- **Location**: `src/Domain/ApplicationUser.cs:27` (and its doc comment, lines 22-26)
- **Detail**: The property initializer is still `= AccountStatus.Pending`, and its doc comment still says "the admin actions that change it land with S-01 (approve)" — an action this slice deleted. `AGENTS.md` gained the assertion "`AccountStatus.Pending` stays declared but is never produced", and `AccountStatus.cs:15-38` documents the same. Both current creation sites set `Status` explicitly (`AuthEndpoints.cs:346`, `AdminSeeder.cs:73`), so there is no live bug. The hazard is latent: a future `new ApplicationUser` that omits `Status` produces an account that now *logs in* (Blocked is the only refusal left, `AuthEndpoints.cs:214`) but fails every `ActiveMember` policy, with the explanatory `/pending` screen deleted — precisely the state `ActivatePendingAccounts` was written to eliminate. The file was not touched by this slice, so this is inherited, but Phase 8 §5's mandate was to correct documents that still describe the retired model.
- **Fix A ⭐ Recommended**: Leave the `Pending` default (0 is the persisted default anyway — changing the initializer would not change the column default) and rewrite the doc comment to say what the field now means and that Pending is retired, cross-referencing `AccountStatus.Pending`.
  - Strength: Comment-only, zero behavioural risk; matches the treatment `AccountStatus.cs` and `MemberListFilter` already received, and fixes the actual defect (a comment asserting something untrue).
  - Tradeoff: The latent "forgot to set Status" hazard remains, mitigated only by documentation.
  - Confidence: HIGH — consistent with the enum's own reasoning that 0 must stay readable.
  - Blind spot: None significant.
- **Fix B**: Change the initializer to `AccountStatus.Active` and update the comment.
  - Strength: A forgotten `Status` yields a working account rather than a limbo one with no explanatory screen.
  - Tradeoff: Fails *open* — a future creation path that should have vetted the account silently produces a usable one. Also diverges from the persisted column default of 0, so C#-created and raw-SQL-inserted rows would differ.
  - Confidence: MEDIUM — safer in the common case, riskier in the case that matters.
  - Blind spot: Have not audited whether any migration or seed inserts rows without going through the C# initializer.
- **Decision**: FIXED via Fix B — the initializer is now `AccountStatus.Active`, with a doc comment stating that the SQL default stays 0, so a row inserted outside the initializer still reads Pending. The adjacent stale `CreatedAt` comment ("the admin's pending list orders by it") was corrected in the same edit, at the user's direction.

### F3 — Success criterion 8.5 is checked, but the grep it specifies does not come back clean

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: `src/Application/Members/MemberAdminEndpoints.cs:903`; `src/app/src/app/core/auth/auth.models.ts:63-64`; `src/app/src/app/app.routes.ts:76`
- **Detail**: Progress item 8.5 ("Grep finds no dead approval references beyond the retired enum member") is marked `[x]`, but the grep as written surfaces live doc rot:
  - `MemberAdminEndpoints.cs:903` — `<see cref="IPendingMemberQuery"/>` points at a type this slice deleted, and its prose still describes "the approvals queue orders oldest first". A dangling cref that no longer compiles as a reference.
  - `auth.models.ts:63-64` — asserts "a pending member now receives a session and is routed to `/pending` by status", describing a screen Phase 8 §3 deleted.
  - `app.routes.ts:76` — "register and the pending screen — everything an unapproved member ever sees".
  This is exactly the failure mode `context/foundation/lessons.md` records ("a document asserting something untrue costs every future reader"), and the criterion that would have caught it was rubber-stamped.
- **Fix**: Correct the three comments (drop the `IPendingMemberQuery` cref and its queue prose; restate the `pending_approval` and route comments in terms of what is true post-S-16), then re-run the 8.5 grep before re-checking it.
- **Decision**: FIXED — and the fix was BROADER than reported. Re-running the 8.5 grep after the three named corrections surfaced four more: `register.ts:21` ("The account is created Pending" — flatly false, in a file this slice modified), `auth.guard.ts:11`, `auth.service.ts:97`, and a `register.spec.ts` fixture named `PENDING` whose status was `'Active'` (renamed `REGISTERED`, test title corrected). All seven corrected; every remaining grep hit is now past-tense historical narration.

### F4 — A login spec now duplicates its neighbour under a title describing deleted behaviour

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/app/src/app/features/auth/login/login.spec.ts:101-113`
- **Detail**: The test is titled `routes a pending member to the awaiting-approval screen` and led by the comment "Since S-01 a pending member logs in successfully — the routing decision is by status". The awaiting-approval screen is gone. The body was edited to flush `{ ...ACTIVE, status: 'Active' }` and assert `navigate(['/'])` — byte-for-byte the assertion of the preceding test at line 95-99. It passes, proves nothing the test above it does not, and its name actively misinforms.
- **Fix**: Delete the test (the case it covered no longer exists) or rename it to what it now checks — that login routes to `/` regardless of returned status — and drop the stale lead comment.
- **Decision**: FIXED — test deleted. Its assertion was byte-identical to `routes an active member to the app` directly above it. Frontend suite 421/421 (was 422; the delta is this removal).

### F5 — `MemberListFilter.Pending` kept, though the plan said it goes

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `src/Application/Members/MemberAdminEndpoints.cs:76-84`
- **Detail**: Phase 8 §1's contract states verbatim "`MemberListFilter` loses its `Pending` member." The implementation instead keeps `Pending = 0` under `[Obsolete]` with a RETIRED doc comment, reasoning that the numeric value is pinned like `AccountStatus.Pending`'s. That rationale is weaker here — this is a query-string filter, not a persisted column — but the "bookmarked filter" argument in the comment is real, the SPA correctly dropped `'Pending'` from its own union (`member-admin.models.ts:30`), and nothing produces or sends it. The gap is that no "**Adapted during implementation.**" note covers the choice, which is the rule `lessons.md` records.
- **Fix**: Add an adaptation note to Phase 8 §1 in `plan.md` recording that the enum member was retired-in-place rather than removed, and why.
- **Decision**: FIXED — adaptation note added to Phase 8 §1 recording that `MemberListFilter.Pending` was retired in place, with the query-string / bookmarked-filter reasoning the code comment gives.

### F6 — Two other contract expansions shipped without adaptation notes

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `src/Application/Members/IMembershipPassStore.cs`; `src/Application/Members/MembershipPassEndpoints.cs:61,407`
- **Detail**: Both are justified, neither is documented in the plan. (a) Phase 1 §5 specified `AddAsync, FindAsync, FindOverlappingAsync, RemoveAsync`; what shipped has synchronous `Add`/`Remove` (staging-only) plus `FindCoveringAsync` (a *tracked* twin of the query method — required because Phase 3's gate must rotate the stamp of a tracked pass) and `FindManyAsync`. Phase 3 could not have worked with the Phase 1 interface. (b) Phase 2 §1 enumerated seven failure reasons; `invalid_type_name` was added as an eighth for a blank or oversized type name. The plan is the artefact future reviews treat as ground truth, and it currently under-describes both.
- **Fix**: Add "**Adapted during implementation.**" notes to Phase 1 §5 and Phase 2 §1 recording the expanded interface and the eighth failure reason.
- **Decision**: FIXED — adaptation notes added to Phase 1 §5 (synchronous `Add`/`Remove`, plus `FindCoveringAsync` and `FindManyAsync` as consequences of Phase 3's stamp-rotation contract) and Phase 2 §1 (the eighth failure reason, `invalid_type_name`).

### F7 — `IMembershipPassStore.FindManyAsync` is dead code; the cascade re-implements it

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/Application/Members/IMembershipPassStore.cs:72-83`, `src/Infrastructure/Members/MembershipPassStore.cs:50-64` vs `src/Infrastructure/Scheduling/BookingStore.cs:91-109`
- **Detail**: The method's doc comment says it exists "for the cancel paths… the block cascade releases a handful of bookings at once and they may sit on different passes" — but `CancelActiveFutureForMemberAsync` does not call it; it runs the same `Where(...).ToListAsync()` directly against `db.MembershipPasses`. `FindManyAsync` has no callers in `src/`. Harmless today (the two copies are identical), but a later edit to one — say adding a status filter — will not reach the other, and the interface carries documented surface area for nothing.
- **Fix**: Have `CancelActiveFutureForMemberAsync` call `FindManyAsync`, or delete the unused interface method and its implementation.
- **Decision**: FIXED via the wire-up option — `BookingStore.CancelActiveFutureForMemberAsync` now calls `passes.FindManyAsync(...)` instead of duplicating the query, and drops its own empty-set guard since the store already short-circuits. This also makes F6's new adaptation note ("a batch load for the block cascade") true. Verified: 74/74 across `MemberBlockPolicyTests`, `MemberAdminEndpointTests`, `BookingEndpointTests`.

### F8 — `MaxValidityDays` has no client-side mirror, against the form's own stated standard

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/app/src/app/features/admin/members/member-passes.ts:15-23,84-89,175-176`
- **Detail**: The component mirrors `TypeNameMaxLength`, `MinEntryCount` and `MaxEntryCount`, and its header comment sets the standard explicitly: "a form that accepts what the API rejects is a worse experience than one with no validation at all." `MembershipPassRules.MaxValidityDays = 400` is enforced server-side (`MembershipPassEndpoints.cs:420`, `invalid_range`) but has no client mirror — the form checks only `validTo < validFrom` (line 176). An admin issuing a two-year karnet is refused only after the round trip, with a message that reads as a range-ordering error.
- **Fix**: Mirror `MaxValidityDays` as a fourth constant and add the span check beside the existing `validTo < validFrom` guard.
- **Decision**: FIXED — `MAX_VALIDITY_DAYS = 400` mirrored and a span check added. The check needed its OWN message: the API answers an over-long span with `invalid_range`, whose shared Polish text says the end date precedes the start — wrong about what actually went wrong. Quality gate clean.

### F9 — Plan's Migration Notes claim four migrations and list three

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `context/changes/membership-pass-and-staff-booking/plan.md` — Migration Notes
- **Detail**: "Four migrations land across the stream, in this order:" is followed by three names. Exactly three exist and each matches its phase contract: `20260909105443_AddMembershipPasses`, `20260909111224_AddBookingMembershipPass`, `20260909123050_ActivatePendingAccounts`. A defect in the plan's prose, not in the code — but the plan is what the next reader trusts. The parenthetical phase labels are also stale, since `AddBookingMembershipPass` moved to Phase 2 per that phase's own adaptation note.
- **Fix**: Change "Four" to "Three" and correct `AddBookingMembershipPass`'s phase label to Phase 2.
- **Decision**: FIXED — "Four" corrected to "Three", and `AddBookingMembershipPass` relabelled Phase 2 to match that phase's own adaptation note.

## What was verified clean

- **Concurrency (the crux).** In `BookingEndpoints.TryBookAsync` (`src/Application/Scheduling/BookingEndpoints.cs:261-390`) the pass lookup and entries-used count are re-read on every retry attempt, not hoisted; `entity.ConcurrencyStamp` and `pass.ConcurrencyStamp` rotate in the same `TrySaveAsync`, correctly modelling two independent pools. Every path that returns an entry rotates the pass stamp — `ReleaseAsync`'s `ReturnEntryAsync` and the block cascade in `BookingStore.CancelActiveFutureForMemberAsync` (every *distinct* pass), while correctly not rotating the class stamp there. `RevokePassAsync`'s check-then-delete race is closed by `IsConcurrencyToken()` on the pass stamp, turning an FK-restrict 500 into a clean 409. Inclusive range logic is inclusive at both ends; `ClubTime` handles the DST spring-forward gap and the ambiguous autumn hour correctly.
- **Layering.** No EF Core reference in `src/Domain` or `src/Application`; the three new seams are EF-free interfaces implemented in `Infrastructure`.
- **Migrations.** All three have working `Down` methods except the deliberate, in-line-justified no-op in `ActivatePendingAccounts`.
- **Trainer scoping.** The ownership guard covers `GetForClassAsync`, `BookForMemberAsync` and `ReleaseAsync`, admin bypasses first, refusal is 403 not 404, and the group doc comment warning the plan required is present.
- **Scope discipline.** No "What We're NOT Doing" boundary crossed: no money, no unlimited pass, no stored entry counter, no trainer-member relationship, no member-facing purchase, no in-app notification centre, `MembershipStatus.Blocked` cascade untouched. Every file changed outside the plan's named lists is a direct consequence of a planned change.
- **Positive deviation.** `booking-failure.ts` fully retired the partial `ADMIN_MESSAGES` table into one exhaustive `Record`, removing the "known soft spot" the plan flagged rather than merely shrinking it.
