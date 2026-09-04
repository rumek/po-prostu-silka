---
date: 2026-09-04T23:19:37+02:00
researcher: Karol Rumianowski
git_commit: e1745c95c54959244ea5494396f814b96471d306
branch: main
repository: PoProstuSilka
topic: "Training plans authored by trainers and assigned to members (S-11)"
tags: [research, codebase, training, authorization, ef-core, angular, trainer-role]
status: complete
last_updated: 2026-09-04
last_updated_by: Karol Rumianowski
---

# Research: Training plans authored by trainers and assigned to members

**Date**: 2026-09-04T23:19:37+02:00
**Researcher**: Karol Rumianowski
**Git Commit**: `e1745c95c54959244ea5494396f814b96471d306`
**Branch**: `main`
**Repository**: PoProstuSilka

## Research Question

Roadmap slice **S-11 `training-plans`** (`context/foundation/roadmap.md:257-268`), reframed by the change
notes: *"plan authoring is available to the Trainer; we create plans for users"*
(`context/changes/training-plans/change.md:12`).

Concretely: a trainer (or admin) builds an ordered list of exercises — sets, reps, weight, rest time,
note — and assigns it to a member; the member sees their current plan and opens any exercise's details
from within it. What does the codebase already provide, what conventions must the implementation match,
and where does the stated intent diverge from the written product record?

### Scope decisions taken with the user before this research

| Question | Decision |
| --- | --- |
| Who may author and assign plans? | **Trainer *and* Admin.** Consistent with prd-v2's additive role model; does not take from Admin what `prd.md` FR-015/FR-016 granted. |
| Whom may a trainer assign to? | **Any active member.** No trainer↔member relationship entity is introduced. |
| A blocked member's assigned plan? | **Left untouched.** Blocking cuts access; it does not destroy training data. Resolves the roadmap's blocking Unknown. |

## Summary

**The ground is well prepared and the shape of the work is unusually predictable.** The `Training`
bounded context already exists in all three layers, landed whole by `exercise-library` two commits ago,
and that slice deliberately left behind the exact seams this one consumes: `Exercise` is deactivated
rather than deleted *specifically* so plan rows cannot be orphaned, and the read-only exercise-detail
screen was built as "the layout S-11 will later adapt for members".

Five findings dominate:

1. **The Trainer role confers nothing today.** `ApplicationRoles.Trainer` exists, is seeded, is granted
   and revoked by admins, and populates the instructor picker — but there is **no `Trainer`
   authorization policy**, no trainer guard in Angular, no `isTrainer()` signal, and no trainer-facing
   route. This change is the first to give the role a capability, and it retires prd-v2's Non-Goal
   *"No trainer screen."* Adding the policy is a two-file edit with a clear precedent.
2. **The "one active plan per member" invariant has an exact template.** `class-booking-and-cancel`
   solved a harder version of the same problem — a rotated string concurrency token on the parent row,
   `IUnitOfWork.TrySaveAsync` returning a `SaveOutcome` enum, a bounded retry loop, and a **filtered
   unique index** as defence in depth. The index for this slice is nearly literal:
   `HasIndex(x => x.MemberUserId).IsUnique().HasFilter("[Status] = 0")`.
3. **Two things in this slice have no precedent in the repo at all**: an *ordered child collection*
   (no owned types, no `OwnsMany`, no order/sequence column exists anywhere), and a **`decimal`**
   column (weight in kg — there is not a single `decimal` property in the codebase, so precision is a
   convention this change establishes rather than follows). Both need explicit decisions in the plan.
4. **List reordering has no UI precedent either, and `@angular/cdk` is not installed.** The calendar's
   drag interactions come from `angular-calendar` + `angular-draggable-droppable`, plus a hand-rolled
   pointer-event gesture. Reordering plan exercises is either a new dependency or up/down buttons.
5. **The bundle budget is the known trap.** Initial bundle sits at 475.01 kB against a 500 kB warning
   threshold; `exercise-library` breached it at 502.88 kB and had to convert three screens to
   `loadComponent`. New screens here should be lazy from the first line of the plan.

## Detailed Findings

### 1. The Training bounded context as it stands

The context is complete across the three layers and is the closest possible template.

**Domain** — `src/Domain/Training/`

- `Exercise.cs:27-80` — a deliberately anemic POCO: `Guid Id`, required `Name`, eight optional `string?`
  fields (`Description`, `MuscleGroup`, `Difficulty`, `Equipment`, `Preparation`, `StartingPosition`,
  `Execution`, `VideoId`), `bool IsActive = true`, `DateTimeOffset CreatedAt`. Public setters
  throughout; no factory method, no guard clauses. Identity is assigned by the caller
  (`ExerciseEndpoints.cs:196`, `Guid.NewGuid()`), not by the DB or the entity.
- The documented invariant "absent is `null`, never empty string" (`Exercise.cs:8-12`) is enforced by
  `Normalize()` in the Application layer (`ExerciseEndpoints.cs:433-434`), not by the entity.
- `YouTubeVideoId.cs` — a static, dependency-free parser (`TryParse:70-123`, `ToWatchUrl:126`) that is
  unit-testable without a database. The one piece of real domain logic in the context.

**Application** — `src/Application/Training/ExerciseEndpoints.cs` (491 lines; DTOs, endpoints, validation
and the persistence seams all in one file)

- Group registration at `:130-132`:
  `app.MapGroup("/api/admin/exercises").WithTags("Training").RequireAuthorization(AuthorizationPolicyNames.Admin)`
  — the policy is applied **once at the group**, never per-endpoint. This is universal in the repo.
- Routes (`:134-142`): `GET /`, `GET /{id:guid}`, `POST /`, `PUT /{id:guid}`,
  `POST /{id:guid}/deactivate`, `POST /{id:guid}/activate`. **No `DELETE`** — its absence is pinned by a
  test (`ExerciseEndpointTests.cs:561`).
- DTO convention: one record serves list *and* detail (`ExerciseSummary:23-35`), one serves create *and*
  edit (`ExerciseRequest:52-61`), plus `ExerciseFailure(string Reason)` (`:76`). Errors are **records
  returned as JSON**, never exceptions and never a `Result<T>` type:
  `Results.Json(failure, statusCode: 400|409)`. No `ProblemDetails` anywhere.
- Validation is hand-rolled per endpoint file returning `IResult?` (null = valid) — `Validate:362-404`.
  There is no validation library in the repo (stated at `:354-355`).
- Length constants mirror the EF `HasMaxLength` values 1:1 (`:105-126`), with a comment calling this
  "the single most repeated finding in this repo's review history" (`:100-104`).
- **No server-side pagination, filtering or search** for either exercises or members. `GetAllAsync`
  returns everything; the SPA filters client-side (`:148-153`). The one exception is a single optional
  enum query param on the member list.
- Seams declared at the bottom of the same file: `IExerciseQuery` (`:462-466`) and `IExerciseStore`
  (`:476-490` — `FindAsync`, `Add`, `IsNameTakenAsync`; **no `Remove`**). Explicitly not a generic
  repository (`:469-471`).

**Infrastructure** — `src/Infrastructure/Training/`

- `ExerciseQuery.cs:19-36` — `AsNoTracking()` + `.Select(...)` projecting **straight into the DTO** in
  the LINQ query; ordering `OrderByDescending(IsActive).ThenBy(Name)`.
- `ExerciseStore.cs:14-17` — `FindAsync` is deliberately **tracked** (callers mutate and rely on the
  change tracker); `IsNameTakenAsync:37-47` uses plain `==` rather than `ToLower()` to stay sargable
  against SQL Server's case-insensitive collation and the filtered index.
- Nothing in a store calls `SaveChanges`; the endpoint commits through `IUnitOfWork`.

**Wiring** — `src/Program.cs:204-207` registers both seams `AddScoped` (so they share the request's
`DbContext` with `IUnitOfWork`); `:254-261` maps endpoint groups in order, all after
`UseAuthentication()/UseAuthorization()` (`:248-249`) and before `MapFallbackToFile("index.html")`
(`:286`, which must stay last).

### 2. Roles and authorization — the one real gap

**What exists** (`src/Domain/ApplicationRoles.cs:20-51`):

- `User` (`:23`), `Admin` (`:26`), `Trainer` (`:33`); `All = [User, Admin, Trainer]` (`:39`) is what the
  seeder creates; `MemberFacing = [User, Admin]` (`:50`) is what satisfies `ActiveMember`.
- **`Trainer` alone does not pass `ActiveMember`** — by design. A real trainer is a member who was
  granted the role, so they also hold `User`.

**Policies** (`src/Domain/AuthorizationPolicyNames.cs:14-24`, built in
`src/Infrastructure/Authorization/AuthorizationPolicies.cs:35-47`) — there are exactly **two**:

```csharp
.AddPolicy(ActiveMember, policy => policy
    .RequireAuthenticatedUser()
    .RequireClaim(StatusClaimType, nameof(AccountStatus.Active))
    .RequireRole(ApplicationRoles.MemberFacing))
.AddPolicy(Admin, policy => policy
    .RequireAuthenticatedUser()
    .RequireClaim(StatusClaimType, nameof(AccountStatus.Active))
    .RequireRole(ApplicationRoles.Admin));
```

**No "Admin OR Trainer" policy exists.** Adding one is a two-place edit, and `RequireRole` already
demonstrates OR-over-an-array semantics via `MemberFacing`:

- a constant beside `Admin` in `src/Domain/AuthorizationPolicyNames.cs:22-23` (Domain, deliberately —
  so Application can reference it without touching Infrastructure);
- a `.AddPolicy(...)` with `.RequireRole(ApplicationRoles.Trainer, ApplicationRoles.Admin)` in
  `AuthorizationPolicies.cs:35-47`.

**Status claim and session timing** — `AppUserClaimsPrincipalFactory.cs:24-32` mints the
`account_status` claim at sign-in; the security-stamp validator refreshes it on a 2-minute interval
(`Program.cs:133-134`). Consequence carried over from `trainer-role-and-assignment/plan.md:70`: a
freshly granted `Trainer` role does not reach the browser's cookie until `POST /api/auth/refresh` or
that interval fires. Any trainer-gated UI must account for it.

**Current user inside a handler** — there is no `ICurrentUserService` and no `ClaimsPrincipal`
extension. The convention is to bind `ClaimsPrincipal principal` as an endpoint parameter and call
`userManager.GetUserId(principal)`, returning `Results.Unauthorized()` on null —
`BookingEndpoints.cs:186` (book), `:299` (cancel mine), `:359` (get mine). That last one is the exact
pattern for "member reads their own plan": resolve the id from the principal and filter by it, never
trust an id from the route or body.

**Frontend state** — `src/app/src/app/core/auth/`:

- `roles.ts:13-22` already exports `ROLES = { member: 'User', admin: 'Admin', trainer: 'Trainer' }`.
  This file is the canonical home for role literals (extracted during `trainer-role-and-assignment`
  review finding F10 after they had drifted into three places) — consume it, do not add a fourth copy.
- `auth.service.ts` has `isAuthenticated:24`, `isActive:27`, `isAdmin:29`. **There is no `isTrainer`.**
- `admin.guard.ts:14-36` is the template for a new guard: SSR no-op via `isPlatformServer`, await
  `auth.loadCurrentUser()` if unresolved, check the role + active status, else
  `router.createUrlTree(['/'])` — home, not `/login`, because an authenticated non-admin is not missing
  a session. Guards are documented as UI-only (`admin.guard.ts:10-12`); the API is the real boundary.
- `app.routes.ts` has **no route referencing trainer status at all**. The only Trainer mention in the UI
  is the admin's grant/revoke action on the members list (`features/admin/members/members.html:142-155`).

### 3. Persistence conventions, and the two things without precedent

**`AppDbContext`** (`src/Infrastructure/Persistence/AppDbContext.cs`) — `IdentityDbContext<ApplicationUser>`
with primary-constructor DI (`:19-20`); DbSets at `:22-32`; `OnModelCreating:34-41` calls
`base.OnModelCreating` **first** (inverting silently drops the Identity tables) then
`ApplyConfigurationsFromAssembly`. No global conventions are configured — every length, conversion and
delete behaviour is set per-property in its `IEntityTypeConfiguration<T>`. A new `DbSet<TrainingPlan>`
must be added by hand at `:32`; the configuration classes are auto-discovered.

**Configuration conventions** (all seven files in `Configurations/` follow one shape:
`ToTable` → `HasKey` → `Property(...)` → `HasIndex`/`HasOne`):

- Enums: `HasConversion<int>()` + `HasDefaultValue(...)` — `BookingConfiguration.cs:22-25`,
  `ClassConfiguration.cs:42-45`.
- Identity FK columns: `HasMaxLength(450)` exactly, matching `AspNetUsers.Id` —
  `BookingConfiguration.cs:29`, `ClassConfiguration.cs:79`.
- Concurrency token: `ClassConfiguration.cs:34-37` — a `string` GUID, `HasMaxLength(36)`,
  `.IsConcurrencyToken()`. Not a SQL `rowversion`; it mirrors Identity's own `ConcurrencyStamp`.
- Delete behaviour: `Restrict` wherever a business invariant matters (`BookingConfiguration.cs:34-42`,
  `ClassConfiguration.cs:69-72,81-84`); `Cascade` only for `PushSubscription`→user
  (`PushSubscriptionConfiguration.cs:29-32`).
- Filtered unique indexes are the house pattern for "unique among the active ones":
  `ClassTypeConfiguration.cs:36-39`, `ExerciseConfiguration.cs:48-52`, and the composite
  `BookingConfiguration.cs:56-59` —
  `HasIndex(x => new { x.ClassId, x.MemberUserId }).IsUnique().HasFilter("[Status] = 0")`, where the
  filter references the enum's **numeric** literal.
- `HasMaxLength` on essentially every string column; `IsRequired(false)` appears nowhere (nullable is
  left implicit — `ExerciseConfiguration.cs:17-20`).
- Soft state over hard delete is the default everywhere; the single guarded exception is class deletion,
  refused with `409 has_bookings`.

**No precedent — ordered child collection.** There are **no owned types** (`OwnsOne`/`OwnsMany`) anywhere,
and no order/sequence column on any entity. Every relationship so far is an FK between two top-level,
independently mapped entities (`Class`→`ClassType`/`Instructor`, `Booking`→`Class`/`Member`). Plan items
therefore have no in-repo template: the plan must decide between a separately mapped `TrainingPlanItem`
entity with an explicit `int Position` (closest to existing conventions) and an owned collection (new
ground for this codebase).

**No precedent — `decimal`.** `grep -rn "decimal" src` finds nothing outside `Migrations/` and `obj/`.
There is no `HasPrecision`, no `HasColumnType("decimal(...)")`. Weight in kg would be the first decimal
column in the schema, so its precision/scale is a convention this change **sets**. The documentation bar
for such a choice is `Class.cs:92-97` — an explanatory doc comment on why the storage choice was made.

**Exercise reference must be `Restrict`.** `exercise-library/research.md:367-371` records that hard delete
was rejected for `Exercise` precisely because "a training plan will reference exercises, so a hard delete
would either orphan plan rows or be blocked by a `Restrict` FK". The FK from a plan item to `Exercises`
should be `Restrict`, matching every other business-critical FK.

**Migrations** (11 in `src/Infrastructure/Persistence/Migrations/`, latest `20260904162124_AddExercises`):
every `Down` mirrors `Up` in reverse, without exception. Exactly one migration contains a destructive data
step (`20260902111715_AddClassTypes.cs:44`) and it is documented at length as a deliberate one-time
exception. **No migration seeds reference data.** Commands, from the archive:
`dotnet ef migrations add <Name> -p src/po-prostu-silka.csproj -o Infrastructure/Persistence/Migrations`,
applied with an explicit `--connection` (never the `AppDbContextFactory` placeholder), reversibility
checked with `dotnet ef migrations script <Previous> <New>` and a real down-then-up round trip before the
phase commit.

### 4. The hard-invariant pattern to reuse for "one active plan per member"

`class-booking-and-cancel` solved a strictly harder version of this problem, and its mechanism transfers
almost verbatim.

- **Token, not transaction.** `Class.cs:99` —
  `public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();`, with an extensive doc
  comment (`:64-98`) stating that the guarantee holds only because *every* writer that changes either
  side of the inequality explicitly reassigns it. `IsConcurrencyToken()` does **not** auto-regenerate on
  save (unlike `IsRowVersion()`) — this was that change's one CRITICAL review finding: the capacity-edit
  path validated correctly but forgot to rotate, so a shrink racing a booking could still overbook.
- **Retry loop.** `BookingEndpoints.cs:126` — `MaxAttempts = 10`, adapted up from a planned 3 because
  with N racers the losers' attempt budget is consumed by the winners. The loop
  (`BookAsync:192-268`): re-read → business checks → mutate → **rotate the stamp** (`:239`) →
  `unitOfWork.TrySaveAsync` (`:241`) → on conflict `unitOfWork.DiscardChanges()` (`:267`, a
  `ChangeTracker.Clear()`) and loop.
- **`IUnitOfWork` keeps EF Core out of Application.**
  `src/Application/Persistence/IUnitOfWork.cs:11-34,70-77` defines
  `SaveOutcome { Saved, ConcurrencyConflict, UniqueViolation }`;
  `src/Infrastructure/Persistence/UnitOfWork.cs:26-61` catches `DbUpdateConcurrencyException` and
  `DbUpdateException` with SQL error 2601/2627 and maps them onto the enum. **Use `TrySaveAsync`, never a
  bare `SaveChangesAsync`, on any write guarded by a uniqueness rule** — the naive pre-check-then-save
  race has been a review finding on three separate slices, most recently `exercise-library` F1.
- **No explicit DB transaction.** `IUnitOfWork.cs:44-49` states the policy: one `SaveChangesAsync` is
  already atomic, and opening a transaction would require
  `Database.CreateExecutionStrategy().ExecuteAsync(...)` because `EnableRetryOnFailure` is on
  (`Program.cs:43-46`) — it throws at runtime otherwise. If the plan does need an explicit transaction
  for the replace-and-archive step, that execution-strategy wrapper is mandatory.
- **Filtered unique index as defence in depth**, not as the primary mechanism
  (`BookingConfiguration.cs:49-52`). For this slice the analogue is direct: a unique index on the
  member's id filtered to the active status makes "two active plans" unrepresentable in the database
  even if a future write path forgets the token.
- **No collection navigation on the aggregate root that a write path could count through** —
  deliberately avoided in booking (`plan.md:142-147`, "a standing invitation for a write path to count
  through it"). Relevant if `TrainingPlan` gains an `Items` collection: reads may use it, capacity-style
  checks may not.

### 5. Angular conventions and the two frontend unknowns

**Structure.** `app.routes.ts:1-88` is a flat `Routes` array; `core/` holds services/guards/models by
bounded context and **no components**; `features/<area>/<screen>/` holds one component as four co-located
files (`.ts`/`.html`/`.scss`/`.spec.ts`); `shared/` holds cross-feature UI (today only `calendar/`).
`app.config.ts:21-39` — `provideRouter`, `provideClientHydration()` (SSR is on),
`provideHttpClient(withFetch(), withInterceptors([authInterceptor]))` (relative `/api/...`, cookie auth),
`provideServiceWorker` gated on `!isDevMode()`, Polish locale (`:19,23`).

**Route ordering rule, stated three times** (`app.routes.ts:52,68-69,81-82`): the literal `new` segment
must be registered **before** the `:id` route or it is swallowed as a parameter value.

**Component conventions.** Standalone with an `imports: [...]` array; `inject()` for DI everywhere (no
constructor injection); `signal`/`computed`/`effect` as the state primitive; RxJS confined to services and
wrapped in `firstValueFrom` so callers see `Promise<T>`; `@if`/`@for (…; track …)` control flow
exclusively; templates and styles always in separate files. **`ChangeDetectionStrategy.OnPush` is used
nowhere** — default change detection throughout. `input()`/`output()` function APIs for presentational
components.

**The exercise-library screens are the direct template**
(`features/admin/exercises/{exercises,exercise-form,exercise-detail}`):

- `exercise-detail.ts:9-19` says in its own docblock that it is the first read-only detail screen and was
  built to be adapted for the member's plan-exercise view. `exercise-library/plan.md:521` says the same
  from the other side. Video embedding: a `computed<SafeResourceUrl|null>` that calls
  `sanitizer.bypassSecurityTrustResourceUrl(embedUrl(id))` **only after** re-validating the id against
  `^[A-Za-z0-9_-]{11}$` (`:49-55`) — the only `bypassSecurityTrust*` call in the app, and it carries a
  dedicated regression spec. The `<iframe>` (`exercise-detail.html:41-54`) is 16:9, `loading="lazy"`,
  `referrerpolicy="strict-origin-when-cross-origin"`. Absent fields are omitted entirely, not rendered
  as dashes.
- `exercise-form.ts:74-88` — Reactive Forms via `inject(FormBuilder).nonNullable.group({...})`; one
  component serves create and edit, branching on `route.snapshot.paramMap.get('id')` (`:109-114`).
  Server failures are mapped onto the **owning form control** via `control.setErrors(...)` +
  `markAsTouched()` (`:193-244`); only failures with no owning control fall back to the banner. Routing a
  refusal to a banner instead of a control is a repeated review finding across two earlier slices.
- `exercises.ts:32-97` — list state as signals plus a monotonic `generation` counter to drop stale async
  responses, a per-row `busy: Set<string>`, and a `failedId` signal.
- Route params are read synchronously with `route.snapshot.paramMap.get('id')` in `ngOnInit`
  (`exercise-form.ts:109`, `exercise-detail.ts:58`, `class-form.ts:111`) — never the reactive `paramMap`.
- Loading/error/empty states are uniform: `role="status"` "Wczytywanie…" / `role="alert"` with a
  "Spróbuj ponownie" button / an `.empty` card.

**Styling.** A hand-rolled CSS design system, explicitly not Tailwind or Material
(`src/app/src/styles.scss:1-10`). Tokens are CSS custom properties on `:root` (`:82-107`): `--ground`,
`--ink`, `--accent`, `--muted`, `--danger`/`--danger-bg`, `--radius`/`--radius-sm`, an 8-step
`--space-1…7` scale, `--shadow-card`. Shared classes (`.card`, `.panel`, `.field`, `.field-error`,
`.badge`, `.button` with a 44px min-height, `.alert`, `.notice`) live globally; component `.scss` stays
layout-only. Breakpoints are per-component `@media (max-width: 30rem)` overrides with a comment giving
the reason — there is no shared breakpoint token. **Check the real token names before writing SCSS**:
`trainer-role-and-assignment` review F2 was a CRITICAL caused by styling against invented variables
(`--surface`, `--border`) that silently fall back to browser defaults.

**Unknown: reordering.** `@angular/cdk` is **not** in `src/app/package.json:17-33`. The calendar's drag
behaviour comes from `angular-calendar ^0.32.2` with `angular-draggable-droppable ^9.0.1` and
`angular-resizable-element ^8.0.3`, plus a hand-rolled `PointerEvent` gesture in
`shared/calendar/schedule-calendar.ts:492-581`. **There is no list-reordering precedent anywhere.**
Options for ordering plan exercises: up/down buttons (zero dependency, trivially keyboard-accessible),
a hand-rolled pointer drag following the calendar's own precedent, or adding `@angular/cdk/drag-drop`
as a new dependency. Note that two separate archived reviews raised CRITICAL/WARNING findings about
custom interactive widgets shipping without working keyboard support — a drag-only reorder with no
keyboard path would be the third.

**Testing.** `angular.json:76-78` — `@angular/build:unit-test` (Vitest), run via `npm test`. Specs sit
beside the component. Pattern from `exercises.spec.ts:45-80`: `TestBed.configureTestingModule` with
`provideHttpClient()`, `provideHttpClientTesting()`, `provideRouter([])`; requests awaited with
`await vi.waitFor(() => controller.expectOne('/api/...')).flush(data)`;
`afterEach(() => controller.verify())`; a `settle()` helper doing
`await fixture.whenStable(); fixture.detectChanges();`; assertions against real DOM text, in Polish.

**Bundle budget.** `angular.json:41-53` — `initial` warning 500 kB / error 1 MB, `anyComponentStyle`
warning 6 kB / error 8 kB. Current initial bundle: **475.01 kB**. Eager exercise screens took it to
502.88 kB and had to be converted to `loadComponent`, recorded as an "Adapted during implementation"
note (`exercise-library/plan.md:460-468,553-555`, commit `ffffc3c`). **Plan the training-plan screens as
lazy routes from the start**, and measure with `npm run build` before assuming otherwise.

### 6. Blocked members, and how the precedent applies here

`MemberAdminEndpoints.BlockAsync:232-300` does four things in one `SaveChanges`: refuses to block an
`Admin` account (`:252-255`, `409 is_admin` — the only thing stopping the club from locking itself out),
sets `Status = Blocked` (`:265`), rotates `ConcurrencyStamp` and **`SecurityStamp`** (`:266-267` — the
security stamp is what actually kills the live cookie, bounded by the 2-minute validation interval), and
cascade-cancels **active future** bookings via `IBookingStore.CancelActiveFutureForMemberAsync`
(`:289-290`). Past bookings are untouched. No notification is sent. Unblocking restores `Active` but
restores nothing else (`:81`).

That booking cascade is documented as a **deliberate product-driven exception** to the repo's otherwise
firm convention that "access consequences are enforced at read time by policy claims, never by stored
cascade state" (`class-booking-and-cancel/plan.md:158-161`). **The user's decision for this slice — leave
the plan untouched — returns to the default convention rather than diverging from it**: a blocked member
simply cannot reach the read endpoint, because it will sit behind `ActiveMember`, and their plan is
waiting when they are unblocked. That is the argument the plan should record.

### 7. The product-record tension the plan must state explicitly

- `prd.md:104-110` (FR-015, FR-016, FR-017) assigns plan authoring and assignment to the **Admin**, and
  `prd.md:148` still states the Non-Goal *"no Trainer role in MVP"*.
- `prd-v2.md:190-201` introduces the Trainer role but pins its capability to **nothing beyond `User`**
  (`:427-428`), locks the Non-Goal *"No trainer screen"* (`:441`), and files *"What does a trainer
  eventually see after signing in?"* as **Open Question 3** (`:474-476`), deferred to a later change.
- `trainer-role-and-assignment/plan.md:74-75` repeats this: "No trainer-facing screen or permission."

**This change is the answer to prd-v2 Open Question 3, and it retires the "No trainer screen" Non-Goal.**
With the user's decision (Trainer *and* Admin author plans), `prd.md` FR-015/FR-016 are widened, not
overturned — the Admin keeps what it had. Both the retirement and the widening should be recorded in
the plan the way `prd-v2.md:462` recorded retiring the earlier "No Trainer role" Non-Goal, and the
roadmap's `S-11` Unknown (`roadmap.md:266`) should be marked resolved so the slice can leave `blocked`.

## Code References

Backend — Training context:

- `src/Domain/Training/Exercise.cs:27-80` — the entity shape a plan item will reference.
- `src/Domain/Training/YouTubeVideoId.cs:70-126` — dependency-free parser; the pattern for pure domain logic.
- `src/Application/Training/ExerciseEndpoints.cs:130-142` — group registration, policy at the group, route list.
- `src/Application/Training/ExerciseEndpoints.cs:362-404` — hand-rolled validation returning `IResult?`.
- `src/Application/Training/ExerciseEndpoints.cs:462-490` — the `IXQuery`/`IXStore` seam convention.
- `src/Infrastructure/Training/ExerciseQuery.cs:19-36` — `AsNoTracking` + direct DTO projection.
- `src/Infrastructure/Training/ExerciseStore.cs:14-47` — tracked reads for mutation; sargable `==` lookups.
- `src/Program.cs:204-207,254-261` — DI registration and endpoint mapping order.

Authorization:

- `src/Domain/ApplicationRoles.cs:20-51` — `All` vs `MemberFacing`; why `Trainer` alone fails `ActiveMember`.
- `src/Domain/AuthorizationPolicyNames.cs:14-24` — where a `TrainerOrAdmin` constant belongs.
- `src/Infrastructure/Authorization/AuthorizationPolicies.cs:35-47` — where the policy is built.
- `src/Infrastructure/Identity/AppUserClaimsPrincipalFactory.cs:24-32` + `src/Program.cs:133-134` — claim minting and the 2-minute refresh window.
- `src/Application/Scheduling/BookingEndpoints.cs:186,299,359` — `ClaimsPrincipal` → `GetUserId` → owner-scoped query.
- `src/Application/Members/MemberAdminEndpoints.cs:139-143,461-469` — the member-list endpoint and `IMemberQuery` seam for a member picker.
- `src/Infrastructure/Members/MemberQuery.cs:25-77` — role-name projection over `UserRoles`/`Roles`.

Persistence and the invariant:

- `src/Infrastructure/Persistence/AppDbContext.cs:22-41` — DbSets and `OnModelCreating` ordering.
- `src/Infrastructure/Persistence/Configurations/BookingConfiguration.cs:22-64` — enum conversion, 450-char Identity FK, `Restrict`, composite filtered unique index.
- `src/Infrastructure/Persistence/Configurations/ExerciseConfiguration.cs:15-52` — column lengths and `IX_Exercises_Name_Active`.
- `src/Domain/Scheduling/Class.cs:64-99` — the concurrency-stamp contract, documented.
- `src/Application/Scheduling/BookingEndpoints.cs:126,192-268` — `MaxAttempts = 10` and the retry loop.
- `src/Application/Persistence/IUnitOfWork.cs:11-34,44-49,70-77` — `SaveOutcome`, the no-transaction policy, `TrySaveAsync`.
- `src/Infrastructure/Persistence/UnitOfWork.cs:26-61` — SQL 2601/2627 mapping.

Frontend:

- `src/app/src/app/app.routes.ts:19-88` — guard composition, lazy loading, literal-before-`:id` ordering.
- `src/app/src/app/core/auth/roles.ts:13-22` — canonical role literals, already carrying `trainer`.
- `src/app/src/app/core/auth/auth.service.ts:24-29` — `isAdmin` pattern; `isTrainer` is missing.
- `src/app/src/app/core/auth/admin.guard.ts:14-36` — template for a trainer-or-admin guard.
- `src/app/src/app/core/training/exercise.service.ts:27-71` — `HttpClient` + `firstValueFrom` → `Promise<T>`.
- `src/app/src/app/core/training/exercise.models.ts:8-82` — API-mirroring models and the failure union.
- `src/app/src/app/features/admin/exercises/exercise-detail.ts:36-83` — the read-only detail screen S-11 adapts.
- `src/app/src/app/features/admin/exercises/exercise-form.ts:74-244` — reactive form + per-control failure mapping.
- `src/app/src/styles.scss:82-107` — the real design tokens.
- `src/app/angular.json:41-53` — bundle budgets.
- `src/app/package.json:17-33` — no `@angular/cdk`.

Tests:

- `tests/po-prostu-silka.Tests/IntegrationTestFixture.cs:25-172` — Testcontainers MSSQL, migrate-before-host, `TestUsers`, `CreateAuthenticatedClientAsync`.
- `tests/po-prostu-silka.Tests/ExerciseEndpointTests.cs:30-140,396-462` — CRUD + `EveryRoute` 401/403 theory + uniqueness-cycle tests.
- `tests/po-prostu-silka.Tests/BookingEndpointTests.cs:514-537` — `Concurrent_bookings_never_exceed_capacity`, the race-test template.

## Architecture Insights

- **Anemic domain, invariants at the edges.** Entities are POCOs; validation lives in the endpoint file,
  uniqueness in a filtered index, concurrency in a rotated token. The one exception —
  `YouTubeVideoId` — is pure, static and DB-free. A `TrainingPlan` that grows guard clauses and factory
  methods would be the first of its kind in this repo; matching the existing style is the lower-risk
  default, with the "one active plan" rule enforced by index + token rather than by the entity.
- **One file per endpoint surface.** DTOs, route mapping, validation, failure records and the persistence
  interfaces all live in `<Aggregate>Endpoints.cs`. Infrastructure supplies a `Query` (read,
  `AsNoTracking`, projects to DTO) and a `Store` (tracked, `Add`, no `Remove`, never saves).
- **Policies at the group, never per-route** — and pinned by a `TheoryData` test enumerating every route
  against anonymous (401) and wrong-role (403) callers. That test shape should be reproduced for both new
  groups (trainer/admin write surface, member read surface).
- **Soft state over deletion, everywhere.** Deactivate/activate rather than delete; `cancelled` as a
  state; archived plans rather than replaced rows. A plan replacing another should archive it, matching
  FR-016.
- **Three copies of every bound** (EF `HasMaxLength`, endpoint validation constant, Angular
  `Validators.maxLength`) glued only by comments. Missing one is the most repeated cause of a
  deterministic 500 in this repo's review history.
- **Failure shape is uniform**: `record XFailure(string Reason)` → `{"reason":"snake_case"}` with 400 for
  validation and 409 for conflict; the SPA routes each reason to the control that owns it.
- **The frontend is deliberately dependency-light** and hand-rolled — a new UI dependency needs an
  argument, not just a preference.

## Historical Context (from prior changes)

- `context/archive/2026-09-04-exercise-library/plan.md:79-81` — explicitly defers to S-11: "No
  member-facing surface… FR-020 reaches an exercise from within a training plan, which is S-11" and "No
  training plans, no `TrainingPlanExercise`, no assignment — S-11."
- `context/archive/2026-09-04-exercise-library/plan.md:521` — the read-only detail screen is "the layout
  S-11 will later adapt for members."
- `context/archive/2026-09-04-exercise-library/research.md:367-371` — deactivate-not-delete was chosen
  *because* training plans will reference exercises.
- `context/archive/2026-09-04-exercise-library/plan.md:625-632` — the unfiltered fat-DTO exercise list is
  accepted "until a member-facing surface starts reading exercises (S-11) — at which point the split is a
  trimmed list DTO plus a dedicated suggestions endpoint." **This slice is the named trigger.**
- `context/archive/2026-09-04-exercise-library/reviews/impl-review.md:40-84` — F1: pre-check-then-write
  race → always use `TrySaveAsync`/`SaveOutcome`. F2: every string input needs a length guard *before*
  any parsing, even when it is not itself a column.
- `context/archive/2026-09-04-exercise-library/plan.md:460-468,553-555` — the bundle-budget breach and the
  lazy-loading fix, recorded as "Adapted during implementation."
- `context/archive/2026-09-02-trainer-role-and-assignment/plan.md:6-9,74-76` — the Trainer role confers
  nothing; no policy, no screen. Grant/revoke are idempotent and refuse a non-Active target with
  `409 not_active`.
- `context/archive/2026-09-02-trainer-role-and-assignment/reviews/impl-review.md` — F1 CRITICAL: a custom
  widget promised `role="menu"` and never implemented keyboard navigation. F2 CRITICAL: SCSS against
  non-existent CSS variables. F10: role literals extracted to `core/auth/roles.ts`.
- `context/archive/2026-09-03-class-booking-and-cancel/plan.md:120-164,191-330,320-325` — the concurrency
  token mechanism, the retry-bound adaptation from 3 to 10, and the deliberate absence of a collection
  navigation on the aggregate root.
- `context/archive/2026-09-03-class-booking-and-cancel/plan.md:158-164,448-462` — the blocked-member
  cascade, framed as a deliberate exception to the read-time-enforcement convention.
- `context/foundation/lessons.md` — the single standing lesson: any deviation from a plan contract gets an
  "**Adapted during implementation.**" note in `plan.md`, in the same phase, before the phase commit.
- Recurring across the archive: **manual verification criteria left unchecked while phases are stamped
  landed** (booking F5, exercise-library, auth-identity-foundation F7, class-change-notifications F9).

## Related Research

- `context/archive/2026-09-04-exercise-library/research.md` — the upstream Training-context exploration.
- `context/archive/2026-09-03-class-booking-and-cancel/research.md` — hard-invariant and concurrency research.
- These two are the only `research.md` files in the archive; every other change went straight to a plan.

## Open Questions

1. **Plan item storage shape.** A separately mapped `TrainingPlanItem` with an explicit `int Position`
   (closest to existing FK conventions) or an EF owned collection (no precedent in this repo)? — for
   `/10x-plan`. Recommendation from the evidence: the separately mapped entity, since every existing
   relationship is shaped that way and the repo has no owned-type experience.
2. **Decimal precision for weight.** This is the first `decimal` column in the schema. What precision and
   scale, and is `decimal(5,2)` (up to 999.99 kg, 0.01 increments) enough? Also: is weight per plan item a
   single number, or a range/prescription string? — for `/10x-plan`.
3. **Reordering interaction.** Up/down buttons, a hand-rolled pointer drag, or a new `@angular/cdk`
   dependency? Given two prior CRITICAL/WARNING findings about keyboard-inaccessible custom widgets, any
   drag solution needs a keyboard path alongside it. — for `/10x-plan`.
4. **Does the exercise-list endpoint get split now?** `exercise-library` named this slice as the trigger
   for a trimmed list DTO plus a dedicated suggestions endpoint. Deferring is defensible at a single gym's
   scale, but the decision should be recorded rather than skipped.
5. **What does a plan's exercise reference freeze?** If an exercise is deactivated after a plan is
   assigned, does the member still see it in their plan? (`Restrict` keeps the row alive, so the likely
   answer is yes — but the read query's filter must say so deliberately.)
6. **Does assigning or replacing a plan notify the member?** F-03's delivery foundation exists and
   `prd.md` FR-021 lists only account-approved and class-cancelled/changed. Probably out of scope, worth
   one line in "What We're NOT Doing".
7. **Can a trainer edit a plan another trainer authored?** The chosen scope (any active member, no
   ownership relation) implies yes. If the plan records an author for display purposes, that should be
   stated as display-only, not an authorization boundary.
