<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Persistence Foundation (F-01)

- **Plan**: context/changes/persistence-foundation/plan.md
- **Scope**: Full plan — Phases 1, 2 and 3 (all complete)
- **Date**: 2026-08-31
- **Verdict**: APPROVED
- **Findings**: 0 critical, 1 warning, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

## Evidence gathered

Automated criteria re-run during this review:

| Check | Result |
|---|---|
| `dotnet nuget list source` (repo dir) | nuget.org only — private feeds excluded (2.1) |
| `dotnet restore src/po-prostu-silka.csproj` | succeeds |
| `dotnet build -c Release` | 0 warnings, 0 errors (2.2) |
| `dotnet list package --vulnerable --include-transitive` | clean, incl. the previously-accepted `Microsoft.OpenApi` risk (2.3) |
| `docker compose ps` | `pps-sql-local` up, healthy, 1433 published and open (2.4) |
| `dotnet ef migrations script --idempotent` under `ASPNETCORE_ENVIRONMENT=Production` | succeeds — design-time factory bypasses runtime config as designed (2.7) |
| `dotnet ef migrations script InitialSchemaMarker 0` | generates `DROP TABLE [SchemaMarkers]` + history delete — `Down` is real and reverses `Up` (2.6) |
| `curl https://po-prostu-silka.azurewebsites.net/health` | 200 `Healthy` (2.12, 3.6) |
| `curl https://.../` and an unmapped route | both 200, Angular shell (3.7) |

Criteria that could not be independently re-verified here (no `gh` CLI on this machine; Azure CLI
2.35.0 unauthenticated): the Phase 1 `az sql db show` tier assertions, and Phase 3's in-run
assertions (3.1–3.5, 3.8–3.12). Git history corroborates 3.8/3.9 — commit `11318ba` introduced a
deliberately broken migration and `c586717` reverted it, which is the recorded pipeline-failure
test. Live `/health` returning 200 is end-to-end proof the App Service reaches Azure SQL.

## Findings

### F1 — Firewall cleanup swallows every failure, not just "already absent"

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: .github/workflows/deploy.yml:104-107
- **Detail**: The `if: always()` guard is correctly present, but the delete command ends with
  `--output none || echo "Rule already absent; nothing to clean up."`. That `||` catches *every*
  non-zero exit, not only "rule not found". If the rule was genuinely created and the delete then
  fails for a real reason — Azure token expiry after a long migration step, a transient ARM error,
  an RBAC gap that permits create but not delete — the step still exits 0, the job goes green, and
  the runner's public IP stays permanently allow-listed on a database that will hold members'
  personal data. The one failure this masking is *meant* to cover is exactly the harmless case;
  every case it accidentally covers is the dangerous one. The plan called this the
  security-critical step ("otherwise a failed migration leaves the runner's IP permanently allowed
  on a database holding members' personal data") — the `if: always()` half of that intent landed,
  the error-handling half did not.
- **Fix**: Distinguish not-found from real failures — capture the delete's stderr, ignore only a
  `ResourceNotFound` / "could not be found" match, and fail the step (or at minimum emit
  `::error::`) on anything else, so a leaked rule is never reported as a clean run.
  - Strength: Preserves tolerant behaviour for the skipped-rule case while restoring a real signal
    for the case the step exists to prevent. Localized to one `run:` block.
  - Tradeoff: A failed cleanup would now redden a run whose deploy may have succeeded — the correct
    signal, but a green run costs slightly more attention to achieve.
  - Confidence: HIGH — the masking behaviour is plain in the shell semantics; no Azure round-trip
    needed to confirm it.
  - Blind spot: Have not confirmed the exact `az` error string/exit code for a missing rule on CLI
    2.35.0, so the not-found match needs verifying against a real run before relying on it.
- **Decision**: SKIPPED — reviewer judged the masked-failure window acceptable for now.

### F2 — Layering rule as written has no composition-root carve-out

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/Program.cs:1,14 (rule text in AGENTS.md and CLAUDE.md)
- **Detail**: `src/Program.cs` opens with `using Microsoft.EntityFrameworkCore;` and calls
  `AddDbContext<AppDbContext>` at line 14. `Program.cs` sits at `src/` root, outside the three
  layer folders, and both AGENTS.md and CLAUDE.md state that `Infrastructure` "is the **only**
  layer that may reference EF Core" with no stated exception. The implementation is correct and
  idiomatic — the composition root has to call `AddDbContext` somewhere, and `Domain/` and
  `Application/` were verified to contain no EF Core `using` at all, so the boundary that matters
  is intact. The gap is in the rule text, not the code. Left as-is, every future review re-flags
  this same line.
- **Fix**: Add a one-line carve-out to the layering rule in AGENTS.md and CLAUDE.md — the
  composition root (`Program.cs`) may reference EF Core solely to register `AppDbContext`.
- **Decision**: DEFERRED — carve-out to be written later.

### F3 — Three additions not itemized in the plan

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: src/Application/README.md; docker-compose.yml:19,24-33; .github/workflows/deploy.yml:96
- **Detail**: Three things exist that no plan contract names. (a) `src/Application/README.md` —
  `Application/` is the one layer with zero code this phase, and git cannot track an empty
  directory, so this is the minimum placeholder needed to commit the folder the plan *did* require;
  its content restates the layering rule. `Domain/` and `Infrastructure/` correctly have no such
  file, because they contain real code. (b) `docker-compose.yml` adds `MSSQL_PID: "Developer"` and
  a `sqlcmd`-based healthcheck beyond the plan's image/EULA/password/port/volume contract — both
  local-dev-only conveniences. (c) `dotnet ef database update` carries
  `--project src/po-prostu-silka.csproj`, which the plan's contract text omitted but which the
  command needs to resolve the csproj from the repo root, matching the `migrations script` step
  above it. None violate the "What We're NOT Doing" list: no repositories, aggregates, dispatcher
  or unit-of-work; no Managed Identity; no `.csproj` split; no App Insights; no slots; no test
  project; one table only.
- **Fix**: No code change — record them in the plan as a short addendum so the next review does not
  re-investigate them.
- **Decision**: DEFERRED — to be recorded later, once real entities exist.

### F4 — No propagation wait between opening the firewall and applying migrations

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: .github/workflows/deploy.yml:77-97
- **Detail**: `Apply migrations to Azure SQL` runs immediately after `Open SQL firewall for this
  runner` with no wait or retry. Azure SQL firewall-rule propagation is normally fast but is not
  contractually instantaneous, so a rare race could fail the migration step on a rule that was in
  fact created. The consequence is a spurious red run rather than anything unsafe — the
  `if: always()` cleanup still fires — so this is a flakiness note, not a defect.
- **Fix**: If ever observed in practice, wrap `dotnet ef database update` in a short bounded retry
  (e.g. 3 attempts, 10s apart). Not worth pre-emptive hardening today.
- **Decision**: SKIPPED — flakiness note only; harden if ever observed.
