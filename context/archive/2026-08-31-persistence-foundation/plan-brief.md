# Persistence Foundation (F-01) — Plan Brief

> Full plan: `context/changes/persistence-foundation/plan.md`

## What & Why

Give the app a database. Roadmap item **F-01** provisions Azure SQL (Basic DTU), wires EF Core with a
bootstrapped `AppDbContext`, and makes schema migrations run through CI on every deploy. It is the head
of the dependency graph for milestone `first-usable-mvp` — F-02, F-03, and all nine slices are blocked
on it. It ships **plumbing plus one proving migration**, not the application schema.

## Starting Point

The app is live at `po-prostu-silka.azurewebsites.net` with zero data surface: one NuGet package
(`Microsoft.AspNetCore.OpenApi`), a `Program.cs` still serving the `/weatherforecast` template sample,
no `ConnectionStrings` section, and a deploy workflow with no database step. Azure App Service, Always
On, and HTTPS Only are all live and verified; Azure SQL was deliberately deferred to this change.

## Desired End State

An Azure SQL Basic DTU database sits in `pps-rg` alongside the App Service. Pushing to `main` generates
an idempotent migration script, applies it to the database, and only then deploys the new code — so a
bad migration fails the pipeline with the old code still serving. `GET /health` on the live site returns
`Healthy`, proving the running app opened a real connection. Local development runs the same schema
against a SQL Server container.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Migration execution | CI applies an idempotent SQL script before deploy | Fails the pipeline before bad code goes live, and produces the reviewable artifact `infrastructure.md`'s human-approval policy needs. |
| Code structure | Domain/Application/Infrastructure **folders**, one csproj | Establishes the DDD boundaries later slices inherit without project-splitting cost in a 3-week MVP. |
| First migration | One trivial `SchemaMarker` table | Proves the pipeline against something that can actually fail on permissions or collation, which an empty migration cannot. |
| DB authentication | SQL auth now, Managed Identity as follow-up | Works immediately from app, CI, and local dev; matches the pragmatic tradeoff already accepted for CI auth. |
| Local database | SQL Server in Docker | Real engine, so the row-locking behaviour S-04's no-overbooking guarantee depends on matches production. |
| NuGet sources | Commit a public-only `nuget.config` | This machine has two private feeds active and F-01 is the change that adds four packages — the predicted CI break becomes possible here. |
| Verification | `/health` with a live DB probe; delete `/weatherforecast` | One honest yes/no on connectivity for every later slice and for CI smoke checks. |
| Provisioning | Agent runs `az`, human confirms the tier flag | Puts the gate exactly where the pre-mortem says the money is lost — Basic vs. free serverless. |
| Phase order | Infrastructure **before** code (plan review, F1) | Pushes to `main` auto-deploy, so landing the EF wiring before the database exists would take the live site down. |
| Migration applier | `dotnet ef database update` in CI (plan review, F3) | `azure/sql-action`'s `.sql` support on Linux runners is unverified; the EF tool is already installed in the same job. |

## Scope

**In scope:** public-only `nuget.config`; EF Core 10.0.11 packages; Domain/Application/Infrastructure
folders; `AppDbContext` + one marker entity + first migration; `docker-compose.yml` for local SQL
Server; `/health` endpoint and removal of the `/weatherforecast` sample; Azure SQL server + Basic DTU
database; connection string in App Service; CI service principal, JIT firewall rule, and
migration-before-deploy pipeline; `deploy-plan.md` updated.

**Out of scope:** any application schema (Identity, members, classes, bookings, plans, outbox);
repositories, aggregates, and domain-event dispatch; splitting into separate `.csproj` projects;
Managed Identity for SQL; App Insights; Azure budget alert; `az` CLI upgrade.

## Architecture / Approach

Three phases, ordered so the database exists **before** any code that needs it reaches production.

```
Phase 1  Azure infra     az sql create (Basic) + conn string       →  DB waiting, no code change
Phase 2  Code            EF Core + folders + migration + /health   →  Docker first, then deployed
Phase 3  CI automation   SP + JIT firewall + migrate-before-deploy →  push applies migrations
```

The ordering is the plan review's main correction. Pushes to `main` auto-deploy, so proving the code
locally first — the instinct — would still have shipped a `DbContext` with no database behind it to the
live site. Provisioning first costs ~$4.90/mo slightly earlier and closes that window; Phase 2 still
proves everything against Docker before it deploys.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Azure SQL provisioning | Basic DTU database live, connection string in App Service | Wrong `--service-objective` — the free serverless tier is a quota trap ending at ~$40/mo |
| 2. Persistence code, local then live | EF Core wired, folders, migration, `/health`, deployed and Healthy | Private NuGet feeds on this machine resolving a package CI cannot reach |
| 3. Migration-on-deploy pipeline | Push to `main` applies migrations before deploying | CI has no Azure identity today; a failed run must not leave the firewall open |

**Prerequisites:** Docker Desktop running locally; Azure CLI logged in to subscription
`1b1298d8…` (account `rumianowski@hotmail.com`); GitHub web access to add two Actions secrets (`gh` is
not installed on this machine).

**Estimated effort:** ~2–3 sessions. Phase 2 is the bulk of the code; Phase 3 is the bulk of the
debugging.

## Open Risks & Assumptions

- **Every phase commit deploys.** There is no staging step between committing and going live, so each
  phase must leave the live site working — not merely leave the repo consistent.
- **EF tooling needs a design-time factory.** `dotnet ef` resolves the `DbContext` through the app host,
  and CI has no runtime connection string; an `IDesignTimeDbContextFactory` is what makes Phase 3
  possible at all. Phase 2 verifies it under the exact conditions CI runs in.

- **CI needs a credential it does not have.** The publish profile reaches App Service only. Phase 3
  creates a resource-group-scoped service principal — if `az ad sp create-for-rbac` misbehaves on the
  local CLI `2.35.0` (~2022 vintage, already a known follow-up), that is the trigger to upgrade mid-phase.
- **GitHub runners are not "Azure services."** The `0.0.0.0` firewall sentinel covers App Service but
  not CI, hence the just-in-time rule. Its cleanup step must carry `if: always()` or a failed migration
  leaves the runner's IP allowed on a database holding personal data.
- **"Deploy succeeded" ≠ "app is serving."** The first deployment reported success from both Actions
  and Kudu while still serving Azure's placeholder; an explicit `az webapp restart` fixed it. Expect it
  again before investigating a failing `/health`.
- **Migrations do not roll back with the artifact.** No slots on B1, so rollback is redeploy-previous —
  reversible `Down` methods are a merge requirement, and destructive changes lag one release.
- **Folder layering is convention, not compiler-enforced.** Recorded in `AGENTS.md`/`CLAUDE.md`; if it
  starts to rot, the project split is the escalation.
- **Follow-up not taken here:** `infrastructure.md` recommends a $25/mo budget alert. Worth doing in
  the Azure billing console once the SQL line item appears.

## Success Criteria (Summary)

- `curl https://po-prostu-silka.azurewebsites.net/health` returns 200 `Healthy` — the deployed app is
  genuinely talking to Azure SQL
- A push to `main` applies pending migrations and then deploys; a failing migration aborts the run with
  the previous code still live
- `az sql db show … --query "sku.name"` returns `Basic` — the tier trap avoided
- A developer can go from a clean clone to a working local database with `docker compose up -d` and
  `dotnet ef database update`
