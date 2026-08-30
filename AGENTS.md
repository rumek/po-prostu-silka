# Repository Guidelines

"Po Prostu Siłka" is a gym class-booking and training-plans web app: an ASP.NET Core Web API (`net10.0`, C#) and an Angular 22 SPA with SSR as sibling projects in one repo. Product requirements live in @context/foundation/prd.md; stack rationale in @context/foundation/tech-stack.md.

## Hard rules

- Never write to `context/archive/` — archived changes are immutable. If a target path resolves there, stop and open a new change instead.
- No overbooking: any booking logic must guarantee a class never accepts more bookings than it has spots (SQL Server transaction via EF Core, per the PRD guardrail).
- MVP notifications are email + push only; do not add an in-app notification center — that scope was explicitly rejected in the PRD.
- Known accepted risk: transitive HIGH vulnerability in `Microsoft.OpenApi 2.0.0` (GHSA-v5pm-xwqc-g5wc); don't "fix" it by downgrading `Microsoft.AspNetCore.OpenApi` — pin a patched transitive reference when available.

## Project structure

- `src/` — .NET Web API (`po-prostu-silka.csproj`, `Program.cs`). Currently the unmodified `dotnet new webapi` stub; the real API is yet to be built.
- `src/app/` — the full Angular workspace (its own `package.json`, `angular.json`); Angular source is at `src/app/src/app/`. Don't confuse the two `src` levels.
- `context/` — foundation docs and change logs (see @context/foundation/README.md).

## Build, test, and dev commands

Backend, from `src/`: `dotnet build`, `dotnet run`, `dotnet list package --vulnerable` (the audit used at bootstrap). No test project exists yet.

Frontend, from `src/app/` (npm 11, pinned via `packageManager`): `npm start` (dev server), `npm test` (unit tests via Vitest), `npm run quality:check` / `quality:fix` (Prettier + ESLint — run `quality:check` before committing frontend changes).

## Style

- C#: nullable reference types and implicit usings are enabled — keep new code warning-free under `<Nullable>enable</Nullable>`.
- Angular: formatting/linting is enforced by Prettier and angular-eslint (@src/app/eslint.config.js), not by hand.

## Commits & CI

History has no established commit convention yet (2 bootstrap commits) — short imperative subjects until one is defined. CI is planned as GitHub Actions auto-deploying to Azure App Service on merge, but no workflow exists yet; don't reference CI checks that aren't there.
