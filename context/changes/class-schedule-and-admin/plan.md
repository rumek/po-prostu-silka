# Class Schedule and Admin Class Management (S-03) Implementation Plan

## Overview

Open the `scheduling` bounded context: a `Class` aggregate, admin create / edit / duplicate / delete
with a room double-booking invariant, and a mobile-first day-by-day schedule for active members
(FR-007, FR-011, FR-012).

This is the read surface S-04 books against, so its schema is load-bearing beyond this slice.

## Current State Analysis

Nothing scheduling-related exists. `src/Domain/` holds only membership (`ApplicationUser`,
`AccountStatus`, `ApplicationRoles`, `AuthorizationPolicyNames`) and notifications
(`OutboxMessage`, `OutboxStatus`, `NotificationChannel`, `PushSubscription`). `AppDbContext` has two
non-Identity `DbSet`s. `Class` is the first entity outside Identity that needs full CRUD.

What exists and constrains this work:

- **Aggregate precedent** — `OutboxMessage` (`src/Domain/Notifications/OutboxMessage.cs`): a
  `public class` with a `Guid Id`, settable properties, non-nullable strings defaulting to
  `string.Empty`, no data annotations. All required/length enforcement lives in an
  `IEntityTypeConfiguration<T>` under `Infrastructure/Persistence/Configurations/`, auto-discovered
  by `ApplyConfigurationsFromAssembly` (`AppDbContext.cs:30`).
- **Enum convention** — explicit numeric values, `.HasConversion<int>()`, `0` reserved for the safe
  default, and a doc comment forbidding renumbering because the column persists as an int
  (`AccountStatus.cs:8-14`, `OutboxStatus.cs:7-13`). Enums cross the wire as their **name**, mapped
  in memory after materialisation because `ToString()` has no SQL translation
  (`MemberQuery.cs:64-67`).
- **No repository pattern.** Writes go through narrow, intention-revealing seams declared in
  Application and implemented in Infrastructure — `IPushSubscriptionStore.UpsertAsync/RemoveAsync`
  (`PushEndpoints.cs:102-112`) is the precedent — with the commit via `IUnitOfWork`
  (`src/Application/Persistence/IUnitOfWork.cs`). Reads use the same shape:
  `IMemberQuery`/`IPendingMemberQuery`.
- **Endpoint conventions** — a static class per feature, one `MapGroup` with the policy applied at
  the group so a later endpoint cannot ship unauthenticated (`MemberAdminEndpoints.cs:73-75`),
  request/response records inline at the top of the same file, hand-rolled validation (no
  FluentValidation anywhere), and failures as `record XFailure(string Reason)` returned via
  `Results.Json(..., statusCode: N)` with short snake_case reason tokens.
- **Migrations never run on startup.** CI applies them out-of-band before deploy
  (`.github/workflows/deploy.yml:59-104`); every migration needs a working `Down` because rollback
  redeploys the artifact without reverting schema (AGENTS.md).
- **Frontend** — reactive forms are the decided idiom (S-01 D8, reasoned as scaling to "later slices
  [with] harder forms than these"); `.field` / `.field-error` styling and the server-error→control
  mapping in `register.ts:63-101` are the patterns to follow. Polish locale is wired globally
  (`app.config.ts:19,23`), so `DatePipe` renders "1 września 2026" with no new machinery.

### Key Discoveries:

- **Two frontend firsts land here.** A grep across `src/app/src/app/**` finds **zero** `type="date"`,
  `type="time"`, or `type="number"` inputs, and **no route takes a parameter** — `app.routes.ts` is a
  flat list with no `:id`. This slice establishes both conventions, and S-04/S-07 will inherit them.
- **`.field input` pins `font-size: 1rem`** with the comment *"16px minimum, or iOS Safari zooms the
  viewport on focus"* (`styles.scss:228-229`) — this applies to the new number and datetime inputs.
- **The generation-counter guard** added to `members.ts:60,94,187` during S-02's review is directly
  reusable for any schedule refetch.
- **`freeSpots` has no real source until S-04.** `Booking` does not exist; the field is projected as
  `Capacity - 0` here, with S-04 replacing the zero.
- **Overlap detection needs `DATEADD`.** EF Core translates `DateTimeOffset.AddMinutes(int)` to SQL,
  so the interval comparison can run server-side rather than pulling rows into memory.

## Desired End State

An admin opens `/admin/classes`, creates a class (name, start, duration, room, instructor,
capacity), edits it, duplicates it across the next N weeks, and deletes a mistake. Attempting to put
two classes in the same room at overlapping times is refused. An active member opens the schedule
and sees the next 14 days as day-grouped sections showing name, time, room, instructor and free
spots — no calendar grid.

Verify by: creating two classes in one room at overlapping times (second refused), duplicating one
across 8 weeks where week 3 collides (7 created, week 3 reported skipped), then viewing the member
schedule on a narrow viewport.

## What We're NOT Doing

- **No booking.** `Booking` is S-04. `freeSpots` is `Capacity` here by construction.
- **No class cancellation.** `ClassStatus.Cancelled` is defined but never set — FR-013 and its
  notification are S-05, which the roadmap deliberately pairs.
- **No notification on edit.** FR-011's "edit a booked class triggers class-changed" is S-05.
- **No recurring series.** FR-012's weekly duplication is the deliberate MVP substitute (PRD
  §Non-Goals).
- **No attendance or check-in.** PRD §Non-Goals.
- **No calendar grid** — day-by-day list only (PRD design note on FR-007).
- **No instructor entity or Trainer role.** Instructor is free text (PRD §Non-Goals).
- **No pagination** on either list — consistent with the reasoning already recorded in
  `MemberAdminEndpoints.cs:49-52`.

## Implementation Approach

Three phases: schema first, then the whole API surface, then the whole UI. The backend phases are
merged because the read and write seams share the entity and the same DI registration block; the
frontend phases are merged because the member screen and the admin screens share the DTO and service.

**Scope note.** Duration and room-overlap detection were added during planning at the user's
request. Neither appears in FR-011's field list or the roadmap outcome for S-03, which name only
name / date-time / room / instructor / capacity. They are built here as decided; the cost is a
second schema field and an invariant the API must enforce on four separate paths (create, edit, and
each generated duplicate).

## Critical Implementation Details

**Overlap is a read-then-write race.** The invariant is checked with a query, then the row is
written — two admins creating overlapping classes at the same instant can both pass. No unique index
can express interval overlap, so closing this properly would need serializable isolation, which
`EnableRetryOnFailure` makes awkward (an explicit transaction must go through
`Database.CreateExecutionStrategy()` or it throws at runtime — see the note already recorded in
`MemberAdminEndpoints.cs:149-152`). **Accepted as-is**: exactly one admin account is ever seeded
(`AdminSeeder.cs`), so concurrent admin writes are not a real scenario for this club. Record the
limitation in a comment on the check rather than silently leaving it.

**Timezone.** Everything is `DateTimeOffset` in UTC on the server; the SPA groups into day headings
by the **browser's local date** and renders with `DatePipe`, matching how every existing timestamp in
the app is handled. The server never groups and never hardcodes a timezone **on the read path**.

The one exception is weekly duplication, which must preserve wall-clock time across a DST transition
and therefore has to know which wall clock — see the adaptation note under Phase 2's duplicate
endpoint. `ClubTime` names `Europe/Warsaw` once, for that arithmetic only.

## Phase 1: Class aggregate and persistence

### Overview

The entity, its status enum, its configuration, and the migration — no endpoints yet.

### Changes Required:

#### 1. Class entity

**File**: `src/Domain/Scheduling/Class.cs` (new)

**Intent**: The scheduling context's aggregate. A mutable class with a `Guid` key, following
`OutboxMessage`'s shape exactly — settable properties, non-nullable strings defaulting to
`string.Empty`, no data annotations.

**Contract**: `Id` (Guid), `Name`, `StartsAt` (DateTimeOffset, UTC), `DurationMinutes` (int),
`Room`, `Instructor`, `Capacity` (int), `Status` (ClassStatus), `CreatedAt` (DateTimeOffset).
Instructor is free text — the PRD rules out a Trainer role, so there is no user to link to.

#### 2. Class status enum

**File**: `src/Domain/Scheduling/ClassStatus.cs` (new)

**Intent**: The lifecycle FR-013 will use. Defined now so S-05 adds a transition rather than a
migration plus a rewrite of every read path this slice creates.

**Contract**: `Scheduled = 0`, `Cancelled = 1`, explicit values, with the same
do-not-renumber doc comment `AccountStatus` and `OutboxStatus` carry. **S-03 never sets `Cancelled`**
— say so in the comment so a reader does not hunt for the transition.

#### 3. Entity configuration

**File**: `src/Infrastructure/Persistence/Configurations/ClassConfiguration.cs` (new)

**Intent**: Required flags, max lengths, the enum conversion, and the two indexes the queries need.
Auto-discovered; do not touch `OnModelCreating`.

**Contract**: `Name` required, max 200. `Room` and `Instructor` required, max 100. `Capacity` and
`DurationMinutes` required. `Status` `.HasConversion<int>()` with `.HasDefaultValue(Scheduled)`.
Two indexes, each justified in a comment: one on `StartsAt` for the schedule-window query, and a
composite on `(Room, StartsAt)` for the overlap check.

**Adapted during implementation.** The schedule-window index is `(Status, StartsAt)`, not `StartsAt`
alone. The window query filters `Status == Scheduled` **and** a `StartsAt` range, then orders by
`StartsAt` — with `Status` leading as the equality predicate, one index serves the filter, the range
scan and the ordering, so the query needs no sort step. A bare `StartsAt` index would have left
`Status` as a residual predicate on every row in the window. Same column count, strictly better
coverage; `(Room, StartsAt)` is unchanged.

#### 4. DbSet and migration

**File**: `src/Infrastructure/Persistence/AppDbContext.cs`, plus a new migration under
`src/Infrastructure/Persistence/Migrations/`

**Intent**: Expose `Classes` and generate the schema change.

**Contract**: `public DbSet<Class> Classes => Set<Class>();`. Migration created with
`dotnet ef migrations add AddClassSchedule -p src/po-prostu-silka.csproj -o Infrastructure/Persistence/Migrations`
and applied locally with an explicit `--connection` (the design-time factory's placeholder always
wins otherwise). The `Down` must drop the table and both indexes.

### Success Criteria:

#### Automated Verification:

- Backend builds warning-free: `dotnet build` from `src/`
- Migration applies cleanly against the local Docker SQL Server
- `Down` is reversible: `dotnet ef migrations script <New> <Previous>` generates without error
- `GET /health` returns 200 after the migration

#### Manual Verification:

- The generated migration creates both indexes, not just the table
- `Status` has a default of `0` in the generated SQL, so an insert without it is `Scheduled`

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase.

---

## Phase 2: Class API surface

### Overview

Every endpoint: the member schedule read, the admin list, create / edit / delete, and duplicate —
with the capacity, future-start and room-overlap rules.

### Changes Required:

#### 1. Contracts and read seam

**File**: `src/Application/Scheduling/ClassEndpoints.cs` (new)

**Intent**: The wire contracts and the narrow read interface, following
`MemberAdminEndpoints.cs`'s file layout — records at the top, interfaces at the bottom.

**Contract**: `ScheduledClass(Id, Name, StartsAt, DurationMinutes, Room, Instructor, Capacity,
FreeSpots, Status)` — `Status` as the enum **name**, marked as a contract the SPA mirrors.
`IClassScheduleQuery.GetUpcomingAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken)`
for the member window, and `IClassAdminQuery.GetUpcomingAsync(...)` for the admin list.

`FreeSpots` is `Capacity` in this slice — the booking count is zero because `Booking` does not exist
until S-04. Project it as an explicit `Capacity - 0` expression with a comment naming S-04 as the
real source, so that slice changes one expression and no DTO, template or spec.

#### 2. Read implementations

**File**: `src/Infrastructure/Scheduling/ClassScheduleQuery.cs` (new)

**Intent**: DB-side projection following `MemberQuery`'s shape — `AsNoTracking`, project in SQL,
map the enum to its name in memory after materialising.

**Contract**: Filters `Status == Scheduled` and `StartsAt` within the window, ordered by `StartsAt`.
Uses the `StartsAt` index. The admin query returns the same shape but is not window-bounded to 14
days — the admin needs to see everything upcoming they have scheduled.

#### 3. Write seam

**File**: `src/Application/Scheduling/ClassEndpoints.cs` (interface),
`src/Infrastructure/Scheduling/ClassStore.cs` (new)

**Intent**: The write counterpart, with intention-revealing methods rather than a generic
repository — following `IPushSubscriptionStore`.

**Contract**: `IClassStore` with `AddAsync(Class)`, `FindAsync(Guid)`, `Remove(Class)`, and
`HasRoomConflictAsync(string room, DateTimeOffset startsAt, int durationMinutes, Guid? excludingId,
CancellationToken)`. The conflict check must run in SQL: two classes conflict when they share a room,
both are `Scheduled`, and their `[StartsAt, StartsAt + DurationMinutes)` intervals intersect. EF Core
translates `DateTimeOffset.AddMinutes(int)` to `DATEADD`, so this needs no client-side evaluation.
`excludingId` exists so editing a class does not conflict with itself.

#### 4. Member schedule endpoint

**File**: `src/Application/Scheduling/ClassEndpoints.cs`

**Intent**: The member-facing read (FR-007). First production consumer of the `ActiveMember` policy.

**Contract**: `GET /api/classes` under a group carrying
`RequireAuthorization(AuthorizationPolicyNames.ActiveMember)`. Returns the next 14 days from now, a
flat time-ordered list — the SPA does the day grouping. No parameters: the window is fixed, so there
is nothing for a caller to get wrong.

#### 5. Admin CRUD endpoints

**File**: `src/Application/Scheduling/ClassEndpoints.cs`

**Intent**: FR-011 plus the delete escape hatch for a mistyped class.

**Contract**: A second group `/api/admin/classes` with
`RequireAuthorization(AuthorizationPolicyNames.Admin)`:

- `GET /` — upcoming classes for the admin list.
- `POST /` — create. Validates in order: required fields non-blank; `Capacity >= 1`;
  `DurationMinutes >= 1`; `StartsAt` in the future; then the room-conflict check.
- `PUT /{id}` — edit. Same rules **except the future-start check, which does not apply** — an admin
  must be able to correct a class that has already started. Passes the class's own id as
  `excludingId`.
- `DELETE /{id}` — hard delete. In this slice nothing is booked so it always succeeds; add the
  comment naming S-04 as the slice that adds the has-bookings guard, and S-05 as the one that adds
  cancel for real cancellations (delete is for mistakes, not for cancelling a class members signed
  up for).

Failure reasons: `invalid_capacity`, `invalid_duration`, `starts_in_past`, `room_conflict` — all 400
except `room_conflict`, which is 409. Unknown id is 404.

#### 6. Duplicate endpoint

**File**: `src/Application/Scheduling/ClassEndpoints.cs`

**Intent**: FR-012. Copies one class forward N weeks at the same weekday and time, skipping weeks
where the room is already taken rather than failing the whole batch.

**Contract**: `POST /api/admin/classes/{id}/duplicate` taking `{ weeks: int }`, validated `1..8`
(`invalid_weeks`, 400). Each copy is `StartsAt.AddDays(7 * n)` — use `AddDays`, not month or week
arithmetic, so the local clock time is preserved across a DST boundary. Every copy is conflict-checked
independently; copies that pass are created, copies that collide are skipped.

**Adapted during implementation.** `AddDays` does **not** preserve local clock time — that assertion
was wrong. On a UTC `DateTimeOffset` it preserves the *instant*, so a 22:34 Warsaw class duplicated
8 weeks forward landed at 21:34 Warsaw once Poland left DST on 2026-10-25 (measured: weeks 0–7 at
22:34 CEST, week 8 at 21:34 CET). Right instant, wrong wall clock, and nothing failed.

This exposed a contradiction the plan itself carried: "Critical Implementation Details" states *"The
server never groups and never hardcodes a timezone"*, while criterion 2.14 requires duplicates to
keep local clock time across DST. With UTC storage those cannot both hold — preserving a wall clock
across a transition requires knowing *which* wall clock.

Resolved by narrowing the no-timezone rule to the **read** path, which is where it was actually
earning its keep. `src/Domain/Scheduling/ClubTime.cs` names `Europe/Warsaw` once and is used **only**
by the duplicate arithmetic: convert to club-local, add days there, convert back to UTC. It also
handles the two DST edge cases `TimeZoneInfo` otherwise mishandles — an invalid local time in the
spring-forward gap (throws) and an ambiguous one in the autumn repeat (silently picks). The schedule
endpoint still returns UTC instants and the SPA still groups by the browser's local date, so a member
abroad still sees their own clock.

Returns a per-week outcome so the admin sees exactly what happened:
`DuplicateResult(int Created, IReadOnlyList<int> SkippedWeeks)`. All copies land in one
`SaveChangesAsync` — a partially-created batch that then fails to save must not leave some weeks
committed.

#### 7. DI registration

**File**: `src/Program.cs`

**Intent**: Register the three new seams and map the endpoint groups.

**Contract**: `AddScoped` for `IClassScheduleQuery`, `IClassAdminQuery`, `IClassStore`, next to the
existing member-query registrations; `app.MapClassEndpoints();` alongside `MapMemberAdminEndpoints()`.

### Success Criteria:

#### Automated Verification:

- Backend builds warning-free: `dotnet build` from `src/`
- `GET /api/classes` returns 200 for an active member, 403 for a pending member, 401 anonymous
- `POST /api/admin/classes` creates a class and returns its id; 403 for a non-admin
- Capacity 0 returns 400 `invalid_capacity`; duration 0 returns 400 `invalid_duration`
- A create with a past `StartsAt` returns 400 `starts_in_past`
- An edit with a past `StartsAt` succeeds (the rule is create-only)
- A second class in the same room at an overlapping time returns 409 `room_conflict`
- The same class edited to keep its own time does NOT self-conflict
- A class in a different room at the same time is accepted
- `DELETE /api/admin/classes/{id}` returns 204 and the class disappears from `GET /api/classes`
- Duplicate across 8 weeks where one week collides returns `Created: 7` and that week in `SkippedWeeks`
- Duplicate with `weeks: 0` or `weeks: 99` returns 400 `invalid_weeks`

#### Manual Verification:

- `GET /api/classes` excludes classes more than 14 days out and any already in the past
- Duplicated classes keep the same local clock time across a DST boundary
- The overlap query runs in SQL (visible as `DATEADD` in the EF logs), not client-side

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase.

---

## Phase 3: Schedule and admin screens

### Overview

The member's day-grouped schedule and the admin's list plus create/edit form — establishing the
date/time/number input and parameterized-route conventions.

### Changes Required:

#### 1. DTOs and service

**File**: `src/app/src/app/core/scheduling/class.models.ts`, `class.service.ts` (new)

**Intent**: Mirror the API records and wrap the endpoints, following `member-admin.service.ts` —
relative `/api` paths, `firstValueFrom`, nothing catches.

**Contract**: `ScheduledClass` with `status: 'Scheduled' | 'Cancelled'` as the enum name union.
`ClassFailure` covering `invalid_capacity | invalid_duration | starts_in_past | room_conflict |
invalid_weeks`. Service methods `getSchedule()`, `getAdminClasses()`, `create()`, `update()`,
`remove()`, `duplicate(id, weeks)`.

#### 2. Member schedule screen

**File**: `src/app/src/app/features/schedule/schedule.ts`, `.html`, `.scss` (new)

**Intent**: FR-007's day-by-day list. Groups the flat API response into day sections in a computed
signal, by the browser's local date.

**Contract**: Loading / load-failed / empty states as signals, matching `approvals.ts` and
`members.ts`. A `computed` producing `{ day: Date, classes: ScheduledClass[] }[]`, grouped by local
calendar date — the API never groups. Day headings via `DatePipe` (`'EEEE, d MMMM'`); times as
`'HH:mm'`. Each row shows name, time, duration, room, instructor and free spots. Polish copy. Route
`/schedule` behind `[authGuard, activeMemberGuard]`.

#### 3. Admin class list

**File**: `src/app/src/app/features/admin/classes/classes.ts`, `.html`, `.scss` (new)

**Intent**: The admin's management list with per-row edit / duplicate / delete.

**Contract**: Same list/state shape as `members.ts`, including the per-row busy `Set` and the
generation guard for refetches. Duplicate opens a small inline weeks input (1–8) rather than a new
route. After a duplicate, surface the result — "utworzono 7, pominięto tydzień 3" — because a
partial success that silently reports "done" is the failure mode this endpoint's contract exists to
avoid. Delete asks for confirmation inline (no `confirm()` dialog — it blocks the event loop and
there is no dialog primitive in this codebase). Route `/admin/classes` behind
`[authGuard, adminGuard]`.

#### 4. Admin class form

**File**: `src/app/src/app/features/admin/classes/class-form.ts`, `.html`, `.scss` (new)

**Intent**: Create and edit in one component, distinguished by the presence of a route parameter.
This is the app's first parameterized route and first date/time/number inputs.

**Contract**: Reactive form (`FormBuilder.nonNullable.group`) with name, startsAt, durationMinutes,
room, instructor, capacity. Client validators mirror the server: `Validators.required` throughout,
`Validators.min(1)` on capacity and duration. Mirrored constants get the
`/** Keep in step with <server file>. */` comment `MIN_PASSWORD_LENGTH` established
(`register.ts:14-15`).

`<input type="datetime-local">` for the start, `type="number"` for capacity and duration — all
inside the existing `.field` wrapper so they inherit the 16px minimum that stops iOS Safari zooming
on focus (`styles.scss:228-229`). `datetime-local` has no timezone: read the local value and convert
to UTC on submit, and convert back to local when loading an existing class for edit. Get this
backwards and every edited class silently shifts by the UTC offset.

Server failures map onto controls via the `applyFailure`/`reject` pattern
(`register.ts:63-101`): `room_conflict` sets a custom error on the room control with a message
naming the clash; `starts_in_past` on the start control. Routes `/admin/classes/new` and
`/admin/classes/:id`, both behind `[authGuard, adminGuard]`, reading `ActivatedRoute` for the id.

#### 5. Routes and navigation

**File**: `src/app/src/app/app.routes.ts`

**Intent**: Register four routes; paths stay English per S-01 D10.

**Contract**: `/schedule`, `/admin/classes`, `/admin/classes/new`, `/admin/classes/:id`. Declare
`new` before `:id` so the literal segment is not swallowed by the parameter.

#### 6. Specs

**File**: `schedule.spec.ts`, `classes.spec.ts`, `class-form.spec.ts`, `class.service.spec.ts` (new)

**Intent**: Cover what is easy to get wrong, following `members.spec.ts` and `register.spec.ts`.

**Contract**: Assert at minimum — the schedule groups classes into day sections by local date and a
class near midnight lands in the right day; the empty state is explicit; a duplicate reporting
skipped weeks surfaces them; `room_conflict` lands on the room control rather than a banner;
`datetime-local` round-trips local→UTC→local without drift; the `new` route renders an empty form
while `:id` loads an existing class.

### Success Criteria:

#### Automated Verification:

- Unit tests pass: `npm test` from `src/app/`
- Lint and formatting pass: `npm run quality:check` from `src/app/`
- Production build succeeds: `npm run build` from `src/app/`

#### Manual Verification:

- The schedule shows day headings in Polish with classes grouped correctly beneath
- A class starting at 23:30 local appears under that day, not the next
- Creating, editing, duplicating and deleting all work end to end against the running API
- A room conflict shows an error on the room field, not a generic banner
- Editing a class does not shift its start time by the UTC offset
- The schedule and both admin screens are usable at mobile width (PRD mobile-first NFR)
- `/admin/approvals` and `/admin/members` still work unchanged

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful.

---

## Testing Strategy

### Unit Tests:

Frontend only — the backend has no test project (AGENTS.md), so backend correctness rests on
`dotnet build` plus the endpoint checks in Phase 2.

- Day grouping by local date, including a near-midnight class
- Duplicate result reporting skipped weeks
- `room_conflict` mapping onto the room control
- `datetime-local` local→UTC→local round-trip
- Create vs edit mode selected by route parameter

### Integration Tests:

None automated. The overlap invariant and the duplicate partial-success path are verified through
the Phase 2 endpoint checks against the running API and local SQL Server.

### Manual Testing Steps:

1. Create a class; confirm it appears on `/schedule` under the right day heading.
2. Create a second class, same room, overlapping time — confirm the room field shows the conflict.
3. Create a third in a different room at the same time — confirm it is accepted.
4. Edit the first class's time and save; confirm no UTC-offset drift.
5. Duplicate it across 8 weeks with a known collision in one week; confirm the reported counts.
6. Delete a class; confirm it leaves the member schedule.
7. Set a class to 23:30 local; confirm it groups under that day, not the next.
8. View `/schedule` at 375px width.
9. Confirm a pending member is refused `/schedule` and a non-admin cannot reach `/admin/classes`.

## Performance Considerations

The schedule window query is covered by the `StartsAt` index and returns a fortnight of a single
club's classes — tens of rows. The overlap check is covered by the `(Room, StartsAt)` composite
index and runs once per create/edit and once per duplicated week (at most 8).

No pagination on either list, consistent with the reasoning already recorded for the pending queue.

## Migration Notes

One additive migration creating the `Classes` table and its two indexes. Purely additive, so the
`Down` is a clean drop and rollback carries no data-loss risk — unlike a destructive change, this one
does not lag a release.

Applied by CI before deploy (`deploy.yml:59-104`), never on startup.

## References

- Roadmap slice: `context/foundation/roadmap.md` §S-03
- PRD: `context/foundation/prd.md:86-101` (FR-007, FR-011, FR-012), §Non-Goals
- Aggregate pattern: `src/Domain/Notifications/OutboxMessage.cs`,
  `src/Infrastructure/Persistence/Configurations/OutboxMessageConfiguration.cs`
- Write-seam pattern: `src/Application/Notifications/PushEndpoints.cs:102-112`
- Read-seam pattern: `src/Infrastructure/Members/MemberQuery.cs:16-70`
- Endpoint + failure conventions: `src/Application/Members/MemberAdminEndpoints.cs`
- Form + server-error mapping: `src/app/src/app/features/auth/register/register.ts:63-101`
- List screen + generation guard: `src/app/src/app/features/admin/members/members.ts`
- Lessons: `context/foundation/lessons.md` — record any necessary deviation in this plan as part of
  the same phase, not only in a commit message

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Class aggregate and persistence

#### Automated

- [x] 1.1 Backend builds warning-free: `dotnet build` from `src/` — d457ebc
- [x] 1.2 Migration applies cleanly against the local Docker SQL Server — d457ebc
- [x] 1.3 `Down` is reversible: `dotnet ef migrations script <New> <Previous>` generates without error — d457ebc
- [x] 1.4 `GET /health` returns 200 after the migration — d457ebc

#### Manual

- [x] 1.5 The generated migration creates both indexes, not just the table — d457ebc
- [x] 1.6 `Status` has a default of `0` in the generated SQL — d457ebc

### Phase 2: Class API surface

#### Automated

- [x] 2.1 Backend builds warning-free: `dotnet build` from `src/` — 60184a6
- [x] 2.2 `GET /api/classes` returns 200 active member, 403 pending, 401 anonymous — 60184a6
- [x] 2.3 `POST /api/admin/classes` creates and returns an id; 403 for a non-admin — 60184a6
- [x] 2.4 Capacity 0 returns 400 `invalid_capacity`; duration 0 returns 400 `invalid_duration` — 60184a6
- [x] 2.5 Create with a past `StartsAt` returns 400 `starts_in_past` — 60184a6
- [x] 2.6 Edit with a past `StartsAt` succeeds (the rule is create-only) — 60184a6
- [x] 2.7 Overlapping class in the same room returns 409 `room_conflict` — 60184a6
- [x] 2.8 A class edited to keep its own time does not self-conflict — 60184a6
- [x] 2.9 A class in a different room at the same time is accepted — 60184a6
- [x] 2.10 `DELETE` returns 204 and the class leaves `GET /api/classes` — 60184a6
- [x] 2.11 Duplicate across 8 weeks with one collision returns `Created: 7` and that week skipped — 60184a6
- [x] 2.12 Duplicate with `weeks: 0` or `weeks: 99` returns 400 `invalid_weeks` — 60184a6

#### Manual

- [x] 2.13 `GET /api/classes` excludes classes beyond 14 days and any in the past — 60184a6
- [x] 2.14 Duplicated classes keep the same local clock time across a DST boundary — 60184a6
- [x] 2.15 The overlap query runs in SQL (`DATEADD` visible in EF logs), not client-side — 60184a6

### Phase 3: Schedule and admin screens

#### Automated

- [x] 3.1 Unit tests pass: `npm test` from `src/app/`
- [x] 3.2 Lint and formatting pass: `npm run quality:check` from `src/app/`
- [x] 3.3 Production build succeeds: `npm run build` from `src/app/`

#### Manual

- [x] 3.4 The schedule shows Polish day headings with classes grouped beneath
- [x] 3.5 A class starting at 23:30 local appears under that day, not the next
- [x] 3.6 Create, edit, duplicate and delete all work end to end
- [x] 3.7 A room conflict shows an error on the room field, not a generic banner
- [x] 3.8 Editing a class does not shift its start time by the UTC offset
- [x] 3.9 The schedule and both admin screens are usable at mobile width
- [x] 3.10 `/admin/approvals` and `/admin/members` still work unchanged
