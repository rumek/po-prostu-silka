# Persistence Foundation (F-01) Implementation Plan

## Overview

Give the app a database. Provision Azure SQL (Basic DTU), wire EF Core with a bootstrapped
`AppDbContext`, establish the DDD-oriented folder structure every later slice inherits, and make
schema migrations run through CI on every deploy — gated so a bad migration fails the pipeline
before bad code reaches the live site.

This is roadmap item **F-01**, the head of the dependency graph for milestone `first-usable-mvp`.
It deliberately ships **plumbing plus one proving migration**, not the application schema. Entities
arrive with the slices that need them (Identity with F-02, outbox with F-03, classes with S-03).

## Current State Analysis

The app is live at `https://po-prostu-silka.azurewebsites.net` and has no data surface whatsoever.

- **`src/po-prostu-silka.csproj`** targets `net10.0` and carries exactly one package:
  `Microsoft.AspNetCore.OpenApi 10.0.11`. No EF Core, no data provider.
- **`src/Program.cs`** is the untouched minimal-API template plus SPA-serving middleware: `AddOpenApi()`
  is the only service registration, and `/weatherforecast` is the only endpoint. There is no DI
  container usage to extend, no `appsettings` binding, and no health surface.
- **`src/appsettings.json`** has `Logging` and `AllowedHosts` only — no `ConnectionStrings` section.
- **`.github/workflows/deploy.yml`** builds Angular into `wwwroot`, runs `dotnet publish`, and deploys
  via `azure/webapps-deploy@v3` using a publish-profile secret. There is **no Azure CLI login step and
  no database step of any kind** — CI currently has no Azure credential beyond the publish profile.
- **Azure**, per `context/deployment/deploy-plan.md`: resource group `pps-rg` in `polandcentral`,
  Linux B1 plan `pps-plan`, web app `po-prostu-silka`. **Always On is already `true` and HTTPS Only is
  already `true`** — F-01's "re-verify" is a check, not work. Azure SQL was deliberately never created.
- **Code layout** is flat: one web project, no Domain/Application/Infrastructure separation, despite the
  roadmap's stated DDD intent (bounded contexts, aggregates guarding invariants, domain events).

### Key Discoveries:

- **The private-NuGet-feed risk predicted by `deploy-plan.md` is real and confirmed.** Running
  `dotnet package search` on this machine reports three sources: `nuget.org`, plus
  `ServiceBus - Local (file:///C:/Projekty/Anbast/Anbast.ServiceBus/artifacts)` and
  `Microsoft Visual Studio Offline Packages`. These come from a machine-level NuGet.Config the repo
  does not control. F-01 is the change that adds the first new packages since go-live, so this is
  exactly the moment the predicted failure — restore succeeds locally, fails on the CI runner —
  becomes possible.
- **All EF Core 10 packages are at `10.0.11`**, the same version as the existing
  `Microsoft.AspNetCore.OpenApi` pin. Verified against `api.nuget.org` flat-container indexes.
  `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` (first-party) is also `10.0.11`,
  so the health check needs no third-party dependency.
- **CI has no Azure identity.** `deploy-plan.md` records that OIDC was abandoned because the local
  Azure CLI is `2.35.0` (~2022) with no federated-credential support; the publish profile was the
  fallback. A publish profile authenticates to App Service only — it cannot reach Azure SQL or manage
  firewall rules. Applying migrations from CI therefore requires a **new** Azure credential.
- **GitHub-hosted runners are not "Azure services".** Azure SQL's "Allow Azure services and resources
  to access this server" setting does *not* cover GitHub Actions runners, so it cannot be the way CI
  reaches the database. A just-in-time firewall rule for the runner's public IP is required.
- **`infrastructure.md` hard-pins the tier**: Basic DTU (~$4.90/mo). The free serverless tier is
  explicitly rejected (`infrastructure.md:95`) — a background poller keeps waking it, exhausts the
  100k vCore-second quota, and the database pauses until the next month. The pre-mortem describes this
  exact failure ending in a $40/mo bill.
- **`deploy-plan.md` records a deploy gotcha to expect again**: on the first deploy, GitHub Actions and
  the Kudu API both reported success while the site still served Azure's `hostingstart.html`
  placeholder. An explicit `az webapp restart` fixed it. Do not treat "deploy succeeded" as "the new
  app is serving."
- **Rollback policy is already written** (`infrastructure.md:85`, `:101`): no deployment slots on B1, so
  rollback is redeploy-previous-artifact, and **EF migrations do not roll back with it**. Migrations
  must be reversible, and destructive changes deferred one release behind the code that stops needing
  them. `infrastructure.md:86` marks running destructive migrations against production as human-only.

## Desired End State

An Azure SQL Basic DTU database exists in `pps-rg`, the deployed app connects to it, and pushing a
commit to `main` applies any pending EF Core migrations before the new code goes live — failing the
pipeline if the migration fails.

**Verified by:**

1. `curl https://po-prostu-silka.azurewebsites.net/health` returns `200` with body `Healthy` — this
   proves the *running app* opened a real connection to Azure SQL using its own credentials and
   network path (which differ from CI's).
2. `az sql db show -g pps-rg -s <server> -n <db> --query "sku.name"` returns `Basic`.
3. The GitHub Actions run log shows the migration step generating the idempotent script, applying the
   migrations, and reporting success — all before the `webapps-deploy` step.
4. A local `docker compose up` plus `dotnet ef database update` produces the same schema locally.
5. `dotnet list package --vulnerable` is clean and `dotnet restore` resolves only from `nuget.org`.

## What We're NOT Doing

- **No application schema.** No Identity tables, no members, classes, bookings, exercises, plans, or
  outbox. One marker table only. Those belong to F-02, F-03, and S-01…S-09.
- **No repositories, aggregates, domain-event dispatcher, or unit-of-work abstraction.** The folder
  structure is created; the DDD building blocks land with the first slice that has a real aggregate to
  guard (S-01 onward). Building them now would be speculative.
- **No Managed Identity for SQL.** SQL authentication now; MI recorded as a post-MVP follow-up.
- **No project (`.csproj`) splitting.** Folders now, projects later if the boundary starts to rot.
- **No Application Insights / observability work.** Explicitly parked by the roadmap.
- **No deployment slots or blue-green.** Not available on B1; out of scope by platform.
- **No Azure budget alert.** `infrastructure.md` recommends one at $25/mo; it is a billing-console
  action unrelated to this code change. Flagged in the brief as a follow-up.
- **No `az` CLI upgrade.** `deploy-plan.md` lists it as a follow-up. Phases 1 and 3 use commands that
  exist in `2.35.0`; if one is missing, that is the moment to upgrade, not before.
- **No test project.** There is none in the repo today and this change does not create one. Standing up
  the test project is handed off to F-02 — see "Testing Strategy" for why, and treat it as a known
  inherited cost rather than an oversight.

## Implementation Approach

Three phases, ordered so the database exists **before** any code that needs it reaches production.

**Phase 1 provisions Azure SQL and hands the connection string to App Service.** Infrastructure only —
no code changes, nothing that can break the currently-working site. It only adds resources and one App
Service setting.

**Phase 2 wires EF Core**, proving it first against a local SQL Server container and then deploying it
to an environment that already has a database waiting. By the end, the live app reports `Healthy`.

**Phase 3 automates migrations in CI.** This is where the new Azure service-principal credential and
the just-in-time firewall rule land, and where a real push proves the whole loop.

**Why infrastructure comes first.** The instinct is to prove the code locally before spending money —
but this repo auto-deploys: the current branch is `main` and `.github/workflows/deploy.yml` triggers on
every push to it (`ci_default_flow: auto-deploy-on-merge`). Committing the EF Core wiring before the
database exists would redeploy the live site with a `DbContext` pointing at an empty connection string.
Provisioning first costs ~$4.90/mo slightly earlier and removes that window entirely. Phase 2 still
proves everything locally against Docker before it deploys — the fail-cheap step survives, it just no
longer sits in front of an unprovisioned production environment.

## Critical Implementation Details

**Every phase's commit deploys to production.** `deploy.yml` triggers on push to `main`, so there is no
staging step between committing a phase and it going live. This is what drives the phase ordering
above, and it means each phase's success criteria must leave the live site in a working state — not
merely leave the repo in a consistent one.

**App Service connection strings map to a prefixed environment variable, not to `ConnectionStrings:`
directly.** Setting a connection string via `az webapp config connection-string set --connection-string-type SQLAzure`
surfaces it inside the container as `SQLAZURECONNSTR_<name>`. ASP.NET Core's default configuration
provider translates that prefix back into `ConnectionStrings:<name>`, so `GetConnectionString("Default")`
works in production with no code change — but only if the App Service setting is created with type
`SQLAzure` and the exact name `Default`. Using `--settings` (a plain app setting) instead produces
`Default` as a bare key and `GetConnectionString` returns null.

**Migrations must be applied before the app deploys, not after.** The migration step runs earlier in
the same job than `azure/webapps-deploy`, so a failed migration aborts the run with the old code still
serving. This ordering is what makes the "no destructive migration coupled to the code that stops
needing it" policy (`infrastructure.md:101`) enforceable — the schema is always one step ahead of, or
equal to, the code.

**The just-in-time firewall rule must be removed in a step that runs even when the migration fails.**
Guard the cleanup step with `if: always()`, otherwise a failed migration leaves the runner's IP
permanently allowed on a database holding members' personal data.

**EF Core tooling resolves the `DbContext` through the app's host, and CI has no runtime config.**
`dotnet ef migrations script` builds `Program.cs`'s host to obtain the model. On the runner
`ASPNETCORE_ENVIRONMENT` is unset, so the environment is Production, `appsettings.Development.json` is
never loaded, and `GetConnectionString("Default")` yields the empty placeholder — which
`UseSqlServer` rejects. An `IDesignTimeDbContextFactory<AppDbContext>` short-circuits this: when
present, EF's tooling uses it instead of the host, so design-time commands never touch runtime
configuration. It is required for Phase 3 to work at all, and it makes `migrations add` reliable on any
machine regardless of local config.

## Phase 1: Azure SQL Provisioning

### Overview

Create the Azure SQL server and Basic DTU database in the existing resource group, and give App Service
the connection string. Infrastructure only — no code changes. Nothing here can break the running site;
it adds resources and one setting.

### Changes Required:

#### 1. Azure SQL server and database

**Files**: none (Azure CLI operations, recorded in `context/deployment/deploy-plan.md`)

**Intent**: Provision the database the app will use, in the same resource group and region as the
existing App Service so there is no cross-region latency and a single group still holds everything.

**Contract**: `az sql server create -g pps-rg -l polandcentral -n <server-name> --admin-user <user> --admin-password <password>`
followed by `az sql db create -g pps-rg -s <server-name> -n pps-db --service-objective Basic`.

**`--service-objective Basic` is the load-bearing flag and requires explicit human confirmation before
execution.** `infrastructure.md:95` rejects the free serverless tier for this workload (a background
poller wakes it, exhausts the vCore-second quota, and the database pauses until the next month), and
the pre-mortem describes a panicked mis-tiering ending at $40/mo. Confirm the flag and the admin
password with the user, then run the command.

#### 2. Firewall configuration

**Files**: none (Azure CLI)

**Intent**: Let the App Service reach the database, and let the developer's workstation reach it for
inspection and for Phase 2's one-time manual migration.

**Contract**: `az sql server firewall-rule create` with start and end IP both `0.0.0.0` — the documented
sentinel that means "allow Azure services", which covers the App Service outbound path. Add a second
rule for the developer's current public IP. Note explicitly: this sentinel does **not** cover
GitHub Actions runners, which is why Phase 3 adds a just-in-time rule rather than relying on this one.

#### 3. App Service connection string

**Files**: none (Azure CLI)

**Intent**: Hand the running app its connection string as an environment variable, without committing
a credential to the repo. Setting it now means Phase 2's code deploys into an environment that can
already connect.

**Contract**: `az webapp config connection-string set -g pps-rg -n po-prostu-silka --connection-string-type SQLAzure --settings Default="<connection string>"`.
The name must be exactly `Default` and the type exactly `SQLAzure` — see "Critical Implementation
Details" for why the prefix mapping depends on both. **This command restarts the app**, so the live
site must be re-checked afterwards even though no code changed.

#### 4. Re-verify existing platform settings

**Files**: none (Azure CLI)

**Intent**: `deploy-plan.md`'s follow-up list flags Always On and HTTPS Only as things to confirm when
the database lands. Both were already set during the first deployment; this is a check, not a change.

**Contract**: `az webapp config show` reporting `alwaysOn: true`, and `az webapp show` reporting
`httpsOnly: true`. If either is false, set it — Always On being off silently kills the hosted services
F-03 will add.

### Success Criteria:

#### Automated Verification:

- Database exists at the correct tier: `az sql db show -g pps-rg -s <server> -n pps-db --query "sku.name"` returns `Basic`
- Database is in the right region and group: `az sql db show` reports `polandcentral` and `pps-rg`
- Connection string is registered with the right type: `az webapp config connection-string list -g pps-rg -n po-prostu-silka` shows `Default` of type `SQLAzure`
- Always On is on: `az webapp config show -g pps-rg -n po-prostu-silka --query alwaysOn` returns `true`
- HTTPS Only is on: `az webapp show -g pps-rg -n po-prostu-silka --query httpsOnly` returns `true`
- The live site survives the setting-induced restart: `curl https://po-prostu-silka.azurewebsites.net/` returns 200 with the Angular shell

#### Manual Verification:

- The user explicitly confirmed the `Basic` service objective and the admin password before the create command ran
- The Azure portal billing view shows the expected ~$4.90/mo line item and no unexpected serverless meter
- The admin password is stored somewhere durable (password manager) and is not in the repo, shell history, or a chat log
- If the site fails after the restart, `az webapp restart` was tried before deeper investigation — `deploy-plan.md` records this exact false negative

**Implementation Note**: After completing this phase and all automated verification passes, pause here
for manual confirmation from the human that the manual testing was successful before proceeding to the
next phase.

---

## Phase 2: Persistence Code — Local, Then Live

### Overview

Wire EF Core: folder structure, `AppDbContext`, the design-time factory, the first migration, and the
`/health` verification surface. Prove it all against a local SQL Server container first, then deploy it
into the environment Phase 1 prepared.

### Changes Required:

#### 1. NuGet source isolation

**File**: `nuget.config` (new, repo root)

**Intent**: Prevent this repo's restore from inheriting the machine-level private feeds
(`ServiceBus - Local`, `Visual Studio Offline Packages`) confirmed active on this workstation. Without
it, an EF Core package could resolve from a source the GitHub Actions runner cannot reach, producing a
restore failure that reproduces nowhere locally.

**Contract**: `<clear />` inside `<packageSources>` followed by a single `nuget.org` entry pointing at
`https://api.nuget.org/v3/index.json`. The `<clear />` is the load-bearing element — without it the
machine-level sources are merged in rather than replaced.

#### 2. EF Core package references

**File**: `src/po-prostu-silka.csproj`

**Intent**: Add the EF Core SQL Server provider, the design-time package needed to generate migrations,
and the first-party EF health-check integration.

**Contract**: Three `PackageReference` entries, all pinned to `10.0.11` to match the existing
`Microsoft.AspNetCore.OpenApi` version — `Microsoft.EntityFrameworkCore.SqlServer`,
`Microsoft.EntityFrameworkCore.Design` (with `PrivateAssets="all"`, it is design-time only and must not
flow to the published output), and `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`.
`Microsoft.EntityFrameworkCore.Tools` is deliberately **not** included — it serves Visual Studio's
Package Manager Console, and this project drives EF through the `dotnet ef` CLI tool instead.

#### 3. DDD folder structure

**Files**: `src/Domain/`, `src/Application/`, `src/Infrastructure/` (new directories)

**Intent**: Establish the layering every later slice inherits, without paying the cost of splitting into
separate projects during a 3-week MVP. Bounded contexts (membership, scheduling, training,
notifications) become subfolders beneath these as their slices land.

**Contract**: Three top-level folders under `src/`. The layering rule this structure exists to express —
`Domain` references nothing, `Application` references `Domain`, `Infrastructure` references both, and
only `Infrastructure` may reference EF Core — is convention here, not compiler-enforced. Record it in
`AGENTS.md` / `CLAUDE.md` so later agent runs honour it. The existing csproj `Remove="app\**"` item
group must remain untouched; it is what keeps the Angular source out of the .NET compile.

#### 4. The proving entity and DbContext

**Files**: `src/Domain/SchemaMarker.cs`, `src/Infrastructure/Persistence/AppDbContext.cs`

**Intent**: A single trivial entity whose only job is to make the first migration create a real table,
so the pipeline is proven against something that can actually fail on permissions, collation, or
provider configuration — which an empty migration cannot.

**Contract**: `SchemaMarker` with an `int Id` key and a `DateTimeOffset AppliedAt`. `AppDbContext`
derives from `DbContext`, takes `DbContextOptions<AppDbContext>` via constructor, and exposes
`DbSet<SchemaMarker>`. Configure entities with `IEntityTypeConfiguration<T>` classes registered via
`ApplyConfigurationsFromAssembly` in `OnModelCreating` — this is the convention later slices will
follow, and setting it up now means they inherit it rather than inventing it.

#### 5. Design-time DbContext factory

**File**: `src/Infrastructure/Persistence/AppDbContextFactory.cs`

**Intent**: Let `dotnet ef` commands resolve the `DbContext` without any runtime configuration, so
migration tooling works on the CI runner (where only Production config loads and the connection string
is an empty placeholder) and on any developer machine regardless of local settings. Phase 3's pipeline
cannot function without this.

**Contract**: `AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>`, whose
`CreateDbContext(string[] args)` builds `DbContextOptionsBuilder<AppDbContext>` with `UseSqlServer`
against a syntactically valid placeholder connection string. EF's tooling prefers this factory over the
application host whenever it is present, so no connection is ever opened at design time — the
placeholder is never used to connect and must not be a real credential. Add a comment saying exactly
that, or a future reader will try to "fix" it.

#### 6. Local SQL Server

**File**: `docker-compose.yml` (new, repo root)

**Intent**: Give local development a real SQL Server engine, so collation, transaction, and row-locking
behaviour match Azure SQL — this matters because the S-04 no-overbooking guarantee will depend on
locking semantics that SQLite or an in-memory provider would not reproduce.

**Contract**: One service on `mcr.microsoft.com/mssql/server:2022-latest`, `ACCEPT_EULA=Y`,
`MSSQL_SA_PASSWORD` set to a development-only password, port `1433` published, and a named volume for
data persistence across restarts. The SA password must satisfy SQL Server complexity rules (8+ chars,
three of: upper, lower, digit, symbol) or the container exits silently on startup.

#### 7. Connection string, service registration, and health surface

**Files**: `src/appsettings.json`, `src/appsettings.Development.json`, `src/Program.cs`,
`src/po-prostu-silka.http`

**Intent**: Bind the connection string from configuration, register `AppDbContext`, expose `/health`
with a real database probe, and delete the `/weatherforecast` sample along with the request file entry
that calls it.

**Contract**: `appsettings.json` gains an empty `ConnectionStrings.Default` placeholder (documenting the
key without committing a value); `appsettings.Development.json` gains the local Docker connection
string including `TrustServerCertificate=True` — the container uses a self-signed certificate and the
connection fails without it. `Program.cs` adds `AddDbContext<AppDbContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Default")))`,
adds `AddHealthChecks().AddDbContextCheck<AppDbContext>()`, maps `MapHealthChecks("/health")`, and
removes the `WeatherForecast` record, the `summaries` array, and the `MapGet("/weatherforecast")` block.
`MapFallbackToFile("index.html")` must remain the last route registration. In
`src/po-prostu-silka.http`, replace the `/weatherforecast/` request with `GET {{po_prostu_silka_HostAddress}}/health`
— it is the only live artifact referencing the deleted endpoint (the hits in `deploy-plan.md` and
`roadmap.md` are historical audit records and must be left alone).

#### 8. Initial migration

**Files**: `src/Infrastructure/Persistence/Migrations/*` (generated)

**Intent**: Generate the first EF Core migration and confirm it applies cleanly to the local container.

**Contract**: Install the CLI tool first — `dotnet tool install --global dotnet-ef --version 10.0.11`
(the packages in step 2 do not provide it). Then
`dotnet ef migrations add InitialSchemaMarker -p src/po-prostu-silka.csproj -o Infrastructure/Persistence/Migrations`.
The output path keeps EF artifacts inside the Infrastructure layer, honouring step 3's rule that only
Infrastructure references EF Core. The generated `Up`/`Down` pair must both be present and non-empty —
a migration whose `Down` is empty violates the reversible-migrations policy from `infrastructure.md:101`
and should be regenerated rather than hand-patched.

#### 9. Apply to Azure SQL, then deploy

**Files**: none

**Intent**: Create the schema in Azure SQL by hand once, then commit — so the code that expects the
table deploys into a database that already has it. This is the last time migrations are applied
manually; Phase 3 automates it.

**Contract**: `dotnet ef database update` against the Azure connection string from the developer
workstation (reachable via the firewall rule added in Phase 1), then commit and push. The push
auto-deploys. Verify the live `/health` afterwards, applying the `az webapp restart` gotcha if needed.

### Success Criteria:

#### Automated Verification:

- Restore resolves only from nuget.org: `dotnet restore src/po-prostu-silka.csproj` succeeds and `dotnet nuget list source` shows nuget.org as the only enabled source for this directory
- Build passes: `dotnet build src/po-prostu-silka.csproj -c Release`
- No vulnerable packages: `dotnet list package --vulnerable --include-transitive` reports none
- Local SQL Server starts: `docker compose up -d` and the container reaches a healthy/running state
- Migration applies cleanly: `dotnet ef database update -p src/po-prostu-silka.csproj`
- Migration is reversible: `dotnet ef migrations script 0 InitialSchemaMarker` and the inverse both generate without error
- Design-time factory works without runtime config: `dotnet ef migrations script --idempotent` succeeds with `ASPNETCORE_ENVIRONMENT=Production` and no Development settings available — this is the exact condition CI runs under
- App starts and the health probe passes: `dotnet run` then `curl http://localhost:<port>/health` returns 200 `Healthy`
- The removed sample endpoint is gone: `curl http://localhost:<port>/weatherforecast` no longer returns JSON — it returns `text/html` (the Angular shell), because `MapFallbackToFile` claims every unmatched route. A 404 is not achievable here and asserting one would contradict the SPA-fallback criterion below.
- SPA fallback still works: an arbitrary unmapped route returns 200 with the Angular shell
- Migration applies to Azure SQL: `dotnet ef database update` against the Azure connection string succeeds
- The deployed app reaches the database: `curl https://po-prostu-silka.azurewebsites.net/health` returns 200 `Healthy`

#### Manual Verification:

- The `SchemaMarker` table is visible in the local database via a SQL client, with the expected columns
- The `SchemaMarker` table is visible in Azure SQL via the portal query editor or a SQL client
- The Domain/Application/Infrastructure layering rule is written into `AGENTS.md` and `CLAUDE.md` so later agent runs inherit it
- Nothing secret is committed — the Docker SA password is development-only, the design-time factory's placeholder is not a real credential, and `appsettings.json` carries no connection string value
- If the live `/health` returns non-200 after deploy, `az webapp restart` was tried before deeper investigation

**Implementation Note**: After completing this phase and all automated verification passes, pause here
for manual confirmation from the human that the manual testing was successful before proceeding to the
next phase.

## Phase 3: Migration-on-Deploy Pipeline

### Overview

Make every push to `main` apply pending migrations before deploying the new code. This requires giving
CI an Azure credential it does not currently have, and opening the SQL firewall to the runner for the
duration of the migration step only.

### Changes Required:

#### 1. Azure service principal for CI

**Files**: none (Azure CLI + GitHub secret)

**Intent**: CI currently authenticates with a publish profile, which reaches App Service and nothing
else. Applying migrations and managing firewall rules needs a real Azure identity.

**Contract**: `az ad sp create-for-rbac --name pps-ci --role contributor --scopes /subscriptions/1b1298d8-ca6a-4a57-a189-192ff31fbd3a/resourceGroups/pps-rg --sdk-auth`,
scoped to the `pps-rg` resource group only — never the subscription. The JSON output becomes a GitHub
Actions secret named `AZURE_CREDENTIALS`. The SQL connection string becomes a second secret,
`AZURE_SQL_CONNECTION_STRING`. Both are written through the GitHub web UI, since `gh` is not installed
on this machine (`deploy-plan.md` follow-up). Delete any local copy of the credential JSON immediately
after, exactly as the publish profile was handled.

If `az ad sp create-for-rbac` behaves unexpectedly on CLI `2.35.0`, that is the trigger to upgrade the
Azure CLI — the version is ~2022-vintage and already flagged as a follow-up.

#### 2. Migration step in the deploy workflow

**File**: `.github/workflows/deploy.yml`

**Intent**: Generate an idempotent SQL script from the migrations, then apply it to Azure SQL — before
the app deploys, so a failed migration aborts the run with the previous code still serving.

**Contract**: New steps inserted after `Publish API` and **before** `Deploy to Azure App Service`:

1. `dotnet tool install --global dotnet-ef --version 10.0.11`
2. `dotnet ef migrations script --idempotent --project src/po-prostu-silka.csproj --output migrations.sql`
3. `azure/login@v2` with `creds: ${{ secrets.AZURE_CREDENTIALS }}`
4. Resolve the runner's public IP (`curl -s https://api.ipify.org`) and create a firewall rule for it
   via `az sql server firewall-rule create`, naming the rule with the run ID so concurrent runs do not
   collide
5. Apply the migrations with `dotnet ef database update --connection "${{ secrets.AZURE_SQL_CONNECTION_STRING }}"`
6. Delete the firewall rule — **this step must carry `if: always()`** so a failed migration cannot leave
   the runner's IP permanently allowed

**On why `dotnet ef` applies the migrations rather than `azure/sql-action`**: the runner is
`ubuntu-latest`, and sql-action's `.sql` support on Linux is not established — current ubuntu images no
longer ship `mssql-tools`. Using `dotnet ef`, which step 1 already installs for the script generation,
removes an unverified third-party dependency from the critical path and keeps the job to one tool.

The generated `migrations.sql` is still produced and uploaded with `actions/upload-artifact` — it is the
reviewable artifact the human-approval policy (`infrastructure.md:86`) attaches to, and the record of
what a given run intended to change. Note the tradeoff this accepts: the uploaded script is evidence of
intent rather than the literal bytes executed, since `database update` re-derives the SQL. Both come
from the same migration set in the same commit, so they cannot diverge in content.

#### 3. Deployment record

**File**: `context/deployment/deploy-plan.md`

**Intent**: `deploy-plan.md` is the standing audit trail for infrastructure state. It currently states
Azure SQL is deliberately absent — that becomes wrong the moment Phase 2 lands.

**Contract**: Append a section recording the SQL server name, database name, tier, the service
principal and its scope, the two new GitHub secrets (names only, never values), and the migration
pipeline. Update the "Scope of this deployment" paragraph and strike the resolved item from "Known
follow-ups". Keep the unresolved follow-ups (az CLI age, `gh` not installed) intact.

### Success Criteria:

#### Automated Verification:

- Workflow YAML is valid and the run reaches the migration step: the Actions web UI shows the steps executing in order
- The migration step runs before the deploy step in the run log
- The idempotent script is produced and uploaded as a run artifact
- The firewall rule is created and subsequently deleted: `az sql server firewall-rule list -g pps-rg -s <server>` shows no leftover runner rule after the run
- A second push with no new migrations succeeds and is a no-op against the schema (this is what `--idempotent` buys)
- Live health check passes after deploy: `curl https://po-prostu-silka.azurewebsites.net/health` returns 200 `Healthy`
- SPA still serves: `curl https://po-prostu-silka.azurewebsites.net/` returns 200 with the Angular shell

#### Manual Verification:

- A deliberately broken migration (tested on a throwaway branch, or reasoned through) fails the pipeline **before** `webapps-deploy` runs, leaving the previous code serving
- The `if: always()` cleanup is confirmed to fire on a failed run, not just a successful one
- The service principal's scope is the `pps-rg` resource group, not the subscription
- Neither secret value appears in any run log — check the migration step output for accidental connection-string echo
- The local copy of the service principal JSON has been deleted

**Implementation Note**: This is the final phase. After it passes, F-01 is done and F-02
(`auth-identity-foundation`) and F-03 (`notification-delivery-foundation`) are unblocked.

---

## Testing Strategy

There is no application behaviour to unit test in this change — it is plumbing plus one marker table.
Testing is therefore structural and integration-shaped, and **this change deliberately creates no test
project**.

**Handoff to F-02 — read this before planning the next foundation.** The repo has no test project at
all. Creating one here would mean building test infrastructure whose only subject is a throwaway marker
table, and the note below says that suite should be deleted rather than grown. But the cost does not
disappear — it moves. F-02 (`auth-identity-foundation`) is the first change with behaviour worth
asserting, and S-04's no-overbooking guarantee is called "the load-bearing correctness work of the
milestone" by the roadmap and will need genuine concurrency tests against a real SQL Server. Whichever
plan stands the test project up should budget for it explicitly rather than discovering it mid-phase.

### Integration Tests:

- Migration applies to a clean local SQL Server container from zero (`dotnet ef database update` against a freshly created container)
- Migration is reversible (`Down` executes without error)
- The app's health check reports `Healthy` against a real database and `Unhealthy` when the database is unreachable — verify the negative case by stopping the container while the app runs

### Manual Testing Steps:

1. `docker compose down -v` then `docker compose up -d` to get a genuinely empty database
2. `dotnet ef database update` — confirm the `SchemaMarker` table appears
3. `dotnet run`, hit `/health`, expect 200 `Healthy`
4. `docker compose stop` while the app runs, hit `/health` again, expect a non-200 — confirms the probe is real and not merely reporting that the DI container resolved
5. `docker compose start`, hit `/health`, expect recovery to 200
6. After Phase 3 deploys, repeat step 3 against the live URL

**A note on what these tests do not cover**: the marker table exists to prove the pipeline, so once
F-02 introduces real entities, this suite should be replaced rather than extended — do not build a
test scaffold around `SchemaMarker` that later slices inherit.

## Performance Considerations

Basic DTU is a 5-DTU, 2GB tier — genuinely small. It is correct for the MVP's load (one gym, tens of
concurrent members) and correct for cost minimisation, but two things follow from it:

- **The 2GB cap is a real ceiling.** Nothing in the must-have feature set approaches it, but the F-03
  outbox table will grow monotonically unless it is pruned. Flag that constraint for F-03's plan rather
  than solving it here.
- **Cold-connection latency from App Service to Azure SQL in the same region is single-digit
  milliseconds** — the NFR's ~1s perceived response has ample headroom. Enable EF Core's connection
  resiliency (`EnableRetryOnFailure`) when the first real query surface lands in F-02; adding it now,
  with only a health check to exercise it, would be untestable.

## Migration Notes

There is no existing data and no existing schema, so there is nothing to migrate. What this phase
establishes is the *policy* every later migration follows:

- Migrations are generated locally, committed, and applied by CI from an idempotent script
- Every migration must have a working `Down` — reversibility is a merge requirement, not a nicety
  (`infrastructure.md:101`)
- Destructive changes (column drops, table drops) are deferred one release behind the code change that
  stops needing them, because rollback on B1 redeploys the artifact but does **not** roll back the schema
- A destructive migration against production is human-approved (`infrastructure.md:86`); the idempotent
  script uploaded as a run artifact is the thing to approve

## References

- Roadmap item: `context/foundation/roadmap.md` — F-01, milestone `first-usable-mvp`
- Platform decision and risk register: `context/foundation/infrastructure.md`
- Deployment audit trail: `context/deployment/deploy-plan.md`
- Stack rationale: `context/foundation/tech-stack.md`
- Current entry point: `src/Program.cs`
- Current package set: `src/po-prostu-silka.csproj:16`
- Existing pipeline: `.github/workflows/deploy.yml`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Azure SQL Provisioning

#### Automated

- [x] 1.1 Database exists at Basic tier — c90e80b
- [x] 1.2 Database in polandcentral and pps-rg — c90e80b
- [x] 1.3 Connection string registered as SQLAzure type named Default — c90e80b
- [x] 1.4 Always On is true — c90e80b
- [x] 1.5 HTTPS Only is true — c90e80b
- [x] 1.6 Live site survives the setting-induced restart — c90e80b

#### Manual

- [x] 1.7 User confirmed Basic service objective and admin password before create — c90e80b
- [x] 1.8 Billing view shows expected line item, no serverless meter — c90e80b
- [x] 1.9 Admin password stored durably and not in repo or history — c90e80b
- [x] 1.10 Restart gotcha honoured if the site failed after restart — c90e80b

### Phase 2: Persistence Code — Local, Then Live

#### Automated

- [x] 2.1 Restore resolves only from nuget.org — 254fc92
- [x] 2.2 Build passes in Release — 254fc92
- [x] 2.3 No vulnerable packages — 254fc92
- [x] 2.4 Local SQL Server container starts — 254fc92
- [x] 2.5 Migration applies cleanly locally — 254fc92
- [x] 2.6 Migration is reversible — 254fc92
- [x] 2.7 Design-time factory works without runtime config — 254fc92
- [x] 2.8 App starts and local /health returns 200 Healthy — 254fc92
- [x] 2.9 /weatherforecast no longer returns JSON (SPA shell instead) — 254fc92
- [x] 2.10 SPA fallback still works locally — 254fc92
- [x] 2.11 Migration applies to Azure SQL — 254fc92
- [x] 2.12 Deployed app /health returns 200 Healthy — 254fc92

#### Manual

- [x] 2.13 SchemaMarker table visible in local database — 254fc92
- [x] 2.14 SchemaMarker table visible in Azure SQL — 254fc92
- [x] 2.15 Layering rule recorded in AGENTS.md and CLAUDE.md — 254fc92
- [x] 2.16 No secret committed — 254fc92
- [x] 2.17 Restart gotcha honoured if live /health failed — 254fc92

### Phase 3: Migration-on-Deploy Pipeline

#### Automated

- [ ] 3.1 Workflow runs and reaches the migration step
- [ ] 3.2 Migration step runs before the deploy step
- [ ] 3.3 Idempotent script uploaded as a run artifact
- [ ] 3.4 Firewall rule created and deleted, none left over
- [ ] 3.5 Second push with no new migrations is a schema no-op
- [ ] 3.6 Live /health returns 200 Healthy after deploy
- [ ] 3.7 Live SPA still serves

#### Manual

- [ ] 3.8 Broken migration fails the pipeline before webapps-deploy
- [ ] 3.9 Cleanup step confirmed to fire on a failed run
- [ ] 3.10 Service principal scoped to pps-rg, not the subscription
- [ ] 3.11 No secret value appears in any run log
- [ ] 3.12 Local service principal JSON deleted
