---
date: 2026-09-03T14:04:29+02:00
researcher: Karol Rumianowski
git_commit: 1656445f9b5258e44346065b190b2ec930cd97ed
branch: main
repository: po-prostu-silka
topic: "Class booking and cancellation (S-08) — codebase research before planning"
tags: [research, codebase, scheduling, booking, concurrency, no-overbooking, ef-core, angular]
status: complete
last_updated: 2026-09-03
last_updated_by: Karol Rumianowski
---

# Research: Class booking and cancellation (S-08)

**Date**: 2026-09-03 14:04 +02:00
**Researcher**: Karol Rumianowski
**Git Commit**: `1656445f9b5258e44346065b190b2ec930cd97ed`
**Branch**: `main`
**Repository**: po-prostu-silka

## Research Question

What in the existing codebase constrains and enables the `class-booking-and-cancel` change
(roadmap S-08: a member books a spot, cancels it, sees their upcoming classes; an admin views a
class's booking list), read against `context/foundation/prd.md` (v1) and
`context/foundation/prd-v2.md` (v2)?

Scope from the roadmap: `context/foundation/roadmap.md` §S-08 — v1 US-01, FR-008, FR-009, FR-010,
FR-014; v2 FR-014. The overriding guarantee: **no overbooking, including under simultaneous
requests**.

## Summary

**This change has been designed for since the very first foundation slice.** It is not greenfield
work dropped into a mature repo — it is the slice that the previous nine deliberately left hooks
for, and the only one entitled (and obliged) to break the convention the codebase has followed so
far: "we accept the read-then-write race, because there is exactly one admin".

Four findings that determine the plan:

1. **`Booking` does not exist in any form** — no entity, no table, no migration, no client code.
   Confirmed by full directory enumeration, not just grep. The whole layer is to be built from
   scratch.

2. **The code names this change explicitly in eight places.** `ClassScheduleQuery.cs:73-77` reads:
   *"S-08 replaces this one expression with `r.Capacity - <booked count>` and nothing else in the
   stack changes"*. `Class.cs:17-19`: *"No booking count lives here. A denormalized counter would
   pre-commit S-08's concurrency design — the load-bearing correctness decision of the milestone —
   and that is not this slice's to make"*. `Program.cs:37-40` states outright what the booking
   transaction must look like. `IntegrationTestFixture.cs:19-21` says the real SQL Server in
   Testcontainers is there for this change's concurrency tests.

3. **The concurrency model is the one genuinely open engineering decision** — and it belongs to
   this change. Every uniqueness/conflict check in this repo today is a knowingly accepted race,
   justified by the sentence "exactly one admin account is ever seeded". That justification
   **does not transfer** to bookings, because members book and there are many of them. There is no
   pattern here to copy — there is a pattern to break.

4. **There is one blocking question, and it is a product decision rather than a technical one**:
   what happens to a blocked member's existing bookings. It was formally reassigned to this change
   during S-02's framing and nobody has answered it since.

## Detailed Findings

### 1. Starting point — what does not exist

- **No `Booking` anywhere in the repo.** `src/Domain/Scheduling/` holds only `Class.cs`,
  `ClassStatus.cs`, `ClassType.cs`, `ClubTime.cs`. `AppDbContext` has four `DbSet`s: `Classes`,
  `ClassTypes`, `OutboxMessages`, `PushSubscriptions`. None of the eight migrations creates a
  bookings table. Independently confirmed in
  `context/archive/2026-09-01-member-management/frame.md:46` (full directory enumeration, not grep).
- **Correction to PRD v2.** `prd-v2.md` §Current System Overview claims members already book and
  cancel spots and already receive notifications. Neither is true, and the roadmap recorded it:
  `context/foundation/roadmap.md` §Baseline, *"Correction recorded 2026-09-02"* — the only
  notification wired to the F-03 delivery foundation is account-approved. **`prd-v2` FR-014 is
  therefore new work in this change, not preserved behaviour.**
- **`ClassStatus.Cancelled` exists as an enum value but no code path ever assigns it**
  (`class-schedule-and-admin/reviews/impl-review.md:22-23`). The transition and its notifications
  are S-09.

### 2. Hooks the codebase left for this change

The complete list of places this change must touch, or that describe its contract:

| File:line | What it says |
| --- | --- |
| `src/Infrastructure/Scheduling/ClassScheduleQuery.cs:73-77` | `FreeSpots = Capacity` "by construction"; S-08 replaces **one expression** with `Capacity - <booked count>` and nothing else in the stack changes |
| `src/Application/Scheduling/ClassEndpoints.cs:729-731` | A second, twin copy of the same expression in `ToDto` — **both must change together** |
| `src/Domain/Scheduling/Class.cs:13-19` | "This is the aggregate S-08 books against"; the absence of a booking counter is deliberate, because it would pre-commit the concurrency design |
| `src/Domain/Scheduling/Class.cs:45-54` | `Capacity` is a **copy**, and it is the value the guarantee is checked against |
| `src/Application/Scheduling/ClassEndpoints.cs:515-521` | `DELETE` always succeeds because nothing is booked — **S-08 adds the guard refusing to delete a class with bookings** |
| `src/Application/Scheduling/ClassEndpoints.cs:137-140` | No cancel endpoint; DELETE is "for a MISTAKE", not for cancelling a class members signed up for |
| `src/Program.cs:32-44` | `EnableRetryOnFailure` is on; the booking transaction **must** go through `CreateExecutionStrategy()` or it throws at runtime |
| `src/app/src/app/core/scheduling/class.models.ts:41-46` | Client: *"Read this, never assume it equals capacity... that slice changes one projection expression on the server and this field starts differing without any change here"* |
| `tests/po-prostu-silka.Tests/IntegrationTestFixture.cs:19-21` | The real SQL Server container is there partly for this change's no-overbooking tests |

### 3. Domain model and persistence

- **`Class` is an anemic entity**: public get/set throughout, no constructor or factory, no
  behaviour methods. Every invariant lives in `ClassEndpoints.cs`, not on the entity
  (`src/Domain/Scheduling/Class.cs:21-124`). A new `Booking` entity should keep the same style —
  departing from it would be a local inconsistency rather than an improvement, unless the plan
  decides otherwise deliberately and says why.
- **Keys**: `Class.Id` and `ClassType.Id` are `Guid`; `ApplicationUser.Id` is Identity's `string`
  (nvarchar(450)). A booking binds a `Guid ClassId` to a `string MemberUserId` — both FK columns,
  both `DeleteBehavior.Restrict` per the convention in `ClassConfiguration.cs:58-76`.
- **Time**: `DateTimeOffset StartsAt`, always UTC, plus `int DurationMinutes` (not `EndsAt`), so
  overlap arithmetic translates to `DATEADD` (`Class.cs:30-42`). `ClubTime` (IANA
  `Europe/Warsaw`) is used **only** for the day arithmetic of weekly duplication.
- **No concurrency token of any kind on the scheduling entities.** The only `IsConcurrencyToken`
  in the repo is Identity's built-in `ConcurrencyStamp` on `ApplicationUser`.
- **Indexes today**: `IX_Classes_Status_StartsAt`, `IX_Classes_StartsAt`,
  `IX_Classes_InstructorUserId`.
- **Filtered-index precedent**: `ClassTypeConfiguration.cs:36-39` uses
  `.IsUnique().HasFilter("[IsActive] = 1")` as the *real* uniqueness backstop — the store-level
  check is only a friendliness pre-check. That is the direct pattern for "one active booking per
  member per class" (a unique index on `(ClassId, MemberUserId)` filtered by booking status).
- **No domain events.** Zero hits for `DomainEvent`, `MediatR`, `INotification`. Cross-context
  reactions go through the outbox: a handler calls `IOutboxEnqueuer.Enqueue(...)` (which
  **does not save**) and one `SaveChangesAsync` closes both
  (`MemberAdminEndpoints.cs:186-197`). The roadmap's "DDD with domain events" intent would mean
  building entirely new infrastructure here — there is nothing to extend.

### 4. Concurrency — the central problem of this change

This is the one area where the repo offers no pattern to copy.

**What the repo does today, and why it is not enough.** `ClassStore.HasTimeConflictAsync`
(`src/Infrastructure/Scheduling/ClassStore.cs:50-58`) says so in its own doc comment:

> "KNOWN LIMITATION - this is a read-then-write race. The caller checks here and writes after, so
> two admins creating overlapping classes at the same instant can both pass. No unique index can
> express interval overlap, and closing it properly would need serializable isolation, which
> EnableRetryOnFailure makes awkward... **Accepted because exactly one admin account is ever
> seeded**, so concurrent admin writes are not a real scenario for this club."

The same justification recurs for `ClassType` (`class-type-definitions/plan.md:173-174`) and for
class `DELETE` (`ClassEndpoints.cs:536-539`). **For bookings it collapses**: members book, there
are dozens of them, and the race for the last spot is exactly that scenario.

**Three independent reviews raised the same class of gap and all three deferred it**:

- `registration-and-approval/reviews/impl-review.md:49-70` (F1) — a double approve sent two
  emails; **fixed** by rotating `ConcurrencyStamp` plus a new `TrySaveChangesAsync` seam.
- `registration-and-approval/reviews/impl-review.md:178-194` (F9) — concurrent registration of the
  same email produced an **unhandled 500** instead of a clean 409; accepted as risk, because
  catching it would need EF Core types in `Application` (forbidden by AGENTS.md).
- `class-type-definitions/reviews/impl-review.md:100-136` (F3) — the unique-index race surfaces as
  a 500 on three write paths. **The critical verification from that review**:
  `TrySaveChangesAsync` catches **only** `DbUpdateConcurrencyException`, and a unique-index
  violation is a `DbUpdateException` — a different type, **which it will not catch**.

**The hard technical constraint.** `EnableRetryOnFailure()` (`Program.cs:44`) makes
`BeginTransaction()` throw `InvalidOperationException` at runtime. Every explicit transaction must
go through `db.Database.CreateExecutionStrategy().ExecuteAsync(...)`. **No code in the repo does
this yet** — this change would be the first consumer. The constraint is documented in five places
(`Program.cs:36-40`, `IUnitOfWork.cs:12-14`, `OutboxEnqueuer.cs:19-22`,
`MemberAdminEndpoints.cs:193-195`, `ClassStore.cs:53-56`).

**Four patterns the repo actually knows** — material for the plan's decision, not a recommendation:

1. **Token rotation plus `TrySaveChangesAsync`** (`MemberAdminEndpoints.cs:173-203`). The
   sequence: check state → mutate → rotate the concurrency token → one `SaveChangesAsync` →
   `false` means somebody got there first. Carrying it to bookings requires **adding a concurrency
   token to `Class`** and rotating it on every booking and cancellation — deliberately serializing
   writes against that class. This is precisely the token S-06 pushed out of its own scope
   (`occurrences-from-class-types/plan-brief.md` §Scope: *"a concurrency token on `Class`"*).
2. **Atomic claim in a single statement** — the outbox does
   `UPDATE ... OUTPUT inserted.* WHERE ...` instead of SELECT-then-UPDATE
   (`OutboxDeliveryWorker.cs:117-173`, `notification-delivery-foundation/plan.md:135-138`). The
   pattern "the condition is part of the write's `WHERE`, not a separate query" is the repo's only
   existing precedent for solving a contended-slot race.
3. **Filtered unique index as the real backstop** (`ClassTypeConfiguration.cs:36-39`) — this solves
   "the same member booked twice", but **not** the capacity limit (`COUNT < Capacity` cannot be
   expressed as an index).
4. **Serializable isolation / UPDLOCK inside an explicit transaction** — used nowhere, and would
   require `CreateExecutionStrategy`. The EF Core docs confirm that is the only correct route with
   retry enabled (see "External research" below).

**Concurrency testing has a precedent.** `MemberAdminEndpointTests.cs:160`
(`Concurrent_approves_still_queue_exactly_one_email`) fires two parallel requests via
`Task.WhenAll`, giving **each caller its own `HttpClient` (own login, own cookie)** — separate DI
scopes and separate `DbContext` instances are what make the race real rather than serialized. That
test was written to **fail before the fix**; the no-overbooking test deserves the same discipline.

### 5. API and authorization conventions

- **Endpoint file shape**: `public static class XEndpoints` with a single
  `MapXEndpoints(this IEndpointRouteBuilder app)`. The policy is applied **to the group, not to
  the endpoint** — the comment at `ClassEndpoints.cs:124-126` says outright this exists so an
  endpoint added later cannot accidentally ship unauthenticated.
- **Two groups in one file** (`ClassEndpoints.cs:193-208`): `/api/classes` under `ActiveMember`,
  `/api/admin/classes` under `Admin`. Bookings will mirror that split.
- **Policy names come from `Domain`** (`src/Domain/AuthorizationPolicyNames.cs`), not from
  `Infrastructure`, so `Application` holds no upward reference. That is the AGENTS.md layering
  rule in practice.
- **The `ActiveMember` policy** (`AuthorizationPolicies.cs:40-43`): authenticated +
  `RequireClaim("account_status", "Active")` + a role from `ApplicationRoles.MemberFacing`.
  **A blocked member gets 403, not 401** — they are authenticated but not authorized.
- **The `account_status` claim can be stale.** It is minted at sign-in and on security-stamp
  refresh, with a **2-minute** validation interval (`Program.cs:131-132`,
  `AppUserClaimsPrincipalFactory.cs:24-32`). `BlockAsync` rotates the `SecurityStamp`, so the
  session does die — but up to two minutes later. That window matters for the blocked-member
  booking decision.
- **Refusal contract**: `record XFailure(string Reason)` returned as
  `Results.Json(new XFailure("reason"), statusCode: N)`. **Not `ProblemDetails`.** 400 for
  validation, **409 for a conflict with existing state** (`time_conflict`, `not_pending`,
  `conflict`). "Class is full" and "already booked" are natural 409s.
- **Validation is hand-rolled** — no FluentValidation, no DataAnnotations
  (`ClassTypeEndpoints.cs:317`: *"there is no validation library here"*). The pattern is a private
  `static IResult? Validate(request)` returning `null` on success.
- **Current user id**: `userManager.GetUserId(principal)` with `ClaimsPrincipal principal` bound
  as a parameter — the precedent is `PushEndpoints.cs:50` and `:79`, the closest existing
  member-initiated write endpoint.
- **There is no "this booking is mine" policy** — the ownership check must be hand-written in the
  handler.
- **Store/Query seam**: `IXQuery` for reads (`AsNoTracking`, projections to DTOs), `IXStore` for
  writes (intention-revealing methods, `Add`/`Remove`/`FindAsync`). **A store never calls
  `SaveChangesAsync`** — the handler does, through an injected `IUnitOfWork`. Interfaces are
  declared at the bottom of the endpoint file, implementations live in `Infrastructure`, and
  registration is `AddScoped` in `Program.cs`. There is **no repository pattern** in this codebase
  (`ClassEndpoints.cs:759-760`).

### 6. Frontend (Angular 22)

- **Stack**: Angular 22.1, `angular-calendar` 0.32.2 plus `angular-draggable-droppable`,
  `angular-resizable-element` and `date-fns` 4. **No UI component library, no Tailwind, no
  NgRx/store.** State is component signals; services are stateless HTTP wrappers returning
  `Promise` via `firstValueFrom`.
- **The `ScheduledClass` model** (`core/scheduling/class.models.ts:5-51`) already separates
  `freeSpots` from `capacity`, with a comment warning against assuming they are equal. **The
  contract is ready — the client needs no model change to start showing real free spots.**
- **Free-spot display already exists**: `shared/calendar/schedule-calendar.html:151-157` renders
  `{{ freeSpots }} / {{ capacity }} wolnych`, or `Brak miejsc`.
- **A real obstacle for the plan to decide**: the class tile renders actions only under
  `@if (!readOnly() && classActions())` (`schedule-calendar.html:159`). The member schedule uses
  the default `readOnly = true` (it does not pass the input at all — confirmed by grep over
  `schedule.html`), while the admin passes `[readOnly]="isPast()"` (`classes.html:20`).
  **`readOnly` currently conflates two things: "no drag/resize gestures" and "no actions".** A
  "Book" button on the member schedule requires separating those concepts — either by loosening
  the condition or by introducing a separate input. This is not an implementation detail; it is a
  contract change to a component shared by two screens.
- **`shared/week-navigator/` is an empty directory** — its role was absorbed by
  `shared/calendar/calendar-week-strip.ts`. Do not create anything there.
- **Refusal-message table**: `core/scheduling/class-failure.ts` — a `Record` keyed by the reason
  union itself, so adding a reason without a message **breaks the build**. The doc comment in that
  file explains why it is one shared table rather than two `switch` statements. An analogous
  `booking-failure.ts` is the obvious move.
- **Routing** (`app.routes.ts`): guards composed as an array — `authGuard`, `activeMemberGuard`,
  `adminGuard`. A new "my classes" route sits next to `/schedule` with
  `[authGuard, activeMemberGuard]`. **Note**: the nav today (`app.html:1-27`) carries no
  member-facing link at all — not even to `/schedule`; the only link is the admin's "Zgłoszenia".
- **Admin list patterns** (`features/admin/classes/classes.ts`, `members.ts`): a
  loading/error/empty tri-state, a `ReadonlySet<string>` of in-flight rows plus an `isBusy(id)`
  helper, and a `generation` counter guarding against out-of-order responses.
- **Testing**: Vitest via the `@angular/build:unit-test` builder, `TestBed` plus
  `provideHttpClientTesting`, `HttpTestingController` with `controller.verify()` in `afterEach`.
  Components with content projection are tested through a local host component.

### 7. Backend testing

- One SQL Server container (Testcontainers, `mssql/server:2022-latest` — the same tag as
  `docker-compose.yml`) and one app host for the whole run; xUnit collection
  `IntegrationCollection`.
- Migrations run **before** the host is built (the seeder touches `AspNetRoles`).
- Four accounts seeded once: active admin, active member, pending, blocked
  (`IntegrationTestFixture.cs:86-89`). Ad-hoc accounts via `CreateUserAsync(...)`.
- Authenticating in a test:
  `await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveMemberEmail)` — a real
  `POST /api/auth/login`, a real cookie.
- Naming: `A_sentence_describing_the_contract`, e.g.
  `Create_copies_the_capacity_from_the_request_not_the_type`.
- Database assertions through a fresh `AppDbContext` (`NewContext()`) with `AsNoTracking()`.

### 8. Prior decisions that bind this change

- **The slice numbering changed.** Older plans call this change **S-04**; newer ones call it
  **S-08**. Every "S-04's no-overbooking guarantee" sentence in `context/archive/**` refers to
  this change.
- **Why `Capacity` is a copy** (`class-type-definitions/plan.md:150-154`): the `DefaultCapacity`
  name on the type is a deliberate guardrail — capacity resolved by reference would let a type
  edit change the capacity of a class that **already has bookings**, moving the very value the
  guarantee is checked against. **The capacity check reads `Class.Capacity`, never
  `Class.ClassType.DefaultCapacity`.**
- **The `Class.ClassType` and `Class.Instructor` navigations are "READ SIDE ONLY"** by convention
  and tests alone, not by the compiler (`Class.cs:74-123`,
  `occurrences-from-class-types/plan.md:126-131`). A `Class.Bookings` navigation would need the
  same discipline — documented and pinned by a test.
- **`FreeSpots` was designed so this change moves one expression**
  (`class-schedule-and-admin/plan.md:222-224`): *"so that slice changes one expression and no DTO,
  template or spec"*. **No escape hatch to a denormalized counter is recorded anywhere** — the
  whole archive treats the live projection as the design bet.
- **The pattern for routing a refusal to a specific control**
  (`occurrences-from-class-types/plan.md:432-433`): `time_conflict` maps onto the `startsAt`
  control. When no control can carry the message, it becomes a form-level banner
  (`schedule-calendar-view/plan.md:734-739`).
- **A read-path refusal gets its own union** (`ScheduleReadFailure` for `invalid_range`,
  `schedule-calendar-view/plan.md:273-279`), so `ClassFailure` mirrors the write contract field
  for field. Booking reasons are write-path reasons, so they belong in their own `BookingFailure`.
- **`InstructorUserId` reaches members** — accepted deliberately
  (`occurrences-from-class-types/reviews/impl-review.md:115-123`, F8) with a **named revisit
  condition: "a member-facing endpoint that accepts a user id"**. This change introduces exactly
  such an endpoint, so the condition has been met and planning should re-examine it.
- **Migration conventions**: names generated by `dotnet ef migrations add <Name>` from `src/`,
  with the command recorded verbatim in the plan; `Down` **must** work and must be verified by
  actually reversing before the phase commit (`occurrences-from-class-types/plan.md:352`).
  Exceptions to the "destructive changes lag one release" rule are permitted but require the cost
  written down and the product owner's decision.
- **An anti-pattern confirmed by review, to avoid**: a "Current State Analysis" section built on
  an unverified assumption. `class-type-definitions/reviews/impl-review.md:41-77` (F1): the claim
  "there is no backend test project" was **the stated premise** of the decision to ship without
  server-side tests, and it was false. Every current-state claim in this change's plan must be
  checked against the filesystem.

## External Research (Context7, EF Core)

Source: `/dotnet/entityframework.docs`, pages `core/saving/concurrency.md`,
`core/saving/transactions.md`, `core/miscellaneous/connection-resiliency.md`.

- **`rowversion` as a concurrency token**:
  `modelBuilder.Entity<T>().Property(p => p.Version).IsRowVersion()` (or `[Timestamp]`). The
  database increments it on every `UPDATE`; EF appends the token to the `WHERE` clause, and zero
  matched rows raises `DbUpdateConcurrencyException`. It is the "minimum-effort token that protects
  the entire row" — a stronger equivalent of the manual `ConcurrencyStamp` rotation the repo
  already applies to `ApplicationUser`.
- **Resolving a conflict**: either tell the user, or re-query and retry in a loop. The repo has
  only the first variant today (`TrySaveChangesAsync` catches once and returns `false`, **it does
  not retry**). Booking the last spot under load is the scenario where a retry loop is the
  difference between "refused although a spot was free" and a correct outcome — a decision for the
  plan.
- **Explicit transactions with `EnableRetryOnFailure`** — confirmed directly:
  `BeginTransactionAsync` throws `InvalidOperationException` ("The configured execution strategy
  'SqlServerRetryingExecutionStrategy' does not support user-initiated transactions"); the fix is
  `var strategy = db.Database.CreateExecutionStrategy(); await strategy.ExecuteAsync(async () => { ... });`.
  The delegate is **replayed in full** on a transient failure, so it must be idempotent.
- Side note: since EF Core 9, `Migrate()`/`MigrateAsync()` manages its own transaction and throws
  if wrapped in an explicit one — relevant because this repo's migrations are run by the test
  fixture.

## Code References

- `src/Domain/Scheduling/Class.cs:13-19` — the aggregate this change books against; the deliberate absence of a counter
- `src/Domain/Scheduling/Class.cs:45-54` — `Capacity` as a copy; the value the guarantee checks
- `src/Domain/Scheduling/ClassStatus.cs:18-25` — `Scheduled`/`Cancelled`, int values pinned
- `src/Infrastructure/Persistence/Configurations/ClassConfiguration.cs:31-76` — indexes, `Restrict`, no token
- `src/Infrastructure/Persistence/Configurations/ClassTypeConfiguration.cs:36-39` — the filtered unique-index pattern
- `src/Infrastructure/Scheduling/ClassStore.cs:50-106` — the documented read-then-write race plus the `db.Classes.Local` half
- `src/Infrastructure/Scheduling/ClassScheduleQuery.cs:73-77` — the `FreeSpots` expression to replace
- `src/Application/Scheduling/ClassEndpoints.cs:124-140` — the group/policy rule and the note on the missing cancel
- `src/Application/Scheduling/ClassEndpoints.cs:191-208` — endpoint group registration
- `src/Application/Scheduling/ClassEndpoints.cs:515-543` — the `DELETE` that needs a has-bookings guard
- `src/Application/Scheduling/ClassEndpoints.cs:729-731` — the twin `FreeSpots` copy in `ToDto`
- `src/Application/Members/MemberAdminEndpoints.cs:76-80` — the blocked-member question reassigned to this change
- `src/Application/Members/MemberAdminEndpoints.cs:173-208` — the pattern: idempotency → token rotation → `TrySaveChangesAsync`
- `src/Application/Members/MemberAdminEndpoints.cs:227-272` — the full `BlockAsync` path; no cascade of any kind
- `src/Application/Persistence/IUnitOfWork.cs:12-30` — the commit contract and the limits of `TrySaveChangesAsync`
- `src/Application/Notifications/OutboxEnqueuer.cs:14-24` — enqueue without saving, atomic with the domain change
- `src/Infrastructure/Authorization/AuthorizationPolicies.cs:40-47` — the `ActiveMember` and `Admin` definitions
- `src/Infrastructure/Identity/AppUserClaimsPrincipalFactory.cs:24-32` — where the `account_status` claim comes from
- `src/Program.cs:32-44` — `EnableRetryOnFailure` and the instruction for the booking transaction
- `src/Program.cs:235-240` — where `app.MapBookingEndpoints()` goes
- `src/app/src/app/core/scheduling/class.models.ts:41-46` — the client-side `freeSpots` contract
- `src/app/src/app/core/scheduling/class-failure.ts` — the exhaustive message-table pattern
- `src/app/src/app/shared/calendar/schedule-calendar.html:151-166` — free-spot rendering and the `readOnly` gate on actions
- `src/app/src/app/shared/calendar/schedule-calendar.ts:155,186` — the `readOnly` input, `contentChild('classActions')`
- `src/app/src/app/app.routes.ts` — guard composition; where a "my classes" route goes
- `tests/po-prostu-silka.Tests/IntegrationTestFixture.cs:19-21,84-138` — why the real SQL Server exists; seeding and login
- `tests/po-prostu-silka.Tests/MemberAdminEndpointTests.cs:160` — the only existing two-parallel-requests test

## Architecture Insights

- **Layering is held by convention, not by the compiler.** One project, three folders.
  `Application` **may not** see EF Core — which is why `IUnitOfWork` exists at all, and why
  `TrySaveChangesAsync` translates `DbUpdateConcurrencyException` into a `bool`. If the booking
  plan needs to catch `DbUpdateException` (a unique-index violation), it **needs a new seam on
  `IUnitOfWork`, or handling inside `Infrastructure`** — not a `catch` in the endpoint. That is
  precisely the wall F9 hit in the registration review.
- **The repo has neither a repository pattern nor domain events.** The convention is one
  `IXQuery`/`IXStore` pair per aggregate with intention-revealing methods. `Booking` should get
  such a pair, nothing generic.
- **API contracts mirror the SPA field for field** — there is no versioning, because the only
  consumer is the product's own client. A contract change is a change on both sides in one
  release.
- **This change is the first place where concurrency risk is real rather than acceptable.** Every
  earlier "accepted as risk" rested on there being one admin account. This plan should name that
  explicitly and decide a mechanism rather than inherit the posture.

## Historical Context (from prior changes)

- `context/archive/2026-08-31-persistence-foundation/plan.md:329-331` — a real SQL Server locally **because** the no-overbooking guarantee depends on locking semantics
- `context/archive/2026-08-31-persistence-foundation/plan.md:517-519` — the warning that this change needs genuine concurrency tests and must budget for them rather than discover them mid-phase
- `context/archive/2026-08-31-auth-identity-foundation/plan.md:249-252` — `EnableRetryOnFailure` added with a note that the booking transaction will need `CreateExecutionStrategy()`
- `context/archive/2026-08-31-notification-delivery-foundation/plan.md:128-138` — the same constraint plus the single-statement atomic-claim pattern
- `context/archive/2026-09-01-class-schedule-and-admin/plan.md:102-109` — the overlap race accepted knowingly, on the "one admin" reasoning
- `context/archive/2026-09-01-class-schedule-and-admin/plan.md:222-224,304-307` — `FreeSpots` as one expression to replace; `DELETE` needing a guard
- `context/archive/2026-09-01-class-schedule-and-admin/reviews/impl-review.md:109-145` — F3: no concurrency token on `Class`, fixed by comment, the gap left open
- `context/archive/2026-09-01-member-management/frame.md:70-87` — the convention that "access consequences are enforced at read time by policy claims, never by stored cascade state"; the blocked-member question reassigned to this change
- `context/archive/2026-09-02-class-type-definitions/plan.md:150-154` — why capacity is copied
- `context/archive/2026-09-02-class-type-definitions/reviews/impl-review.md:100-136` — F3: `TrySaveChangesAsync` **does not catch** `DbUpdateException`
- `context/archive/2026-09-02-occurrences-from-class-types/plan-brief.md` §Scope — "a concurrency token on `Class`" explicitly out of scope for S-06
- `context/archive/2026-09-02-schedule-calendar-view/plan.md:107-109` — the `Status == Scheduled` filter on the member path and its deliberate absence on the admin path; both behaviours must survive

## Related Research

There is no earlier `research.md` in this repo — no archived change contains that artifact (they
hold `plan.md`, `plan-brief.md`, `frame.md`, `reviews/`). This is the project's first research
document.

## Open Questions

1. **BLOCKING — what happens to a blocked member's existing bookings?** Owner: user. This is PRD
   Open Question 1 (`prd.md:181`, `prd-v2.md` OQ5), formally reassigned to this change during
   S-02's framing (`context/archive/2026-09-01-member-management/frame.md:86-87`) and recorded in
   the roadmap as `Block: yes`. The options: cascade-cancel on block (frees the spot, but extends
   `BlockAsync` into the scheduling context), or leave the bookings standing and refuse access
   prospectively through the claim (consistent with the repo's recorded convention, but the spot
   stays held by someone who will not turn up). The repo's convention points at the second; the
   product may want the first. **This cannot be settled from the code — it is a product decision.**

2. **Which mechanism guarantees no overbooking?** Owner: the plan. Four candidates are described in
   §4. The choice determines the migration (a token on `Class`? a filtered index on the booking?
   both?), the handler's shape (one `SaveChanges` or `CreateExecutionStrategy`?), and whether a
   retry loop is needed. The roadmap calls this "the load-bearing correctness work of the
   milestone".

3. **Is a cancelled booking a row with a status, or a deleted row?** v1 FR-009 says *"the cancelled
   booking stays in history"*, so a status. The consequence: counting taken spots must filter by
   status, and the unique index on `(ClassId, MemberUserId)` must be filtered (like
   `IX_ClassTypes_Name_Active`), or re-booking after a cancellation will be rejected.

4. **How should `readOnly` be separated from action visibility on the calendar tile?**
   (`schedule-calendar.html:159`). A contract change to a component shared by the member schedule
   and the admin panel — it touches both screens.

5. **Is `InstructorUserId` in the member contract still acceptable?** The revisit condition named
   in F8 of the S-06 review has now been met.

6. **Free-spot projection performance.** `GET /api/classes` answers a window of up to 8 weeks.
   Counting bookings per class must stay one query with a `GROUP BY`/subquery, not N+1 — the
   "~1 s perceived response" NFR is in the PRD, and Azure SQL Basic is 5 DTU.

7. **May an admin cancel someone else's booking?** Roadmap S-08 says only "admin sees bookings",
   and v1 FR-014 says only "view". The default is no — but worth recording as a Non-Goal so it
   does not come back.

8. **There is no member-facing nav link.** Today neither `/schedule` nor a future "my classes" has
   an entry in `app.html`. Does this change add member navigation, or is that a separate concern?
