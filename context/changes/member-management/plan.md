# Member Management (S-02) Implementation Plan

## Overview

Generalise the admin surface S-01 shipped — a pending-approvals queue — into a full member list with
status badges, a server-side status filter and client-side search (FR-005), and add block/unblock
(FR-004). Along the way, close the session-revocation gap that F-02 explicitly deferred to this
slice: today nothing sets `AccountStatus.Blocked`, and nothing would cut the blocked member's live
cookie if it did.

## Current State Analysis

The slice was marked `blocked` in the roadmap on PRD Open Question 1 ("what happens to a blocked
member's existing bookings and assigned plan?"). `context/changes/member-management/frame.md`
established that the question is mis-attached: `Booking` and `TrainingPlan` do not exist in
`src/Domain/`, in `AppDbContext`, or in any migration, and nothing S-02 ships touches them. The
question was reassigned to S-04 and S-07 on 2026-09-01 and S-02 unblocked.

What exists and is directly reusable:

- **Endpoint group** — `/api/admin/members` with `RequireAuthorization(AuthorizationPolicyNames.Admin)`
  applied at the group, so a new endpoint added there cannot ship unauthenticated
  (`src/Application/Members/MemberAdminEndpoints.cs:37-45`).
- **Transition template** — `ApproveAsync` (`MemberAdminEndpoints.cs:59-123`): idempotency check on
  current status, manual `ConcurrencyStamp` rotation making that check atomic, one
  `SaveChangesAsync`, lost-race fallback to `Ok()`, and a documented prohibition on explicit
  transactions (`EnableRetryOnFailure` requires an execution strategy or it throws at runtime).
- **Read seam** — `IPendingMemberQuery` in Application, implemented by `PendingMemberQuery` in
  Infrastructure, DB-projected and filtered on an indexed column
  (`MemberAdminEndpoints.cs:130-133`, `src/Infrastructure/Members/PendingMemberQuery.cs:16-29`).
- **The index this slice needs already exists**, added for it by name: `builder.HasIndex(x => x.Status)`
  — *"S-02's member list filters by status (FR-005)"*
  (`src/Infrastructure/Persistence/Configurations/ApplicationUserConfiguration.cs:29-30`).
- **Frontend precedent** — `features/admin/approvals/` (component, template, styles, Vitest spec)
  with loading / load-failed / empty states as signals, a per-row busy `Set`, per-row failure
  display, Polish copy, and `DatePipe` with a Polish locale already wired. Service and DTO
  conventions in `core/admin/member-admin.service.ts` and `member-admin.models.ts`; admin route
  guard in `core/auth/admin.guard.ts`; route registration in `app.routes.ts:19-25`.

What is missing:

- No code path anywhere sets `Status = Blocked`. The enum value exists
  (`src/Domain/AccountStatus.cs:21`) and login already refuses it
  (`src/Application/Auth/AuthEndpoints.cs:97-101`), but the transition into it was never built.
- Nothing rotates `SecurityStamp` on a status change. F-02's plan assigned that obligation forward
  by name — *"whichever slice implements block (S-02) must also invalidate the cookie via Identity's
  security stamp"* (`context/archive/2026-08-31-auth-identity-foundation/plan.md:134-139`).
- The seeded admin is an ordinary `ApplicationUser` with `Status = Active` in the same table
  (`src/Infrastructure/Identity/AdminSeeder.cs:57-65`), and `PendingMemberQuery` filters on status
  alone — so a query copied from it surfaces the admin as a blockable member.

### Key Discoveries:

- **Rotating the security stamp is not sufficient on its own.** `UpdateSecurityStampAsync`
  invalidates the cookie, but `SecurityStampValidatorOptions.ValidationInterval` decides how often
  that invalidation is *checked* — currently 30 minutes (`src/Program.cs:118-122`, whose own comment
  says the number "matters to S-02"). Both must change for block to bite promptly.
- **Status is read from a claim, never the database** — deliberately, to avoid a query per
  authorized request on a Basic DTU tier (`src/Infrastructure/Authorization/AuthorizationPolicies.cs:13-18,38,42`).
  This plan preserves that design; it shortens the staleness window rather than replacing the mechanism.
- **`ApproveAsync` bypasses `UserManager.UpdateAsync` on purpose**, so the status flip and the outbox
  rows land in one `SaveChangesAsync` — which is why it rotates `ConcurrencyStamp` by hand
  (`MemberAdminEndpoints.cs:89-99`). Block/unblock must follow the same shape or reintroduce the
  double-write race that rotation closes.
- **No backend test project exists** (AGENTS.md). Backend verification is `dotnet build` plus manual
  testing; frontend has Vitest and every existing feature ships a `.spec.ts`.

## Desired End State

An admin opens `/admin/members`, sees every member (never themselves), filters by pending / active /
blocked, types to narrow by name or email, and blocks or unblocks any row. A blocked member is
refused at login, and an already-signed-in member loses access within about two minutes rather than
half an hour. The existing `/admin/approvals` queue continues to work unchanged.

Verify by: signing in as admin, blocking a member who has a live session in another browser, and
confirming that within ~2 minutes their next request bounces to login; then unblocking them and
confirming access returns.

## What We're NOT Doing

- **Not answering PRD Open Question 1.** Blocked-member consequences for bookings and training plans
  now belong to S-04 and S-07, where those aggregates are defined. Nothing here cascades.
- **No schema change and no migration.** The flat `AccountStatus` enum is sufficient; no
  `BlockedAt`, no block reason, no prior-status column (PRD §Non-Goals rules out extra lifecycle
  state, and the frame confirmed nothing needs restoring on unblock).
- **No pagination.** A single gym's member list fits on one screen; the same reasoning
  `PendingMemberQuery` already records (`MemberAdminEndpoints.cs:49-52`).
- **No audit trail of who blocked whom, or when.** Not in the PRD.
- **No replacement of the claim-based authorization design** with per-request DB status checks.
- **Not touching `/admin/approvals`.** It stays exactly as shipped.
- **No member-facing "you are blocked" screen** beyond the login refusal that already exists.

## Implementation Approach

Three phases, splitting the backend along the read/write line so the security-shaped work is
isolated in one reviewable commit. Phase 1 adds a read surface with the admin structurally absent.
Phase 2 adds the two mutations and the session-revocation change. Phase 3 builds the screen that
consumes both.

## Critical Implementation Details

**Timing & lifecycle.** `UpdateSecurityStampAsync` and the `ApproveAsync`-style manual save are in
tension: `UserManager.UpdateSecurityStampAsync` performs its own save through `UserManager`, which
would split the block into two writes. Rotate the stamp by assigning `user.SecurityStamp` a new value
in the same tracked-entity block as the status flip, so one `SaveChangesAsync` carries status,
concurrency stamp and security stamp together — matching the reason `ApproveAsync` bypasses
`UserManager.UpdateAsync` in the first place (`MemberAdminEndpoints.cs:89-99`).

**State sequencing.** Shortening `ValidationInterval` makes Identity re-validate more often, which
re-mints claims from the database via `AppUserClaimsPrincipalFactory`. This means approval also
propagates in ~2 minutes without an explicit `/api/auth/refresh` call. That endpoint stays — the
approvals screen calls it and it is still the immediate path — but its 30-minute worst case becomes
a 2-minute one.

---

## Phase 1: Member read surface

### Overview

A query and endpoint returning every non-admin member with their status, optionally narrowed to one
status server-side.

### Changes Required:

#### 1. Member summary contract and query seam

**File**: `src/Application/Members/MemberAdminEndpoints.cs`

**Intent**: Add the DTO the member list renders and the read seam Infrastructure implements,
alongside the existing `PendingMember` / `IPendingMemberQuery` pair rather than replacing them —
the approvals queue keeps its own narrower contract.

**Contract**: A `MemberSummary` record carrying `Id`, `Email`, `DisplayName`, `Status`, `CreatedAt`.
`Status` crosses the wire as its enum *name* (`"Pending"` / `"Active"` / `"Blocked"`), not its int,
so the SPA's badge logic reads a stable string and does not depend on the numeric values
`AccountStatus` pins for persistence. A sibling `IMemberQuery` interface exposing
`GetMembersAsync(AccountStatus? status, CancellationToken)`.

#### 2. Query implementation with admin exclusion

**File**: `src/Infrastructure/Members/MemberQuery.cs` (new)

**Intent**: Implement `IMemberQuery` following `PendingMemberQuery`'s shape — `AsNoTracking`,
DB-side projection, no whole Identity rows — with two additions: an optional status filter and a
structural exclusion of anyone in the `Admin` role.

**Contract**: Excludes admins by anti-joining `UserRoles`/`Roles` against `ApplicationRoles.Admin`
rather than by user id, so the exclusion still holds if a second admin is ever seeded. Applies
`WHERE Status = @status` only when the parameter is non-null (the indexed path). Orders by
`DisplayName` — this is a browse surface, not the oldest-first work queue `/pending` serves.

#### 3. Endpoint registration

**File**: `src/Application/Members/MemberAdminEndpoints.cs`

**Intent**: Map the list endpoint into the existing group so it inherits the `Admin` policy, and
update the class doc comment, which currently states that S-02's surface does not exist and is
blocked on the bookings question.

**Contract**: `GET /api/admin/members` with an optional `status` query parameter bound as
`AccountStatus?`. An unparseable value must be refused as a 400 rather than silently treated as "no
filter", so a typo in the SPA surfaces instead of quietly returning everyone.

#### 4. DI registration

**File**: `src/Program.cs`

**Intent**: Register `IMemberQuery` → `MemberQuery` next to the existing `IPendingMemberQuery`
registration.

**Contract**: Scoped, matching the existing registration's lifetime (it depends on `AppDbContext`).

### Success Criteria:

#### Automated Verification:

- Backend builds warning-free: `dotnet build` from `src/`
- `GET /api/admin/members` returns 200 for an admin session
- `GET /api/admin/members` returns 401/403 for a member session
- `GET /api/admin/members?status=Blocked` returns 200 and only blocked rows
- `GET /api/admin/members?status=nonsense` returns 400

#### Manual Verification:

- The seeded admin account does not appear in the returned list
- Members created during S-01 testing appear with correct status values
- Response shape matches what Phase 3's models will mirror

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase.

---

## Phase 2: Block, unblock, and session revocation

### Overview

The two mutations, plus the security-stamp and validation-interval changes that make blocking
actually take effect on a live session.

### Changes Required:

#### 1. Block endpoint

**File**: `src/Application/Members/MemberAdminEndpoints.cs`

**Intent**: Flip a member to `Blocked` and invalidate their session, following `ApproveAsync`'s
transition pattern exactly — the idempotency check, the manual concurrency-stamp rotation, the single
save, and the lost-race fallback all carry over for the same reasons.

**Adapted during implementation.** The lost-race fallback did *not* carry over. `ApproveAsync`
returns `Ok()` when `TrySaveChangesAsync` reports a lost race, and its comment justifies that by the
winner having done the same work — they approved the member and enqueued the email, so reporting
success stays accurate. That reasoning does not transfer to block: the winner may have *approved*
this member instead, in which case returning `Ok()` tells the admin "blocked" about an account that
is now `Active`, and nothing else in the product would ever correct that belief. Both mutations
therefore return `409 BlockFailure("conflict")` / `UnblockFailure("conflict")` on a lost race, which
lands on the refetch path Phase 3's component contract already specifies for 409.

**Contract**: `POST /api/admin/members/{id}/block`. Returns 404 for an unknown id; 200 and no-op if
already `Blocked` (idempotent, matching approve); 409 `BlockFailure("is_admin")` if the target holds
the `Admin` role — the API-side half of the defence the query already provides. Blockable from both
`Active` and `Pending`. Rotates `ConcurrencyStamp` **and** `SecurityStamp` by direct assignment in
the same tracked block as the status flip, so one `SaveChangesAsync` carries all three; do not call
`UserManager.UpdateSecurityStampAsync`, which would save separately. No outbox notification — the
PRD requires no block email.

#### 2. Unblock endpoint

**File**: `src/Application/Members/MemberAdminEndpoints.cs`

**Intent**: Return a blocked member to `Active`. For an account blocked while still `Pending`, this
doubles as approval — a deliberate simplification that avoids a prior-status column.

**Contract**: `POST /api/admin/members/{id}/unblock`. 404 unknown; 200 no-op if already `Active`;
409 `UnblockFailure("not_blocked")` if the target is `Pending` (use `/approve` instead); 409
`UnblockFailure("conflict")` on a lost race, per the adaptation noted under the block endpoint. Same
concurrency-stamp rotation and single-save shape. Does **not** rotate `SecurityStamp` — there is no
stale permissive claim to invalidate, since the blocked member's cookie already fails the policy.
No approval email: `IAccountApprovedNotification` fires on approve, and re-notifying an unblocked
member would be a second "welcome" for an account that was already approved once.

#### 3. Update the deferred-obligation comments

**File**: `src/Application/Members/MemberAdminEndpoints.cs`

**Intent**: Three comments currently assert that block/unblock does not exist and is blocked on the
bookings question — the class doc comment, the `ApproveAsync` comment at lines 80-81, and the
`not_pending` rationale. Correct them, since leaving them would tell the next reader the opposite of
what shipped.

**Contract**: `ApproveAsync`'s 409 `not_pending` branch is now reachable only for a `Blocked` target,
and the comment should point at the `/unblock` endpoint as the correct action.

#### 4. Shorten the security-stamp validation interval

**File**: `src/Program.cs`

**Intent**: Drop `ValidationInterval` from 30 minutes to 2 so a rotated stamp is noticed promptly.
Update the surrounding comment, which currently frames the 30-minute value as a bound S-02 would
inherit.

**Contract**: `TimeSpan.FromMinutes(2)`. The comment should record that the cost is one cached
lookup per active user per interval, accepted as negligible for a single gym on Basic DTU, and that
this also shortens approval propagation.

### Success Criteria:

#### Automated Verification:

- Backend builds warning-free: `dotnet build` from `src/`
- `POST /api/admin/members/{id}/block` on an active member returns 200 and the row reads `Blocked`
- Blocking an already-blocked member returns 200 (idempotent)
- Blocking a pending member returns 200 and the row reads `Blocked`
- Blocking the admin's own id returns 409 `is_admin`
- `POST /api/admin/members/{id}/unblock` on a blocked member returns 200 and the row reads `Active`
- Unblocking a pending member returns 409 `not_blocked`
- Both endpoints return 404 for an unknown id and 401/403 for a non-admin session

#### Manual Verification:

- A blocked member is refused at login with the existing `blocked` failure
- A member signed in **in another browser** loses access within ~2 minutes of being blocked, without
  clearing cookies — the load-bearing check for this phase
- Unblocking restores access after the same interval
- `GET /health` still succeeds and no unusual DB load appears after the interval change

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase.

---

## Phase 3: Members screen

### Overview

The admin-facing list: status badges, a status filter, live text search, and per-row actions.

### Changes Required:

#### 1. DTOs

**File**: `src/app/src/app/core/admin/member-admin.models.ts`

**Intent**: Add the TypeScript mirror of `MemberSummary` and the two failure reasons, alongside the
existing `PendingMember` / `ApproveFailure`. Correct the `ApproveFailure` doc comment, which
currently says unblock is blocked on the bookings question.

**Contract**: `Member` with `status: 'Pending' | 'Active' | 'Blocked'` as a union of the enum names
the API emits. `BlockFailure { reason: 'is_admin' }`, `UnblockFailure { reason: 'not_blocked' }`.

#### 2. Service methods

**File**: `src/app/src/app/core/admin/member-admin.service.ts`

**Intent**: Add `getMembers(status?)`, `block(id)`, `unblock(id)` following the existing methods'
shape — relative `/api` paths, `firstValueFrom`, and deliberately no `catch` so failures reach the
screen. Update the class doc comment, which states this surface belongs to a future slice.

**Contract**: `getMembers` omits the `status` parameter entirely when no filter is active rather
than sending an empty value, which the endpoint would reject as unparseable.

#### 3. Members component

**File**: `src/app/src/app/features/admin/members/members.ts`, `members.html`, `members.scss` (new)

**Intent**: The screen. Mirror `approvals.ts`'s state model — `loading` / `loadFailed` signals, a
per-row busy `Set`, per-row failure display — extended with a status filter and a search box.

**Contract**: Status filter changes refetch from the API; the search term filters the loaded rows in
a computed signal, case-insensitively over display name and email. Rows are **not** removed
optimistically after block or unblock — unlike approve, the member still belongs on this list with a
new badge, so update the row's status in place. A 409 means the local row is stale: refetch rather
than removing it. Pending rows offer Approve (reusing the existing service method) as well as Block;
active rows offer Block; blocked rows offer Unblock. Polish copy throughout, matching `approvals.html`.

#### 4. Route

**File**: `src/app/src/app/app.routes.ts`

**Intent**: Register `/admin/members` behind the same guards as `/admin/approvals`.

**Contract**: `canActivate: [authGuard, adminGuard]`, path in English per S-01's D10.

#### 5. Specs

**File**: `src/app/src/app/features/admin/members/members.spec.ts`,
`src/app/src/app/core/admin/member-admin.service.spec.ts` (new + extend)

**Intent**: Cover the behaviour that is easy to get wrong, following `approvals.spec.ts`'s structure
and its `HttpClient` mocking approach.

**Contract**: Assert at minimum — search narrows without refetching; a status filter change does
refetch; block updates the row in place rather than removing it; a 409 triggers a refetch; a failed
block leaves the row's status unchanged and surfaces an error.

### Success Criteria:

#### Automated Verification:

- Unit tests pass: `npm test` from `src/app/`
- Lint and formatting pass: `npm run quality:check` from `src/app/`
- Production build succeeds: `npm run build` from `src/app/`

#### Manual Verification:

- The list shows every member with a correct status badge, and never the admin
- Each of the three filter positions returns the right subset
- Search narrows instantly with no visible request per keystroke
- Block and unblock update the row in place; the badge changes without a full reload
- A blocked member cannot log in; unblocking restores them
- `/admin/approvals` still works exactly as before
- The screen is usable at mobile width (PRD mobile-first NFR)

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful.

---

## Testing Strategy

### Unit Tests:

Frontend only — the backend has no test project (AGENTS.md), so backend correctness rests on
`dotnet build`, the endpoint checks above, and manual verification.

- Search filters loaded rows without issuing a request
- Status filter change issues a request with the right parameter
- Block/unblock update the row in place; the row is never removed
- 409 responses trigger a refetch rather than a silent removal
- A genuine failure leaves the row's status untouched and shows an error

### Integration Tests:

None automated. The load-bearing integration path — block → cookie invalidation → access refused —
is verified manually in Phase 2 because it depends on the security-stamp validation interval
elapsing in real time.

### Manual Testing Steps:

1. Sign in as admin, open `/admin/members`, confirm the admin's own account is absent.
2. Filter to each of pending / active / blocked; confirm subsets are correct.
3. Type a partial name and a partial email; confirm both narrow instantly.
4. Block an active member; confirm the badge changes in place.
5. In a second browser signed in as that member, confirm access is refused within ~2 minutes.
6. Confirm that member is refused at login with the existing blocked message.
7. Unblock; confirm the badge returns to active and access is restored.
8. Block a pending member, then unblock; confirm they become active.
9. Attempt `POST /api/admin/members/{admin-id}/block` directly; confirm 409 `is_admin`.
10. Open `/admin/approvals`; confirm it behaves exactly as before.

## Performance Considerations

The status filter runs against the existing `IX_AspNetUsers_Status` index. The admin-role exclusion
adds a join to `AspNetUserRoles`, which is small and keyed. Search runs client-side over a loaded
list, so it costs nothing server-side.

The `ValidationInterval` change is the only new recurring load: Identity re-validates each signed-in
user's security stamp every ~2 minutes instead of every 30, which is one indexed lookup per active
user per interval. For a single gym on Basic DTU this is negligible, but it is the one change here
that scales with concurrent users rather than with member count — worth re-checking if the user base
ever grows by an order of magnitude.

## Migration Notes

None. No schema change, no migration, no data backfill. The `Status` column, its default and its
index all shipped with F-02's `AddIdentitySchema` migration.

The `ValidationInterval` change takes effect on deploy with no migration step, and is reversible by
redeploying the previous artifact — unlike a schema change, it carries no rollback lag.

## References

- Frame brief: `context/changes/member-management/frame.md`
- Roadmap slice: `context/foundation/roadmap.md` §S-02
- PRD: `context/foundation/prd.md:78-81` (FR-004, FR-005), §Access Control, §Non-Goals
- Transition pattern to follow: `src/Application/Members/MemberAdminEndpoints.cs:59-123`
- Query pattern to follow: `src/Infrastructure/Members/PendingMemberQuery.cs:16-29`
- Screen pattern to follow: `src/app/src/app/features/admin/approvals/approvals.ts:1-99`
- Deferred obligation this plan discharges:
  `context/archive/2026-08-31-auth-identity-foundation/plan.md:134-139`
- Lessons: `context/foundation/lessons.md` — record any necessary deviation in this plan as part of
  the same phase, not only in a commit message or deploy log

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Member read surface

#### Automated

- [x] 1.1 Backend builds warning-free: `dotnet build` from `src/` — 4cbb846
- [x] 1.2 `GET /api/admin/members` returns 200 for an admin session — 4cbb846
- [x] 1.3 `GET /api/admin/members` returns 401/403 for a member session — 4cbb846
- [x] 1.4 `GET /api/admin/members?status=Blocked` returns 200 and only blocked rows — 4cbb846
- [x] 1.5 `GET /api/admin/members?status=nonsense` returns 400 — 4cbb846

#### Manual

- [x] 1.6 The seeded admin account does not appear in the returned list — 4cbb846
- [x] 1.7 Members created during S-01 testing appear with correct status values — 4cbb846
- [x] 1.8 Response shape matches what Phase 3's models will mirror — 4cbb846

### Phase 2: Block, unblock, and session revocation

#### Automated

- [x] 2.1 Backend builds warning-free: `dotnet build` from `src/`
- [x] 2.2 Block on an active member returns 200 and the row reads `Blocked`
- [x] 2.3 Blocking an already-blocked member returns 200 (idempotent)
- [x] 2.4 Blocking a pending member returns 200 and the row reads `Blocked`
- [x] 2.5 Blocking the admin's own id returns 409 `is_admin`
- [x] 2.6 Unblock on a blocked member returns 200 and the row reads `Active`
- [x] 2.7 Unblocking a pending member returns 409 `not_blocked`
- [x] 2.8 Both endpoints return 404 for an unknown id and 401/403 for a non-admin session

#### Manual

- [x] 2.9 A blocked member is refused at login with the existing `blocked` failure
- [x] 2.10 A member signed in in another browser loses access within ~2 minutes of being blocked
- [x] 2.11 Unblocking restores access after the same interval
- [x] 2.12 `GET /health` still succeeds and no unusual DB load appears after the interval change

### Phase 3: Members screen

#### Automated

- [ ] 3.1 Unit tests pass: `npm test` from `src/app/`
- [ ] 3.2 Lint and formatting pass: `npm run quality:check` from `src/app/`
- [ ] 3.3 Production build succeeds: `npm run build` from `src/app/`

#### Manual

- [ ] 3.4 The list shows every member with a correct status badge, and never the admin
- [ ] 3.5 Each of the three filter positions returns the right subset
- [ ] 3.6 Search narrows instantly with no visible request per keystroke
- [ ] 3.7 Block and unblock update the row in place; the badge changes without a full reload
- [ ] 3.8 A blocked member cannot log in; unblocking restores them
- [ ] 3.9 `/admin/approvals` still works exactly as before
- [ ] 3.10 The screen is usable at mobile width (PRD mobile-first NFR)
