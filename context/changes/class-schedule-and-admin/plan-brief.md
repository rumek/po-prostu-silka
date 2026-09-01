# Class Schedule and Admin Class Management (S-03) — Plan Brief

> Full plan: `context/changes/class-schedule-and-admin/plan.md`

## What & Why

Open the `scheduling` bounded context: a `Class` aggregate with admin create / edit / duplicate /
delete, and a mobile-first day-by-day schedule for active members (FR-007, FR-011, FR-012). This is
the read surface S-04 books against, so the schema decided here is load-bearing well beyond this
slice — getting the aggregate wrong is rework in two downstream slices, not a local fix.

## Starting Point

Nothing scheduling-related exists. `src/Domain/` holds only membership and notifications, and
`AppDbContext` has two non-Identity `DbSet`s. `Class` is the first entity outside Identity needing
full CRUD, so it also establishes the write-seam pattern later slices will copy. On the frontend,
reactive forms are already the decided idiom — but there is not a single date, time, or number input
anywhere in the app, and no route takes a parameter yet.

## Desired End State

An admin opens `/admin/classes`, creates a class, edits it, duplicates it across the next N weeks,
and deletes a mistake — with two classes in one room at overlapping times refused. An active member
opens the schedule and sees the next 14 days as day-grouped sections showing name, time, room,
instructor and free spots. No calendar grid.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Cancelled state | `ClassStatus` enum shipped now, never set | S-05 then adds a transition instead of a migration plus a rewrite of every read path this slice creates. | Plan |
| Free spots | `Capacity - 0` projected now | The wire contract and UI are final, so S-04 changes one expression and no DTO, template or spec. | Plan |
| Instructor | Free-text string | The PRD rules out a Trainer role, so there is no user to link to. | Plan |
| Duplicate | One class → pick 1–8 following weeks | Matches "duplicate to following weeks" and targets the weekly toil this substitutes for. | Plan |
| Schedule range | Rolling 14 days, one request | One round-trip fits the ~1 s NFR; a club's fortnight is tens of rows. | Plan |
| Day grouping | Client-side, browser-local date | Matches how every existing timestamp is rendered; no server timezone config. | Plan |
| Duration | `DurationMinutes` int | One column that cannot contradict itself, and overlap is simple arithmetic. | Plan |
| Room overlap | Refuse single edits (409); duplicate skips colliding weeks and reports | Keeps the invariant real, while a partial duplicate stays useful. | Plan |
| Mistake recovery | Hard delete, guard added in S-04 | Solves the mistyped class now without pre-empting S-05's cancel semantics. | Plan |
| Admin UI | List + `/admin/classes/new` and `/:id` | Follows the shipped list pattern; a full-page form suits six fields on a phone. | Plan |

## Scope

**In scope:** `Class` entity, `ClassStatus`, configuration, migration; `IClassScheduleQuery` /
`IClassAdminQuery` / `IClassStore`; member schedule endpoint and admin CRUD + duplicate with
capacity, duration, future-start and room-overlap rules; member schedule screen; admin list and
create/edit form; Vitest specs.

**Out of scope:** booking (S-04), class cancellation and its notification (S-05), edit-triggered
notifications, recurring series, attendance, a calendar grid, an instructor entity or Trainer role,
pagination.

## Architecture / Approach

`Class` follows `OutboxMessage`'s aggregate shape (Guid key, mutable class, config in an
`IEntityTypeConfiguration`). Reads go through narrow query seams like `IMemberQuery`; writes through
an intention-revealing `IClassStore` like `IPushSubscriptionStore`, committing via `IUnitOfWork` —
there is no repository pattern in this codebase and this slice does not introduce one. The overlap
check runs in SQL via `DateTimeOffset.AddMinutes`, which EF translates to `DATEADD`. The API returns
a flat time-ordered list; the SPA groups into days by local date.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Aggregate and persistence | Entity, enum, configuration, migration | First migration of the milestone; `Down` must be genuinely reversible |
| 2. API surface | Schedule read, admin CRUD, duplicate, overlap invariant | Overlap is a read-then-write race; duplicate's partial success is a non-trivial contract |
| 3. Schedule and admin screens | Member schedule, admin list and form | First `datetime-local` handling — a local↔UTC mistake silently shifts every edited class |

**Prerequisites:** S-01 complete (done); local SQL Server via `docker compose up -d`; `dotnet-ef`
available for the migration.
**Estimated effort:** ~3–4 sessions across 3 phases; Phase 3 is the largest.

## Open Risks & Assumptions

- **Duration and room-overlap were added during planning, beyond the PRD.** Neither appears in
  FR-011's field list or the roadmap outcome for S-03. Built as decided; the cost is a second schema
  field and an invariant enforced on four paths (create, edit, and each duplicated week).
- **The overlap check is a read-then-write race.** No unique index can express interval overlap, and
  serializable isolation conflicts awkwardly with `EnableRetryOnFailure`. Accepted because exactly
  one admin account is ever seeded — documented in a comment rather than left silent.
- **`freeSpots` is briefly a tautology** (always equal to capacity) until S-04 lands.
- **Backend has no test project**, so Phase 1 and 2 verification is `dotnet build` plus manual
  endpoint checks.

## Success Criteria (Summary)

- An admin can run a week's timetable end to end: create, edit, duplicate forward, delete a mistake —
  and cannot accidentally double-book a room.
- An active member sees the next fortnight as a readable day-by-day list on a phone, with free spots
  shown.
- Nothing about `/admin/approvals`, `/admin/members` or the auth flow changes.
