---
date: 2026-09-23T14:44:40+02:00
researcher: Claude (with Karol Rumianowski)
git_commit: 8749e4491fc8298333c2992532c8075a4905e571
branch: main
repository: po-prostu-silka
topic: "S-27 class-attendance — staff mark attendance, the karnet counts attended classes, the member sees their history"
tags: [research, codebase, scheduling, booking, membership-pass, attendance, roster, my-classes]
status: complete
last_updated: 2026-09-23
last_updated_by: Claude
---

# Research: S-27 class-attendance

**Date**: 2026-09-23T14:44:40+02:00
**Researcher**: Claude (with Karol Rumianowski)
**Git Commit**: 8749e44
**Branch**: main
**Repository**: po-prostu-silka

## Research Question

What must change, and where, so that (AT-01) a trainer for the classes they instruct and an admin for
any class mark attendance per booked member, (AT-02) only on bookings, (AT-03) a karnet entry is
spent by attending rather than by booking, while entries left stays derived, and (AT-04/AT-05) a
member sees their own attendance history, presented for scanning on a phone? Scope anchors live in
the M-8 charter of `context/foundation/roadmap.md`.

## Summary

- **Attendance does not exist anywhere yet.**
  - `Booking` carries a lifecycle status (`Active` / `Cancelled`) and an optional `MembershipPassId`,
    and nothing else (`src/Domain/Scheduling/Booking.cs`, `BookingStatus.cs`).
  - The staff roster returns active bookings with no status field.
  - The member's `/api/bookings/mine` returns **upcoming classes only**. There is no read path for a
    member's past classes at all.

- **"Entries used" is one predicate in three places.** It is
  `MembershipPassId == pass && Status == Active`, with no date or class-status filter:
  - `BookingStore.CountActiveForPassAsync`, the write-path gate: `src/Infrastructure/Scheduling/BookingStore.cs:44-48`, used at `src/Application/Scheduling/BookingProtocol.cs:133-137`.
  - `MembershipPassQuery.GetForMemberAsync`, the admin's pass history: `src/Infrastructure/Members/MembershipPassQuery.cs:50-51`.
  - `MembershipPassQuery.FindCoveringAsync`, the member's `/api/passes/mine`: `:93-94`.

  AT-03 changes that predicate, and nothing else about the derivation. The count stays a count and no
  stored counter appears.

- **AT-03 can be met without breaking MP-06's overdraw guarantee.** The approach: a booking goes on
  RESERVING the entry, and only a recorded absence (or a cancelled class) gives it back. Unrecorded
  attendance therefore counts as spent. That is today's behaviour, so no backfill is needed and
  classes nobody marks behave exactly as now.

- **The recommended shape is an orthogonal, nullable attendance column on `Booking`,** not new
  `BookingStatus` values. Every existing `Status == Active` predicate (capacity, the unique index,
  roster, `mine`, block cascade) then stays correct untouched. Only the three entries-used sites
  change.

- **Three places must rotate `MembershipPass.ConcurrencyStamp` that do not today:**
  1. Marking a booking absent, which returns an entry.
  2. Correcting absent → present, which re-spends one, so it must pass the same entry gate as a
     booking and can be refused with `no_entries_left`.
  3. Cancelling a class. Under the new rule its bookings stop consuming entries. `CancelClass`
     touches no pass stamp today (`src/Application/Scheduling/CancelClass.cs:21-22, 81, 89`).

  Point 3 is also the answer to Open Roadmap Question 7.

- **UI surfaces:**
  - Staff reach a past class's roster through `/schedule`, which is always `selectable`
    (`src/app/src/app/features/schedule/schedule.html:15-22`). The desk calendar is different: it
    makes whole past weeks non-selectable (`features/admin/classes/classes.ts:138-142`,
    `classes.html:19-20`), so an admin at a desk cannot open last week's roster there.
  - The member side needs a new endpoint and a history view. `/my-classes` is a `tab` with no
    child route today (`app.routes.ts:145-151`).

## Detailed Findings

### 1. The domain today

- `Booking` has the fields `Id, ClassId, MemberId, MembershipPassId?, Status, CreatedAt, CancelledAt?`
  (`src/Domain/Scheduling/Booking.cs`).
  - `BookingStatus` is `Active = 0`, `Cancelled = 1`, stored as **int**
    (`src/Infrastructure/Persistence/Configurations/BookingConfiguration.cs:22-25`).
  - The values are pinned because the filtered unique index `IX_Bookings_Class_MemberId_Active`
    hard-codes `[Status] = 0` (`BookingConfiguration.cs:66-71`).
- `MembershipPass` has the fields `TypeName, ValidFrom, ValidTo, EntryCount, IssuedAt, ConcurrencyStamp`
  (`src/Domain/Members/MembershipPass.cs`). It has no balance column, by design (S-16 plan:102, :170-172).
- A null `MembershipPassId` means the booking was made before the karnet existed and consumes no entry
  (S-16 plan:369-370).
- The latest migration is `20260909123050_ActivatePendingAccounts`.

### 2. Where "entries used" is computed (the predicate AT-03 changes)

| Site | File | Consumers |
| --- | --- | --- |
| Write-path gate | `src/Infrastructure/Scheduling/BookingStore.cs:44-48` (`CountActiveForPassAsync`), seeks `IX_Bookings_MembershipPassId_Status` | `BookingProtocol.cs:133-137` → `no_entries_left` |
| Admin pass history | `src/Infrastructure/Members/MembershipPassQuery.cs:50-51`, left = `EntryCount - used` at :63 | `MembershipPassProjection.EntriesUsedAsync` (`src/Application/Members/MembershipPassProjection.cs:73-82`) → `RevokePass` (`has_active_bookings`, `RevokePass.cs:45-50`), `UpdatePass` (`invalid_entry_count`, `UpdatePass.cs:67-72`) |
| Member's karnet | `MembershipPassQuery.cs:93-94` (`FindCoveringAsync`), left at :110 | `GET /api/passes/mine` → `GetMyPass.cs:44-48` → dashboard karnet card |

All three sites must change together. If one of them drifts, the gate and the displayed balance
disagree, and a member is refused while the card shows an entry left, or the reverse.

Two consumers need an explicit decision about the new rule:
- **`RevokePass` refuses when used > 0.** A pass whose every booking was marked absent would become
  revocable. That is probably right, but the plan should decide it.
- **`UpdatePass` refuses lowering `EntryCount` below used.**

### 3. The booking write path and its concurrency protocol

- **Route.** `POST /api/admin/classes/{classId}/bookings`, `TrainerOrAdmin`
  (`src/Api/Endpoints/Scheduling/BookingEndpoints.cs:94-99`) → `BookForMember.cs`, which checks:
  1. the class exists;
  2. `MayActOn`;
  3. `member_blocked` (:70-73);
  4. `member_is_staff` (:77-80).

  Then it runs `BookingProtocol.TryBookAsync`.
- **`TryBookAsync`** (`src/Application/Scheduling/BookingProtocol.cs:63-193`) is a retry loop of up
  to 10 attempts. Each attempt:
  1. Re-reads the class.
  2. Refuses with `class_cancelled`, `class_started` (`StartsAt <= now`, :86-94), `already_booked`,
     `class_full`.
  3. Resolves the pass by the class's club-local date (:122) and refuses with `no_valid_pass`.
  4. Counts entries used and refuses with `no_entries_left` (:133-137).
  5. Inserts the booking and rotates **both** `Class.ConcurrencyStamp` and
     `MembershipPass.ConcurrencyStamp` in one `SaveChangesAsync`.

  A lost race discards the changes and retries; after the last attempt the answer is `conflict`.
- **No explicit transaction or isolation level.** Each save is its own implicit transaction. The
  guarantee comes entirely from the two optimistic tokens
  (`ClassConfiguration.cs:34-37`, `MembershipPassConfiguration.cs:40-43`).
  `UnitOfWork.TrySaveAsync` maps `DbUpdateConcurrencyException` and SQL errors 2601/2627
  (`src/Infrastructure/Persistence/UnitOfWork.cs:42-60`).
- **Release.** `DELETE .../bookings/{bookingId}` (`src/Application/Scheduling/ReleaseBooking.cs`):
  - It sets `Cancelled` and `CancelledAt` and rotates the class stamp.
  - `BookingProtocol.ReturnEntryAsync` (`BookingProtocol.cs:212-227`) rotates the pass stamp.
  - **It has no time check**, so staff can release a booking on a class that has already happened.
- **Block.** `BlockMember.cs:114-115` → `BookingStore.CancelActiveFutureForMemberAsync`
  (`BookingStore.cs:53-110`):
  - It cancels only `StartsAt > asOf`, so past bookings stay Active and keep their entry.
  - It rotates each distinct pass's stamp.
- **Class cancel.** `CancelClass.cs`:
  - Refuses with `class_started` (:73).
  - Flips `ClassStatus.Cancelled` (:81) and rotates only the class stamp (:89).
  - Every booking stays Active (:21-22) and every entry stays spent. This is Open Roadmap Question 7,
    deliberately left unpinned by the booking-invariants test rollout
    (`context/archive/2026-09-11-testing-booking-invariants/plan.md:42-45`).

### 4. Who may act on a class (AT-01)

- **`BookingAuthorization.MayActOn`** is `IsInRole(Admin) || principal.GetMemberId() == entity.InstructorMemberId`
  (`src/Application/Scheduling/BookingAuthorization.cs:49-51`). A failure is `NotYourClass()`, a 403 (:63).
  - AT-01 is exactly this predicate. Attendance endpoints go in the same staff group, whose header
    comment requires every endpoint to call `MayActOn` (`BookingEndpoints.cs:79-93`).
- **Policies** (`src/Infrastructure/Authorization/AuthorizationPolicies.cs`): `MemberOnly` is at
  :69-74 and `TrainerOrAdmin` at :88-92.
  - The member history endpoint is `MemberOnly`, like every other `/mine` route. S-25 rule: staff
    must not even *request* `/mine` (role-based-visibility plan:183-185).

### 5. The staff roster (where attendance is marked)

- **Read.** `GET /api/admin/classes/{classId}/bookings` → `GetClassBookings.cs:24-45` →
  `BookingQuery.GetForClassAsync`:
  - The query (`src/Infrastructure/Scheduling/BookingQuery.cs:52-73`) returns Active bookings only,
    ordered by `CreatedAt`.
  - The DTO is `ClassBooking(BookingId, MemberId, UserId?, DisplayName, Email, BookedAt)`
    (`src/Application/Scheduling/ClassBooking.cs:28-34`), with no attendance field.
- **SPA overlay.** `src/app/src/app/features/class-bookings/class-bookings-overlay.{ts,html}`:
  - It uses `useOverlayFocus` (:89), `createBusySet` (:114) and a load fence (:139).
  - Row action "Zwolnij miejsce" (html:39-47); add picker (html:61-124).
  - Errors stay **in the overlay**, on the row (`failedId`/`failure`) and as `addFailure`, not as a
    toast. Words come from `transportMessage(info) ?? bookingFailureMessage(info.reason)`
    (`core/scheduling/booking-failure.ts:21-40`).
  - The overlay is opened from `/schedule` (`schedule.html:35-41`, trainer and admin below desk) and
    from the desk calendar (`features/admin/classes/classes.html:66-72`, `classes.ts:330`), never
    from the dashboard.
- **Past classes.** `/schedule` is always `selectable`, so a trainer opens the roster of a class that
  has ended. On the desk calendar, `isPast` is true only when the **whole visible range** has ended
  (`classes.ts:138-142`). Today's finished classes are therefore reachable at a desk, while last
  week's are not (`classes.html:19-20`).
- **The overlay has no notion of "started".** On a started class, its add picker can only produce
  `class_started`, and its release button still works (see §3). With attendance, the overlay needs
  two modes: before the start (book, release) and after it (present, absent).

### 6. The member side (AT-04, AT-05)

- **`GET /api/bookings/mine`** (`MemberOnly`, `BookingEndpoints.cs:68-72`) → `GetMyBookings.cs:30-44`
  → `BookingQuery.GetUpcomingForMemberAsync` (`BookingQuery.cs:15-50`):
  - The filter is `Status == Active && Class.Status == Scheduled && StartsAt >= now`, and nothing
    past is returned.
  - The DTO is `MyBooking(BookingId, ClassId, Name, Description, StartsAt, DurationMinutes, Instructor, BookedAt)`
    (`src/Application/Scheduling/MyBooking.cs:27-35`).
- **`GET /api/passes/mine`** → `MembershipPassView(…, EntryCount, EntriesUsed, EntriesLeft, …)`
  (`src/Application/Members/MembershipPassView.cs:27-36`). It is rendered on the dashboard karnet
  card (`features/dashboard/dashboard.html:46-72`, loaded at `dashboard.ts:208-235`).
- **`/my-classes`:**
  - Route `app.routes.ts:145-151`: title "Zajęcia", `level: 'tab'`, `authGuard` + `memberGuard`.
  - `features/my-classes/my-classes.{ts,html}`: upcoming bookings in a plain `<ul>` with
    `<li appRow class="card">` wrapping `app-class-summary`. It uses `app-empty` and `app-loading`,
    but not `app-list`.
  - Nav entry `MY_CLASSES` is at `core/layout/navigation.ts:28-33`; the member gets
    `[START, MY_CLASSES, MY_PLAN]`.
- **Conventions a history view must follow:**
  - A child route with `data: { level: 'child', parent: '/my-classes' }` and a Polish `title`.
  - An `h1.screen-title`.
  - `app-list` / `li[appRow]`, `app-empty`, `app-loading`, enforced by
    `tools/eslint-rules/no-hand-rolled-presentational.js`.
  - Breakpoints only via `_breakpoints.scss`.
  - Any overlay uses `useOverlayFocus`.
  - Sources: AGENTS.md "Screen identity" and "The presentational kit"; mobile-native-feel plan:141-154.

### 7. Tests that pin today's entry rule

Tests that will change or need siblings are all under `tests/po-prostu-silka.Tests/`.

- **`AdminBookingEndpointTests.cs`:**
  - `Exhausting_the_entries_refuses_the_next_booking` (:621)
  - `Releasing_a_booking_returns_the_entry` (:643)
  - `A_booking_records_the_karnet_that_paid_for_it` (:669)
  - `Concurrent_bookings_never_exceed_the_pass_entry_count` (:696)
  - `Blocking_a_member_returns_the_entries_their_future_bookings_held` (:979)
- **`ClassCancellationTests.cs`:**
  - `Cancelling_moves_the_class_to_cancelled_and_leaves_every_booking_active` (:372)
  - `A_cancelled_class_leaves_my_bookings_but_the_row_stays_active` (:1019)

  Row status stays Active under the recommendation, but entries now return.
- **`MembershipPassEndpointTests.cs`:**
  - `A_pass_with_a_spent_entry_cannot_be_revoked` (:351)
  - `A_member_reads_their_own_karnet` (:550)
- **`BookingEndpointTests.cs`:** `Mine_hides_a_class_that_has_already_started` (:543) stays valid,
  because history is a new route, not a widened `mine`.
- **SPA:** `class-bookings-overlay.spec.ts` (21 tests), `my-classes.spec.ts`, `dashboard.spec.ts`.
- **E2E:** `src/app/e2e/back-closes-open-overlay.spec.ts:78-110` already opens the roster on
  `/schedule`.

## Code References

- `src/Domain/Scheduling/Booking.cs` - the entity attendance attaches to
- `src/Domain/Scheduling/BookingStatus.cs` - lifecycle enum, values pinned by a filtered index
- `src/Domain/Members/MembershipPass.cs` - `EntryCount`, `ConcurrencyStamp`
- `src/Infrastructure/Persistence/Configurations/BookingConfiguration.cs:22-25, 53-54, 66-71, 75-76` - status storage and indexes
- `src/Infrastructure/Scheduling/BookingStore.cs:44-48` - write-path entries-used count
- `src/Infrastructure/Scheduling/BookingStore.cs:53-110` - block cascade (future only, rotates pass stamps)
- `src/Infrastructure/Members/MembershipPassQuery.cs:50-51, 93-94` - read-side entries-used counts
- `src/Application/Members/MembershipPassProjection.cs:73-82` - `EntriesUsedAsync` for revoke/update
- `src/Application/Scheduling/BookingProtocol.cs:63-193` - `TryBookAsync`, the entry gate at :133-137
- `src/Application/Scheduling/BookingProtocol.cs:212-227` - `ReturnEntryAsync` (pass stamp rotation)
- `src/Application/Scheduling/ReleaseBooking.cs` - release, no time check
- `src/Application/Scheduling/CancelClass.cs:21-22, 73, 81, 89` - bookings stay Active; no pass stamp
- `src/Application/Scheduling/BookingAuthorization.cs:49-51` - `MayActOn` (AT-01)
- `src/Api/Endpoints/Scheduling/BookingEndpoints.cs:68-72, 79-100` - `mine` and the staff group
- `src/Application/Scheduling/ClassBooking.cs:28-34` - roster DTO
- `src/Infrastructure/Scheduling/BookingQuery.cs:15-50, 52-73` - `mine` and roster queries
- `src/Application/Members/MembershipPassView.cs:27-36` - karnet DTO
- `src/app/src/app/features/class-bookings/class-bookings-overlay.{ts,html}` - the roster UI
- `src/app/src/app/features/schedule/schedule.html:15-22, 35-41` - always-selectable schedule
- `src/app/src/app/features/admin/classes/classes.ts:138-142`, `classes.html:19-20` - past weeks inert at a desk
- `src/app/src/app/features/my-classes/my-classes.{ts,html}` - member's classes (upcoming only)
- `src/app/src/app/features/dashboard/dashboard.html:46-72` - karnet card
- `src/app/src/app/app.routes.ts:145-151` - `/my-classes` route identity
- `src/app/src/app/core/scheduling/booking.models.ts:9-56, 75-109` - mirrored DTOs and reason union
- `src/app/src/app/core/scheduling/booking-failure.ts:21-40` - booking failure words

## Architecture Insights

### The recommended entry rule

A booking with a pass consumes an entry unless it was released, its class was cancelled, or it was
marked absent. As a predicate:

```
b.MembershipPassId == pass
&& b.Status == Active
&& b.Class.Status != Cancelled
&& b.Attendance != Absent
```

This gives a three-state reading:
- **reserved:** a future booking, or a past one not yet marked;
- **spent:** present, or never marked;
- **returned:** absent, released, or the class was cancelled.

It keeps MP-06's guarantee. No sequence of bookings can overdraw a pass, because every entry a future
booking might spend is already held. It also needs no data migration, because existing rows have no
attendance and so read exactly as today.

- **"Unrecorded counts as attended"** is the recommendation for the S-27 unknown about classes nobody
  marks. It is the only default that neither silently refunds entries nor needs a background job. It
  is a product call for the user to confirm in `/10x-plan`.
- **The join to `Class`** adds a condition the current index
  (`IX_Bookings_MembershipPassId_Status`) does not cover. That is harmless at a pass's scale (tens of
  rows), and the plan should note it rather than add an index by reflex.

### Why a column, not new `BookingStatus` values

`Attended` / `NoShow` as statuses would drop marked rows out of every `Status == Active` predicate:
- capacity (`BookingStore.cs:41-42`, `ClassScheduleQuery.cs:87`);
- the unique index filter;
- the roster (`BookingQuery.cs:64`);
- `mine` (`BookingQuery.cs:37`).

Each of those would have to be revisited, and some would silently change meaning. Attendance is a
fact about a class that took place, orthogonal to whether the booking stands. A nullable column
(`Attendance`: Present | Absent, plus when and by whom it was recorded) is:
- additive;
- reversible (its `Down` drops the column);
- invisible to every existing predicate except the entries-used one.

### Where the stamps must rotate

S-16's rule is that any write that makes a booking possible or impossible rotates the pass stamp
(membership-pass-and-staff-booking plan:141-145). It applies here in four places:
- **present → absent:** returns an entry, so rotate.
- **absent → present:** spends an entry. This must run the same `entriesUsed >= EntryCount` gate
  inside a retry loop, like `TryBookAsync`. Otherwise an entry freed by an absence and re-booked
  elsewhere gets spent twice. The refusal is `no_entries_left`, which already has a sentence in
  `booking-failure.ts`.
- **unmarked → present:** changes nothing about the count under the recommended rule, but rotating
  anyway is the cheap, uniform choice.
- **class cancel:** now returns every booked entry, so it must rotate each distinct pass's stamp, the
  way the block cascade already does (`BookingStore.cs:92-104`).

### The four-outlet rule, applied to the roster

The roster is an overlay whose failures today stay in the overlay, per row. Marking attendance is a
row action, so it follows the same in-overlay pattern. A second mechanism beside it would be a new
mechanism, which S-19 exists to prevent.

## Historical Context (from prior changes)

- **S-16 plan (`context/archive/2026-09-09-membership-pass-and-staff-booking/plan.md`):**
  - :85-87 "Only the insert consumes an entry". AT-03 amends exactly this sentence.
  - :102 and :170-172 say why entries are derived, not stored.
  - :124-126 and :141-145 describe the pass stamp and why release rotates it too.
  - :88-90 and :948-951 say a cancelled class keeps entries spent, "a separate decision about what a
    cancelled class owes its attendees".
- **S-08 plan (`context/archive/2026-09-03-class-booking-and-cancel/plan.md`):**
  - :99-100 "The admin's list says who signed up, never who showed up".
  - :468-470 says the admin release action exists partly for "the no-show case", the workaround S-27
    replaces.
  - :313-314 gives the `class_started` rule.
- **S-09 plan (`context/archive/2026-09-04-class-change-notifications/plan.md:60-71, 394`):** a
  cancelled class keeps its rows so the roster and history still name who was booked. The
  recommendation keeps the rows Active and changes only what they cost.
- **Booking-invariants tests (`context/archive/2026-09-11-testing-booking-invariants/plan.md:42-45`):**
  - G5 was deliberately left unpinned, because "Pinning it would cement what may be a member-harming
    bug" (plan-brief.md:26).
  - S-27 can pin the new behaviour.
- **S-25 plan (`context/archive/2026-09-22-role-based-visibility/plan.md`):**
  - :63-65 the trainer already reads and acts on their roster.
  - :183-185 staff never request `/mine`.
  - :138-139 "/my-classes stays the list".
- **PRD v1:** `prd.md:192` lists attendance as a Non-Goal, now unparked. `:152` is the privacy NFR:
  member data is visible to the admin and the member only. `:210` puts "advanced statistics" out of
  scope, which is why AT-04 is the member's own history and no more.

## Related Research

- `context/archive/2026-09-22-role-based-visibility/research.md` - personas, trainer roster
- `context/archive/2026-09-23-mobile-native-feel/research.md` - screen identity, child routes, back

## Open Questions

1. **Unmarked attendance.** Does an unmarked class spend the entry forever, which is the
   recommendation and today's behaviour, or is it released after a window? Owner: user, in
   `/10x-plan`.
2. **The marking window.** Can attendance be marked or corrected from the class's start until when?
   Before the start it must be refused. Is there an end, e.g. same club-local day, 7 days, or
   unlimited for an admin? Owner: `/10x-plan`.
3. **Release after the start.** Should releasing a booking be refused once the class has started, now
   that "absent" is the honest record of a no-show? Today it is allowed (`ReleaseBooking.cs`, no time
   check). Owner: `/10x-plan`.
4. **The admin's past weeks at a desk.** The desk calendar makes past weeks non-selectable, so last
   week's roster is unreachable there. Should a past tile open the roster in attendance mode only, or
   should the admin go through `/schedule`? Owner: `/10x-plan`.
5. **The member's history scope.** Should cancelled classes and staff-released bookings appear in the
   history, and how far back? A page or month grouping bounds the query. Owner: user, in
   `/10x-plan`. AT-05 asks for grouping and a summary, not a flat list.
6. **The karnet card wording.** "Zostało N wejść" stays correct under the reservation rule. Should
   the card distinguish reserved (booked, upcoming) from spent? Owner: `/10x-plan`, UX.
7. **`RevokePass` / `UpdatePass`** consume the same count. A pass whose bookings were all marked
   absent becomes revocable. Confirm that is intended. Owner: `/10x-plan`.
8. **Open Roadmap Question 8.** Should the roster show members' emails to a trainer? It is adjacent,
   not blocking, and S-27 widens the trainer's use of the roster. Leave as is unless the plan decides
   otherwise.
