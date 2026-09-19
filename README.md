# Po Prostu Siłka

A class-booking and training-plans web app for a single gym, replacing the spreadsheet the club
used to run sign-ups, schedule changes and individual training plans.

Existing gym software tends to do bookings *or* training plans. Po Prostu Siłka puts the group-class
schedule, bookings, membership passes (karnety), individual training plans and an exercise library
with instructions and videos in one mobile-first place.

## What it does

Three roles, each with its own surface:

- **Admin (club owner)** — manages members and their karnety, defines class types and schedules
  classes from them, books any member into any class, cancels or changes classes, builds the exercise
  library and assigns training plans, and issues invitation codes.
- **Trainer** — books members into the classes they personally instruct, and builds training plans.
- **Member** — sees the schedule as a calendar, their upcoming classes, their karnet with entries
  left, and their own training plan, and is notified by email and web push when a booked class is
  cancelled or changed.

The rules the app is built around:

- **Booking is a staff action.** A member never books or cancels their own spot.
- **The karnet gates training.** Nobody is booked without a pass valid on the class's club-local
  (Europe/Warsaw) date with a free entry. Entries left are derived from active bookings, never stored
  as a counter.
- **No overbooking.** A class never holds more bookings than it has spots, and a karnet never goes
  past its entry count — including under simultaneous requests (optimistic concurrency stamps on
  SQL Server, proven by parallel-request integration tests).
- **No missed cancellations.** Notifications go through a transactional outbox with retries; a message
  that still fails is dead-lettered and surfaced by `GET /health` rather than dropped.
- **Registration is by invitation only.** The admin creates the member record first and hands out a
  single-use code; the person registers with it and lands on their existing bookings, karnet and plan.

## Tech stack

| Area | Choice |
| --- | --- |
| API | ASP.NET Core Web API, .NET 10 (C#), minimal APIs |
| Auth | ASP.NET Core Identity, cookie authentication, role and policy based authorization |
| Data | EF Core over SQL Server (Docker locally, Azure SQL in production) |
| SPA | Angular 22 with SSR, served from the API's `wwwroot` in production |
| Notifications | Azure Communication Services (email), Web Push (VAPID) |
| Tests | xUnit + `WebApplicationFactory` + Testcontainers (real SQL Server), Vitest, Playwright |
| Hosting / CI | Azure App Service, GitHub Actions (`.github/workflows/deploy.yml`) |

The rationale for each choice is in [`context/foundation/tech-stack.md`](context/foundation/tech-stack.md)
and [`context/foundation/infrastructure.md`](context/foundation/infrastructure.md).

## Repository layout

```
src/                     the .NET backend as four projects + the SPA workspace
  Domain/                entities and rules — references nothing
  Application/           use cases and ports — references Domain
  Infrastructure/        EF Core, Identity, email, push — the only project that touches EF Core
    Persistence/         AppDbContext, entity configurations, migrations
  Api/                   the host (Program.cs, appsettings, wwwroot) — publishes as po-prostu-silka.dll
  app/                   the Angular workspace (its own package.json); source in src/app/src/app/
    e2e/                 Playwright specs
tests/po-prostu-silka.Tests/   backend integration tests
context/foundation/      PRD, roadmap, test plan, tech stack, lessons
context/archive/         completed changes (plans and reviews), immutable
```

Bounded contexts — members, scheduling, training, notifications — are subfolders within each layer.

## Getting started

Prerequisites: .NET SDK 10 (pinned in `global.json`), Node 22+ with npm 11, Docker, and the EF Core
CLI (`dotnet tool install --global dotnet-ef`).

```bash
# 1. Start the local SQL Server (a real engine, so locking matches Azure SQL)
docker compose up -d

# 2. Apply migrations — the app does not migrate on startup
dotnet ef database update \
  --project src/Infrastructure/po-prostu-silka.Infrastructure.csproj \
  --startup-project src/Api/po-prostu-silka.Api.csproj

# 3. Build the SPA into the API's wwwroot
cd src/app
npm ci
npm run e2e:stage
cd ../..

# 4. Run the API, which also serves the SPA
dotnet run --project src/Api/po-prostu-silka.Api.csproj
```

Open <http://localhost:5264>. On startup a development admin account is seeded from
`src/Api/appsettings.Development.json` (`AdminSeed`); those credentials are development-only. Check
database connectivity with `GET /health`.

For frontend work, `npm start` in `src/app/` runs the Angular dev server on <http://localhost:4200>.
It has no `/api` proxy, so screens that call the API need the setup above.

Email and push are optional locally: with no `Acs` or `VapidKeys` settings the app still runs and
logs the delivery as failed.

## Tests

```bash
# Backend integration tests (starts a SQL Server container, ~30-60 s) — from the repo root
dotnet test

# SPA unit tests and lint/format — from src/app/
npm test
npm run quality:check

# Browser tests — from src/app/, with the app running as in "Getting started"
npx playwright install chromium   # once
npm run e2e
```

Tests are planned from risks, not from code coverage.
[`context/foundation/test-plan.md`](context/foundation/test-plan.md) lists the failure scenarios the
project protects against: concurrent overbooking, wrong booking admission, retired capabilities
left open, cross-user data access, missed notifications, API contract drift and ungated SPA
regressions. For each one it names the test layer that proves the protection.

CI runs the SPA lint, specs and build, then `dotnet test`, before it applies migrations and deploys.
A Lefthook pre-commit hook (`lefthook.yml`) runs Prettier and ESLint on staged SPA files.

## Documentation

This project is built from a written foundation in [`context/foundation/`](context/foundation/):

- [`prd.md`](context/foundation/prd.md) — the original product requirements: problem, personas, user
  stories, functional requirements, guardrails and non-goals.
- [`prd-v2.md`](context/foundation/prd-v2.md) — the second iteration (class types, trainers, calendar).
- [`roadmap.md`](context/foundation/roadmap.md) — milestones and vertical slices, including the later
  scope changes (accountless members, karnety and staff booking, invitation-only registration).
- [`test-plan.md`](context/foundation/test-plan.md) — risk map and phased test rollout.
- [`lessons.md`](context/foundation/lessons.md) — recurring pitfalls captured during implementation.

Where the PRD and the roadmap disagree, the roadmap is the more recent decision. Each shipped change
has its plan and review under [`context/archive/`](context/archive/). Contributor rules are in
[`AGENTS.md`](AGENTS.md). `gym_management_app_overview.md` is the initial idea sketch, in Polish,
and is superseded by the documents above.
