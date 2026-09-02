# Repository Guidelines

"Po Prostu Siłka" is a gym class-booking and training-plans web app: an ASP.NET Core Web API (`net10.0`, C#) and an Angular 22 SPA with SSR as sibling projects in one repo. Product requirements live in @context/foundation/prd.md; stack rationale in @context/foundation/tech-stack.md.

## Hard rules

- Never write to `context/archive/` — archived changes are immutable. If a target path resolves there, stop and open a new change instead.
- No overbooking: any booking logic must guarantee a class never accepts more bookings than it has spots (SQL Server transaction via EF Core, per the PRD guardrail).
- MVP notifications are email + push only; do not add an in-app notification center — that scope was explicitly rejected in the PRD.
- Known accepted risk: transitive HIGH vulnerability in `Microsoft.OpenApi 2.0.0` (GHSA-v5pm-xwqc-g5wc); don't "fix" it by downgrading `Microsoft.AspNetCore.OpenApi` — pin a patched transitive reference when available.

## Project structure

- `src/` — .NET Web API (`po-prostu-silka.csproj`, `Program.cs`), organised in DDD layers (see below).
- `src/app/` — the full Angular workspace (its own `package.json`, `angular.json`); Angular source is at `src/app/src/app/`. Don't confuse the two `src` levels.
- `context/` — foundation docs and change logs (see @context/foundation/README.md).

### Layering (convention — not compiler-enforced)

One project, three layers as folders. Bounded contexts (membership, scheduling, training, notifications) become subfolders within them as their slices land.

| Layer | May reference |
| --- | --- |
| `src/Domain/` | nothing |
| `src/Application/` | `Domain` |
| `src/Infrastructure/` | `Domain`, `Application` — and it is the **only** layer that may reference EF Core |

- EF Core artifacts (`AppDbContext`, entity configurations, migrations) live under `src/Infrastructure/Persistence/`. Never put a `using Microsoft.EntityFrameworkCore` in `Domain` or `Application`.
- Entity configuration goes in `IEntityTypeConfiguration<T>` classes under `Infrastructure/Persistence/Configurations/` — they are auto-discovered by `ApplyConfigurationsFromAssembly`, so don't accumulate fluent config in `OnModelCreating`.
- Because nothing enforces this, it rots silently. If it does, the escalation is splitting into separate `.csproj` projects so the compiler enforces it.

### Database

- Local dev runs SQL Server in Docker: `docker compose up -d` (root `docker-compose.yml`), connection string in `src/appsettings.Development.json`. A real engine, not SQLite — locking semantics must match Azure SQL for the no-overbooking guarantee.
- Production is Azure SQL (Basic DTU). The connection string comes from the App Service connection string named `Default`, type `SQLAzure` — both exact values matter, since the platform maps `SQLAZURECONNSTR_Default` back onto `ConnectionStrings:Default`.
- `GET /health` opens a real DB connection; use it to check connectivity rather than inferring it.
- Migrations must be reversible (working `Down`). Rollback redeploys the previous artifact but does **not** roll back schema, so destructive changes lag one release behind the code that stops needing them.
- `nuget.config` pins nuget.org only. Keep the `<clear />` — this machine has private feeds that CI cannot reach.

## Build, test, and dev commands

Backend, from `src/`: `dotnet build`, `dotnet run`, `dotnet list package --vulnerable` (the audit used at bootstrap). Tests live in `tests/po-prostu-silka.Tests/` — run them from the repo root with `dotnet test`. They are integration tests: `IntegrationTestFixture` boots the real app via `WebApplicationFactory<Program>` against a real SQL Server started by Testcontainers, so behaviour that depends on the engine (filtered unique indexes, locking) is actually exercised. CI gates the deploy on `dotnet test`.

Frontend, from `src/app/` (npm 11, pinned via `packageManager`): `npm start` (dev server), `npm test` (unit tests via Vitest), `npm run quality:check` / `quality:fix` (Prettier + ESLint — run `quality:check` before committing frontend changes).

## Style

- C#: nullable reference types and implicit usings are enabled — keep new code warning-free under `<Nullable>enable</Nullable>`.
- Angular: formatting/linting is enforced by Prettier and angular-eslint (@src/app/eslint.config.js), not by hand.

## Commits & CI

History has no established commit convention yet (2 bootstrap commits) — short imperative subjects until one is defined. CI is planned as GitHub Actions auto-deploying to Azure App Service on merge, but no workflow exists yet; don't reference CI checks that aren't there.
