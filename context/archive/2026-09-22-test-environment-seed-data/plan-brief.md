# Test Environment Seed Data — Plan Brief

> Full plan: `context/changes/test-environment-seed-data/plan.md`

## What & Why

The single Azure environment and the local Docker database start empty apart from one admin, so
every manual test begins by building a club by hand. A config-gated seeder fills them with a
realistic, deterministic club: 200 members, 2 admins, 2 trainers, passes, a schedule with bookings,
an exercise library and training plans.

## Starting Point

`AdminSeeder` is the only seeder. It runs idempotently on every start from `Program.cs`. Azure runs
as `Production` (the variable is unset), and the code branches only on `Development` and `Testing`.
Booking rules (capacity, a pass covering the club-local date, entries derived from active bookings)
live in `BookingProtocol`, not in the database.

## Desired End State

Locally and on Azure (renamed to `Staging`), setting `TestDataSeed:Enabled` plus `Reset` once yields
the full data set. Restarts afterwards leave it alone. An admin, a trainer and a member can log in
with one shared password. In `Production` the seeder refuses whatever the flags say.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Mechanism | Startup seeder in Infrastructure, not a migration | Seed rows in a migration would stay in history and reach production. |
| Reset trigger | `TestDataSeed:Reset=true`, turned off by hand | Simplest; the risk of a forgotten flag was accepted. |
| Environment guard | Runs only in `Development` and `Staging` | A production app refuses outright; `Staging` over `Test` because `Testing` enables probe endpoints. |
| Wipe scope | Everything except the `AdminSeed` account and roles | A predictable, identical state after every reset. |
| Headcount | 200 members (160 with an account, 40 accountless) + 2 admins + 2 trainers | Fills the paged member list from S-21. |
| Passwords | One shared password from `TestDataSeed:Password` | Not in the repo for a publicly reachable environment; the same pattern as `AdminSeed`. |
| Time window | Classes and passes from −4 to +4 weeks | Gives expired and used-up passes and history, not just the future. |
| Exercise videos | Each exercise gets one of the 3 Shorts the user supplied, picked by the seeded `Random` | Real videos on the exercise card without made-up ids. |
| Testing | Integration tests on a dedicated Testcontainers SQL Server | Catches S-16 rule and index violations before Azure; the wipe can't hurt other tests. |

## Scope

**In scope:**
- options, gate, wipe, deterministic generator, `Program.cs` hook
- 8 integration tests
- runbook and `AGENTS.md` entry
- switching Azure to `Staging`

**Out of scope:**
- migrations and schema changes
- a reset endpoint or UI
- seeding in `Production` or `Testing`
- preserving hand-made test accounts
- notifications
- push subscriptions

## Architecture / Approach

`TestDataGenerator` builds the entity graph in memory from `(seed, now)` and mirrors
`BookingProtocol`'s capacity and pass rules. `TestDataSeeder` gates on the environment, the flag and
the password. With `Reset` on, it wipes in FK order via `ExecuteDeleteAsync`. It seeds only when the
sentinel `admin1@example.test` is absent. It hashes the password once and saves everything in one
execution-strategy transaction. It runs after `AdminSeeder` in its own `try/catch`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. The seeder and its tests | Seeded data locally, proven by integration tests | Generated bookings violating capacity/pass rules; DST shifting class times |
| 2. Staging on Azure | Azure renamed to `Staging` and seeded; runbook written | `Reset` left on wipes on every recycle; forgetting to switch back to `Production` before real use |

**Prerequisites:** S-16 (passes gate bookings) and S-22 (plans hang off members), both implemented.
**Estimated effort:** ~2 sessions across 2 phases.

## Open Risks & Assumptions

- A forgotten `Reset=true` erases the environment on every App Service recycle (accepted).
- The same database becoming production while still `Staging` with the flag on would be wiped. The
  runbook makes switching the environment the first step of going live.
- The seed blocks startup for a few seconds on B1 / Basic DTU. This is assumed acceptable.

## Success Criteria (Summary)

- One flag flip produces a populated club, and restarts do not disturb it.
- Every generated booking would have been accepted by the live booking rules.
- Admin, trainer and member can log in on Azure and see populated screens.
