<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Persistence Foundation (F-01)

- **Plan**: `context/changes/persistence-foundation/plan.md`
- **Mode**: Deep
- **Date**: 2026-08-31
- **Verdict**: REVISE → **SOUND** (all 7 findings fixed)
- **Findings**: 2 critical, 3 warnings, 2 observations

## Verdicts

| Dimension | Verdict (at review) | After fixes |
|-----------|---------------------|-------------|
| End-State Alignment | PASS | PASS |
| Lean Execution | PASS | PASS |
| Architectural Fitness | WARNING | PASS |
| Blind Spots | FAIL | PASS |
| Plan Completeness | WARNING | PASS |

## Grounding

8/8 existing paths ✓, 6/6 new paths correctly absent ✓, 4/4 code anchors ✓, brief↔plan ✓,
Progress 36/36 items ✓ (39/39 after rework).

`docs/reference/contract-surfaces.md` and `context/foundation/lessons.md` are both absent — those
checks skipped.

## Findings

### F1 — Phase 1's commit auto-deploys EF Core to production before a database exists

- **Severity**: ❌ CRITICAL
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Blind Spots
- **Location**: Implementation Approach / Phase 1
- **Detail**: Branch is `main`; `.github/workflows/deploy.yml:4` triggers on every push to it
  (`ci_default_flow: auto-deploy-on-merge`). Phase 1 registered a `DbContext` against an empty
  connection-string placeholder, while Azure SQL and the App Service setting only arrived in Phase 2.
  Committing Phase 1 would have redeployed the live site with a `DbContext` that has nowhere to connect.
  The plan specified no branching strategy and never stated when each phase reaches production.
- **Fix A ⭐ Recommended**: Swap Phase 1 and Phase 2 — infrastructure first, then code.
  - Strength: Eliminates the window entirely with no branch discipline required; Phase 1 becomes pure
    infrastructure that cannot break the running app.
  - Tradeoff: Creates the ~$4.90/mo resource before EF Core is proven locally, inverting "fail cheap first".
  - Confidence: HIGH — trigger, branch, and missing connection string all directly verified.
  - Blind spot: Whether the app hard-crashes on an empty connection string or merely serves Unhealthy
    is unverified — an attempt to test it was blocked by a permission denial.
- **Fix B**: Run Phases 1–2 on a feature branch, merge once the DB exists.
  - Strength: Preserves the local-first ordering.
  - Tradeoff: Relies on discipline the plan doesn't encode; cuts against the documented auto-deploy story.
  - Confidence: MED.
  - Blind spot: Whether `/10x-implement`'s commit ritual also pushes is unchecked.
- **Decision**: FIXED via Fix A — phases swapped; "Implementation Approach" now explains the ordering,
  and a new "Critical Implementation Details" note records that every phase commit deploys.

### F2 — CI cannot generate the migration script: no design-time connection string

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 3 step 2 / Phase 1 step 6 (original numbering)
- **Detail**: `dotnet ef migrations script` builds the app host to resolve `AppDbContext`. On the runner
  `ASPNETCORE_ENVIRONMENT` is unset → Production → `appsettings.Development.json` never loads →
  `GetConnectionString("Default")` returns the empty placeholder, which `UseSqlServer` rejects. Phase 3's
  entire pipeline rests on that command.
- **Fix**: Add `IDesignTimeDbContextFactory<AppDbContext>` in `src/Infrastructure/Persistence/`.
  - Strength: Canonical EF Core answer; decouples design-time tooling from runtime config permanently.
  - Tradeoff: One more file whose purpose isn't self-evident without a comment.
  - Confidence: MED — mechanism certain; EF Core 10's exact empty-vs-null behaviour unverified.
  - Blind spot: If EF Core 10 tolerates a null connection string at design time, this drops from blocker
    to good practice.
- **Decision**: FIXED — added as Phase 2 change #5, with an explanatory note in "Critical Implementation
  Details" and a dedicated success criterion (2.7) that verifies it under the exact conditions CI runs in.

### F3 — `azure/sql-action@v2` Linux support is unverified

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Completeness
- **Location**: Phase 3, step 2, sub-step 5
- **Detail**: The workflow runs on `ubuntu-latest` (deploy.yml:12). The plan applied migrations.sql via
  `azure/sql-action@v2` without establishing Linux `.sql` support — the action's history is
  Windows-centric and current ubuntu images no longer ship mssql-tools. Failure would land at the last
  step of Phase 3, after all credential and firewall work.
- **Fix A ⭐ Recommended**: Apply with `dotnet ef database update`; keep the script as the artifact.
  - Strength: Removes an unverified dependency; the tool is already installed in the same job.
  - Tradeoff: The uploaded artifact is evidence of intent, not the literal executed bytes.
  - Confidence: HIGH.
  - Blind spot: None significant.
- **Fix B**: Keep sql-action but verify Linux support and pin the version first.
  - Strength: Applied and reviewed SQL stay byte-identical.
  - Tradeoff: Needs a research step; dependency must be re-checked on upgrades.
  - Confidence: MED.
  - Blind spot: sql-action's current Linux matrix unchecked.
- **Decision**: FIXED via Fix A — with the tradeoff recorded explicitly in the plan.

### F4 — Migrations land outside Infrastructure, contradicting the plan's own layering rule

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Architectural Fitness
- **Location**: Phase 1 step 7 vs step 3 (original numbering)
- **Detail**: Step 3 established "only Infrastructure may reference EF Core," but step 7 generated
  migrations to `src/Migrations/` at the project root. Every later slice inherits this location.
- **Fix**: Generate to `src/Infrastructure/Persistence/Migrations`.
- **Decision**: FIXED — output path changed and the rationale stated inline.

### F5 — `dotnet-ef` is never installed for local work

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1 step 7; criteria 1.5, 1.6 (original numbering)
- **Detail**: The local phase ran `dotnet ef` commands but the tool was only installed on the CI runner
  in Phase 3. Separately, `Microsoft.EntityFrameworkCore.Tools` serves Visual Studio's Package Manager
  Console, not the CLI.
- **Fix**: Add the global `dotnet-ef` install to the local phase; drop the `Tools` package.
- **Decision**: FIXED — install added to Phase 2 step 8; package list reduced from four to three with a
  note explaining the omission.

### F6 — `po-prostu-silka.http` still calls the deleted endpoint

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1, step 6 (original numbering)
- **Detail**: The plan deleted `/weatherforecast` but missed `src/po-prostu-silka.http:3`, the only live
  artifact referencing it. The `deploy-plan.md` and `roadmap.md` hits are historical audit records and
  must stay.
- **Fix**: Replace with `GET {{po_prostu_silka_HostAddress}}/health`.
- **Decision**: FIXED — added to Phase 2 step 7, with an explicit note not to touch the audit records.

### F7 — No test project exists, and none is planned

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Testing Strategy
- **Detail**: Testing is entirely manual and the repo has no test project. Defensible for plumbing, but
  S-04's no-overbooking guarantee — the roadmap's "load-bearing correctness work" — will need automated
  concurrency tests, so F-02 silently inherits the setup cost.
- **Fix**: Record the deferral explicitly as a handoff to F-02.
- **Decision**: FIXED — added to "What We're NOT Doing" and a "Handoff to F-02" block in Testing Strategy.
