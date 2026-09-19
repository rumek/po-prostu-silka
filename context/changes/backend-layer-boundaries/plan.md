# Compiler-enforced layer boundaries and thin endpoint classes — Implementation Plan

## Overview

Split `src/` from one `Microsoft.NET.Sdk.Web` project into four — `Domain`, `Application`,
`Infrastructure`, `Api` — so the layering table in `AGENTS.md` becomes a build constraint rather than
a convention, and reduce each of the thirteen `*Endpoints` classes to route registration by moving
handlers, validation, mapping, ports and resource-level authorization into `Application`, one file per
handler body.

No behaviour changes (CS-03): no route moves, no `reason` code added or renamed, no schema change.

## Current State Analysis

`src/po-prostu-silka.csproj` is a single Web SDK project whose `Domain`, `Application` and
`Infrastructure` folders carry the intended layering by convention alone. The dependency graph is
already acyclic and already correct — verified at `e925de8`: no `using po_prostu_silka.Infrastructure`
in `Application`/`Domain`, no `using po_prostu_silka.Application` in `Domain`, no EF Core or
`Microsoft.Data.SqlClient` outside `Infrastructure`, no `InternalsVisibleTo`. Nothing checks any of it.

Every file already declares `po_prostu_silka.Domain.*` / `.Application.*` / `.Infrastructure.*`. With a
matching `RootNamespace` per project and the folder structure preserved, **no `using` and no namespace
declaration changes anywhere** — not in `src/`, not in the 47 `using` statements across 23 test files.

The thirteen `*Endpoints` files hold 6,276 lines, 36 routes and 65 handler-shaped methods, each file
bundling seven concerns: DTO contracts, `I*Store`/`I*Query` port definitions, hand-rolled validation,
mapping, business rules, resource-level authorization, and limit constants.

### Key Discoveries

- **The implicit-usings trap is the highest-probability phase-1 stall.** The Web SDK contributes nine
  global usings a plain `Microsoft.NET.Sdk` class library does not get. 14 Application files need
  `Microsoft.AspNetCore.Http`, 13 need `.Routing`, 13 need `.Builder`; 5 Infrastructure files need
  `Microsoft.Extensions.Logging`, 2 need `.DependencyInjection`, 2 need `.Configuration`, 1 needs
  `.Hosting`. `Domain` needs none — it is a clean class library. (research §B)
- **Ports must be extracted before handlers.** Nine ports declared inside one context's `*Endpoints`
  file are consumed from another context's file. Splitting handlers first drags a whole compilation
  unit across contexts for one interface. (research §D)
- **`AddHostedService<OutboxDeliveryWorker>()` must stay generic-typed.**
  `IntegrationTestFixture.cs:416-423` removes the worker by matching
  `ImplementationType == typeof(OutboxDeliveryWorker)` with `.Single(...)`. Registering it via a
  factory delegate makes `ImplementationType` null, `Single` throws, and the test host fails to start.
  (research §E.3)
- **`/api/admin/classes` is registered twice under two policies** — `ClassEndpoints.cs:224` (`Admin`)
  and `BookingEndpoints.cs:232` (`TrainerOrAdmin`). These groups must not be merged or reordered.
- **`TrainingPlanEndpoints` requires `/members` (`:257`) before `/{id:guid}` (`:259`)**, documented at
  `:253-256`.
- **`BookingEndpoints`' inline `MayActOn` ownership check is enforced only by a comment**
  (`:217-231`), already ~350 lines from the check itself (`:590-608`).
- **`EndpointAuthorizationTests.cs:12-17` names S-18 in its own doc comment** as the reason it exists.
  It reads endpoint metadata at runtime rather than reflecting over assemblies, and is a strictly
  better gate than the route-literal grep the change brief proposed.
- **Migrations are a confirmed non-issue.** `Program.cs:45-48` passes no `MigrationsAssembly`, which is
  exactly why `AppDbContext` and `Persistence/Migrations/` must land in the same assembly. Migration
  files already declare `namespace po_prostu_silka.Infrastructure.Persistence.Migrations`. No runtime
  `Migrate()`/`MigrateAsync()` exists in `src/`. (research §H)
- **Eleven hard breaks outside `src/*.cs`**, five of them in `.github/workflows/deploy.yml` — the only
  place this slice can break production. (research §G)

## Desired End State

`src/` holds four `.csproj` files plus `src/app/`. Adding `using Microsoft.EntityFrameworkCore;` to any
file under `src/Application/` or `src/Domain/` fails `dotnet build` with CS0234. Each `*Endpoints` class
under `src/Api/` contains a `Map*Endpoints` method and nothing else; every handler body lives in its own
file under `src/Application/<Context>/`. `dotnet test po-prostu-silka.slnx` is green with **no test file
edited**, and `dotnet ef migrations script --idempotent` produces a byte-identical script to the one
captured before the split.

Verify by: the CS0234 probe above (added, built, reverted — never committed), a green
`dotnet test --filter "FullyQualifiedName~EndpointAuthorizationTests"`, and the migration-script diff.

## What We're NOT Doing

- MediatR, FluentValidation, AutoMapper, a repository pattern.
- Replacing `IResult` with a result union — the `{ reason }` vocabulary is mirrored field-for-field by
  the SPA's discriminated unions and pinned by tests on both sides.
- `AddProblemDetails` or exception-handler middleware — it changes the error shape the SPA parses.
- Enriching `Domain` or moving invariants into entities.
- Moving `ApplicationUser` out of `Domain`. It gains `Microsoft.Extensions.Identity.Stores` instead;
  `IdentityUser` is not EF Core, and `context/archive/2026-08-31-auth-identity-foundation/plan.md:201-204`
  carries an explicit "do not fix this by moving the type".
- **Merging the duplicated class limits.** `ClassEndpoints.cs:198-207` documents the duplication as
  deliberate — an occurrence may override its type's defaults (prd-v2 FR-008), so the two values are
  independent after creation. Both sets stay where they are. Same for the `MaxNameLength` /
  `MaxDescriptionLength` and `MaxAttempts = 10` pairs: parallel derivation, not shared rules.
- An architecture test (`NetArchTest`). After the split the compiler refuses the violation.
- Renaming a route, an assembly, or a namespace; squashing or regenerating migrations.
- Central Package Management — the natural follow-up once four package lists exist.
- Extracting `AddInfrastructure()` / `AddApplication()` DI extension methods. `Program.cs` gets new
  `using`s and nothing else; the registration block is load-bearing for the test host (phase 7 §4).
- Anything under `src/app/` beyond the two lines that name the backend project path.

## Implementation Approach

Seven phases. Phase 1 proves CS-01 and is a pure `git mv` plus four `.csproj` files — no `.cs` content
changes at all. Phase 2 extracts the ports and shared contracts, which every later phase depends on.
Phases 3–6 split handlers out of the endpoint files, pilot first, then by bounded context. Phase 7 moves
the now-thin registration classes into `Api` and shrinks `Application`'s implicit-using set.

Every phase uses `git mv` so rename detection keeps `git log --follow` and `git blame` intact. The
codebase is 55–70% comments, so move phases produce diffs that look enormous while changing nothing; the
only workable review question is **"did any non-comment line change?"**.

### Decisions settled during planning

| Decision | Choice |
| --- | --- |
| The two `src/app/` lines that name the backend path | S-18 takes them, in phase 1 |
| Scope | Full CS-01 + CS-02 — all thirteen files |
| Logger categories | Pinned: keep `typeof(AuthEndpoints)` / `typeof(ProfileEndpoints)` with a comment |
| Dead injected parameters | Dropped, in the phase that moves their handler |
| Route registration | `*Endpoints` → `Api`; handlers → `Application` |
| Port file layout | One file per interface, in the context folder under `Application` |
| Shared MSBuild properties | `src/Directory.Build.props`; `<Using>` items declared per project |
| Use-case file boundary | The handler body — one method, one file, regardless of route count |
| DTOs | Stay in `Application`, all 49 extracted in phase 2 — handlers return `Results.Json(new XFailure(...))`, and 13 records are consumed from `Infrastructure` |
| `Api` assembly name | Pinned to `po-prostu-silka` |

## Critical Implementation Details

**`<AssemblyName>po-prostu-silka</AssemblyName>` on the `Api` project is load-bearing twice.**
`Microsoft.AspNetCore.Mvc.Testing` emits a `WebApplicationFactoryContentRootAttribute` into the test
assembly keyed by the entry-point assembly name, and a stale `obj/` holding the old key yields a
confusing "could not find a part of the path …\src". Independently, `dotnet publish` currently produces
`po-prostu-silka.dll`, which is what App Service starts — holding the name constant keeps the deploy
artifact identical. Set it rather than relying on a clean build.

**`Microsoft.EntityFrameworkCore.Design` goes in TWO projects** — `Infrastructure` (migrations output)
and `Api` (`dotnet ef` startup project) — each with `PrivateAssets="all"`, which does not flow
transitively. Omitting it from `Api` produces "your startup project doesn't reference
Microsoft.EntityFrameworkCore.Design"; expect that error exactly once if it is missed.

**`Api` needs project references to both `Application` and `Infrastructure`**, not just
`Infrastructure`: all thirteen `Map*Endpoints()` live in `Application` until phase 7, and the
`Testing`-only probes at `Program.cs:399`/`:402` use `AuthorizationPolicies` from `Infrastructure`.

**`src/wwwroot/` exists on disk right now** — 103 untracked files from a local `e2e:stage`, 0 tracked
(`git ls-files src/wwwroot` → empty). After the move it is an orphan that must be deleted by hand or a
stale bundle lingers forever. `.gitignore:41` is `wwwroot/`, path-agnostic, and already covers
`src/Api/wwwroot/` — no `.gitignore` edit is needed.

**31 archived files would become stale.** They are immutable by rule and must not be edited;
`context/foundation/lessons.md` already codifies the mitigation.

---

## Phase 1: Four projects

### Overview

Split the single `.csproj` into four, move every file with `git mv`, and fix all eleven hard breaks
outside `src/*.cs` in the same commit. No `.cs` file content changes in this phase.

### Changes Required:

#### 1. Shared MSBuild properties

**File**: `src/Directory.Build.props` (new)

**Intent**: Hold `TargetFramework`, `Nullable` and `ImplicitUsings` once for the four new projects
instead of four times, so the next TFM bump touches one file. `tests/po-prostu-silka.Tests.csproj` sits
outside `src/` and deliberately keeps its own copy — pulling the test project under a shared props file
is a separate decision this slice does not make.

**Contract**: `net10.0`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`. No
`<Using>` items here — those are per project, because `Domain` must not silently inherit ASP.NET
namespaces it does not need. Do not add `TreatWarningsAsErrors`: the repo is warning-clean today by a
manual bar, and changing the bar is not this slice's business.

#### 2. The four project files

**File**: `src/Domain/po-prostu-silka.Domain.csproj` (new)

**Intent**: The innermost layer as a plain class library.

**Contract**: `Microsoft.NET.Sdk`; `RootNamespace=po_prostu_silka.Domain`; no `ProjectReference`; one
`PackageReference` — `Microsoft.Extensions.Identity.Stores` (where `IdentityUser` lives; it is not EF
Core). No `<Using>` items — `Domain` needs none of the Web SDK's nine.

**File**: `src/Application/po-prostu-silka.Application.csproj` (new)

**Intent**: The use-case layer, referencing `Domain` only.

**Contract**: `Microsoft.NET.Sdk`; `RootNamespace=po_prostu_silka.Application`; `ProjectReference` →
`Domain`; `<FrameworkReference Include="Microsoft.AspNetCore.App" />`. **No `PackageReference` at all.**
The FrameworkReference is what supplies every non-BCL type `Application` uses: `UserManager` (×28) and
`SignInManager` (×5) — both ship in the ASP.NET Core shared framework, so they do *not* depend on
`Domain`'s `Identity.Stores` package — plus `Microsoft.AspNetCore.Mvc` (`[FromBody]`, 5 files) and the
`Microsoft.AspNetCore.Http` / `.Routing` / `.Builder` types below. In particular
`Microsoft.AspNetCore.Identity.EntityFrameworkCore` must **not** appear here: it is the EF Core-backed
Identity store and belongs to `Infrastructure`.

`<Using>` items, each with a stated reason (research §B counts): `Microsoft.AspNetCore.Http` (14 files —
`IResult`, `Results`, `HttpContext`), `Microsoft.AspNetCore.Routing` (13 — `IEndpointRouteBuilder`),
`Microsoft.AspNetCore.Builder` (13 — `MapGet`, `RequireAuthorization`), `Microsoft.Extensions.Logging`
(3). The `Routing` and `Builder` items are temporary and get removed in phase 7; say so in the comment.

⚠️ `context/archive/2026-09-01-registration-and-approval/reviews/impl-review.md:90-113` records an
out-of-plan `.csproj` commit whose justification was disproved and dropped from history. Every item here
carries its reason inline.

**File**: `src/Infrastructure/po-prostu-silka.Infrastructure.csproj` (new)

**Intent**: The only project that may see EF Core.

**Contract**: `Microsoft.NET.Sdk`; `RootNamespace=po_prostu_silka.Infrastructure`; `ProjectReference` →
`Domain`, `Application`; `FrameworkReference` → `Microsoft.AspNetCore.App`. Packages moved verbatim from
today's list: `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Design`
(`PrivateAssets="all"`), `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`,
`Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Azure.Communication.Email`,
`Lib.Net.Http.WebPush` — keep each existing comment with its package.

`<Using>` items: `Microsoft.Extensions.Logging` (5 files), `Microsoft.Extensions.DependencyInjection`
(2), `Microsoft.Extensions.Configuration` (2), `Microsoft.Extensions.Hosting` (1 — `BackgroundService`).

**File**: `src/Api/po-prostu-silka.Api.csproj` (new)

**Intent**: The host. The only Web SDK project.

**Contract**: `Microsoft.NET.Sdk.Web`; `RootNamespace=po_prostu_silka`;
**`<AssemblyName>po-prostu-silka</AssemblyName>`** with a comment naming both reasons (test content-root
key, publish artifact name); `ProjectReference` → `Application`, `Infrastructure`. Packages:
`Microsoft.AspNetCore.OpenApi`, and `Microsoft.EntityFrameworkCore.Design` with `PrivateAssets="all"` —
the second copy, needed because `Api` is the `dotnet ef` startup project. No `<Using>` items: the Web
SDK supplies them.

The `<Compile/Content/EmbeddedResource/None Remove="app\**" />` block at
`src/po-prostu-silka.csproj:10-15` is **not** carried over — `src/app/` is no longer inside any project
directory. Delete it with the old file.

#### 3. File moves

**Files**: `src/Domain/**`, `src/Application/**`, `src/Infrastructure/**` stay at their paths (the new
`.csproj` sits beside them); `src/Program.cs`, `src/appsettings.json`,
`src/appsettings.Development.json`, `src/Properties/launchSettings.json`, `src/po-prostu-silka.http` →
`src/Api/`.

**Intent**: Give the host its own directory so no project's directory contains another's.

**Contract**: `git mv` for every file. `src/Application/README.md` stays put and is edited in change #7.
Delete `src/po-prostu-silka.csproj`. Delete the untracked `src/wwwroot/` by hand. Not one namespace
declaration or `using` changes in this phase — that is the phase gate.

#### 4. Solution and test wiring

**File**: `po-prostu-silka.slnx`

**Intent**: List the four projects so `dotnet test po-prostu-silka.slnx` (deploy.yml:67) keeps working
untouched.

**Contract**: Replace the single `<Project Path="src/po-prostu-silka.csproj" />` inside the existing
`/src/` folder with four entries. The `/tests/` folder is unchanged.

**File**: `tests/po-prostu-silka.Tests/po-prostu-silka.Tests.csproj:25`

**Intent**: Point the test project at the host.

**Contract**: `ProjectReference` → `..\..\src\Api\po-prostu-silka.Api.csproj`. One reference is enough —
`Application`, `Infrastructure` and `Domain` flow transitively. Clean the test project's `obj/` and
`bin/` before the first build regardless of the pinned `AssemblyName`.

#### 5. Deploy pipeline — the only production risk

**File**: `.github/workflows/deploy.yml`

**Intent**: Update the five lines that name paths which cease to exist. Same commit as the split.

**Contract**: `:54` `mkdir -p src/Api/wwwroot`; `:55` `cp -r src/app/dist/app/browser/. src/Api/wwwroot/`;
`:70` `dotnet publish src/Api/po-prostu-silka.Api.csproj`; `:85` and `:123` the `dotnet ef` invocations
become `--project src/Infrastructure/po-prostu-silka.Infrastructure.csproj --startup-project src/Api/po-prostu-silka.Api.csproj`.
`:67` (`dotnet test po-prostu-silka.slnx`) is solution-level and needs no edit. The `env:` block
(`:9-11`) holds Azure resource names, **not** project names — do not touch it.

#### 6. The two `src/app/` lines

**File**: `src/app/package.json:15`

**Intent**: Stage the SPA build into the host project's `wwwroot`, not an orphaned directory.

**Contract**: the `e2e:stage` `cpSync` target becomes `../Api/wwwroot`. This failure is silent —
`e2e:stage` keeps succeeding, `dotnet run` serves an empty `wwwroot`, every route 404s with no error.

**File**: `src/app/playwright.config.ts:38`

**Intent**: Boot the host that still exists.

**Contract**: `dotnet run --project ../Api/po-prostu-silka.Api.csproj --launch-profile http`. The
launch profile name is unchanged — `launchSettings.json` moves with the host.

#### 7. Documentation

**Files**: `AGENTS.md:15,21,25-27,29,31,35,43`; `CLAUDE.md:6,9-12,16-18`; `README.md:54-61,78,80,87,91`;
`src/Application/README.md:10,18-19`.

**Intent**: Every stated path and command becomes correct. In an agent-driven repo a stale command is a
defect, because the next agent will run it.

**Contract**: The layering table loses "convention — not compiler-enforced" and gains the four project
names. Record the consequence of keeping `ApplicationUser` in `Domain`: **"Domain references nothing"
stops being true** — it references `Microsoft.Extensions.Identity.Stores`, and the rule the build
enforces is about EF Core specifically. Build/run/ef commands updated to the new project paths.
`AGENTS.md:57` is already stale (it claims no CI workflow exists) — fix it while there.

Foundation docs stating the old paths (`context/foundation/prd-v2.md:23-25`,
`roadmap.md:179,184-185`, `test-plan.md:268-269`, `context/deployment/deploy-plan.md:26,29,31,56`) are
updated in phase 7, once the final shape is real. **`context/archive/` is never edited.**

### Success Criteria:

#### Automated Verification:

- Capture BOTH baselines BEFORE any move, into the same scratch directory: (a) `dotnet ef migrations script --idempotent --project src/po-prostu-silka.csproj -o <scratch>/before.sql` plus `dotnet ef migrations list --project src/po-prostu-silka.csproj --no-build --no-connect` (22 migrations); (b) the route literals — `grep -rho '"/api/[^"]*"' src --include=*.cs | sort > <scratch>/routes-before.txt`. **Adapted during implementation.** This captures 18 lines / 16 unique values, not 36: the grep sees `MapGroup` prefixes, while the individual routes are relative (`"/"`, `"/{id:guid}"`). It remains a valid regression gate — a group prefix that moves or disappears shows up — but it is a group-level check, not a per-route one; per-route coverage is `EndpointAuthorizationTests`. Every later phase diffs against these two files, never against a freshly taken "pre-phase" capture
- Build is clean: `dotnet build po-prostu-silka.slnx -c Release` → 0 warnings, 0 errors
- Layering greps return nothing: `grep -rn "using Microsoft.EntityFrameworkCore" src/Application src/Domain --include=*.cs` and the same for `using po_prostu_silka.Infrastructure`
- Migration script is byte-identical: same command with `--project src/Infrastructure/... --startup-project src/Api/...` into `<scratch>/after.sql`, then `diff before.sql after.sql` → empty
- `dotnet ef migrations list` shows the same 22 in the same order
- Full suite green with no test file edited: `dotnet test po-prostu-silka.slnx` (requires Docker running)
- `git diff --stat` shows every `.cs` file as a pure rename (R100) or unchanged

#### Manual Verification:

- The CS-01 proof: add `using Microsoft.EntityFrameworkCore;` to a file under `src/Application/`, run `dotnet build`, confirm CS0234, revert. Never commit the probe
- `docker compose up -d` then `dotnet run --project src/Api/...` and `GET /health` opens a real DB connection
- From `src/app/`: `npm run e2e:stage` writes into `src/Api/wwwroot/`, and the app serves the SPA at `/` rather than 404
- `git log --follow` still reaches the first commit on a moved file. **Adapted during implementation.** This can only be checked AFTER the phase commit — rename detection reads committed history, and until then the move sits in the working tree as `D` + untracked. Run it on `src/Api/Program.cs` (the only `.cs` whose path changed in this phase) once the commit lands. `AuthEndpoints.cs` was the wrong example: it never moves in phase 1, since the `.csproj` files came to the layer folders rather than the folders moving.
- `src/wwwroot/` is gone from disk. **Adapted during implementation: declined.** The 108 orphaned files are untracked and covered by `.gitignore:41` (`wwwroot/`, path-agnostic), so they cannot reach the repo, and no project owns `src/` any more so nothing picks them up at build time. They were left on disk by explicit decision; the only cost is local disk and a stale bundle nobody reads.

**Implementation Note**: Pause here for manual confirmation before phase 2. This is the phase that can
break production; the deploy workflow is only exercised on merge.

---

## Phase 2: Ports and contracts

### Overview

Empty the `*Endpoints` files of every declaration — **15 interfaces, 49 DTO records and 1 enum, 65 in
total** — into their own files under `Application`. **Adapted during implementation.** The plan said
"nine ports": research §D had enumerated only the nine that are consumed ACROSS contexts, but six more
interfaces are declared in these files (`IMembershipPassQuery`, `ITrainerQuery`, `IVapidPublicKey`,
`IPushSubscriptionStore`, `IClassScheduleQuery`, `IClassTypeQuery`, `IExerciseQuery`, `IExerciseStore`,
`ITrainingPlanStore`), plus the `MemberListFilter` enum. All of them have to leave for the same reason:
the file they live in moves to `Api` in phase 7. The endpoint files keep their routes and handlers;
only declarations move. This is the prerequisite for every handler split **and** the reason phase 7
compiles: whatever is still declared in a `*Endpoints.cs` file moves to `Api` with it.

### Changes Required:

#### 1. The nine ports

**Files**: one new file per interface, in the context folder that declares it today —
`Application/Scheduling/IBookingStore.cs`, `IBookingQuery.cs`, `IClassStore.cs`, `IClassTypeStore.cs`;
`Application/Members/IMemberQuery.cs`; `Application/Training/ITrainingPlanQuery.cs`; plus any remaining
`I*Store`/`I*Query` still declared at the bottom of an endpoint file.

**Intent**: Give each port a predictable path so a use-case file can reference it without pulling in
another context's compilation unit.

**Contract**: Interface bodies move verbatim, doc comments included. This matches the convention the two
newest slices already chose (`Application/Members/IMemberStore.cs`, `IMembershipPassStore.cs`) — it
settles undocumented drift rather than inventing a third pattern. Two interfaces carry methods with no
caller in their declaring file — `IMemberQuery.EmailExistsAsync` (used only by `AuthEndpoints`) and
`ITrainingPlanQuery.FindActiveForMemberAsync` / `FindPlanExerciseAsync` (used only by `MyPlanEndpoints`).
Both are correct; they are the reason the one-file-per-context assumption is false.

#### 2. Every DTO record — all 49, not just the cross-context ones

**Files**: all 49 `record` declarations currently inside the thirteen `*Endpoints.cs` files, each to its
own file under `Application/<Context>/` (`Application/Scheduling/ScheduledClass.cs`,
`Application/Auth/LoginFailure.cs`, …). Counts per file: Auth 10, MemberAdmin 9, TrainingPlan 7, Class 5,
Booking 4, Exercise 3, ClassType 3, MembershipPass 3, Push 2, Profile 2, Trainer 1.

**Intent**: **This is what makes phase 7 compile.** Phase 7 `git mv`s the `*Endpoints.cs` files into
`Api`; every record still declared inside one of them would move with it, and two things break:

1. **Application → Api.** `LoginFailure` is declared at `AuthEndpoints.cs:47` and constructed by the
   login handler at `:198` (`Results.Json(new LoginFailure("invalid_credentials"), statusCode: 401)`).
   Phases 3–6 move that handler into `Application`; the record must not end up in `Api`.
2. **Infrastructure → Api, which is a reference CYCLE** — `Api` already references `Infrastructure`, so
   no added reference can fix it. Thirteen of these records are consumed from `Infrastructure` because
   the query implementations project into them: `ClassScheduleQuery.cs:15,82` returns and constructs
   `ScheduledClass`, declared at `ClassEndpoints.cs:52`. The other twelve are `MemberSummary`,
   `MemberDetail`, `MembershipPassView`, `TrainerSummary`, `MyBooking`, `ClassBooking`,
   `ClassTypeSummary`, `ExerciseSummary`, `TrainingPlanSummary`, `TrainingPlanItemView`,
   `TrainingPlanDetail`, `AssignableMember`.

Research §D enumerated the *ports* as the cross-context web and stopped there; the DTOs those ports
return are the same problem and were missed. This item closes both.

**Contract**: Records move verbatim — these are wire contracts the SPA mirrors, and a renamed field
breaks a screen silently. The 16 `*Failure` records and their 60 `reason` codes are pinned by tests on
both sides; not one string changes. Two records carry documented cross-file consumers worth naming:
`ClassBooking` (`BookingEndpoints.cs:37` → `ClassChangeNotification.cs:54,69,82,99,167`) and
`ExerciseSummary` (`ExerciseEndpoints.cs:23` → `TrainingPlanEndpoints.cs:698`).

A record used only by its own handler may sit in that handler's file once phases 3–6 create it — but it
must leave the `*Endpoints.cs` file here regardless, because that file is what moves in phase 7.

**Adapted during implementation: the `using` block is copied wholesale.** Each extracted file carries
its source endpoint file's complete `using` block rather than a minimised one. Unused `using`s are not
compiler warnings, so this costs nothing at the 0-warnings bar and removes a whole class of extraction
error; the alternative — deriving the minimal set per declaration — would have been the one step in
this phase capable of silently dropping a needed import. Tidying them is a `dotnet format` follow-up,
not this slice's business.

#### 3. The two `internal` cross-file members

**Files**: `Application/Scheduling/ClassDtoMapping.cs`, `Application/Auth/CurrentUserBuilder.cs` (new,
names indicative).

**Intent**: `BookingEndpoints.cs:378` calls `ClassEndpoints.ToDto` and `ProfileEndpoints.cs:125` calls
`AuthEndpoints.BuildCurrentUserAsync`. Each is the only `internal` member of its file, and each would
otherwise force one endpoint file to survive until the other is split.

**Contract**: Both move to their own file with the same `internal` accessibility and the same signature.
Call sites change class name only.

#### 4. Placement corrections

**File**: `Application/Notifications/OutboxOptions.cs` → `Infrastructure/Notifications/`

**Intent**: It is read only by `OutboxDeliveryWorker` and `OutboxHealthCheck`; nothing in `Application`
touches it.

**Contract**: `git mv` plus a namespace change to `po_prostu_silka.Infrastructure.Notifications` and the
corresponding `using` at both consumers and at its `Configure<>` registration in `Program.cs`. This is
the one file in the whole slice whose namespace changes — call it out in the commit message.

**Files staying put, with the reason recorded here so the next reviewer does not re-open them**:
`Members/ClaimsPrincipalExtensions.cs` (looks Api-shaped, but `ClaimsPrincipal` is BCL and all six
callers are handlers); `Notifications/ClassChangeNotification.cs` (a genuine application service — it
renders text and fans out over ports, never saves); `Members/MembershipPassRules.cs` (Domain is
arguable, but it is already referenced from a Domain doc-comment and an EF configuration, and moving it
is not this slice's business).

### Success Criteria:

#### Automated Verification:

- `dotnet build po-prostu-silka.slnx -c Release` → 0 warnings, 0 errors
- `dotnet test po-prostu-silka.slnx` green, no test file edited
- `dotnet test --filter "FullyQualifiedName~EndpointAuthorizationTests"` green
- No new `using po_prostu_silka.Infrastructure` in `Application`/`Domain`
- **No record or interface is left in an endpoint file** — `grep -cE '^\s*(public|internal)\s+(sealed\s+)?(record|interface) ' src/Application/**/*Endpoints.cs` returns 0 for every file (49 records and the ports at the start)

#### Manual Verification:

- Every moved interface and record is byte-identical to its previous body apart from indentation
- No `*Endpoints` file declares an interface any more
- Spot-check that the 16 `*Failure` records kept every `reason` string unchanged

---

## Phase 3: Handler split — pilot

### Overview

Split the five smallest endpoint files to establish the convention on a small sample before repeating it
eleven more times. Five files, 8 routes, 517 lines total.

### Changes Required:

#### 1. The pilot files

**Files**: `Application/Members/TrainerEndpoints.cs` (91, 1 route),
`Application/Members/MyPassEndpoints.cs` (84, 1), `Application/Training/MyPlanEndpoints.cs` (103, 2),
`Application/Members/ProfileEndpoints.cs` (127, 1), `Application/Notifications/PushEndpoints.cs` (112, 3).

**Intent**: One handler body, one file. The `*Endpoints` class keeps `Map*Endpoints` and nothing else.

**Contract**: Each handler moves to `Application/<Context>/<HandlerName>.cs` as a `public static class`
with a single `public static async Task<IResult>` method, bound from the endpoint file by method group
exactly as today — the binding syntax does not change. Group registration, policies, `WithTags` and
route order stay in the `*Endpoints` file, untouched.

`ProfileEndpoints.cs:114` builds its logger category from `typeof(ProfileEndpoints)`. **Keep that
`typeof` after the move**, with a comment stating it is pinned so the Azure log category string does not
change mid-refactor (CS-03). The same rule applies to `AuthEndpoints.cs:389`, `:452`, `:660` in phase 6.
Done: the pin now lives in `UpdateProfile.HandleAsync` with the reason inline, and `ProfileEndpoints`
carries a pointer to it so the `typeof` reference does not look like a leftover.

`PushEndpoints` is the only surface with no failure record — it answers bare `Results.BadRequest()`.
Do not give it one.

**Adapted during implementation — two more dead injected parameters, dropped here.**
`MyPlanEndpoints.GetMineAsync` and `GetMyExerciseAsync` each took a
`UserManager<ApplicationUser>` that is bound per request and never read; both resolve the caller from
the principal's member claim instead. They are dropped with the handlers, under the same decision the
plan records for the two in `BookingEndpoints` and `TrainingPlanEndpoints`. Research §E.2 stated there
were exactly two in the repo; there are at least four, so **treat its count as a floor, not an
inventory, when phases 5 and 6 reach those files** — check each signature against its body rather than
against the list.

**A caution about how that was found.** An automated unused-parameter scan written for this reported
237 dead parameters, including ones plainly used (`ITrainerQuery query` in `GetTrainers`). The bug was
in the scan, not the code — it located the method body with `rindex(')')` and so compared against a
truncated body. The four real cases were confirmed by reading the bodies. Do not resurrect that script;
if phases 5–6 want a systematic sweep, use the compiler (`dotnet build` with IDE0060 elevated) rather
than a regex.

**Convention established by this pilot** (the reason the phase exists):

- One file per handler body, named for the use case — `GetMyPass.cs`, `UpdateProfile.cs`,
  `Subscribe.cs` — not for the route and not for the endpoint class.
- The class is `public static` and named for the use case; the method is uniformly `HandleAsync`
  (or `Handle` when the handler is synchronous, as `GetVapidKey` is). Uniform naming is what makes
  the binding line read as a use-case reference: `group.MapPut("/", UpdateProfile.HandleAsync)`.
- The handler's doc comment travels with the handler. The endpoint class keeps only the doc that is
  about the GROUP — its policy, its prefix, why it is a separate group.

### Success Criteria:

#### Automated Verification:

- `dotnet build po-prostu-silka.slnx -c Release` → 0 warnings, 0 errors
- `dotnet test po-prostu-silka.slnx` green, no test file edited
- `dotnet test --filter "FullyQualifiedName~EndpointAuthorizationTests"` green — the real route/policy gate, better than a route-literal grep
- Route-literal diff is empty: `grep -rho '"/api/[^"]*"' src --include=*.cs | sort | diff - <scratch>/routes-before.txt`

#### Manual Verification:

- Review the five new-file sets and confirm the convention reads well before it is repeated 11 times: file naming, class naming, where the doc comment lives
- No non-comment line changed inside any moved handler body

**Implementation Note**: Pause here. This phase exists to be reviewed — the cost of a wrong convention
is eleven files, not five.

---

## Phase 4: Handler split — Members

### Overview

`MemberAdminEndpoints.cs` (928 lines, 11 routes) and `MembershipPassEndpoints.cs` (477, 4).

### Changes Required:

#### 1. The two Members files

**File**: `Application/Members/MemberAdminEndpoints.cs`, `Application/Members/MembershipPassEndpoints.cs`

**Intent**: Same convention as phase 3.

**Contract**: `ChangeTrainerRoleAsync` is **one body behind two routes** (grant and revoke). The file
boundary is the handler body, so it becomes one file bound from two `Map*` calls — splitting it would
duplicate logic, which is a behaviour change disguised as reorganization. `GetAccessCode` +
`RevokeAccessCode` are two bodies and therefore two files.

**Adapted during implementation — three mechanical facts for phases 5 and 6.**

1. `GrantTrainerAsync` / `RevokeTrainerAsync` are not bound directly to the shared body: each is a
   one-line delegating wrapper. All three live in `ChangeTrainerRole.cs`, bound as
   `ChangeTrainerRole.GrantAsync` and `.RevokeAsync`.
2. **Shared helpers need their own file too, and they are not always obvious from the route list.**
   `MemberAdminEndpoints.TryReadRequest` (2 callers) became `MemberRequestReader`;
   `MembershipPassEndpoints`' `TryReadRequest` + `EntriesUsedAsync` + `ViewOfAsync` became
   `MembershipPassProjection`. Note both files declared a *differently-bodied* method with the same
   name `TryReadRequest` — they are unrelated and must not be merged.
3. **Constants travel with their sole user.** `MemberAdminEndpoints.CodeAttempts` had exactly one
   consumer and moved into `IssueAccessCode` with its doc comment. This does **not** apply to the
   class-limit constants in phase 5, which are duplicated deliberately and stay put.

**And one caution about the extraction itself:** a moved method's calls to its former siblings are not
rewritten by moving it — the compiler catches each as CS0103, which is the intended safety net, but
expect a build-fix round after every split rather than treating a green first build as the norm.

### Success Criteria:

#### Automated Verification:

- `dotnet build po-prostu-silka.slnx -c Release` → 0 warnings, 0 errors
- `dotnet test po-prostu-silka.slnx` green, no test file edited
- `dotnet test --filter "FullyQualifiedName~EndpointAuthorizationTests"` green
- Route-literal diff empty against `<scratch>/routes-before.txt`

#### Manual Verification:

- `ChangeTrainerRole` is one file bound twice, and both routes still carry their original policy

---

## Phase 5: Handler split — Scheduling

### Overview

The hardest phase: `ClassEndpoints.cs` (1082, 8 routes), `BookingEndpoints.cs` (798, 4),
`ClassTypeEndpoints.cs` (404, 6).

### Changes Required:

#### 1. The three Scheduling files

**File**: `Application/Scheduling/ClassEndpoints.cs`, `BookingEndpoints.cs`, `ClassTypeEndpoints.cs`

**Intent**: Same convention. `ClassEndpoints`' shared helpers (`ResolveRange`, `Validate`,
`ValidateInstructorAsync` — two callers each) become their own files alongside the handlers, like
`ToDto` in phase 2.

**Contract**: Three registration facts must survive unchanged.

1. **`/api/admin/classes` is registered twice, in two files, under two different policies** —
   `ClassEndpoints.cs:224` (`Admin`) and `BookingEndpoints.cs:232` (`TrainerOrAdmin`). Do not merge or
   reorder these groups. `EndpointAuthorizationTests` is what proves it.
2. **`BookingEndpoints`' `MayActOn` ownership check** (`:590-608`) is enforced only by a comment at
   `:217-231`, already ~350 lines away. Moving handlers into separate files moves the check away from
   its only warning — carry the warning comment into the file that now holds the check, and reference it
   from the registration site. **`EndpointAuthorizationTests` does NOT cover this**: its own doc comment
   (`:27-29`) names "the inline instructor check on staff booking" as one of the two things it cannot
   see. The gate here is `AdminBookingEndpointTests`, which exercises it over HTTP — run it explicitly
   in this phase rather than relying on the metadata test.
3. **`TryBookAsync`** (`:243-391`) is the no-overbooking protocol. It moves as one body, untouched. The
   retry bound is **10** (`BookingEndpoints.cs:197`), not the 3 an archived plan-brief states — do not
   "correct" it toward the archive.

`BookingEndpoints.GetMineAsync:468` takes a `UserManager<ApplicationUser>` that is bound per request and
never used. **Drop it** as the handler moves; the signature is being rewritten anyway.

The four class-limit constants stay duplicated across `ClassEndpoints` and `ClassTypeEndpoints`, with
their existing comments. Do not consolidate. They now live in `ClassRequestValidator` and
`ClassTypeValidator` respectively — both `private`, both still (1, 480, 1, 200), and each carries the
"duplicated on purpose" note so neither can be mistaken for a copy that drifted.

**Adapted during implementation — three things this phase surfaced.**

1. **Two methods defeat a single-line signature match and were moved by hand**: `ResolveRange`
   (nested tuple return type) and `ValidateInstructorAsync` (declaration split across two lines).
   Anything scripted over these files must assert what it matched rather than assume full coverage —
   a silent miss here leaves a method behind in a file that is about to move to `Api`.
2. **Only three of the fourteen extracted constants became `public`**: `MaxAttempts` (read by
   `ReleaseBooking` as well as the protocol) and the two schedule-window bounds (read by both schedule
   handlers). The other eleven are used solely inside their own validator and were left `private` —
   the split is an opportunity to narrow them, not a reason to widen everything.
3. **`MayActOn`'s warning now exists in two places, deliberately.** The long-form warning stays at the
   registration site in `BookingEndpoints` (where someone adding a route will read it) and a copy
   travels with the check into `BookingAuthorization` (where someone editing the check will). This is
   the one duplication this phase adds on purpose; the plan asked for the comment to follow the check,
   and removing it from the registration site would have defeated its original purpose.

### Success Criteria:

#### Automated Verification:

- `dotnet build po-prostu-silka.slnx -c Release` → 0 warnings, 0 errors
- `dotnet test po-prostu-silka.slnx` green, no test file edited
- `dotnet test --filter "FullyQualifiedName~EndpointAuthorizationTests"` green — specifically that both `/api/admin/classes` groups still report their distinct policies
- Route-literal diff empty against `<scratch>/routes-before.txt`

#### Manual Verification:

- Both `/api/admin/classes` groups exist, in their original files, with their original policies
- The `MayActOn` warning comment sits with the check it warns about
- `TryBookAsync`'s body is unchanged line-for-line; retry bound still 10

---

## Phase 6: Handler split — Auth and Training

### Overview

`AuthEndpoints.cs` (812, 8 routes), `TrainingPlanEndpoints.cs` (746, 5), `ExerciseEndpoints.cs` (512, 6).
This completes CS-02 for all thirteen files.

### Changes Required:

#### 1. The three remaining files

**File**: `Application/Auth/AuthEndpoints.cs`, `Application/Training/TrainingPlanEndpoints.cs`,
`Application/Training/ExerciseEndpoints.cs`

**Intent**: Same convention.

**Contract**:

- `RegisterAsync` (`AuthEndpoints.cs:238-480`, 243 lines) is the largest handler in the repo. It moves as
  one body; do not decompose it. That is a different slice.
- `ForgotPassword` and `ResetPassword` are two bodies → two files.
- The three `CreateLogger(typeof(AuthEndpoints))` calls at `:389`, `:452`, `:660` keep their `typeof`
  after the move, with the pinning comment (CS-03).
- `TrainingPlanEndpoints` **requires `/members` (`:257`) registered before `/{id:guid}` (`:259`)**,
  documented at `:253-256` and currently masked by the `:guid` constraint. Registration order stays
  literal and deterministic in the `*Endpoints` file.
- `TrainingPlanEndpoints.CreateAsync:324` takes an `IMemberStore` that is never used. **Drop it.**
- Twelve of `TrainingPlanEndpoints`' thirteen constants feed one `ValidateShape` — that validator becomes
  its own file and takes the constants with it.
- `ExerciseEndpoints` registers **two groups on the same `/api/admin/exercises` prefix, split by
  policy**. Keep both groups and their order.

### Success Criteria:

#### Automated Verification:

- `dotnet build po-prostu-silka.slnx -c Release` → 0 warnings, 0 errors
- `dotnet test po-prostu-silka.slnx` green, no test file edited
- `dotnet test --filter "FullyQualifiedName~EndpointAuthorizationTests"` green
- Route-literal diff empty against `<scratch>/routes-before.txt`
- No `*Endpoints` file contains a method other than `Map*Endpoints`

#### Manual Verification:

- `/api/admin/plans/members` still resolves ahead of `/api/admin/plans/{id}` for a non-guid segment
- Both `/api/admin/exercises` groups exist with their distinct policies
- The 60 `reason` codes are unchanged — spot-check against the SPA's discriminated unions

---

## Phase 7: Registration moves to Api

### Overview

Move the thirteen now-thin `*Endpoints` classes into `Api`, apply the remaining placement corrections,
and shrink `Application`'s implicit-using set to what it actually needs.

### Changes Required:

#### 1. The thirteen registration classes

**File**: `Application/**/*Endpoints.cs` → `Api/Endpoints/<Context>/`

**Intent**: With the handlers gone, these files are pure HTTP routing — `IEndpointRouteBuilder`,
`MapGroup`, `RequireAuthorization`. Moving them makes "the application layer does not know about HTTP
routing" a compiler constraint too.

**Contract**: **Precondition, from phase 2: these files declare nothing but their `Map*Endpoints`
method.** Any record or interface still inside one moves to `Api` with it and breaks the build —
`Application` cannot reference `Api`, and for the 13 records consumed from `Infrastructure` it is a
reference cycle. Verify with the phase-2 grep before starting.

`git mv` plus a namespace change to `po_prostu_silka.Api.Endpoints.<Context>` and a
matching `using` in `Program.cs`. `Program.cs:369-381` keeps all thirteen `app.Map*Endpoints()` calls in
the same order. Handlers stay in `Application` and are referenced by method group across the project
boundary — they are already `public static`.

#### 2. Remaining placement corrections

**File**: `Application/Auth/RateLimitPolicies.cs` → `Api/Auth/`

**Intent**: It takes `HttpContext` (`:70`) and reads `X-Forwarded-For`; its only consumers are
`Program.cs` and route registration, which is itself now in `Api`.

**Contract**: `git mv` plus namespace change and the `using` in `Program.cs`.

**File**: `Application/Auth/PasswordResetThrottle.cs` — class → `Infrastructure/Auth/`, interface stays

**Intent**: The class is a stateful in-memory singleton, which is the role `Infrastructure` plays for
every other adapter. `IPasswordResetThrottle` stays in `Application` as the port.

**Contract**: Split the file. `IPasswordResetThrottle` remains at
`Application/Auth/IPasswordResetThrottle.cs`; the implementation moves with a namespace change and the
DI registration in `Program.cs` updates its `using`.

#### 3. Shrink Application's implicit usings

**File**: `src/Application/po-prostu-silka.Application.csproj`

**Intent**: With registration gone, `Microsoft.AspNetCore.Routing` and `Microsoft.AspNetCore.Builder` are
no longer needed by any `Application` file. Removing them is what makes the boundary real rather than
nominal.

**Contract**: Delete both `<Using>` items. `Microsoft.AspNetCore.Http` stays — handlers return `IResult`.
Verify by build, not by grep: if a file still needs one, the build says so by name.

#### 4. DI registration — what must not change

**File**: `src/Api/Program.cs`

**Intent**: `Program.cs` gains `using` statements for the moved namespaces and nothing else. **No
`AddInfrastructure()` / `AddApplication()` extraction**: nothing in the Desired End State needs it, and
this is the phase where "while I'm here" is most expensive.

**Contract**: ⚠️ **`AddHostedService<OutboxDeliveryWorker>()` must stay generic-typed.**
`IntegrationTestFixture.cs:416-423` removes the worker by matching
`ImplementationType == typeof(OutboxDeliveryWorker)` and uses `.Single(...)`. Registering it through a
factory delegate makes `ImplementationType` null, `Single` throws, and **the test host fails to start** —
the fixture comment says that failure is deliberate. This is the concrete reason the registration block
is left alone rather than tidied.

The `Testing`-only probes at `Program.cs:399`/`:402` use `AuthorizationPolicies` from `Infrastructure` —
that reference must survive.

#### 5. Foundation documentation

**File**: `context/foundation/prd-v2.md:23-25`, `context/foundation/roadmap.md:179,184-185`,
`context/foundation/test-plan.md:268-269`, `context/deployment/deploy-plan.md:26,29,31,56`

**Intent**: Update stated paths now that the final shape is real.

**Contract**: Paths and commands only — no claim about what the slice delivered changes. **Never edit
`context/archive/`**; the 14 stale `Mirrors … (path)` citations across the SPA are S-19's or a later
slice's business, and the three naming `ContactDetails.cs` / `MembershipPassRules.cs` survive because
`src/Application/` remains a real folder.

### Success Criteria:

#### Automated Verification:

- `dotnet build po-prostu-silka.slnx -c Release` → 0 warnings, 0 errors
- `dotnet test po-prostu-silka.slnx` green, no test file edited
- `dotnet test --filter "FullyQualifiedName~EndpointAuthorizationTests"` green
- Route-literal diff empty against `<scratch>/routes-before.txt`
- No `IEndpointRouteBuilder`, `MapGroup` or `RequireAuthorization` under `src/Application/`
- Migration script still byte-identical to `<scratch>/before.sql`

#### Manual Verification:

- The CS-01 proof, re-run and recorded: `using Microsoft.EntityFrameworkCore;` in an `Application` file fails the build; the probe is reverted, not committed
- The test host starts — proof that `AddHostedService<OutboxDeliveryWorker>()` is still generic-typed
- `docker compose up -d`, `dotnet run --project src/Api/...`, `GET /health` connects; log in through the SPA and confirm a booking, a plan and a schedule view behave identically
- `AGENTS.md` describes what the build now enforces, including that "Domain references nothing" is no longer literally true

---

## Testing Strategy

### Existing tests carry this slice — no test file may be edited

`context/foundation/test-plan.md` has all four phases `complete`, and §3 risk #6 is literally "a
structural refactor silently changes an API contract the SPA depends on". The safety net was built for
this slice.

- `EndpointAuthorizationTests.cs` reads endpoint metadata at runtime and asserts every route's policy.
  It is the primary gate for phases 3–7 and strictly better than grepping route literals, because it
  catches a group that survives with the wrong policy. **Its two stated blind spots** (`:27-29`) are
  whether a policy's *definition* is right, and the inline instructor check on staff booking — so it is
  not the gate for phase 5's `MayActOn` move. `AdminBookingEndpointTests` is.
- `IntegrationTestFixture` boots the real app via `WebApplicationFactory<Program>` against a real SQL
  Server (Testcontainers), so engine-dependent behaviour — filtered unique indexes, locking, the
  no-overbooking guarantee — is exercised on every phase gate.
- The SPA contract tests pin the 60 `reason` codes on the frontend side.

**A failing test in any phase means the move was wrong.** Editing the test to make it pass is the one
move this plan forbids outright.

### Manual Testing Steps

1. `docker compose up -d`; `GET /health` returns healthy against a real connection.
2. Log in as admin; open the schedule, book a member into a class, cancel it. No-overbooking path intact.
3. Open a training plan as a member; the plan card renders with prescription detail.
4. Register through an invitation link; confirm the account is active and rate limiting still refuses a
   burst (`RateLimitPolicies` moved projects in phase 7).
5. Trigger a class change and confirm email + push still fan out (the outbox worker's registration is
   the phase-7 trap).
6. From `src/app/`: `npm run e2e:stage` then the Playwright suite against the built SPA.

## Performance Considerations

None at runtime — four assemblies instead of one changes JIT and startup immeasurably for an app this
size. Build time rises slightly (four compilations, parallelisable) and incremental builds get *faster*,
since editing an `Application` file no longer recompiles `Infrastructure`'s 84 files.

## Migration Notes

No schema change, and no migration is regenerated, squashed or content-edited. `AppDbContext` and
`Persistence/Migrations/` land in the same assembly, so `MigrationsAssembly` is not needed and the next
generated migration lands in the same namespace it would have today.

The gate is byte-equality of `dotnet ef migrations script --idempotent` before and after, written outside
the repo, plus `dotnet ef migrations list` showing the same 22 in the same order.

Rollback: phases 1–6 are revertible by `git revert` with no schema consequence. Phase 1 is the only
phase whose revert must also restore `deploy.yml`, and the two are in the same commit precisely so that
revert is atomic.

## References

- Change brief: `context/changes/backend-layer-boundaries/change.md`
- Research: `context/changes/backend-layer-boundaries/research.md`
- Roadmap item: `context/foundation/roadmap.md` — S-18, anchors CS-01/CS-02/CS-03
- The gate this slice was built around: `tests/po-prostu-silka.Tests/EndpointAuthorizationTests.cs:12-17`
- The DI trap: `tests/po-prostu-silka.Tests/IntegrationTestFixture.cs:416-423`
- Pre-authorised escalation: `context/archive/2026-08-31-persistence-foundation/plan.md:91`, `plan-brief.md:103-104`
- "Do not move `ApplicationUser`": `context/archive/2026-08-31-auth-identity-foundation/plan.md:201-204`
- csproj-justification precedent: `context/archive/2026-09-01-registration-and-approval/reviews/impl-review.md:90-113`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Four projects

#### Automated

- [x] 1.1 Capture both baselines before any move: migration script + list (22), and route literals (18 lines / 16 unique) to `<scratch>/routes-before.txt` — f5b58f0
- [x] 1.2 `dotnet build po-prostu-silka.slnx -c Release` → 0 warnings, 0 errors — f5b58f0
- [x] 1.3 Layering greps return nothing — f5b58f0
- [x] 1.4 Migration script byte-identical (`diff before.sql after.sql` empty) — f5b58f0
- [x] 1.5 `dotnet ef migrations list` shows the same 22 in the same order — f5b58f0
- [x] 1.6 `dotnet test po-prostu-silka.slnx` green, no test file edited — f5b58f0
- [x] 1.7 `git diff --stat` shows every `.cs` as pure rename or unchanged — f5b58f0

#### Manual

- [x] 1.8 CS-01 proof: EF Core using in Application fails the build; probe reverted — f5b58f0
- [x] 1.9 `GET /health` connects against Docker SQL Server — f5b58f0
- [x] 1.10 `npm run e2e:stage` writes to `src/Api/wwwroot/` and the SPA is served — f5b58f0
- [x] 1.11 `git log --follow` reaches first commit on `src/Api/Program.cs` (check AFTER the phase commit) — f5b58f0
- [x] 1.12 `src/wwwroot/` left on disk by decision — untracked, gitignored, owned by no project — f5b58f0

### Phase 2: Ports and contracts

#### Automated

- [x] 2.1 `dotnet build po-prostu-silka.slnx -c Release` → 0 warnings, 0 errors — e6b882d
- [x] 2.2 `dotnet test po-prostu-silka.slnx` green, no test file edited — e6b882d
- [x] 2.3 `EndpointAuthorizationTests` green — e6b882d
- [x] 2.4 No new `using po_prostu_silka.Infrastructure` in Application/Domain — e6b882d
- [x] 2.5 No record, interface or enum left in an endpoint file — grep returns 0 for all 13 (65 declarations moved) — e6b882d

#### Manual

- [x] 2.6 Moved interfaces and records byte-identical apart from indentation — e6b882d
- [x] 2.7 No `*Endpoints` file declares an interface — e6b882d
- [x] 2.8 The 16 `*Failure` records kept every `reason` string unchanged — e6b882d

### Phase 3: Handler split — pilot

#### Automated

- [x] 3.1 `dotnet build po-prostu-silka.slnx -c Release` → 0 warnings, 0 errors — 99e1822
- [x] 3.2 `dotnet test po-prostu-silka.slnx` green, no test file edited — 99e1822
- [x] 3.3 `EndpointAuthorizationTests` green — 99e1822
- [x] 3.4 Route-literal diff empty against `<scratch>/routes-before.txt` — 99e1822

#### Manual

- [x] 3.5 Convention reviewed and approved before repeating on eleven files — 99e1822
- [x] 3.6 No non-comment line changed inside any moved handler body — 99e1822

### Phase 4: Handler split — Members

#### Automated

- [x] 4.1 `dotnet build po-prostu-silka.slnx -c Release` → 0 warnings, 0 errors — 2f8fb5e
- [x] 4.2 `dotnet test po-prostu-silka.slnx` green, no test file edited — 2f8fb5e
- [x] 4.3 `EndpointAuthorizationTests` green — 2f8fb5e
- [x] 4.4 Route-literal diff empty against `<scratch>/routes-before.txt` — 2f8fb5e

#### Manual

- [ ] 4.5 `ChangeTrainerRole` is one file bound from two routes, policies intact

### Phase 5: Handler split — Scheduling

#### Automated

- [x] 5.1 `dotnet build po-prostu-silka.slnx -c Release` → 0 warnings, 0 errors
- [x] 5.2 `dotnet test po-prostu-silka.slnx` green, no test file edited
- [x] 5.3 `EndpointAuthorizationTests` green, both `/api/admin/classes` groups distinct
- [x] 5.4 Route-literal diff empty against `<scratch>/routes-before.txt`

#### Manual

- [ ] 5.5 Both `/api/admin/classes` groups in their original files with original policies
- [ ] 5.6 `MayActOn` warning comment sits with the check it warns about
- [ ] 5.7 `TryBookAsync` body unchanged line-for-line; retry bound still 10

### Phase 6: Handler split — Auth and Training

#### Automated

- [ ] 6.1 `dotnet build po-prostu-silka.slnx -c Release` → 0 warnings, 0 errors
- [ ] 6.2 `dotnet test po-prostu-silka.slnx` green, no test file edited
- [ ] 6.3 `EndpointAuthorizationTests` green
- [ ] 6.4 Route-literal diff empty against `<scratch>/routes-before.txt`
- [ ] 6.5 No `*Endpoints` file contains a method other than `Map*Endpoints`

#### Manual

- [ ] 6.6 `/api/admin/plans/members` resolves ahead of `/{id:guid}`
- [ ] 6.7 Both `/api/admin/exercises` groups exist with distinct policies
- [ ] 6.8 The 60 reason codes unchanged, spot-checked against the SPA

### Phase 7: Registration moves to Api

#### Automated

- [ ] 7.1 `dotnet build po-prostu-silka.slnx -c Release` → 0 warnings, 0 errors
- [ ] 7.2 `dotnet test po-prostu-silka.slnx` green, no test file edited
- [ ] 7.3 `EndpointAuthorizationTests` green
- [ ] 7.4 Route-literal diff empty against `<scratch>/routes-before.txt`
- [ ] 7.5 No `IEndpointRouteBuilder` / `MapGroup` / `RequireAuthorization` under `src/Application/`
- [ ] 7.6 Migration script still byte-identical to the phase-1 baseline

#### Manual

- [ ] 7.7 CS-01 proof re-run and recorded; probe reverted
- [ ] 7.8 Test host starts — `AddHostedService<OutboxDeliveryWorker>()` still generic-typed
- [ ] 7.9 Full manual walkthrough: health, booking, plan, registration, notifications
- [ ] 7.10 `AGENTS.md` describes what the build now enforces
