# Membership Pass and Staff Booking Implementation Plan

## Overview

Introduce the *karnet* (`MembershipPass`) as a third access axis — a named pass with an inclusive
date range and a required entry count — and make it the thing that decides whether a person may be
booked into a class. In the same stream, move booking entirely onto staff routes (admin everywhere,
trainer for their own classes), and retire admin approval of new accounts.

The frame brief established that these are not one gate moving: the karnet gates *training* and
hangs off `Member`; approval gates *account creation* and hangs off `ApplicationUser`. Removing
approval is therefore planned as its own decision with its own deliverable, not as a consequence of
the karnet landing.

## Current State Analysis

**Two access axes exist and are documented as a closed set.** `Domain/Members/MembershipStatus.cs:14-28`
asserts that `AccountStatus` answers "may this LOGIN be used" and `MembershipStatus` answers "may
this PERSON use the club", and that neither implies the other.
`Infrastructure/Authorization/AuthorizationPolicies.cs:20-24` states every policy checks both. There
is no third axis anywhere: 18 distinct "may this person do X" decision points all reduce to those
two statuses, a role, or class capacity. Nothing models a time-bounded or counted entitlement to
train.

**The booking write path is a single-stamp retry loop.** `Application/Scheduling/BookingEndpoints.cs:253-341`
(`TryBookAsync`) re-reads the class, counts active bookings, inserts, rotates
`Class.ConcurrencyStamp` (L309) and commits through one `TrySaveAsync` (L311), up to
`MaxAttempts = 10` (L163), discarding the change tracker between attempts (L337). There is no
explicit transaction, deliberately: `EnableRetryOnFailure` is on (`Program.cs:48`), so
`BeginTransaction` would throw outside an execution strategy — `Application/Persistence/IUnitOfWork.cs:44-49`
records that decision. Two-stamp writes exist (`MemberAdminEndpoints.cs:384/400`, `554/569`,
`640/651`) but never inside a retry loop.

**Staff booking is already shipped.** `BookForMemberAsync` (`BookingEndpoints.cs:362-388`) and
`ReleaseAsync` (L516-562) exist under the `Admin` policy and already share `TryBookAsync`. The SPA
surface is `features/admin/classes/class-bookings-overlay.ts`, opened per class from the admin
calendar, picking a member from a plain `<select>` populated by `MemberAdminService.getMembers('Active')`.
What MP-02 actually adds is the trainer half.

**Approval carries load beyond approval.** `AuthEndpoints.cs:233-234` states verbatim that the
approval gate *is* the anti-spam mitigation for registration, and `RateLimitPolicies.cs:14-19`
confirms `/forgot-password` is the only rate-limited endpoint in the app. `/register` is
`AllowAnonymous` with no limiter (`AuthEndpoints.cs:139-140`). `AccountStatus.Pending = 0` is the
persisted int default (`Domain/AccountStatus.cs:12-22`), and approval is the only transition out of
it. `IAccountApprovedNotification` (`Application/Notifications/AccountApprovedNotification.cs:19-58`)
fires only on that transition, on both email and push.

**No resource-ownership check exists anywhere.** `TrainingPlanEndpoints.cs:154-158` says so
explicitly: "THERE IS NO OWNERSHIP RULE." The closest precedent is `MyPlanEndpoints.cs:54-102`,
which scopes queries *by* the caller's member id rather than comparing one. `Class.InstructorMemberId`
is a required, indexed FK (`ClassConfiguration.cs:74-84`) and is already projected onto the wire
(`ClassEndpoints.cs:41-50`).

**No `date` column and no `DateOnly` exists in the model.** Every temporal column is
`DateTimeOffset`. `Domain/Scheduling/ClubTime.cs` owns the single timezone reference
(`Europe/Warsaw`) and is used today only on write paths (`ClassEndpoints.cs:842`) and in
notification rendering — never on a read path.

**There is exactly one data-migration precedent.** `20260907200723_AddMembers.cs:101-120` uses
`migrationBuilder.Sql` with an idempotent `INSERT ... WHERE NOT EXISTS`, and documents the deploy
invariant it relies on: schema is always at or ahead of code, never behind.

## Desired End State

An admin opens a member's karnet screen, issues a pass — type name, validity range, entry count —
and books that member into a class from the admin calendar. A trainer does the same, but the booking
action is refused on any class they do not personally instruct. The member opens the app, sees a
karnet card on their dashboard showing type, validity and entries left, sees their upcoming classes,
and has no way to book or cancel anything. A booking is refused when the member holds no pass valid
on the class's club-local date, or holds one whose entries are all consumed. Registering produces an
account that is active immediately, is rate-limited per client IP, and there is no approvals tab and
no awaiting-approval screen.

Verify by: issuing a two-entry pass, booking the member into three classes inside the validity range
(third is refused with `no_entries_left`), booking into a class outside the range (refused with
`pass_not_valid`), releasing one booking and confirming the entry returns.

### Key Discoveries:

- `TryBookAsync` is the single shared write path for both surviving booking routes
  (`BookingEndpoints.cs:239-244` — "ONE LOOP, TWO CALLERS"). The pass gate belongs inside it, once.
- Cancelling never rotates `Class.ConcurrencyStamp` because it only frees spots
  (`Infrastructure/Scheduling/BookingStore.cs:46-72`). The same reasoning does **not** hold for a
  pass: releasing returns an entry, which is a state another booking races for.
- Four sites write `Booking` rows: `BookingEndpoints.cs:298,438,548` and `BookingStore.cs:69` (the
  block cascade). Only the insert consumes an entry; the three cancels return one implicitly, since
  the count derives from active rows.
- `ClassEndpoints.cs` `CancelAsync` (L719-793) deliberately leaves booking rows Active when a class
  is cancelled (L694-700). Entries therefore stay consumed for a cancelled class — see Migration
  Notes.
- Filtered unique indexes are this codebase's chosen concurrency mechanism where they fit
  (`TrainingPlanConfiguration.cs:67-71`, documented as safety rather than optimization). They do not
  fit an "at most N" pool, which is why the entry gate uses a stamp instead.
- The dashboard is deliberately eager and deliberately read-only (`features/dashboard/dashboard.ts:33-35`);
  the initial-bundle warning budget is 550 kB (`AGENTS.md`).

## What We're NOT Doing

- **Money.** A pass is issued, never sold. No price, no payment, no invoice — the M-4 carve-out
  stands.
- **Unlimited passes.** The entry count is always required (MP-04).
- **A stored entry counter.** Entries left are always derived from active bookings (MP-06).
- **Removing `AccountStatus.Pending` from the enum.** Its numeric values are pinned
  (`Domain/AccountStatus.cs:7-10`); the code stops producing it, but the value stays reserved.
  Retiring it is deferred past this stream.
- **A trainer-to-member relationship.** Trainer scoping is class-scoped only; `TrainingPlanEndpoints.cs:154-158`
  stays true for plans.
- **A member-facing pass purchase or renewal request.** Members read their pass; they never act on it.
- **Changing the block/unblock lever.** `MembershipStatus.Blocked` keeps its current meaning and
  cascade.
- **An in-app notification centre**, and any notification about passes at all in this stream.

## Implementation Approach

Additive first, subtractive last, destructive one release later — the S-14 shape
(`context/archive/2026-09-07-member-entity-and-accountless-members/plan.md`, 10 phases, drops
deferred). Every phase leaves the app deployable and working.

The karnet lands as a normal entity with a Store/Query pair behind an Application-level interface,
per the convention in §2 of the codebase survey. Its two invariants are enforced in different
places, because they are different shapes: **non-overlapping ranges** is an "at most one row per
date" rule checked in the handler and made atomic by rotating `Member.ConcurrencyStamp` — the same
protocol `Member` already uses for code-claiming and blocking (`Domain/Members/Member.cs:119-130`).
**The entry pool** is an "at most N rows" rule that no index can express, so it joins the existing
capacity check inside `TryBookAsync` and is made atomic by rotating a second stamp,
`MembershipPass.ConcurrencyStamp`, in the same `SaveChangesAsync`.

`Booking.MembershipPassId` records which pass paid for a booking. This is what keeps the derived
count stable: entries left is `COUNT` of active bookings carrying that pass id, so editing a pass's
validity range never silently reattributes history. It remains a derivation, not a counter.

## Critical Implementation Details

**Timing & lifecycle — Phase 6 has an ordering requirement that spans a deploy.** The data migration
that flips existing `Pending` accounts to `Active` must be committed and applied *before* the code
that stops producing `Pending` ships. The deploy workflow applies migrations before the new artifact
starts (`AddMembers.cs` doc comment records this invariant), so both may live in the same phase —
but the migration must be the first commit of that phase, and the rate limiter must land with or
before the registration change, never after.

**State sequencing — the second stamp must be rotated on release, not only on booking.** The
existing cancel paths skip rotating `Class.ConcurrencyStamp` because freeing a spot cannot overbook.
An entry returning to a pass is the opposite case: it makes a booking possible that was refused a
moment ago, so every path that cancels a booking carrying a `MembershipPassId` must rotate that
pass's stamp. That includes the block cascade in `BookingStore.CancelActiveFutureForMemberAsync`,
which today rotates nothing and runs outside a retry loop.

**Debug & observability — the entry count must be verified against the database, not the response.**
The concurrency tests in this repo assert both the HTTP outcome and an independent DB re-query
(`BookingEndpointTests.cs:539-540`). Entry-pool races must do the same, because a response computed
from a pre-save read is exactly what a lost race would still report correctly.

---

## Phase 1: The karnet entity and its schema

### Overview

Land `MembershipPass` as a persisted entity with its non-overlap invariant, with no consumer yet.
Nothing about booking changes in this phase.

### Changes Required:

#### 1. The entity

**File**: `src/Domain/Members/MembershipPass.cs`

**Intent**: A pass belonging to one member: a type name, an inclusive validity range, a required
entry count, and a concurrency stamp. Anemic like `Member` and `Class` — the invariants that must be
atomic live in the endpoints where they share a unit of work. Document why the entry count is not a
remaining-balance column: the balance is derived from bookings, and a stored counter would be a
second source of truth that drifts.

**Contract**: `Id: Guid`, `MemberId: Guid`, `Member: Member?` (read side only, same contract as every
other navigation here), `TypeName: string`, `ValidFrom: DateOnly`, `ValidTo: DateOnly` (inclusive
both ends), `EntryCount: int` (required, ≥ 1), `IssuedAt: DateTimeOffset`,
`ConcurrencyStamp: string`. No `Bookings` collection — a collection hanging off the aggregate is what
`Booking.cs:40-45` warns against.

#### 2. String and range bounds

**File**: `src/Application/Members/MembershipPassRules.cs`

**Intent**: One place naming the bounds that the entity configuration, the endpoint validation and
the Angular validators must all agree on, mirroring how `ContactDetails` and `MemberAccessCode`
already do it.

**Contract**: `TypeNameMaxLength`, `MinEntryCount`, `MaxEntryCount`, and a maximum validity span in
days. Referenced by `MembershipPassConfiguration` and the endpoint's validation, never re-typed as a
literal.

#### 3. Entity configuration

**File**: `src/Infrastructure/Persistence/Configurations/MembershipPassConfiguration.cs`

**Intent**: Auto-discovered configuration following `MemberConfiguration`. This is the first
`DateOnly` mapping in the model, so it is explicit rather than conventional.

**Contract**: `TypeName` bounded from `MembershipPassRules`; `ValidFrom`/`ValidTo` mapped with
`HasColumnType("date")`; `EntryCount` required; `ConcurrencyStamp` required, `HasMaxLength(36)`,
`IsConcurrencyToken()`; FK to `Member` with `OnDelete(DeleteBehavior.Restrict)` per the convention
that deleting a person must never silently take dependent rows. Index on `(MemberId, ValidFrom)`
named `IX_MembershipPasses_MemberId_ValidFrom` to support the overlap probe and the history
ordering. No unique index — non-overlap is a between-rows range rule no index can express.

#### 4. Migration

**File**: `src/Infrastructure/Persistence/Migrations/<timestamp>_AddMembershipPasses.cs`

**Intent**: Create the table and its index. Schema only — no data.

**Contract**: `dotnet ef migrations add AddMembershipPasses -p src/po-prostu-silka.csproj -o Infrastructure/Persistence/Migrations`.
`Down` drops the table and index and is genuinely reversible (nothing else references it yet).
Confirm the `date` column type appears as `date`, not `datetime2`, in the generated migration —
this is the one thing about this migration that is not routine.

#### 5. Store and overlap probe

**File**: `src/Application/Members/IMembershipPassStore.cs`

**Intent**: The write seam, so Application never references EF Core. Includes the overlap probe,
because the check and the insert must share one unit of work.

**Contract**: `AddAsync`, `FindAsync(passId)`, `FindOverlappingAsync(memberId, from, to, excludingPassId)`
returning the first colliding pass or null, `RemoveAsync`. Implementation in
`src/Infrastructure/Members/MembershipPassStore.cs`; registered scoped in `src/Program.cs` beside the
other member stores, with a comment naming S-16.

**Adapted during implementation.** The interface that shipped is wider than this, and Phase 3 could
not have worked with the one specified here. `Add`/`Remove` are SYNCHRONOUS — they only stage onto
the change tracker, so an async signature would have promised I/O that never happens, which is the
convention the other stores already follow. Two methods were added: `FindCoveringAsync`, a TRACKED
twin of `IMembershipPassQuery.FindCoveringAsync` (the booking gate has to rotate the resolved pass's
`ConcurrencyStamp`, and a no-tracking projection cannot be written back), and `FindManyAsync`, a
batch load for the block cascade, which releases several bookings at once and they may sit on
different passes. Both are consequences of Phase 3's stamp-rotation contract rather than new scope.

### Success Criteria:

#### Automated Verification:

- Solution builds warning-free under `<Nullable>enable</Nullable>`: `dotnet build` from `src/`
- Migration applies cleanly against a real engine: `dotnet ef database update -p src/po-prostu-silka.csproj --connection "<dev>"`
- Migration is reversible: `dotnet ef migrations script <previous> AddMembershipPasses` generates without error, and rolling back to `<previous>` succeeds
- Existing suite still green: `dotnet test` from the repo root

#### Manual Verification:

- The `ValidFrom`/`ValidTo` columns are SQL type `date` in the created table, confirmed by inspecting the schema in the local SQL Server container
- `GET /health` still opens a real DB connection after the migration

---

## Phase 2: Admin API for issuing and reading karnety

### Overview

The admin surface for the pass: issue, list a member's history with entries used, edit, revoke. The
entries-used figure is computed in the query, never stored.

### Changes Required:

#### 1. Endpoint group

**File**: `src/Application/Members/MembershipPassEndpoints.cs`

**Intent**: A new group under the `Admin` policy applied once at the group, following
`MemberAdminEndpoints`. Issuing validates the range, the entry count and non-overlap, then commits
the insert and the rotated `Member.ConcurrencyStamp` in one save. Revoking refuses when the pass has
active bookings against it — a pass that paid for a booking cannot vanish underneath it.

**Contract**: Routes `GET /api/admin/members/{memberId:guid}/passes`,
`POST /api/admin/members/{memberId:guid}/passes`,
`PUT /api/admin/members/{memberId:guid}/passes/{passId:guid}`,
`DELETE /api/admin/members/{memberId:guid}/passes/{passId:guid}`. Records `MembershipPassView`
(pass id, type name, validity range, entry count, entries used, entries left, issued at, and whether
it is the pass covering today) and `IssuePassRequest`. Failures as
`record MembershipPassFailure(string Reason)` with reasons enumerated in the doc comment:
`member_not_found`, `member_blocked`, `invalid_range`, `invalid_entry_count`, `overlapping_pass`,
`has_active_bookings`, `conflict`. 400 for malformed input, 409 for a conflict with existing state,
404 for an unaddressable member or pass — the split `MemberAdminEndpoints` already uses.

**Adapted during implementation.** There are EIGHT reasons, not seven: `invalid_type_name` was added
for a blank or over-length type name. The list above folded that case into `invalid_range`'s
neighbours by omission, but a bad name and a bad date range are different corrections at the desk,
and `MembershipPassRules.TypeNameMaxLength` exists precisely so the bound is nameable.

#### 2. Read projection

**File**: `src/Application/Members/MembershipPassEndpoints.cs` (interface),
`src/Infrastructure/Members/MembershipPassQuery.cs` (implementation)

**Intent**: Project a member's pass history in one no-tracking statement, with entries used as a
correlated subquery over active bookings — the same technique `BookingEndpoints` uses to avoid a
collection navigation on the aggregate.

**Contract**: `IMembershipPassQuery.GetForMemberAsync(memberId, ct)` returning
`IReadOnlyList<MembershipPassView>` ordered newest-first, and
`FindCoveringAsync(memberId, clubLocalDate, ct)` returning the single pass whose inclusive range
contains that date, or null. Registered scoped in `Program.cs`.

**Adapted during implementation.** This projection counts active bookings by `MembershipPassId`, so
it cannot compile until that column exists — and the plan placed the column in Phase 3. Phase 2
therefore also lands **Phase 3's Changes Required §1 and §2** (`Booking.MembershipPassId`, its FK,
the `(MembershipPassId, Status)` index and the `AddBookingMembershipPass` migration). The alternative
was a Phase 2 that reported a hardcoded `entriesUsed: 0` and rewrote the projection a phase later,
which would have shipped an API that knowingly lied. Phase 3 is unchanged apart from no longer
carrying those two items: it is now purely the gate and the stamp rotations.

#### 3. Blocked-member guard

**File**: `src/Application/Members/MembershipPassEndpoints.cs`

**Intent**: Issuing a pass to a blocked member is refused, matching how `IssueAccessCodeAsync`
already refuses (`MemberAdminEndpoints.cs:810-813`).

**Contract**: `member.Status != MembershipStatus.Active` → 409 `member_blocked`.

#### 4. Integration tests

**File**: `tests/po-prostu-silka.Tests/MembershipPassEndpointTests.cs`

**Intent**: Cover the invariants that are not obvious from the handler.

**Contract**: `[Collection(nameof(IntegrationCollection))]`, snake-case sentence names. Cases:
issuing returns every field; an overlapping range is 409; a range abutting exactly (previous
`ValidTo` + 1 day) is accepted; `ValidTo` before `ValidFrom` is 400; entry count of zero is 400;
issuing to a blocked member is 409; issuing to an accountless member succeeds (this is the karnet's
whole point per the frame); a non-admin is 403; concurrent issues of two overlapping passes leave
exactly one, via `Task.WhenAll` with two clients, asserted against a DB re-query.

### Success Criteria:

#### Automated Verification:

- Build is warning-free: `dotnet build` from `src/`
- New and existing tests pass: `dotnet test` from the repo root
- The concurrent-overlap test passes repeatedly (run the suite three times) — a race test that passes once proves nothing

#### Manual Verification:

- Issuing a pass through the API and re-reading the history shows `entriesUsed: 0` and `entriesLeft` equal to the issued count
- Issuing a second pass overlapping the first is refused with a message naming the colliding pass

---

## Phase 3: The gate in the booking write path

### Overview

The karnet starts deciding. `TryBookAsync` gains a pass lookup, an entry-pool check and a second
rotated stamp; every cancel path returns the entry by rotating that stamp too.

### Changes Required:

#### 1. Booking carries its pass — **LANDED IN PHASE 2**

**Adapted during implementation.** Moved forward to Phase 2, which cannot compile without the column;
see that phase's read-projection contract for why. The contract below is what shipped, unchanged.

**File**: `src/Domain/Scheduling/Booking.cs`, `src/Infrastructure/Persistence/Configurations/BookingConfiguration.cs`

**Intent**: Record which pass paid for this booking, so the derived entry count is stable against
later edits of a pass's validity range. Nullable, because bookings created before this migration
have no pass and because the column must be addable without a backfill.

**Contract**: `MembershipPassId: Guid?` on `Booking`; FK to `MembershipPass` with
`OnDelete(DeleteBehavior.Restrict)`; index on `(MembershipPassId, Status)` to serve the entries-used
subquery. Document on the property that null means "booked before the karnet existed" and that such
bookings consume no entry — this is a stated consequence, not a gap.

#### 2. Migration — **LANDED IN PHASE 2**

**Adapted during implementation.** Moved forward with §1 above, for the same reason.

**File**: `src/Infrastructure/Persistence/Migrations/<timestamp>_AddBookingMembershipPass.cs`

**Intent**: Add the nullable column, FK and index. Schema only; existing rows keep null.

**Contract**: `Down` drops the index, FK and column — genuinely reversible because the column is
nullable and nothing else depends on it.

#### 3. The gate inside the shared loop

**File**: `src/Application/Scheduling/BookingEndpoints.cs`

**Intent**: Inside `TryBookAsync`, after the existing class-state and duplicate checks and beside the
capacity count, resolve the pass covering the class's club-local date and refuse when there is none
or when its entries are exhausted. Rotate the pass's stamp alongside the class's, in the same
`TrySaveAsync`, so both invariants are guarded by one atomic write. Both refusals must happen before
the insert, and the pass lookup must be re-done on every attempt of the retry loop, not hoisted above
it — a hoisted read is exactly the stale guess the loop exists to prevent.

**Contract**: The class's date is `ClubTime.ToClubLocal(entity.StartsAt)` reduced to a `DateOnly` —
this is `ClubTime`'s second read-path consumer and the file's doc comment anticipates such a
narrowing exception, so state it there. Entries used is counted over active bookings carrying that
pass id. New `BookingFailure` reasons `no_valid_pass` and `no_entries_left`, both 409, both added to
the doc comment enumerating valid reasons. `Booking.MembershipPassId` is set from the resolved pass
on insert.

#### 4. Returning the entry on every cancel path

**File**: `src/Application/Scheduling/BookingEndpoints.cs`, `src/Infrastructure/Scheduling/BookingStore.cs`

**Intent**: A released entry makes a previously-refused booking possible, so unlike freeing a class
spot it is a state other writers race for. Every path that flips a booking to `Cancelled` must rotate
the stamp of the pass that booking carries.

**Contract**: `CancelMineAsync` (retired in Phase 7 but correct until then), `ReleaseAsync`, and
`BookingStore.CancelActiveFutureForMemberAsync` all rotate `MembershipPass.ConcurrencyStamp` for each
distinct pass touched, within their existing unit of work. The block cascade runs outside a retry
loop and reports a lost race as a single 409 — that stays true, and is acceptable for the same reason
it is acceptable today.

#### 5. Concurrency and behaviour tests

**Adapted during implementation.** The arrangement change the contract anticipated landed as two
fixture helpers (`IntegrationTestFixture.IssuePassAsync` / `IssuePassForAccountAsync`) called from
each suite's member-creating helper, rather than as a per-test edit. Four suites needed it —
`BookingEndpointTests`, `AdminBookingEndpointTests`, `ClassCancellationTests` and
`ClassEndpointTests` — and the fixture pass is deliberately century-wide, because those suites work
in fabricated years (2030/2032/2034/2036) that slide further out with every test in the file.

**File**: `tests/po-prostu-silka.Tests/BookingEndpointTests.cs`, `tests/po-prostu-silka.Tests/AdminBookingEndpointTests.cs`

**Intent**: Prove the second invariant holds under the race it was built for — one member, one entry
left, two different classes, two simultaneous staff bookings.

**Adapted during implementation.** `pass_not_valid` does not exist; both "no pass at all" and "a pass
that does not reach the class's date" answer `no_valid_pass`. The two are indistinguishable from the
lookup's side — `FindCoveringAsync` returns null for both — and the club's response to each is the
same (sell them a karnet), so splitting them would have meant a second query purely to phrase a
refusal. `no_entries_left` stays distinct, because *that* is a different conversation at the desk.

**Contract**: New cases: booking with no pass is 409 `no_valid_pass`; booking into a class one day
outside the range is 409 `no_valid_pass`; booking on the exact first and last day of the range
succeeds (inclusive both ends); exhausting the entries refuses the next with `no_entries_left`;
releasing a booking frees an entry and the next booking succeeds; a booking whose pass is null
(pre-migration row) is left alone by the entries count. The race:
`Concurrent_bookings_never_exceed_the_pass_entry_count` — N+1 parallel bookings of the same member
into N+1 distinct classes with an N-entry pass, asserting exactly N succeed, via `Task.WhenAll` and
an independent DB re-query, mirroring `BookingEndpointTests.cs:519-541`. Existing tests that book
without a pass must be updated to arrange one — expect most of both files to need an arrangement
change.

### Success Criteria:

#### Automated Verification:

- Build is warning-free: `dotnet build` from `src/`
- Full suite passes: `dotnet test` from the repo root
- The entry-pool race test passes on three consecutive full-suite runs
- Migration is reversible: rolling back `AddBookingMembershipPass` and re-applying succeeds

#### Manual Verification:

- With a two-entry pass, the third booking is refused and the refusal message names the reason
- Releasing one of the two bookings makes a third booking succeed
- A member whose pass expired yesterday cannot be booked into today's class

---

## Phase 4: Trainer-scoped staff booking

### Overview

The staff booking routes admit trainers, but a trainer may act only on classes they personally
instruct. Admin passes through unconditionally.

### Changes Required:

#### 1. Widen the group, narrow the handler

**File**: `src/Application/Scheduling/BookingEndpoints.cs`

**Intent**: Move the admin booking group from the `Admin` policy to `TrainerOrAdmin`, and add an
ownership guard inside each handler comparing the caller's member id to the loaded class's
instructor. This is the codebase's first resource-ownership check; write it as an inline comparison
in the handler body, matching how all authorization here is either a group policy or a hand-rolled
field check — not as an `IAuthorizationHandler`, which nothing in this repo uses.

**Contract**: `principal.GetMemberId()` (`Application/Members/ClaimsPrincipalExtensions.cs:36-41`)
compared against `Class.InstructorMemberId`, which is already loaded in the handler. A caller in the
`Admin` role skips the comparison. A trainer acting on someone else's class gets 403, not 404 — the
class's existence is not a secret, the action is. Applies to `GetForClassAsync`, `BookForMemberAsync`
and `ReleaseAsync`. Add a doc comment on the group stating that the group policy alone is no longer
sufficient here and that any endpoint added to this group must carry the same check — this is the
known cost of the inline approach and it must be written down where the next person will read it.

#### 2. Tests

**File**: `tests/po-prostu-silka.Tests/AdminBookingEndpointTests.cs`

**Intent**: Prove the scoping in both directions.

**Contract**: A trainer books into a class they instruct and succeeds; the same trainer is 403 on a
class instructed by someone else; an admin succeeds on both; a trainer releases a booking on their
own class and is 403 on another's; the bookings list is readable by the instructing trainer and 403
otherwise. `TestUsers.ActiveTrainerEmail` already holds both roles, which is the realistic case.

### Success Criteria:

#### Automated Verification:

- Build is warning-free: `dotnet build` from `src/`
- Full suite passes: `dotnet test` from the repo root
- Every handler in the widened group has a test asserting the 403 for a non-instructing trainer

#### Manual Verification:

- Signed in as a trainer, the booking action works on own classes and is refused elsewhere
- Signed in as an admin, nothing about the existing booking flow changed

---

## Phase 5: Karnet on screen

### Overview

The admin issues and reads passes on a dedicated screen; the member sees their pass on the
dashboard.

### Changes Required:

#### 1. Admin karnet screen

**File**: `src/app/src/app/features/admin/members/member-passes.ts` / `.html` / `.scss`

**Intent**: One member's pass history with entries used and left, plus a reactive form to issue a new
one. Standalone component with signal state and the `generation` fencing counter the other admin
screens use.

**Contract**: Route `admin/members/:id/passes`, lazy `loadComponent`, guards `authGuard, adminGuard`
— added to `app.routes.ts` beside the existing `admin/members/:id` entry. Reactive form via
`FormBuilder().nonNullable.group`, matching `member-form.ts:56-64`, with validators mirroring
`MembershipPassRules`. Entry point is a new item in the row action menu in `members.html`, placed
beside the access-code actions.

#### 2. Admin service and models

**File**: `src/app/src/app/core/admin/member-admin.service.ts`, `src/app/src/app/core/admin/member-admin.models.ts`

**Intent**: The client half of the pass API, following the existing method and doc conventions.

**Contract**: `getPasses(memberId)`, `issuePass(memberId, request)`, `updatePass(memberId, passId, request)`,
`revokePass(memberId, passId)`. Models `MembershipPassView` and `IssuePassRequest` carrying the
"Mirrors the API's X record — keep the two in step" doc comment every model file here uses. A
`membership-pass-failure.ts` mapping reasons to Polish messages as a full `Record`, not a `Partial` —
`booking-failure.ts`'s `ADMIN_MESSAGES` being partial is a known soft spot and the new file should
not repeat it.

#### 3. Member pass endpoint

**File**: `src/Application/Members/ProfileEndpoints.cs` or a sibling member-facing route

**Intent**: The member reads their own pass, scoped by the caller's member id so there is no id to
tamper with — the `MyPlanEndpoints` pattern rather than an ownership comparison.

**Contract**: `GET /api/passes/mine` under the `ActiveMember` policy, returning the pass covering
today plus entries left, or null when there is none. Never lists other members' passes and never
accepts a member id.

**Adapted during implementation.** It landed as its own file, `src/Application/Members/MyPassEndpoints.cs`,
rather than on `ProfileEndpoints`. Same reason `MyPlanEndpoints` is separate from
`TrainingPlanEndpoints`: this codebase applies one policy per group, and `ProfileEndpoints` is a bare
`RequireAuthorization()` group (an account with no contact details must reach it while still failing
`ActiveMember`), so hanging an `ActiveMember` route off it would have meant two policies in one group.
"No content" is 204 rather than null-in-a-200, mirroring `MyPlanEndpoints.GetMineAsync` — the SPA has
to tell "you hold none" from "the request failed", and it can only do that if the API does.

#### 4. Member dashboard card

**File**: `src/app/src/app/features/dashboard/dashboard.ts` / `.html`

**Intent**: A third card beside upcoming classes and the training plan, showing type, validity and
entries left, and stating plainly when there is no valid pass. Read-only, like everything else on
this screen.

**Contract**: Loaded in parallel with the existing two calls, following their loading/failure signal
shape. Keep the card's markup lean — this route is eager and the initial-bundle warning budget is
550 kB.

### Success Criteria:

#### Automated Verification:

- Frontend quality gate passes: `npm run quality:check` from `src/app/`
- Frontend unit tests pass: `npm test` from `src/app/`
- Backend suite passes: `dotnet test` from the repo root
- The production build stays under the 550 kB initial-bundle warning: `npm run build` from `src/app/` emits no budget warning

#### Manual Verification:

- An admin issues a pass from the new screen and sees it appear in the history with the right entries left
- The member's dashboard shows the pass, and shows a clear empty state when they have none
- After a booking is made for the member, the entries-left figure on both screens drops by one
- The screen renders correctly on a phone-width viewport

---

## Phase 6: Registration produces an active account

### Overview

The approval gate stops gating. The data migration lands first, the rate limiter lands with the
registration change, and the approval *surfaces* stay in place until Phase 8 so nothing is
half-removed at any point.

**The order inside this phase is load-bearing** — commit the migration before the code change.

### Changes Required:

#### 1. Data migration activating existing Pending accounts

**File**: `src/Infrastructure/Persistence/Migrations/<timestamp>_ActivatePendingAccounts.cs`

**Intent**: Flip every `Pending` account to `Active` so that no account is left able to log in but
unable to pass any policy once approval stops existing. This is the second data migration in the
project's history; follow the first one's shape exactly.

**Contract**: `migrationBuilder.Sql` with an idempotent `UPDATE [AspNetUsers] SET [Status] = 1 WHERE [Status] = 0`,
written as a triple-quoted raw string, mirroring `AddMembers.cs:101-120`. The `Down` is a documented
no-op with a comment explaining why: which accounts were previously pending is not recoverable, and
re-pending live accounts would lock out real members. Reference the deploy invariant — migrations
apply before the artifact ships, and the previous artifact tolerates Active accounts fine.

#### 2. Rate limiting on registration

**File**: `src/Application/Auth/RateLimitPolicies.cs`, `src/Program.cs`, `src/Application/Auth/AuthEndpoints.cs`

**Intent**: Replace the mitigation that approval was providing. Per-client-IP limiter on
`POST /api/auth/register`, following the `/forgot-password` policy already configured.

**Contract**: A new named policy beside `RateLimitPolicies.ForgotPassword`, registered in
`Program.cs` alongside it, applied to the register endpoint with `.RequireRateLimiting(...)`. Update
`RateLimitPolicies.cs:14-19`, which currently claims `/forgot-password` is the only rate-limited
endpoint in the app — that comment becomes false the moment this lands.

#### 3. Registration creates an active account

**File**: `src/Application/Auth/AuthEndpoints.cs`

**Intent**: New accounts are `Active` immediately. Correct the doc comments that justify the current
behaviour, rather than leaving them asserting something untrue — `lessons.md` names exactly this
failure mode.

**Contract**: `Status = AccountStatus.Active` at L323. Rewrite the comment at L233-234 (approval as
anti-spam mitigation) to name the rate limiter instead, and the comment at L227-232 (email
enumeration justified by the approval wait) to restate the disclosure trade on its own terms, since
its stated reason disappears. The `Pending` branch in `LoginAsync` (L206-213) becomes unreachable
but stays until Phase 8.

#### 4. Trainer-grant vetting

**File**: `src/Application/Members/MemberAdminEndpoints.cs`

**Intent**: The guard at L744 refuses `!= Active`, whose stated purpose (L146-149) is refusing
Pending. With Pending no longer produced, only Blocked remains refusable. Update the doc comment to
say what the guard now actually guarantees, so the next reader is not misled about a vetting
property that no longer exists.

**Contract**: Comment-only change; the check itself stays correct for Blocked.

#### 5. Tests

**File**: `tests/po-prostu-silka.Tests/RegisterEndpointTests.cs`, `tests/po-prostu-silka.Tests/AuthEndpointTests.cs`

**Intent**: Assert the new registration outcome and the limiter.

**Contract**: Registration yields an Active account that immediately passes an `ActiveMember`-gated
route; repeated registrations from one client are eventually refused by the limiter. Existing tests
asserting a Pending outcome are updated, not deleted — `TestUsers.PendingMemberEmail` stays seeded
until Phase 8 so the policy tests that use it keep working.

**Adapted during implementation.** The limiter forced a fixture change the plan did not anticipate:
every request in the suite arrives over one in-memory connection, so all tests shared a single
rate-limiter partition and the fourth registration *any* test performed answered 429 — surfacing as
28 unrelated failures that looked nothing like a rate limit. `IntegrationTestFixture.CreateClient`
now gives each client its own `X-Forwarded-For` from the RFC 5737 documentation range, which is the
header production sends anyway; `CreateClientFromAddress` is the overload a test uses when it wants
to share one address on purpose (the limiter test, and `PasswordEndpointTests`'
ephemeral-port regression test, which had to move onto it — appending a second header value would
have left it passing while proving nothing).

### Success Criteria:

#### Automated Verification:

- Build is warning-free: `dotnet build` from `src/`
- Full suite passes: `dotnet test` from the repo root
- The data migration is idempotent: applying it twice against a database with mixed statuses leaves the same result
- Migration script generates: `dotnet ef migrations script <previous> ActivatePendingAccounts`

#### Manual Verification:

- A freshly registered account reaches the dashboard without any approval step
- No account in the local database remains `Status = 0` after the migration
- Rapid repeated registration attempts from one client are refused rather than accepted

---

## Phase 7: Self-service booking is removed

### Overview

Members lose the ability to book and cancel. The schedule stays browsable and `my-classes` becomes a
read-only list.

### Changes Required:

#### 1. Member booking routes

**File**: `src/Application/Scheduling/BookingEndpoints.cs`

**Intent**: Remove `POST /api/classes/{classId}/bookings` and `DELETE /api/classes/{classId}/bookings/mine`
with their handlers. `GET /api/bookings/mine` stays — MP-01 keeps the read-only view.

**Contract**: The `classBookings` group (L172-175) goes; the `myBookings` group (L177-181) stays.
`BookAsync` and `CancelMineAsync` are deleted; `TryBookAsync` stays as the staff path's loop. Failure
reasons that only the member routes produced (`not_booked` on the member path) are removed from the
enumerating doc comment.

#### 2. SPA booking removal

**File**: `src/app/src/app/core/scheduling/booking.service.ts`, `src/app/src/app/features/schedule/schedule.ts` / `.html`, `src/app/src/app/features/schedule/class-details-overlay/class-details-overlay.ts` / `.html`, `src/app/src/app/features/my-classes/my-classes.ts` / `.html`, `src/app/src/app/core/scheduling/booking-failure.ts`

**Intent**: Remove `book()` and `cancel()` from the service, the book/cancel actions and their
outputs from the overlay, the `act()`/`book()`/`cancel()` and `bookedClassIds` bookkeeping from the
schedule screen, and the cancel button from `my-classes`. The overlay keeps its informational role;
the calendar keeps selection.

**Contract**: `class-details-overlay` loses its `book`/`cancelBooking` outputs and the `canBook`/
`canCancel` computeds; it keeps `row`, name, time, description, instructor and free spots. The
member-facing half of `booking-failure.ts` shrinks to the admin vocabulary. `bottom-nav` is unchanged
— both tabs still lead somewhere useful.

#### 3. Tests

**File**: `tests/po-prostu-silka.Tests/BookingEndpointTests.cs`, Angular specs for the touched components

**Intent**: The member routes are gone, so the tests asserting them go with them; the remaining
member-facing assertion is that the routes are absent.

**Adapted during implementation.** The removed routes answer **405, not 404** — `MapFallbackToFile`
claims every unmatched path for GET and HEAD only, so a POST or DELETE to a path nothing else maps
matches the fallback's pattern but not its method. The test asserts 405 and says why; what it is
really pinning is that the answer is neither a 200 nor a 409, either of which would mean a handler
ran. Also: the suites' `BookAsync` helpers were *repointed* at the staff route rather than deleted —
almost every assertion in `BookingEndpointTests` is about the write path's behaviour (capacity,
duplicates, both races), which did not move, and rewriting those tests around a new arrangement is
where the coverage would have quietly thinned.

**Contract**: Tests for `BookAsync`/`CancelMineAsync` are removed; a test asserts both routes now
return 404. The concurrency tests that used the member route move to the admin route, which already
has an equivalent race test to model on. Do not delete race coverage in the move — the admin path
carries the same guarantee.

### Success Criteria:

#### Automated Verification:

- Build is warning-free: `dotnet build` from `src/`
- Full suite passes: `dotnet test` from the repo root
- Frontend quality gate passes: `npm run quality:check` from `src/app/`
- Frontend unit tests pass: `npm test` from `src/app/`
- No reference to the removed routes remains: a grep for the member booking paths across `src/` and `src/app/src/` returns nothing

#### Manual Verification:

- A member browsing the schedule can open a class and see its details, with no booking control anywhere
- `my-classes` lists upcoming bookings with no cancel control
- An admin booking that member in still causes the class to appear on their list

---

## Phase 8: Approval surfaces are removed, docs are reconciled

### Overview

The last subtractive step. Everything approval-shaped goes, and the documents that still describe the
old model are corrected.

### Changes Required:

#### 1. Backend approval surface

**File**: `src/Application/Members/MemberAdminEndpoints.cs`, `src/Infrastructure/Members/PendingMemberQuery.cs`, `src/Application/Notifications/AccountApprovedNotification.cs`, `src/Program.cs`, `src/Infrastructure/Members/MemberQuery.cs`

**Intent**: Remove the approval queue route and handler, the `PendingMember`/`ApproveFailure`
records, `IPendingMemberQuery` and its implementation, the approved-notification class and its DI
registration, and the `Pending` branch of the admin member-list filter.

**Contract**: `GET /api/admin/members/pending` and `POST /api/admin/members/{id}/approve` are gone.
`MemberListFilter` loses its `Pending` member; the `Active` branch's definition is simplified now
that no third state is produced. `UnblockAsync`'s comment at L613, which reasons from the approve
notification's existence, is rewritten.

**Adapted during implementation.** `MemberListFilter` did NOT lose its `Pending` member; it was
retired in place instead — `[Obsolete]`, a RETIRED doc comment, and the numeric value reserved. The
plan's reasoning for deleting it was that, unlike `AccountStatus.Pending`, it is not a persisted
column. True, but it is a *query-string* value: an admin with a bookmarked or stored filter URL still
sends `0`, and re-using that position for something else would silently repoint them at the wrong
list. Nothing produces or sends it, and the SPA correctly dropped `'Pending'` from its own
`MemberFilter` union, so the retired position is backend-only and inert.

#### 2. Login and enum

**File**: `src/Application/Auth/AuthEndpoints.cs`, `src/Domain/AccountStatus.cs`

**Intent**: The `Pending` branch in `LoginAsync` is unreachable; remove the branch but keep the enum
member. Its numeric values are pinned and historical rows may still be read by a rolled-back
artifact — this is the destructive step the rollback rule says must lag.

**Contract**: `AccountStatus.Pending` stays declared, with a doc comment marking it retired, when it
stopped being produced, and that removing it is a separate change once no deployed artifact reads it.

#### 3. SPA approval surface

**File**: `src/app/src/app/features/admin/approvals/*`, `src/app/src/app/features/auth/pending/*`, `src/app/src/app/app.routes.ts`, `src/app/src/app/core/auth/active-member.guard.ts`, `src/app/src/app/core/admin/member-admin.service.ts`, `src/app/src/app/core/admin/member-admin.models.ts`, `src/app/src/app/features/more/more.html`, `src/app/src/app/features/dashboard/dashboard.ts` / `.html`, `src/app/src/app/features/admin/members/members.ts` / `.html`

**Intent**: Delete both feature folders and their routes, remove the Zgłoszenia nav entry, remove the
pending-approvals widget from the admin half of the dashboard, remove the approve action and Pending
filter chip from the member list, and remove the guard branch that redirected to `/pending`.

**Contract**: `activeMemberGuard` keeps its `isActive()` check — membership blocking still needs it —
but the `/pending` redirect target is replaced by the same treatment a blocked member gets. The
`AccountStatus` type in `auth.models.ts` keeps `'Pending'` in the union while the server enum keeps
the value, so the two stay mirrored. `more.ts`'s doc comment about nav conditions matching guard
conditions stays true after the edit.

#### 4. Tests and fixtures

**File**: `tests/po-prostu-silka.Tests/IntegrationTestFixture.cs` and every suite referencing Pending

**Intent**: Remove `TestUsers.PendingMemberEmail` and the tests asserting pending-specific behaviour;
keep the blocked-account coverage, which is unaffected.

**Adapted during implementation.** Removing `TestUsers.PendingMemberEmail` left a hole the plan did
not anticipate: several suites used it to obtain **a signed-in session that fails `ActiveMember`**,
and the obvious substitute does not work — login refuses a blocked account outright, so there is no
cookie to test with. That state still matters (a session issued before a block must keep being
refused), so the fixture gained `CreateInactiveSessionAsync`: sign in an active account, block its
MEMBERSHIP directly in the database, refresh the claims. `AuthEndpointTests`' claim-staleness test
was rewritten around a block for the same reason, and is stronger for it — the stale claim there is
*permissive*, which is the direction that actually costs something.

**Contract**: `AccountApprovedNotificationTests.cs` is deleted. Pending cases in
`AuthEndpointTests`, `MemberAdminEndpointTests`, `MemberEndpointTests`, `MyPlanEndpointTests`,
`PushEndpointTests`, `TrainingPlanEndpointTests` are removed; their `[Theory]` variants parameterized
on `[Pending, Blocked]` keep the Blocked case.

#### 5. Document reconciliation

**File**: `context/foundation/roadmap.md`, `context/foundation/prd.md`, `AGENTS.md`

**Intent**: Three documents still describe the retired model. Leaving them is the failure mode
`lessons.md` already records — a document asserting something untrue costs every future reader.

**Contract**: `roadmap.md:69-72` (AM-005, "the account is still created `pending`") is corrected and
annotated as superseded by MP-03. `prd.md:140,153` and US-01's self-booking scenario get a note
marking them superseded by M-4, without rewriting the PRD's history. `AGENTS.md` gains a line stating
that booking is a staff action and that the karnet, not an approval flag, gates training.

### Success Criteria:

#### Automated Verification:

- Build is warning-free: `dotnet build` from `src/`
- Full suite passes: `dotnet test` from the repo root
- Frontend quality gate passes: `npm run quality:check` from `src/app/`
- Frontend unit tests pass: `npm test` from `src/app/`
- No dead references remain: a grep for `approv`, `Pending`, `/pending` and `admin/approvals` across `src/` and `src/app/src/` returns only the retired enum member and its doc comment
- The production build stays under the 550 kB initial-bundle warning: `npm run build` from `src/app/`

#### Manual Verification:

- Registering, logging in and reaching the dashboard involves no approval step and no waiting screen
- The admin panel has no Zgłoszenia entry and the member list has no Pending filter or approve action
- An admin blocking a member still signs them out and releases their future bookings
- The end-to-end north star holds: a member with a valid karnet is booked in by staff, and one whose karnet ran out is refused

---

## Testing Strategy

### Unit Tests:

This codebase has no unit-test layer on the backend by design — `AGENTS.md` records that the suite is
integration-first, booting the real app against a real SQL Server via Testcontainers, precisely so
that engine-dependent behaviour (filtered indexes, locking) is genuinely exercised. Frontend unit
tests run under Vitest and cover component logic for the new karnet screens.

### Integration Tests:

- The pass invariants: overlap refusal, inclusive range boundaries, entry exhaustion, entry return on
  release, refusal for a member with no pass
- The two races, each asserted against an independent DB re-query rather than the HTTP response:
  concurrent overlapping pass issues, and concurrent bookings against a single-entry pass across two
  distinct classes
- Trainer scoping in both directions on all three staff booking routes
- Registration producing an active account, and the rate limiter refusing a burst
- The absence of the removed member booking routes and approval routes

### Manual Testing Steps:

1. `docker compose up -d`, confirm `GET /health` opens a real connection
2. As admin, create a member with no account, issue them a two-entry pass valid this week
3. Book that member into two classes this week; confirm the third booking is refused with an entry
   message, and a booking next month is refused with a validity message
4. Release one booking; confirm the entry returns and a third booking now succeeds
5. As a trainer, book a member into a class you instruct; confirm the same action is refused on a
   class you do not
6. As that member, confirm the dashboard shows the pass with the right entries left, `my-classes`
   lists the bookings, and there is no booking or cancel control anywhere
7. Register a brand-new account; confirm it reaches the dashboard immediately and sees an empty-pass
   state
8. Block the member; confirm future bookings are released and the entries return

## Performance Considerations

The pass lookup and the entries-used count add two reads inside a loop that already performs three,
on a 5-DTU Basic tier. Both are indexed single-member lookups, and neither can be hoisted out of the
loop without reintroducing the stale-read hazard the loop exists to prevent — so the cost is accepted
deliberately rather than optimized away. The `(MembershipPassId, Status)` index exists specifically
to keep the entries count a seek.

The dashboard gains a third parallel request on an eager route. It is a single-row lookup scoped by
the caller's member id, and it runs alongside the two calls already there rather than after them.

Claim-based authorization is untouched: the pass gate is a live database read inside the write path,
never a claim. This is the right side of the trade — `AuthorizationPolicies.cs:15-18` avoids queries
for per-request policy checks, and a per-class-per-date entitlement could never ride a per-session
claim correctly anyway.

## Migration Notes

Three migrations land across the stream, in this order: `AddMembershipPasses` (Phase 1, schema),
`AddBookingMembershipPass` (Phase 2, schema — moved forward from Phase 3; see that phase's adaptation
note), `ActivatePendingAccounts` (Phase 6, data). All are
reversible except the data migration, whose `Down` is a documented no-op — which accounts were
pending is not recoverable, and re-pending live accounts would lock out real members.

Existing bookings carry a null `MembershipPassId` and consume no entry. This is deliberate: back-
filling them would require inventing a pass that was never issued. The practical consequence is that
a member's first pass is not retroactively debited for classes they attended before the karnet
existed.

A cancelled *class* leaves its booking rows Active (`ClassEndpoints.cs:694-700`), so those entries
stay consumed. That is the existing behaviour of the booking model rather than something this stream
introduces, and changing it would be a separate decision about what a cancelled class owes its
attendees.

Per the rollback rule, the retired `AccountStatus.Pending` enum member stays declared. Removing it is
a later change, once no deployed artifact reads it.

## References

- Frame brief: `context/changes/membership-pass-and-staff-booking/frame.md`
- Roadmap slice: `context/foundation/roadmap.md` — S-16, milestone M-4, anchors MP-01–MP-07
- Prior art for a multi-move slice with deferred drops: `context/archive/2026-09-07-member-entity-and-accountless-members/plan.md`
- The one data-migration precedent: `src/Infrastructure/Persistence/Migrations/20260907200723_AddMembers.cs:101-120`
- The booking write path: `src/Application/Scheduling/BookingEndpoints.cs:239-341`
- The two-axis contract: `src/Domain/Members/MembershipStatus.cs:14-28`, `src/Infrastructure/Authorization/AuthorizationPolicies.cs:20-24`
- Recurring rules: `context/foundation/lessons.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: The karnet entity and its schema

#### Automated

- [x] 1.1 Solution builds warning-free — 49f6e23
- [x] 1.2 Migration applies cleanly against a real engine — 49f6e23
- [x] 1.3 Migration is reversible — 49f6e23
- [x] 1.4 Existing suite still green — 49f6e23

#### Manual

- [x] 1.5 ValidFrom/ValidTo are SQL type `date` — 49f6e23
- [x] 1.6 GET /health still opens a real DB connection — 49f6e23

### Phase 2: Admin API for issuing and reading karnety

#### Automated

- [x] 2.1 Build is warning-free — bbcb731
- [x] 2.2 New and existing tests pass — bbcb731
- [x] 2.3 Concurrent-overlap test passes on three consecutive runs — bbcb731
- [x] 2.6 AddBookingMembershipPass rolls back and re-applies (moved from 3.4 — see the adaptation note) — bbcb731

#### Manual

- [x] 2.4 Issued pass reads back with entriesUsed 0 and correct entriesLeft — bbcb731
- [x] 2.5 Overlapping pass is refused with a message naming the collision — bbcb731

### Phase 3: The gate in the booking write path

#### Automated

- [x] 3.1 Build is warning-free — af4470b
- [x] 3.2 Full suite passes — af4470b
- [x] 3.3 Entry-pool race test passes on three consecutive full-suite runs — af4470b

#### Manual

- [x] 3.5 Third booking against a two-entry pass is refused with a clear reason — af4470b
- [x] 3.6 Releasing a booking makes a further booking succeed — af4470b
- [x] 3.7 An expired pass cannot cover today's class — af4470b

### Phase 4: Trainer-scoped staff booking

#### Automated

- [x] 4.1 Build is warning-free — 35d335f
- [x] 4.2 Full suite passes — 35d335f
- [x] 4.3 Every widened handler has a non-instructing-trainer 403 test — 35d335f

#### Manual

- [x] 4.4 Trainer books own classes, is refused elsewhere — 35d335f
- [x] 4.5 Admin booking flow is unchanged — 35d335f

### Phase 5: Karnet on screen

#### Automated

- [x] 5.1 Frontend quality gate passes — 2969ae2
- [x] 5.2 Frontend unit tests pass — 2969ae2
- [x] 5.3 Backend suite passes — 2969ae2
- [x] 5.4 Production build emits no bundle-budget warning — 2969ae2

#### Manual

- [x] 5.5 Admin issues a pass and sees correct entries left — 2969ae2
- [x] 5.6 Member dashboard shows the pass and a clear empty state — 2969ae2
- [x] 5.7 Entries left drop by one after a booking — 2969ae2
- [x] 5.8 Screens render correctly at phone width — 2969ae2

### Phase 6: Registration produces an active account

#### Automated

- [x] 6.1 Build is warning-free — 17d9be6
- [x] 6.2 Full suite passes — 17d9be6
- [x] 6.3 Data migration is idempotent when applied twice — 17d9be6
- [x] 6.4 Migration script generates without error — 17d9be6

#### Manual

- [x] 6.5 New registration reaches the dashboard with no approval step — 17d9be6
- [x] 6.6 No account remains Status = 0 after the migration — 17d9be6
- [x] 6.7 Burst registration from one client is refused — 17d9be6

### Phase 7: Self-service booking is removed

#### Automated

- [x] 7.1 Build is warning-free — d561a82
- [x] 7.2 Full suite passes — d561a82
- [x] 7.3 Frontend quality gate passes — d561a82
- [x] 7.4 Frontend unit tests pass — d561a82
- [x] 7.5 Grep finds no reference to the removed member booking routes — d561a82

#### Manual

- [x] 7.6 Schedule shows class details with no booking control — d561a82
- [x] 7.7 my-classes lists bookings with no cancel control — d561a82
- [x] 7.8 A staff booking still surfaces on the member's list — d561a82

### Phase 8: Approval surfaces are removed, docs are reconciled

#### Automated

- [x] 8.1 Build is warning-free — 07828d7
- [x] 8.2 Full suite passes — 07828d7
- [x] 8.3 Frontend quality gate passes — 07828d7
- [x] 8.4 Frontend unit tests pass — 07828d7
- [x] 8.5 Grep finds no dead approval references beyond the retired enum member — 07828d7
- [x] 8.6 Production build emits no bundle-budget warning — 07828d7

#### Manual

- [x] 8.7 Register to dashboard involves no approval step or waiting screen — 07828d7
- [x] 8.8 No Zgłoszenia entry, no Pending filter, no approve action — 07828d7
- [x] 8.9 Blocking still signs out and releases future bookings — 07828d7
- [x] 8.10 North star holds: valid karnet is booked in, exhausted karnet is refused — 07828d7
