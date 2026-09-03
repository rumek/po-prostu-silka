# Class Booking and Cancellation Implementation Plan

## Overview

A member books a spot in a class, cancels it, and sees their upcoming classes; an admin sees who
signed up and can release a spot. The class never accepts more bookings than it has spots, even
when two members tap Book at the same instant.

This is roadmap slice **S-08** (`context/foundation/roadmap.md`), delivering `prd.md` US-01,
FR-008, FR-009, FR-010, FR-014, and `prd-v2.md` FR-014. It is the slice the previous nine left
hooks for, and the roadmap calls it "the load-bearing correctness work of the milestone".

## Current State Analysis

- **`Booking` does not exist in any form.** No entity, no table, no migration, no client code.
  `src/Domain/Scheduling/` holds only `Class.cs`, `ClassStatus.cs`, `ClassType.cs`, `ClubTime.cs`;
  `AppDbContext` has four `DbSet`s and none of them is bookings. Verified by directory enumeration.
- **`FreeSpots` is a placeholder in exactly two places**, both carrying a comment naming this slice
  as the replacement: `ClassScheduleQuery.cs:73-77` and `ClassEndpoints.cs:729-731`. The wire
  contract (`ScheduledClass.FreeSpots`, `class.models.ts:41-46`) was designed so this slice changes
  the value and not the shape.
- **`Class` carries no concurrency token**, deliberately. `Class.cs:17-19` says a booking counter
  "would pre-commit S-08's concurrency design — the load-bearing correctness decision of the
  milestone — and that is not this slice's to make", and
  `occurrences-from-class-types/plan-brief.md` §Scope lists "a concurrency token on `Class`" as
  explicitly out of scope for S-06. The decision was reserved for this plan.
- **Every existing uniqueness/conflict check in the repo is a knowingly accepted read-then-write
  race**, justified by "exactly one admin account is ever seeded" (`ClassStore.cs:50-58`,
  `class-type-definitions/plan.md:173-174`, `ClassEndpoints.cs:536-539`). That justification does
  not transfer to member-initiated bookings.
- **`TrySaveChangesAsync` catches only `DbUpdateConcurrencyException`** (`UnitOfWork.cs:16-30`).
  A unique-index violation is a `DbUpdateException` and would surface as an unhandled 500 — the
  finding three separate reviews raised and deferred
  (`registration-and-approval/reviews/impl-review.md:178-194` F9,
  `class-type-definitions/reviews/impl-review.md:100-136` F3).
- **`EnableRetryOnFailure` is on** (`Program.cs:44`), so a bare `BeginTransaction()` throws at
  runtime. No code in the repo uses `CreateExecutionStrategy` yet.
- **`DELETE /api/admin/classes/{id}` is unguarded** and always succeeds; `ClassEndpoints.cs:518-520`
  says this slice adds the has-bookings guard.
- **Blocking a member touches nothing but their status and stamps** (`MemberAdminEndpoints.cs:227-272`).
  The PRD's open question about a blocked member's bookings was formally reassigned here
  (`MemberAdminEndpoints.cs:76-80`, `member-management/frame.md:86-87`).
- **`readOnly` on the shared calendar gates four things at once** — drag, resize, draw, and the
  `classActions` projection (`schedule-calendar.ts:220,415,471`, `schedule-calendar.html:121,159`).
  The member schedule leaves it at its `true` default and wires no click handler at all.
- **The member schedule is unreachable.** `/schedule` has no nav entry (`app.html:1-27` links only
  the admin's "Zgłoszenia") and `home.html` says "Grafik zajęć i zapisy pojawią się tutaj wkrótce."
  Nothing in the app links a member to the schedule today.
- **Concurrency testing has a precedent**: `MemberAdminEndpointTests.cs:160`
  (`Concurrent_approves_still_queue_exactly_one_email`) races two requests with a separate
  `HttpClient` each, which is what makes separate DI scopes and separate `DbContext`s.

## Desired End State

A member opens the schedule from the top nav, taps a class, and a detail overlay shows its name,
description, instructor, time and remaining spots with a single Book button. Booking succeeds or is
refused with a reason in plain Polish; the tile's spot count updates without a refetch. The member
reaches "Moje zajęcia" from the same nav and sees every upcoming booking in one chronological list,
each with a Cancel action. A cancelled booking disappears from that list and leaves a cancelled row
behind; the member may book the same class again.

An admin opens a class from the calendar and sees who signed up, with an action to release any
spot. Deleting a class that has bookings is refused. Reducing a class's capacity below the number of
people already signed up is refused. Blocking a member silently cancels their future bookings.

Under two members racing for one remaining spot, exactly one wins and the other is told the class is
full — never both.

**Verify by**: `dotnet test` from the repo root (including the new concurrency tests),
`npm test` and `npm run quality:check` from `src/app/`, plus the manual walkthrough in
§Testing Strategy against `docker compose up -d`.

### Key Discoveries:

- `ClassScheduleQuery.cs:47-59` — the anonymous projection runs entirely in SQL; a booking count
  must join in *there*, not after `ToListAsync`, or the "one round trip" property is lost.
- `ClassScheduleQuery.cs:40-43` — related rows are reached by referencing navigations *inside*
  `Select`, never `.Include(...)`. The new count must follow the same discipline.
- `MemberAdminEndpoints.cs:173-203` — the exact idiom for an atomic read-check-write: check, mutate,
  rotate the concurrency token, one `SaveChangesAsync`, treat a lost race as a first-class outcome.
- `ClassTypeConfiguration.cs:36-39` — `.IsUnique().HasFilter("[IsActive] = 1")`, the repo's proof
  that a filtered unique index is the real backstop and the store check is a courtesy.
- `features/admin/classes/class-create-overlay.*` — an existing overlay-over-the-calendar component
  with its own styles; the member's class-detail overlay is an adaptation of it, not an invention.
- `classes.html:63-98` — the panel-below-the-calendar pattern (subject line, actions, busy-text
  swap, Anuluj) that the admin bookings list copies.
- `classes.ts:54,57,276-303` — `busy: ReadonlySet<string>` + `failedId` + `isBusy(id)`, the per-row
  state idiom to reuse verbatim.
- `app.routes.ts:30-34` — `/schedule` and `/admin/classes` are lazy specifically to keep
  `angular-calendar` out of the initial bundle (500 kB budget, ~424 kB used). A plain list screen
  must not import the calendar.

## What We're NOT Doing

- **No notifications of any kind.** Booking confirmations, cancellation emails, and the class-cancelled
  delivery all belong to S-09. The block cascade in Phase 2 is deliberately silent.
- **No `ClassStatus.Cancelled` transition.** There is still no cancel endpoint for a class; DELETE
  remains the mistake-eraser it is today, now guarded. S-09 owns the transition.
- **No attendance or check-in tracking** — an explicit PRD Non-Goal. The admin's list says who signed
  up, never who showed up.
- **No waitlist for a full class** — an explicit PRD Non-Goal.
- **No cancellation deadline.** `prd.md` §Non-Goals locks free-cancel-anytime; a member may cancel a
  booking at any time, including after the class has started.
- **No booking from the admin panel.** An admin books as a member, from the member schedule.
- **No dashboard work.** `Home` keeps its placeholder; the nearest-classes card is S-12.
- **No change to the shared `ScheduledClass` projection shape.** No `bookedByMe` field is added; the
  member screen resolves its own bookings from `GET /api/bookings/mine`. Splitting the member and
  admin projections was weighed and declined in S-06 and stays declined.
- **No `Class.Bookings` collection navigation.** See §Critical Implementation Details.
- **No unblock restoration.** Unblocking returns the account to Active and leaves the cancelled
  bookings cancelled.

## Implementation Approach

Four phases: two server-side, then two screens. The server phases are ordered so the application is
coherent after each — Phase 1 adds a booking API whose effects nothing else reads yet, Phase 2 makes
every existing surface tell the truth about bookings. The client phases are independent of each
other and each delivers one reachable screen.

The correctness strategy is a **concurrency token on `Class`**, rotated by every booking write.
Because every booking and cancellation for a class updates that one row, all writes against a class
serialize against each other: the capacity race and the double-booking race are closed by the same
mechanism, with no explicit transaction and therefore no `CreateExecutionStrategy`. A single
`SaveChangesAsync` is already atomic; what was missing was a way to make the *check* atomic with the
write, and a rotated token is exactly that. A bounded retry loop turns a lost race into a re-read
rather than a spurious refusal.

## Critical Implementation Details

**The token is what serializes bookings, and it must be rotated on every booking write.** A booking
inserts a row in `Bookings` and touches nothing on `Classes` by itself, so without an explicit
rotation of `Class.ConcurrencyStamp` EF issues no UPDATE against `Classes`, no `WHERE` predicate
carries the token, and two concurrent bookings both commit. Rotating the stamp is not bookkeeping;
it is the entire mechanism. The same applies to cancellation, which must also rotate — a cancel and
a book racing for the same last spot must not both believe they won.

**Retry must discard tracked state between attempts.** After a failed save the `DbContext` still
tracks the rejected `Booking` insert and a `Class` whose stamp is stale, so a naive second attempt
re-sends the same doomed write. The loop needs an explicit discard between attempts, which is why
Phase 1 adds `IUnitOfWork.DiscardChanges()` rather than re-resolving a scope by hand.

**No `Class.Bookings` collection navigation is introduced.** `Class.ClassType` and
`Class.Instructor` are marked READ SIDE ONLY (`Class.cs:74-123`) and held to it by convention plus
tests alone. A collection of bookings hanging off the aggregate is a standing invitation for a write
path to count through it — the precise failure `ClassEndpointTests` exists to catch for capacity.
The read projection therefore uses a correlated subquery over `db.Bookings`, which produces the same
single SQL statement without adding the hazard.

**`FreeSpots` is not clamped at zero.** With the `capacity_below_bookings` guard in place a negative
value is unreachable, and clamping would convert a broken invariant into a silently plausible
number. An integration test pins that the only path that could produce it is refused.

**Filtered indexes constrain the writing session's SET options.** SQL Server refuses DML against a
table with a filtered index unless the session has the required `SET` options — the repo already met
this with `IX_ClassTypes_Name_Active` (`class-type-definitions/plan.md`, "Discovered during Phase 1").
EF Core's own connections set them correctly; hand-run SQL in a raw session may not.

**Blocking reaches into the scheduling context on purpose.** `MemberAdminEndpoints.BlockAsync`
gaining a dependency on `IBookingStore` is a deliberate, product-driven exception to the repo's
recorded convention that "access consequences are enforced at read time by policy claims, never by
stored cascade state" (`member-management/frame.md:70-78`). It is safe with respect to capacity
because cancelling only ever *frees* spots: a concurrent booker reading a pre-cancellation count is
conservative, never permissive, so the cascade does not need the class token.

---

## Phase 1: Model, schema and the booking write path

### Overview

The `Booking` aggregate, the concurrency token that guards capacity, the migration, and the three
member endpoints. Nothing else in the application reads bookings yet, so the schedule still reports
`FreeSpots = Capacity` at the end of this phase — that is Phase 2's single expression.

### Changes Required:

#### 1. The Booking entity and its status

**File**: `src/Domain/Scheduling/Booking.cs` (new), `src/Domain/Scheduling/BookingStatus.cs` (new)

**Intent**: Model one member's claim on one class occurrence, keeping cancelled claims in history as
`prd.md` FR-009 requires. Follow `Class.cs`'s anemic style — public get/set, no factory, invariants
enforced at the endpoint — so the scheduling context stays internally consistent.

**Contract**: `Booking` carries `Guid Id`, `Guid ClassId` with a `Class` navigation, `string
MemberUserId` with an `ApplicationUser Member` navigation, `BookingStatus Status`,
`DateTimeOffset CreatedAt`, and `DateTimeOffset? CancelledAt`. Both navigations carry the same
READ SIDE ONLY doc comment `Class.cs:74-93` uses. `BookingStatus` is `Active = 0`, `Cancelled = 1`
with the int values pinned in a comment, mirroring `ClassStatus.cs:18-25`.

#### 2. The concurrency token on Class

**File**: `src/Domain/Scheduling/Class.cs`

**Intent**: Give `Class` the token that makes a capacity check atomic with the booking write. Document
it as the mechanism of the no-overbooking guarantee, replacing the `Class.cs:17-19` note that
reserved this decision for this slice.

**Contract**: A `string ConcurrencyStamp` property initialised to a new GUID string, configured as a
concurrency token in Phase 1 §4. Its doc comment states that every write which changes how many spots
are taken must rotate it, and that failing to rotate it silently disables the guarantee.

#### 3. Entity configuration

**File**: `src/Infrastructure/Persistence/Configurations/BookingConfiguration.cs` (new)

**Intent**: Map the table, and establish the database-level backstop for "one active booking per
member per class" the same way `ClassTypeConfiguration` does for type names.

**Contract**: Table `Bookings`, PK `Id`. `MemberUserId` required with `HasMaxLength(450)` to match
`AspNetUsers.Id`. `Status` stored via `HasConversion<int>()` with a default of `Active`. Both FKs use
`OnDelete(DeleteBehavior.Restrict)`, per `ClassConfiguration.cs:58-76`. Two indexes:

- `IX_Bookings_Class_Member_Active` — unique on `(ClassId, MemberUserId)` with
  `HasFilter("[Status] = 0")`, so a cancelled booking does not hold the pair hostage and the member
  can book again.
- `IX_Bookings_Member_Status` — on `(MemberUserId, Status)`, backing `GET /api/bookings/mine`.

#### 4. DbContext and Class configuration

**File**: `src/Infrastructure/Persistence/AppDbContext.cs`,
`src/Infrastructure/Persistence/Configurations/ClassConfiguration.cs`

**Intent**: Register the new set and mark the token so EF puts it in the `WHERE` clause of every
`Classes` UPDATE.

**Contract**: `DbSet<Booking> Bookings => Set<Booking>();` alongside `Classes`/`ClassTypes`.
`ConcurrencyStamp` configured `.IsRequired().HasMaxLength(36).IsConcurrencyToken()`.

#### 5. Migration

**File**: `src/Infrastructure/Persistence/Migrations/<timestamp>_AddBookings.cs` (generated)

**Intent**: Create `Bookings` and add `Classes.ConcurrencyStamp` in one reversible step.

**Contract**: Generated with `dotnet ef migrations add AddBookings` from `src/`. The column is added
NOT NULL with `defaultValueSql` producing a fresh GUID per existing row, so the dev rows already in
`Classes` get distinct tokens rather than sharing one. `Down` drops the table and the column — no
data-loss exception is taken here, unlike `AddClassTypes`. The file header follows the XML-doc
`<summary>`/`<para>` style of `DropDeadClassColumns.cs`, and states that the filtered unique index
is the backstop rather than the primary mechanism.

#### 6. The unit-of-work seam for unique violations

**File**: `src/Application/Persistence/IUnitOfWork.cs`,
`src/Infrastructure/Persistence/UnitOfWork.cs`

**Intent**: Close the gap three reviews raised and deferred: `TrySaveChangesAsync` reports a lost
optimistic race but lets a unique-index violation escape as an unhandled 500. Add a seam that
distinguishes the two without putting EF Core types in `Application`. Also add the change-discard
the retry loop needs.

**Contract**: A `SaveOutcome` enum in `Application/Persistence` with `Saved`, `ConcurrencyConflict`
and `UniqueViolation`; `Task<SaveOutcome> TrySaveAsync(CancellationToken)`; and
`void DiscardChanges()`. The implementation maps `DbUpdateConcurrencyException` to
`ConcurrencyConflict` and a `DbUpdateException` whose innermost `SqlException` number is 2601 or 2627
to `UniqueViolation`; every other exception still propagates. The existing `SaveChangesAsync` and
`TrySaveChangesAsync` are left untouched so no current caller changes behaviour.

#### 7. Booking store and query seams

**File**: `src/Application/Scheduling/BookingEndpoints.cs` (new — interfaces at the bottom, per the
repo's convention), `src/Infrastructure/Scheduling/BookingStore.cs` (new),
`src/Infrastructure/Scheduling/BookingQuery.cs` (new)

**Intent**: One `IXStore`/`IXQuery` pair for the new aggregate, matching `ClassStore`/`ClassScheduleQuery`
exactly: the store holds intention-revealing writes plus the read helpers the write logic needs, the
query holds `AsNoTracking` projections for display, and neither ever saves.

**Contract**: `IBookingStore` exposes `Add(Booking)`, a tracked
`FindActiveAsync(Guid classId, string memberUserId, CancellationToken)`, a tracked
`FindByIdAsync(Guid bookingId, CancellationToken)`, `CountActiveAsync(Guid classId, CancellationToken)`,
and `CancelActiveFutureForMemberAsync(string memberUserId, DateTimeOffset asOf, CancellationToken)`
which marks matching tracked rows cancelled without saving (used by Phase 2's block cascade).
`IBookingQuery` exposes `GetUpcomingForMemberAsync(string memberUserId, DateTimeOffset from, CancellationToken)`
returning `MyBooking[]`, and `GetForClassAsync(Guid classId, CancellationToken)` returning
`ClassBooking[]`. Both implementations take `AppDbContext` through a primary constructor and are
registered `AddScoped` next to `IClassStore` in `Program.cs:182-183`.

#### 8. Member booking endpoints

**File**: `src/Application/Scheduling/BookingEndpoints.cs`

**Intent**: The three member-facing operations, each refusing with a named reason rather than an
exception, and each returning enough for the client to update in place without a refetch.

**Contract**: `MapBookingEndpoints` registers two groups, both under
`AuthorizationPolicyNames.ActiveMember`, and both applied at the group per `ClassEndpoints.cs:124-126`:

- `POST /api/classes/{classId:guid}/bookings` → `200 ScheduledClass` (the class as it now stands,
  with the caller's booking counted)
- `DELETE /api/classes/{classId:guid}/bookings/mine` → `200 ScheduledClass`
- `GET /api/bookings/mine` → `200 MyBooking[]`, chronological, upcoming only

`record BookingFailure(string Reason)` with reasons `class_cancelled`, `class_started`,
`already_booked`, `class_full`, `not_booked`, `conflict`. All are 409 — every one is a conflict with
existing state rather than malformed input. A missing class is `404`.

`record MyBooking(Guid BookingId, Guid ClassId, string Name, string? Description,
DateTimeOffset StartsAt, int DurationMinutes, string Instructor, DateTimeOffset BookedAt)`.

The caller's id comes from `userManager.GetUserId(principal)` with `ClaimsPrincipal principal` bound
as a parameter, per `PushEndpoints.cs:50`.

#### 9. The no-overbooking write path

**File**: `src/Application/Scheduling/BookingEndpoints.cs`

**Intent**: Make the capacity check atomic with the insert, and turn a lost race into a re-read
rather than a false refusal.

**Contract**: `BookAsync` runs a bounded loop of at most **3 attempts**. Each attempt loads the class
tracked through `IClassStore.FindAsync`, refuses a cancelled class (`class_cancelled`) or one whose
`StartsAt` is at or before `timeProvider.GetUtcNow()` (`class_started`), refuses when
`FindActiveAsync` returns a row (`already_booked`), refuses when `CountActiveAsync` has reached
`Capacity` (`class_full`), then adds the booking, **rotates `class.ConcurrencyStamp`**, and calls
`TrySaveAsync`. `Saved` returns the DTO; `ConcurrencyConflict` and `UniqueViolation` both call
`DiscardChanges()` and retry; exhausting the attempts returns `conflict`.

`CancelMineAsync` follows the same shape: find the caller's active booking (`not_booked` when there
is none), set `Status = Cancelled` and `CancelledAt`, rotate the class stamp, `TrySaveAsync`, retry
on conflict. It applies **no time rule** — free cancel anytime is locked by the PRD.

#### 10. Wiring

**File**: `src/Program.cs`

**Intent**: Register the two new seams and map the endpoint groups.

**Contract**: `AddScoped<IBookingStore, BookingStore>()` and `AddScoped<IBookingQuery, BookingQuery>()`
beside the existing scheduling registrations at `Program.cs:182-183`, with the same shared-DbContext
comment; `app.MapBookingEndpoints();` appended after `app.MapClassTypeEndpoints();` at
`Program.cs:240`.

#### 11. Integration tests

**File**: `tests/po-prostu-silka.Tests/BookingEndpointTests.cs` (new)

**Intent**: Pin the guarantee and every refusal. The concurrency test is the reason this slice exists
and must be written so that it fails without the token rotation.

**Contract**: Tests covering — a booking reduces nothing until Phase 2 but creates one Active row;
double booking the same class is `already_booked`; booking a full class is `class_full`; booking a
started class is `class_started`; cancelling then re-booking succeeds and leaves two rows, one
Cancelled and one Active; cancelling without a booking is `not_booked`; a pending and a blocked
member are both refused by the policy with 403. The headline test races **N+1 members against a class
of capacity N** using one `HttpClient` per member and `Task.WhenAll`, per
`MemberAdminEndpointTests.cs:160`, and asserts exactly N succeed, the rest receive `class_full`, and
the database holds exactly N Active rows.

### Success Criteria:

#### Automated Verification:

- Solution builds warning-free: `dotnet build` from `src/`
- Migration applies against a clean database and reverses: `dotnet ef database update`, then back to
  `DropDeadClassColumns`, then forward again
- All tests pass including `BookingEndpointTests`: `dotnet test` from the repo root
- The concurrency test fails when the `ConcurrencyStamp` rotation is commented out — verified once,
  by hand, before the phase commit
- No EF Core reference in `Domain` or `Application`: grep for `Microsoft.EntityFrameworkCore` in
  those folders returns nothing

#### Manual Verification:

- `GET /health` reports a healthy database connection after the migration
- Booking the same class twice from one account is refused with `already_booked`
- The member schedule still renders unchanged (free spots still equal capacity at this point)

**Implementation Note**: After completing this phase and all automated verification passes, pause here
for manual confirmation from the human that the manual testing was successful before proceeding to the
next phase.

---

## Phase 2: Bookings become visible to the rest of the system

### Overview

Everything that must now tell the truth because bookings exist: the two free-spot expressions, the
two admin guards, the block cascade, and the admin's read and release endpoints.

### Changes Required:

#### 1. Real free spots on the read path

**File**: `src/Infrastructure/Scheduling/ClassScheduleQuery.cs`

**Intent**: Replace the placeholder with a real count, in SQL, without adding a collection navigation
and without a second round trip.

**Contract**: The anonymous projection at `ClassScheduleQuery.cs:47-59` gains a `BookedCount` computed
as a correlated subquery over `db.Bookings` filtered to this class and `BookingStatus.Active`; the
`ScheduledClass` construction at line 77 becomes `r.Capacity - r.BookedCount`. Both query methods
share `ProjectAsync`, so this is one edit for the member and admin paths alike. The comment naming
S-08 is replaced by one explaining why a subquery is used instead of a navigation.

#### 2. Real free spots on the write path's DTO

**File**: `src/Application/Scheduling/ClassEndpoints.cs`

**Intent**: The second placeholder. `ToDto` has no query of its own, so the count is supplied by the
caller.

**Contract**: `ToDto` takes an additional `int bookedCount` and returns `entity.Capacity - bookedCount`
as `FreeSpots`. `CreateAsync` passes `0` (a class created this instant has no bookings);
`GetByIdAsync` and `UpdateAsync` pass `IBookingStore.CountActiveAsync`. Phase 1's booking endpoints
use the same helper so the returned `ScheduledClass` is consistent with the schedule.

#### 3. Delete guard

**File**: `src/Application/Scheduling/ClassEndpoints.cs`

**Intent**: Honour the instruction left at `ClassEndpoints.cs:518-520`. DELETE is for a class created
by mistake; once someone has signed up, taking it off the schedule is S-09's cancellation, not a
delete.

**Contract**: `DeleteAsync` refuses with `409 has_bookings` when `CountActiveAsync` is greater than
zero, before removing anything. `has_bookings` joins the `ClassFailure` reason union.

#### 4. Capacity guard

**File**: `src/Application/Scheduling/ClassEndpoints.cs`

**Intent**: Keep the headline guarantee true across edits, not just across bookings. Lowering capacity
below the number of people already signed up would break it from the other side.

**Contract**: `UpdateAsync` refuses with `409 capacity_below_bookings` when the requested capacity is
less than `CountActiveAsync` for that class. `capacity_below_bookings` joins the `ClassFailure` reason
union.

#### 5. Block cascade

**File**: `src/Application/Members/MemberAdminEndpoints.cs`

**Intent**: Free the spots a blocked member is holding, so the schedule stops promising seats to
someone who cannot attend. Deliberate, product-chosen exception to the repo's no-cascade convention —
recorded as such.

**Contract**: `BlockAsync` calls `IBookingStore.CancelActiveFutureForMemberAsync(user.Id, now, ct)`
after flipping the status and before saving, so the status flip and the cancellations land in the one
`SaveChangesAsync` the handler already performs. Only bookings whose class starts after `now` are
touched; past bookings stay Active in history. **No notification is enqueued** — consistent with the
existing decision that blocking sends no mail (`MemberAdminEndpoints.cs:224-225`). The class stamp is
not rotated: cancelling only frees spots, so a concurrent booker reading a pre-cascade count is
conservative. `UnblockAsync` is unchanged and restores nothing.

#### 6. Admin booking endpoints

**File**: `src/Application/Scheduling/BookingEndpoints.cs`

**Intent**: Let the admin see who signed up (`prd.md` FR-014) and release a spot — the latter chosen
deliberately beyond FR-014's "view", because it is what makes the capacity guard and the no-show case
workable, and the server-side cancel path already exists for the cascade.

**Contract**: A third group under `AuthorizationPolicyNames.Admin`:

- `GET /api/admin/classes/{classId:guid}/bookings` → `200 ClassBooking[]`, ordered by `BookedAt`,
  Active only
- `DELETE /api/admin/classes/{classId:guid}/bookings/{bookingId:guid}` → `204`, `404` when the booking
  does not exist or belongs to another class

`record ClassBooking(Guid BookingId, string MemberUserId, string DisplayName, string Email,
DateTimeOffset BookedAt)`. The admin cancel rotates the class stamp and retries exactly like the
member's cancel, so an admin releasing a spot and a member claiming it cannot both win.

#### 7. Tests

**File**: `tests/po-prostu-silka.Tests/BookingEndpointTests.cs`,
`tests/po-prostu-silka.Tests/ClassEndpointTests.cs`,
`tests/po-prostu-silka.Tests/MemberAdminEndpointTests.cs`

**Intent**: Pin each new refusal and the cascade, and pin that free spots now move.

**Contract**: Free spots fall by one after a booking and return after a cancellation, on both the
member and admin read paths; `DELETE` of a class with an active booking is `has_bookings` and the
class survives; the same class with only a cancelled booking deletes successfully; an update lowering
capacity below the booked count is `capacity_below_bookings`; blocking a member cancels their future
bookings and leaves past ones Active; a class's free spots recover after that cascade.

### Success Criteria:

#### Automated Verification:

- Solution builds warning-free: `dotnet build` from `src/`
- All tests pass: `dotnet test` from the repo root
- No new N+1: the member schedule for a week issues one SQL statement, verified by EF Core query
  logging at `Information` level during a single manual request

#### Manual Verification:

- Booking a class drops its spot count on the schedule; cancelling restores it
- Deleting a class with a booking is refused; the class is still on the calendar afterwards
- Lowering a booked class's capacity below the signed-up count is refused on the capacity field
- Blocking a member frees their future spots and leaves their past bookings alone

**Implementation Note**: After completing this phase and all automated verification passes, pause here
for manual confirmation from the human that the manual testing was successful before proceeding to the
next phase.

---

## Phase 3: The member's booking screens

### Overview

The two screens a member actually uses: a class-detail overlay with the Book and Cancel actions, and
a chronological "Moje zajęcia" list. Plus the navigation without which neither is reachable.

### Changes Required:

#### 1. Booking models and failure messages

**File**: `src/app/src/app/core/scheduling/booking.models.ts` (new),
`src/app/src/app/core/scheduling/booking-failure.ts` (new)

**Intent**: Mirror the new wire contracts field for field, and give every refusal one Polish sentence
in one place — the shared-table discipline `class-failure.ts` documents, applied before the second
consumer exists rather than after.

**Contract**: `MyBooking` mirroring the server record; `BookingFailure` as a union of the six reason
strings. `bookingFailureMessage(reason: unknown): string` built on a
`Record<BookingFailure['reason'], string>` so an unmapped reason breaks the build, with an `UNKNOWN`
fallback and the `Object.hasOwn` guard `class-failure.ts` uses.

#### 2. Booking service

**File**: `src/app/src/app/core/scheduling/booking.service.ts` (new)

**Intent**: A stateless HTTP wrapper matching `class.service.ts` — promises, `firstValueFrom`, no
internal catch.

**Contract**: `book(classId): Promise<ScheduledClass>`, `cancel(classId): Promise<ScheduledClass>`,
`getMine(): Promise<MyBooking[]>`.

#### 3. Tile selection on the shared calendar

**File**: `src/app/src/app/shared/calendar/schedule-calendar.ts`,
`src/app/src/app/shared/calendar/schedule-calendar.html`

**Intent**: Let the member open a class without changing anything about the admin's calendar. The
existing `readOnly` input conflates gestures with actions and is left exactly as it is; selection is a
separate concept and gets a separate name.

**Contract**: A new `selectable = input(false)` and a new `classSelected = output<ScheduledClass>()`.
The tile becomes an activatable control only when `selectable()` is true, emitting the row on
activation; keyboard activation must work, not only pointer. `readOnly` continues to gate drag,
resize, draw and the `classActions` projection, unchanged — a test pins that a `readOnly` calendar
still refuses all four.

#### 4. Class detail overlay

**File**: `src/app/src/app/features/schedule/class-details-overlay/` (new: `.ts`, `.html`, `.scss`,
`.spec.ts`)

**Intent**: The member's booking surface, and the first place a class type's description is shown to
anyone — it has had nowhere to live since S-05 defined it.

**Contract**: Adapted from `features/admin/classes/class-create-overlay` (same overlay-over-calendar
structure and styling approach). Inputs: the selected `ScheduledClass`, whether the caller has an
active booking on it, and a busy flag. Outputs: book, cancel, close. It shows name, description,
instructor, start time, duration, and `freeSpots / capacity`; it offers Book when not booked and spots
remain, Cancel when booked, and an explanatory line when the class is full or already started. A
refusal renders inline through `bookingFailureMessage`. Closing on Escape and returning focus to the
tile are required.

#### 5. Schedule screen wiring

**File**: `src/app/src/app/features/schedule/schedule.ts`,
`src/app/src/app/features/schedule/schedule.html`,
`src/app/src/app/features/schedule/schedule.spec.ts`

**Intent**: Hold the member's own bookings, open the overlay, and apply a booking result in place so
the tile count moves without a refetch.

**Contract**: Binds `[selectable]="true"` and `(classSelected)`. Loads `getMine()` once alongside the
first schedule load and keeps a `Set<string>` of booked class ids. Book and cancel replace the matching
row in `rows` with the `ScheduledClass` the server returned and update the set, guarded by the existing
`generation` fence so a week navigation mid-flight cannot resurrect a stale row. Failures are mapped
through `bookingFailureMessage` and shown in the overlay, not as a screen-level banner.

#### 6. Moje zajęcia

**File**: `src/app/src/app/features/my-classes/` (new: `.ts`, `.html`, `.scss`, `.spec.ts`)

**Intent**: `prd.md` FR-010 — the full chronological list, deliberately not a calendar.

**Contract**: A standalone signal component following `schedule.ts`'s shell shape: `rows`, `loading`,
`loadFailed`, a `generation` fence, and the loading/error/empty/content tri-state of `members.html`.
Each row is a `.card` naming the class, its date and time, and its instructor, with a Cancel action
using the `busy`/`failedId`/`isBusy(id)` idiom from `classes.ts:54,57,276-303`. Cancelling removes the
row in place. **It must not import the calendar** — the 500 kB bundle budget is why `/schedule` is
lazy.

#### 7. Route and navigation

**File**: `src/app/src/app/app.routes.ts`, `src/app/src/app/app.html`

**Intent**: Make both member screens reachable. Today neither is: `/schedule` has no link anywhere and
`Home` still says the schedule will appear "wkrótce".

**Contract**: A lazy `my-classes` route with `[authGuard, activeMemberGuard]`, placed beside
`schedule`. Two nav links in `app.html` — "Grafik" to `/schedule` and "Moje zajęcia" to `/my-classes` —
gated on `auth.isActive()`, following the `routerLinkActive="is-active"` pattern of the existing admin
link. `Home`'s placeholder sentence is updated so it no longer promises something that has arrived.

### Success Criteria:

#### Automated Verification:

- Frontend unit tests pass: `npm test` from `src/app/`
- Lint and format clean: `npm run quality:check` from `src/app/`
- Frontend builds within budget: `npm run build` from `src/app/` with no budget warning
- Backend tests still pass: `dotnet test` from the repo root

#### Manual Verification:

- A member reaches the schedule and "Moje zajęcia" from the top nav
- Tapping a class opens the overlay showing its description; booking updates the tile count without a
  reload
- Booking a full class shows the refusal in the overlay in Polish
- A booked class offers Cancel; cancelling restores the spot and removes it from "Moje zajęcia"
- The overlay closes on Escape and focus returns to the tile
- Both screens are comfortable on a phone
- The admin calendar is unchanged: drag, resize and draw all still work, and a past week still hides
  its actions

**Implementation Note**: After completing this phase and all automated verification passes, pause here
for manual confirmation from the human that the manual testing was successful before proceeding to the
next phase.

---

## Phase 4: The admin's booking list

### Overview

Who signed up, and the ability to release a spot — as a panel below the calendar, the pattern the
duplicate and delete flows already use on that screen.

### Changes Required:

#### 1. Admin booking service methods

**File**: `src/app/src/app/core/scheduling/booking.service.ts`,
`src/app/src/app/core/scheduling/booking.models.ts`

**Intent**: Extend the existing service rather than add a second one.

**Contract**: `getForClass(classId): Promise<ClassBooking[]>` and
`cancelAsAdmin(classId, bookingId): Promise<void>`, plus the `ClassBooking` interface mirroring the
server record.

#### 2. Bookings panel

**File**: `src/app/src/app/features/admin/classes/classes.ts`,
`src/app/src/app/features/admin/classes/classes.html`,
`src/app/src/app/features/admin/classes/classes.scss`,
`src/app/src/app/features/admin/classes/classes.spec.ts`

**Intent**: Add "Zapisani" to the tile actions and a panel below the calendar listing them, matching
the duplicate and delete panels exactly so the screen keeps one shape.

**Contract**: A fourth `.link-button` in the `#classActions` template opening the panel. The panel is a
`.card classes-panel` guarded by `@if (viewingBookings(); as row)`, opening with the
`.classes-panel-subject` line naming the class and its start time, then the loading/error/empty/content
tri-state, then one row per booking showing display name, email and booking time with a Cancel action.
Cancelling uses the existing `busy`/`failedId` idiom keyed by booking id, removes the row in place, and
patches the class row's `freeSpots` so the calendar tile behind the panel stays honest. "Anuluj" closes
the panel, per the sibling panels.

### Success Criteria:

#### Automated Verification:

- Frontend unit tests pass: `npm test` from `src/app/`
- Lint and format clean: `npm run quality:check` from `src/app/`
- Frontend builds within budget: `npm run build` from `src/app/`

#### Manual Verification:

- "Zapisani" opens a panel listing everyone signed up, with an explanatory empty state when nobody is
- Releasing a spot removes the row and raises the tile's free-spot count immediately
- The panel is usable on a phone with a class near capacity
- A past week still hides the tile actions, including the new one

**Implementation Note**: After completing this phase and all automated verification passes, pause here
for manual confirmation from the human that the manual testing was successful.

---

## Testing Strategy

### Unit Tests:

- `bookingFailureMessage` maps every reason in the union and falls back for an unknown one, mirroring
  `class-failure.spec.ts`
- `ScheduleCalendar` emits `classSelected` only when `selectable` is true, by pointer and by keyboard,
  and still refuses drag, resize, draw and actions when `readOnly`
- `ClassDetailsOverlay` renders Book when unbooked, Cancel when booked, an explanation when full or
  started, and closes on Escape
- `MyClasses` loads on init, removes a row on cancel, and shows the empty state

### Integration Tests:

- The headline race: N+1 members against a class of capacity N, one `HttpClient` each, `Task.WhenAll`;
  exactly N succeed, the rest get `class_full`, and the database holds exactly N Active rows
- A cancel and a book racing for the same last spot both terminate, and the resulting Active count never
  exceeds capacity
- Every refusal reason: `already_booked`, `class_full`, `class_started`, `class_cancelled`, `not_booked`
- Cancel then re-book leaves two rows, one Cancelled and one Active, and the filtered unique index does
  not reject the second
- `has_bookings` blocks a delete; a class with only cancelled bookings still deletes
- `capacity_below_bookings` blocks the edit; equal-to-booked-count is allowed
- Blocking a member cancels future bookings, leaves past ones, and frees the spots
- Pending and blocked accounts are refused by the `ActiveMember` policy on every booking route

### Manual Testing Steps:

1. `docker compose up -d`, apply migrations, confirm `GET /health`
2. Sign in as the admin, create a class type and a class with capacity 2
3. Sign in as two different active members in two browsers; both open the schedule and the same class
4. Both tap Book at the same moment — both succeed and the tile reads "0 / 2"
5. A third member taps Book — refused with the full-class message in the overlay
6. The first member cancels; the third member's Book now succeeds
7. Check "Moje zajęcia" for each member — each sees exactly their own upcoming class
8. As admin, open "Zapisani" for the class and confirm both names; release one spot and watch the tile
   count rise
9. As admin, try to delete the class — refused; try to lower its capacity to 1 — refused
10. As admin, block one of the booked members; confirm their spot returns and their past bookings, if
    any, are untouched
11. Repeat step 3-5 on a phone-width viewport

## Performance Considerations

The member schedule answers a window of up to 8 weeks, which for a single club is a few dozen classes.
The booked count is a correlated subquery inside the existing projection, so the request stays **one
round trip** — the property `ClassScheduleQuery.cs:40-43` was written to protect. `IX_Bookings_Class_Member_Active`
is a covering seek for that count, and `IX_Bookings_Member_Status` backs `GET /api/bookings/mine`. This
keeps the PRD's ~1 s perceived-response NFR intact on Azure SQL Basic's 5 DTU.

The retry loop is bounded at three attempts and only re-runs on a genuine lost race, which for a club
of dozens is rare; the cost of the token is one extra `Classes` UPDATE per booking.

## Migration Notes

One migration, `AddBookings`, creating the `Bookings` table and adding `Classes.ConcurrencyStamp`.
`Down` drops both and is genuinely reversible — no data-loss exception is taken, unlike `AddClassTypes`.
Verify the reverse by actually running it before the Phase 1 commit, per
`occurrences-from-class-types/plan.md:352`.

Rolling back past this release is safe in the code-then-schema sense: the previous build does not read
`Bookings` or `ConcurrencyStamp`, and an extra table plus an extra NOT NULL column with a default do not
break its queries. The schema therefore lags one release as `AGENTS.md` requires, and no destructive
step is taken here at all.

The filtered unique index means a session performing DML against `Bookings` must have SQL Server's
required `SET` options; EF Core's connections do, hand-run raw SQL sessions may not.

## References

- Requirements: `context/foundation/prd.md` US-01, FR-008, FR-009, FR-010, FR-014;
  `context/foundation/prd-v2.md` FR-014
- Roadmap item: `context/foundation/roadmap.md` §S-08
- Research: `context/changes/class-booking-and-cancel/research.md`
- The atomic status-flip idiom this plan generalises: `src/Application/Members/MemberAdminEndpoints.cs:173-203`
- The filtered unique index precedent: `src/Infrastructure/Persistence/Configurations/ClassTypeConfiguration.cs:36-39`
- The placeholder this plan replaces: `src/Infrastructure/Scheduling/ClassScheduleQuery.cs:73-77`,
  `src/Application/Scheduling/ClassEndpoints.cs:729-731`
- The delete guard instruction: `src/Application/Scheduling/ClassEndpoints.cs:518-520`
- The blocked-member question's reassignment: `context/archive/2026-09-01-member-management/frame.md:86-87`
- The unique-violation gap this plan closes: `context/archive/2026-09-01-registration-and-approval/reviews/impl-review.md:178-194`,
  `context/archive/2026-09-02-class-type-definitions/reviews/impl-review.md:100-136`
- The concurrency-test precedent: `tests/po-prostu-silka.Tests/MemberAdminEndpointTests.cs:160`
- The overlay precedent: `src/app/src/app/features/admin/classes/class-create-overlay.ts`
- The panel precedent: `src/app/src/app/features/admin/classes/classes.html:63-98`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Model, schema and the booking write path

#### Automated

- [x] 1.1 Solution builds warning-free
- [x] 1.2 Migration applies against a clean database and reverses
- [x] 1.3 All tests pass including `BookingEndpointTests`
- [x] 1.4 The concurrency test fails without the stamp rotation
- [x] 1.5 No EF Core reference in `Domain` or `Application`

#### Manual

- [ ] 1.6 `GET /health` reports a healthy database connection
- [ ] 1.7 Booking the same class twice is refused with `already_booked`
- [ ] 1.8 The member schedule still renders unchanged

### Phase 2: Bookings become visible to the rest of the system

#### Automated

- [ ] 2.1 Solution builds warning-free
- [ ] 2.2 All tests pass
- [ ] 2.3 The member schedule for a week issues one SQL statement

#### Manual

- [ ] 2.4 Booking drops the spot count; cancelling restores it
- [ ] 2.5 Deleting a class with a booking is refused and the class survives
- [ ] 2.6 Lowering a booked class's capacity below the signed-up count is refused
- [ ] 2.7 Blocking a member frees future spots and leaves past bookings alone

### Phase 3: The member's booking screens

#### Automated

- [ ] 3.1 Frontend unit tests pass
- [ ] 3.2 Lint and format clean
- [ ] 3.3 Frontend builds within budget
- [ ] 3.4 Backend tests still pass

#### Manual

- [ ] 3.5 Both member screens are reachable from the top nav
- [ ] 3.6 The overlay shows the description; booking updates the tile without a reload
- [ ] 3.7 A full class shows the refusal in the overlay in Polish
- [ ] 3.8 Cancel restores the spot and removes it from "Moje zajęcia"
- [ ] 3.9 The overlay closes on Escape and returns focus to the tile
- [ ] 3.10 Both screens are comfortable on a phone
- [ ] 3.11 The admin calendar is unchanged, including a past week hiding its actions

### Phase 4: The admin's booking list

#### Automated

- [ ] 4.1 Frontend unit tests pass
- [ ] 4.2 Lint and format clean
- [ ] 4.3 Frontend builds within budget

#### Manual

- [ ] 4.4 "Zapisani" lists everyone signed up, with an empty state when nobody is
- [ ] 4.5 Releasing a spot removes the row and raises the tile's count
- [ ] 4.6 The panel is usable on a phone with a class near capacity
- [ ] 4.7 A past week still hides the tile actions, including the new one
