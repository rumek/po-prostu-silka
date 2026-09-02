# Class Type Definitions — Plan Brief

> Full plan: `context/changes/class-type-definitions/plan.md`

## What & Why

A class has no definition today. The admin retypes its identity — name, room, instructor — every
time they put an occurrence on the schedule, and nothing binds this Monday's
"Joga dla początkujących" to next Monday's. This slice introduces the **class type**: defined once
with a name, a description, a default duration and a default capacity, then browsed, edited and
deactivated. It is roadmap item **S-05**, delivering `prd-v2.md` FR-004 through FR-007.

## Starting Point

`Class` is a flat entity carrying its own name, room, free-text instructor and capacity
(`src/Domain/Scheduling/Class.cs`). The admin surface around it is complete and idiomatic — a
minimal-API group under an `Admin` policy, `IClassStore`/`IClassScheduleQuery` seams keeping EF Core
out of `Application`, and an Angular list + `new`/`:id` form pair. `Booking` does not exist yet.
There is no class-type concept anywhere, and no backend test project.

## Desired End State

An admin opens `/admin/class-types`, adds "Joga dla początkujących" with an optional description and
defaults of 60 minutes / 12 spots, edits it, deactivates it — it stays visible behind a
"pokaż nieaktywne" toggle with a badge — and reactivates it. A second *active* type cannot reuse an
active name. Everything about creating, editing, duplicating and booking a class occurrence is
untouched; the type is not yet wired into the schedule.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Boundary with S-06 | `ClassType` + **nullable** `Class.ClassTypeId`; no occurrence wiring | Lands the column S-06 needs while leaving the existing class form working, so each stage ends with a working app. |
| Dev data | Cleared in **this** slice (`Classes` only; no `Bookings` table exists) | Gets the irreversible part done now so S-06 starts from an empty schedule. |
| `NOT NULL` on the FK | Deferred to S-06 | Tightening it now would break `POST /api/admin/classes`, which cannot supply a type until S-06. |
| Deactivation | Two-way toggle, dedicated `activate`/`deactivate` endpoints | FR-006 forbids hard delete; without reactivation a mistaken deactivation is just as final. |
| Name uniqueness | Unique **among active types**, via a filtered unique index; `409 name_taken` | Stops the name drift the whole change exists to remove, without holding a name hostage after deactivation. |
| Description | Optional, max 1000 chars | Lets the admin create a type fast; forcing prose produces filler worse than nothing. |
| Numeric bounds | Duration 1–480 min, capacity 1–200 | Keeps the existing `>= 1` floors and adds ceilings that catch a 600-for-60 typo. |
| FR-007 asymmetry | Fields named `DefaultDurationMinutes` / `DefaultCapacity` | The naming is the guardrail that stops S-06 resolving capacity through the type and breaking no-overbooking. |
| Backend testing | No test project; build + Angular specs + a manual checklist | Every shipped slice worked this way; test infrastructure is its own decision, not a rider on a CRUD slice. |

## Scope

**In scope:** the `ClassType` entity, its EF configuration with the filtered unique index, one
migration (create table, add nullable FK, clear `Classes`), the `/api/admin/class-types` endpoint
group with its store/query seams, and the Angular list + form screens with routes and Vitest specs.

**Out of scope:** the type selector and prefill on the class form, name resolution through the type,
removing `Room`/`Name`/free-text `Instructor`, widening the overlap rule club-wide, `NOT NULL` on
the FK, hard delete, a member-facing view, a backend test project, and a global nav entry.

## Architecture / Approach

The slice mirrors the shipped `Class` slice at every layer, so the codebase ends with one pattern
rather than two:

```
Domain/Scheduling/ClassType.cs
  └─ Infrastructure/Persistence/Configurations/ClassTypeConfiguration.cs  (filtered unique index)
Application/Scheduling/ClassTypeEndpoints.cs   DTOs + IClassTypeStore / IClassTypeQuery
  └─ Infrastructure/Scheduling/ClassTypeStore.cs, ClassTypeQuery.cs
app/core/scheduling/class-type.models.ts + class-type.service.ts
  └─ features/admin/class-types/{class-types, class-type-form}
```

`GET /` returns active *and* inactive types in one call; the toggle filters client-side.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Model and schema | Entity, configuration, `DbSet`, nullable FK, migration with the data wipe | `Down` cannot restore the deleted rows — the one accepted departure from the reversibility rule |
| 2. The admin API | Six endpoints under the `Admin` policy, plus both seams | Reactivation can collide with the filtered unique index; activate must re-check the name |
| 3. The admin screens | List with badge + "pokaż nieaktywne" toggle, create/edit form, routes, specs | Failure-to-control mapping must cover `name_taken` or the admin sees an unexplained banner |

**Prerequisites:** S-03 (class schedule) and S-04 (Trainer role) closed — both are. Docker SQL
Server running locally for the migration.
**Estimated effort:** ~2–3 sessions, one per phase.

## Open Risks & Assumptions

- The `Classes` wipe is irreversible. Safe only because the data is development-only, as
  `prd-v2.md` states; if any real data has appeared since, stop and revisit.
- A nullable FK lives in the schema between S-05 and S-06, so a class can be created with no type in
  that window. Deliberate, and the reason S-06 owns the tightening.
- No server-side regression test guards the uniqueness and deactivation rules; the manual checklist
  is the only backstop until a backend test project exists.
- SQL Server's default case-insensitive collation is assumed for name matching — a case-sensitive
  collation would let "Joga" and "joga" both exist as active types.

## Success Criteria (Summary)

- The admin defines a class type once and can find, edit, deactivate and reactivate it without ever
  hard-deleting anything.
- Two active types cannot share a name, and the refusal appears on the name field rather than as a
  banner.
- Nothing in the existing schedule, booking or member surfaces changes behaviour.
