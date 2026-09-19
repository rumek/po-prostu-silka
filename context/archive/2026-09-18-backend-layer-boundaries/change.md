---
change_id: backend-layer-boundaries
title: Compiler-enforced layer boundaries and thin endpoint classes
status: archived
created: 2026-09-18
updated: 2026-09-19
archived_at: 2026-09-19T14:45:42Z
---

## Notes

M-6's first slice (roadmap S-18, and M-6's north star). `AGENTS.md` describes a layering table and
calls it "convention — not compiler-enforced"; this slice makes the compiler enforce it, and moves the
logic out of the thirteen `*Endpoints` classes on the way.

The anchors are `CS-01`–`CS-03` in `context/foundation/roadmap.md`. The proof that this slice worked
is one sentence: **adding `using Microsoft.EntityFrameworkCore;` to a file in `Application` must fail
the build.** Today that same edit compiles and leaves no trace.

### What the survey found (2026-09-10, recorded in the roadmap's `## Baseline`)

- `src/` is ONE `.csproj` with `Domain`, `Application` and `Infrastructure` as folders.
- Thirteen `*Endpoints` classes live under `src/Application/` and each carries seven concerns at once:
  DTO contracts, `I*Store`/`I*Query` port definitions, hand-rolled validation, mapping, business
  rules, resource-level authorization, and limit constants. Largest: `ClassEndpoints.cs` (1082),
  `MemberAdminEndpoints.cs` (928), `AuthEndpoints.cs` (812), `BookingEndpoints.cs` (798),
  `TrainingPlanEndpoints.cs` (746).
- The EF Core rule is currently SATISFIED — the only hits in `Domain`/`Application` are prose in
  comments. Nothing checks it: not the compiler (one assembly), not an analyzer, not a test.

### Calls settled before planning

> **Research landed 2026-09-18** — see `research.md`. It reversed one decision below (the limit
> constants must NOT be merged), corrected a count, and found that this slice cannot avoid touching two
> files under `src/app/`. Where the two documents disagree, `research.md` is the later evidence.

1. **Four projects**, not two or three: `Domain`, `Application`, `Infrastructure`, `Api`. Four is the
   smallest set that enforces the table in `AGENTS.md` as written — fewer leaves "Domain references
   nothing" unenforced, which is the rule most likely to rot next.
2. **Namespaces do not move.** Every file already declares `po_prostu_silka.Domain.*` /
   `.Application.*` / `.Infrastructure.*`. With a matching `RootNamespace` per project and the folder
   structure preserved, not one `using` changes — in `src/` or in the 23 test files that import those
   namespaces. This is a file move, not a rename refactor.
3. **Migrations stay in the same assembly as `AppDbContext`** (both go to `Infrastructure`), so
   `MigrationsAssembly` is not needed and no migration is regenerated, squashed, or content-edited.
   The migration files' namespace is already `po_prostu_silka.Infrastructure.Persistence.Migrations`,
   which is exactly what EF will derive for the next one too.
4. **`ApplicationUser : IdentityUser` stays in `Domain`**, which gains one package:
   `Microsoft.Extensions.Identity.Stores` (where `IdentityUser` actually lives — it is not EF Core).
   The alternative would force an `IUserAccounts` port covering registration, sign-in, reset tokens,
   security stamps, roles and claims, because `UserManager<ApplicationUser>` appears in 7 Application
   files. That is a bigger and riskier slice than this one, for no operational gain.
   **Consequence to record in `AGENTS.md`:** "Domain references nothing" stops being true.
5. **Handlers keep returning `IResult`**, as `public static` methods bound by method group. Converting
   to a result union would rewrite every `Results.Json(new XFailure("reason_code"), …)` in thirteen
   files — and those reason strings are mirrored field-for-field by the SPA's discriminated unions.
   `IResult` comes from the ASP.NET Core shared framework, not EF Core; the rule this slice enforces
   is about EF Core specifically.
6. **No architecture test.** After the split the compiler refuses the violation, so `NetArchTest`
   would assert the same thing more slowly. The one residual gap — someone adding an EF-Core-bearing
   package to Application's `.csproj` — is a visible, reviewable csproj diff. If a guard is wanted
   later it is a ~30-line csproj-reading test in the existing test project, and it belongs in the last
   phase, never the first.

### Hard constraints

- **No behaviour changes** (CS-03). No route moves, no `reason` code added or renamed, no schema
  change. The gate for every move phase: full `dotnet test` green with **no test file edited**, and a
  route-literal diff (`grep -ho '"/api/[^"]*"'` over the endpoint files, sorted) that is empty.
- **`.github/workflows/deploy.yml` changes in the same commit as the split.** It names
  `src/po-prostu-silka.csproj` in the publish step and in both `dotnet ef` invocations, and stages the
  Angular build into `src/wwwroot`. All four paths cease to exist. This is the only place this slice
  can break production.
- **`Microsoft.EntityFrameworkCore.Design` goes in TWO projects** — `Infrastructure` (migrations
  output) and `Api` (`dotnet ef` startup project) — each with `PrivateAssets="all"`, which does not
  flow transitively. Omitting it from `Api` produces the "your startup project doesn't reference
  Microsoft.EntityFrameworkCore.Design" error; expect it exactly once.
- **Migration-equivalence gate:** before the split, `dotnet ef migrations script --idempotent` into a
  file outside the repo; after, the same with the new flags. The two must be **byte-identical**, and
  `dotnet ef migrations list` must show the same 22 in the same order.
- **Use `git mv` throughout** so rename detection keeps `git log --follow` and `git blame` intact. The
  codebase is 55–70% comments, so move phases produce diffs that look enormous while changing nothing;
  the only workable review question is "did any non-comment line change?".
- **`AGENTS.md` and `CLAUDE.md` update in the first phase**, not as a follow-up. Both state the
  one-project layering and the old `dotnet build` / `dotnet run` / `dotnet ef` invocations; in an
  agent-driven repo a stale command is a defect, because the next agent will run it.

### Not in scope, deliberately

MediatR, FluentValidation, AutoMapper, a repository pattern; replacing `IResult` with a result union;
`AddProblemDetails` or exception-handler middleware (it would change the error shape the SPA parses —
that is a coordinated frontend change); enriching `Domain` or moving invariants into entities; moving
`ApplicationUser` out of `Domain`; renaming a route or an assembly; squashing or regenerating
migrations; Central Package Management (the natural follow-up once there are four package lists to
keep in sync); anything under `src/app/` — that is S-19's territory, and the two slices are designed to
share no files so they can run at the same time.
