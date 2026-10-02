# Class Makeups Implementation Plan

## Overview

Staff record one of three outcomes for a booked member: "był", "nie był – odrobi" or "nie był –
przepada". This matches the club's attendance spreadsheet (`xlsx/treningi.xlsx`). A "odrobi" absence
spends its entry and earns ONE free makeup. The makeup is a booking linked to the absence, it consumes
no entry, and it must take place within 30 days. Admins and trainers work from a new "Odrabianie"
list, and a member sees on Moje zajęcia that they have a class to make up.

## Current State Analysis

- **`BookingAttendance`** (`src/Domain/Scheduling/BookingAttendance.cs:20-27`) has `Present = 0` and
  `Absent = 1`, persisted as an int. `null` means unrecorded and counts as spent.
- **`EntryConsumption.ConsumesAnEntry`** (`src/Infrastructure/Scheduling/EntryConsumption.cs:40-43`)
  returns the entry for `Absent`. It is read at FOUR sites:
  - `BookingStore.CountConsumingForPassAsync` (`BookingStore.cs:44-51`), the gate in `BookingProtocol`
    and `RecordAttendance`;
  - `MembershipPassQuery.GetForMemberAsync` (`:53-55`);
  - `MembershipPassQuery.FindCoveringAsync` (`:99-101`);
  - `MemberQuery.GetMembersAsync` (`:125-131`).
- **`RecordAttendance`** (`src/Application/Scheduling/RecordAttendance.cs`) accepts
  `"present" | "absent"`. Its only re-spend case is `Present && old == Absent`, which is refused with
  `no_entries_left` when the pass is full (114-134). It never rotates the class stamp.
- **`BookingProtocol.TryBookAsync`** (`BookingProtocol.cs:63-193`) is the race-safe booking loop:
  - capacity, guarded by the class stamp;
  - the covering pass on the class's club-local date (`FindCoveringAsync`);
  - the entry pool, guarded by the pass stamp;
  - retry on concurrency or unique violation.

  It takes no principal; the callers authorize.
- **`BookingAuthorization.MayActOn`** (`BookingAuthorization.cs:49-51`) limits a trainer to classes
  they instruct. Booking, release, attendance and the roster all call it.
- **Cancelling a class does NOT cancel its bookings.** They stay `Active`, and `ConsumesAnEntry`
  excludes them through `Class.Status`.
- **Derived-on-read precedent:** `ExpiringPassPredicate` (`src/Infrastructure/Members/ExpiringPassPredicate.cs:36-67`).
  It holds the `WindowDays` constant and one expression that both the card and the list read.
- **SPA:**
  - The roster overlay `features/class-bookings/class-bookings-overlay.*` marks attendance with two
    `aria-pressed` buttons (html:499-549) and a tally (ts:132-140).
  - `features/my-classes/attendance-history.ts` maps outcomes to words and icons (`OUTCOME_LABELS`,
    ts:16-21).
  - `core/scheduling/booking.models.ts`: `Attendance = 'present' | 'absent'` (l.65),
    `AttendanceOutcome` (l.129) and the `BookingFailure` union (l.87-123).
  - A trainer can reach only their own classes. No class picker exists outside the schedule grid.

## Desired End State

- **Roster:** a started class's roster offers three outcomes per member, Był / Odrobi / Przepada.
  - The tally reads "Obecni · Odrobią · Przepada · Nieoznaczeni".
  - A makeup booking carries an "Odrabianie" badge and offers only Był / Przepada.
  - A legacy `Absent` row reads "Nieobecny – wejście zwrócone" until someone re-marks it.
- **Entries:** "Odrobi" and "Przepada" keep the entry spent. A legacy `Absent` keeps it returned, so
  no balance changes on deploy.
- **"Odrabianie" screen (admin and trainer):**
  - lists open items, i.e. "do odrobienia" and "zaplanowane", by nearest deadline first;
  - a checkbox "Pokaż zamknięte" (`zamkniete=1` in the URL) adds the "odrobił" and "nie odrobił"
    items;
  - per item:
    - "Zapisz na odrabianie" opens a picker of upcoming classes from ANY trainer, starting by the
      deadline, with a free spot, on a day the member's karnet covers;
    - "Zwolnij" releases a planned makeup before its start;
    - "Zamknij" / "Otwórz ponownie" closes the item by hand, or reopens it while within the
      deadline.
- **Member:**
  - Moje zajęcia shows "Do odrobienia: N" with the nearest deadline;
  - an upcoming makeup reads "Odrabianie";
  - history shows the new outcome words.
- **Verify** with `dotnet test`, `npm test`, `npm run quality:check`, and the rewritten E2E spec in
  pre-push.

### Key Discoveries:

- **`ConsumesAnEntry` must keep NULL rows:** EF emits `IS NULL OR <> 1`
  (`EntryConsumption.cs:34-36`). Adding `MakeupForBookingId == null` and widening the attendance
  check must keep that semantics.
- **Cancelled-class bookings stay `Active`.** A filtered unique index "one active makeup per absence"
  would therefore block re-booking after the makeup's class is cancelled. The makeup booking path
  cancels such a superseded row in the same save (see Phase 2).
- **The roster projection `BookingQuery.GetForClassAsync` (`:52-77`) is reused** by `CancelClass`,
  the class edit fan-out and `RecordAttendance`'s return value. A new `IsMakeup` field reaches all of
  them, which is harmless.
- **New routes must be listed in `tests/po-prostu-silka.Tests/EndpointAuthorizationTests.cs`**
  (`TrainerAdminRoutes` 75-82, `MemberOnlyRoutes` 89-96).
- **The SPA failure contract counts unions:**
  - `core/http/failure-contract.spec.ts:175-178` asserts eighteen;
  - a new `MakeupFailure` union makes it nineteen;
  - every reason needs a unique sentence.
- **Manual cases ATT-01/02 live in `context/testing/04-schedule-bookings-attendance.md`.**

## What We're NOT Doing

- Fixed group rosters and recurring series.
- Comments on attendance, the "WYGASŁ" filter, and money (amounts, payment history).
- Booking a makeup from the class roster's "Dopisz członka". Makeups are booked only from the
  Odrabianie list.
- A dashboard card for makeups, because the eager bundle has 19 kB of slack.
- An "odrobi" outcome on a makeup booking. A makeup is a single attempt, so no chains.
- Rewriting legacy `Absent` rows, and any data backfill.
- Notifications about makeups.
- Member self-service of any kind (S-16 stands).

## Implementation Approach

A makeup is not a new entity. Each "item" is an absence booking (`Attendance == Makeup`), optionally
linked to one makeup booking (`Booking.MakeupForBookingId`), plus an optional manual close on the
absence row. One Infrastructure predicate derives the status, and the list, the member summary and
the booking gate all read it, exactly as S-35 did. The makeup booking reuses `BookingProtocol`'s
loop with the entry check skipped, so capacity and the no-overbooking guarantee stay on the same
code path.

## Critical Implementation Details

**State sequencing.** An item's status is derived in this order, first match wins:
1. Closed by hand → `not_made_up`.
2. It has a linked `Active` makeup booking whose class is not cancelled:
   - marked `Present` → `made_up`;
   - marked `Forfeited` → `not_made_up`;
   - otherwise → `planned`.
3. Club-local today > deadline → `not_made_up`.
4. Otherwise → `open`.

The deadline is the absence class's club-local date + `MakeupRules.DeadlineDays` (30). A makeup class
qualifies when its club-local date ≤ the deadline.

**Timing.** "Zaplanowane" survives past the deadline when the makeup was booked within it, because
the deadline governs the class date, not the outcome. Do not let rule 3 override rule 2.

## Phase 1: Attendance outcomes and the entry rule

### Overview

The data model, the migration and the new consumption rule. Attendance writes accept three outcomes.
After this phase the API behaves correctly for attendance; makeups cannot be booked yet.

### Changes Required:

#### 1. Domain

**File**: `src/Domain/Scheduling/BookingAttendance.cs`, `src/Domain/Scheduling/Booking.cs`, new `src/Domain/Scheduling/MakeupRules.cs`

**Intent**: Add the two outcomes and keep `Absent` as the legacy "entry returned" value. Give the
booking its makeup link and manual-close fields. Put the 30-day constant where both Application and
Infrastructure can read it.

**Contract**:
- `BookingAttendance { Present = 0, Absent = 1 /* legacy, read-only */, Makeup = 2, Forfeited = 3 }`.
  Update the doc comments to say that `Absent` is no longer written.
- `Booking.MakeupForBookingId : Guid?`. It is set only on a makeup booking and points at the absence
  booking. Add a read-side navigation if useful.
- `Booking.MakeupClosedAt : DateTimeOffset?` and `Booking.MakeupClosedBy : string?` (max 450). They
  are set only on an absence booking that was closed by hand.
- `MakeupRules.DeadlineDays = 30` and `MakeupRules.DeadlineFor(DateOnly absenceDate)`.

#### 2. Persistence and migration

**File**: `src/Infrastructure/Persistence/Configurations/BookingConfiguration.cs`, new migration `AddClassMakeups`

**Intent**: Store the new columns and enforce "at most one active makeup per absence" in the
database.

**Contract**:
- A self-FK `MakeupForBookingId → Bookings.Id` with `Restrict`.
- A filtered unique index `IX_Bookings_MakeupForBookingId_Active` on `MakeupForBookingId` with
  `[Status] = 0 AND [MakeupForBookingId] IS NOT NULL`, following `BookingConfiguration.cs:76-79`.
- The migration adds nullable columns only, with no backfill. `Down` drops the index, the FK and the
  columns.
- `MakeupClosedAt` needs no index, because nothing in this change filters on it alone.

#### 3. The consumption rule

**File**: `src/Infrastructure/Scheduling/EntryConsumption.cs`

**Intent**: A makeup booking never consumes. Of the attendance values, only legacy `Absent` returns
the entry.

**Contract**:
`Status == Active && Class.Status != Cancelled && MakeupForBookingId == null && Attendance != Absent`.
Keep NULL attendance rows. Update the doc to list the readings: reserved, spent (present, makeup,
forfeited, or unrecorded), returned (legacy absent, released, cancelled class), and free (a makeup
booking). The four call sites need no edits, but each site's test must still pass.

#### 4. Recording attendance

**File**: `src/Application/Scheduling/RecordAttendance.cs`, `BookingFailure.cs`, `src/Infrastructure/Scheduling/BookingQuery.cs`, `ClassBooking.cs`

**Intent**: Accept the three outcomes and restrict what a makeup booking may be marked. Refuse to
re-mark an absence that already has a planned or attended makeup. Generalise the re-spend check.

**Contract**:
- **Body.** `AttendanceRequest.Attendance` takes `"present" | "makeup" | "forfeited"`. Anything else
  (including `"absent"`) is a `400` keyed `attendance`.
- **Makeup booking.** If the booking has `MakeupForBookingId != null`, `"makeup"` → 409
  `makeup_not_allowed`.
- **An absence that already has a makeup.** If the old value is `Makeup` and a linked `Active`
  makeup booking on a non-cancelled class exists, any change → 409 `makeup_booked`.
- **Re-spend.** It applies when the old value is legacy `Absent` and the new one is anything. The
  existing `no_entries_left` check and the pass stamp rotation apply; the booking must be
  non-makeup and carry a pass.
- **Read side.** `ClassBooking` gains `bool IsMakeup`. The roster projection maps attendance to:
  - `"present"`;
  - `"makeup"`;
  - `"forfeited"`;
  - `"absent"` (legacy);
  - `null`.
- **History outcomes** (`BookingQuery.cs:79-108`):
  - `"present" | "makeup" | "forfeited" | "absent" | "unrecorded" | "cancelled"`;
  - `MyAttendanceEntry` gains `bool IsMakeup`;
  - `AttendanceCounts.Absent` counts legacy, makeup and forfeited.

### Success Criteria:

#### Automated Verification:

- The migration applies to the local DB and reverts cleanly (`dotnet ef database update` to it and back).
- `dotnet build po-prostu-silka.slnx` is warning-free.
- `dotnet test`: the new and updated cases in `AttendanceEndpointTests` pass.
  - **Rewritten:** `A_booking_marked_absent_returns_its_entry` becomes "a legacy absent row (seeded
    as 1) still returns its entry". The member list, `/passes/mine` and the gate all agree.
  - **New:** "makeup" and "forfeited" each keep the entry spent on all four read sites.
  - **New:** legacy absent → makeup re-spends; it is refused with `no_entries_left` when the pass is
    full.
  - **New:** `"absent"` in the body → 400.
  - **New:** roster and history report the new strings.

#### Manual Verification:

- After the migration, an existing member with a legacy absent row shows the same entries left as
  before the deploy.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: The makeup API

### Overview

A list, eligible classes, booking, release, close and reopen for staff, and a summary for the
member. Everything reads one status predicate.

### Changes Required:

#### 1. The status predicate and query

**File**: new `src/Infrastructure/Scheduling/MakeupStatus.cs`, new `src/Infrastructure/Scheduling/MakeupQuery.cs`, new `src/Application/Scheduling/IMakeupQuery.cs`

**Intent**: One definition of an item's status, following the "Critical Implementation Details"
order. Club-local today is computed as in `MemberQuery.ClubToday()`. The list, the member summary
and the booking gate all read it.

**Contract**:
- **`GetItemsAsync(bool includeClosed, int page, ct)`** pages the items, ordered by deadline and then
  by absence start.
- **`MakeupItem` DTO.** Fields:
  - `AbsenceBookingId`, `MemberId`, `DisplayName`;
  - `ClassName`, `AbsenceStartsAt`, `Instructor`;
  - `DateOnly Deadline`, `string Status` (`open | planned | made_up | not_made_up`), `bool ClosedByHand`;
  - `MakeupClass?` with `ClassId`, `Name`, `StartsAt`, `Instructor`.
- **Staff are excluded** with `StaffPredicate.IsNotStaff`, as defence in depth.
- **`GetOpenForMemberAsync(memberId)`** returns `{ Count, NearestDeadline? }`, counting `open` items
  only.

#### 2. Eligible classes

**File**: new `src/Application/Scheduling/GetMakeupClasses.cs`

**Intent**: The picker offers only classes the booking would accept. A refusal should therefore be a
lost race, not a normal outcome.

**Contract**: `GET /api/makeups/{absenceBookingId}/classes` returns classes that:
- are `Scheduled` and start after now;
- start on a club-local date ≤ the deadline;
- have active bookings < capacity;
- do not already have the member booked;
- fall on a club-local date covered by a valid pass of the member.

It returns 404 for an unknown absence and 409 `makeup_not_open` when the item is not `open`. Results
are ordered by `StartsAt`, from any instructor. The DTO reuses `ScheduledClass`'s shape where possible.

#### 3. Booking and releasing a makeup

**File**: `src/Application/Scheduling/BookingProtocol.cs`, new `src/Application/Scheduling/BookMakeup.cs`, new `src/Application/Scheduling/ReleaseMakeup.cs`

**Intent**: Book through the same race-safe loop, without the entry check, linked to the absence.
Authorization is by persona only (TrainerOrAdmin, with no `MayActOn`), because a trainer may book a
makeup into any class.

**Contract**:
- **`TryBookAsync` gains an optional `Guid? makeupForBookingId` parameter.** When it is set:
  - it skips the entry check and the pass stamp rotation;
  - it still requires a covering pass (`no_valid_pass`) and stamps `MembershipPassId`;
  - it sets `MakeupForBookingId`.
- **Inside the loop, before adding,** it re-reads the absence and checks that its status is `open`,
  using the predicate's rules in memory on tracked rows. If not → `makeup_not_open`. A class date
  past the deadline → `makeup_deadline_passed`.
- **Superseded makeup.** An existing active makeup row on a cancelled class is set `Cancelled` in the
  same save. The filtered unique index catches two concurrent makeup bookings as a retry.
- **`POST /api/makeups/{absenceBookingId}/booking`** with `{ classId }`:
  - the member must be active (`member_blocked`) and not staff (`member_is_staff`);
  - the absence must exist and be `Makeup` (404 otherwise);
  - returns the updated `MakeupItem`.
- **`DELETE /api/makeups/{absenceBookingId}/booking`** releases the linked makeup:
  - allowed before its class starts, otherwise `class_started`;
  - rotates the class stamp, as `ReleaseBooking` does.

#### 4. Close and reopen

**File**: new `src/Application/Scheduling/CloseMakeup.cs`

**Intent**: Staff close an item they know won't be made up, and can undo it while the deadline
allows.

**Contract**:
- **`PUT /api/makeups/{id}/closed`** sets `MakeupClosedAt` and `MakeupClosedBy`. Only an `open` item
  can be closed; anything else → `makeup_not_open`.
- **`DELETE /api/makeups/{id}/closed`** clears them. It requires a hand-closed item with club-local
  today ≤ deadline; otherwise → `makeup_not_reopenable`.
- Both are idempotent on the same state.

#### 5. Endpoints and the failure union

**File**: new `src/Api/Endpoints/Scheduling/MakeupEndpoints.cs`, new `src/Application/Scheduling/MakeupFailure.cs`, `Program.cs` registration, `EndpointAuthorizationTests.cs`

**Intent**: Staff routes go under `/api/makeups` (TrainerOrAdmin). The member route
`GET /api/makeups/mine` goes under MemberOnly. Every refusal is a 409 with a `MakeupFailure` reason.

**Contract**:
- `MakeupFailure` reasons:
  - `makeup_not_open`, `makeup_deadline_passed`, `makeup_not_reopenable`;
  - `class_full`, `class_started`, `class_cancelled`, `already_booked`;
  - `no_valid_pass`, `member_blocked`, `member_is_staff`, `conflict`.
- `BookingFailure` gains `makeup_booked` and `makeup_not_allowed`, from Phase 1.
- Add the new routes to the authorization inventories.

### Success Criteria:

#### Automated Verification:

- `dotnet test`: a new `MakeupEndpointTests` passes, covering:
  - **listing and status:**
    - an item's status in each of the four states;
    - deadline boundaries at day 30 and 31 around the Warsaw midnight;
    - a "planned" item stays planned past the deadline;
    - the closed filter;
  - **booking and the entry rule:**
    - a makeup into another trainer's class by a trainer succeeds;
    - a makeup leaves entries left unchanged;
    - the eligible-classes list excludes a full class, a past-deadline class, a class on a day no
      pass covers, and a class the member is already in;
    - a renewed pass covers a makeup after the original pass expired;
  - **refusals and corrections:**
    - a second makeup → `makeup_not_open`;
    - concurrent makeup bookings → exactly one;
    - the makeup class cancelled → the item reopens and can be re-booked;
    - releasing reopens it;
    - re-marking the absence with a planned makeup → `makeup_booked`;
    - a makeup booking marked `"makeup"` → `makeup_not_allowed`;
  - **outcomes and close/reopen:**
    - makeup marked present → `made_up`;
    - makeup marked forfeited → `not_made_up`;
    - close and reopen within the deadline;
    - reopen after the deadline → `makeup_not_reopenable`;
  - **access:**
    - member `mine` counts only open items;
    - a member is refused on the staff routes and staff on `mine`.
- `EndpointAuthorizationTests` passes with the new routes listed.

#### Manual Verification:

- On seeded data, `GET /api/makeups` returns plausible rows after a few absences are marked "makeup" via the API.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Roster and member screens

### Overview

The SPA learns the new outcomes: in the roster, in history and on Moje zajęcia.

### Changes Required:

#### 1. Models, service and failure tables

**File**: `src/app/src/app/core/scheduling/booking.models.ts`, `booking-failure.ts`, new `core/scheduling/makeup.models.ts` / `makeup.service.ts` / `makeup-failure.ts`, `core/http/failure-contract.spec.ts`

**Intent**: Bring the types in line with the API and register the words. Each surface makes exactly
one decision about where a refusal goes (S-19).

**Contract**:
- **Booking models:**
  - `Attendance = 'present' | 'makeup' | 'forfeited'`;
  - `RecordedAttendance = Attendance | 'absent'` for reads;
  - `ClassBooking.isMakeup`;
  - `AttendanceOutcome` gains `'makeup' | 'forfeited'`;
  - `MyAttendanceEntry.isMakeup`.
- **`BookingFailure`** gains `makeup_booked` and `makeup_not_allowed`.
- **`MakeupFailure`** is a new union, its table, and a `UNIONS` entry. The count goes from eighteen
  to nineteen (test name too).
- **`MakeupService`:** `list`, `classes`, `book`, `release`, `close`, `reopen`, `mine`.

#### 2. Roster overlay

**File**: `src/app/src/app/features/class-bookings/class-bookings-overlay.{ts,html,scss,spec.ts}`

**Intent**: Three segmented options instead of two, with the same `aria-pressed` pattern and the
same row-error outlet.

**Contract**:
- **Buttons:**
  - Był: icon `present`;
  - Odrobi: icon `repeat`;
  - Przepada: icon `absent`.
  - Each has a hidden word and a `title`, and the group label stays `Obecność: {name}`.
- **Makeup booking:** the Odrobi button is not rendered. The row shows an "Odrabianie" badge.
- **Legacy `absent`:** no button is pressed, and the foot reads "Nieobecny – wejście zwrócone".
- **Tally:** "Obecni: X · Odrobią: Y · Przepada: Z · Nieoznaczeni: W". "Wszyscy obecni" is
  unchanged.
- **Width:** the toggle must fit a phone row. Verify at 360 px, and drop the hidden-word layout if
  it does not fit.

#### 3. Member: history and Moje zajęcia

**File**: `features/my-classes/attendance-history.{ts,html,scss}`, `features/my-classes/my-classes.{ts,html}`, `shared/booked-class` (badge)

**Intent**: The member reads the new outcomes and sees they have something to make up.

**Contract**:
- **`OUTCOME_LABELS`:**
  - present → "Obecny";
  - makeup → "Do odrobienia" (icon `repeat`);
  - forfeited → "Nieobecny" (icon `absent`);
  - legacy absent → "Nieobecny" (icon `absent`).
- **Month tally:** `counted` = present + all three absent kinds.
- **Makeup rows:** a makeup history row and an upcoming makeup booking read "Odrabianie".
- **Hero hint:** `GET /api/makeups/mine` adds "Do odrobienia: N · do {date}" when `count > 0`,
  behind its own load fence. If that request fails, the hint is skipped; the screen still shows.

**Adapted during implementation.** The upcoming list needed `isMakeup` on `MyBooking`, which no
backend phase had added. It was added to `src/Application/Scheduling/MyBooking.cs` and its
`BookingQuery` projection in this phase. The chip renders through a new `makeup` input on
`shared/class-date/booked-class.ts`, shared by both tabs.

### Success Criteria:

#### Automated Verification:

- `npm test` passes, with updated and new specs:
  - **roster:** three options; a makeup row without Odrobi; the legacy row's text; the tally; the
    refusal sentence for `makeup_booked`;
  - **history:** new labels;
  - **Moje zajęcia:** hint shown or absent depending on the count, and no request from staff;
  - **failure contract:** nineteen unions.
- `npm run quality:check` passes.

#### Manual Verification:

- Roster on a phone (360 px), a tablet and a desktop: the three buttons fit and read clearly.
- A member with an open makeup sees the hint, and the history labels read naturally in Polish.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 4: The "Odrabianie" screen

### Overview

The staff working list, the class picker and the item actions.

### Changes Required:

#### 1. Route and navigation

**File**: `src/app/src/app/app.routes.ts`, `core/layout/navigation.ts`, `app.routes.spec.ts`

**Intent**: Give both staff personas a lazy tab screen, guarded identically in the menu, the guard
and the API (S-25).

**Contract**:
- **Route:** `path: 'makeups'`, `title: 'Odrabianie'`, `data: { level: 'tab' }`,
  `canActivate: [authGuard, staffGuard]`, and `loadComponent`.
- **`MAKEUPS` link** with icon `repeat`, placed in:
  - the trainer's header and bar (the bar's 4th slot, before More);
  - the admin's header;
  - the admin's `more`.

#### 2. The list screen

**File**: new `src/app/src/app/features/makeups/makeups.{ts,html,scss,spec.ts}`

**Intent**: A working list in the style of `features/trainer/members/trainer-members.*`: URL state,
one load fence, the presentational kit only.

**Contract**:
- **Query params:** `zamkniete=1` and `page`. The checkbox "Pokaż zamknięte" uses `app-checkbox` in
  `app-field`, with `replaceUrl`.
- **Rows:** `app-list` / `li appRow class="card"`:
  - `row-name` holds the member;
  - `row-meta` holds class name, absence date and instructor;
  - a deadline line reads "do {date}" or "za N dni";
  - a status badge;
  - for `planned`, the makeup class and date.
- **Row actions** go in `slot="actions"`, with errors shown as toasts (S-19 outlet 3):
  - `open` → "Zapisz na odrabianie" and "Zamknij";
  - `planned` → "Zwolnij" (before start);
  - hand-closed within the deadline → "Otwórz ponownie".
- **Empty states:** `app-empty icon="repeat"`, with a different sentence for the open-only view and
  the closed view.

#### 3. The class picker overlay

**File**: new `src/app/src/app/features/makeups/makeup-class-picker.{ts,html,scss,spec.ts}`

**Intent**: Choose one eligible class. The overlay follows the standard shape: `app-overlay-head`,
then `.overlay-body`, then `.overlay-actions`, with `useOverlayFocus`.

**Contract**:
- **Loading:** it loads `GET /api/makeups/{id}/classes` and groups the classes by day with
  `groupByMonth` / class-date helpers.
- **Rows:** each row shows time, class name, instructor and free spots.
- **Selection:** selecting a class and confirming calls `book`.
  - On success the overlay closes, a toast confirms and the row refreshes.
  - A refusal stays in the overlay as an `.alert` banner (outlet 2), and the list reloads.
- **Empty:** `app-empty compact` "Brak zajęć w terminie odrabiania".

**Adapted during implementation.** One tap per class instead of select-then-confirm. Each row books
with its own "Zapisz" button, and the overlay has no `.overlay-actions` foot, because there is
nothing to confirm (AGENTS.md "omitted when there is nothing to confirm"). A refusal still lands as
the in-overlay `.alert` banner, and the list reloads.

### Success Criteria:

#### Automated Verification:

- `npm test` passes:
  - `makeups.spec.ts` covers the URL state, actions per status and the empty states;
  - `makeup-class-picker.spec.ts` covers the selection, the refusal banner and the empty state;
  - `app.routes.spec.ts` checks the identity and the menu label against the title;
  - the navigation specs check the trainer bar and the admin header.
- `npm run quality:check` passes, including the presentational-kit lint rule.
- `npm run build`: the eager bundle grows by less than 1 kB, and the figure is recorded in AGENTS.md.

#### Manual Verification:

- **Trainer:** books a makeup into another trainer's class from the list. That class's trainer sees
  the member in their roster with the "Odrabianie" badge, and marks them present. The item becomes
  "odrobił".
- **Admin:** closes an item and reopens it. Releasing a planned makeup reopens the item.
- **Layout:** the list and picker at phone, tablet and desktop widths; back closes the picker.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 5: E2E and documentation

### Overview

Replace the E2E spec that encoded the old rule, and record the slice.

### Changes Required:

#### 1. E2E

**File**: remove `src/app/e2e/absence-returns-karnet-entry.spec.ts`, new `src/app/e2e/makeup-absence-books-a-free-class.spec.ts`; extend `e2e/support/club.ts` / `schedule.ts` only through builders

**Intent**: One spec for the risk "an Odrobi absence keeps the entry spent, and the makeup costs
nothing". It follows `src/app/e2e/CLAUDE.md`: PHONE, role locators, `club` builders, unique names.

**Contract**:
1. **Arrange:** a member with a 5-entry karnet, one started class and one future class (another
   trainer).
2. **Mark Odrobi:** the trainer marks Odrobi in the roster. The member's Start still reads `4 z 5`.
3. **Book the makeup:** the trainer opens Odrabianie and books the future class from the picker.
4. **Member view:** the member sees "Odrabianie" on Moje zajęcia, and the karnet still reads `4 z 5`.

#### 2. Docs

**File**: `context/foundation/roadmap.md`, `context/testing/04-schedule-bookings-attendance.md`, `AGENTS.md`

**Intent**: Record the slice and the rule change, and keep manual cases true.

**Contract**:
- **Roadmap:** add S-36 `class-makeups`, outside any milestone, in the At a glance table, the slice
  body and the backlog row. Note on S-27 that AT-03's "a recorded absence gives the entry back" is
  amended by S-36.
- **Manual cases:** revise ATT-01/02 and add MAKEUP-01… for each manual check above.
- **AGENTS.md:** the bundle figure, and one line under the S-16 hard rule: "a makeup booking consumes
  no entry but still needs a valid karnet".

### Success Criteria:

#### Automated Verification:

- `npx playwright test` passes locally (the pre-push hook), including the new spec.
- `dotnet test` and `npm test` still pass.

#### Manual Verification:

- The new spec has been reviewed against the five anti-patterns (`/10x-e2e`).
- The roadmap and manual cases read correctly.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Testing Strategy

### Integration Tests:

- **Where:** `AttendanceEndpointTests` (updated) and the new `MakeupEndpointTests`. Seed legacy and
  edge rows through the DbContext, and move class times with `ExecuteUpdateAsync`, as
  `StartAsync`/`MarkAsync` already do.
- **Restate the rule by hand:** `ConsumingForPassAsync` (test helper, 694-703) must restate the NEW
  rule. Keep it independent of `EntryConsumption`.
- **Deadline boundaries:** use club-local dates around Warsaw midnight, as `AdminBookingEndpointTests`
  (459-597) does.

### Unit Tests (SPA):

- Overlay, history, my-classes, makeups and picker specs, plus the failure contract and the routes.

### Manual Testing Steps:

1. Mark three members Był / Odrobi / Przepada and check entries left for each.
2. From Odrabianie, as a trainer, book into another trainer's class. As that trainer, mark the member
   present, and check that the item becomes "odrobił".
3. Cancel a makeup's class as admin, and check that the item becomes "do odrobienia" and can be
   re-booked.
4. Close an item and reopen it. Then move the clock past the deadline (seed an old absence) and
   check that reopening is refused.

## Performance Considerations

- **Status in the query:** the status predicate runs as correlated subqueries per absence row.
  Absence rows with `Attendance = Makeup` are few. Add an index on `(Attendance)` only if the query
  plan shows a scan of all bookings; measure first.
- **Lazy screen:** the screen does not touch the eager bundle, except the nav link and maybe an icon
  (`repeat` already exists).

## Migration Notes

- **Additive only:** nullable columns, a self-FK and a filtered unique index. There is no backfill.
- **Legacy rows:** existing `Absent = 1` rows keep returning their entry.
- **Rollback:** redeploying the previous artifact leaves the new columns unused. A row marked
  `Makeup = 2` or `Forfeited = 3` would read as "not absent" to the old expression, so it counts as
  spent, which is the same balance. The old SPA would show it as unmarked. That is acceptable for
  one release.

## References

- Change and decisions: `context/changes/class-makeups/change.md`
- Derived-predicate precedent: `src/Infrastructure/Members/ExpiringPassPredicate.cs`, `src/Application/Members/GetExpiringPasses.cs`
- Booking loop: `src/Application/Scheduling/BookingProtocol.cs:63-193`
- Attendance write: `src/Application/Scheduling/RecordAttendance.cs`
- Roster overlay: `src/app/src/app/features/class-bookings/class-bookings-overlay.html:499-549`
- Staff list precedent: `src/app/src/app/features/trainer/members/trainer-members.*`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Attendance outcomes and the entry rule

#### Automated

- [x] 1.1 Migration applies and reverts cleanly — 620711f
- [x] 1.2 Solution builds warning-free — 620711f
- [x] 1.3 AttendanceEndpointTests pass with legacy, makeup, forfeited and re-spend cases — 620711f

#### Manual

- [ ] 1.4 Legacy absent member shows unchanged entries after migration

### Phase 2: The makeup API

#### Automated

- [x] 2.1 MakeupEndpointTests pass — 5532dd4
- [x] 2.2 EndpointAuthorizationTests pass with the new routes — 5532dd4

#### Manual

- [ ] 2.3 Makeup list returns plausible rows on seeded data

### Phase 3: Roster and member screens

#### Automated

- [x] 3.1 SPA specs pass (roster, history, my-classes, nineteen failure unions) — fee34a8
- [x] 3.2 quality:check passes — fee34a8

#### Manual

- [ ] 3.3 Roster toggle fits and reads at phone, tablet and desktop
- [ ] 3.4 Member sees the makeup hint and new history labels

### Phase 4: The "Odrabianie" screen

#### Automated

- [x] 4.1 SPA specs pass (makeups, picker, routes, navigation)
- [x] 4.2 quality:check passes
- [x] 4.3 Eager bundle grows under 1 kB and is recorded in AGENTS.md

#### Manual

- [ ] 4.4 Trainer books a makeup into another trainer's class; it ends as "odrobił"
- [ ] 4.5 Close, reopen and release behave as specified
- [ ] 4.6 List and picker read well at all widths; back closes the picker

### Phase 5: E2E and documentation

#### Automated

- [ ] 5.1 Playwright suite passes locally with the new makeup spec
- [ ] 5.2 dotnet test and npm test still pass

#### Manual

- [ ] 5.3 Spec reviewed against the five anti-patterns
- [ ] 5.4 Roadmap and manual cases read correctly
