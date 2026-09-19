# Compiler-enforced layer boundaries and thin endpoint classes — Plan Brief

> Full plan: `context/changes/backend-layer-boundaries/plan.md`
> Research: `context/changes/backend-layer-boundaries/research.md`

## What & Why

`AGENTS.md` describes a three-layer architecture and calls it "convention — not compiler-enforced". A
rule nothing checks is not a rule. This slice splits `src/` into four projects so the build refuses the
violation, and moves the logic out of thirteen `*Endpoints` classes on the way. The proof is one
sentence: **adding `using Microsoft.EntityFrameworkCore;` to a file in `Application` must fail the
build.** Today that same edit compiles and leaves no trace.

## Starting Point

One `Microsoft.NET.Sdk.Web` project with `Domain`, `Application` and `Infrastructure` as folders. The
dependency graph is already acyclic and already correct — verified at `e925de8`, with zero EF Core
imports outside `Infrastructure`. Nothing enforces it. Thirteen `*Endpoints` files hold 6,276 lines, 36
routes and 65 handlers, each file carrying seven concerns at once; the largest is 1,082 lines.

## Desired End State

Four `.csproj` files under `src/`. An EF Core import in `Application` or `Domain` fails compilation with
CS0234. Each `*Endpoints` class lives in `Api` and contains route registration and nothing else; every
handler body has its own file under `Application/<Context>/`. Nothing a member or an admin can see
changes, and no test file was edited to make it pass.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| Number of projects | Four (`Domain`, `Application`, `Infrastructure`, `Api`) | Smallest set that enforces the table as written | Change brief |
| Namespaces | Do not move | `RootNamespace` per project + preserved folders ⇒ zero `using` changes anywhere | Change brief / Research |
| `ApplicationUser` | Stays in `Domain` | `IdentityUser` is Identity, not EF Core; an `IUserAccounts` port would be a bigger, riskier slice | Change brief |
| Migrations | Same assembly as `AppDbContext` | No `MigrationsAssembly` in `Program.cs`; no migration regenerated or edited | Change brief / Research |
| Handler return type | Stays `IResult` | 60 `reason` codes are mirrored field-for-field by the SPA and pinned by tests | Change brief |
| Architecture test | None | The compiler refuses the violation; `NetArchTest` would assert it more slowly | Change brief |
| Class-limit constants | Stay duplicated | Documented as deliberate at both sites (prd-v2 FR-008); research reversed the brief here | Research |
| Two `src/app/` lines | S-18 takes them | One of them fails **silently** — e2e stages into an orphan and every route 404s | Plan |
| Scope | Full CS-01 + CS-02 | All thirteen files; a half-done split is worse than either end state | Plan |
| Logger categories | Pinned (`typeof(AuthEndpoints)` kept) | CS-03 is literal — the category string is observable behaviour | Plan |
| Dead injected params | Dropped | The signature is being rewritten anyway; carrying them forward makes them look intentional | Plan |
| Route registration | `*Endpoints` → `Api`, handlers → `Application` | Only layout where `Application` stops needing ASP.NET routing at all | Plan |
| Port file layout | One file per interface | Matches what the two newest slices already chose | Plan |
| DTO placement | All 49 records leave the endpoint files in phase 2 | 13 are consumed from `Infrastructure`; leaving them would make phase 7 a reference cycle | Plan review |
| MSBuild properties | `src/Directory.Build.props`, `<Using>` per project | Shared props once; `Domain` inherits no ASP.NET namespaces | Plan |
| Use-case boundary | The handler body | Mechanical rule; never splits a shared body, which would be a behaviour change | Plan |
| `Api` assembly name | Pinned to `po-prostu-silka` | Holds the test content-root key **and** the publish artifact name | Plan |

## Scope

**In scope:** four `.csproj` + `Directory.Build.props`; `git mv` of the whole backend; the five
`deploy.yml` lines and two `src/app/` lines that name backend paths; extraction of nine cross-context
ports, all 49 DTO records and two `internal` members out of the endpoint files; handler split for all
thirteen endpoint files;
`AGENTS.md` / `CLAUDE.md` / `README.md` / foundation-doc path updates.

**Out of scope:** MediatR, FluentValidation, AutoMapper, a repository pattern; replacing `IResult`;
`AddProblemDetails`; enriching `Domain`; moving `ApplicationUser`; merging the duplicated class limits;
squashing migrations; Central Package Management; anything else under `src/app/` (S-19's territory);
editing `context/archive/`.

## Architecture / Approach

`Domain` (no references) ← `Application` (Domain) ← `Infrastructure` (Domain + Application, the only
project that may see EF Core), with `Api` (Application + Infrastructure) as the Web SDK host. Phase 1 is
a pure `git mv` plus four project files — no `.cs` content changes, which is what makes its diff
reviewable. Every declaration comes out next — nine ports and 49 DTO records — because the endpoint
files themselves move to `Api` at the end, and anything still declared inside one would move with it;
thirteen of those records are consumed from `Infrastructure`, which `Api` already references, so leaving
them would close a reference cycle. Then handlers move out of the endpoint files, pilot first. Finally the
thin registration classes move to `Api`, which is what lets `Application` drop
`Microsoft.AspNetCore.Routing` and `.Builder` from its usings — the boundary becoming real rather than
nominal.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Four projects | CS-01 proved; deploy + e2e paths fixed | `deploy.yml` — the only place this slice can break production |
| 2. Ports and contracts | Nine ports and all 49 DTO records in their own files | A declaration left behind rides into `Api` in phase 7 and breaks the build |
| 3. Handler split — pilot | 5 small files, 8 routes; the convention | Wrong convention, caught here rather than 11 files later |
| 4. Handler split — Members | MemberAdmin (11 routes), MembershipPass | `ChangeTrainerRole`: one body, two routes |
| 5. Handler split — Scheduling | Class, Booking, ClassType | `/api/admin/classes` twice under two policies; `MayActOn` loses its warning |
| 6. Handler split — Auth + Training | Auth, TrainingPlan, Exercise; CS-02 complete | Route order `/members` before `/{id:guid}`; 243-line `RegisterAsync` |
| 7. Registration moves to Api | Thin `*Endpoints` in `Api`; usings shrink | `AddHostedService<OutboxDeliveryWorker>()` must stay generic-typed or the test host will not start |

**Prerequisites:** S-17 done and no product slice in flight (phase 1 renames essentially the whole
backend; any concurrent branch under `src/` conflicts badly). Docker Desktop running — `dotnet test`
cannot execute locally without it. `dotnet ef` 10.0.11 installed, matching the CI pin.

**Estimated effort:** ~6–8 sessions across 7 phases. Phases 1 and 5 are the two long ones.

## Open Risks & Assumptions

- **The implicit-usings trap.** 14 Application and 10 Infrastructure files compile today only because
  the Web SDK contributes nine global usings a class library does not get. This is the likeliest cause
  of a "hundreds of errors" phase-1 stall — and it is fixable in one file.
- **`Application` carries no NuGet package at all.** `UserManager` (×28) and `SignInManager` (×5) ship
  in the ASP.NET Core shared framework, so a single `FrameworkReference` covers them along with
  `[FromBody]` and `IResult`. If the phase-1 build disagrees, the fix is one line — but the expectation
  is a package list of zero, which is itself a useful signal that the layer is clean.
- **Review fatigue is a real risk.** The codebase is 55–70% comments, so move phases produce diffs that
  look enormous while changing nothing. The only workable review question is "did any non-comment line
  change?".
- **31 archived files go stale** and must not be edited (immutable by rule). `lessons.md` already
  codifies the mitigation: verify a prerequisite against the code, never against an archived plan.
- **14 `Mirrors … (path)` citations in the SPA break.** Deliberately left to S-19 or later; they are
  comments, not behaviour.

## Success Criteria (Summary)

- `using Microsoft.EntityFrameworkCore;` in an `Application` file fails `dotnet build` with CS0234.
- `dotnet test po-prostu-silka.slnx` green at every phase gate, with **no test file edited** — and
  `EndpointAuthorizationTests` green specifically, since it was written for this slice.
- `dotnet ef migrations script --idempotent` is byte-identical before and after, and the same 22
  migrations list in the same order.
