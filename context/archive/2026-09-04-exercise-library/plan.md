# Exercise Library Implementation Plan

## Overview

Build the admin-facing exercise library (roadmap **S-10**, `prd.md` FR-018 and FR-019): an
`Exercise` entity carrying a required name plus seven optional descriptive fields and a YouTube
video, an admin API under the `Admin` policy, and four screens — list, detail, create and edit.
This is the head of the training bounded context and the prerequisite for S-11 (training plans),
which is where members will eventually reach an exercise from.

## Current State Analysis

- **Nothing in this domain exists.** No `Exercise`, no `TrainingPlan`, no `src/Domain/Training/`
  folder; confirmed in `context/changes/exercise-library/research.md` and
  `context/archive/2026-09-01-member-management/frame.md:46`.
- **A complete admin-CRUD vertical exists to copy**: `ClassType` (S-05) — entity
  (`src/Domain/Scheduling/ClassType.cs:28-66`), EF configuration with a filtered unique index
  (`src/Infrastructure/Persistence/Configurations/ClassTypeConfiguration.cs:7-41`), a minimal-API
  group with authorization applied at `MapGroup` and hand-rolled validation
  (`src/Application/Scheduling/ClassTypeEndpoints.cs:113-131,318-351`), `IClassTypeQuery` /
  `IClassTypeStore` seams keeping EF Core out of `Application` (`:375-404`), Angular list + form
  (`features/admin/class-types/`), and integration tests
  (`tests/po-prostu-silka.Tests/ClassTypeEndpointTests.cs`).
- **Two things have no precedent anywhere in the repo**: displaying an image (no `<img>`, no
  `NgOptimizedImage`, no `DomSanitizer` usage in the entire frontend) and parsing a URL (`Uri.`,
  `UriKind`, `TryCreate` appear nowhere in `src/`).
- **A read-only detail screen is a new shape.** Every existing admin feature is list + form.
- **No CSP is set anywhere** — no header in `src/Program.cs`, no `<meta http-equiv>` in
  `src/app/src/index.html` (verified during planning) — and `src/app/ngsw-config.json` has empty
  `assetGroups` and `dataGroups`, so the service worker does not intercept cross-origin requests.
  The YouTube iframe and `img.youtube.com` thumbnail therefore need no infrastructure change.
- **`GET /` on an admin group returns active *and* inactive rows**, and the screen filters
  client-side (`ClassTypeEndpoints.cs:134-143`). This slice reuses that, and it is also what makes
  the muscle-group suggestions free.

## Desired End State

An admin opens `/admin/exercises` and sees a card list of exercises: name, a truncated description,
a muscle-group badge and a 16:9 YouTube thumbnail (or a neutral placeholder when there is no video).
A "pokaż nieaktywne" toggle reveals deactivated exercises with a badge. "Dodaj ćwiczenie" opens a
form whose muscle-group and difficulty inputs suggest values already used in the library while still
accepting anything typed. Pasting any common YouTube link shape is accepted; an unrecognised link is
refused on the video field itself, never as a banner. Clicking an exercise opens `/admin/exercises/:id`
— every field laid out for reading, with the video playing in an embedded player and an "Edytuj"
button leading to `/admin/exercises/:id/edit`. An exercise is deactivated, never deleted, and a
deactivated name becomes available again.

Verify by: `dotnet test` (server), `npm test` + `npm run quality:check` (client), and the manual
checklists per phase.

### Key Discoveries:

- The `ClassType` vertical is a 1:1 template at every layer — see the file list above; introducing a
  repository abstraction, a validation library or `ProblemDetails` would be the deviation, not the
  improvement (`context/changes/exercise-library/research.md`, "Architecture Insights").
- Failure bodies are flat `{ "reason": "snake_case" }` records, `400` for validation and `409` for
  conflict, and the Angular form maps each reason onto the control that owns it via `applyFailure()`
  (`features/admin/class-types/class-type-form.ts:148-174`). Routing a refusal to a banner instead
  has been a review finding twice.
- Server bound, EF column length and Angular validator are three copies of the same number, glued
  only by "keep the two in step" comments (`ClassTypeEndpoints.cs:108-111`). A missing bound on one
  side is the single most repeated cause of a deterministic 500 in this repo's review history.
- `IsActive` is deliberately absent from the request DTO so an edit cannot silently resurrect a
  deactivated row (`context/archive/2026-09-02-class-type-definitions/plan.md:313`).
- Tests share one Testcontainers SQL Server with **no reset between tests**; collisions are avoided
  with `UniqueName(prefix) => $"{prefix}-{Guid.NewGuid():N}"`
  (`tests/po-prostu-silka.Tests/ClassTypeEndpointTests.cs:17-40`).
- Pure unit tests without a database already live in the same test project
  (`ClubTimeTests.cs`, `WebPushPayloadTests.cs`) — the video-id parser belongs there.
- Angular routes list the literal `new` segment before `:id`, guards compose as
  `canActivate: [authGuard, adminGuard]`, and simple admin screens are imported eagerly
  (`src/app/src/app/app.routes.ts:23-60`).

## What We're NOT Doing

- **No top-menu entry.** Decided 2026-09-04 (`change.md`): the library is reached by URL, exactly
  like `/admin/members`, `/admin/classes` and `/admin/class-types` today. `src/app/src/app/app.html`
  is not touched. Surfacing admin entries is S-12's problem.
- **No member-facing surface.** `prd.md:163` cuts standalone library browsing for members; FR-020
  reaches an exercise from within a training plan, which is S-11.
- **No training plans, no `TrainingPlanExercise`, no assignment** — S-11.
- **No hard delete.** No `DELETE` route, no `Remove` on the store; a test pins the absence.
- **No muscle-group or difficulty dictionary entity**, no enum, no controlled vocabulary — both are
  free text with suggestions (see Decisions).
- **No image upload or self-hosted media.** `prd.md:176` — video is external, and the only image is
  the thumbnail YouTube derives from the video id.
- **No search, filtering, sorting controls or pagination on the list.** The only filter is the
  active/inactive toggle the `ClassType` list already establishes.
- **No CSP work.** None exists to amend; adding one is its own change.
- **No seed data or import path.** Who enters the initial content stays open (`roadmap.md:253`,
  `prd.md:183`) and is not a build blocker.
- **No concurrency token.** Consistent with `ClassType`, on the same single-seeded-admin grounds; a
  second admin is the trigger to revisit.

## Implementation Approach

Copy the `ClassType` vertical into a new **training** bounded context (`src/Domain/Training/`,
`src/Application/Training/`, `src/Infrastructure/Training/`, `src/app/src/app/core/training/`,
`features/admin/exercises/`), changing only what this slice genuinely adds:

1. **A pure video-id parser in `Domain`.** The admin pastes a link in any common shape; the system
   stores only the 11-character id. Parsing at the write boundary means an unrecognised link is a
   `400` on the video field rather than a broken image discovered later, and it gives both the
   thumbnail and the embed URL a single, trustworthy source. The parser is pure and has no
   dependencies, so it lives in `Domain` and is unit-tested without a database.
2. **The same six-route admin group** under the `Admin` policy at `MapGroup`, with hand-rolled
   validation and one `reason` per rule.
3. **Four routes on the client** instead of three, because the detail screen is read-only and the
   form is separate. The detail screen is the only new component shape; the list and the form are
   restatements of `class-types` with more fields.
4. **Suggestions without a new endpoint.** The form fetches the same unfiltered
   `GET /api/admin/exercises` the list uses and derives distinct muscle groups and difficulties
   client-side into a `<datalist>`. A failed fetch leaves the suggestions empty and never blocks the
   form.

Phases 1–3 leave a fully usable feature (create, edit, list, deactivate). Phase 4 adds the detail
screen and the embedded player — the only piece with no precedent — so it can be judged on its own.

## Decisions

Recorded here because the plan must not carry open questions; the rationale for each is in
`plan-brief.md`'s decisions table.

| Area | Decision |
| --- | --- |
| Muscle group | Free-text `nvarchar(100)`, optional, with `<datalist>` suggestions derived from values already in the library. |
| Difficulty | Same treatment as muscle group (free text + suggestions), for consistency and because nothing reads it programmatically yet. |
| YouTube | Server parses the pasted link and stores **only** `VideoId`; an unrecognised link is `400 invalid_video_url` on the field. |
| Video URL round-trip | The edit form shows a canonical `https://www.youtube.com/watch?v=<id>` rebuilt from the stored id, not the string the admin originally pasted. |
| Thumbnail | `https://img.youtube.com/vi/<id>/mqdefault.jpg`, composed client-side from `videoId`; the API returns no derived URLs. |
| Player | `https://www.youtube-nocookie.com/embed/<id>` in an iframe, on the detail screen only. |
| Lifecycle | Deactivate/activate like `ClassType`; no `DELETE`. |
| Name uniqueness | Unique among **active** exercises via a filtered unique index; `409 name_taken`. |

## Critical Implementation Details

**Angular blocks an iframe `[src]` binding.** A resource URL must pass through
`DomSanitizer.bypassSecurityTrustResourceUrl`, which is the first use of the sanitizer in this
frontend. Re-validate the id against `^[A-Za-z0-9_-]{11}$` in the component immediately before
trusting it — the server already guarantees the shape, but `bypassSecurityTrust*` is the one call
where a future change upstream turns into an injection, and the check costs a line.

**Route ordering.** Register `admin/exercises/new` **before** `admin/exercises/:id`, or the literal
segment is swallowed by the parameter — the same trap `app.routes.ts` comments on twice.
`admin/exercises/:id/edit` has a different segment count and does not collide, but list it beside
its siblings for readability.

**Reactivation can collide.** Activating an exercise whose name is now taken by another active row
violates the filtered unique index. `ActivateAsync` must re-check the name and return
`409 name_taken`, exactly as `ClassTypeEndpoints.ActivateAsync` does — this was found in review
there, not in planning.

---

## Phase 1: Model and schema

### Overview

The `Exercise` entity, the YouTube id parser it depends on, the EF configuration and one reversible
migration. Nothing is reachable from the app yet.

### Changes Required:

#### 1. YouTube video id parser

**File**: `src/Domain/Training/YouTubeVideoId.cs`

**Intent**: Turn whatever the admin pasted into the canonical 11-character video id, or refuse it.
Pure static logic with no dependencies, so it belongs in `Domain` and is testable without a
database. It is the single place that knows what a YouTube link looks like.

**Contract**: `public static bool TryParse(string? input, out string videoId)` — returns `false` and
an empty `videoId` for anything it does not recognise. Accepted shapes, with or without scheme,
`www.`/`m.` host prefix, and with arbitrary extra query parameters: a bare id,
`youtube.com/watch?v=<id>`, `youtu.be/<id>`, `youtube.com/embed/<id>`, `youtube.com/shorts/<id>`,
`youtube.com/live/<id>`. A valid id matches `^[A-Za-z0-9_-]{11}$`.

**Adapted during implementation.** The accepted set is a superset of the shapes listed above: it also
takes `music.youtube.com`, `youtube-nocookie.com` and the legacy `youtube.com/v/<id>` form. Each has
its own case in `YouTubeVideoIdTests`. A shape the parser fails to recognise is a refusal the admin
must work around by hand, so erring wide costs nothing here — the extracted candidate still has to
satisfy the 11-character pattern before it is accepted. Also expose
`public static string ToWatchUrl(string videoId)` returning
`https://www.youtube.com/watch?v=<id>` — the form needs a canonical URL to display when editing.

#### 2. The entity

**File**: `src/Domain/Training/Exercise.cs`

**Intent**: The library row. A POCO in the house style — no factory, no guard clauses, invariants
enforced at the edges — carrying one required name and every descriptive field as optional, per
FR-018's "fields are optional per seed".

**Contract**: `Guid Id`; `string Name` (default `string.Empty`); nullable `string?` for
`Description`, `MuscleGroup`, `Difficulty`, `Equipment`, `Preparation`, `StartingPosition`,
`Execution`, `VideoId`; `bool IsActive = true`; `DateTimeOffset CreatedAt`. Absent means `null`,
never `""` — document that on the optional properties as `ClassType.Description` does.

#### 3. EF configuration

**File**: `src/Infrastructure/Persistence/Configurations/ExerciseConfiguration.cs`

**Intent**: Pin the table, the column lengths that back the server's validation bounds, and the
uniqueness rule that stops name drift among active exercises.

**Contract**: `ToTable("Exercises")`; `HasKey(Id)`; `Name` required, `HasMaxLength(200)`;
`Description` 1000; `MuscleGroup` 100; `Difficulty` 50; `Equipment` 200; `Preparation` 2000;
`StartingPosition` 2000; `Execution` 4000; `VideoId` 20 (the id is 11 — the headroom avoids a
migration if YouTube ever lengthens it); `CreatedAt` required; `IsActive` required with
`HasDefaultValue(true)`. Filtered unique index:
`HasIndex(x => x.Name).IsUnique().HasFilter("[IsActive] = 1").HasDatabaseName("IX_Exercises_Name_Active")`.
Optional columns get no `IsRequired` call — the nullable CLR type is what makes them nullable.

#### 4. DbContext registration

**File**: `src/Infrastructure/Persistence/AppDbContext.cs`

**Intent**: Expose the set. Configuration is auto-discovered, so nothing goes in `OnModelCreating`.

**Contract**: `public DbSet<Exercise> Exercises => Set<Exercise>();` beside the existing one-liners.

#### 5. Migration

**File**: `src/Infrastructure/Persistence/Migrations/<timestamp>_AddExercises.cs`

**Intent**: Create the table and its filtered index, reversibly. Purely additive — no other table is
touched, and no data is destroyed, so `Down` is a true inverse (unlike `AddClassTypes`).

**Contract**: `Up` creates `Exercises` and `IX_Exercises_Name_Active`; `Down` drops both. Generated
with `dotnet ef migrations add AddExercises -p src/po-prostu-silka.csproj -o Infrastructure/Persistence/Migrations`;
`AppDbContextModelSnapshot.cs` is regenerated and committed with it.

#### 6. Parser unit tests

**File**: `tests/po-prostu-silka.Tests/YouTubeVideoIdTests.cs`

**Intent**: Lock the parser's accepted and rejected shapes before anything depends on it. No
database, no fixture — the pattern `ClubTimeTests.cs` and `WebPushPayloadTests.cs` already use.

**Contract**: A `[Theory]` over accepted inputs asserting they all yield the same id, and a
`[Theory]` over rejected inputs (empty, whitespace, a non-YouTube URL, a Vimeo link, a watch URL
with a 10- or 12-character id, a playlist-only URL) asserting `false`. Include the awkward real
cases: `https://youtu.be/<id>?t=42`, `https://www.youtube.com/watch?list=PL123&v=<id>`, and an id
containing `-` and `_`.

### Success Criteria:

#### Automated Verification:

- Solution builds warning-free: `dotnet build` from `src/`
- Parser tests pass: `dotnet test` from the repo root
- Migration is reversible: `dotnet ef migrations script <previous> AddExercises` generates without error
- Migration applies to a clean local database: `dotnet ef database update -p src/po-prostu-silka.csproj --connection "<dev connection string>"`

#### Manual Verification:

- `IX_Exercises_Name_Active` exists in the local database with the `[IsActive] = 1` filter
- Two rows with the same name are rejected by the database while both are active, and accepted once one is deactivated (checked by hand in SQL)
- `docker compose up -d` then `GET /health` still returns healthy after the migration

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase.

---

## Phase 2: The admin API

### Overview

Six endpoints under the `Admin` policy, their DTOs and validation, the two persistence seams, and
the integration tests that pin authorization, validation bounds, uniqueness and the absence of a
delete route.

### Changes Required:

#### 1. Endpoints, DTOs and validation

**File**: `src/Application/Training/ExerciseEndpoints.cs`

**Intent**: The whole admin surface for the library, mirroring `ClassTypeEndpoints.cs` in structure,
naming and error conventions so the codebase keeps one pattern.

**Contract**: `public static IEndpointRouteBuilder MapExerciseEndpoints(this IEndpointRouteBuilder app)`.
Group: `app.MapGroup("/api/admin/exercises").WithTags("Training").RequireAuthorization(AuthorizationPolicyNames.Admin)`
— authorization at the group, never per route. Routes: `GET /`, `GET /{id:guid}`, `POST /`,
`PUT /{id:guid}`, `POST /{id:guid}/deactivate`, `POST /{id:guid}/activate`. No `DELETE`.

Records in the same file:
- `ExerciseSummary(Guid Id, string Name, string? Description, string? MuscleGroup, string? Difficulty, string? Equipment, string? Preparation, string? StartingPosition, string? Execution, string? VideoId, bool IsActive, DateTimeOffset CreatedAt)` — one shape for both list and detail; the library is small enough that a trimmed list DTO would buy nothing and cost a second contract to keep in step.
- `ExerciseRequest(string Name, string? Description, string? MuscleGroup, string? Difficulty, string? Equipment, string? Preparation, string? StartingPosition, string? Execution, string? VideoUrl)` — note `VideoUrl`, not `VideoId`: the client sends what the admin typed and the server owns the parsing. `IsActive` is deliberately absent.
- `ExerciseFailure(string Reason)`.

`private static IResult? Validate(ExerciseRequest)` returns `null` when valid, else
`Results.Json(new ExerciseFailure(reason), statusCode: 400)`. Reasons, each keyed to the length in
`ExerciseConfiguration`: `missing_field` (blank name), `name_too_long`, `description_too_long`,
`muscle_group_too_long`, `difficulty_too_long`, `equipment_too_long`, `preparation_too_long`,
`starting_position_too_long`, `execution_too_long`, `invalid_video_url`. `name_taken` is `409`.
Every optional string is trimmed and normalised to `null` when blank before it reaches the entity,
the way `NormalizeDescription` does. `VideoUrl` is normalised through
`YouTubeVideoId.TryParse`: blank → `null`, parsed → the id, unparseable → `invalid_video_url`.
`GET /` returns active and inactive rows in one call, ordered active-first then by name.

Seams declared at the bottom of the file, mirroring `IClassTypeQuery` / `IClassTypeStore`:
`IExerciseQuery` with `GetAllAsync` returning `IReadOnlyList<ExerciseSummary>`; `IExerciseStore`
with `FindAsync`, `Add`, `IsNameTakenAsync` — no `Remove`, and it does not save.

#### 2. Seam implementations

**Files**: `src/Infrastructure/Training/ExerciseQuery.cs`, `src/Infrastructure/Training/ExerciseStore.cs`

**Intent**: Keep EF Core out of `Application`. Read side projects straight into the DTO and does not
track; write side tracks, because the mutation handlers rely on change tracking and commit through
the shared `IUnitOfWork`.

**Contract**: Primary-constructor DI over `AppDbContext`. `ExerciseQuery.GetAllAsync` uses
`AsNoTracking()` + a LINQ `Select` into `ExerciseSummary`, ordered
`OrderByDescending(IsActive).ThenBy(Name)`. `ExerciseStore.IsNameTakenAsync` compares with `==`
(not `ToLower()`) to stay sargable against SQL Server's case-insensitive collation, and excludes a
given id so an edit that keeps its own name is not a conflict.

#### 3. Wiring

**File**: `src/Program.cs`

**Intent**: Register the seams and map the group.

**Contract**: `builder.Services.AddScoped<IExerciseQuery, ExerciseQuery>();` and
`AddScoped<IExerciseStore, ExerciseStore>();` beside the existing scoped registrations;
`app.MapExerciseEndpoints();` beside the other `Map*Endpoints()` calls.

#### 4. Integration tests

**File**: `tests/po-prostu-silka.Tests/ExerciseEndpointTests.cs`

**Intent**: Pin the rules this slice exists to enforce. The `class-type-definitions` review's one
CRITICAL finding was shipping the uniqueness invariant with no server test; that is the trap to
pre-empt here.

**Contract**: `[Collection(nameof(IntegrationCollection))]`, `IntegrationTestFixture` injected,
unique names per test via a local `UniqueName` helper, a private response record mirroring the JSON.
Cases:
- `TheoryData<string,string> EveryRoute` over all six routes driving `Anonymous_is_401_on_every_route` and `Active_non_admin_is_403_on_every_route`
- create then read back, asserting every field round-trips and blank optionals come back `null`
- `TheoryData<object,string> InvalidRequests()` covering `missing_field`, every `*_too_long` bound (one character over) and `invalid_video_url`
- a `[Theory]` asserting several accepted YouTube link shapes all persist the same `videoId` (the parser's contract as seen through the API)
- a second active exercise with the same name is `409 name_taken`; deactivating releases the name; activating into a taken name is `409`
- `PUT` keeping the row's own name is not a conflict
- unknown id is `404` on `GET /{id}`, `PUT`, `deactivate` and `activate`
- `There_is_no_delete_endpoint`

### Success Criteria:

#### Automated Verification:

- Solution builds warning-free: `dotnet build` from `src/`
- All tests pass, including the new endpoint suite: `dotnet test` from the repo root
- The new suite covers every route for both anonymous and non-admin callers (assert by the `EveryRoute` theory listing six routes)

#### Manual Verification:

- With the app running, an admin session can create, edit, deactivate and reactivate an exercise through the API (curl or an HTTP client)
- Pasting a `youtu.be/<id>?t=42` link and a `watch?list=…&v=<id>` link both yield the same stored `videoId`
- A non-YouTube URL returns `400 invalid_video_url`, not a 500

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase.

---

## Phase 3: The list and the form

### Overview

The Angular service and models, the list screen with thumbnails and the active/inactive toggle, the
shared create/edit form with suggestion inputs, the routes, and Vitest specs. After this phase the
feature is usable end to end; only the read-only detail screen is missing.

### Changes Required:

#### 1. Models

**File**: `src/app/src/app/core/training/exercise.models.ts`

**Intent**: Mirror the API records as a contract, with the house doc comment saying so.

**Contract**: `ExerciseSummary` with every field from the server record, optionals typed
`string | null`; `ExerciseRequest` with `videoUrl: string | null`; `ExerciseFailureReason` as a union
of the ten reason strings, and `ExerciseFailure { reason: ExerciseFailureReason }`.

#### 2. Service

**File**: `src/app/src/app/core/training/exercise.service.ts`

**Intent**: One method per operation, matching `class-type.service.ts` exactly.

**Contract**: `providedIn: 'root'`, `inject(HttpClient)`, `firstValueFrom` over relative
`/api/admin/exercises` URLs with `encodeURIComponent(id)` on path parameters: `getAll`, `getById`,
`create`, `update`, `deactivate`, `activate`.

#### 3. Thumbnail helper

**File**: `src/app/src/app/core/training/youtube.ts`

**Intent**: One place that composes YouTube URLs from an id, so the list, the form preview and the
detail screen cannot drift apart.

**Contract**: `isVideoId(value: string | null): boolean` testing `^[A-Za-z0-9_-]{11}$`;
`thumbnailUrl(videoId: string): string` returning
`https://img.youtube.com/vi/<id>/mqdefault.jpg`; `watchUrl(videoId: string): string`. The embed URL
belongs to the detail component, which must pair it with the sanitizer (Phase 4).

#### 4. List screen

**Files**: `src/app/src/app/features/admin/exercises/exercises.{ts,html,scss}`

**Intent**: Browse the library and change an exercise's active state without leaving the list.

**Contract**: Standalone component following `class-types.ts`: signals for `rows`, `loading`,
`loadFailed`, `showInactive`, `busy: ReadonlySet<string>`, `failedId`, `notice`; a `generation`
counter discarding stale loads; `computed()` for `visible` and `hiddenByFilter`. Template renders a
`<ul>` of `<li class="card ...">` with the four states (loading `role="status"`, failure
`role="alert"` + retry, empty, empty-by-filter), an inactive badge, a row `routerLink` into
`/admin/exercises/:id/edit` (repointed at the detail route in Phase 4), a "Dodaj ćwiczenie" link to
`/new`, and a per-row deactivate/activate
button. Each row shows a 16:9 thumbnail box: an `<img>` with `alt=""` (decorative — the name is
adjacent text), `loading="lazy"`, and an `(error)` handler that swaps in a neutral placeholder;
rows without a video render the placeholder directly. The fixed aspect box matters — an image that
loads late must not reflow the list.

#### 5. Create/edit form

**Files**: `src/app/src/app/features/admin/exercises/exercise-form.{ts,html,scss}`

**Intent**: One component for create and edit, keyed on an optional `:id`, with every server bound
mirrored client-side and every server refusal landing on the field that caused it.

**Contract**: `FormBuilder.nonNullable.group` with a required `name` plus eight optional controls;
`Validators.maxLength` constants matching `ExerciseConfiguration` one-for-one, each with the "keep
the two in step" comment. Long-prose fields (`preparation`, `startingPosition`, `execution`,
`description`) render as `<textarea>`. `muscleGroup` and `difficulty` are text inputs bound to
`<datalist>` elements whose options come from the distinct non-null values in a `getAll()` fetch made
on init; a failed fetch leaves them empty and never blocks the form. `videoUrl` shows
`watchUrl(videoId)` when editing an exercise that has one, and empty otherwise. `applyFailure()`
maps each of the ten reasons to its control — `invalid_video_url` to `videoUrl`, `name_taken` and
`name_too_long` to `name`, and so on — with only `missing_field` falling back to the form-level
banner. Empty strings convert to `null` on submit; `null` converts to `''` on load. Success
navigates to `/admin/exercises`.

#### 6. Routes

**File**: `src/app/src/app/app.routes.ts`

**Intent**: Register the four screens behind the admin guard.

**Contract**: Components loaded with `loadComponent`, in this order: `admin/exercises`,
`admin/exercises/new`, `admin/exercises/:id/edit`; each `canActivate: [authGuard, adminGuard]`.

**Adapted during implementation.** The plan specified *eagerly* imported components, reasoning that
no heavy dependency justified a lazy chunk. Measured, the three screens add ~28 kB to the initial
bundle, taking it from 475.01 kB to 502.88 kB — past the 500 kB `maximumWarning` budget in
`angular.json`, so `npm run build` began emitting a bundle-budget warning. Lazy loading keeps the
build clean at no cost an admin would notice, and the reasoning is recorded in `app.routes.ts` next
to the routes. `admin/exercises/:id` is **not** registered in this phase —
it lands with the detail component in Phase 4, so no route ever points at a component that does not
exist. Consequently the list's row link targets `/admin/exercises/:id/edit` in this phase and is
repointed at `/admin/exercises/:id` in Phase 4; that one-line change is part of Phase 4's work.

#### 7. Specs

**Files**: `src/app/src/app/features/admin/exercises/exercises.spec.ts`, `exercise-form.spec.ts`

**Intent**: Cover the behaviours that specs caught in prior slices: the four list states, the
toggle, and failure-to-control mapping.

**Contract**: `TestBed` with `provideHttpClient()`, `provideHttpClientTesting()`, `provideRouter([])`;
`vi.waitFor(() => controller.expectOne(url)).flush(...)`; `afterEach(() => controller.verify())`.
List: renders rows, shows the empty state, surfaces a failed load with a working retry, hides
inactive rows until toggled, renders a thumbnail only when `videoId` is present. Form: `name_taken`
lands on the name control and not the banner, `invalid_video_url` lands on the video control, blank
optionals are sent as `null`, and the datalist is populated from distinct values.

### Success Criteria:

#### Automated Verification:

- Client tests pass: `npm test` from `src/app/`
- Formatting and lint pass: `npm run quality:check` from `src/app/`
- Server tests still pass: `dotnet test` from the repo root

#### Manual Verification:

- An admin creates an exercise with only a name, then edits it to add every field, including a video
- The list shows the thumbnail for exercises with a video and the placeholder for those without, with no layout shift as images load
- Typing in the muscle-group field suggests values already used, and a brand-new value is still accepted
- Pasting a broken video link shows the message on the video field, not as a banner
- Deactivating hides the row until "pokaż nieaktywne" is on; reactivating restores it
- The screens are usable on a phone-width viewport

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase.

---

## Phase 4: The detail screen with an embedded player

### Overview

The read-only view of an exercise — every field laid out for reading, the video playing in place,
and an "Edytuj" button. The only component shape without a precedent in this frontend.

### Changes Required:

#### 1. Detail screen

**Files**: `src/app/src/app/features/admin/exercises/exercise-detail.{ts,html,scss}`

**Intent**: Give an exercise a readable page — the screen an admin looks at to check what the
library actually says, and the layout S-11 will later adapt for members.

**Contract**: Standalone component reading `:id` from the route, `exercise`/`loading`/`loadFailed`
signals, and a `getById` call on init. Renders: the name as `<h1>`, an inactive badge when
applicable, the muscle-group and difficulty values, equipment, and the four prose blocks
(description, preparation, starting position, execution) each rendered only when present — an
exercise with a name and nothing else must not show eight empty headings. A section that is entirely
absent is omitted, not shown as "—". States: loading (`role="status"`), load failure
(`role="alert"` with retry), and `404` from the API rendered as a distinct "nie znaleziono" state
with a link back to the list. Header carries an "Edytuj" link to `/admin/exercises/:id/edit` and a
"Wróć do listy" link, matching the `class-type-form` header pattern.

Video: when `videoId` is present and `isVideoId()` confirms its shape, render an iframe with
`[src]` bound to `DomSanitizer.bypassSecurityTrustResourceUrl('https://www.youtube-nocookie.com/embed/' + id)`,
computed once per id rather than in a template expression (a getter re-invoked on every change
detection cycle would re-trust on every tick). Attributes: `title` naming the exercise,
`loading="lazy"`, `allowfullscreen`, `referrerpolicy="strict-origin-when-cross-origin"`, inside the
same 16:9 box the list uses.

**Adapted during implementation.** The iframe also carries
`allow="accelerometer; clipboard-write; encrypted-media; gyroscope; picture-in-picture"` — the
permissions YouTube's own embed snippet sets, without which fullscreen and picture-in-picture behave
inconsistently across browsers. Additive only; it grants the frame nothing the page itself does not
already have. No video renders no iframe at all — not an empty frame.

#### 2. Route

**File**: `src/app/src/app/app.routes.ts`

**Intent**: Point `/admin/exercises/:id` at the detail component and send the list there.

**Contract**: `{ path: 'admin/exercises/:id', loadComponent: () => import(...).then((m) => m.ExerciseDetail), canActivate: [authGuard, adminGuard] }`,
listed after `new` and after `:id/edit`. **Adapted during implementation** — lazy rather than eager,
for the bundle-budget reason recorded under Phase 3's routes change. In the same change, repoint the list row's `routerLink`
from `/admin/exercises/:id/edit` to `/admin/exercises/:id` and update the list spec's assertion.

#### 3. Spec

**File**: `src/app/src/app/features/admin/exercises/exercise-detail.spec.ts`

**Intent**: Pin the conditional rendering and the video trust boundary.

**Contract**: Renders every field when all are present; omits the prose sections that are `null`;
renders no iframe when `videoId` is `null`; renders an iframe whose `src` is the
`youtube-nocookie.com/embed/<id>` URL when it is present; shows the not-found state on a `404`; the
"Edytuj" link points at `/admin/exercises/:id/edit`.

### Success Criteria:

#### Automated Verification:

- Client tests pass: `npm test` from `src/app/`
- Formatting and lint pass: `npm run quality:check` from `src/app/`
- Full suite still green: `dotnet test` from the repo root

#### Manual Verification:

- Opening an exercise from the list shows every populated field and no empty headings
- The video plays inline on the detail screen, including fullscreen
- An exercise without a video shows no player and no empty frame
- Visiting `/admin/exercises/<random guid>` shows the not-found state, not a spinner or a crash
- "Edytuj" opens the form pre-filled, and saving returns to the list
- The detail screen is readable on a phone-width viewport, with the player scaling to the column
- A member (non-admin) visiting the URL is redirected by `adminGuard`, and the API refuses them independently

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful.

---

## Testing Strategy

### Unit Tests:

- `YouTubeVideoIdTests` — every accepted link shape resolves to the same id; ids with `-`/`_`
  survive; near-misses (10 and 12 characters), non-YouTube hosts, playlist-only URLs and blanks are
  refused.
- Angular specs — the four list states, the inactive toggle, thumbnail presence/absence, failure
  reasons landing on their controls, `null`↔`''` conversion, conditional rendering and the iframe
  boundary on the detail screen.

### Integration Tests:

- `ExerciseEndpointTests` — authorization on every route for anonymous and non-admin callers; the
  create/read-back round trip; every validation bound one character over; link-shape acceptance
  through the API; uniqueness among active rows, name release on deactivate, reactivation collision;
  `404` on unknown ids across all four id-bearing routes; and the absence of a delete endpoint.

### Manual Testing Steps:

1. `docker compose up -d`, apply migrations, `GET /health` returns healthy.
2. Sign in as the seeded admin, open `/admin/exercises` — empty state.
3. Create an exercise with a name only; it appears with the placeholder thumbnail.
4. Edit it, filling every field and pasting a `youtu.be/<id>?t=42` link; the list now shows a
   thumbnail.
5. Open the exercise; every field reads correctly, the player works, fullscreen works.
6. Create a second exercise reusing the first's name — refused on the name field.
7. Deactivate the first; the name becomes reusable; create it again; try to reactivate the original
   — refused with the name message on the field.
8. Paste `https://vimeo.com/12345` — refused on the video field.
9. Repeat steps 2-5 at a 375px viewport width.
10. Sign in as an active non-admin member and visit `/admin/exercises` and
    `/admin/exercises/<id>` — both bounce.

## Performance Considerations

`GET /api/admin/exercises` returns the whole library, unfiltered, including the long prose fields —
the same shape `ClassType` uses, and the form fetches it a second time to build its suggestions. At
the expected size (dozens to low hundreds of exercises, one admin) this is irrelevant, and it buys a
simpler contract and no extra endpoint. The threshold worth revisiting is the point where the list
screen feels slow on a phone, or where a member-facing surface starts reading exercises (S-11) — at
which point the split is a trimmed list DTO plus a dedicated suggestions endpoint, not pagination.

Thumbnails are lazily loaded and served by YouTube's CDN; the fixed aspect box is what keeps a slow
image from reflowing the list. The iframe is created only on the detail screen and only when a video
exists, so no third-party script loads anywhere else in the app.

## Migration Notes

`AddExercises` is purely additive — a new table and its index, no column added to an existing table,
no data touched — so `Down` is a genuine inverse and rollback is safe. This is the first migration in
the training context; nothing in scheduling, membership or notifications is affected. Deploy order is
the standard one: migrations run on deploy, and the API tolerates the table being empty.

## References

- Research: `context/changes/exercise-library/research.md`
- Change notes and the no-nav-entry decision: `context/changes/exercise-library/change.md`
- Closest precedent, end to end: `context/archive/2026-09-02-class-type-definitions/plan.md`
- Its review, whose F1 and F2 this plan pre-empts: `context/archive/2026-09-02-class-type-definitions/reviews/impl-review.md`
- Entity and configuration to mirror: `src/Domain/Scheduling/ClassType.cs:28-66`, `src/Infrastructure/Persistence/Configurations/ClassTypeConfiguration.cs:7-41`
- Endpoint group, validation and seams to mirror: `src/Application/Scheduling/ClassTypeEndpoints.cs:113-131,318-351,375-404`
- Screens to mirror: `src/app/src/app/features/admin/class-types/`
- Test fixture and idioms: `tests/po-prostu-silka.Tests/IntegrationTestFixture.cs:25-158`, `ClassTypeEndpointTests.cs:17-40`
- Roadmap item S-10: `context/foundation/roadmap.md:244-255`
- Requirements: `context/foundation/prd.md:110-115,163`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Model and schema

#### Automated

- [x] 1.1 Solution builds warning-free (`dotnet build` from `src/`) — af62c5f
- [x] 1.2 Parser tests pass (`dotnet test` from the repo root) — af62c5f
- [x] 1.3 Migration is reversible (`dotnet ef migrations script <previous> AddExercises`) — af62c5f
- [x] 1.4 Migration applies to a clean local database — af62c5f

#### Manual

- [x] 1.5 `IX_Exercises_Name_Active` exists with the `[IsActive] = 1` filter
- [x] 1.6 Duplicate active names rejected by the database; accepted once one row is deactivated
- [x] 1.7 `GET /health` healthy after the migration

### Phase 2: The admin API

#### Automated

- [x] 2.1 Solution builds warning-free — ccade3a
- [x] 2.2 All tests pass, including `ExerciseEndpointTests` — ccade3a
- [x] 2.3 `EveryRoute` theory covers all six routes for anonymous and non-admin callers — ccade3a

#### Manual

- [x] 2.4 Create, edit, deactivate and reactivate work through the API as an admin
- [x] 2.5 `youtu.be/<id>?t=42` and `watch?list=…&v=<id>` persist the same `videoId`
- [x] 2.6 A non-YouTube URL returns `400 invalid_video_url`, not a 500

### Phase 3: The list and the form

#### Automated

- [x] 3.1 Client tests pass (`npm test` from `src/app/`) — a36378e
- [x] 3.2 Formatting and lint pass (`npm run quality:check`) — a36378e
- [x] 3.3 Server tests still pass (`dotnet test`) — a36378e

#### Manual

- [x] 3.4 Create with a name only, then edit to add every field including a video
- [x] 3.5 Thumbnails and placeholders render with no layout shift
- [x] 3.6 Muscle-group suggestions appear and a new value is still accepted
- [x] 3.7 A broken video link shows on the video field, not as a banner
- [x] 3.8 Deactivate hides the row until the toggle is on; reactivate restores it
- [x] 3.9 Usable at phone width

### Phase 4: The detail screen with an embedded player

#### Automated

- [x] 4.1 Client tests pass (`npm test`) — 0b15c0f
- [x] 4.2 Formatting and lint pass (`npm run quality:check`) — 0b15c0f
- [x] 4.3 Full server suite still green (`dotnet test`) — 0b15c0f

#### Manual

- [x] 4.4 Every populated field shows; no empty headings
- [x] 4.5 The video plays inline, including fullscreen
- [x] 4.6 No player and no empty frame when there is no video
- [x] 4.7 An unknown id shows the not-found state
- [x] 4.8 "Edytuj" opens the pre-filled form; saving returns to the list
- [x] 4.9 Readable at phone width, player scales to the column
- [x] 4.10 A non-admin is bounced by the guard and refused by the API independently
