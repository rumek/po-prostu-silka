# Group Fixed Roster Implementation Plan

## Overview

A group keeps a fixed list of members ("stały skład"), entered once, and its members are booked into
the group's upcoming classes automatically: when a class of the group is created or duplicated, when
a person joins the roster, when a member's karnet is issued or its dates change, and on demand
("Uzupełnij zapisy"). Every automatic booking goes through the existing no-overbooking protocol with
its karnet gate, and a refused one is skipped and reported rather than failing the rest. Leaving the
roster releases the member's future bookings in the group. Roadmap S-37 (`group-fixed-roster`),
requested by the club from `xlsx/treningi.xlsx` ("STAŁA LISTA OSÓB - wpisujesz raz").

## Current State Analysis

- `ClassGroup` (`src/Domain/Scheduling/ClassGroup.cs`) is a definition: name, description, default
  duration and capacity, active flag. It has no members, no time and no trainer. Its API is admin-only
  (`src/Api/Endpoints/Scheduling/ClassGroupEndpoints.cs:36`, `/api/admin/class-groups`).
- Staff booking is `BookForMember` (`src/Application/Scheduling/BookForMember.cs`): ownership via
  `BookingAuthorization.MayActOn` (admin, or the class's instructor), then member-blocked and
  `member_is_staff` checks, then `BookingProtocol.TryBookAsync`
  (`src/Application/Scheduling/BookingProtocol.cs:64`). The protocol re-reads the class, checks
  started/cancelled/already-booked/capacity, finds a covering karnet for the class's club-local date,
  checks entries, inserts, rotates the class and pass stamps and saves; on a lost race it calls
  `unitOfWork.DiscardChanges()` and retries up to `MaxAttempts`. It returns an `IResult`, so a batch
  caller cannot read the refusal reason without unwrapping HTTP results.
- `DuplicateClass` (`src/Application/Scheduling/DuplicateClass.cs`) copies a class up to 8 weeks in one
  save and reports skipped weeks in `DuplicateResult`; it copies no bookings. `CreateClass` saves one
  class and returns the `ScheduledClass` DTO with `bookedCount: 0`.
- `IssuePass` (`src/Application/Members/IssuePass.cs`) and `UpdatePass` are admin-only
  (`/api/admin/members/{memberId}/passes`) and know nothing about classes.
- `ReleaseBooking` (`src/Application/Scheduling/ReleaseBooking.cs`) releases one booking before the
  start: status Cancelled, class stamp rotated, `BookingProtocol.ReturnEntryAsync` rotates the pass.
  Makeup bookings carry `Booking.MakeupForBookingId` (S-36).
- SPA: the admin group list and form are `features/admin/class-groups/`; the class roster overlay
  `features/class-bookings/class-bookings-overlay` is shared by admin and trainer and searches members
  through `core/scheduling/booking-candidates.ts` (admin list vs trainer list, which excludes staff).
  The trainer's bottom bar is full since S-36; `navigationFor` gives the trainer an empty `more` list
  (`core/layout/navigation.ts:95`). Refusal words for bookings live in
  `core/scheduling/booking-failure.ts`.

## Desired End State

- An admin opens a group from "Grupy" and sees "Skład": up to the group's default capacity of members,
  each with how many of the group's upcoming classes they are booked into and, where a class lacks
  their booking, which class and why ("brak karnetu ważnego w dniu zajęć", "brak wolnych miejsc", …).
  Adding a person books them into every upcoming class of the group they can be booked into; removing
  one releases their future non-makeup bookings in the group; "Uzupełnij zapisy" retries every gap.
- A trainer reaches "Grupy" from Więcej, sees the groups whose upcoming classes they instruct, and
  manages those rosters on the same screen. A group they do not instruct is a 403 from the API.
- Creating or duplicating a class of a group with a roster books the roster in; issuing or editing a
  karnet books its holder into their groups' upcoming classes inside its validity. Each of these
  actions answers with a report, and the screen shows a dismissible `.notice` panel listing who was
  skipped, for which class and why.
- No path can overbook a class or spend an entry twice: every automatic booking is one
  `BookingProtocol` transaction.

Verify with `dotnet test`, `npm test`, `npm run quality:check`, the new E2E spec, and the manual cases
in `context/testing/04-schedule-bookings-attendance.md`.

### Key Discoveries:

- `BookingProtocol.TryBookAsync` discards the WHOLE tracked graph on a lost race
  (`BookingProtocol.cs:229`), so anything a caller staged but did not save would be lost. Roster
  booking must run only after the triggering write (class, karnet, roster row) has committed.
- The karnet gate dates by the class's club-local date (`BookingProtocol.cs:122`); the karnet hook must
  select classes the same way (`ClubTime.ToClubLocal`), or a 21:00 class on a karnet's last day is
  missed.
- `BookForMember` keeps the blocked and staff checks OUT of the protocol on purpose
  (`BookForMember.cs:67-79`); the roster batch repeats them per member, outside the protocol.
- The trainer's member search (`trainerCandidateSearch`) already excludes staff, which is the set a
  roster may hold.
- `ClassGroup` has no concurrency token because only one admin was assumed
  (`UpdateClassGroup.cs:62-67`); trainers now write rosters too — see "What We're NOT Doing" on the cap.

## What We're NOT Doing

- No series entity and no change to the 8-week duplication bound (decided 2026-10-03). Weekly
  duplication still creates the classes.
- No fixed trainer or fixed time on a group; "a group the trainer instructs" is derived from classes.
- No notification to the member and no new member-facing screen or label; roster bookings appear in
  Moje zajęcia like any staff booking.
- No marker distinguishing roster-made bookings from manual ones; removal releases every future
  non-makeup booking of the member in the group.
- No hard guarantee on the roster size: the cap (group default capacity) is checked on add without a
  lock, so two simultaneous adds can exceed it by one. The class capacity — the guarantee that matters —
  is enforced by the protocol regardless. A duplicate member in one roster IS prevented, by a unique
  index.
- Lowering a group's default capacity below its roster size is not refused; the surplus just stops
  fitting and is reported as `class_full` where it no longer fits.
- Blocking a member does not remove them from rosters (their bookings are cancelled by the existing
  cascade, and later automatic bookings report `member_blocked`). Granting Trainer to a roster member
  does not remove them either; their automatic bookings report `member_is_staff`.
- No karnet hook on revoking a karnet (revoke is already refused while bookings use it).

## Implementation Approach

One Application service, `RosterBooking`, owns "book these members into these classes and collect what
was refused", built on a reason-returning core of `BookingProtocol`. Every trigger — roster add,
"Uzupełnij", create, duplicate, karnet issue/edit — commits its own write first and then calls the
service, which runs one protocol transaction per (member, class) pair. The roster read derives gaps
on the fly rather than storing them, so the screen is always current and the reports never have to be
persisted. Authorization for rosters is a new resource check beside `BookingAuthorization`: admin, or
the instructor of at least one upcoming, non-cancelled class of the group — the same check backs the
trainer's group list, so list and access cannot drift.

## Critical Implementation Details

- **State sequencing.** Every trigger saves its own entity BEFORE calling `RosterBooking`, and projects
  its response from values it already holds, never from tracked entities read before the batch: a
  protocol retry calls `DiscardChanges()`, which detaches them. `CreateClass`'s `bookedCount` comes from
  the report, not from the constructed entity.
- **One transaction per booking.** Do not batch several inserts into one `SaveChanges`; the class and
  pass stamps make each booking its own optimistic lock, and a partial batch must leave every booking
  it did make valid. Removal is the exception: it cancels several bookings in one save, rotating every
  touched class stamp and returning each entry, and retries the whole removal on a conflict, as
  `ReleaseBooking` does for one.
- **What counts as reportable.** `already_booked` is never reported (it is the idempotent case
  "Uzupełnij" relies on). `class_started`/`class_cancelled` cannot arise for the classes selected
  ("upcoming" = `StartsAt > now` and `Status == Scheduled`) except by a race, and are reported if they
  do.
- **User experience spec.** The report panel groups skips by member and reason, with the class dates:
  "Anna Kowalska — brak karnetu ważnego w dniu zajęć: 4.11, 11.11, 18.11". Words come from
  `bookingFailureMessage`'s reasons; the panel adds no sentence of its own beyond its heading.

## Phase 1: The roster in the API

### Overview

The roster table, the batch booking service, and the roster routes with their authorization.

### Changes Required:

#### 1. Roster entity and persistence

**File**: `src/Domain/Scheduling/GroupRosterEntry.cs`,
`src/Infrastructure/Persistence/Configurations/GroupRosterEntryConfiguration.cs`, new migration
`AddGroupRoster`

**Intent**: Record that a member belongs to a group's fixed roster.

**Contract**: `GroupRosterEntry { Guid Id; Guid ClassGroupId; Guid MemberId; DateTimeOffset AddedAt;
string? AddedBy }`, table `GroupRosterEntries`, FKs to `ClassGroups` and `Members` (restrict), unique
index `(ClassGroupId, MemberId)` named `IX_GroupRosterEntries_Group_Member`. Migration with a working
`Down` (drops the table). The restrict FKs block `TestDataSeeder`'s reset
(`src/Infrastructure/TestData/TestDataSeeder.cs`, the `ClassGroups`/`Members` `ExecuteDeleteAsync`
calls), so add `db.GroupRosterEntries.ExecuteDeleteAsync()` before them. Created with `dotnet ef migrations add AddGroupRoster --project
src/Infrastructure/po-prostu-silka.Infrastructure.csproj --startup-project
src/Api/po-prostu-silka.Api.csproj`.

#### 2. Roster store and query seams

**File**: `src/Application/Scheduling/IGroupRosterStore.cs`, `IGroupRosterQuery.cs`;
`src/Infrastructure/Scheduling/GroupRosterStore.cs`, `GroupRosterQuery.cs` (registered beside the
other scheduling stores)

**Intent**: Intention-revealing reads and writes, as the other scheduling seams are.

**Contract**: store — `Add`, `Remove`, `FindAsync(groupId, memberId)`, `CountAsync(groupId)`,
`MemberIdsAsync(groupId)`, `GroupIdsForMemberAsync(memberId)`, `UpcomingClassIdsAsync(groupId, asOf)`
(scheduled, `StartsAt > asOf`, ordered by start), `UpcomingClassIdsInRangeAsync(groupId, from, to,
asOf)` (club-local date range, inclusive), `IsInstructorOfUpcomingAsync(groupId, memberId, asOf)`;
query — the roster view (below) and the trainer's group list.

**Adapted during implementation.** `UpcomingClassIdsAsync` / `UpcomingClassIdsInRangeAsync` return
`RosterClass(Id, StartsAt, InstructorMemberId)` rather than bare ids (`UpcomingClassesAsync` /
`UpcomingClassesInRangeAsync`): the batch needs the instructor to apply `not_your_class` and the start
to report a skip, without reloading each class. The store also carries `ReleasableBookingsAsync(groupId,
memberId, asOf, instructorMemberId?)` — the tracked bookings a removal cancels.

#### 3. Reason-returning booking core

**File**: `src/Application/Scheduling/BookingProtocol.cs`

**Intent**: Let a batch caller learn why a booking was refused without unwrapping `IResult`, while the
existing route keeps its exact behaviour.

**Contract**: a new `TryBookCoreAsync(...)` returning a `BookingAttempt` (booked with the class entity
and the booked count, refused with a reason, or class not found). `TryBookAsync` becomes a thin mapper
from `BookingAttempt` to the current `IResult`s. The sequence inside the loop does not change.

#### 4. The batch service

**File**: `src/Application/Scheduling/RosterBooking.cs`, `RosterReport.cs`

**Intent**: Book a set of members into a set of classes, one protocol transaction each, and collect
refusals.

**Contract**: `BookAsync(IReadOnlyList<Guid> memberIds, IReadOnlyList<Guid> classIds, ...)` →
`RosterReport(int Booked, IReadOnlyList<RosterSkip> Skipped)`, with `RosterSkip(Guid MemberId, string
MemberName, Guid ClassId, DateTimeOffset StartsAt, string Reason)`. Per member, before any class:
missing → skipped silently (deleted between read and write), blocked → `member_blocked` for each class,
staff (`IMemberStore.IsStaffAsync`) → `member_is_staff` for each class. `already_booked` is dropped from
the report. Classes are processed in start order, so an exhausted karnet refuses the LATER classes.
`BookAsync` also takes `Guid? actingInstructorId` — null for an admin and for every automatic trigger
(create, duplicate, karnet); a trainer's member id on the roster routes. When set, a class whose
`InstructorMemberId` differs is not attempted and is reported `not_your_class`: the S-16 rule ("a
trainer books into the classes they personally instruct", `BookingAuthorization.MayActOn`) holds for
roster bookings too. Those classes stay gaps until an admin's "Uzupełnij" or the next automatic
trigger fills them.

**Adapted during implementation.** `RosterBooking` is a scoped class (registered in `Program.cs`), not a
static like `BookingProtocol`: four handlers in two bounded contexts call it, and seven dependencies per
call site buried what each did. It takes `IReadOnlyList<RosterClass>` rather than class ids (see §2) and
adds `BookRosterIntoAsync(groupId, classes)` for the create/duplicate hooks.

#### 5. Roster authorization

**File**: `src/Application/Scheduling/RosterAuthorization.cs`

**Intent**: The resource check for roster routes, beside `BookingAuthorization`.

**Contract**: `MayManageAsync(principal, groupId, store, timeProvider)` — true for Admin; for anyone
else, true when the caller's member id instructs at least one upcoming, non-cancelled class of the
group. An unknown group id is 404; a known group the trainer does not instruct is
`Results.Forbid()` (403). Revealing that a group id exists costs nothing — group names are on the
schedule — and it matches `BookingAuthorization.NotYourClass`'s reasoning.

#### 6. Roster handlers and routes

**File**: `src/Application/Scheduling/GetGroupRoster.cs`, `AddToRoster.cs`, `RemoveFromRoster.cs`,
`SyncRoster.cs`, `GetTrainerGroups.cs`, `RosterFailure.cs`; `src/Api/Endpoints/Scheduling/GroupRosterEndpoints.cs`
(mapped in `Program.cs`)

**Intent**: Read, add, remove, and re-sync a roster; list the trainer's groups.

**Contract**: group `/api/groups/{groupId:guid}/roster`, policy `TrainerOrAdmin`, every handler calls
`RosterAuthorization.MayManageAsync` first.
- `GET /` → `GroupRosterView { GroupId, Name, IsActive, Capacity (default), UpcomingClassCount,
  Members: [{ MemberId, DisplayName, HasAccount, AddedAt, BookedUpcoming, Gaps: [{ ClassId, StartsAt,
  Reason }] }] }`. A gap is an upcoming class of the group with no active booking for the member; its
  reason is evaluated read-only per class (blocked, staff, no covering karnet, no entries left, full),
  `not_your_class` when the caller is a trainer who does not instruct that class, or `bookable` when
  nothing would refuse it now.
- `POST /` body `{ memberId }` → 409 `RosterFailure` for `already_in_roster`, `roster_full`,
  `member_blocked`, `member_is_staff`, `inactive_class_group` (a deactivated group takes no new
  members; its existing roster still syncs, and the hooks still fill its remaining classes — FR-006
  keeps them); 404 for an unknown member. Saves the entry, then
  `RosterBooking` over the group's upcoming classes; answers `{ roster: GroupRosterView, report:
  RosterReport }`.
- `DELETE /{memberId:guid}` → removes the entry and, in the same save, cancels the member's ACTIVE
  bookings with `MakeupForBookingId == null` on the group's classes with `StartsAt > now`, rotating each
  class stamp and returning each entry (`BookingProtocol.ReturnEntryAsync`); retries on conflict like
  `ReleaseBooking`. 404 when not in the roster. Answers the refreshed view. When the caller is a
  trainer (not Admin), only bookings on classes they instruct are cancelled; the member's bookings on
  other trainers' classes of the group stay (S-16), and the view still lists them as booked.
- `POST /sync` → `RosterBooking` for all roster members × upcoming classes; answers `{ roster, report }`.
- `GET /api/trainer/groups` (policy `TrainerOrAdmin`, narrowed to groups whose upcoming classes the
  caller instructs) → `[{ Id, Name, RosterCount, Capacity, NextClassAt }]`.
- `ClassGroupSummary` gains `RosterCount` so the admin's group list can show it. **Adapted during
  implementation:** `ClassGroupProjection.ToDto` takes the count too, so `GetClassGroup`, `UpdateClassGroup`,
  `ActivateClassGroup` and `DeactivateClassGroup` inject `IGroupRosterStore` — otherwise their responses
  would report 0 and reset the list row the SPA patches from them.

#### 7. Integration tests

**File**: `tests/po-prostu-silka.Tests/GroupRosterEndpointTests.cs`, additions to
`EndpointAuthorizationTests.cs` / `PersonaAccessTests.cs`

**Intent**: Pin the rules where they can break.

**Contract**: add books every upcoming class; add of a member without a karnet adds them and reports
`no_valid_pass` per class; karnet with fewer entries than classes books the earliest and reports
`no_entries_left` for the later; `roster_full`, `already_in_roster`, `member_is_staff`,
`member_blocked`; remove releases future non-makeup bookings, returns their entries, keeps a makeup
booking and past/started classes; sync is idempotent (second run books 0, reports nothing new);
concurrent roster adds into a nearly full class never exceed capacity; trainer instructing an upcoming
class of the group may manage it, a trainer who does not gets 403, a member gets 403; trainer group
list contains exactly the groups the trainer may manage; a trainer's add/sync books only the classes
they instruct and reports the others `not_your_class`, and a trainer's remove leaves the member's
bookings on another trainer's class of the group intact.

### Success Criteria:

#### Automated Verification:

- Migration applies cleanly and reverts: `dotnet ef database update` then update to the previous
  migration, against the local Docker SQL Server
- Backend builds warning-free: `dotnet build po-prostu-silka.slnx`
- All tests pass, the new roster tests included: `dotnet test`

#### Manual Verification:

- Via `po-prostu-silka.http` or the Scalar/OpenAPI page: add a seeded member with a karnet to a group
  with upcoming classes and see their bookings on those classes' rosters

**Implementation Note**: After completing this phase and all automated verification passes, pause here
for manual confirmation from the human that the manual testing was successful before proceeding to the
next phase.

---

## Phase 2: Automatic bookings on classes and karnets

### Overview

Creating or duplicating a class and issuing or editing a karnet book the relevant roster members and
report what was skipped.

### Changes Required:

#### 1. Class creation and duplication

**File**: `src/Application/Scheduling/CreateClass.cs`, `DuplicateClass.cs`, `DuplicateResult.cs`

**Intent**: A new class of a group with a roster gets the roster booked in.

**Contract**: after the existing save, `RosterBooking.BookAsync(rosterMemberIds, [newClassIds])`.
`CreateClass` answers the existing `ScheduledClass` fields with `bookedCount = report.Booked` plus a new
top-level `roster: RosterReport` (existing fields unchanged, so current readers keep working).
`DuplicateResult` gains `Roster: RosterReport`. A group with an empty roster yields an empty report and
no extra queries beyond the roster read.

#### 2. Karnet issue and edit

**File**: `src/Application/Members/IssuePass.cs`, `UpdatePass.cs` (and their response shapes)

**Intent**: A renewed or newly issued karnet books its holder into their groups' upcoming classes inside
its validity, so a monthly karnet does not leave the following weeks empty.

**Contract**: after the existing save, for each group the member is in, the upcoming classes whose
club-local date lies in `[ValidFrom, ValidTo]`; `RosterBooking` over them. Responses keep their current
top-level fields and add `roster: RosterReport`. `UpdatePass` runs the hook only when `ValidFrom` or
`ValidTo` or `EntryCount` changed. `no_valid_pass` cannot be produced for these classes except by a race.
The pass view in the response is built AFTER the batch, with entries used = the pre-batch count
(`0` on issue, `used` on edit) `+ report.Booked`, so it does not report entries the hook just spent as
free.

#### 3. Integration tests

**File**: `tests/po-prostu-silka.Tests/GroupRosterEndpointTests.cs` (or a sibling
`GroupRosterTriggerTests.cs`)

**Contract**: create books the roster and reports a member without karnet; duplicate over 8 weeks with a
karnet covering 4 books 4 and reports 4 `no_valid_pass` per that member; issuing the next month's
karnet books the remaining classes; editing a karnet's `ValidTo` forward books the newly covered
classes; a class with capacity below the roster books up to capacity and reports `class_full`; existing
`ClassEndpointTests`/`MembershipPassEndpointTests` still pass unchanged.

### Success Criteria:

#### Automated Verification:

- Backend builds warning-free: `dotnet build po-prostu-silka.slnx`
- All tests pass: `dotnet test`

#### Manual Verification:

- Duplicating a seeded group class for 8 weeks shows the roster on every copy that a karnet covers

**Implementation Note**: After completing this phase and all automated verification passes, pause here
for manual confirmation from the human that the manual testing was successful before proceeding to the
next phase.

---

## Phase 3: Screens

### Overview

The shared roster screen, the admin's and trainer's entries to it, and the report panel on every screen
that triggers automatic bookings.

### Changes Required:

#### 1. Models, service and failure words

**File**: `src/app/src/app/core/scheduling/roster.models.ts`, `roster.service.ts`, `roster-failure.ts`;
`class.models.ts` (`roster` on the create and duplicate results), the karnet models

**Intent**: Mirror the API; give `RosterFailure` its own message table so
`core/http/failure-contract.spec.ts` passes.

**Contract**: `RosterReport`, `RosterSkip`, `GroupRosterView`, `TrainerGroup`; `RosterService` with
`get`, `add`, `remove`, `sync`, `trainerGroups`. Skip and gap reasons are read through
`bookingFailureMessage`; `not_your_class` is added to its table ("zajęcia prowadzi inny trener");
`bookable` gets its own phrase in the roster screen ("można zapisać — uzupełnij zapisy").

#### 2. The report panel

**File**: `src/app/src/app/shared/roster-report/roster-report.ts` (+ html/scss/spec)

**Intent**: The dismissible panel the user asked for: informational screen content, so `.notice`, with
an X (icon button, `aria-label="Zamknij"`).

**Contract**: `<app-roster-report [report] (dismissed)>`; renders nothing when `report.skipped` is
empty. Heading "Nie wszystkich zapisano"; one line per member and reason with the class dates, grouped
as in "Critical Implementation Details". `role="status"`. The success count goes in the screen's
existing success toast ("Zapisano 10 osób na zajęcia"), not in the panel.

#### 3. The roster screen

**File**: `src/app/src/app/features/groups/group-roster.ts` (+ html/scss/spec); routes in `app.routes.ts`

**Intent**: One screen for both personas: who is in the roster, what each is missing, add, remove,
"Uzupełnij zapisy".

**Contract**: two routes on the same component — `admin/class-groups/:id/roster` (admin guard, `level:
'child'`, `parent: '/admin/class-groups'`) and `trainer/groups/:id` (trainer guard, `level: 'child'`,
`parent: '/trainer/groups'`), both titled from the group name via `useScreenTitle`. The search source
is `adminCandidateSearch` or `trainerCandidateSearch` by persona. Kit only: `app-list`/`li[appRow]`
rows with `.row-meta` "zapisany na 6 z 8 zajęć" and each gap as a meta line, `app-field` for the search,
`app-loading`, `app-empty` (icon `group`, "Skład jest pusty — dopisz pierwszą osobę"). The add block
is disabled at capacity with the count "6/6". Remove is an inline confirmation in the row ("Usunąć ze
składu? Przyszłe zapisy w tej grupie zostaną zwolnione."), refusals go to a toast (outlet 3), a failed
load is screen state (outlet 4). Add and sync show `app-roster-report` above the list. Mobile-first,
checked at tablet and desk widths.

#### 4. Entries

**File**: `features/admin/class-groups/class-groups.html` (+ ts), `features/trainer/groups/trainer-groups.ts`
(+ html/scss/spec), `core/layout/navigation.ts`, `app.routes.ts`

**Intent**: Admin reaches the roster from the group card; the trainer gets "Grupy" in Więcej.

**Contract**: the group card gains a "Skład (n/cap)" secondary button. New route `trainer/groups`
(trainer guard, `level: 'tab'`, title "Grupy") listing `trainerGroups()` as `app-list` rows (name,
"skład 4/6", next class date) leading to `trainer/groups/:id`; empty state "Nie prowadzisz teraz zajęć
żadnej grupy". `navigationFor('trainer')` → `more: [TRAINER_GROUPS]`; header gains it too, as the
admin's header carries `CLASS_GROUPS`. `app.routes.spec.ts` stays green (title equals menu label).

#### 5. Report panel on the triggering screens

**File**: `features/admin/classes/classes.ts` (+ html), `features/admin/members/member-passes.ts`
(+ html)

**Intent**: Create, duplicate, issue and edit show what was skipped.

**Contract**: each keeps the last non-empty `RosterReport` in a signal, renders `app-roster-report`
above its content, clears it on dismiss or on the next action. The duplicate toast keeps its
skipped-weeks wording; roster skips go to the panel only.

**Adapted during implementation.** A class is created in two places, neither of them `classes.ts`: the
calendar's `class-create-overlay` (its `created` output now carries the `RosterReport`) and the
`class-form` route, which navigates to the list with the report in navigation state
(`shared/roster-report/roster-report-state.ts`); `classes.ts` reads it while that navigation builds the
screen. The duplicate toast keeps its wording and appends the roster's booked count when non-zero.

#### 6. Specs and bundle

**Contract**: specs for the panel (grouping, nothing when empty, dismiss), the roster screen (both
search sources, add/remove/sync, capacity disable, gap rendering), trainer groups list, navigation
(trainer `more`), classes and member-passes showing the panel. Measure `main` with `npm run build` and
record the figure in AGENTS.md's bundle paragraph (expected: only the trainer nav link and route entry
are eager).

### Success Criteria:

#### Automated Verification:

- Unit tests pass: `npm test` (from `src/app/`)
- Lint and format pass, kit rule included: `npm run quality:check`
- Production build passes under the 600 kB warning: `npm run build`

#### Manual Verification:

- Admin: group card → Skład → add, remove, Uzupełnij; panel lists a member without karnet
- Trainer: Więcej → Grupy shows only instructed groups; roster editable; a typed URL to another group
  shows the 403 screen state
- Duplicating a class shows the panel on the Zajęcia screen; issuing a karnet shows it on the karnet
  screen
- Phone, tablet and desk widths read well; no horizontal scroll

**Implementation Note**: After completing this phase and all automated verification passes, pause here
for manual confirmation from the human that the manual testing was successful before proceeding to the
next phase.

---

## Phase 4: E2E and documentation

### Overview

One browser-level proof of the whole path, and the docs that record the rule.

### Changes Required:

#### 1. E2E spec

**File**: `src/app/e2e/group-roster-books-duplicated-classes.spec.ts`; `src/app/e2e/support/club.ts`
(builders `createGroup` — `E2E <purpose> <suffix>`, deactivated on cleanup — and `addToRoster`;
`createClass` gains an optional `classGroupId`, and when it is given it neither creates a group nor
registers a group deactivation, since today it always creates its own)

**Intent**: Prove that a roster member ends up booked on duplicated classes and sees them.

**Contract**: arrange a unique group, an E2E member with a karnet, a class of that group (`createClass`
with the group); in the UI (admin, desk width) add the member to the roster on the roster screen,
duplicate the class for 2 weeks from the Zajęcia screen; assert the member's Moje zajęcia (member
context) lists all three classes. Provenance header (risk + seed), Polish accessible names, no
`waitForTimeout`. Cleanup follows the builder order: `createGroup` → `issuePass` → `createClass`. The
copies the UI duplication creates are not made by a builder, so the spec captures the duplicate
response (`waitForResponse`) and registers each copy's id with the same `removeClass` cleanup
(exposed on `Club`); otherwise every run leaves two booked, scheduled classes in future slots.

#### 2. Docs

**File**: `context/foundation/roadmap.md`, `context/testing/04-schedule-bookings-attendance.md`,
`AGENTS.md`

**Intent**: Record the slice and its rule.

**Contract**: roadmap S-37 status as the lifecycle sets it; manual cases ROSTER-01..06 (add, add without
karnet, remove keeps makeup, sync, duplicate report, karnet renewal) in the testing file; one AGENTS.md
hard-rule sentence: roster bookings are staff bookings made through `BookingProtocol`, one transaction
each, refusals reported never forced.

**Adapted during implementation.** No `addToRoster` builder: the spec adds the member through the roster
screen, which is the risk, and an unused builder would be dead code. Cleanup of the UI-made copies is
`club.removeGroupClassesAfterwards(groupId, except)` — the duplicate response carries no copy ids, so it
lists the group's classes after the test. The manual cases are ROSTER-01..08: 07 (trainer scope) and 08
(widths) cover Phase 3's manual items.

### Success Criteria:

#### Automated Verification:

- The new spec passes: `npx playwright test e2e/group-roster-books-duplicated-classes.spec.ts`
- The full local suite passes: `npm run e2e`

#### Manual Verification:

- ROSTER-01..06 walked through on the local app

**Implementation Note**: After completing this phase and all automated verification passes, pause here
for manual confirmation from the human that the manual testing was successful.

---

## Testing Strategy

### Unit Tests:

- `app-roster-report` grouping and empty state; roster screen states; navigation table for the trainer.

### Integration Tests:

- Batch booking skip reasons in start order; no overbooking under concurrent roster adds; removal keeps
  makeups and history and returns entries; sync idempotence; triggers (create, duplicate, issue, edit)
  and their reports; roster authorization for admin, instructing trainer, other trainer, member.

### Manual Testing Steps:

1. Admin creates group "Pon 18:00", adds 3 members (one without karnet), creates and duplicates a class
   for 4 weeks — the panel names the member without karnet.
2. Issue that member a karnet — the panel is empty and the roster screen shows no gaps.
3. Remove a member who has a makeup booked into the group — the makeup stays, other future bookings go.
4. Trainer instructing one upcoming class of the group edits its roster; another trainer cannot.

## Performance Considerations

A duplicate of 8 weeks with a roster of 6 is 48 protocol transactions, each a handful of seeks; fine at
club scale. The roster read evaluates gaps per (member, upcoming class) — bounded by roster size × the
classes scheduled ahead (≈ 6 × 8); load passes and entry counts once per member, not per class.

## Migration Notes

Additive table only; `Down` drops it. Existing groups start with empty rosters, so no behaviour changes
until someone fills one.

## References

- Roadmap: `context/foundation/roadmap.md` S-37
- Booking protocol: `src/Application/Scheduling/BookingProtocol.cs:64`
- Partial-success pattern: `src/Application/Scheduling/DuplicateClass.cs:36`
- Release with entry return: `src/Application/Scheduling/ReleaseBooking.cs:36`
- Prior change shape: `context/changes/class-makeups/plan.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: The roster in the API

#### Automated

- [x] 1.1 Migration applies cleanly and reverts — 0efcb73
- [x] 1.2 Backend builds warning-free — 0efcb73
- [x] 1.3 All tests pass, the new roster tests included — 0efcb73

#### Manual

- [ ] 1.4 Adding a seeded member to a group books them on its upcoming classes

### Phase 2: Automatic bookings on classes and karnets

#### Automated

- [x] 2.1 Backend builds warning-free — e6e5980
- [x] 2.2 All tests pass — e6e5980

#### Manual

- [ ] 2.3 Duplicating a group class for 8 weeks books the roster on covered copies

### Phase 3: Screens

#### Automated

- [x] 3.1 Unit tests pass — 6c4a4bc
- [x] 3.2 Lint and format pass, kit rule included — 6c4a4bc
- [x] 3.3 Production build passes under the 600 kB warning — 6c4a4bc

#### Manual

- [ ] 3.4 Admin roster flow with the report panel
- [ ] 3.5 Trainer Grupy list, roster editing and 403 on another group
- [ ] 3.6 Panel on Zajęcia after duplicate and on karnet screen after issue
- [ ] 3.7 Phone, tablet and desk widths read well

### Phase 4: E2E and documentation

#### Automated

- [x] 4.1 The new spec passes
- [x] 4.2 The full local suite passes

#### Manual

- [ ] 4.3 ROSTER-01..06 walked through on the local app
