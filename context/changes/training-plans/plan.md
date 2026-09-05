# Training Plans Implementation Plan

## Overview

A trainer or an admin builds a named, ordered list of exercises — sets, reps, weight, rest, note — and
assigns it to a member. A member has at most one active plan; assigning a new one archives the old.
The member reads their active plan at `/my-plan` and opens any exercise's details from inside it.

This is roadmap slice **S-11** (`context/foundation/roadmap.md:257-268`), covering `prd.md` FR-015,
FR-016, FR-017 and FR-020. It is also the first change that gives the `Trainer` role a capability of
its own, which answers `prd-v2.md` Open Question 3 and retires that document's "No trainer screen"
Non-Goal.

## Current State Analysis

The `Training` bounded context exists across all three layers, landed whole by `exercise-library`
(commit `e1745c9`), and that slice deliberately left the seams this one consumes.

- **`Exercise` is ready to be referenced.** `src/Domain/Training/Exercise.cs:27-80` is an anemic POCO
  with `IsActive` soft-deletion and no delete endpoint. `exercise-library/research.md:367-371` records
  that hard delete was rejected *specifically* because "a training plan will reference exercises, so a
  hard delete would either orphan plan rows or be blocked by a `Restrict` FK."
- **The read-only exercise-detail screen was built for this slice.**
  `src/app/src/app/features/admin/exercises/exercise-detail.ts:9-19` says so in its own docblock;
  `exercise-library/plan.md:521` calls it "the layout S-11 will later adapt for members."
- **The `Trainer` role confers nothing.** `src/Domain/ApplicationRoles.cs:33` defines it, the admin
  grants and revokes it, and `S-06` reads it to populate the instructor picker — but
  `src/Infrastructure/Authorization/AuthorizationPolicies.cs:35-47` defines exactly two policies
  (`ActiveMember`, `Admin`), there is no `isTrainer()` in `auth.service.ts`, no trainer guard, and no
  route in `app.routes.ts` referencing trainer status.
- **`Trainer` alone does not satisfy `ActiveMember`** — `ApplicationRoles.cs:50` sets
  `MemberFacing = [User, Admin]` deliberately. A real trainer is an approved member who was granted the
  role, so they hold `User` too.
- **The hard-invariant machinery already exists.** `class-booking-and-cancel` built
  `IUnitOfWork.TrySaveAsync` → `SaveOutcome {Saved, ConcurrencyConflict, UniqueViolation}` plus
  `DiscardChanges()` (`src/Application/Persistence/IUnitOfWork.cs:11-34,70-77`), a rotated string
  concurrency token (`src/Domain/Scheduling/Class.cs:64-99`), a bounded retry loop
  (`src/Application/Scheduling/BookingEndpoints.cs:126,192-268`), and a filtered unique index as
  defence in depth (`BookingConfiguration.cs:56-59`).
- **Three things have no precedent in this repo**: an ordered child collection (no owned types, no
  order column anywhere), a `decimal` column (`grep -rn "decimal" src` finds nothing outside
  `Migrations/`), and list-reordering UI (`@angular/cdk` is not in `src/app/package.json:17-33`).
- **The bundle budget is tight.** `src/app/angular.json:41-53` warns at 500 kB; the initial bundle is
  475.01 kB. `exercise-library` breached it at 502.88 kB and converted three screens to
  `loadComponent`.
- **Member navigation has three entries** (`src/app/src/app/app.html`): Grafik and Moje zajęcia gated
  on `isActive()`, Zgłoszenia on `isAdmin() && isActive()`.

Full evidence: `context/changes/training-plans/research.md`.

## Desired End State

- A signed-in trainer (or admin) opens `/trainer/plans`, sees every active plan in the club, and
  creates a new one: pick an active member, name the plan, add exercises from the library, set sets /
  reps / weight / rest / note per exercise, drag rows into order, save.
- Saving a plan for a member who already has one archives the old plan in the same transaction. Two
  simultaneous assignments to the same member leave exactly one active plan — proven by a race test.
- A signed-in member opens `/my-plan` from the top navigation and sees their active plan: its name,
  who assigned it, and the ordered exercise list with its parameters. Tapping a row opens that
  exercise's instructions and video.
- A member with no plan sees a plain "no plan yet" card, not an error.
- A blocked member's plan is untouched; they simply cannot reach the endpoint, and the plan is waiting
  when they are unblocked.

### Key Discoveries:

- `src/Application/Training/ExerciseEndpoints.cs:130-132` — the policy is applied at the `MapGroup`,
  never per-endpoint, and a `TheoryData` test enumerates every route against anonymous (401) and
  wrong-role (403) callers (`ExerciseEndpointTests.cs:94-140`).
- `src/Application/Scheduling/BookingEndpoints.cs:186,299,359` — the owner-scoped read pattern: bind
  `ClaimsPrincipal`, resolve `userManager.GetUserId(principal)`, filter by it, never trust a route id.
- `src/Application/Persistence/IUnitOfWork.cs:44-49` — no explicit DB transaction anywhere; a single
  `SaveChangesAsync` is atomic, and `BeginTransaction` would require
  `Database.CreateExecutionStrategy().ExecuteAsync(...)` because `EnableRetryOnFailure` is on
  (`Program.cs:43-46`).
- `src/Infrastructure/Persistence/Configurations/BookingConfiguration.cs:56-59` — the filtered unique
  index shape, with the filter written against the enum's **numeric** literal.
- `src/app/src/app/core/auth/roles.ts:13-22` — the canonical home for role literals, already carrying
  `trainer`. Do not add a fourth copy.
- `src/app/src/app/core/auth/admin.guard.ts:14-36` — the guard template: SSR no-op, await
  `loadCurrentUser()`, check role + active, else redirect to `/` (not `/login`).
- `prd.md:163` — Non-Goal "No standalone exercise library browsing — exercises are reached from the
  training plan only." This forces the member's exercise read to be scoped to their own plan rather
  than exposing the library.

## What We're NOT Doing

- **No day / session split.** A plan is one flat ordered list, as `prd.md` FR-015 specifies. The data
  model keeps the door open (see Implementation Approach) but this change ships flat.
- **No plan history UI.** `prd.md:164` locks it. Archived plans stay in the database and no screen
  reads them.
- **No trainer↔member assignment relationship.** A trainer may assign to any active member. Nothing
  records "my clients."
- **No notification on assignment.** `prd.md` FR-021 lists exactly two triggers — account approved and
  class cancelled/changed. Assigning a plan sends nothing.
- **No plan deletion.** Consistent with every other aggregate in the repo: archive, never delete.
- **No member-facing exercise browsing.** The member reads exercises only through their own plan.
- **No keyboard path for reordering.** Explicitly decided (see Open Risks in `plan-brief.md`).
- **No dashboard cards.** The member's plan card on Home is S-12.
- **No progress tracking, logging of completed sets, or weight progression.** Out of MVP entirely.

## Implementation Approach

**Two entities, not an owned collection.** `TrainingPlan` is the aggregate root; `TrainingPlanItem` is
a separately mapped entity with an explicit `int Position`. Every relationship in this repo is shaped
that way and there is no owned-type experience here — but the deciding reason is forward-looking: the
user wants the option of a day/session split later, and re-parenting rows from a plan to a day is a
routine migration when items are their own table, while an owned collection would have to be rewritten.

**Item writes replace the whole list.** `POST` and `PUT` accept the full ordered item array; the
handler removes the plan's existing items and inserts the new ones with `Position` assigned from array
order. This is what makes reordering trivially correct — there is no per-row position update, so no
transient duplicate positions and no need for a unique index on `(TrainingPlanId, Position)` that a
multi-statement reorder would trip over mid-batch.

**One active plan, enforced twice.** The primary mechanism is a filtered unique index on the member's
id where the status is active, which makes two active plans unrepresentable. The handler archives the
member's current active plan and inserts the new one inside a bounded retry loop over
`IUnitOfWork.TrySaveAsync`, rotating the archived plan's `ConcurrencyStamp`; the rotation makes a
racer lose earlier and more cheaply, and is the only guard on the edit path, which inserts nothing. A
racer that loses either way retries, now sees the winner's plan as the active one, archives it, and
inserts — the loop converges rather than failing.

**Adapted during implementation.** The plan originally called the retry loop over `ConcurrencyStamp`
the primary mechanism and the index the backstop, by analogy with the booking slice. Manual
verification 2.5 measured the opposite. With the rotation commented out of the assignment handler the
six-racer test still passes; with the index weakened to non-unique (migration only — weakening the
configuration too trips `PendingModelChangesWarning`) it fails with all six racers holding an active
plan. Assignment always INSERTs a new active row, so the index sees every collision, whereas in the
booking slice the capacity check had no index to express it and the stamp had to carry the whole
invariant. The ordering is reversed above and in the doc comments on `TrainingPlan.ConcurrencyStamp`,
`TrainingPlanConfiguration`'s index, and `TrainingPlanEndpoints.CreateAsync`; verification criterion
2.5 is restated to match what actually fails red.

**Two API surfaces with different policies.** Authoring lives under `/api/trainer/plans` behind a new
`TrainerOrAdmin` policy. The member's read lives under `/api/plans` behind `ActiveMember`, scoped to
the caller's own id. The member's exercise read is deliberately *not* the library endpoint — it serves
an exercise only when that exercise appears in the caller's active plan, which is how `prd.md:163`'s
Non-Goal is enforced rather than merely respected.

**The exercise list becomes readable by trainers by splitting the group, not by loosening a route.**
`/api/admin/exercises` becomes two `MapGroup` calls on the same path: the read group (`GET /`,
`GET /{id}`) behind `TrainerOrAdmin`, the write group (`POST`, `PUT`, deactivate, activate) behind
`Admin`. Each group keeps exactly one policy, so the convention holds and the `EveryRoute` test still
works.

## Critical Implementation Details

**Two FKs to the same principal table.** `TrainingPlan` references `AspNetUsers` twice —
`MemberUserId` and `AssignedByUserId`. Both must be `DeleteBehavior.Restrict`; leaving either on the
default cascade makes SQL Server refuse the migration with a multiple-cascade-path error.

**`IsConcurrencyToken()` does not self-rotate.** Unlike `IsRowVersion()`, EF will not regenerate the
value on save — every writer that changes a plan's status must assign a fresh `Guid.NewGuid().ToString()`
explicitly. This was the single CRITICAL finding of `class-booking-and-cancel`
(`context/archive/2026-09-03-class-booking-and-cancel/plan.md:120-164`), caught only by a race test.

**The retry loop must re-read.** On `ConcurrencyConflict` or `UniqueViolation`, call
`unitOfWork.DiscardChanges()` and re-read the member's active plan from the top of the loop. Reusing
the stale tracked entity is how a retry loop turns into an infinite one.

**`decimal` precision is set here for the first time.** `WeightKg` is `decimal(5,2)` — up to 999.99 kg
in 0.01 steps. Configure it with `HasPrecision(5, 2)`; without it EF defaults to `decimal(18,2)` on SQL
Server, which is not wrong but silently establishes a different convention for every decimal that
follows. Carry a doc comment explaining the choice, matching the bar set by `Class.cs:92-97`.

**Bounds live in three places.** Every length and range below must be identical in the EF configuration,
the endpoint validation constants, and the Angular `Validators`. Missing one is the most repeated cause
of a deterministic 500 in this repo's review history.

## Phase 1: Domain model, schema and migration

### Overview

Add the two entities, their EF configurations and one additive migration. Nothing reads or writes them
yet — this phase is provably correct on its own via the migration round trip and a build.

### Changes Required:

#### 1. Plan status enum

**File**: `src/Domain/Training/TrainingPlanStatus.cs`

**Intent**: Name the two states a plan can be in, so the filtered index and every query read the same
vocabulary.

**Contract**: `public enum TrainingPlanStatus { Active = 0, Archived = 1 }`. Values are pinned
explicitly — the index filter is written against the numeric literal, so a renumbering would silently
break it.

#### 2. The plan aggregate

**File**: `src/Domain/Training/TrainingPlan.cs`

**Intent**: The aggregate root: a named plan belonging to one member, authored by one trainer or admin,
holding an ordered list of items.

**Contract**: POCO with public setters, matching `Exercise.cs`. Properties: `Guid Id`,
`string Name` (default `string.Empty`), `string MemberUserId`, `string AssignedByUserId`,
`TrainingPlanStatus Status` (default `Active`), `DateTimeOffset CreatedAt`,
`DateTimeOffset? ArchivedAt`, `string ConcurrencyStamp` (default `Guid.NewGuid().ToString()`), and
`List<TrainingPlanItem> Items` (default empty).

The `Items` collection navigation is deliberate and is *not* the thing `class-booking-and-cancel`
avoided: that plan refused a `Class.Bookings` navigation because a write path could count through it to
check capacity. Here the items *are* the plan's content, read and replaced wholesale, and no invariant
is derived from counting them. Carry a doc comment saying so, and one on `ConcurrencyStamp` restating
the "every writer rotates it explicitly" contract from `Class.cs:64-99`.

#### 3. The plan item

**File**: `src/Domain/Training/TrainingPlanItem.cs`

**Intent**: One exercise inside a plan, at a known position, with its prescription.

**Contract**: POCO. `Guid Id`, `Guid TrainingPlanId`, `Guid ExerciseId`, `int Position` (0-based,
dense, assigned from array order on write), `int? Sets`, `string? Reps`, `decimal? WeightKg`,
`int? RestSeconds`, `string? Note`. Every prescription field is optional, matching the library's own
"everything optional" posture (`Exercise.cs:36-62`) — a trainer may prescribe only a note.

`Reps` is a string rather than an int on purpose: "8-12" and "do upadku" are how prescriptions are
actually written, and an int would push them into the note where nothing validates them. Carry a doc
comment saying so, since it is the one field that breaks the numeric symmetry.

#### 4. Plan configuration

**File**: `src/Infrastructure/Persistence/Configurations/TrainingPlanConfiguration.cs`

**Intent**: Map the aggregate, and make "two active plans for one member" unrepresentable.

**Contract**: `IEntityTypeConfiguration<TrainingPlan>`, auto-discovered. Table `TrainingPlans`, key
`Id`. `Name` required, `HasMaxLength(120)`. `MemberUserId` and `AssignedByUserId` required,
`HasMaxLength(450)` (matching `AspNetUsers.Id`, as `BookingConfiguration.cs:29`). `Status`
`HasConversion<int>()` with `HasDefaultValue(TrainingPlanStatus.Active)`. `CreatedAt` required;
`ArchivedAt` nullable. `ConcurrencyStamp` required, `HasMaxLength(36)`, `.IsConcurrencyToken()`.

Two `HasOne<ApplicationUser>()` relationships — one per user column — both
`.OnDelete(DeleteBehavior.Restrict)`.

The defence-in-depth index:

```csharp
builder
    .HasIndex(x => x.MemberUserId)
    .IsUnique()
    .HasFilter("[Status] = 0")
    .HasDatabaseName("IX_TrainingPlans_Member_Active");
```

#### 5. Plan item configuration

**File**: `src/Infrastructure/Persistence/Configurations/TrainingPlanItemConfiguration.cs`

**Intent**: Map the items as dependent rows of the plan, referencing exercises without allowing an
exercise to be deleted out from under a plan.

**Contract**: Table `TrainingPlanItems`, key `Id`. `HasOne` the plan with
`.OnDelete(DeleteBehavior.Cascade)` — items are dependent content with no life of their own, the same
reasoning as `PushSubscriptionConfiguration.cs:29-32`. `HasOne<Exercise>()` on `ExerciseId` with
`.OnDelete(DeleteBehavior.Restrict)` — the FK the exercise library's deactivate-not-delete decision
was made for.

`Position` required. `Sets` and `RestSeconds` nullable ints. `Reps` `HasMaxLength(50)`. `Note`
`HasMaxLength(500)`. `WeightKg` `HasPrecision(5, 2)`.

A **non-unique** index on `(TrainingPlanId, Position)`, named `IX_TrainingPlanItems_Plan_Position`.
Non-unique deliberately: positions are unique by construction because writes replace the whole item
list, and a unique index would be tripped by any future per-row reorder that updates rows one statement
at a time.

#### 6. DbSet registration

**File**: `src/Infrastructure/Persistence/AppDbContext.cs`

**Intent**: Expose the two new sets.

**Contract**: Add `DbSet<TrainingPlan> TrainingPlans` and `DbSet<TrainingPlanItem> TrainingPlanItems`
alongside the existing sets at `:32`. `OnModelCreating` is untouched — the configurations are
auto-discovered.

#### 7. Migration

**File**: `src/Infrastructure/Persistence/Migrations/<timestamp>_AddTrainingPlans.cs`

**Intent**: Create both tables. Purely additive — no existing table is altered, no data is touched.

**Contract**: Generated with
`dotnet ef migrations add AddTrainingPlans -p src/po-prostu-silka.csproj -o Infrastructure/Persistence/Migrations`.
`Up` creates `TrainingPlans` (with the filtered unique index and both user FKs) then
`TrainingPlanItems` (with its two FKs and the position index). `Down` mirrors it in reverse — drop
`TrainingPlanItems`, then `TrainingPlans`. Verify by running the down-then-up round trip once against
the local Docker SQL Server before committing, as the booking plan's Migration Notes require.

### Success Criteria:

#### Automated Verification:

- Backend builds warning-free: `dotnet build` from `src/`
- Migration script generates cleanly: `dotnet ef migrations script AddExercises AddTrainingPlans -p src/po-prostu-silka.csproj`
- Migration applies against the local database: `dotnet ef database update -p src/po-prostu-silka.csproj --connection "<dev connection string>"`
- Existing test suite still green: `dotnet test`

#### Manual Verification:

- Down-then-up round trip runs without error against the local Docker SQL Server, and the two tables are gone after the down and present after the up
- `IX_TrainingPlans_Member_Active` exists and is filtered — inserting two active plans for one member by hand fails, inserting an active plus an archived one succeeds
- `GET /health` still returns healthy after the migration

**Implementation Note**: After completing this phase and all automated verification passes, pause here
for manual confirmation from the human that the manual testing was successful before proceeding to the
next phase.

---

## Phase 2: Authorization, the plans API, and the member read surface

### Overview

Give the `Trainer` role its first capability, open the exercise list to trainers by splitting the
existing group, and build both API surfaces with the one-active-plan invariant and its race test.

### Changes Required:

#### 1. The policy name

**File**: `src/Domain/AuthorizationPolicyNames.cs`

**Intent**: Name the new policy in Domain, beside `Admin`, so Application can reference it without
touching Infrastructure.

**Contract**: `public const string TrainerOrAdmin = "TrainerOrAdmin";`

#### 2. The policy itself

**File**: `src/Infrastructure/Authorization/AuthorizationPolicies.cs`

**Intent**: Express "an active account holding Trainer or Admin". This is the first policy in the app
that admits a role union.

**Contract**: Inside `AddApplicationPolicies()`, alongside the two existing policies:
`.RequireAuthenticatedUser()`, `.RequireClaim(StatusClaimType, nameof(AccountStatus.Active))`,
`.RequireRole(ApplicationRoles.Trainer, ApplicationRoles.Admin)`. Multi-argument `RequireRole` is OR in
ASP.NET Core — the same semantics `MemberFacing` already relies on.

Note in a comment that a granted `Trainer` role does not reach an existing session's cookie until
`POST /api/auth/refresh` or the 2-minute security-stamp validation interval fires
(`Program.cs:133-134`).

#### 3. Split the exercise endpoint group

**File**: `src/Application/Training/ExerciseEndpoints.cs`

**Intent**: Let a trainer read the library for the plan builder, without letting them write it, and
without putting two different policies inside one group.

**Contract**: `MapExerciseEndpoints` declares two groups on the same path `/api/admin/exercises`, both
tagged `"Training"`. The read group carries `RequireAuthorization(AuthorizationPolicyNames.TrainerOrAdmin)`
and holds `GET /` and `GET /{id:guid}`. The write group carries the existing
`RequireAuthorization(AuthorizationPolicyNames.Admin)` and holds `POST /`, `PUT /{id:guid}`,
`POST /{id:guid}/deactivate`, `POST /{id:guid}/activate`. No handler changes.

Rewrite the group's existing comment so it states the split and why — a comment describing a guard that
no longer exists as written was a review finding on `trainer-role-and-assignment` (F3).

#### 4. Plan DTOs, endpoints and seams

**File**: `src/Application/Training/TrainingPlanEndpoints.cs`

**Intent**: The authoring surface. One file holds DTOs, route mapping, validation, failure records and
the persistence interfaces, matching `ExerciseEndpoints.cs`.

**Contract**:

Write group: `app.MapGroup("/api/trainer/plans").WithTags("Training").RequireAuthorization(AuthorizationPolicyNames.TrainerOrAdmin)`
with routes `GET /`, `GET /members`, `GET /{id:guid}`, `POST /`, `PUT /{id:guid}`. No `DELETE`.
`GET /members` must be registered **before** `GET /{id:guid}` — a literal segment behind a `{id:guid}`
route is only saved by the route constraint, and relying on that is the same trap `app.routes.ts`
warns about three times on the client side.

`GET /members` returns the members a plan may be assigned to: `AssignableMember(string Id, string DisplayName)`,
active accounts only, ordered by display name. It exists because the full member list
(`/api/admin/members`) is `Admin`-only and a trainer needs nothing from it but a name and an id.

DTOs:
- `TrainingPlanSummary(Guid Id, string Name, string MemberUserId, string MemberDisplayName, string AssignedByDisplayName, DateTimeOffset CreatedAt, int ItemCount)` — the list row.
- `TrainingPlanItemView(Guid Id, Guid ExerciseId, string ExerciseName, int Position, int? Sets, string? Reps, decimal? WeightKg, int? RestSeconds, string? Note)`.
- `TrainingPlanDetail(Guid Id, string Name, string MemberUserId, string MemberDisplayName, string AssignedByDisplayName, DateTimeOffset CreatedAt, IReadOnlyList<TrainingPlanItemView> Items)` — one shape serves the trainer's edit load and the member's read.
- `TrainingPlanItemRequest(Guid ExerciseId, int? Sets, string? Reps, decimal? WeightKg, int? RestSeconds, string? Note)` — no position field; array order *is* the order.
- `TrainingPlanRequest(string Name, string MemberUserId, IReadOnlyList<TrainingPlanItemRequest> Items)` — one shape serves create and edit; on `PUT` the member may not change, and a mismatch is refused.
- `TrainingPlanFailure(string Reason)`.

Validation constants, mirrored from the EF configuration: `MaxNameLength = 120`, `MaxRepsLength = 50`,
`MaxNoteLength = 500`, `MaxItems = 50`, `MinSets = 1`, `MaxSets = 20`, `MinRestSeconds = 0`,
`MaxRestSeconds = 3600`, `MinWeightKg = 0`, `MaxWeightKg = 999.99`.

Failure reasons — 400 unless noted: `missing_field`, `too_long`, `no_items`, `too_many_items`,
`invalid_sets`, `invalid_reps`, `invalid_weight`, `invalid_rest`, `unknown_exercise`,
`inactive_exercise`, `duplicate_exercise`; 404 `not_found`; 409 `member_not_found`,
`member_not_active`, `member_changed`, `conflict`.

**Adapted during implementation.** The shipped set differs from the line above in two ways, and the
SPA's `TrainingPlanFailure` union mirrors the shipped one. `too_long` is split into
`name_too_long`, `reps_too_long` and `note_too_long`, because a single reason gives the builder
nothing to attach to a control — the whole reason the union is closed. `invalid_reps` does not exist:
reps is free text with only a length bound, so an over-long value is `reps_too_long` and there is no
other way for it to be invalid. And `not_found` is not a body reason at all — the three 404s return a
bare `Results.NotFound()`, matching every other 404 in this codebase, because a reason string adds
nothing to a status that already says the whole story.

Seams at the bottom of the file: `ITrainingPlanQuery` (list, detail by id, active plan for a member id)
and `ITrainingPlanStore` (find tracked by id, find the member's tracked active plan, add, clear an
existing plan's items, and a check that a set of exercise ids all exist and are active).

#### 5. The assignment handler and its invariant

**File**: `src/Application/Training/TrainingPlanEndpoints.cs` (the `CreateAsync` handler)

**Intent**: Assign a new plan and archive the member's existing one atomically, surviving concurrent
assignment.

**Contract**: `private const int MaxAttempts = 10;` — the same bound `BookingEndpoints.cs:126` settled
on, and for the same reason: with N racers the losers' attempt budget is consumed by the winners.

The loop, per attempt: re-read the member's tracked active plan → if one exists, set
`Status = Archived`, `ArchivedAt = timeProvider.GetUtcNow()`, and **assign a fresh
`ConcurrencyStamp`** → build the new plan and its items with `Position` from array order → add →
`unitOfWork.TrySaveAsync(...)`. On `Saved`, return the detail. On `ConcurrencyConflict` or
`UniqueViolation`, call `unitOfWork.DiscardChanges()` and continue. After `MaxAttempts`, return
`409 conflict`.

The member is validated once before the loop: the target must exist and be `AccountStatus.Active`
(`409 member_not_found` / `member_not_active`). `AssignedByUserId` comes from
`userManager.GetUserId(principal)`, never from the request body.

Editing (`PUT`) does **not** need the loop — it changes one plan's own rows and no cross-row invariant.
It does still rotate the plan's `ConcurrencyStamp` so two trainers editing the same plan cannot
silently overwrite each other, and uses `TrySaveAsync`, mapping `ConcurrencyConflict` to `409 conflict`.

#### 6. The member's read surface

**File**: `src/Application/Training/MyPlanEndpoints.cs`

**Intent**: Let a member read their own active plan, and read an exercise's details **only** when that
exercise sits in that plan — which is how `prd.md:163`'s "no standalone library browsing" Non-Goal is
enforced rather than merely respected.

**Contract**: `app.MapGroup("/api/plans").WithTags("Training").RequireAuthorization(AuthorizationPolicyNames.ActiveMember)`.

- `GET /mine` → `TrainingPlanDetail` for the caller's active plan, or `204 No Content` when they have
  none. A member without a plan is a normal state, not a 404 — the SPA renders an empty card, and
  distinguishing "no plan" from "request failed" is the exact test `exercise-library` wrote for its own
  empty state.
- `GET /mine/exercises/{id:guid}` → the full `ExerciseSummary` for that exercise, but only if it
  appears in the caller's active plan; otherwise `404 not_found`. The check is a join against the
  caller's plan items, not a role check.

The caller's id comes from `userManager.GetUserId(principal)` on every route
(`BookingEndpoints.cs:359` pattern). No route or body ever supplies a member id here.

The read intentionally does **not** filter out exercises that were deactivated after assignment — a
member's plan does not rearrange itself because of library housekeeping, and the `Restrict` FK keeps
the row alive. Carry a comment saying so, since the opposite reading is the natural one.

#### 7. Infrastructure implementations

**Files**: `src/Infrastructure/Training/TrainingPlanQuery.cs`,
`src/Infrastructure/Training/TrainingPlanStore.cs`

**Intent**: The read/write split every aggregate in this repo uses.

**Contract**: `TrainingPlanQuery(AppDbContext db)` — `AsNoTracking()`, projecting straight into the
DTOs in the LINQ query (`ExerciseQuery.cs:19-36` pattern), joining `AspNetUsers` for the member and
author display names and `Exercises` for each item's name, ordering items by `Position`. The list
returns active plans only, ordered by member display name.

`TrainingPlanStore(AppDbContext db)` — tracked reads for mutation, `Add`, an items-clear that removes
the plan's existing item rows, and an exercise-validation query returning which of a set of ids exist
and are active. Nothing here calls `SaveChanges`.

#### 8. Wiring

**File**: `src/Program.cs`

**Intent**: Register the seams and map the two new groups.

**Contract**: `AddScoped<ITrainingPlanQuery, TrainingPlanQuery>()` and
`AddScoped<ITrainingPlanStore, TrainingPlanStore>()` beside the exercise registrations at `:204-207`.
`MapTrainingPlanEndpoints()` and `MapMyPlanEndpoints()` after `MapExerciseEndpoints()` at `:254-261`,
before the environment-guarded probes and `MapFallbackToFile`.

#### 9. A trainer test user

**File**: `tests/po-prostu-silka.Tests/IntegrationTestFixture.cs`

**Intent**: The suite seeds an active admin, member, pending and blocked account; a trainer is now a
distinct authorization case and needs its own seeded user.

**Contract**: Add `TestUsers.ActiveTrainerEmail` and seed it in `InitializeAsync` via the existing
`CreateUserAsync(email, status, role, displayName)` helper with `ApplicationRoles.Trainer`. The account
must **also** hold `User` — a trainer who holds only `Trainer` fails `ActiveMember` by design
(`ApplicationRoles.cs:50`), and seeding one that way would test a state the product cannot produce.

#### 10. Endpoint tests

**Files**: `tests/po-prostu-silka.Tests/TrainingPlanEndpointTests.cs`,
`tests/po-prostu-silka.Tests/MyPlanEndpointTests.cs`

**Intent**: Pin the contract, the authorization matrix, and the invariant.

**Contract**: `[Collection(nameof(IntegrationCollection))]` with primary-constructor fixture DI, a
private record mirroring each DTO, and `Snake_Case_With_Capitals` sentence names, per
`ExerciseEndpointTests.cs:30-46`.

The authorization matrix uses the `EveryRoute` `TheoryData` pattern (`ExerciseEndpointTests.cs:94-140`)
for each new group: anonymous → 401, wrong role → 403. Add a case proving a **trainer can now read**
`GET /api/admin/exercises` but **still cannot write** it — that is the whole point of the group split.

The invariant gets two tests, matching `BookingEndpointTests.cs:514-537`:
- deterministic — assign, assign again, exactly one plan is `Active` and the first is `Archived` with
  an `ArchivedAt`;
- concurrent — N authenticated clients (each its own `HttpClient`, hence its own cookie and DI scope)
  race `Task.WhenAll` to assign to the same member; afterwards exactly one plan is `Active`.

Also pin: `GET /mine` returns 204 for a member with no plan; a member cannot read another member's
plan; `GET /mine/exercises/{id}` refuses an exercise that is not in the caller's plan with 404; an
exercise deactivated after assignment is still returned inside the plan; `PUT` with a changed member is
`409 member_changed`; every validation bound refuses at its edge.

### Success Criteria:

#### Automated Verification:

- Backend builds warning-free: `dotnet build` from `src/`
- Full suite passes, including the new files: `dotnet test`
- The concurrent-assignment test passes repeatedly: `dotnet test --filter Concurrent` run three times
- The `EveryRoute` matrices cover every route on both new groups and the split exercise groups

#### Manual Verification:

- The race test fails red when `IX_TrainingPlans_Member_Active` is weakened to non-unique in the migration — confirm this once by hand, then restore it (the discipline `class-booking-and-cancel/plan.md:346-356` requires). Commenting out the `ConcurrencyStamp` rotation instead does NOT fail the race test; see the "Adapted during implementation" note above
- Signed in as a trainer via the API, `GET /api/admin/exercises` returns 200 and `POST /api/admin/exercises` returns 403
- Signed in as a plain member, `GET /api/plans/mine` returns 204 before assignment and the plan after
- A blocked member's plan row is still `Active` in the database after the block, and the member gets 403 rather than an empty plan

**Implementation Note**: After completing this phase and all automated verification passes, pause here
for manual confirmation from the human that the manual testing was successful before proceeding to the
next phase.

---

## Phase 3: The trainer's builder and the member's plan screen

### Overview

Both Angular surfaces: the authoring screens under `/trainer/plans` and the member's `/my-plan`. All
routes lazy, following the `admin/exercises` precedent, because the initial bundle has ~25 kB of headroom
and this phase adds a new dependency.

### Changes Required:

#### 1. The CDK dependency

**File**: `src/app/package.json`

**Intent**: Add `@angular/cdk` for `cdkDropList` reordering.

**Contract**: `@angular/cdk` at `^22.1.0` — the CDK's major version tracks Angular's, and every
`@angular/*` package here is on `^22.1.0`. Installed with npm 11 (pinned via `packageManager`). Only
`@angular/cdk/drag-drop` is imported, and only by the builder component, so it lands in that route's
lazy chunk rather than the initial bundle.

#### 2. Role signal and guard

**Files**: `src/app/src/app/core/auth/auth.service.ts`,
`src/app/src/app/core/auth/trainer.guard.ts`

**Intent**: Let the SPA know whether the session may author plans, and keep the authoring routes off
other members' screens.

**Contract**: Add an `isTrainer` computed to `AuthService` mirroring `isAdmin:29`, reading
`ROLES.trainer` from `core/auth/roles.ts` — no new role literal anywhere.

`trainer.guard.ts` is a `CanActivateFn` copied in shape from `admin.guard.ts:14-36`: SSR no-op via
`isPlatformServer`, await `loadCurrentUser()` when the session is unresolved, admit when
`(isTrainer() || isAdmin()) && isActive()`, else `router.createUrlTree(['/'])`. Carry the same docblock
note that this hides a screen and secures nothing — the API is the boundary.

#### 3. Models and service

**Files**: `src/app/src/app/core/training/training-plan.models.ts`,
`src/app/src/app/core/training/training-plan.service.ts`

**Intent**: Mirror the API contract and expose it as promises, matching `exercise.service.ts:27-71`.

**Contract**: Interfaces mirroring `TrainingPlanSummary`, `TrainingPlanDetail`, `TrainingPlanItemView`,
`TrainingPlanRequest`, `TrainingPlanItemRequest`, and a `TrainingPlanFailure` reason union — the same
closed-union shape as `exercise.models.ts:69-82`, so the builder can map each reason onto the control
that owns it.

`TrainingPlanService`, `providedIn: 'root'`, plain `HttpClient` + `firstValueFrom`, every method
returning `Promise<T>`, catching nothing. Methods for the trainer routes plus `getMine()` (which must
treat `204` as `null`, not as a parse error) and `getMyExercise(id)`.

#### 4. Trainer plan list

**Files**: `src/app/src/app/features/trainer/plans/plans.{ts,html,scss,spec.ts}`

**Intent**: The entry point: every active plan in the club, and a way into creating a new one.

**Contract**: Standalone component, signals for `rows`/`loading`/`loadFailed`, `@if`/`@for` control
flow, the repo's standard loading / error-with-retry / empty states. Rows show member name, plan name,
item count and assignment date, and link to `/trainer/plans/:id`. A "Nowy plan" link to
`/trainer/plans/new`. Client-side search over member and plan name, matching how the exercise list
filters (`exercises.ts:32-47`) — the API returns everything.

#### 5. Plan builder

**Files**: `src/app/src/app/features/trainer/plans/plan-builder.{ts,html,scss,spec.ts}`

**Intent**: One component for create and edit, branching on the `:id` route param — the pattern
`exercise-form.ts:109-114` and `class-form.ts:110-144` both use.

**Contract**: Reactive Forms via `inject(FormBuilder).nonNullable`. A plan-level group for name and
member (member selection disabled in edit mode), and a `FormArray` of item groups. Client
`Validators` mirror the endpoint constants exactly: name required + `maxLength(120)`, reps
`maxLength(50)`, note `maxLength(500)`, sets `min(1)/max(20)`, weight `min(0)/max(999.99)`, rest
`min(0)/max(3600)`, at least one and at most 50 items.

The member picker loads `GET /api/trainer/plans/members` (Phase 2, §4) — the existing member list is
`Admin`-only, so trainers get their own minimal read of assignable members rather than a loosened
admin endpoint.

Exercise selection reads `GET /api/admin/exercises` (now trainer-readable), filters to active
client-side, and offers a search box. Selecting an exercise appends an item row.

Reordering uses `cdkDropList` on the item list with `cdkDrag` on each row and `moveItemInArray` in the
`cdkDropListDropped` handler, then reassigns the `FormArray` order. Per-row remove button. Submit maps
failure reasons onto the owning control via `setErrors` + `markAsTouched`
(`exercise-form.ts:193-244`), with only form-level reasons falling back to the banner.

#### 6. Member plan screen

**Files**: `src/app/src/app/features/my-plan/my-plan.{ts,html,scss,spec.ts}`

**Intent**: The member's read of their active plan.

**Contract**: Loads `GET /api/plans/mine` on init. Three distinct states: loading, load-failed with a
retry button, and **no plan** (a plain `.empty` card — distinguished from a failure, which is exactly
the test `exercise-library` wrote for its own empty state). With a plan: the plan name, "Plan od:
{author}", and the ordered item list. Each row shows the exercise name and whichever prescription
fields are populated — absent fields are omitted entirely, not rendered as dashes, matching
`exercise-detail.html:84-101`. Each row links to `/my-plan/exercises/:id`.

#### 7. Member exercise detail

**Files**: `src/app/src/app/features/my-plan/plan-exercise-detail.{ts,html,scss,spec.ts}`

**Intent**: FR-020 — exercise instructions and video, reached only from the plan.

**Contract**: Adapted from `exercise-detail.ts:36-83`, which was written to be adapted for exactly this.
Reads `GET /api/plans/mine/exercises/{id}`, keeps the distinct `notFound` (404) vs `loadFailed` states,
and keeps the video handling **unchanged in substance**: a `computed<SafeResourceUrl|null>` that
re-validates the id against `^[A-Za-z0-9_-]{11}$` immediately before
`bypassSecurityTrustResourceUrl(embedUrl(id))`. Carry the regression spec that a malformed id and a
`javascript:` string render no iframe — `exercise-library` review F4 required it, and copying the
component without copying that test is how it would be lost.

Back link returns to `/my-plan`.

#### 8. Routes and navigation

**Files**: `src/app/src/app/app.routes.ts`, `src/app/src/app/app.html`

**Intent**: Register the five routes and surface the two entry points.

**Contract**: All five lazy via `loadComponent`, with `new` registered **before** `:id`:
`/trainer/plans`, `/trainer/plans/new`, `/trainer/plans/:id` (all `[authGuard, trainerGuard]`), and
`/my-plan`, `/my-plan/exercises/:id` (both `[authGuard, activeMemberGuard]`).

Navigation: a "Mój plan" link beside "Moje zajęcia", gated on `auth.isActive()` like its neighbours; a
"Plany" link gated on `(auth.isTrainer() || auth.isAdmin()) && auth.isActive()` — the condition must
match `trainerGuard`'s exactly, for the reason the existing admin-link comment spells out.

#### 9. Bundle measurement

**File**: `src/app/angular.json` (read only)

**Intent**: Confirm the CDK and five new screens did not push the initial bundle past 500 kB.

**Contract**: Run `npm run build` and read the reported initial bundle. If a budget warning appears
despite the lazy routes, find what pulled the new code into the initial chunk before relaxing the
budget — and record the finding as an "**Adapted during implementation.**" note in this plan, per
`context/foundation/lessons.md`.

**Adapted during implementation.** Two files the phase's contract did not list had to change, and
one spec needed a harness detail:

- `src/app/src/app/app.spec.ts` — the shell's `AuthService` stubs are partial objects, so adding
  `auth.isTrainer()` to `app.html` broke every one of them with
  `TypeError: ctx_r1.auth.isTrainer is not a function`. Each stub gained `isTrainer`, and three tests
  were added for the two new links, mirroring the existing approvals-link matrix.
- `src/app/src/app/core/auth/trainer.guard.spec.ts` — written alongside the guard, matching
  `admin.guard.spec.ts`; the contract named the guard but not its spec.
- `my-plan.spec.ts` settles TWO rounds of `whenStable`/`detectChanges` rather than one.
  `TrainingPlanService.getMine()` is the only service method that awaits and then post-processes
  (204 → `null`), so its promise resolves one microtask after the response and a single round
  renders the loading state.

### Success Criteria:

#### Automated Verification:

- Frontend unit tests pass: `npm test` from `src/app/`
- Formatting and linting pass: `npm run quality:check` from `src/app/`
- Production build succeeds with **no bundle-budget warning**: `npm run build` from `src/app/`
- The plan-exercise-detail spec proves a malformed video id renders no iframe
- The my-plan spec distinguishes "no plan" from "load failed"

#### Manual Verification:

- Signed in as a trainer: "Plany" appears in the navigation, `/trainer/plans` loads, a new plan can be created for an active member with several exercises and full parameters, and it saves
- Dragging a row reorders it, and the order survives a save and a reload
- Assigning a second plan to the same member replaces the first — the trainer list shows one plan for that member
- Signed in as that member: "Mój plan" appears, the plan renders in the trainer's order with the author's name, and tapping a row opens the exercise's instructions and video
- A member with no plan sees the empty card, not an error
- Signed in as a plain member, `/trainer/plans` redirects to home
- The builder is usable on a phone-width viewport: the member picker, the exercise search and the parameter fields are all reachable, and the drag handles are large enough to hit
- **Known and accepted**: the item order cannot be changed without a pointer — there is no keyboard path. Confirm the rest of the builder (adding, removing, editing, saving) works from the keyboard alone.

**Implementation Note**: After completing this phase and all automated verification passes, pause here
for manual confirmation from the human that the manual testing was successful.

---

## Testing Strategy

### Unit Tests:

- Frontend specs per component, using `HttpTestingController` with `vi.waitFor` and the `settle()`
  helper, asserting against real DOM text in Polish (`exercises.spec.ts:45-80` pattern).
- The video-id re-validation regression spec, carried over rather than reinvented.
- The my-plan empty-vs-failed state distinction.

### Integration Tests:

- Full authorization matrices for `/api/trainer/plans`, `/api/plans` and both halves of the split
  exercise group — anonymous 401, wrong role 403, right role 200.
- Assignment: create, edit, replace-and-archive, and every validation bound at its edge.
- The member read: own plan only, 204 when absent, exercise reachable only from inside the plan, a
  deactivated exercise still visible in an assigned plan.
- The invariant: deterministic replace, and the concurrent race with N clients.

### Manual Testing Steps:

1. Grant the Trainer role to a test account, sign that account out and in again (the role does not
   reach an existing cookie for up to two minutes).
2. Build a plan of five exercises with mixed parameters, reorder two rows by dragging, save.
3. Reload the builder and confirm the order persisted.
4. Assign a second plan to the same member; confirm exactly one plan shows for them.
5. Sign in as that member; confirm the plan, the author name, the order, and the exercise detail with
   its video.
6. Deactivate one of the plan's exercises as an admin; confirm the member's plan is unchanged.
7. Block that member as an admin; confirm they are refused, then unblock and confirm the plan is still
   there.
8. Repeat step 2 on a phone-width viewport.

## Performance Considerations

The plan builder loads the **full** exercise library through `GET /api/admin/exercises`, including the
long prose fields and video ids it does not use. `exercise-library/plan.md:625-632` named this slice as
the trigger for splitting that endpoint into a trimmed list DTO; the decision here was to reuse the
existing endpoint and accept the weight, because a single gym's library is small and a second read
surface over the same table is cost without a present benefit. The threshold worth revisiting is when
the library grows past a few hundred exercises with populated prose, at which point the split is a
trimmed `ExercisePickerRow` on the trainer read group.

Plan reads project directly into DTOs with `AsNoTracking()` and join for names, so the member's screen
is one query. The item list is capped at 50 rows.

## Migration Notes

`AddTrainingPlans` is purely additive: two new tables, no alteration of any existing table, no data
touched. `Down` drops both in dependency order and is fully reversible — verify with a real
down-then-up round trip against the local Docker SQL Server before the Phase 1 commit, not only with
`dotnet ef migrations script`.

Rollback redeploys the previous artifact without rolling back schema, per `AGENTS.md`. That is safe
here: the previous release simply ignores two empty tables.

## References

- Research: `context/changes/training-plans/research.md`
- Brief: `context/changes/training-plans/plan-brief.md`
- Upstream slice: `context/archive/2026-09-04-exercise-library/plan.md`
- Invariant precedent: `context/archive/2026-09-03-class-booking-and-cancel/plan.md:120-164,191-330`
- Role precedent: `context/archive/2026-09-02-trainer-role-and-assignment/plan.md:6-9,74-76`
- Standing lesson: `context/foundation/lessons.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles.

### Phase 1: Domain model, schema and migration

#### Automated

- [x] 1.1 Backend builds warning-free
- [x] 1.2 Migration script generates cleanly
- [x] 1.3 Migration applies against the local database
- [x] 1.4 Existing test suite still green

#### Manual

- [x] 1.5 Down-then-up round trip runs clean against local SQL Server
- [x] 1.6 Filtered unique index rejects a second active plan, allows an archived one
- [x] 1.7 `GET /health` healthy after the migration

### Phase 2: Authorization, the plans API, and the member read surface

#### Automated

- [x] 2.1 Backend builds warning-free
- [x] 2.2 Full suite passes including the new test files (367 green)
- [x] 2.3 Concurrent-assignment test passes on three consecutive runs
- [x] 2.4 `EveryRoute` matrices cover both new groups and both split exercise groups

#### Manual

- [x] 2.5 Race test fails red with the filtered unique index weakened, then restored (the stamp rotation alone does not — see the adaptation note in Phase 2)
- [x] 2.6 Trainer can read the exercise library and cannot write it
- [x] 2.7 `GET /api/plans/mine` returns 204 before assignment, the plan after
- [x] 2.8 A blocked member's plan row stays Active and the member is refused with 403

### Phase 3: The trainer's builder and the member's plan screen

#### Automated

- [x] 3.1 Frontend unit tests pass
- [x] 3.2 `npm run quality:check` passes
- [x] 3.3 Production build succeeds with no bundle-budget warning (initial 479.22 kB against 500 kB; the CDK landed in the `plan-builder` lazy chunk)
- [x] 3.4 Malformed-video-id spec proves no iframe renders
- [x] 3.5 My-plan spec distinguishes "no plan" from "load failed"

#### Manual

- [x] 3.6 Trainer creates and saves a plan with several exercises and full parameters
- [x] 3.7 Dragging reorders rows, and the order survives save and reload
- [x] 3.8 A second assignment replaces the first for that member
- [x] 3.9 Member sees the plan, the author, the order, and the exercise detail with video
- [x] 3.10 Member with no plan sees the empty card, not an error
- [x] 3.11 A plain member is redirected away from `/trainer/plans`
- [x] 3.12 The builder is usable at phone width
- [x] 3.13 Everything except reordering works from the keyboard alone (reordering is a known, accepted gap)
