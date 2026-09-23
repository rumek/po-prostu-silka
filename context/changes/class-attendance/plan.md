# S-27 Class Attendance Implementation Plan

## Overview

Staff record who came to a class, and the karnet counts attendance rather than booking. The trainer
does this for the classes they instruct, the admin for any class. A member sees their own attendance
history on `/my-classes` in a tab built for scanning on a phone. Roadmap S-27, M-8 anchors
AT-01–AT-05. The research this plan builds on is `context/changes/class-attendance/research.md`.

## Current State Analysis

- **`Booking` has no attendance.** It carries `Status` (`Active`/`Cancelled`) and an optional
  `MembershipPassId` (`src/Domain/Scheduling/Booking.cs`).
- **Entries used is `MembershipPassId == pass && Status == Active`**, copied in three places:
  - `BookingStore.CountActiveForPassAsync` (`src/Infrastructure/Scheduling/BookingStore.cs:44-48`),
    the write-path gate in `BookingProtocol.TryBookAsync` (`src/Application/Scheduling/BookingProtocol.cs:133-137`);
  - `MembershipPassQuery.GetForMemberAsync` (`src/Infrastructure/Members/MembershipPassQuery.cs:50-51`),
    read by the admin pass list, `RevokePass` and `UpdatePass`;
  - `MembershipPassQuery.FindCoveringAsync` (`:93-94`), read by `/api/passes/mine`.
- **`CancelClass` keeps every booking Active and rotates only the class stamp**
  (`src/Application/Scheduling/CancelClass.cs:81-89`). A cancelled class therefore keeps its
  entries spent. This is Open Roadmap Question 7.
- **`ReleaseBooking` has no time check** (`src/Application/Scheduling/ReleaseBooking.cs`), so staff
  can release a booking on a class that already happened. S-08 kept it partly as the no-show
  workaround.
- **The roster DTO has no attendance field**
  (`ClassBooking`, `src/Application/Scheduling/ClassBooking.cs:28-34`), and the SPA overlay has no
  notion of "started" (`features/class-bookings/class-bookings-overlay.{ts,html}`).
- **The desk calendar makes whole past weeks non-selectable** (`features/admin/classes/classes.ts:138-142`,
  `classes.html:19-20`). `/schedule` is always selectable (`schedule.html:18`).
- **The member has no read path for past classes.** `/api/bookings/mine` returns upcoming bookings
  only (`src/Infrastructure/Scheduling/BookingQuery.cs:15-50`). `/my-classes` is a single list
  (`features/my-classes/my-classes.html`).
- **`RevokePass` refuses while "used > 0"** (`src/Application/Members/RevokePass.cs:45-50`). The FK
  from `Booking` to the pass is `Restrict` on ANY booking, cancelled ones included
  (`BookingConfiguration.cs:40-47`).

## Desired End State

- **Marking attendance.**
  - A trainer opens the roster of a class they instruct, once it has started, and marks each booked
    member Obecny or Nieobecny. An admin does the same for any class, including from a past week's
    tile at the desk.
  - Corrections have no time limit and switch between the two states only.
  - Before the start the roster is today's booking roster. After the start, "Zwolnij miejsce" and the
    add picker are gone.
- **The entry rule.**
  - A booking with a pass consumes an entry unless it was released, its class was cancelled, or it
    was marked absent.
  - Unrecorded attendance counts as spent.
  - Correcting absent → present passes the same entry gate as a booking and can be refused with
    `no_entries_left`.
  - No sequence of writes can overdraw a pass.
  - A cancelled class returns its entries. This closes Open Roadmap Question 7.
- **Member history.** `/my-classes?widok=historia` shows:
  - a summary card for the current karnet (present / absent / not recorded);
  - past classes grouped by club-local month, each group with an "attended / counted" tally;
  - each row with a date, a name and a status chip: Obecny, Nieobecny, Nie odnotowano or Odwołane;
  - a "Pokaż wcześniejsze" button that loads three more months.
- **Verification.** `dotnet test`, `npm test` and `npm run quality:check` all pass, together with
  the manual steps under each phase.

### Key Discoveries:

- `BookingAuthorization.MayActOn` (`src/Application/Scheduling/BookingAuthorization.cs:49-51`) is
  AT-01 verbatim. Every endpoint in the staff group must call it (`BookingEndpoints.cs:79-93`).
- `BookingProtocol.ReturnEntryAsync` (`BookingProtocol.cs:212-227`) is how a write that returns an
  entry rotates the pass stamp. The block cascade rotates each distinct pass
  (`BookingStore.cs:92-104`), and that is the model for `CancelClass`.
- `BookingStatus` values are pinned by the filtered index `[Status] = 0`
  (`BookingConfiguration.cs:66-71`). Attendance must therefore not become a status value.
- `bookingFailureMessage`'s `Record` is keyed on the union
  (`core/scheduling/booking-failure.ts:21`), so a new reason fails the SPA build until it has words.
- `EndpointAuthorizationTests.cs:73-90` and `PersonaAccessTests.cs:25, 66` enumerate routes. Every
  new route must be added there, or those tests fail.
- `TestDataSeeder` seeds bookings (`src/Infrastructure/TestData/TestDataSeeder.cs:156`). Staging's
  seeded past bookings stay unrecorded, which reads correctly under the new rule.

## What We're NOT Doing

- Walk-ins without a booking, member self check-in, attendance statistics, and per-member no-show
  patterns for staff (M-8 "Not in scope").
- A time window, a background job or a staff flag for classes nobody marked. Unrecorded stays
  spent.
- Clearing a mark back to "not recorded".
- Separate correction rules for trainers and admins. One rule, from the start, unlimited.
- Changing the dashboard karnet card. "Zostało N wejść" stays as it is.
- Showing staff-released bookings in the member's history.
- Showing a cancelled class in the history before its start time. It joins the history when its date
  passes, like every other entry.
- Open Roadmap Question 8, whether the trainer sees e-mails on the roster. Untouched.
- An index for the new `Class.Status` join in the entries-used count. A pass has tens of bookings.

## Implementation Approach

- **Attendance is an orthogonal nullable column on `Booking`**, not a new `BookingStatus`. Every
  existing `Status == Active` predicate stays correct untouched: capacity, the unique index, the
  roster, `mine` and the block cascade. Only the entries-used predicate changes.
- **That predicate is defined once, in Infrastructure**, and all three sites use the one definition.
  The gate and the displayed balance cannot drift apart.
- **Order of the phases:**
  1. The rule and the schema. Deployable alone: the app behaves as today except that cancelled
     classes return entries and a started class refuses release.
  2. The marking API.
  3. The history API.
  4. The two SPA surfaces.

## Critical Implementation Details

- **One predicate, three sites.** The shared definition must translate inside `MembershipPassQuery`'s
  correlated `Select` subquery.
  - A static `Expression<Func<Booking, bool>>` holding the non-pass terms, used as
    `db.Bookings.Where(ConsumesAnEntry).Count(b => b.MembershipPassId == p.Id)`, is inlined by
    EF Core's funcletizer.
  - A method call inside the tree is not translated.
  - If translation still fails, keep three textual copies and add an integration test that asserts
    the gate and both read paths agree for every state (present, absent, unrecorded, cancelled
    class). Record that under "Adapted during implementation" (lessons.md).
- **Null semantics.** `b.Attendance != BookingAttendance.Absent` must keep NULL rows. EF Core's
  default relational null semantics emit `IS NULL OR <> 1`. The unrecorded-counts-as-spent test pins
  this. Do not "optimise" the query with `UseRelationalNulls`.
- **Stamp rotation.** Every write that can change the pool rotates `MembershipPass.ConcurrencyStamp`
  inside the same `SaveChangesAsync` as the write:
  - present → absent;
  - absent → present;
  - unrecorded → either state (a uniform rule, cheap);
  - class cancel, once per distinct pass.
- **Absent → present re-runs the gate inside a retry loop**, the same as `TryBookAsync`: re-read the
  class, the booking and the pass on every attempt. A present → absent mark needs the loop too,
  because its pass stamp can lose a race.
- **`RevokePass` must NOT switch to the new count.** It refuses while any `Active` booking references
  the pass, marked absent included. That row is history that records which karnet paid, and the
  `Restrict` FK would reject the delete anyway. `UpdatePass` switches to the new count, since
  lowering `EntryCount` is refused only below entries actually consumed.

## Phase 1: The entry rule and the schema

### Overview

Add the attendance column. Put the entries-used predicate in one place and use it at all three
sites. Make class cancellation return entries. Refuse release after the start. Pin all of it with
integration tests.

### Changes Required:

#### 1. Domain

**File**: `src/Domain/Scheduling/BookingAttendance.cs` (new), `src/Domain/Scheduling/Booking.cs`

**Intent**: Record attendance as a fact about a booking whose class took place. It is kept separate
from whether the booking stands.

**Contract**:
- `enum BookingAttendance { Present = 0, Absent = 1 }`, values pinned.
- `Booking` gains three fields:
  - `BookingAttendance? Attendance` (null = not recorded);
  - `DateTimeOffset? AttendanceRecordedAt`;
  - `string? AttendanceRecordedBy`, the recording account's user id, with no FK. Staff may lack a
    `Member` row, and an audit field must survive the account.
- Amend the `MembershipPassId` doc comment. "ACTIVE bookings" becomes the new rule (AT-03 amends
  MP-06).

#### 2. Persistence

**File**: `src/Infrastructure/Persistence/Configurations/BookingConfiguration.cs`, new migration
`AddBookingAttendance`

**Intent**: Store the new fields additively. `Down` drops the three columns.

**Contract**:
- `Attendance` is stored as a nullable int. `AttendanceRecordedBy` is `nvarchar(450)`, the Identity
  key length.
- No backfill: existing rows read as unrecorded, and unrecorded means spent, which is today's
  meaning.
- Generate the migration with
  `dotnet ef migrations add AddBookingAttendance --project src/Infrastructure/po-prostu-silka.Infrastructure.csproj --startup-project src/Api/po-prostu-silka.Api.csproj`.

#### 3. One entries-used predicate

**File**: `src/Infrastructure/Scheduling/EntryConsumption.cs` (new), `BookingStore.cs`,
`src/Infrastructure/Members/MembershipPassQuery.cs`

**Intent**: Replace the three copies of the entries-used predicate with one definition.

**Contract**:
- `EntryConsumption.ConsumesAnEntry` is
  `b => b.Status == Active && b.Class.Status != ClassStatus.Cancelled && b.Attendance != BookingAttendance.Absent`.
- `CountActiveForPassAsync` is renamed `CountConsumingForPassAsync` in `IBookingStore` and its
  caller.
- Both `EntriesUsed` subqueries in `MembershipPassQuery` use `ConsumesAnEntry`.
- The type's doc comment carries the three-state reading (reserved / spent / returned) and the
  reason unrecorded counts as spent.

#### 4. Revoke keeps its literal meaning

**File**: `src/Application/Members/RevokePass.cs`, `IBookingStore`/`BookingStore`

**Intent**: A pass referenced by any active booking stays unrevocable, whatever that booking's
attendance says. See Critical Implementation Details.

**Contract**:
- New `IBookingStore.AnyActiveForPassAsync(passId)`. `RevokePass` refuses `has_active_bookings`
  on it instead of on `EntriesUsedAsync`.
- `UpdatePass` keeps `EntriesUsedAsync` and therefore picks up the new rule.

#### 5. Cancelling a class returns entries

**File**: `src/Application/Scheduling/CancelClass.cs`, `IBookingStore`/`BookingStore`

**Intent**: Nobody attends a cancelled class, so its bookings stop consuming. Rotate every affected
pass stamp in the same single save. This answers Open Roadmap Question 7.

**Contract**:
- New `IBookingStore.RotatePassStampsForClassAsync(classId)`. It loads the distinct pass ids of the
  class's Active bookings and rotates each through `IMembershipPassStore.FindManyAsync`, the same
  shape as the block cascade.
- `CancelClass` calls it before `TrySaveChangesAsync`.
- Update the handler's "nothing else is touched" doc paragraph. Rows stay Active, and what changes
  is what they cost.

#### 6. Release refuses after the start

**File**: `src/Application/Scheduling/ReleaseBooking.cs`

**Intent**: Once a class has started, "Nieobecny" is the honest record of a no-show. Release would
erase the booking from the member's history.

**Contract**:
- After `MayActOn` and the 404 checks, refuse `class_started` (409, the existing reason and words)
  when `entity.StartsAt <= now`.
- The doc comment drops the "no-show case" rationale.

#### 7. Tests

**File**: `tests/po-prostu-silka.Tests/AdminBookingEndpointTests.cs`, `ClassCancellationTests.cs`,
`MembershipPassEndpointTests.cs`

**Intent**: Pin the new rule, and update the tests that pinned the old one.

**Contract**: The new or changed tests. Attendance is seeded directly through the DbContext here,
because the endpoint arrives in Phase 2.
- `An_unrecorded_past_booking_still_consumes_its_entry`
- `A_booking_marked_absent_returns_its_entry`: via `/api/passes/mine` and `/api/admin/members/{id}/passes`.
- `Cancelling_a_class_returns_the_entries_its_bookings_held`: replaces the "leaves every booking
  active" assertion about entries. Rows stay Active.
- `Cancelling_a_class_rotates_each_booked_pass_stamp`: a stale-stamp pass write loses.
- `Releasing_a_booking_after_the_start_is_refused`
- `A_pass_whose_only_booking_was_marked_absent_still_cannot_be_revoked`
- `Lowering_entry_count_ignores_absent_bookings`

**Adapted during implementation.** The Phase 1 tests live in the new
`tests/po-prostu-silka.Tests/AttendanceEndpointTests.cs` (the file Phase 2 names) rather than spread
across the three existing files: they all need the same "book a future class, then move its start into
the past in the database" arrangement, and one set of helpers beats three copies. The existing
cancellation and revoke tests were left as they were, since their assertions still hold. One extra
test, `A_booking_marked_present_keeps_its_entry_spent`, pins the Present leg. `EntryConsumption`
translated as an expression in all three sites, so the textual-copy fallback was not needed.

### Success Criteria:

#### Automated Verification:

- Build passes with no new warnings: `dotnet build po-prostu-silka.slnx`
- Migration applies and reverts cleanly against local SQL Server: `dotnet ef database update` then
  `dotnet ef database update ActivatePendingAccounts` (both projects passed)
- All backend tests pass: `dotnet test`

#### Manual Verification:

- On a local DB, cancelling a class with a karnet-paid booking raises that member's "Zostało N
  wejść" on the dashboard by one.
- Releasing a booking on a class that has started shows "Te zajęcia już się rozpoczęły" in the
  roster overlay.

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase.

---

## Phase 2: The marking API

### Overview

Add an endpoint that records attendance, gated by `MayActOn` and guarded by the pass stamp. The
roster starts carrying attendance.

### Changes Required:

#### 1. Record attendance

**File**: `src/Application/Scheduling/RecordAttendance.cs` (new),
`src/Api/Endpoints/Scheduling/BookingEndpoints.cs`

**Intent**: Staff mark one booked member present or absent, and correct the mark later. Correction
back to absent → present spends an entry, so it passes the booking gate.

**Contract**:
- Route: `PUT /api/admin/classes/{classId:guid}/bookings/{bookingId:guid}/attendance` in the staff
  group. Body: `{ "attendance": "present" | "absent" }`.
- Response: 200 with the updated `ClassBooking` row.
- Order of checks, inside a `BookingProtocol.MaxAttempts` retry loop:
  1. The class exists, else 404.
  2. `MayActOn`, else 403 via `NotYourClass()`.
  3. The booking exists, belongs to the class and is Active, else 404 (the same rules as release).
  4. The class is not cancelled, else `class_cancelled`.
  5. The class has started (`StartsAt <= now`), else a new reason, `class_not_started`.
  6. If the mark is unchanged, return 200 with no write. An idempotent PUT.
  7. If the booking has a pass: when the new state consumes and the old one did not (absent →
     present), count consuming bookings for the pass and refuse `no_entries_left` when the count is
     `>= EntryCount`. Rotate the pass stamp on every change.
  8. Set `Attendance`, `AttendanceRecordedAt` and `AttendanceRecordedBy` (the principal's
     `NameIdentifier`).
  9. `TrySaveAsync`. On a conflict, `DiscardChanges` and loop. After the last attempt, `conflict`.
- The class stamp is NOT rotated: attendance changes no capacity.
- Refusals go through `BookingProtocol.Refuse` (409 with `BookingFailure`).
- The body is parsed as a string enum. An unknown value is a 400 validation problem, like the other
  staff endpoints.

#### 2. The roster carries attendance

**File**: `src/Application/Scheduling/ClassBooking.cs`, `src/Infrastructure/Scheduling/BookingQuery.cs`

**Intent**: The overlay needs each row's current mark.

**Contract**: `ClassBooking` gains `string? Attendance` (`"present"` / `"absent"` / null), projected
in `GetForClassAsync`.

#### 3. Tests

**File**: `tests/po-prostu-silka.Tests/AttendanceEndpointTests.cs` (new),
`EndpointAuthorizationTests.cs`

**Intent**: Pin AT-01, AT-02 and the gate.

**Contract**:
- The PUT route is added to the staff list in `EndpointAuthorizationTests`.
- Tests:
  - `A_trainer_marks_attendance_on_a_class_they_instruct`
  - `A_trainer_cannot_mark_attendance_on_another_trainers_class` (403)
  - `An_admin_marks_attendance_on_any_class`
  - `A_member_cannot_mark_attendance` (403 by policy)
  - `Marking_before_the_start_is_refused` (`class_not_started`)
  - `Marking_on_a_cancelled_class_is_refused`
  - `Marking_a_released_booking_is_not_found`
  - `Marking_the_same_state_twice_is_idempotent`
  - `Correcting_absent_to_present_is_refused_when_the_pass_is_full`: the question-4 scenario with 8
    entries, one absent and a new booking filling the pool.
  - `Correcting_absent_to_present_spends_the_entry_again`
  - `Concurrent_rebooking_and_correction_never_overdraw_the_pass`: the same shape as
    `Concurrent_bookings_never_exceed_the_pass_entry_count`.
  - `The_roster_reports_each_rows_attendance`
  - `Marking_attendance_records_who_and_when`

### Success Criteria:

#### Automated Verification:

- All backend tests pass: `dotnet test`

#### Manual Verification:

- Through `/openapi` or a REST client on a local DB, a trainer marks, corrects and is refused on
  another trainer's class. The karnet's entries-left moves as expected.

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase.

---

## Phase 3: The member history API

### Overview

Give the member a read path for their past classes and a summary for their current karnet.

### Changes Required:

#### 1. History query and endpoint

**File**: `src/Application/Scheduling/GetMyAttendanceHistory.cs` (new),
`src/Application/Scheduling/MyAttendanceHistory.cs` (new DTOs), `IBookingQuery`/`BookingQuery`,
`BookingEndpoints.cs`

**Intent**: AT-04. This is the member's own past classes, and nothing about anyone else.

**Contract**:
- Route: `GET /api/bookings/history?before=YYYY-MM-DD`, in the `MemberOnly` `myBookings` group. The
  member is resolved from the principal, like `mine`.
- **Window.** `before` is the club-local first day of a month, exclusive. When it is absent, the
  window ends now. The window covers three club-local calendar months: the month containing its end
  and the two before it.
- **Items.** Every booking of this member with `Status == Active` and `Class.StartsAt <= now` whose
  class starts inside the window, newest first.
  - Cancelled classes are included, so a member whose entry came back can see why.
  - Released bookings are excluded, because they are `Status == Cancelled`.
- `MyAttendanceEntry(BookingId, ClassId, Name, StartsAt, DurationMinutes, Instructor, Outcome)`,
  with `Outcome` one of `present | absent | unrecorded | cancelled`. `cancelled` wins over any mark.
- `MyAttendanceHistory(Summary?, Items, EarlierBefore?)`:
  - `EarlierBefore` is the next `before` value, or null when the member has no older active booking.
  - `Summary` is present only on the first page (no `before`), and only when a pass covers today.
- `MyAttendanceSummary(TypeName, ValidFrom, ValidTo, Present, Absent, Unrecorded)` counts that
  pass's started, non-cancelled Active bookings.
- Club-local month arithmetic goes through `ClubTime`. Do not convert in UTC.

**Adapted during implementation.** `MyAttendanceSummary` also carries `EntryCount`: Phase 5's dot
strip draws "one dot per counted class up to the pass's count" and has no other way to learn the
count. `ClubTime` gained `StartOfLocalDay(DateOnly)` for the window bounds. A `before` that is not the
first of a month is a 400 validation problem, and the tests pin it
(`A_before_that_is_not_the_first_of_a_month_is_a_bad_request`).

#### 2. Tests

**File**: `tests/po-prostu-silka.Tests/AttendanceHistoryTests.cs` (new), `PersonaAccessTests.cs`,
`EndpointAuthorizationTests.cs`

**Intent**: Pin the history scope, the window and the persona rule.

**Contract**:
- Tests:
  - `History_lists_past_classes_with_their_outcome` (all four outcomes)
  - `History_omits_released_bookings_and_future_classes`
  - `History_never_includes_another_members_bookings`
  - `A_class_started_but_unmarked_reads_unrecorded`
  - `The_window_is_three_club_local_months_and_pages_backwards`: includes a booking at 23:30 UTC on
    the last day of a month, which is the next month club-local.
  - `EarlierBefore_is_null_when_nothing_older_exists`
  - `The_summary_counts_the_current_pass_only_and_skips_cancelled_classes`
  - `There_is_no_summary_without_a_covering_pass`
- Add the route to the `MemberOnly` lists in `PersonaAccessTests` (staff refused) and
  `EndpointAuthorizationTests`.

### Success Criteria:

#### Automated Verification:

- All backend tests pass: `dotnet test`

#### Manual Verification:

- On Staging-shaped seed data, `GET /api/bookings/history` as a seeded member returns sensible
  months and a summary.

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase.

---

## Phase 4: SPA — the roster in attendance mode, and past tiles at the desk

### Overview

The roster overlay gets two modes, chosen by the class's start. The desk calendar lets an admin open
a past week's rosters.

### Changes Required:

#### 1. Models, service and words

**File**: `core/scheduling/booking.models.ts`, `booking.service.ts`, `booking-failure.ts`

**Intent**: Mirror Phase 2's contract.

**Contract**:
- `ClassBooking` gains `attendance: 'present' | 'absent' | null`.
- `BookingFailure` gains `class_not_started`, with words in `MESSAGES` and a key in
  `BOOKING_FAILURE_REASONS`. Suggested words: "Obecność można zaznaczyć dopiero po rozpoczęciu
  zajęć."
- `BookingService.recordAttendance(classId, bookingId, attendance)` returns the updated row.

#### 2. The roster overlay

**File**: `features/class-bookings/class-bookings-overlay.{ts,html,scss,spec.ts}`

**Intent**: Before the start, the overlay is unchanged. After it, each row carries an Obecny /
Nieobecny toggle, and release and the add block are gone. A cancelled class is a read-only list.

**Contract**:
- `started` is computed from `row().startsAt <= now`, read once on open. A class that starts while
  its overlay is open switches mode on the next open, not live.
- Each row carries a two-button group (`role="group"`, `aria-pressed` on each button) with an icon
  and a word, so colour is not the only signal.
  - The current state is pressed. Unrecorded shows neither pressed.
  - Busy and failure use the existing `createBusySet` and `failedId`/`failure` per-row pattern,
    words from `bookingFailureMessage`. There is no toast, per research §"four-outlet rule".
  - The server's returned row replaces the local one.
- The header line gains a tally for started classes: "Obecni: X · Nieobecni: Y · Nieoznaczeni: Z".
- The toggle is local markup inside `features/`. It is not a kit family, so the presentational rule
  does not apply. The buttons are real `<button>`s.
- New spec cases:
  - the mode split;
  - pressed state;
  - a mark round-trip;
  - `no_entries_left` shown on the row;
  - no release button after the start;
  - no add block after the start;
  - a cancelled class read-only.

#### 3. Desk calendar past tiles

**File**: `features/admin/classes/classes.{ts,html,spec.ts}`

**Intent**: The "unlimited" correction window only means something if an admin can reach a past
week's roster from the desk.

**Contract**:
- `[selectable]` becomes always true. `[readOnly]="isPast()"` stays (no drag, draw or resize).
- In a past week, `select(row)` opens the bookings overlay directly (`openBookings`) instead of the
  actions overlay.
- The past-week hint changes to "Ten tydzień już minął — możesz tylko sprawdzić i poprawić
  obecność."
- `schedule-calendar`'s selectable/readOnly separation is untouched.

### Success Criteria:

#### Automated Verification:

- SPA unit tests pass: `npm test` (from `src/app/`)
- Lint and format pass: `npm run quality:check`
- Production build stays under the budget warning: `npm run build`. Record the initial-bundle size
  in AGENTS.md only if the eager chunk changed.

#### Manual Verification:

- As a trainer on a phone width, open a started class on `/schedule`, mark two members and correct
  one. The tally and the pressed states update, and nothing on the row is colour-only.
- As a trainer, a class that has not started still shows "Zwolnij miejsce" and the add picker.
- As an admin at a desk, go back a week, click a tile: the roster opens in attendance mode and a
  correction sticks.
- Correct absent → present on a pass that is full. The row shows the "no free entries" sentence.

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase.

---

## Phase 5: SPA — the member's history tab

### Overview

`/my-classes` gets "Nadchodzące | Historia" tabs. The history tab is a summary card and month groups
of rows with status chips.

### Changes Required:

#### 1. Models and service

**File**: `core/scheduling/booking.models.ts`, `booking.service.ts`

**Intent**: Mirror Phase 3's contract.

**Contract**: `MyAttendanceHistory`, `MyAttendanceEntry` and `MyAttendanceSummary` types, and
`BookingService.getHistory(before?: string)`.

#### 2. The tabs

**File**: `features/my-classes/my-classes.{ts,html,scss,spec.ts}`

**Intent**: One screen with two views. The tab lives in the URL so reload keeps it, and the tab does
not add a history entry, so system back leaves the screen as on Android.

**Contract**:
- The query param `widok=historia` selects the history tab. Absent means upcoming.
- Switching calls `router.navigate([], { queryParams, replaceUrl: true })`.
- ARIA tabs: `role="tablist"` / `tab` / `tabpanel`, `aria-selected`, and arrow-key movement between
  the two tabs.
- The route's `title` ("Zajęcia") and `level: 'tab'` are unchanged, and `h1.screen-title` stays
  "Moje zajęcia".
- The history loads lazily on first selection, with its own load fence. That is one fence per
  independent load, per AGENTS.md.

#### 3. The history view

**File**: `features/my-classes/attendance-history.{ts,html,scss,spec.ts}` (new component inside the
feature)

**Intent**: AT-05. Readable at a glance on a phone: grouping, visual status, a summary. No sentence
per class.

**Contract**:
- **Summary card**, only when `summary` is present:
  - the pass name and its "do d MMMM" end date;
  - three counts with their chips (Obecny / Nieobecny / Nie odnotowano);
  - a compact dot strip, one dot per counted class up to the pass's count, decorative and
    `aria-hidden`, with the counts carrying the meaning.
- **Month groups:**
  - a heading with the Polish month and year, plus a tally "X/Y obecności", where Y excludes
    cancelled and unrecorded;
  - rows through `app-list` / `li[appRow]`, with the day number and weekday in a fixed-width date
    column, then `.row-name` for the class name and `.row-meta` for time and instructor;
  - the chip in `slot="actions"`.
- **Chips:** icon plus word, with state tokens from `styles.scss` and a dark-mode variant.
  Cancelled is muted and struck through.
- **States:**
  - `app-loading` on first load;
  - `app-empty` "Nie masz jeszcze zajęć w historii." when nothing has happened yet;
  - load failure as the screen-state alert (outlet 4);
  - "Pokaż wcześniejsze" appends the next window. Its failure is an inline alert under the button,
    because the loaded list stays readable.
- **Breakpoints:** only via `bp` mixins. On a phone the summary is one column. Above `form-columns`
  the three counts sit in a row.
- **Spec:**
  - grouping by club-local month;
  - chip per outcome;
  - summary absent without a pass;
  - "Pokaż wcześniejsze" appends and hides when `earlierBefore` is null;
  - empty state;
  - a staff account never requests `/api/bookings/history`, because the route is `memberGuard`ed.

**Adapted during implementation.**
- **No dark-mode variant.** The app has no dark theme at all: `styles.scss` defines no
  `prefers-color-scheme` block. The chips use the existing `--success`/`--danger`/`--muted` tokens,
  and a dark variant arrives with a dark theme, not ahead of one. Manual check 5.5 is light only.
- The tab, URL and keyboard cases live in `my-classes.tabs.spec.ts`, which uses
  `RouterTestingHarness` because the tab state is in the URL. `my-classes.spec.ts` keeps its
  list cases, and its "no buttons" assertion now excludes the two `role="tab"` buttons.
- The history is lazy through a latch: it is not rendered until its tab is first selected, and after
  that it stays mounted but hidden, so switching tabs back and forth does not refetch it.
- `Intl.DateTimeFormat` with `timeZone: 'Europe/Warsaw'` does the club-local grouping and the date
  column. Angular's `DatePipe` accepts only fixed offsets, not IANA zones.

### Success Criteria:

#### Automated Verification:

- SPA unit tests pass: `npm test`
- Lint and format pass: `npm run quality:check`
- Route identity spec passes (`app.routes.spec.ts`, part of `npm test`)
- Production build: `npm run build`

#### Manual Verification:

- On a 360 px phone, the history tab reads at a glance: the summary on top, months as sections, a
  status visible without reading a sentence. Check light and dark mode.
- Reloading on `?widok=historia` keeps the tab. System back from the history tab leaves
  `/my-classes` rather than flipping tabs.
- Screen reader (NVDA or VoiceOver) announces the tabs and each row's status word.
- "Pokaż wcześniejsze" loads older months and disappears at the start of the member's history.

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase.

---

## Testing Strategy

### Unit Tests:

- The SPA overlay's mode split, toggle pressed-state and per-row failure.
- The desk calendar's past-week selection.
- The history grouping, chips, pagination and tabs-in-URL.

### Integration Tests:

- The entry rule across every state: unrecorded, present, absent, cancelled class, released.
- The absent → present gate under concurrency.
- `CancelClass`'s stamp rotation.
- Release refused after the start.
- Revoke unchanged, `UpdatePass` on the new count.
- The marking authorization (AT-01) and the bookings-only rule (AT-02).
- The history window, scope and summary.
- The persona lists.

### Manual Testing Steps:

1. As a trainer, mark attendance on a started class, then correct a mark. Check the member's karnet.
2. Fill a karnet, then correct an absent back to present, and see the refusal.
3. As an admin, cancel a class with bookings. Every booked member's entries-left goes up.
4. As the member, open Zajęcia → Historia on a phone and scan the months.

## Performance Considerations

- The entries-used count gains a join to `Classes` that `IX_Bookings_MembershipPassId_Status` does
  not cover. A pass holds tens of bookings, so the count stays trivially cheap.
- The history query is bounded to three months per call and seeks `IX_Bookings_MemberId_Status`.

## Migration Notes

- The migration is additive, with three nullable columns. `Down` drops them.
- No backfill: existing rows read as "not recorded", which spends the entry, exactly today's
  meaning.
- The only behavioural change on deploy is that already-cancelled classes return their entries.
  Members affected by past cancellations see their balance rise. That is the intended answer to Open
  Roadmap Question 7, and it is worth one line in the release note.
- Rollback: the previous artifact ignores the columns. Its old predicate re-spends cancelled-class
  and absent entries, which only lowers balances. No overdraw is possible.

## References

- Research: `context/changes/class-attendance/research.md`
- Roadmap: `context/foundation/roadmap.md`, M-8 AT-01–AT-05 and S-27, Open Question 7
- The booking gate: `src/Application/Scheduling/BookingProtocol.cs:63-193`
- The stamp rotation model: `src/Infrastructure/Scheduling/BookingStore.cs:53-110`
- The roster overlay: `src/app/src/app/features/class-bookings/class-bookings-overlay.{ts,html}`
- S-16 plan (entry derivation): `context/archive/2026-09-09-membership-pass-and-staff-booking/plan.md:85-90, 141-145`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: The entry rule and the schema

#### Automated

- [x] 1.1 Build passes with no new warnings — 2fefc0f
- [x] 1.2 Migration applies and reverts cleanly against local SQL Server — 2fefc0f
- [x] 1.3 All backend tests pass — 2fefc0f

#### Manual

- [ ] 1.4 Cancelling a class raises the booked member's entries left by one
- [ ] 1.5 Releasing on a started class shows the "already started" sentence

### Phase 2: The marking API

#### Automated

- [x] 2.1 All backend tests pass — 9149170

#### Manual

- [ ] 2.2 Trainer marks, corrects and is refused on another trainer's class via REST client

### Phase 3: The member history API

#### Automated

- [x] 3.1 All backend tests pass — b78f440

#### Manual

- [ ] 3.2 History endpoint returns sensible months and summary on seed data

### Phase 4: SPA — the roster in attendance mode, and past tiles at the desk

#### Automated

- [x] 4.1 SPA unit tests pass — b397a84
- [x] 4.2 Lint and format pass — b397a84
- [x] 4.3 Production build stays under the budget warning — b397a84

#### Manual

- [ ] 4.4 Trainer marks and corrects on a phone; tally and pressed states update
- [ ] 4.5 A not-yet-started class keeps release and add
- [ ] 4.6 Admin opens a past week's tile at the desk and corrects attendance
- [ ] 4.7 Absent → present on a full pass shows the refusal on the row

### Phase 5: SPA — the member's history tab

#### Automated

- [x] 5.1 SPA unit tests pass
- [x] 5.2 Lint and format pass
- [x] 5.3 Route identity spec passes
- [x] 5.4 Production build

#### Manual

- [ ] 5.5 History tab reads at a glance on a 360 px phone, light and dark
- [ ] 5.6 Reload keeps the tab; system back leaves the screen
- [ ] 5.7 Screen reader announces tabs and status words
- [ ] 5.8 "Pokaż wcześniejsze" pages back and disappears at the start
