# Class Type Definitions Implementation Plan

## Overview

Add a definition layer beneath the schedule. A **class type** is created once by the admin —
name, description, default duration, default capacity — then browsed, edited, and deactivated
(never hard-deleted). This slice ships the entity, its admin API, and its admin screens; it does
**not** yet wire the type into occurrence creation, which is S-06
(`occurrences-from-class-types`).

Two forward-looking pieces land here so S-06 inherits a clean starting point: `Class` gains a
**nullable** `ClassTypeId` foreign key, and the development-only rows in `Classes` are cleared.

Roadmap item: **S-05** in `context/foundation/roadmap.md`.
Requirements: `context/foundation/prd-v2.md` — FR-004, FR-005, FR-006, FR-007.

## Current State Analysis

- **A class has no definition.** `src/Domain/Scheduling/Class.cs` is a flat entity carrying its own
  `Name`, `StartsAt`, `DurationMinutes`, `Room`, free-text `Instructor`, `Capacity`, `Status`,
  `CreatedAt`. Nothing binds this Monday's "Joga dla początkujących" to next Monday's.
- **The admin CRUD pattern is fully established** and this slice copies it almost verbatim:
  - Minimal-API endpoint group with the policy applied at `MapGroup` level, never per endpoint
    (`src/Application/Scheduling/ClassEndpoints.cs:88-99`).
  - A narrow write seam (`IClassStore`) and read seam (`IClassScheduleQuery`) declared in
    `Application`, implemented in `Infrastructure/Scheduling/` — this is how `Application` avoids
    referencing EF Core (AGENTS.md layering).
  - Neither seam saves; the endpoint commits through `IUnitOfWork`
    (`src/Application/Persistence/IUnitOfWork.cs`).
  - Hand-rolled validation returning `record XFailure(string Reason)`; no validation library
    (`ClassEndpoints.cs:377-401`).
  - Enums cross the wire as their **name**, never their int (`ClassEndpoints.cs:10-14`).
- **Entity configuration is auto-discovered.** `AppDbContext.OnModelCreating` calls
  `ApplyConfigurationsFromAssembly` (`src/Infrastructure/Persistence/AppDbContext.cs:33`); a new
  `IEntityTypeConfiguration<T>` under `Configurations/` is picked up with no edit there. A `DbSet`
  property is still needed for the store/query to address the table.
- **`Class` carries no concurrency token, deliberately.** Exactly one admin account is ever seeded
  (`AdminSeeder`), so scheduling writes are last-write-wins and `ClassStore` names "a second admin"
  as the trigger to revisit (`src/Infrastructure/Scheduling/ClassStore.cs:26-35`). `ClassType`
  follows the same reasoning — see Critical Implementation Details.
- **The Angular side mirrors the C# records field-for-field** as a documented contract
  (`src/app/src/app/core/scheduling/class.models.ts:1-4`), behind a promise-based service using
  relative `/api` paths (`class.service.ts`). Screens are a list component plus a `new`/`:id` form
  component, with `'new'` routed **before** `':id'` (`app.routes.ts:36-38`).
- **The list-component conventions are settled** in `features/admin/classes/classes.ts`: `loading` /
  `loadFailed` signals, a per-row `busy` `Set` so one slow row does not disable the list, a
  `failedId` for per-row failure, a list-level `notice`, and a `generation` guard so a refetch that
  resolves late cannot overwrite fresher rows. Inline confirmation, never `confirm()`.
- **The form conventions are settled** in `features/admin/classes/class-form.ts`: one component for
  create and edit distinguished by the route parameter, `FormBuilder.nonNullable.group`, and an
  `applyFailure`/`reject` pair that lands a server refusal on the **control** responsible for it
  rather than in a banner.
- **`Booking` does not exist yet.** There is no `Booking` entity, no `DbSet`, and no table — the
  comments at `ClassEndpoints.cs:251` and `ClassScheduleQuery.cs:58` state this explicitly. The
  PRD's "clear classes and bookings" therefore reduces to clearing `Classes` alone.
- **Admin screens carry no global nav entry.** `app.html` links only `/admin/approvals`;
  `/admin/members` and `/admin/classes` are reached by URL. Nothing links to a class-type screen
  today.
- **No backend test project exists** (AGENTS.md). Frontend tests are Vitest specs sitting beside
  every component.

## Desired End State

An admin signs in, opens `/admin/class-types`, and sees a list of class types. They add
"Joga dla początkujących" with an optional description, a default duration of 60 minutes and a
default capacity of 12. They edit it. They deactivate it — it stays in the list behind the
"pokaż nieaktywne" toggle with a clear badge — and reactivate it. A second active type may not
reuse an active type's name; attempting it is refused on the name field. Nothing about browsing,
creating, editing, duplicating or deleting a class occurrence changes.

Verify by: `dotnet build` clean, `npm test` and `npm run quality:check` green, `GET /health`
answering after the migration, and walking the manual steps in **Testing Strategy** below.

### Key Discoveries:

- The endpoint-group authorization pattern to copy: `ClassEndpoints.cs:88-99` — policy at the
  group, so an endpoint added later cannot ship unauthenticated.
- The Application→Infrastructure seam pair to copy: `ClassEndpoints.cs:405-450` (interfaces) and
  `src/Infrastructure/Scheduling/ClassStore.cs` / `ClassScheduleQuery.cs` (implementations).
- The list-screen skeleton to copy: `features/admin/classes/classes.ts:26-80` (signals, generation
  guard) and `features/admin/members/members.ts` (status badge + filtering).
- The form-screen skeleton to copy: `features/admin/classes/class-form.ts:44-52` (nonNullable
  group with min validators mirroring the server floors) and `:137-170` (`applyFailure`/`reject`).
- Constraint: `Application` must not `using Microsoft.EntityFrameworkCore` — AGENTS.md, enforced by
  convention only.
- Constraint: migrations need a working `Down`; rollback redeploys the previous build but does not
  roll back schema.

## What We're NOT Doing

- **Not wiring the class type into occurrence creation.** No type selector on the class form, no
  duration/capacity prefill, no name resolution through the type. All of that is FR-008/FR-010,
  i.e. S-06.
- **Not making `Class.ClassTypeId` `NOT NULL`.** The column lands nullable so the existing
  `POST /api/admin/classes` keeps working untouched; S-06 tightens it once the form supplies a
  type. See the decision note under Phase 1.
- **Not removing `Class.Room`, `Class.Name`, or the free-text `Class.Instructor`.** FR-010 and
  FR-011 belong to S-06, and the room column's drop deliberately lags a release.
- **Not touching the overlap rule.** It stays room-scoped until S-06 widens it to club-wide.
- **Not hard-deleting class types.** FR-006 rules it out; there is no `DELETE` endpoint.
- **Not adding a member-facing view of class types.** The description reaches members through the
  occurrence in S-06.
- **Not introducing a backend test project.** Standing up test infrastructure is its own decision,
  not a rider on a CRUD slice.
- **Not adding a global nav entry.** Consistent with `/admin/members` and `/admin/classes`; one
  cross-link from the class list is the whole navigation change.

## Implementation Approach

Three phases, each leaving the application working end to end.

Phase 1 is schema-only and additive apart from the data wipe. Phase 2 adds an endpoint group that
is fully exercisable with an HTTP client before any UI exists. Phase 3 adds the two screens that
turn it into a user-visible capability. The slice deliberately mirrors the shape of the shipped
`Class` slice at every level — entity, configuration, seams, endpoints, models, service,
list + form — so the next reader finds one pattern rather than two.

The asymmetric binding FR-007 mandates is *established* here rather than exercised: the type owns
`Name` and `Description` (resolved by reference in S-06), and `DefaultDurationMinutes` /
`DefaultCapacity` are named **`Default…`** precisely so a future reader cannot mistake them for
values an occurrence resolves through. That naming is the guardrail — see Critical Implementation
Details.

## Critical Implementation Details

**Ordering — the wipe must precede the foreign key.** Within the single Phase 1 migration, the
`DELETE FROM Classes` has to run *before* `ClassTypeId` becomes a foreign key with `Restrict`
behaviour, or existing rows are still present when the constraint is introduced. Since the column
is nullable, existing rows would in fact survive it — but ordering the wipe first keeps the
migration's intent readable and is required if S-06 ever pulls the `NOT NULL` tightening forward.

**Irreversibility, accepted knowingly.** `Down` restores the *schema* (drops the column, drops the
table) but cannot restore the deleted `Classes` rows. This is the one place this slice departs from
the repository's reversibility rule. It is accepted because the data is development-only and the
PRD states it is discarded rather than migrated; the departure must be written into the migration
as a comment so the next reader does not read it as an oversight.

**Naming is the guardrail on FR-007.** `DefaultDurationMinutes` and `DefaultCapacity` — not
`DurationMinutes` / `Capacity`. Capacity resolved through the type would let a type edit change the
capacity of a class that already has bookings, which is exactly what the no-overbooking guarantee
cannot survive. The `Default` prefix is what makes the copy-at-creation semantics self-evident at
the S-06 call site.

**The filtered unique index is not a plain unique index.** Uniqueness holds only among active
types, so the index needs `HasFilter("[IsActive] = 1")` in addition to `IsUnique()`. Without the
filter, deactivating a type would hold its name hostage forever.

**Discovered during Phase 1 — filtered indexes constrain raw SQL sessions.** SQL Server refuses any
INSERT/UPDATE/DELETE against a table carrying a filtered index unless `QUOTED_IDENTIFIER` is `ON`,
failing with `Msg 1934`. This never affects the application — `Microsoft.Data.SqlClient` sets the
option `ON` for every connection, so EF Core writes and the migration's own `DELETE FROM [Classes]`
are unaffected. It *does* affect hand-run maintenance: `sqlcmd` defaults the option `OFF`, so any
manual query against `ClassTypes` (or against `Classes`, once S-06 adds a filtered index there)
needs `sqlcmd -I`. Worth knowing before someone reads `Msg 1934` as a broken migration.

**Uniqueness is checked twice, deliberately.** The store checks for a name clash before the write
so the API can return a clean `name_taken` failure, and the filtered index backs it so a race
cannot slip past. As with `ClassStore.HasRoomConflictAsync`, the pre-check is a read-then-write
race that is acceptable only while one admin account exists; unlike the overlap rule, here the
index genuinely closes it — the write fails with a `DbUpdateException` rather than corrupting data.
Do not add a concurrency token to `ClassType`; follow `Class`, and record the same
"revisit when a second admin exists" reasoning.

---

## Phase 1: Model and schema

### Overview

Introduce `ClassType` as a first-class scheduling entity with its configuration and migration, add
the nullable forward-looking foreign key on `Class`, and clear the development-only class rows.

### Changes Required:

#### 1. The entity

**File**: `src/Domain/Scheduling/ClassType.cs` (new)

**Intent**: The definition an occurrence is built from (FR-004). Owns identity — the fields that
resolve by reference — and supplies defaults that are copied, never resolved. Document the FR-007
asymmetry in the class doc comment the way `Class.cs` documents its own load-bearing fields; that
comment is what stops S-06 getting the binding backwards.

**Contract**: `Id: Guid`, `Name: string`, `Description: string?`,
`DefaultDurationMinutes: int`, `DefaultCapacity: int`, `IsActive: bool` (defaulting to `true`),
`CreatedAt: DateTimeOffset`. No `Status` enum — active/inactive is a two-state flag, and an enum
would imply states that FR-006 does not define. `Description` is genuinely nullable, matching the
"optional" decision.

#### 2. The EF configuration

**File**: `src/Infrastructure/Persistence/Configurations/ClassTypeConfiguration.cs` (new)

**Intent**: Map the table, size the string columns, and enforce name uniqueness among active types
only.

**Contract**: table `ClassTypes`; key on `Id`; `Name` required, max length 200 (matching
`ClassConfiguration`'s `Name`); `Description` optional, max length 1000; the three remaining
scalars required; `IsActive` with a database default of `true`. The uniqueness constraint is the
non-obvious part:

```csharp
builder.HasIndex(x => x.Name)
    .IsUnique()
    .HasFilter("[IsActive] = 1")
    .HasDatabaseName("IX_ClassTypes_Name_Active");
```

No second index on `IsActive` — Phase 2 returns both active and inactive in one unbounded call over
a handful of rows, so it would never be used.

#### 3. The DbSet

**File**: `src/Infrastructure/Persistence/AppDbContext.cs`

**Intent**: Expose the table so the store and query can address it. The configuration itself is
auto-discovered; `OnModelCreating` must not grow.

**Contract**: `public DbSet<ClassType> ClassTypes => Set<ClassType>();` beside the existing
`Classes` property.

#### 4. The forward-looking foreign key

**File**: `src/Domain/Scheduling/Class.cs`

**Intent**: Land the column S-06 will populate, without changing any behaviour now. Add a comment
saying it is nullable *on purpose in this slice* and that S-06 tightens it — otherwise the next
reader reads a nullable FK as an unfinished thought.

**Contract**: `public Guid? ClassTypeId { get; set; }` plus, in `ClassConfiguration`, a relationship
to `ClassType` with `DeleteBehavior.Restrict`. `Restrict` rather than `Cascade`: FR-006 rules out
hard deletion of a type, and a cascade that could remove occurrences is the worst available
failure. No navigation property — nothing traverses it in this slice, and adding one now would
invite S-06 to resolve capacity through it.

#### 5. The migration

**File**: `src/Infrastructure/Persistence/Migrations/<timestamp>_AddClassTypes.cs` (generated)

**Intent**: Create `ClassTypes`, add `Classes.ClassTypeId` with its constraint and index, and clear
the development-only class rows so S-06 starts from an empty schedule.

**Contract**: Generated with `dotnet ef migrations add AddClassTypes` from `src/`, then hand-edited
to insert the wipe at the top of `Up` and to carry the irreversibility comment. The wipe is the one
statement EF will not scaffold:

```csharp
// Development-only data, discarded rather than migrated (prd-v2.md, Constraints & Compatibility).
// NARROW BY DESIGN: Classes only. Accounts, roles, statuses and training plans are untouched, and
// no Bookings table exists yet. Down cannot restore these rows — the schema reverses, the data does
// not. Accepted deliberately; no real club is using the application.
migrationBuilder.Sql("DELETE FROM [Classes];");
```

`Down` drops the foreign key, the index and the column from `Classes`, then drops `ClassTypes` —
schema fully reversed.

### Success Criteria:

#### Automated Verification:

- Solution builds warning-free: `dotnet build` from `src/`
- Migration applies cleanly against the Docker SQL Server: `dotnet ef database update`
- Migration reverses cleanly: `dotnet ef database update <previous-migration>` then re-apply
- The model snapshot is regenerated and committed alongside the migration

#### Manual Verification:

- `GET /health` answers after the migration, confirming the connection still opens
- `ClassTypes` exists with the filtered unique index present on `(Name)` where `IsActive = 1`
- `Classes` is empty and `ClassTypeId` is present and nullable
- Creating a class through the existing `/admin/classes/new` form still works unchanged
- Identity tables, member accounts, roles and statuses are untouched

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before
proceeding to the next phase.

---

## Phase 2: The admin API

### Overview

An `Admin`-only endpoint group over `/api/admin/class-types` with the read and write seams behind
it, following the `ClassEndpoints` shape exactly.

### Changes Required:

#### 1. Contracts and endpoints

**File**: `src/Application/Scheduling/ClassTypeEndpoints.cs` (new)

**Intent**: Expose list, fetch-one, create, edit, deactivate and reactivate. Document the DTOs as
contracts the SPA mirrors, as `ClassEndpoints` does — that comment is what stops a rename breaking
two screens silently.

**Contract**:

- `record ClassTypeSummary(Guid Id, string Name, string? Description, int DefaultDurationMinutes, int DefaultCapacity, bool IsActive, DateTimeOffset CreatedAt)`
- `record ClassTypeRequest(string Name, string? Description, int DefaultDurationMinutes, int DefaultCapacity)` — same shape for create and edit, matching `ClassRequest`'s convention that an edit replaces every field. `IsActive` is deliberately **not** on the request: activation is its own endpoint, so a careless edit cannot silently resurrect a deactivated type.
- `record ClassTypeFailure(string Reason)` with reasons `missing_field`, `invalid_duration`, `invalid_capacity`, `description_too_long`, `name_taken`. All `400` except `name_taken`, which is `409` — a conflict with existing state, not bad input, mirroring how `room_conflict` is treated.
- Routes on a group carrying `AuthorizationPolicyNames.Admin` at the group level:
  `GET /`, `GET /{id:guid}`, `POST /`, `PUT /{id:guid}`,
  `POST /{id:guid}/deactivate`, `POST /{id:guid}/activate`.
  Activation is two verbs rather than a boolean field for the same reason the members surface uses
  `block`/`unblock` rather than a status patch.
- `GET /` returns **both** active and inactive, ordered `IsActive` descending then by name. The
  screen filters; a server-side flag would need the client to refetch on every toggle.
- Validation is hand-rolled in a private `Validate(ClassTypeRequest)` returning `IResult?`, exactly
  as `ClassEndpoints.Validate` does. Bounds: duration `1..480`, capacity `1..200`, `Name` required
  after trimming, `Description` trimmed and normalised to `null` when blank, max 1000 chars.
- `Name` and `Description` are trimmed before the uniqueness check and before the write.
- **Activate must re-check uniqueness.** Reactivating a type whose name has since been taken by
  another active type would violate the filtered index and surface as an unhandled
  `DbUpdateException`. Activate calls `IsNameTakenAsync` and returns `409 name_taken` instead.
  Deactivate needs no such check.
- Deactivate and activate are otherwise idempotent — deactivating an already-inactive type is
  `200`, not an error. Nothing is gained by refusing it and the screen would have to handle a
  failure it cannot explain.

#### 2. The seams

**File**: `src/Application/Scheduling/ClassTypeEndpoints.cs` (same file, tail)

**Intent**: Keep `Application` free of EF Core while giving the endpoints exactly the operations
they need. Declared in the same file as the endpoints, matching `ClassEndpoints`.

**Contract**:

- `IClassTypeQuery` — `Task<IReadOnlyList<ClassTypeSummary>> GetAllAsync(CancellationToken)`.
- `IClassTypeStore` — `FindAsync(Guid, CancellationToken)`, `Add(ClassType)`, and
  `Task<bool> IsNameTakenAsync(string name, Guid? excludingId, CancellationToken)`. The
  `excludingId` parameter mirrors `HasRoomConflictAsync`'s: without it every edit that keeps the
  name would conflict with itself. Nothing here saves — the endpoint commits through `IUnitOfWork`.
- `IsNameTakenAsync` matches on active types only, case-insensitively via the database collation
  (SQL Server's default is case-insensitive, so a plain `==` comparison suffices — do **not** call
  `ToLower()`, which would defeat the index).

#### 3. The implementations

**Files**: `src/Infrastructure/Scheduling/ClassTypeStore.cs`,
`src/Infrastructure/Scheduling/ClassTypeQuery.cs` (both new)

**Intent**: EF Core side of the two seams.

**Contract**: `ClassTypeStore(AppDbContext db)` with a **tracked** `FindAsync` (the update and
activate handlers mutate what it returns and expect the change tracker to notice — the same note
`ClassStore.FindAsync` carries) and an `AsNoTracking` `IsNameTakenAsync`.
`ClassTypeQuery(AppDbContext db)` projects straight to `ClassTypeSummary` with `AsNoTracking`,
ordered `IsActive` descending then `Name`.

#### 4. Wiring

**File**: `src/Program.cs`

**Intent**: Register the two seams and map the endpoint group.

**Contract**: scoped registrations for `IClassTypeStore`/`IClassTypeQuery` beside the existing
scheduling registrations, and `app.MapClassTypeEndpoints();` after `app.MapClassEndpoints();`.

### Success Criteria:

#### Automated Verification:

- Solution builds warning-free: `dotnet build` from `src/`
- No EF Core using appears under `src/Application/` or `src/Domain/`:
  `grep -rn "Microsoft.EntityFrameworkCore" src/Application src/Domain` returns nothing
- The API starts and the new routes appear in the OpenAPI document

#### Manual Verification:

- Creating a class type returns `200` with the persisted values; the description round-trips, and a
  blank description comes back as `null`
- Creating a second active type with the same name is refused `409 name_taken`
- Deactivating that first type, then creating a new type with the same name, succeeds
- Reactivating the first type while the new one holds its name is refused `409 name_taken`
- Editing a type keeping its own name succeeds (the `excludingId` path)
- Duration `0`, `481`, capacity `0`, `201`, a blank name, and a 1001-character description are each
  refused `400` with the right reason
- Deactivate then activate round-trips, and a repeated deactivate is not an error
- All six routes answer `401`/`403` for an unauthenticated caller and for a non-admin member

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before
proceeding to the next phase.

---

## Phase 3: The admin screens

### Overview

The list with its badge and "pokaż nieaktywne" toggle, and the create/edit form — plus the models,
the service, the routes, and the specs.

### Changes Required:

#### 1. The wire contract

**File**: `src/app/src/app/core/scheduling/class-type.models.ts` (new)

**Intent**: Mirror the C# records field-for-field, carrying the same "this is a contract, not a
convenience type" doc comment `class.models.ts` opens with.

**Contract**: `ClassTypeSummary`, `ClassTypeRequest`, and a `ClassTypeFailure` whose `reason` is the
union of the five literal strings the API can return.

#### 2. The service

**File**: `src/app/src/app/core/scheduling/class-type.service.ts` (new)

**Intent**: Promise-based HTTP wrapper on relative `/api` paths, catching nothing — a failure has to
reach the screen.

**Contract**: `getAll()`, `getById(id)`, `create(request)`, `update(id, request)`,
`deactivate(id)`, `activate(id)`, each `encodeURIComponent`-ing the id as `ClassService` does.
Both activation calls return the updated `ClassTypeSummary` so the screen can patch the row.

#### 3. The list screen

**Files**: `src/app/src/app/features/admin/class-types/class-types.ts` / `.html` / `.scss` (new)

**Intent**: Browse types (FR-005) and toggle activation (FR-006). Copy the `classes.ts` skeleton
wholesale — `loading` / `loadFailed` / per-row `busy` `Set` / `failedId` / `notice` signals and the
`generation` guard.

**Contract**: a `showInactive` signal (default `false`) driving a computed filtered view over the
full row set; each row shows name, description, the two defaults, and a status badge styled after
the members screen's status badges; an active row offers "Dezaktywuj" and an inactive one
"Aktywuj"; a link to `/admin/class-types/new` and per-row "Edytuj". Activation updates the row in
place from the response rather than refetching the list, but a `name_taken` refusal on activate
sets the list-level `notice` explaining the name is now in use. Empty state when there are no
types, and a distinct one when the filter hides everything.

#### 4. The form screen

**Files**: `src/app/src/app/features/admin/class-types/class-type-form.ts` / `.html` / `.scss` (new)

**Intent**: Create and edit in one component keyed on the route parameter (FR-004, FR-005),
following `class-form.ts`.

**Contract**: `FormBuilder.nonNullable.group` with `name` required, `description` optional with
`Validators.maxLength(1000)`, `defaultDurationMinutes` defaulting to `60` and
`defaultCapacity` to `12`, both with `min`/`max` validators mirroring the server bounds. Declare
those bounds as named constants carrying the "keep in step with the server" comment
`class-form.ts:16-20` uses. An `applyFailure`/`reject` pair maps `name_taken` onto the name control,
`invalid_duration`/`invalid_capacity` onto their numeric controls, `description_too_long` onto the
description control, and anything else to the form-level `error` signal. On success, navigate to
`/admin/class-types`.

#### 5. Routes and navigation

**Files**: `src/app/src/app/app.routes.ts`, `src/app/src/app/features/admin/classes/classes.html`

**Intent**: Reach the screens.

**Contract**: three routes under `[authGuard, adminGuard]` — `admin/class-types`,
`admin/class-types/new`, `admin/class-types/:id` — with `new` **before** `:id`, carrying the same
warning comment the class routes carry. Plus one cross-link from the admin class list header to
`/admin/class-types`, matching how the class list already links to `/admin/classes/new`. No global
nav entry: `/admin/members` and `/admin/classes` have none either, and adding one only for this
screen would be inconsistent.

#### 6. Specs

**Files**: `class-types.spec.ts`, `class-type-form.spec.ts` (new)

**Intent**: Cover the behaviour the screens are actually responsible for, following the depth of
`classes.spec.ts` and `class-form.spec.ts`.

**Contract**: list — renders rows, hides inactive by default, reveals them under the toggle, shows
the badge, calls deactivate/activate and updates the row, marks only the acting row busy, surfaces
a per-row failure, reports a `name_taken` refusal on activate, renders both empty states. Form —
prefills on edit, blocks submit while invalid, posts on create and puts on edit, maps `name_taken`
onto the name control, maps `description_too_long` onto the description control, and shows the
generic error otherwise.

### Success Criteria:

#### Automated Verification:

- Unit tests pass: `npm test` from `src/app/`
- Prettier and ESLint pass: `npm run quality:check` from `src/app/`
- The production build succeeds: `npm run build` from `src/app/`

#### Manual Verification:

- The full proof flow works: create a type with a description, see it in the list, edit it,
  deactivate it, reveal it with the toggle, reactivate it
- Creating a second active type with the same name shows the refusal on the **name** field, not as a
  banner
- Reactivating a type whose name has been taken shows a readable message, not a crash
- The description is genuinely optional — a type saves with it empty and renders without it
- Out-of-range duration or capacity is caught client-side before the request goes out
- The screens are usable on a phone-width viewport
- A non-admin member navigating to `/admin/class-types` is redirected by `adminGuard`
- `/admin/classes` still creates, edits, duplicates and deletes classes exactly as before

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful.

---

## Testing Strategy

### Unit Tests:

Frontend only — there is no backend test project and this slice does not introduce one.

- The list screen's filter behaviour: inactive rows hidden by default, revealed by the toggle
- Activation/deactivation updating one row without refetching, and without disabling the list
- The `name_taken`-on-activate path, which is the slice's sharpest edge
- The form's validator bounds and the failure-to-control mapping, `name_taken` above all

### Integration Tests:

None. Standing up `WebApplicationFactory` and a test database is a separate decision, and there is
no CI workflow to run it in yet (AGENTS.md).

### Manual Testing Steps:

1. `docker compose up -d`, apply the migration, confirm `GET /health`
2. Confirm `Classes` is empty and member accounts, roles and statuses survived
3. Create a class through the existing class form — it must still work with a `null` `ClassTypeId`
4. Create "Joga dla początkujących" (60 min, 12 spots, with a description)
5. Try creating a second active type with the same name — expect the refusal on the name field
6. Edit the first type's description and duration; confirm both persist
7. Deactivate it; confirm it leaves the default list and appears under "pokaż nieaktywne" with a badge
8. Create a *new* active type reusing the deactivated name — expect success
9. Try reactivating the original while the new one holds the name — expect a readable refusal, not a
   crash. Then deactivate the new one and reactivate the original — expect success
10. Repeat steps 4–7 on a phone-width viewport
11. Sign in as a non-admin member and confirm `/admin/class-types` redirects

Step 9 is the sharpest edge in the slice: reactivation can collide with the filtered unique index
even though the activate request carries no name. Phase 2 handles it explicitly; this step is what
proves it.

## Performance Considerations

None material. A single club's class-type list is a handful of rows fetched in one unbounded query,
well inside the PRD's ~1 s perceived-response commitment. `GET /` deliberately returns inactive
types too, which is what makes the toggle instant; if the list ever grew past the low hundreds, the
filter would move server-side, but nothing in this product suggests it will.

## Migration Notes

- One migration: `AddClassTypes`. `Up` clears `Classes`, creates `ClassTypes` with its filtered
  unique index, and adds `Classes.ClassTypeId` (nullable, `Restrict`). `Down` reverses the schema
  fully but cannot restore the deleted rows — documented in the migration, accepted because the data
  is development-only.
- `Class.ClassTypeId` stays nullable through this slice. S-06 populates it and tightens it to
  `NOT NULL` in its own migration, once the class form supplies a type.
- Nothing outside the scheduling context is touched.

## References

- Roadmap item: `context/foundation/roadmap.md` — S-05
- Requirements: `context/foundation/prd-v2.md` — FR-004, FR-005, FR-006, FR-007
- Entity + configuration to mirror: `src/Domain/Scheduling/Class.cs`,
  `src/Infrastructure/Persistence/Configurations/ClassConfiguration.cs`
- Endpoint group + seams to mirror: `src/Application/Scheduling/ClassEndpoints.cs:88`
- Store/query implementations to mirror: `src/Infrastructure/Scheduling/ClassStore.cs`,
  `ClassScheduleQuery.cs`
- List screen to mirror: `src/app/src/app/features/admin/classes/classes.ts`
- Form screen to mirror: `src/app/src/app/features/admin/classes/class-form.ts`
- Status badge + filtering to mirror: `src/app/src/app/features/admin/members/members.ts`
- Recording adaptations: `context/foundation/lessons.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Model and schema

#### Automated

- [x] 1.1 Solution builds warning-free: `dotnet build` from `src/`
- [x] 1.2 Migration applies cleanly against the Docker SQL Server: `dotnet ef database update`
- [x] 1.3 Migration reverses cleanly: `dotnet ef database update <previous-migration>` then re-apply
- [x] 1.4 The model snapshot is regenerated and committed alongside the migration

#### Manual

- [ ] 1.5 `GET /health` answers after the migration, confirming the connection still opens
- [ ] 1.6 `ClassTypes` exists with the filtered unique index present on `(Name)` where `IsActive = 1`
- [ ] 1.7 `Classes` is empty and `ClassTypeId` is present and nullable
- [ ] 1.8 Creating a class through the existing `/admin/classes/new` form still works unchanged
- [ ] 1.9 Identity tables, member accounts, roles and statuses are untouched

### Phase 2: The admin API

#### Automated

- [ ] 2.1 Solution builds warning-free: `dotnet build` from `src/`
- [ ] 2.2 No EF Core using appears under `src/Application/` or `src/Domain/`
- [ ] 2.3 The API starts and the new routes appear in the OpenAPI document

#### Manual

- [ ] 2.4 Creating a class type returns `200`; the description round-trips and a blank one comes back `null`
- [ ] 2.5 A second active type with the same name is refused `409 name_taken`
- [ ] 2.6 Deactivating the first type frees its name for a new type
- [ ] 2.7 Reactivating the first type while the new one holds its name is refused `409 name_taken`
- [ ] 2.8 Editing a type keeping its own name succeeds (the `excludingId` path)
- [ ] 2.9 Out-of-range duration, capacity, blank name and over-long description are each refused `400`
- [ ] 2.10 Deactivate then activate round-trips, and a repeated deactivate is not an error
- [ ] 2.11 All six routes answer `401`/`403` for an unauthenticated caller and a non-admin member

### Phase 3: The admin screens

#### Automated

- [ ] 3.1 Unit tests pass: `npm test` from `src/app/`
- [ ] 3.2 Prettier and ESLint pass: `npm run quality:check` from `src/app/`
- [ ] 3.3 The production build succeeds: `npm run build` from `src/app/`

#### Manual

- [ ] 3.4 The full proof flow works: create, list, edit, deactivate, reveal, reactivate
- [ ] 3.5 A duplicate active name shows the refusal on the name field, not as a banner
- [ ] 3.6 Reactivating a type whose name has been taken shows a readable message, not a crash
- [ ] 3.7 The description is genuinely optional and renders correctly when absent
- [ ] 3.8 Out-of-range duration or capacity is caught client-side
- [ ] 3.9 The screens are usable on a phone-width viewport
- [ ] 3.10 A non-admin member navigating to `/admin/class-types` is redirected by `adminGuard`
- [ ] 3.11 `/admin/classes` still creates, edits, duplicates and deletes classes exactly as before
