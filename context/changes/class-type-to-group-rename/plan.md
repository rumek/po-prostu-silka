# Class Type → Group Rename Implementation Plan

## Overview

Roadmap S-33, outside any milestone. The club's class definitions have been called "typy zajęć" (and
`ClassType` in code) since S-05. From now on they are "grupy" (`ClassGroup`). The rename covers every
layer: the admin's screens and copy, the SPA routes, the API surface and its reason codes, the
`Domain` / `Application` / `Infrastructure` / `Api` code, the database table, column, keys and indexes,
the test-data seeder, the integration tests, the SPA specs and the E2E helpers. Nothing about
behaviour changes. A group keeps every field and rule a class type had: a name unique among active
groups, an optional description, a default duration and capacity that are **copied** onto an
occurrence, deactivation instead of deletion, and immutability on a class once it is built.

Groups also get their own glyph. Today they reuse `repeat`, which already means "Powtórzenia" on the
plan, so one icon carries two meanings.

## Current State Analysis

- **Backend.** About 30 files carry `ClassType` in their name or body:
  - `src/Domain/Scheduling/ClassType.cs`, plus `Class.ClassTypeId` and the `Class.ClassType` navigation (`src/Domain/Scheduling/Class.cs:110,131`).
  - Twelve `src/Application/Scheduling/*ClassType*.cs` files, plus references in `CreateClass`, `UpdateClass`, `ClassRequest`, `ClassRequestValidator`, `ScheduledClass`, `ClassDtoMapping`, `ClassFailure`, `GetClass`, `DuplicateClass`, `CancelClass` and `BookingProtocol`.
  - In `src/Infrastructure`: `ClassTypeStore`, `ClassTypeQuery`, `ClassTypeConfiguration`, `ClassConfiguration`, `AppDbContext.ClassTypes`, `ClassScheduleQuery`, `ClassStore`, `BookingQuery`, and the TestData seeder/generator/names.
  - In `src/Api`: `src/Api/Endpoints/Scheduling/ClassTypeEndpoints.cs`, plus the DI registrations at `src/Api/Program.cs:331-332,432`.
- **Wire contract.** The API group is `/api/admin/class-types` (`ClassTypeEndpoints.cs:39`). The JSON field is `classTypeId` (`ScheduledClass.cs:54`, `ClassRequest.cs:27`, `class.models.ts:9,64`). The reason codes are `unknown_class_type`, `inactive_class_type` and `class_type_immutable` (`ClassFailure.cs:18-19`, `class-failure.ts:37-39`).
- **Database.** Table `ClassTypes` with `PK_ClassTypes` and the filtered unique index `IX_ClassTypes_Name_Active` (`ClassTypeConfiguration.cs:11,36-39`). On `Classes`: column `ClassTypeId`, index `IX_Classes_ClassTypeId`, FK `FK_Classes_ClassTypes_ClassTypeId` (`AppDbContextModelSnapshot.cs:522,550,867`). The last migration is `20260923133910_AddBookingAttendance`.
- **Deploy order.** `.github/workflows/deploy.yml:93-97` applies migrations **before** the deploy, so that "schema ≥ code" holds. `rollback.yml` redeploys an artifact and runs no migration (`context/deployment/deploy-plan.md:553-555`). A schema rename breaks both of those guarantees. The user has accepted that for this one change (see "What We're NOT Doing").
- **SPA.**
  - Files: `core/scheduling/class-type{.models,.service,-failure}.ts`, the folder `features/admin/class-types/` with 8 files, the routes `admin/class-types{,/new,/:id}` (`app.routes.ts:175-192`), and the nav entry `CLASS_TYPES` (`core/layout/navigation.ts:46-51`, icon `repeat`).
  - About 40 Polish strings in 15 files, listed by `grep -rniE "typ(y|u|ów|em)? zaj" src/app/src/app`.
  - Spec assertions in `navigation.spec.ts`, `more.spec.ts`, `empty.spec.ts`, `failure-contract.spec.ts`, `select.spec.ts`, the class-form and overlay specs, and the class-types specs.
  - E2E: `src/app/e2e/support/club.ts:145,156` posts to `/api/admin/class-types` directly. No E2E spec asserts the Polish words.
- **Tests.** Ten integration test files reference `ClassType`, `class-types` or the reason codes. Among them is `tests/po-prostu-silka.Tests/ClassTypeEndpointTests.cs`, and `TestDataSeederTests.cs:318-327` counts `db.ClassTypes`.
- **No hidden caches.** The service worker caches nothing (`ngsw-config.json` `dataGroups: []`), and no outbox payload stores a class-type name or id, so nothing persisted outside the schema carries the old word.
- **"Grupa mięśniowa" does not collide.** It always appears in that full form, and only on the exercise form and in the exercise view (`exercise-form.html:77`, `exercise-view.ts:63`). No screen shows both words.

## Desired End State

- An admin opens **Więcej → Grupy** (`/admin/class-groups`) and sees the list titled "Grupy", with a group glyph in the menu and in the empty state.
- They create one under "Nowa grupa", edit it under "Edytuj grupę", and pick it in the class form's **Grupa** select.
- Every refusal reads in terms of "grupa".
- `/admin/class-types` no longer exists.
- No non-archive source file, spec, E2E helper or living doc contains `ClassType`, `class-type`, `classType`, `class_type` or "typ zajęć". The only exceptions are the migration history (`Migrations/*` before this change, kept by design) and the documents listed as untouched below.
- The database holds `ClassGroups`, `Classes.ClassGroupId` and keys and indexes renamed to match. The existing staging rows survive, because the migration renames and never drops.

### Key Discoveries:

- EF Core scaffolds a renamed entity type as `DropTable` + `CreateTable`. Because of the FK, that would also force a rebuild of `Classes`. The migration must be hand-written as renames. The user waived the data rehearsal, not the rename itself, which costs nothing.
- `repeat` is already the "Powtórzenia" glyph (`shared/plan-parameters/plan-parameters.ts:25`, with an `icon.html` comment that says "one meaning, one icon"). Groups move off it.
- `IconName` + `@case` in `shared/icons/icon.{ts,html}` is the whole contract for a new glyph ("Adding one means adding a case to the template, and nothing else", `icon.ts:3`). The icon primitive is eager, so the glyph costs bundle bytes. AGENTS.md records the bundle size after every eager addition.
- `app.routes.spec.ts` fails if a menu label differs from its route's title. The nav label "Grupy" and the route title "Grupy" must change together.

## What We're NOT Doing

- **No safe-deploy path for the schema rename. This is a one-off exception the user granted on 2026-10-01 because the environment is still staging.** We accept two things:
  - For the few minutes between "Apply migrations" and the new build serving, every screen that reads classes on the old build (schedule, dashboard, bookings, attendance) returns 500.
  - `rollback.yml` cannot restore the previous artifact without first running `dotnet ef database update 20260923133910_AddBookingAttendance` by hand.

  We do not rehearse `Up` or `Down` against a copy of real data. It is not a precedent: the next schema rename goes back to the "schema ≥ code" rule.
- No redirect from `/admin/class-types`.
- No change to behaviour, validation, fields, the filtered-uniqueness rule, the copy semantics of the defaults, or persona gating (groups stay admin-only).
- No edits to `context/foundation/prd-v2.md`, `shape-notes.md`, the roadmap's history and archived slice texts, or anything under `context/archive/`. All of these are versioned as shipped, and the archive is immutable. Existing migration files are not touched either.
- No rename of the muscle-group concept, and no change to the `repeat` glyph's drawing.

## Implementation Approach

Rename outward from the schema, so that each phase leaves a consistent layer behind it. Phase 1
changes the backend and the API contract in one step. Phase 2 moves the SPA and the E2E helper onto
that contract. Phase 3 brings the docs along and runs the full gate. The three phases go into one
branch and one PR. The SPA is broken against the API between phases 1 and 2, so nothing is pushed
until phase 3's E2E run passes. The pre-push hook enforces that anyway.

Rename files with `git mv`, so that history follows them.

## Critical Implementation Details

- **The migration is hand-written, never accepted as scaffolded.** Generate it with
  `dotnet ef migrations add RenameClassTypesToClassGroups --project src/Infrastructure/po-prostu-silka.Infrastructure.csproj --startup-project src/Api/po-prostu-silka.Api.csproj`
  so that the Designer and snapshot are correct. Then replace the body of `Up`/`Down`. `Up` runs in
  this order:
  1. drop `FK_Classes_ClassTypes_ClassTypeId`;
  2. `RenameTable("ClassTypes" → "ClassGroups")`;
  3. `RenameColumn("ClassTypeId" → "ClassGroupId", table "Classes")`;
  4. `RenameIndex` for `IX_Classes_ClassTypeId` and `IX_ClassTypes_Name_Active`;
  5. rename `PK_ClassTypes` → `PK_ClassGroups` with `sp_rename` via `migrationBuilder.Sql`, because EF has no `RenamePrimaryKey`;
  6. re-add `FK_Classes_ClassGroups_ClassGroupId` with the same delete behaviour the snapshot records today.

  `Down` is the exact mirror. Afterwards, `dotnet ef migrations has-pending-model-changes` must report
  none. If it does report changes, a name in the config or the migration disagrees.

  **Adapted during implementation.** The installed `dotnet-ef` is 7.0.4, which has no
  `has-pending-model-changes` command (it arrived in EF Core 8). The equivalent check was run instead:
  `dotnet ef migrations add ZzPendingProbe` scaffolded an empty `Up`/`Down`, and the probe files were
  then deleted by hand (`migrations remove` wants a live database). The snapshot diff is exactly the
  eight renamed names.
- **Names are set explicitly in `ClassGroupConfiguration`** (`ToTable("ClassGroups")`, `HasDatabaseName("IX_ClassGroups_Name_Active")`), the same way they are for the class-type config today. The filter `[IsActive] = 1` is unchanged.

## Phase 1: Backend, API contract and database

### Overview

Every C# name, the API route, the wire field, the reason codes, the seeder and the schema now say
"group". The integration suite proves that behaviour did not change.

### Changes Required:

#### 1. Domain

**File**: `src/Domain/Scheduling/ClassType.cs` → `ClassGroup.cs`; `src/Domain/Scheduling/Class.cs`; `src/Domain/Scheduling/Booking.cs` (doc reference)

**Intent**: Rename the entity, the FK property and the navigation. Rewrite the doc comments to say "group". Keep the asymmetric identity-vs-template explanation word for word apart from the noun, because it is load-bearing.

**Contract**: `public class ClassGroup` (same members). `Class.ClassGroupId : Guid`. `Class.ClassGroup : ClassGroup`.

#### 2. Application

**File**: every `src/Application/Scheduling/*ClassType*.cs` → `*ClassGroup*.cs` (`ActivateClassGroup`, `ClassGroupFailure`, `ClassGroupProjection`, `ClassGroupRequest`, `ClassGroupSummary`, `ClassGroupValidator`, `CreateClassGroup`, `DeactivateClassGroup`, `GetClassGroup`, `GetClassGroups`, `IClassGroupQuery`, `IClassGroupStore`, `UpdateClassGroup`), plus the references in `CreateClass`, `UpdateClass`, `ClassRequest`, `ClassRequestValidator`, `ScheduledClass`, `ClassDtoMapping`, `ClassFailure`, `GetClass`, `DuplicateClass`, `CancelClass`, `BookingProtocol` and `Notifications/ClassChangeNotification.cs` (comment).

**Intent**: A mechanical rename of types, members, locals and comments.

**Contract**:
- `ClassRequest.ClassGroupId` and `ScheduledClass.ClassGroupId` serialise as `classGroupId`.
- `ClassFailure` reasons become `unknown_class_group`, `inactive_class_group` and `class_group_immutable`.
- The `ClassGroupFailure` reasons keep their own names (`name_taken` and so on), because they carry no noun.

#### 3. Infrastructure

**File**: `Scheduling/ClassTypeStore.cs`, `ClassTypeQuery.cs` → `ClassGroup*`; `Persistence/Configurations/ClassTypeConfiguration.cs` → `ClassGroupConfiguration.cs`; `ClassConfiguration.cs`; `AppDbContext.cs`; `Scheduling/ClassScheduleQuery.cs`, `ClassStore.cs`, `BookingQuery.cs`; `TestData/TestDataSeeder.cs`, `TestDataGenerator.cs`, `TestDataNames.cs`

**Intent**: Rename the store, the query, the configuration and `DbSet` (`AppDbContext.ClassGroups`). Point `ClassConfiguration` at `ClassGroupId` / `ClassGroup`. Update the seeder's guard, add and wipe to `db.ClassGroups`, and rename the generator's record field `ClassGroups`.

**Contract**: table `ClassGroups`, index `IX_ClassGroups_Name_Active` (filter unchanged), column `Classes.ClassGroupId`.

#### 4. Migration

**File**: `src/Infrastructure/Persistence/Migrations/<timestamp>_RenameClassTypesToClassGroups.cs` (+ Designer, + snapshot)

**Intent**: Rename the schema objects in place so that every staging row survives. See "Critical Implementation Details" for the order and the hand-written body.

**Contract**: `Up` renames to the `ClassGroup*` names listed above. `Down` restores the exact `ClassType*` names. No `DropTable` or `CreateTable` appears anywhere in the file.

#### 5. Api

**File**: `src/Api/Endpoints/Scheduling/ClassTypeEndpoints.cs` → `ClassGroupEndpoints.cs`; `src/Api/Program.cs`

**Intent**: Rename the class and the `Map…` extension, and move the group to `/api/admin/class-groups` with the same handlers, the same policy and the same sub-routes (`/{id}`, `/{id}/deactivate`, `/{id}/activate`). Update the DI registrations.

**Contract**: `MapClassGroupEndpoints()`, `/api/admin/class-groups`.

#### 6. Integration tests

**File**: `tests/po-prostu-silka.Tests/ClassTypeEndpointTests.cs` → `ClassGroupEndpointTests.cs`; `AdminBookingEndpointTests`, `AttendanceEndpointTests`, `AttendanceHistoryTests`, `BookingEndpointTests`, `ClassCancellationTests`, `ClassEndpointTests`, `MemberClaimTests`, `MembershipPassEndpointTests`, `TestDataSeederTests`

**Intent**: Point the tests at the new routes, field and reason codes, and rename their helpers and assertions. Do not change what any test asserts beyond the names. A test whose assertion has to change in substance is a sign that behaviour drifted.

**Contract**: The test count does not change.

### Success Criteria:

#### Automated Verification:

- Solution builds warning-free: `dotnet build po-prostu-silka.slnx`
- Model and migrations agree: `dotnet ef migrations has-pending-model-changes --project src/Infrastructure/po-prostu-silka.Infrastructure.csproj --startup-project src/Api/po-prostu-silka.Api.csproj` reports no changes
- The new migration contains no `DropTable` or `CreateTable`: `grep -E "DropTable|CreateTable" src/Infrastructure/Persistence/Migrations/*_RenameClassTypesToClassGroups.cs` prints nothing
- Integration tests pass with an unchanged test count: `dotnet test po-prostu-silka.slnx`
- No backend leftovers outside migration history: `grep -rnE "ClassType|class-type|class_type" src/Domain src/Application src/Infrastructure src/Api tests --include=*.cs | grep -v /Migrations/` prints nothing

#### Manual Verification:

- Against the local Docker SQL Server holding existing data: `dotnet ef database update` keeps every group and class row, and running `Down` to `20260923133910_AddBookingAttendance` and back again works

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: SPA, icon and E2E helpers

### Overview

The SPA consumes the renamed contract, the admin reads "Grupy" everywhere, and groups have their own
glyph.

### Changes Required:

#### 1. Core scheduling client

**File**: `src/app/src/app/core/scheduling/class-type.models.ts`, `class-type.service.ts`, `class-type-failure.ts` → `class-group.*`; `class.models.ts`; `class-failure.ts`; `core/http/failure-contract.spec.ts`

**Intent**: Rename the types (`ClassGroup`, `ClassGroupFailure`, `CLASS_GROUP_BOUNDS`, `ClassGroupService`), the URL constant (`/api/admin/class-groups`), the field (`classGroupId`) and the reason keys. Rewrite the messages in the "grupa" wording.

**Contract**: The message tables stay exhaustive over their unions, which `failure-contract.spec.ts` checks. Copy:
- `name_taken`: "Ta nazwa jest już zajęta przez inną aktywną grupę. Wybierz inną."
- unknown: "Nie udało się zapisać grupy. Spróbuj ponownie za chwilę."
- `missing_field`: "Wybierz grupę i prowadzącego."
- the three class reasons: "Nie można użyć tej grupy. Odśwież stronę i spróbuj ponownie."

#### 2. Admin feature folder

**File**: `src/app/src/app/features/admin/class-types/` → `features/admin/class-groups/` (`class-groups.{ts,html,scss,spec.ts}`, `class-group-form.{ts,html,scss,spec.ts}`)

**Intent**: Use `git mv`, then rename the components (`ClassGroups`, `ClassGroupForm`), the selectors, the CSS class prefixes that carry the noun, and the copy.

**Contract**: Copy:
- `h1` "Grupy"
- form `h1` "Nowa grupa" / "Edytuj grupę"
- "Nie udało się wczytać grup." / "Nie udało się wczytać tej grupy."
- empty state "Nie zdefiniowano jeszcze żadnej grupy." with `icon="group"`
- field error "Podaj nazwę grupy."

#### 3. Class screens that reference groups

**File**: `features/admin/classes/classes.{ts,html}`, `class-form.{ts,html,spec.ts}`, `class-create-overlay.{ts,html,spec.ts}`, `classes.spec.ts`, `class-actions-overlay.spec.ts`

**Intent**: Point the links at `/admin/class-groups` and rename the form control `classTypeId` → `classGroupId`, the ids `create-class-type` → `create-class-group`, and the copy.

**Contract**: Copy:
- labels "Grupa" and "Grupy"
- "Zmień szczegóły tego terminu. Grupa zostaje ta sama, żeby zachować jego historię."
- "Najpierw zdefiniuj grupę — zajęcia powstają z definicji." with the action "Przejdź do grup" and `icon="group"`
- "Nie udało się wczytać grup i trenerów."
- "Aby dodać zajęcia, potrzebna jest aktywna grupa i konto z rolą trenera."
- "Wybierz grupę."

#### 4. Routes and navigation

**File**: `src/app/src/app/app.routes.ts`, `core/layout/navigation.ts`, `navigation.spec.ts`, `features/more/more.spec.ts`, `app.spec.ts`

**Intent**: Rename the routes to `admin/class-groups`, `admin/class-groups/new` and `admin/class-groups/:id`, with the titles "Grupy", "Nowa grupa" and "Edytuj grupę", and `parent: '/admin/class-groups'`. Rename `CLASS_TYPES` → `CLASS_GROUPS` with the label "Grupy" and the icon `group`. Fix the explanatory comments.

**Contract**: `app.routes.spec.ts` stays green, which means the menu label equals the route title. There is no entry for the old path.

#### 5. Group glyph

**File**: `src/app/src/app/shared/icons/icon.ts`, `icon.html`

**Intent**: Add `'group'` to `IconName` and a `@case` that draws a small cluster of people in the existing stroke style. It has to be visibly different from `members` and `person`. The comment above it should name the meaning, the same way its neighbours do.

**Contract**: `IconName` gains `'group'`. `repeat` is used only by `plan-parameters` afterwards.

#### 6. Other specs and shared code that mention the old names

**File**: `shared/forms/empty/empty.spec.ts`, `shared/forms/select/select.spec.ts`, `shared/list/{list,row}.ts`, `shared/forms/checkbox/checkbox.{ts,scss}`, `shared/calendar/schedule-calendar.spec.ts`, `features/dashboard/dashboard.spec.ts`, `features/schedule/schedule.spec.ts`, `features/class-bookings/class-bookings-overlay.spec.ts`, `features/admin/exercises/*`, `src/styles.scss`, `tools/eslint-rules/no-hand-rolled-presentational.spec.ts`

**Intent**: Rename fixture fields (`classTypeId`) and update the comments that list "class-types" among screens. Nothing here changes behaviour.

**Contract**: —

#### 7. E2E helper

**File**: `src/app/e2e/support/club.ts`

**Intent**: Post to `/api/admin/class-groups`, send `classGroupId`, and rename the local variables.

**Contract**: The helper's public method names may keep their shape, renamed to "group".

### Success Criteria:

#### Automated Verification:

- Lint and formatting pass: `npm run quality:check` (in `src/app`)
- SPA specs pass: `npm test` (in `src/app`)
- Production build succeeds: `npm run build` (in `src/app`)
- No SPA leftovers: `grep -rnEi "class-?type|class_type|typ(y|u|ów|em)? zaj" src/app/src src/app/e2e src/app/tools` prints nothing

#### Manual Verification:

- As admin on a desktop: Więcej → Grupy shows the list, the group glyph appears in the menu and the empty state, and creating, editing, deactivating and reactivating a group works
- The class form's "Grupa" select lists active groups, and saving a class works
- On a phone width, the pinned app bar reads "Grupy" / "Nowa grupa" / "Edytuj grupę"
- The `group` glyph reads clearly beside `members` in the admin menu

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Living docs and the full gate

### Overview

The docs people work from say "grupa". The bundle cost of the glyph is recorded, and the browser
suite proves that the renamed contract holds end to end.

### Changes Required:

#### 1. Living docs

**File**: `context/foundation/ui-style-guide.md`, `context/foundation/test-plan.md`, `context/testing/01-account-and-access.md`, `context/testing/04-schedule-bookings-attendance.md`, `context/testing/README.md`

**Intent**: Replace "typ zajęć" / "class type" and `/admin/class-types` with "grupa" / "group" and `/admin/class-groups`. Add the `group` glyph to the style guide's icon roles, and state that `repeat` now means only "Powtórzenia".

**Contract**: —

#### 2. Roadmap note and deploy log

**File**: `context/foundation/roadmap.md`, `context/deployment/deploy-plan.md`

**Intent**:
- **Roadmap:** add one line under S-33 that names the migration, and record that the deploy-window outage and the manual-`Down` rollback were accepted once by the user, with no precedent.
- **Deploy plan:** under "Rollback note", add the exact `dotnet ef database update 20260923133910_AddBookingAttendance` step that has to come before rolling back past this release.

**Contract**: The roadmap's history entries and S-05/S-06 texts stay unedited.

#### 3. Bundle measurement

**File**: `AGENTS.md`

**Intent**: Measure the initial bundle after phase 2 and record it in the bundle paragraph with its delta, in the existing "measured at … after …" form, attributed to the `group` glyph.

**Contract**: The initial bundle stays under the 600 kB warning.

### Success Criteria:

#### Automated Verification:

- No leftovers in living docs: `grep -rniE "class.?type|typ(y|u|ów)? zaj" context/foundation/ui-style-guide.md context/foundation/test-plan.md context/testing` prints nothing
- Full backend suite still passes: `dotnet test po-prostu-silka.slnx`
- Browser suite passes locally against Docker SQL Server: `npm run e2e:stage && npm run e2e` (in `src/app`)

#### Manual Verification:

- The recorded bundle figure matches the `npm run build` output
- After the merge to `main`, the staging deploy's "Apply migrations" step succeeds, and `/health` answers `Healthy` once the new build serves

---

## Testing Strategy

### Unit Tests:

- `failure-contract.spec.ts`: the renamed unions still each have a complete table.
- `app.routes.spec.ts`: every renamed route has an identity, and the nav label equals the route title.
- The renamed `class-groups` and `class-group-form` specs carry the same cases as before, with the new copy.

### Integration Tests:

- `ClassGroupEndpointTests`: the same coverage as before. Create, edit, filtered name uniqueness across deactivation, and activate/deactivate.
- `ClassEndpointTests`: the three renamed reason codes are still returned in the same situations (unknown, inactive, and changing the group on edit).
- `TestDataSeederTests`: the seed and the wipe still count groups. This proves the seeder's `ExecuteDeleteAsync` reaches the renamed table.

### Manual Testing Steps:

1. Start from a local database migrated to `AddBookingAttendance` that holds seeded groups and classes. Apply the new migration. The groups and classes are still there.
2. Roll back to `AddBookingAttendance`, then forward again. The data is intact both times.
3. Click through the admin's Grupy screens and the class form at desktop and phone widths.

## Migration Notes

The migration renames in place: a table, a column, two indexes, a PK and an FK. No row moves.
Accepted once, on staging only: while it runs before the deploy, the previous build is serving
against the new names, so screens that read classes fail until the new build is up (about 6–7
minutes on B1). Rolling back past this release requires running `Down` by hand first. Phase 3 records
that step in `deploy-plan.md`.

## References

- Roadmap: `context/foundation/roadmap.md` S-33
- Original slices: S-05 (`class-type-definitions`), S-06 (`occurrences-from-class-types`) in `context/archive/`
- Deploy ordering: `.github/workflows/deploy.yml:93-97`; rollback: `context/deployment/deploy-plan.md:148-151,553-555`
- Icon contract: `src/app/src/app/shared/icons/icon.ts:3`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Backend, API contract and database

#### Automated

- [x] 1.1 Solution builds warning-free — 432c051
- [x] 1.2 Model and migrations agree (no pending model changes) — 432c051
- [x] 1.3 The new migration contains no DropTable or CreateTable — 432c051
- [x] 1.4 Integration tests pass with an unchanged test count — 432c051
- [x] 1.5 No backend leftovers outside migration history — 432c051

#### Manual

- [x] 1.6 Migration Up/Down keeps existing local rows

### Phase 2: SPA, icon and E2E helpers

#### Automated

- [x] 2.1 Lint and formatting pass — bd182c0
- [x] 2.2 SPA specs pass — bd182c0
- [x] 2.3 Production build succeeds — bd182c0
- [x] 2.4 No SPA leftovers — bd182c0

#### Manual

- [x] 2.5 Admin Grupy screens work end to end on desktop
- [x] 2.6 Class form's Grupa select works
- [x] 2.7 Phone app bar shows the new titles
- [x] 2.8 The group glyph reads clearly beside members

### Phase 3: Living docs and the full gate

#### Automated

- [x] 3.1 No leftovers in living docs — e27d0e4
- [x] 3.2 Full backend suite still passes — e27d0e4
- [x] 3.3 Browser suite passes locally — e27d0e4

#### Manual

- [x] 3.4 Recorded bundle figure matches the build
- [ ] 3.5 Staging deploy migrates and /health is Healthy
