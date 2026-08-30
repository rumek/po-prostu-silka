---
bootstrapped_at: 2026-08-30T20:38:30Z
starter_id: dotnet
starter_name: ".NET (ASP.NET Core webapi)"
project_name: po-prostu-silka
language_family: dotnet
package_manager: dotnet
cwd_strategy: subdir-then-move
bootstrapper_confidence: verified
phase_3_status: ok
audit_command: "dotnet list package --vulnerable"
---

## Hand-off

```yaml
starter_id: dotnet
package_manager: dotnet
project_name: po-prostu-silka
hints:
  language_family: dotnet
  team_size: solo
  deployment_target: azure-app-service
  ci_provider: github-actions
  ci_default_flow: auto-deploy-on-merge
  bootstrapper_confidence: verified
  path_taken: custom
  quality_override: false
  self_check_answers:
    typed: true
    from_official_starter: true
    conventions: true
    docs_current: true
    can_judge_agent: true
  has_auth: true
  has_payments: false
  has_realtime: false
  has_ai: false
  has_background_jobs: true
```

### Why this stack

A solo builder shipping a gym booking-and-training-plans web app in 3 weeks chose the custom path to pin the stack they can judge: ASP.NET Core Web API (C#, EF Core with SQL Server) plus an Angular SPA as two sibling projects in one repository. Both halves clear all four agent-friendly gates — typed end-to-end (C# + TypeScript), convention-based official templates (`dotnet new webapi`, `ng new`), popular within their families, with current versioned docs. Auth (email+password, admin approval, block/unblock) maps to ASP.NET Core Identity; email + push notification delivery on class cancel/change maps to .NET hosted background services; the no-overbooking guardrail leans on SQL Server transactions via EF Core. Deployment targets Azure App Service (the starter default, and the natural home for ASP.NET Core + Azure SQL), with CI on GitHub Actions auto-deploying on merge. The five-point self-check came back clean, so no quality override is recorded; the accepted trade-off is two codebases of surface for a 3-week solo MVP, taken because .NET + Angular is where the builder is productive.

## Pre-scaffold verification

| Signal      | Value   | Severity | Notes                                                                                       |
| ----------- | ------- | -------- | ------------------------------------------------------------------------------------------- |
| npm package | not run | —        | non-JS starter (language_family: dotnet); no npm-distributed CLI in cmd_template             |
| GitHub repo | not run | —        | card docs_url (https://learn.microsoft.com/aspnet/core) is not a github.com/<owner>/<repo> URL |

No recency signal available for this starter. The scaffold CLI is the locally installed .NET SDK (10.0.100-rc.2.25502.107), so recency of a remote package is not a meaningful signal here anyway.

## Scaffold log

**Resolved invocation**: `dotnet new webapi -n .bootstrap-scaffold --no-restore`
**Strategy**: subdir-then-move
**Exit code**: 0
**Files moved**: 6 (`po-prostu-silka.csproj`, `po-prostu-silka.http`, `Program.cs`, `Properties/launchSettings.json`, `appsettings.json`, `appsettings.Development.json`)
**Conflicts (.scaffold siblings)**: none
**.gitignore handling**: absent in scaffold (the `dotnet new webapi` template ships no `.gitignore`; run `dotnet new gitignore` to add the official one)
**.bootstrap-scaffold cleanup**: left in place (empty directory; deletion blocked by an external file lock — "Device or resource busy" persisted across retries with no dotnet/MSBuild process running; safe to delete manually once whatever process holds the handle releases it)

**Run notes**:

- The temp-directory substitution (`{name}=.bootstrap-scaffold`) leaked into the generated artifact names, as `dotnet new -n` uses the name for the project file and identifiers. Before move-up, bootstrapper normalized these to what the template would have produced for the hand-off's `project_name`: renamed `.bootstrap-scaffold.csproj` → `po-prostu-silka.csproj`, `.bootstrap-scaffold.http` → `po-prostu-silka.http`, and replaced the sanitized identifier `_bootstrap_scaffold` → `po_prostu_silka` in `<RootNamespace>` (csproj) and the `.http` host-address variable (3 content occurrences).
- `dotnet restore po-prostu-silka.csproj` was run after move-up (exit 0) because the post-scaffold audit (`dotnet list package --vulnerable`) requires resolved dependency assets and the card template scaffolds with `--no-restore`.

## Post-scaffold audit

**Tool**: `dotnet list package --vulnerable --include-transitive`
**Summary**: 0 CRITICAL, 1 HIGH, 0 MODERATE, 0 LOW
**Direct vs transitive**: 0/0/0/0 direct of total 0/1/0/0 — the single finding is transitive

#### CRITICAL findings

None.

#### HIGH findings

- **Microsoft.OpenApi 2.0.0** (transitive, pulled in by the webapi template's OpenAPI integration) — advisory [GHSA-v5pm-xwqc-g5wc](https://github.com/advisories/GHSA-v5pm-xwqc-g5wc). Also surfaced as NuGet warning NU1903 at restore time. Fix: pin a patched `Microsoft.OpenApi` version via an explicit `PackageReference` once the upstream `Microsoft.AspNetCore.OpenApi` package updates, or add the pinned transitive reference now.

#### MODERATE findings

None.

#### LOW / INFO findings

None.

## Hints recorded but not acted on

| Hint                    | Value                                                                                  |
| ----------------------- | -------------------------------------------------------------------------------------- |
| bootstrapper_confidence | verified                                                                                |
| quality_override        | false                                                                                   |
| path_taken              | custom                                                                                  |
| self_check_answers      | typed: true, from_official_starter: true, conventions: true, docs_current: true, can_judge_agent: true |
| team_size               | solo                                                                                    |
| deployment_target       | azure-app-service                                                                       |
| ci_provider             | github-actions                                                                          |
| ci_default_flow         | auto-deploy-on-merge                                                                    |
| has_auth                | true                                                                                    |
| has_payments            | false                                                                                   |
| has_realtime            | false                                                                                   |
| has_ai                  | false                                                                                   |
| has_background_jobs     | true                                                                                    |

Note for the future reader: the hand-off's `## Why this stack` describes a two-project custom path (ASP.NET Core Web API + Angular SPA as siblings in one repo). This run scaffolded the .NET Web API half via the registry card's `cmd_template`; the Angular SPA (`ng new`) is a manual follow-up outside bootstrapper v1's scope.

## Next steps

Next: a future skill will set up agent context (CLAUDE.md, AGENTS.md). For now, your project is scaffolded and verified — happy hacking.

Useful manual steps in the meantime:
- `git init` (if you have not already) to start your own repo history.
- `dotnet new gitignore` — the webapi template ships no `.gitignore`.
- Delete the leftover empty `.bootstrap-scaffold/` directory once the file lock releases.
- Scaffold the Angular SPA half of your custom path (`ng new`) as a sibling project when ready.
- Address the HIGH transitive finding (Microsoft.OpenApi 2.0.0) per your project's risk tolerance — the full breakdown is above.
