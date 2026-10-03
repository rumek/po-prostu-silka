# Club-Shaped Test Data Implementation Plan

## Overview

Reshape the S-24 test club (`src/Infrastructure/TestData/`) so that Staging looks like the club the
spreadsheets describe:

- `xlsx/treningi.xlsx` — fixed weekly groups of 1, 2, 3 or up to 6 people, entered once with a fixed time
  and trainer; attendance "Był / Nie był – odrobi / Nie był – przepada"; a shared makeup list.
- `xlsx/twt.xlsx` — monthly karnety, one-off entries and vouchers; the payment states AKTYWNY / KOŃCZY SIĘ /
  WYGASŁ / BRAK PŁATNOŚCI.

Today's seed is a 200-member chain gym with random daily classes, and it shows none of the features
built since: rosters (S-37), attendance (S-27), makeups (S-36).

## Current State Analysis

- `TestDataGenerator` (`src/Infrastructure/TestData/TestDataGenerator.cs`) builds the club in memory from
  one fixed-seed `Random`. Its parts:
  - **People:** 2 admins (admin2 also teaches), 2 trainers, 160 account members, 40 accountless.
  - **Passes:** "Karnet 8/12/30 wejść".
  - **Schedule:** 7 themed groups (Joga, Crossfit…, capacity 10–20). Each day draws 3–5 random slots
    from 8 fixed times and a random group per slot, over ±28 days.
  - **Bookings:** random, but mirroring `BookingProtocol` (capacity, covering karnet by club-local date,
    entries left).
  - **Training:** exercises and 8 plans.
- `TestDataSeeder` persists the set in one `SaveChangesAsync`. It does not write `GroupRosterEntries`;
  the wipe already deletes them (`TestDataSeeder.cs:231`).
- The seeder writes straight through the `DbContext`. The database enforces only the
  one-active-booking-per-member index and the one-live-makeup-per-absence index. Every other rule lives
  in Application code and must be mirrored by the generator:
  - capacity, the karnet gate and the entry count (`BookingProtocol`);
  - attendance only after start (`RecordAttendance`);
  - the makeup rules (`BookMakeup`, `MakeupRules`): deadline, karnet on the makeup date, no entry spent;
  - `EntryConsumption.ConsumesAnEntry`.
- The seeder places classes without `HasTimeConflictAsync`, the club-wide half-open overlap check
  (`ClassStore.cs:60`). The app refuses any two classes that overlap, so the seed must never produce one.
- `TestDataSeederTests` pins exact counts (164 accounts, 40 accountless, 3 trainers, 8 active plans).
  `TestDataGeneratorTests` pins day-determinism, no row predating its member, and unpaid karnety.
- The manual test scripts use these accounts:
  - `context/testing/01-account-and-access.md`: `czlonek010`;
  - `02-member-screens.md`, `04-schedule-bookings-attendance.md` and `environment.md`: `trener1`/`trener2`;
  - all of them: `admin1`.

  The runbook (`context/deployment/deploy-plan.md:290-350`) lists the accounts and the first-seed counts.

## Desired End State

After a reset-and-seed, Staging holds a club of about 100 members with:

- **Groups:** about 30 fixed weekly groups, each with a fixed time and trainer, all slots disjoint.
  Each group has a roster, and its classes carry those rosters' bookings.
- **Schedule changes:** a few substitutions, moved times and cancellations.
- **Karnety:** "Miesięczny" (4 entries / 30 days), "Wejście jednorazowe" and "Voucher". Every payment
  state is present in guaranteed numbers on any reseed day.
- **Attendance:** past classes are marked. The last two days are partly unmarked.
- **Makeups:** items in every `MakeupState`.

Every row is one the app itself could have produced. Verify with `dotnet test` (the new invariant tests
included) and a local reseed walked through the screens.

### Key Discoveries:

- **Entry consumption.** It is `Active ∧ class not Cancelled ∧ MakeupForBookingId == null ∧
  Attendance != Absent` (`EntryConsumption`). The seed never writes the legacy `Absent`, so every
  non-makeup active booking spends an entry, marked or not.
- **Makeup state.** It is derived by `MakeupRules.StateOf` (`MakeupRules.cs:38`); the deadline is the
  absence's club-local date + 30 days and governs the makeup class's date.
  - A makeup booking sets `MakeupForBookingId`.
  - Its `MembershipPassId` is a karnet covering the makeup class's own date.
  - It consumes no entry (`Booking.cs:121-131`).
- **Audit fields** (`AttendanceRecordedBy`, `MakeupClosedBy`) hold a user id with no foreign key.
- **Blocking.** It does not remove a member from rosters (group-fixed-roster plan, "What We're NOT Doing").
  A blocked member in a roster is a legitimate state, and shows a `member_blocked` gap.
- **"Kończą się karnety"** uses a five-day window (`MakeupRules.cs:10`, the S-30 card), matching the
  spreadsheet's "KOŃCZY SIĘ ≤ 5 dni".

## What We're NOT Doing

- **No schema change.** The spreadsheet's karnet price and payment form (Przelew/Gotówka/BLIK) have no
  column, and adding them is a product change, not test data.
- **No real names.** Nothing from `twt.xlsx`'s client list enters the seed: no names, dates or amounts.
  The trainers are fictional (decided 2026-10-03).
- **No overlapping classes, even if a real studio could run two trainers at once.** The app refuses
  overlap club-wide, and seed data must be producible by the app.
- **No change to the seeder's gates, its reset, the sentinel (`admin1@example.test`) or the e-mail
  scheme.** `czlonek010@example.test`, `trener1`, `trener2` and `admin1` keep meaning what the manual
  scripts expect.
- **No legacy `BookingAttendance.Absent` rows** — nothing writes them since S-36.
- **The exercise library and the training plans keep their content.** Only plan owners are re-drawn
  from the new population.

## Implementation Approach

Rewrite the generator's people/passes/schedule/bookings half around one idea: **the group is the unit.**
A group spec carries its kind (individual, pair, trio, group of up to 6), weekday, club-local time,
trainer and capacity.

1. **Build the weekly slot table.** Pick about 30 disjoint slots, Monday to Saturday, on the
   spreadsheet's 15-minute grid.
2. **Fill the rosters.** Draw them from the members.
3. **Issue karnety per member.** Time them so the guaranteed payment and expiry states exist relative
   to today.
4. **Generate the classes.** Walk each group's weekly occurrences over the window and apply the
   scripted exceptions (substitution, moved time, cancellation).
5. **Book in start order, the way a roster sync would.** Then place one-off and voucher holders, and the
   makeups, into the remaining free spots.
6. **Mark attendance last.** Past classes only, then derive the makeup items from the "odrobi" marks.

Every write goes through the generator's in-memory mirror of the app's checks, so a row the app would
refuse is a generator bug and fails the tests.

## Critical Implementation Details

- **Day-independence of the guarantees.** "At least N expiring", "at least one item in each makeup state"
  and "unpaid current karnety exist" must hold whatever club-local day the seed runs. So build those
  cases from `today` rather than drawing them. Keep the existing rule that every timestamp derives from
  the club-local date, never the instant, so a reseed on one day is identical at any hour.
- **Makeup windows need history past 30 days.** An item "not made up because the deadline passed" needs
  an absence more than 30 days ago. The past half of the window therefore grows to 35 days; the future
  half stays 28.
- **Ordering.** Attendance and makeups are derived after ALL bookings exist. A makeup must land on a
  class with a free spot, and its member must hold no other active booking on that class. Its
  `CreatedAt` falls after the absence's mark and before the makeup class starts. The mark itself falls
  after the absence class starts.

## Phase 1: The club-shaped generator

### Overview

The whole new data set and the tests that pin every rule it must obey.

### Changes Required:

#### 1. Vocabulary

**File**: `src/Infrastructure/TestData/TestDataNames.cs`

**Intent**: Replace the themed group catalogue with what the club sells, and add the karnet types from
`twt.xlsx`.

**Contract**:
- `ClassGroups` / `ClassGroupSpec` are replaced by group-kind specs: individual (capacity 1),
  pair (2), trio (3) and group (6), each with a duration and a description.
- Pass types:
  - "Miesięczny": 4 entries, 30 days.
  - "Wejście jednorazowe": 1 entry, 30 days.
  - "Voucher": 4 entries, 60 days.
- Names, streets, cities, exercises, plan names and item notes are unchanged.
- Group names:
  - an individual group is named after its person ("Anna Kowalska – indywidualny");
  - the others after day and time ("Para Pon 18:00", "Trio Śr 07:15", "Grupa Wt 19:00").
- One extra group is inactive, with past classes only ("… (zakończona)"), so the list shows both states.

#### 2. People and staff

**File**: `src/Infrastructure/TestData/TestDataGenerator.cs`

**Intent**: A medium club with the spreadsheet's proportions.

**Contract**:
- **Staff:**
  - `admin1@` holds Admin and teaches nothing;
  - `admin2@` holds Admin+Trainer and teaches a few groups, keeping the S-25 case;
  - `trener1@`–`trener4@` hold Trainer+User and carry fictional names from `TestDataNames`.
- **Members:**
  - about 70 account members, `czlonek001@`…, and about 30 accountless, `bezkonta01@`…;
  - a quarter of the accountless have no e-mail, half have a live invitation code (unchanged rules).
- **Blocked members:** about 3, drawn only from indices above 020, so `czlonek010` stays an active
  member with a roster place, a current karnet and bookings, as the manual scripts assume.
- `MemberMinAgeDays` is kept large enough that no pass, booking or roster entry predates its member.
- **Population constants** (`AccountMemberCount`, `AccountlessMemberCount`) are updated, and the
  seeder's log line stays truthful.

#### 3. Groups, the weekly slot table and rosters

**File**: `TestDataGenerator.cs`

**Intent**: About 30 fixed weekly groups, like `treningi.xlsx`'s "SZABLON DO 6 OSÓB" sheets, with
rosters entered once.

**Contract**:
- **Mix:** about 10 individual, 8 pairs, 5 trios and 7 groups of up to 6.
- **Slots:**
  - each group gets one weekday (Monday–Saturday) and a club-local start on the 15-minute grid
    (06:15–20:30);
  - no two weekly slots overlap under half-open intervals, back-to-back allowed;
  - the slot table is pre-checked against itself.
- **Trainer:** fixed per group, from `trener1`–`trener4` plus `admin2`.
- **Rosters:**
  - new `GroupRosterEntry` rows. Each roster has between 1 and its capacity members; most are full,
    and some groups of 6 hold 4–5, leaving free spots;
  - no staff in any roster, and no member in two rosters at the same slot;
  - about 75 members belong to a roster. The rest are former clients (expired karnet only), one-off
    and voucher holders, and a few with no karnet;
  - exactly one blocked member stays in a roster;
  - `AddedAt` predates the window for most members; 2–3 members joined mid-window, and their bookings
    start from their `AddedAt`;
  - `AddedBy` is admin1's user id.
- **`TestDataSet`** gains `IReadOnlyList<GroupRosterEntry> RosterEntries`.

#### 4. Karnety and payments

**File**: `TestDataGenerator.cs`

**Intent**: Every member's karnet history reads like a row of `twt.xlsx` kept over time, and every
payment state is present in guaranteed numbers.

**Contract**:
- **Roster members** hold consecutive "Miesięczny" karnety, never overlapping (the issue rule), covering
  the past window and today.
- **Guaranteed cases**, built from `today`:
  - at least 8 karnety covering today whose `ValidTo` is within `today..today+5`;
  - at least 5 current karnety unpaid (`PaidAt == null`);
  - at least 3 expired karnety unpaid;
  - at least 4 roster members whose karnet expired with no renewal, so their future classes are gaps.
- **Renewals:** about a third are renewed ahead of time with a second karnet starting the day after the
  current one ends.
- **One-off and vouchers:** some non-roster members hold a "Wejście jednorazowe" or a "Voucher".
- **Paid karnety:** `PaidAt` is on or after `ValidFrom`, never after today. `IssuedAt` stays before the
  karnet's start and after the member's creation.

#### 5. Classes and their exceptions

**File**: `TestDataGenerator.cs`

**Intent**: The occurrences of each group every 7 days over the window, with the changes the
spreadsheet's instructions describe (substitution, different time on one occurrence).

**Contract**:
- **Window:** occurrences from `today-35` to `today+28`.
- **Defaults:** capacity, duration and trainer come from the group.
- **Exceptions:**
  - 3–4 occurrences carry a different instructor (substitution);
  - 2 are moved to a different time that overlaps no other class;
  - 2 are `Cancelled`, one past and one future, keeping their active bookings (as `CancelClass` does);
  - the inactive group has only past occurrences.
- **`CreatedAt`** predates the occurrence.
- **No overlap:** the full class list is overlap-free under `HasTimeConflictAsync`'s rule.

#### 6. Bookings

**File**: `TestDataGenerator.cs`

**Intent**: Bookings are what roster bookings and staff bookings would have made.

**Contract**:
- **Roster bookings:** for each roster member, the group's classes from their `AddedAt` on, in start
  order, each booked when the in-memory mirror allows it:
  - free spot;
  - a karnet covering the class's club-local date, earliest `ValidFrom` first;
  - an entry left, counted by `ConsumesAnEntry`.

  A refusal is skipped. With 4 entries per 30 days, a fifth weekly occurrence inside one karnet is
  naturally a gap.
- **No future bookings for a blocked member.**
- **One-off and voucher holders** are booked into free spots of groups of 6 inside their validity.
- **`CreatedAt`** is before the class starts and after the member joined.
- The old "fill three classes exactly to capacity" step goes away. Individual and full pair classes
  satisfy "some future class is full" naturally.

#### 7. Attendance and makeups

**File**: `TestDataGenerator.cs`

**Intent**: The past looks marked as `treningi.xlsx` is, and the makeup list has every state.

**Contract**:
- **Classes to mark:** active bookings on non-cancelled classes from `today-3` back. Today's are never
  marked; `today-1` and `today-2` are about half unmarked.
- **Mix:** about 85% `Present`, 10% `Makeup` ("odrobi"), 5% `Forfeited`.
- **Audit:** `AttendanceRecordedAt` is after the class ends and no later than today's anchor;
  `AttendanceRecordedBy` is the instructor's user id.
- **Makeup items, at least one in each state**, using `MakeupRules.StateOf` as the oracle:
  - **made up:** a makeup on a past class, marked `Present`;
  - **planned:** a makeup on a future class within the deadline;
  - **open:** no makeup, deadline not passed;
  - **not made up, closed by hand:** `MakeupClosedAt` and `MakeupClosedBy` set;
  - **not made up, deadline passed:** an absence more than 30 days ago with no makeup;
  - **not made up, makeup forfeited:** a makeup marked `Forfeited`.
- **Makeup booking rules:**
  - at most one active makeup per absence;
  - the makeup class is not the absence class and falls within the deadline;
  - it has a free spot;
  - the member holds a karnet covering that date;
  - `MembershipPassId` is that karnet;
  - the booking spends no entry.

#### 8. Persistence

**File**: `src/Infrastructure/TestData/TestDataSeeder.cs`

**Intent**: Persist rosters with the rest, in the same single save.

**Contract**:
- `db.GroupRosterEntries.AddRange(data.RosterEntries)`.
- The "refuses to seed over existing data" probe also checks `GroupRosterEntries`.
- The log line gains the roster count.
- No change to the gates, the wipe or the sentinel.

#### 9. Tests

**File**: `tests/po-prostu-silka.Tests/TestDataGeneratorTests.cs`,
`tests/po-prostu-silka.Tests/TestDataSeederTests.cs`

**Intent**: Pin every rule the seed must share with the app.

**Contract**:
- **Pure generator invariants.** Run over 10 consecutive club-local days, including a DST change, so
  the guarantees are not day-dependent:
  - no two non-cancelled classes overlap, under half-open intervals;
  - roster size ≤ group capacity; no staff in a roster; no member in two rosters at one slot;
  - per class, active bookings ≤ capacity;
  - every active booking's karnet belongs to its member and covers the class's club-local date;
  - per karnet, `ConsumesAnEntry` count ≤ `EntryCount`;
  - attendance only on started, non-cancelled classes, recorded after the class ends;
  - no `Absent`;
  - makeups obey the deadline, the karnet coverage and one-live-per-absence, and never on the absence
    class;
  - every `MakeupState` is present;
  - expiring ≥ 8, current unpaid ≥ 5, expired unpaid ≥ 3;
  - no blocked member has a future booking;
  - `czlonek010` is active, in a roster and has a current karnet.
- **Existing tests:** day-determinism keeps its shape and adds roster entries; "no row predates its
  member" adds roster entries.
- **Seeder:**
  - `Seeds_the_expected_population` gets the new counts, including 5 trainers counting admin2;
  - `Bookings_obey_capacity_and_passes` replaces its raw `Used` count with the entry-consumption rule;
  - a new assertion checks that seeded rosters persist and that `GET /api/groups/{id}/roster` for a
    seeded group answers 200 with its members.

### Success Criteria:

#### Automated Verification:

- Backend builds warning-free: `dotnet build po-prostu-silka.slnx`
- All tests pass, the new generator invariants included: `dotnet test`

#### Manual Verification:

- A local reseed (`TestDataSeed:Reset=true`, Development, then back to `false`) logs the new counts.
  The admin's calendar shows about 30 groups in a week, none overlapping.

**Implementation Note**: After completing this phase and all automated verification passes, pause here
for manual confirmation from the human that the manual testing was successful before proceeding to the
next phase.

---

## Phase 2: Docs and reseed

### Overview

Record the new club where people read about it, then reseed and walk the screens.

### Changes Required:

#### 1. Runbook and test environment docs

**File**: `context/deployment/deploy-plan.md` ("Test data (Staging only)"),
`context/testing/environment.md`

**Intent**: The account list and the shape of the data match what the seed now produces.

**Contract**:
- The runbook describes the club: about 100 members, about 30 fixed groups with rosters, karnet types,
  attendance and makeups. It lists `trener1`–`trener4`, `czlonek001…070` and `bezkonta01…30`.
- The 2026-09-22 first-seed log stays as history; a new line records this reseed's counts.
- `environment.md` lists the four trainers.

#### 2. Manual scripts still hold

**File**: `context/testing/*.md`

**Intent**: The accounts and cases the scripts cite still exist.

**Contract**: Each script's references (`czlonek010`, `trener1`/`trener2`, a member with a karnet ending
soon, an unpaid karnet, an open makeup) still resolve. Only wording that cites the old shape is
corrected, for example a themed group name.

### Success Criteria:

#### Automated Verification:

- All tests still pass: `dotnet test`

#### Manual Verification:

- Locally after a reseed, each screen shows the club:
  - **Start (admin):** "Kończą się karnety" holds 8 or more members.
  - **Grupy → Skład:** a full pair, a group of 6 with free spots, and a roster with gaps (no karnet,
    entries exhausted, blocked).
  - **Odrabianie:** items in every state.
  - **A member's karnety:** "Miesięczny", one-off and voucher types.
  - **Unpaid filter:** has hits.
  - **trener1's screen:** their groups, including a substituted class.
  - **`czlonek010` signed in:** their bookings and karnet.
- After the merge to `main`, Staging is reseeded (`TestDataSeed__Reset=true` once, then `false`) and
  shows the same.

**Implementation Note**: After completing this phase and all automated verification passes, pause here
for manual confirmation from the human that the manual testing was successful.

---

## Testing Strategy

### Unit Tests:

- Pure generator invariants over 10 consecutive days, including a DST boundary (Phase 1 §9). This is
  where the app's rules are mirrored, and where a rule broken by the seed is caught.

### Integration Tests:

- The seeder against a real SQL Server:
  - population counts;
  - the booking rule through `EntryConsumption`;
  - rosters persisted and readable through the roster API;
  - the existing gate, reset and determinism tests unchanged in intent.

### Manual Testing Steps:

1. Reset-seed locally; log in as `admin1`; check Start, Grupy, Skład, Odrabianie, Członkowie
   (unpaid filter), Zajęcia (a week of the calendar).
2. Log in as `trener1`; check "Twoje zajęcia", Grupy, and that a substituted class appears for the
   substitute only.
3. Log in as `czlonek010`; check Moje zajęcia and the karnet card.

## Performance Considerations

About 30 groups × 9 weeks ≈ 270 classes and about 700 bookings. That is fewer rows than today's seed
(about 250 classes, about 2,000 bookings), still one `SaveChangesAsync`, and well inside the B1 cold
start.

## Migration Notes

None: no schema change. Staging gets the new club only through an explicit reset-and-seed. Until then
it keeps the old one.

## References

- Change notes and decisions: `context/changes/club-shaped-test-data/change.md`
- Spreadsheets: `xlsx/treningi.xlsx`, `xlsx/twt.xlsx` (shape only)
- Generator: `src/Infrastructure/TestData/TestDataGenerator.cs`
- Rules to mirror:
  - `src/Application/Scheduling/BookingProtocol.cs`
  - `src/Application/Scheduling/BookMakeup.cs`
  - `src/Application/Scheduling/RecordAttendance.cs`
  - `src/Domain/Scheduling/MakeupRules.cs`
  - `EntryConsumption`
- Prior seed plan: `context/archive/2026-09-22-test-environment-seed-data/`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: The club-shaped generator

#### Automated

- [x] 1.1 Backend builds warning-free
- [x] 1.2 All tests pass, the new generator invariants included

#### Manual

- [ ] 1.3 Local reseed logs the new counts and a calendar week shows ~30 non-overlapping groups

### Phase 2: Docs and reseed

#### Automated

- [x] 2.1 All tests still pass

#### Manual

- [ ] 2.2 Local screens show the club (Start, Skład, Odrabianie, karnety, unpaid filter, trainer, czlonek010)
- [ ] 2.3 Staging reseeded after the merge and shows the same
