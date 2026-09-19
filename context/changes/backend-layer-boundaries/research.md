---
date: 2026-09-18T00:00:00+02:00
researcher: Karol Rumianowski
git_commit: e925de8b463b63c588ccc7a6350abaf2f7278007
branch: main
repository: PoProstuSilka
topic: "Splitting src/ into compiler-enforced layer projects and thinning the 13 *Endpoints classes (S-18)"
tags: [research, codebase, layering, csproj, minimal-api, ef-core-migrations, endpoints]
status: complete
last_updated: 2026-09-18
last_updated_by: Karol Rumianowski
---

# Research: Compiler-enforced layer boundaries and thin endpoint classes

**Date**: 2026-09-18
**Researcher**: Karol Rumianowski
**Git Commit**: `e925de8`
**Branch**: `main`
**Repository**: PoProstuSilka (`github.com/rumek/po-prostu-silka`)

## Research Question

What does the codebase actually require in order to (a) split `src/` into four projects so the
compiler enforces the layering `AGENTS.md` describes, and (b) reduce each `*Endpoints` class to route
registration, with handlers, validation, mapping, ports and resource-level authorization moved into
Application — **without changing any behaviour**?

Specifically: the exact cut list per endpoint file, every coupling that must be severed, every
reference outside `src/*.cs` that breaks, and whatever recorded reasoning would make any of this a
mistake.

## Summary

**The split is mechanical, and the codebase is in better shape for it than the change brief assumed.**
Six findings decide the plan:

1. **The dependency direction is already correct.** No `using po_prostu_silka.Infrastructure` in
   `Application`/`Domain`; no `using po_prostu_silka.Application` in `Domain`; no EF Core or
   `Microsoft.Data.SqlClient` in either. Combined with namespaces that already match the target
   `RootNamespace` per project, **phase 1 changes no `using` and no namespace anywhere** — in `src/` or
   in the 47 `using` statements across 23 test files.

2. **The one genuine build trap is implicit usings.** The Web SDK contributes 9 global usings a plain
   `Microsoft.NET.Sdk` class library does not get. **13–14 Application files and 10 Infrastructure
   files compile today only because of them.** This is the most likely cause of a "hundreds of errors"
   phase-1 stall, and it is fixable in one file.

3. **Extracting the ports is a prerequisite for the handler split, not a parallel task.** Every
   `*Endpoints.cs` declares its context's `I*Store`/`I*Query` in the same file as its routes, and those
   ports are consumed **across contexts** — nine such dependencies. Split handlers first and a
   use-case file in `Scheduling/` drags in the whole `MemberAdminEndpoints` compilation unit for one
   interface.

4. **The safety net was purpose-built for this slice and has fully landed.** All four phases of
   `context/foundation/test-plan.md` are `complete`. `tests/po-prostu-silka.Tests/EndpointAuthorizationTests.cs:12-17`
   names S-18 in its own doc comment as the reason it exists, reads endpoint metadata at runtime rather
   than reflecting over assemblies, and is therefore a **strictly better gate than the route-literal
   grep** the change brief proposed.

5. **S-18 cannot avoid touching `src/app/`.** Two files in the Angular workspace name the backend
   project path. This breaks the "S-18 and S-19 share no files" premise and needs a decision before
   planning — see *Corrections* §3.

6. **One decision in the change brief is wrong and must be reversed** — the duplicated class limits
   are deliberate, not a smell. See *Corrections* §1.

Nothing here contradicts the slice's premise. The archive records the `.csproj` split as the
**pre-authorised escalation** for exactly this situation, deferred three times on schedule grounds
during a 3-week MVP — never on merit.

## Corrections to `change.md`

Three items in the change brief did not survive contact with the code.

### 1. The duplicated class limits must NOT be merged

`change.md` treats the `ClassEndpoints` ↔ `ClassTypeEndpoints` constant duplication as a smell to
consolidate into a shared `ClassLimits`. Read firsthand, it is the opposite —
`src/Application/Scheduling/ClassEndpoints.cs:198-207`:

> DUPLICATED FROM ClassTypeEndpoints ON PURPOSE, not shared through it. An occurrence may legitimately
> override its type's defaults (prd-v2 FR-008), so it cannot inherit the type's bounds by reference any
> more than it inherits its numbers — the whole point is that the two values are independent after
> creation. Keep the four constants in step by hand.

The other side acknowledges it too (`ClassTypeEndpoints.cs:84`, `:93`). All four pairs are equal today
(1, 480, 1, 200) — **no drift**.

**Resolution: leave both sets where they are.** (The strict reading is that sharing a `const`
constrains the *bounds*, not the *values* — but the decision is documented at both sites with a product
reason, and reversing it is not this slice's business.) Two further same-valued pairs —
`MaxNameLength`/`MaxDescriptionLength` in ClassType vs Exercise, and `MaxAttempts = 10` in Booking vs
TrainingPlan — are **parallel derivation from independent sources**, not shared rules. Also leave alone.

A full audit of code↔EF-configuration constant mirrors found **no drift anywhere**.

### 2. "Seven Application files use `UserManager`" — it is eight

`AuthEndpoints`, `MemberAdminEndpoints`, `ProfileEndpoints`, `PushEndpoints`, `BookingEndpoints`,
`ClassEndpoints`, `MyPlanEndpoints`, `TrainingPlanEndpoints`. Strengthens the decision to keep
`ApplicationUser` in `Domain`.

### 3. "Nothing under `src/app/` — that is S-19's territory" is not achievable

Two files inside the Angular workspace name the backend project path and **break**:

| File:line | Current | Breaks |
| --- | --- | --- |
| `src/app/package.json:15` | `cpSync('dist/app/browser', '../wwwroot', …)` in `e2e:stage` | writes into an orphaned directory |
| `src/app/playwright.config.ts:38` | `dotnet run --project ../po-prostu-silka.csproj --launch-profile http` | project no longer exists |

The failure mode of the first is silent: `e2e:stage` keeps succeeding, `dotnet run` serves an empty
`wwwroot`, and every route 404s with no error. **Decide in the plan** whether S-18 takes these two lines
(recommended — they are backend-path references that happen to live in the SPA workspace, and they are
in different files from everything S-19 touches) or whether a third micro-change owns them.

Related: **`src/wwwroot/` exists on disk right now** — 103 untracked files from a local `e2e:stage`.
After the move it is an orphan that must be deleted by hand, or a stale bundle lingers forever.
`git ls-files src/wwwroot` → 0 files, so nothing is committed and no `.gitignore` edit is needed
(`.gitignore:41` is `wwwroot/`, path-agnostic, and already covers `src/Api/wwwroot/`).

## Detailed Findings

### A. The reference graph is already acyclic (verified today, not from the 2026-09-10 survey)

`change.md` asserts the EF-Core rule holds "as of the 2026-09-10 survey". Eight days and four archived
changes have passed, so it was re-run at `e925de8`:

| Check | Result |
| --- | --- |
| `using po_prostu_silka.Infrastructure` in `Application`/`Domain` | none |
| `using po_prostu_silka.Application` in `Domain` | none |
| `using Microsoft.EntityFrameworkCore` \| `Microsoft.Data.SqlClient` in `Application`/`Domain` | none |
| `InternalsVisibleTo` anywhere | none |

`using` tally per layer (migrations excluded):

- **`src/Domain`** — three non-BCL imports total, one of which is `Microsoft.AspNetCore.Identity`
  (`ApplicationUser.cs` only).
- **`src/Application`** — `Microsoft.AspNetCore.Identity` ×8, `Microsoft.AspNetCore.Mvc` ×5
  (`[FromBody]`), `System.Security.Claims` ×8, `Microsoft.Extensions.Options` ×1. **Zero EF Core.**
- **`src/Infrastructure`** — EF Core ×57 plus the SqlClient, Azure and WebPush adapters.

### B. The implicit-usings trap — the highest-probability phase-1 stall

`src/obj/.../po-prostu-silka.GlobalUsings.g.cs` shows the Web SDK contributing nine global usings a
plain class library does not get: `Microsoft.AspNetCore.Builder`, `.Hosting`, `.Http`, `.Routing`,
`Microsoft.Extensions.Configuration`, `.DependencyInjection`, `.Hosting`, `.Logging`, and
`System.Net.Http.Json`.

Files that stop compiling without them (comment-only matches excluded by hand):

| Namespace | Application | Infrastructure | Domain |
| --- | --- | --- | --- |
| `Microsoft.AspNetCore.Http` (`IResult`, `Results`, `HttpContext`) | **14** | 0 | 0 |
| `Microsoft.AspNetCore.Routing` (`IEndpointRouteBuilder`) | **13** | 0 | 0 |
| `Microsoft.AspNetCore.Builder` (`MapGet`, `RequireAuthorization`) | **13** | 0 | 0 |
| `Microsoft.Extensions.Logging` | 3 | 5 | 0 |
| `Microsoft.Extensions.DependencyInjection` | 0 | 2 | 0 |
| `Microsoft.Extensions.Configuration` | 0 | 2 | 0 |
| `Microsoft.Extensions.Hosting` (`BackgroundService`) | 0 | 1 | 0 |

**Domain needs none of them** — it is a clean class library.

**There is no `Directory.Build.props`, `Directory.Packages.props` or root `.editorconfig`.** The shared
properties live once, in `src/po-prostu-silka.csproj:4-7`; without a `Directory.Build.props` they go
from 2 copies (src + tests) to 5. Recommendation: create `src/Directory.Build.props` for
`TargetFramework` / `Nullable` / `ImplicitUsings` plus the `<Using Include="…" />` items. Note
Application's set **shrinks** in later phases: once endpoints move to `Api`, `Routing` and `Builder`
become unnecessary and only `Http` remains for `IResult`.

⚠️ **Precedent:** `context/archive/2026-09-01-registration-and-approval/reviews/impl-review.md:90-113`
records an out-of-plan `.csproj` commit whose justification was disproved by an A/B of item sets and
which was dropped from history entirely. Every item in the four new files should carry a stated reason.

### C. The cut list

13 endpoint files, 6,276 lines, **36 routes**, 65 handler-shaped methods, 60 `Map*` calls. Sizes are
unchanged since 2026-09-10 — the four testing slices added tests, not endpoint code.

| File | Lines | Routes | Handlers | Projected files | Hardest part |
| --- | --- | --- | --- | --- | --- |
| `Scheduling/ClassEndpoints.cs` | 1082 | 8 | 8 | ~13 | `ToDto` shared with BookingEndpoints; `ResolveRange`, `Validate`, `ValidateInstructorAsync` have 2 callers each |
| `Members/MemberAdminEndpoints.cs` | 928 | 11 | 11 | ~12 | `ChangeTrainerRoleAsync` is one body behind two routes — keep as one file |
| `Auth/AuthEndpoints.cs` | 812 | 8 | 8 | ~9 | `RegisterAsync` is 243 lines (`:238-480`), the largest handler in the repo |
| `Scheduling/BookingEndpoints.cs` | 798 | 4 | 4 | ~9 | `TryBookAsync` (`:243-391`) is the no-overbooking protocol |
| `Training/TrainingPlanEndpoints.cs` | 746 | 5 | 5 | ~9 | 12 of 13 constants feed one `ValidateShape` |

Projected for these five alone: **36 handlers → ~33 use-case files + ~19 shared/port files ≈ 52 files.**

The eight small files: `ClassTypeEndpoints` (404, 6 routes), `ExerciseEndpoints` (512, 6 — **two groups
on the same `/api/admin/exercises` prefix split by policy**), `MembershipPassEndpoints` (477, 4),
`ProfileEndpoints` (127, 1), `TrainerEndpoints` (91, 1), `MyPassEndpoints` (84, 1), `MyPlanEndpoints`
(103, 2), `PushEndpoints` (112, 3 — the only surface with **no failure record**; it answers bare
`Results.BadRequest()`).

#### Three registration facts a phase plan must preserve

1. **`/api/admin/classes` is registered twice, in two files, under two different policies** —
   `ClassEndpoints.cs:224` (`Admin`) and `BookingEndpoints.cs:232` (`TrainerOrAdmin`). Splitting either
   file must not merge or reorder these groups.
2. **`BookingEndpoints`' inline `MayActOn` ownership check is enforced only by a comment**
   (`:217-231`) already ~350 lines from the check (`:590-608`). The group policy is *not* the whole
   authorization story there; moving handlers into separate files moves the check away from its only
   warning.
3. **`TrainingPlanEndpoints` requires `/members` (`:257`) before `/{id:guid}` (`:259`)** — documented at
   `:253-256`, currently masked by the `:guid` constraint. A per-file registration scheme must keep the
   order deterministic.

### D. Every coupling that must be severed

Two `internal` members reach across files, and each is the only `internal` member of its file:

| From | To | Site |
| --- | --- | --- |
| `BookingEndpoints` | `ClassEndpoints.ToDto` | `BookingEndpoints.cs:378` |
| `ProfileEndpoints` | `AuthEndpoints.BuildCurrentUserAsync` | `ProfileEndpoints.cs:125` |

**The larger web is the ports**, which a `ClassName.` grep does not see:

| Port / type | Declared in | Consumed from |
| --- | --- | --- |
| `IBookingStore` / `IBookingQuery` | `BookingEndpoints.cs:664/760` | `ClassEndpoints.cs:374,482,483,660,722`; `MemberAdminEndpoints.cs:435` |
| `IClassStore` | `ClassEndpoints.cs:1044` | `BookingEndpoints.cs:264,417,494,534` |
| `IMemberQuery` | `MemberAdminEndpoints.cs:897` | `AuthEndpoints.cs:272,325` |
| `IMembershipPassStore` | own file | `BookingEndpoints.cs:266,419,536,641` |
| `IClassTypeStore` | `ClassTypeEndpoints.cs:390` | `ClassEndpoints.cs:393` |
| `ITrainingPlanQuery` | `TrainingPlanEndpoints.cs:653` | `MyPlanEndpoints.cs:57,90` |
| `ExerciseSummary` (DTO) | `ExerciseEndpoints.cs:23` | `TrainingPlanEndpoints.cs:698` |
| `ClassBooking` (DTO) | `BookingEndpoints.cs:37` | `ClassChangeNotification.cs:54,69,82,99,167` |

**This is why ports and contracts get their own phase, before handlers.**

Two interfaces carry methods with no caller in their declaring file — `IMemberQuery.EmailExistsAsync`
(used only by `AuthEndpoints`) and `ITrainingPlanQuery.FindActiveForMemberAsync` /
`FindPlanExerciseAsync` (used only by `MyPlanEndpoints`). Both are correct; they just make the
one-file-per-context assumption false.

### E. Three behaviour-adjacent details CS-03 must rule on explicitly

1. **Logger categories are baked from class names.** `loggerFactory.CreateLogger(typeof(AuthEndpoints))`
   at `AuthEndpoints.cs:389`, `:452`, `:660`, and `typeof(ProfileEndpoints)` at `ProfileEndpoints.cs:114`.
   Moving those handlers **changes the log category string** — observable behaviour, if only in logs.
2. **Two dead injected parameters.** `BookingEndpoints.GetMineAsync`'s `UserManager<ApplicationUser>`
   (`:468`) and `TrainingPlanEndpoints.CreateAsync`'s `IMemberStore` (`:324`) are bound per request and
   never used. Dropping them is behaviour-neutral; keeping them is also defensible under a strict
   no-change rule. Decide once, in the plan.
3. **`AddHostedService<OutboxDeliveryWorker>()` must stay generic-typed.**
   `IntegrationTestFixture.cs:416-423` removes the worker by matching
   `ImplementationType == typeof(OutboxDeliveryWorker)` and uses `.Single(...)`. If an
   `AddInfrastructure()` extension registers it via a factory delegate, `ImplementationType` becomes
   `null`, `Single` throws, and **the test host fails to start**. The fixture comment says that failure
   is deliberate. This is the sharpest trap in the DI-extraction phase.

### F. Placement decisions for the non-endpoint Application files

Most stay in Application. The exceptions, with evidence:

| File | Goes to | Why |
| --- | --- | --- |
| `Auth/RateLimitPolicies.cs` | **Api** | Takes `HttpContext` (`:70`), reads `X-Forwarded-For`; consumed only by `Program.cs` and by route registration, which is itself moving to Api |
| `Auth/PasswordResetThrottle.cs` (class only) | **Infrastructure** | Stateful in-memory singleton — the role Infrastructure plays for every other adapter. The `IPasswordResetThrottle` interface stays in Application |
| `Notifications/OutboxOptions.cs` | **Infrastructure** | Read only by `OutboxDeliveryWorker` and `OutboxHealthCheck`; nothing in Application touches it |
| `Members/ClaimsPrincipalExtensions.cs` | **stays Application** | Looks Api-shaped, but `ClaimsPrincipal` is BCL (`System.Security.Claims`), not ASP.NET; all six callers are handlers |
| `Notifications/ClassChangeNotification.cs` | **stays Application** | A genuine application service — renders text, fans out over ports, never saves |
| `Members/MembershipPassRules.cs` | Application (Domain arguable) | The only "rules" type with no wire vocabulary; already referenced from a Domain doc-comment and an EF configuration |

`Infrastructure/Persistence/Configurations/*` consume `ContactDetails` and `MembershipPassRules` length
constants at compile time — Infrastructure → Application, which the layering table permits.

### G. What breaks outside `src/*.cs` — the hard-break list

Eleven items. Anything missed here is a broken build, test, or deploy.

| # | File:line | Current | Severity |
| --- | --- | --- | --- |
| 1 | `po-prostu-silka.slnx:3` | `<Project Path="src/po-prostu-silka.csproj" />` | build + CI `dotnet test` |
| 2 | `tests/po-prostu-silka.Tests/po-prostu-silka.Tests.csproj:25` | `<ProjectReference Include="..\..\src\po-prostu-silka.csproj" />` | tests |
| 3 | `.github/workflows/deploy.yml:54` | `mkdir -p src/wwwroot` | **production — SPA not served** |
| 4 | `.github/workflows/deploy.yml:55` | `cp -r src/app/dist/app/browser/. src/wwwroot/` | **production** |
| 5 | `.github/workflows/deploy.yml:70` | `dotnet publish src/po-prostu-silka.csproj` | **production** |
| 6 | `.github/workflows/deploy.yml:85` | `--project src/po-prostu-silka.csproj` (migrations script) | **production** |
| 7 | `.github/workflows/deploy.yml:123` | `--project src/po-prostu-silka.csproj` (database update) | **production — schema** |
| 8 | `src/app/package.json:15` | `cpSync('dist/app/browser', '../wwwroot', …)` | local e2e, silent failure |
| 9 | `src/app/playwright.config.ts:38` | `dotnet run --project ../po-prostu-silka.csproj` | local e2e |
| 10 | `src/po-prostu-silka.csproj:10-15` | `<Compile/Content/… Remove="app\**" />` | becomes dead — delete |
| 11 | `src/po-prostu-silka.http` (tracked) | named after the old project, sits at `src/` root | orphan |

`deploy.yml:67` (`dotnet test po-prostu-silka.slnx`) is solution-level and **survives automatically**
once the `.slnx` lists the four projects. The `env:` block (`:9-11`) holds Azure resource names, not
project names — must NOT be renamed.

Two non-obvious constraints:

- **Stale test `obj/bin` will produce a confusing failure.** `Microsoft.AspNetCore.Mvc.Testing` emits a
  `WebApplicationFactoryContentRootAttribute` into the *test* assembly keyed by the entry-point
  assembly name. It regenerates from the `ProjectReference`, so the rename is normally transparent —
  but a stale `obj/` holds the old key and yields "could not find a part of the path …\src". Either
  clean-build the test project, or set `<AssemblyName>po-prostu-silka</AssemblyName>` on the Api
  project to hold the key constant.
- **`Api` needs project references to both Application and Infrastructure**, not just Infrastructure:
  all 13 `Map*Endpoints()` live in Application, and the `Testing`-only probes at `Program.cs:399/:402`
  use `AuthorizationPolicies` from Infrastructure.

Docs stating paths or commands that must be updated: `AGENTS.md:15,21,25-27,29,31,35,43`;
`CLAUDE.md:6,9-12,16-18`; `README.md:54-61,78,80,87,91`; **`src/Application/README.md:10,18-19`** (easy
to miss — a doc living inside `src/`); `context/foundation/prd-v2.md:23-25`;
`context/foundation/roadmap.md:179,184-185`; `context/foundation/test-plan.md:268-269`;
`context/deployment/deploy-plan.md:26,29,31,56`. Note `AGENTS.md:57` is *already* stale — it claims no
CI workflow exists.

**17 `Mirrors … (path)` citations across 9 SPA files** name backend paths. **14 break** (every citation
naming an `*Endpoints.cs`); 3 survive (`ContactDetails.cs` ×2, `MembershipPassRules.cs` ×1) because
`src/Application/` remains a real folder. Worst case is `src/app/src/app/core/auth/auth.models.ts`,
with five citations to one file whose types will land in several places.

**31 archived files** would become stale. They are immutable by rule and **must not be edited**;
`context/foundation/lessons.md:31-46` already codifies the mitigation ("verify a prerequisite against
the code, not against an archived plan").

### H. Migrations: confirmed non-issue, with one gate

- `dotnet ef migrations list --no-build --no-connect` runs clean today and lists all **22** migrations,
  proving `AppDbContextFactory` resolves the context with no runtime configuration.
- Migration files already declare `namespace po_prostu_silka.Infrastructure.Persistence.Migrations`, so
  with `RootNamespace=po_prostu_silka.Infrastructure` and the folder preserved, **no migration file is
  content-edited and the next generated migration lands in the same namespace**.
- `Program.cs:45-48` passes only `EnableRetryOnFailure()` to `UseSqlServer` — **no `MigrationsAssembly`**,
  which is exactly why `AppDbContext` and `Persistence/Migrations/` must land in the same assembly.
- **No runtime `Migrate()`/`MigrateAsync()` exists in `src/`** — schema is applied only by `dotnet ef`
  in CI, so no startup path is at risk. (The archive argues the CI choice positively but never names
  startup migration as a rejected alternative — an undocumented gap, not a decision.)
- **Gate:** `dotnet ef migrations script --idempotent` before and after, written outside the repo, must
  be **byte-identical**; `dotnet ef migrations list` must show the same 22 in the same order.

### I. Tooling readiness (checked at `e925de8`)

| Tool | State |
| --- | --- |
| .NET SDK | `10.0.400` — matches `global.json` |
| `dotnet ef` | `10.0.11` installed globally — matches the CI pin, so the migration gate is runnable locally |
| Baseline build | `dotnet build po-prostu-silka.slnx` → **0 warnings, 0 errors**. No `TreatWarningsAsErrors`/`NoWarn` anywhere, so "warning-clean" is a real but manual bar |
| Docker | **not running** — `dotnet test` cannot execute locally until Docker Desktop is started |

## Architecture Insights

- **The endpoint file is the unit of co-location, by convention.** Every one bundles routes + DTOs +
  ports + validation + mapping + rules — a deliberate one-file-per-feature choice
  (`context/archive/2026-09-01-class-schedule-and-admin/plan.md:35-39`) that works at 100 lines and
  stops working at 1,000.
- **`IUnitOfWork` is the linchpin and the split cements it.** It exists *because* Application may not
  see EF Core: `TrySaveChangesAsync` absorbs `DbUpdateConcurrencyException` and `SaveOutcome`
  translates SqlException 2601/2627. The recorded constraint is directional
  (`context/archive/2026-09-03-class-booking-and-cancel/research.md:373-378`): *"If the booking plan
  needs to catch `DbUpdateException` … it needs a new seam on `IUnitOfWork`, or handling inside
  Infrastructure — not a `catch` in the endpoint."*
- **Authorization is declared per route group with no fallback policy**, so an endpoint is anonymous
  unless its group says otherwise. That is why re-creating every group is the slice's sharpest risk —
  and why `EndpointAuthorizationTests` was written before it.
- **The `{ reason }` failure contract is closed vocabulary, deliberately not ProblemDetails**, and is
  now pinned by tests on both sides — 16 `*Failure` records, 60 distinct codes
  (`context/archive/2026-09-13-testing-frontend-gate-and-contract/plan.md:32`).

## Historical Context (from prior changes)

- **The split is the pre-authorised escalation, deferred on schedule grounds — never on merit.**
  `context/archive/2026-08-31-persistence-foundation/plan-brief.md:32` — *"…folders, one csproj |
  …without project-splitting cost in a 3-week MVP"*; `plan.md:91` — *"**No project (`.csproj`)
  splitting.** Folders now, projects later if the boundary starts to rot"*; `plan-brief.md:103-104` —
  *"if it starts to rot, **the project split is the escalation**"*. Re-affirmed in
  `context/archive/2026-08-31-auth-identity-foundation/plan.md:91`.
- **`ApplicationUser` in `Domain` has an explicit "do not fix this" instruction.**
  `context/archive/2026-08-31-auth-identity-foundation/plan.md:201-204` — *"`IdentityUser` … lives in
  `Microsoft.AspNetCore.Identity` — not in `Microsoft.EntityFrameworkCore` … **Do not "fix" it by
  moving the type to `Infrastructure`**; the layering rule names EF Core specifically."*
- **No repository pattern, repeatedly and deliberately.**
  `context/archive/2026-09-04-exercise-library/research.md:473-477` — a generic repository *"would be
  **the deviation**"*. `I*Query` (AsNoTracking + projection) / `I*Store` (tracked, never saves) is the
  convention.
- **Where the interfaces live is NOT a recorded decision.** Every archive mention describes the
  bottom-of-file placement; no plan justifies it. Likewise nothing explains why `IMemberStore.cs` and
  `IMembershipPassStore.cs` got their own files while eleven others did not — undocumented drift in the
  two newest slices. **The refactor is free to choose here.**
- **`AppDbContextFactory` carries a mandate to explain itself.**
  `context/archive/2026-08-31-persistence-foundation/plan.md:309-323` — *"**Add a comment saying exactly
  that, or a future reader will try to 'fix' it.**"*
- **No archived change has ever moved files between layers at scale or renamed namespaces.** The only
  cross-layer move is one review fix (policy names → `Domain/AuthorizationPolicyNames.cs`,
  `context/archive/2026-09-01-registration-and-approval/reviews/impl-review.md:176`). There is no "what
  went wrong" history for this class of work.
- **`lessons.md` has two entries, both about plan-vs-code fidelity.** The second earned its keep here:
  an archived impl-review calls `IMembershipPassStore.FindManyAsync` dead code, but it has a live caller
  at `src/Infrastructure/Scheduling/BookingStore.cs:101`. Similarly the booking retry bound is **10**
  (`BookingEndpoints.cs:197`), not the 3 an archived plan-brief states.

## Related Research

- `context/foundation/test-plan.md` — all four phases `complete`; §3 risk #6 is literally *"A structural
  refactor silently changes an API contract the SPA depends on"*, and §2 names S-18 as the reason
  phases 1–3 were sequenced first.
- `context/archive/2026-09-11-testing-access-surface/` — produced `EndpointAuthorizationTests.cs`.
- `context/archive/2026-09-13-testing-frontend-gate-and-contract/` — pinned the 60 reason codes on both
  sides and added the SPA gate to CI.
- `context/archive/2026-09-03-class-booking-and-cancel/` — `SaveOutcome`, `DiscardChanges`, retry loop.

## Open Questions

1. **Who fixes `src/app/package.json:15` and `src/app/playwright.config.ts:38`?** They break with S-18
   but live in S-19's declared territory. Recommendation: S-18 takes them. Owner: plan. **Block: yes** —
   leaving them breaks the local e2e loop silently.
2. **Logger categories** (§E.1) — pin `typeof(AuthEndpoints)` deliberately, or accept the change?
   Owner: plan. Block: no.
3. **Dead injected parameters** (§E.2) — drop or keep under a strict no-change rule? Owner: plan. Block: no.
4. **`Directory.Build.props`** — create one at `src/`, or repeat properties across four `.csproj` files?
   Recommendation: create it. Owner: plan. Block: no.
5. **One file or two** for `ChangeTrainerRole`, `ForgotPassword`+`ResetPassword`, and
   `GetAccessCode`+`RevokeAccessCode`? Affects the file count by ~3. Owner: plan. Block: no.
6. **Docker must be running** before any phase gate can be verified locally. Owner: user. Block: yes,
   for verification only — not for planning.

## Verification commands

```bash
# Layering (must return nothing, before and after)
grep -rn "using Microsoft.EntityFrameworkCore" src/Application src/Domain --include=*.cs
grep -rn "using po_prostu_silka.Infrastructure" src/Application src/Domain --include=*.cs

# Migration equivalence (write outside the repo; the two files must be byte-identical)
dotnet ef migrations script --idempotent --project src/po-prostu-silka.csproj -o <scratch>/before.sql
dotnet ef migrations list --project src/po-prostu-silka.csproj --no-build --no-connect   # 22 migrations

# Phase gate
dotnet build po-prostu-silka.slnx -c Release      # baseline: 0 warnings, 0 errors
dotnet test po-prostu-silka.slnx                  # needs Docker; no test file may be edited

# The real route/policy gate — better than a route-literal grep
dotnet test --filter "FullyQualifiedName~EndpointAuthorizationTests"
```
