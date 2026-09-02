# Occurrences From Class Types Implementation Plan

## Overview

`Class` stops describing itself and becomes an *instance* of a definition. Identity (name,
description) resolves **by reference** to `ClassType`; the instructor resolves by reference to an
account holding the `Trainer` role; duration and capacity are **copied** onto the occurrence at
creation and never re-read. The room disappears from the code, and the overlap invariant widens from
"one room, one class at a time" to "one club, one class at a time".

This is roadmap item **S-06**, delivering `prd-v2.md` US-01 and FR-008 through FR-013, and
superseding parts of `prd.md` FR-011 / FR-012.

## Current State Analysis

**The occurrence is flat.** `src/Domain/Scheduling/Class.cs` carries its own `Name`, `Room`,
free-text `Instructor` and `Capacity`. Nothing binds this Monday's "Joga dla początkujących" to next
Monday's; weekly duplication (`ClassEndpoints.DuplicateAsync`) copies the strings forward, deferring
the drift rather than removing it.

**S-05 already laid half the foundation.** `ClassType` exists with `Name`, `Description`,
`DefaultDurationMinutes`, `DefaultCapacity`, `IsActive` and a filtered unique index on the name among
active types. `Class.ClassTypeId` exists as a **nullable** FK with `DeleteBehavior.Restrict`,
deliberately with **no navigation property** — `Class.cs` warns that a navigation would invite this
very slice to resolve `DefaultCapacity` through it.

**The dev-data wipe already happened.** Migration `20260902111715_AddClassTypes` ran
`DELETE FROM [Classes] WHERE [ClassTypeId] IS NULL;`. The `Classes` table is empty, so tightening the
FK to `NOT NULL` needs no backfill and no data decision.

**The Trainer role is fully wired.** `ApplicationRoles.Trainer` is seeded,
`POST/DELETE /api/admin/members/{id}/roles/trainer` grant and revoke it, and `MemberSummary.Roles`
already crosses the wire. What does **not** exist is any way to *list* trainers.

**The overlap check is room-keyed.** `ClassStore.HasRoomConflictAsync` filters on `c.Room == room`,
checks the database **and** `db.Classes.Local` for queued-but-unsaved inserts, and is backed by
`IX_Classes_Room_StartsAt`. Its known read-then-write race is accepted on single-admin grounds.

**Test infrastructure now exists** — it did not when S-05 was planned.
`tests/po-prostu-silka.Tests/` boots the real app via `WebApplicationFactory<Program>` against SQL
Server in Testcontainers; `ClassTypeEndpointTests.cs` is the pattern to copy. There is no
`ClassEndpointTests.cs`.

**Four client surfaces mirror the contract:** `core/scheduling/class.models.ts`,
`features/schedule/schedule.html` (member), `features/admin/classes/classes.html` (admin list), and
`features/admin/classes/class-form.{ts,html}` — the last of which maps `room_conflict` onto the room
control.

## Desired End State

An admin opens `/admin/classes/new`, picks "Joga dla początkujących" from a type dropdown and a
trainer from a trainer dropdown. Duration and capacity prefill from the definition and stay editable.
There is no name field and no room field. A start time colliding with **any** existing class is
refused with a message on the start-time control. The class appears on the admin list and on the
member schedule showing the type's name and the trainer's display name, with no room anywhere.
Correcting a typo in the type's name corrects it on every occurrence, past ones included; editing the
type's `DefaultCapacity` changes nothing about classes that already exist.

Verify by running `dotnet test` from the repo root (the new `ClassEndpointTests` assert exactly these
invariants), `npm test` from `src/app/`, and the manual checklist in each phase.

### Key Discoveries:

- `src/Domain/Scheduling/Class.cs:70-90` — `ClassTypeId` is already nullable-by-design with a
  documented instruction that S-06 tightens it. No column to add, only a constraint to change.
- `src/Infrastructure/Persistence/Migrations/20260902111715_AddClassTypes.cs:36` — the `Classes`
  table was already emptied, so `NOT NULL` needs no backfill.
- `src/Infrastructure/Scheduling/ClassStore.cs:47-88` — the conflict check must keep **both** halves:
  the database query and the `db.Classes.Local` pass over `EntityState.Added` rows. Dropping the
  second half would let two weeks of one duplicate batch collide with each other.
- `src/Domain/Scheduling/ClubTime.cs` — duplication adds days in club-local time to survive DST.
  Untouched by this change; only the conflict call inside the loop changes.
- `src/Infrastructure/Members/MemberQuery.cs:52-66` — the role join pattern
  (`db.UserRoles join db.Roles`) that `ITrainerQuery` copies.
- `src/Application/Members/MemberAdminEndpoints.cs:353,401` — `IsInRoleAsync` is the existing
  role-membership check; it normalises its argument.
- `src/Application/Scheduling/ClassTypeEndpoints.cs:82-108` — the bounds constants
  (`MinDurationMinutes` 1 / `MaxDurationMinutes` 480, `MinCapacity` 1 / `MaxCapacity` 200) that the
  occurrence's own validation now aligns to.
- `src/app/src/app/features/admin/class-types/class-type-form.ts` — the load/submit/`applyFailure`
  shape the rewritten class form follows.

## What We're NOT Doing

- **No calendar.** The week strip, day picker and full-week grid are S-07 (`schedule-calendar-view`).
  Both views keep their current layout; only their fields change.
- ~~**No `DROP COLUMN`.**~~ **Reversed during implementation** — `Name`, `Room` and the old
  free-text `Instructor` column are dropped in this slice, one release earlier than the repository
  rule prescribes. See the Migration Notes for what that costs.
- **No rename of `Class` / `ScheduledClass` / `/api/classes` / the `Classes` table.** The PRD says
  "occurrence"; the code keeps saying "class". Recorded as accepted vocabulary drift.
- **No changes to the Trainer grant/revoke endpoints.** Revoking the role or blocking an account with
  future classes stays permitted and unflagged (see Open Risks in the brief).
- **No guest instructor.** `prd-v2` Open Question 1 ships unsupported: every occurrence references a
  real account.
- **No booking, no cancel transition, no notifications.** `Booking` still does not exist; `FreeSpots`
  stays `Capacity` by construction. S-08 and S-09.
- **No concurrency token on `Class`.** The single-seeded-admin reasoning in `ClassStore` and
  `ClassEndpoints.UpdateAsync` is unchanged by this slice.
- **No member-facing class-type screen.** A type still reaches a member only through an occurrence.
- **No global nav entry** for anything added here.

## Implementation Approach

Two phases.

Phase 1 changes the model, the schema and the whole API in one step, and lands the integration tests
that pin the invariants in the same commit. Phase 2 rewrites the two admin screens and the member
schedule against the already-verified contract.

> **Adapted during implementation.** This plan originally split Phase 1 (model + migration) from
> Phase 2 (API + tests) on the premise that the model change was behaviourally neutral. It is not:
> `ClassTypeId` and `InstructorUserId` become `NOT NULL` with foreign keys, so the moment the schema
> lands, the *old* write path — which supplies neither — fails on a constraint violation. A split
> would therefore have left `POST /api/admin/classes` broken between the two phases for no benefit.
> The two were merged; the former Phase 3 became Phase 2.

The load-bearing constraint runs through both: **identity by reference, numbers by copy**
(`prd-v2` FR-007). A navigation property from `Class` to `ClassType` is being introduced against
S-05's explicit advice — it is what makes the read joins idiomatic — so the compile-time barrier that
used to protect the asymmetry is gone. Phase 1's tests replace it.

## Critical Implementation Details

**The navigation property is a read-only affordance.** `Class.ClassType` and `Class.Instructor`
exist so `ClassScheduleQuery` can project the type's name and the trainer's display name in one
statement. **No write path may read `ClassType.DefaultDurationMinutes` or `ClassType.DefaultCapacity`
through them.** `CreateAsync` copies those numbers from the *request*, which the client prefilled;
the server loads the type only to validate that it exists and is active. `UpdateAsync` and
`DuplicateAsync` never touch the type's defaults at all. This is the one rule whose breach is silent,
and `ClassEndpointTests` is what catches it.

**Three columns are removed, not two.** `Name`, `Room` **and** the old free-text `Instructor`
column all stop being read after this slice, and all three are dropped by a second migration in the
same release.

> **Adapted during implementation, twice.** First: the plan originally named only `Name` and `Room`.
> The third was missed because the entity property `Instructor` is *reused* as the navigation to
> `ApplicationUser`, which hides the fact that a `nvarchar(100)` column is left behind.
>
> Second: the plan deferred the drop by one release, per `AGENTS.md`. The product owner then chose to
> drop all three immediately, with the rollback cost stated and accepted. The interim
> `InstructorName` property that carried the renamed column therefore never shipped — the properties
> are simply gone.

**The conflict check has two halves and needs both.** `HasTimeConflictAsync` must keep
`ClassStore`'s existing database query *and* the `db.Classes.Local` / `EntityState.Added` pass.
Removing the room predicate makes the second half matter more than it did: duplicate copies are seven
days apart and could not previously collide, but any future batch that is not weekly would.

**Ordering inside the migration.** The `AlterColumn` tightening `ClassTypeId` to `NOT NULL` must come
after the existing FK is dropped and be followed by re-adding it — EF generates this correctly from
the model, but read the generated file rather than assuming. The `Down` must restore `NOT NULL` on
the three dead columns and loosen `ClassTypeId` back to nullable; because the table is empty, both
directions are safe.

---

## Phase 1: Model, schema and the API

### Overview

The occurrence becomes an instance of a definition, in the database and on the wire at once: required
references replace the typed identity fields, the overlap rule widens to the whole club, and
`ClassEndpointTests` lands in the same commit to pin the invariants that no longer have a
compile-time guard.

### Changes Required:

#### 1. The occurrence entity

**File**: `src/Domain/Scheduling/Class.cs`

**Intent**: Turn the flat class into an instance of a definition. `ClassTypeId` becomes required and
gains a navigation property; a required `InstructorUserId` plus its navigation replaces the free-text
instructor; `Name`, `Room` and the free-text instructor property are removed outright.

**Contract**: `Guid ClassTypeId` (non-nullable), `ClassType ClassType` navigation,
`string InstructorUserId` (Identity's string key), `ApplicationUser Instructor` navigation. No
`Name`, no `Room`, no instructor string. The XML doc on `ClassType` must state that its `Default*`
values are never read through the navigation — that comment is the replacement for the barrier S-05
got from having no navigation at all.

#### 2. EF configuration

**File**: `src/Infrastructure/Persistence/Configurations/ClassConfiguration.cs`

**Intent**: Make the two references required with `Restrict` delete behaviour, relax the three dead
columns, and re-point the overlap index now that `Room` is not part of the predicate.

**Contract**:
- `Name`, `Room` and the instructor string lose their mappings entirely — the columns are dropped.
- `ClassTypeId` required; `HasOne(x => x.ClassType).WithMany().HasForeignKey(x => x.ClassTypeId)`
  with `OnDelete(DeleteBehavior.Restrict)` — the navigation replaces the current `HasOne<ClassType>()`
  form.
- `InstructorUserId` required, max length 450 (Identity's key length);
  `HasOne(x => x.Instructor).WithMany().HasForeignKey(x => x.InstructorUserId)` with
  `OnDelete(DeleteBehavior.Restrict)` — a trainer's account can never be hard-deleted out from under
  a scheduled class.
- `IX_Classes_Room_StartsAt` is **replaced** by `IX_Classes_StartsAt` — the overlap check is now a
  pure time-range scan. Keep `IX_Classes_Status_StartsAt`; it still serves the member window.
- Add `IX_Classes_InstructorUserId` for the FK.

#### 3. Migration

**File**: `src/Infrastructure/Persistence/Migrations/<timestamp>_AddOccurrenceBinding.cs`

**Intent**: Apply the above. No data statements — the table is empty.

**Contract**: TWO migrations. `AddOccurrenceBinding.Up` drops `IX_Classes_Room_StartsAt`, adds
`IX_Classes_StartsAt`, relaxes `Name`, `Room` and `Instructor` to nullable, alters `ClassTypeId` to
`NOT NULL` (dropping and re-adding its FK as EF generates), and adds `InstructorUserId`
`nvarchar(450) NOT NULL` with its FK to `AspNetUsers` and index. `DropDeadClassColumns.Up` then drops
the three relaxed columns; its `Down` restores them as nullable — the state the first migration left
them in, not the `NOT NULL` they carried before S-06. Both directions of both migrations are safe on
an empty table. The second migration's header must state the rollback cost it accepts.

#### 4. The trainer list

**File**: `src/Application/Members/TrainerEndpoints.cs` (new)

**Intent**: Give the occurrence form a list of people it may assign, without handing it the whole
member surface. Admin-only, policy applied at the group.

**Contract**: `GET /api/admin/trainers` returning `IReadOnlyList<TrainerSummary>` where
`TrainerSummary(string Id, string DisplayName)`. Active accounts holding `Trainer` only, ordered by
display name. A new seam `ITrainerQuery` declared alongside, mirroring `IMemberQuery`. Registered in
`Program.cs` next to the other member registrations and mapped next to `MapMemberAdminEndpoints`.

Deliberately not on `ClassEndpoints`: the list is about accounts, not the schedule, and S-07 may want
it too.

#### 5. The trainer query

**File**: `src/Infrastructure/Members/TrainerQuery.cs` (new)

**Intent**: Project active trainers in the database, so `Application` never sees EF Core.

**Contract**: `AsNoTracking`, filtered on `u.Status == AccountStatus.Active` and membership of the
`Trainer` role via the `db.UserRoles join db.Roles` pattern in `MemberQuery.cs:52-66`. Match the role
by `NormalizedName` against `ApplicationRoles.Trainer.ToUpperInvariant()`, not by `Name` — `Name`
carries the display form and is not what Identity indexes.

#### 6. The occurrence contract

**File**: `src/Application/Scheduling/ClassEndpoints.cs`

**Intent**: Replace the typed identity fields with references on the way in, and with resolved values
on the way out. Widen the conflict rule. Validate that the referenced type is active and the
referenced instructor is an active trainer.

**Contract**:
- `ScheduledClass` — `Room` and the free-text `Instructor` are removed. Added: `Guid ClassTypeId`,
  `string InstructorUserId`. `Name` stays but is now **resolved from the type**, and `Instructor`
  stays as a **resolved display name**. `Description` (`string?`, from the type) is added — it is the
  member-facing text `prd-v2` FR-004 introduced and this is its first read surface. `Capacity`,
  `FreeSpots`, `DurationMinutes`, `StartsAt`, `Status` unchanged.
- `ClassRequest` — `Name` and `Room` removed; `Guid ClassTypeId` and `string InstructorUserId` added;
  `DurationMinutes` and `Capacity` stay, because they are the client's prefilled-then-overridable
  copies.
- `ClassFailure` reasons: `room_conflict` → **`time_conflict`** (still 409). New 400 reasons:
  `unknown_class_type`, `inactive_class_type`, `unknown_instructor`, `instructor_not_trainer`,
  `class_type_immutable`. `missing_field` now means a missing type or instructor id. Keep
  `starts_in_past`, `invalid_capacity`, `invalid_duration`, `invalid_weeks`.
- `DuplicateResult.SkippedWeeks` keeps its meaning and its name; only the reason behind a skip
  changed. Update its doc comment.
- `Validate` gains upper bounds matching `ClassTypeEndpoints`: duration 1–480, capacity 1–200. State
  in a comment that these are duplicated from that file deliberately — an occurrence may legitimately
  override its type, so it cannot inherit the type's bounds by reference either.
- **The type is validated but its defaults are never read.** `CreateAsync` loads the type to check it
  exists and `IsActive`, then takes `DurationMinutes` and `Capacity` from the *request*.
- **The type is immutable on an occurrence.** `UpdateAsync` refuses with `400 class_type_immutable`
  when `request.ClassTypeId` differs from the stored one — a refusal rather than a silent ignore, so
  a client bug surfaces. Because the type cannot change, the active-type check runs on **create
  only**: an occurrence whose type was deactivated afterwards stays editable, which is what FR-006
  promises.
- The instructor **is** re-validated on update (it can change), and on create.
- `DuplicateAsync` copies `ClassTypeId`, `InstructorUserId`, `DurationMinutes`, `Capacity` and
  `StartsAt` shifted by `ClubTime.AddLocalDays`. It performs no validation of the type's active state
  — the source occurrence is already valid.
- `ToDto` can no longer build a complete DTO from the entity alone: it needs the type's name and
  description and the instructor's display name. Have `IClassStore.FindAsync` return the entity with
  both navigations loaded, and project from those.

#### 7. The store seam

**File**: `src/Application/Scheduling/ClassEndpoints.cs` (interfaces at the tail) and
`src/Infrastructure/Scheduling/ClassStore.cs`

**Intent**: Rename the conflict check to what it now means, drop the room parameter, and load the
navigations the DTO projection needs.

**Contract**:
- `HasRoomConflictAsync(string room, …)` becomes
  `HasTimeConflictAsync(DateTimeOffset startsAt, int durationMinutes, Guid? excludingId, CancellationToken)`.
  The implementation drops `c.Room == room` from both the database predicate and the
  `db.Classes.Local` pass; everything else — half-open intervals, the `Status == Scheduled` filter,
  the `excludingId` exclusion, the `EntityState.Added` check — is preserved verbatim. Keep and update
  the known-limitation comment about the read-then-write race; it is unchanged in substance.
- `FindAsync` loads `ClassType` and `Instructor` (tracked, as today — `UpdateAsync` mutates what it
  returns).
- Type lookup for validation reuses the existing `IClassTypeStore.FindAsync` rather than adding a
  third seam.
- Instructor validation goes through `UserManager<ApplicationUser>` (`FindByIdAsync` +
  `IsInRoleAsync`) exactly as `MemberAdminEndpoints.GrantTrainerAsync` does, plus a
  `Status == Active` check. No new query seam for a single lookup.

#### 8. The read queries

**File**: `src/Infrastructure/Scheduling/ClassScheduleQuery.cs`

**Intent**: Resolve the name, description and instructor display name through the navigations, in one
database statement.

**Contract**: The anonymous projection gains `c.ClassType.Name`, `c.ClassType.Description` and
`c.Instructor.DisplayName` in place of `c.Name`, `c.Room` and `c.Instructor`. Both `GetScheduleAsync`
and `GetUpcomingForAdminAsync` keep their existing filters and ordering; only the shared
`ProjectAsync` changes. The `FreeSpots = Capacity` comment stays as is — S-08 still replaces one
expression.

#### 9. Integration tests

**File**: `tests/po-prostu-silka.Tests/ClassEndpointTests.cs` (new)

**Intent**: Pin the invariants that the navigation property just removed the compile-time guard from,
plus the rule changes this phase makes. Follow `ClassTypeEndpointTests.cs` for fixture use, auth and
assertion style.

**Contract**: at minimum —
1. Creating an occurrence stores the **request's** capacity, not the type's default, when the request
   overrides it.
2. Editing a `ClassType`'s `DefaultCapacity` afterwards leaves the occurrence's `Capacity` unchanged.
3. Editing a `ClassType`'s `Name` changes the name returned for an **existing** occurrence.
4. A second occurrence overlapping in time is refused `409 time_conflict` on create.
5. The same on edit, and an edit that keeps its own time does **not** conflict with itself.
6. `duplicate` into weeks where one collides returns `Created` < requested with that week in
   `SkippedWeeks`, and the non-colliding copies are persisted.
7. Creating with an inactive `ClassTypeId` is refused `400 inactive_class_type`; **editing** an
   occurrence whose type was deactivated after creation succeeds.
8. Creating with an instructor who lacks the `Trainer` role is refused `400 instructor_not_trainer`;
   with a blocked trainer, `400 unknown_instructor`.
9. Editing an occurrence with a different `ClassTypeId` is refused `400 class_type_immutable`.
10. `GET /api/admin/trainers` returns only active trainers, and includes an account holding both
    `Admin` and `Trainer`.

### Success Criteria:

#### Automated Verification:

- Solution builds warning-free: `dotnet build` from `src/`
- Migration applies against a clean database and reverses: `dotnet ef database update` to the new
  migration, then back to `AddClassTypes`, then forward again
- All tests pass, including the new file: `dotnet test` from the repo root
- No EF Core reference has appeared in `Domain` or `Application`:
  `grep -r "Microsoft.EntityFrameworkCore" src/Domain src/Application` returns nothing

#### Manual Verification:

- `GET /health` reports a healthy database connection after the migration
- `GET /api/admin/trainers` returns the seeded trainer accounts and nothing else
- `POST /api/admin/classes` with a valid type and trainer creates a class; the response carries the
  type's name and the trainer's display name
- Two overlapping start times are refused with `time_conflict` regardless of type

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase. The client is still on the old contract at this point, so the admin class screens
will be broken until Phase 2 — expected, and the reason these phases are not independently
deployable.

---

## Phase 2: The screens

### Overview

The admin form becomes a form of selections, the two lists lose the room, and the empty state that
greets the admin on an empty database tells them what to do first.

### Changes Required:

#### 1. Contract mirrors

**Files**: `src/app/src/app/core/scheduling/class.models.ts`,
`src/app/src/app/core/scheduling/class.service.ts`

**Intent**: Mirror the new `ScheduledClass`, `ClassRequest` and `ClassFailure` field for field.

**Contract**: `ScheduledClass` drops `room`, gains `classTypeId`, `instructorUserId` and
`description: string | null`; `name` and `instructor` keep their names and become resolved values —
say so in the doc comment, because "resolved" is the whole point of the slice. `ClassRequest` drops
`name` and `room`, gains `classTypeId` and `instructorUserId`. The `ClassFailure` union replaces
`room_conflict` with `time_conflict` and adds the five new reasons. `ClassService` is otherwise
unchanged.

#### 2. The trainer mirror

**Files**: `src/app/src/app/core/admin/member-admin.models.ts`,
`src/app/src/app/core/admin/member-admin.service.ts`

**Intent**: A typed `TrainerSummary` and a `getTrainers()` call.

**Contract**: `interface TrainerSummary { id: string; displayName: string }`;
`getTrainers(): Promise<TrainerSummary[]>` hitting `/api/admin/trainers`. It goes on the existing
member-admin service — it is a member-shaped resource, and a whole service for one method is noise.

#### 3. The occurrence form

**Files**: `src/app/src/app/features/admin/classes/class-form.ts`,
`class-form.html`, `class-form.spec.ts`

**Intent**: Replace the name, room and instructor text inputs with a class-type select and a trainer
select; prefill the numbers from the chosen type on create; block the form with a signposted empty
state when there is nothing to choose from.

**Contract**:
- Form controls: `classTypeId` (required), `startsAt`, `durationMinutes`, `instructorUserId`
  (required), `capacity`. No `name`, no `room`.
- `ngOnInit` loads active class types (`ClassTypeService.getAll()`, filtered to `isActive`) and
  trainers in parallel, then — if editing — the class.
- **Prefill fires on type selection, create mode only.** Choosing a type sets `durationMinutes` and
  `capacity` from its `defaultDurationMinutes` / `defaultCapacity`; the admin may then override
  either. It must not fire when the form is populated from an existing class, or the numbers the
  occurrence owns would be silently replaced.
- **The type select is disabled in edit mode** and shows the current type. If that type is inactive,
  append " (nieaktywny)" to its option label; the inactive type must appear in that select even
  though the create-mode list excludes inactive ones.
- Empty states, before the form renders: no active class types → a message and a link to
  `/admin/class-types`; no trainers → a message and a link to `/admin/members`. Both cases hide the
  form entirely rather than disabling a submit button over empty selects.
- `applyFailure` mapping: `time_conflict` → `startsAt` control (replacing the room mapping);
  `starts_in_past` → `startsAt`; `invalid_capacity` → `capacity`; `invalid_duration` →
  `durationMinutes`; `unknown_class_type` / `inactive_class_type` / `class_type_immutable` →
  `classTypeId`; `unknown_instructor` / `instructor_not_trainer` → `instructorUserId`; everything
  else → the form-level banner. Polish copy in the existing register/class-form voice.
- Specs cover: prefill on type change, no prefill when loading an existing class, the two empty
  states, and the `time_conflict` mapping landing on the start-time control.

#### 4. The admin list

**Files**: `src/app/src/app/features/admin/classes/classes.html`,
`src/app/src/app/features/admin/classes/classes.ts`, `classes.spec.ts`

**Intent**: Drop the room from the meta line, and stop any duplicate-result copy from blaming a room.

**Contract**: `{{ row.room }} · {{ row.instructor }} · {{ row.capacity }} miejsc` becomes
`{{ row.instructor }} · {{ row.capacity }} miejsc`. Check `classes.ts`'s skipped-weeks notice text
and re-word it around a time collision. Specs updated for the removed field.

#### 5. The member schedule

**Files**: `src/app/src/app/features/schedule/schedule.html`, `schedule.spec.ts`

**Intent**: Drop the room; the name and instructor now arrive resolved and need no template change
beyond that.

**Contract**: `{{ item.room }} · {{ item.instructor }}` becomes `{{ item.instructor }}`. Leave the
day-grouped layout alone — S-07 replaces it. The existing empty-state block stays as is. Do **not**
surface `description` here; it has no place in a one-line list row, and S-07 owns the schedule's
information design.

### Success Criteria:

#### Automated Verification:

- Frontend unit tests pass: `npm test` from `src/app/`
- Lint and format clean: `npm run quality:check` from `src/app/`
- Frontend builds: `npm run build` from `src/app/`
- Backend tests still pass: `dotnet test` from the repo root

#### Manual Verification:

- On an empty database, `/admin/classes/new` shows the "define a class type first" message with a
  working link, and after adding a type but before granting Trainer, the trainer message
- With a type and a trainer present: selecting a type prefills its values, overriding capacity
  survives the save, and the created class appears on both lists with the type's name and the
  trainer's display name
- Opening an existing class for edit shows the type disabled and the numbers as saved — not
  re-prefilled
- Editing the type's name in `/admin/class-types` changes the name shown on that already-created class
- Creating a second class overlapping in time shows the message on the start-time field, not a banner
- Duplicating across a week that collides reports the skipped week and creates the rest
- The member schedule shows no room anywhere and is comfortable on a phone-width viewport

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful.

---

## Testing Strategy

### Unit Tests:

- Angular specs for the form's prefill rule (fires on selection, not on load), the two empty states,
  and the failure-to-control mapping — these are where a regression is silent.
- No new backend unit tests: this codebase has no unit-test layer, and the invariants worth pinning
  need a real database.

### Integration Tests:

- `ClassEndpointTests.cs` as enumerated in Phase 1, against SQL Server in Testcontainers via the
  existing `IntegrationTestFixture`.
- Items 1–3 are the FR-007 asymmetry and are the reason this file exists at all.

### Manual Testing Steps:

1. `docker compose up -d`, apply migrations, confirm `GET /health`.
2. Sign in as the seeded admin; visit `/admin/classes/new` on an empty database — expect the
   class-type signpost.
3. Create a class type "Joga dla początkujących", 60 min, 12 spots. Return to `/admin/classes/new` —
   expect the trainer signpost.
4. Grant Trainer to an approved account from `/admin/members`. Reload the form — both selects populate.
5. Select the type: duration and capacity prefill. Override capacity to 8, pick a future time, save.
6. Confirm the class appears on `/admin/classes` and on the member schedule with the type's name, the
   trainer's display name, no room, and 8 spots.
7. Edit the class: the type select is disabled, capacity still reads 8.
8. Edit the class type's name; reload both lists — the name changed on the existing class.
9. Edit the class type's default capacity; reload — the class still reads 8.
10. Create a second class overlapping the first — refused on the start-time field.
11. Duplicate the first class 3 weeks forward with a collision planted in week 2 — expect 2 created,
    week 2 reported.
12. Deactivate the type; edit the class's start time — the save succeeds and the select shows
    "(nieaktywny)". Create a new class — the deactivated type is absent from the list.

## Performance Considerations

Two joins are added to the schedule projection. Both are indexed FK lookups against tables holding at
most a handful of types and a hundred accounts, in a query already bounded to a fortnight, so the
~1 s perceived-response NFR is not at risk. Confirm the projection stays a **single** statement — EF's
default `SingleQuery` behaviour applies and nothing in this repo sets `UseQuerySplittingBehavior`, but
an `.Include()` accidentally added alongside the projection would change that.

`IX_Classes_Room_StartsAt` is replaced by `IX_Classes_StartsAt`. The overlap check loses its equality
predicate and becomes a pure range scan — strictly cheaper to plan, and at one-club row counts the
difference is unmeasurable either way.

## Migration Notes

- The `Classes` table is already empty (migration `20260902111715_AddClassTypes`), so tightening
  `ClassTypeId` and adding a required `InstructorUserId` need no backfill and no data decision.
- **Three columns ARE dropped, in this same release:** `Name`, `Room` and the old free-text
  `Instructor`. This is a deliberate exception to `AGENTS.md`, taken by the product owner after the
  cost was stated twice.

  What the exception costs: rollback redeploys the previous artifact but does not roll back the
  schema, so a rollback past this release leaves the pre-S-06 build looking for three columns that no
  longer exist. It does not degrade gracefully — `ClassScheduleQuery` projects all three, so
  `GET /api/classes` and the admin list both fail with "invalid column name" and the member schedule
  stops rendering entirely. Recovery is rolling forward, or running `DropDeadClassColumns.Down` by
  hand.

  What makes it defensible: `Classes` has been empty since `AddClassTypes` cleared it, no real club
  is using the application, and the alternative was carrying three dead columns plus a follow-up
  change nothing would have enforced.
- Both directions of the migration are safe on an empty table; verify `Down` by actually running it
  before committing the phase.

## References

- Requirements: `context/foundation/prd-v2.md` — US-01, FR-008–FR-013, `## Business Logic Changes`,
  `## Constraints & Compatibility`; `context/foundation/prd.md` — FR-011, FR-012 (superseded in part)
- Roadmap item: `context/foundation/roadmap.md` — S-06
- Predecessor slice: `context/archive/2026-09-02-class-type-definitions/plan.md`
- The asymmetry this plan must not break: `src/Domain/Scheduling/ClassType.cs:1-30`
- The navigation-property warning this plan overrides: `src/Domain/Scheduling/Class.cs:70-90`
- Conflict-check implementation to modify: `src/Infrastructure/Scheduling/ClassStore.cs:47-88`
- Role join pattern for `ITrainerQuery`: `src/Infrastructure/Members/MemberQuery.cs:52-66`
- Test fixture pattern: `tests/po-prostu-silka.Tests/ClassTypeEndpointTests.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Model, schema and the API

#### Automated

- [x] 1.1 Solution builds warning-free — 67679b7
- [x] 1.2 Migration applies against a clean database and reverses — 67679b7
- [x] 1.3 All tests pass, including `ClassEndpointTests` — 67679b7
- [x] 1.4 No EF Core reference in `Domain` or `Application` — 67679b7
- [x] 1.9 The dead-column drop applies and reverses — ca9a5b0

#### Manual

- [x] 1.5 `GET /health` reports a healthy database connection — 67679b7
- [x] 1.6 `GET /api/admin/trainers` returns only active trainers — 67679b7
- [x] 1.7 Create with a valid type and trainer resolves the name and display name — 67679b7
- [x] 1.8 Overlapping start times are refused with `time_conflict` — 67679b7

### Phase 2: The screens

#### Automated

- [x] 2.1 Frontend unit tests pass — 41ec4a3
- [x] 2.2 Lint and format clean — 41ec4a3
- [x] 2.3 Frontend builds — 41ec4a3
- [x] 2.4 Backend tests still pass — 41ec4a3

#### Manual

- [x] 2.5 Empty-state signposts appear on an empty database — 41ec4a3
- [x] 2.6 Prefill, override and save round-trip correctly — 41ec4a3
- [x] 2.7 Edit shows the type disabled and the numbers as saved — 41ec4a3
- [x] 2.8 A type rename propagates to an existing class — 41ec4a3
- [x] 2.9 Time conflict lands on the start-time field — 41ec4a3
- [x] 2.10 Duplicate reports the skipped week and creates the rest — 41ec4a3
- [x] 2.11 No room anywhere on the member schedule; comfortable on a phone — 41ec4a3
