# Occurrences From Class Types — Plan Brief

> Full plan: `context/changes/occurrences-from-class-types/plan.md`

## What & Why

A class occurrence retypes its own identity. The admin types the name, the room and the instructor
every week, nothing binds this Monday's "Joga dla początkujących" to next Monday's, and two of those
three fields carry no information at all — the club has one room, and the instructor is free text
only because the product shipped without a Trainer role. This slice makes an occurrence an *instance*
of a `ClassType`: name and description resolve **by reference**, duration and capacity are **copied**,
the instructor becomes a real account, and the room disappears. Roadmap item **S-06**, delivering
`prd-v2.md` US-01 and FR-008 through FR-013.

## Starting Point

S-05 already landed `ClassType` and a **nullable** `Class.ClassTypeId`, and already emptied the
`Classes` table — the dev-data wipe is done, so nothing here needs a backfill. S-04 landed the
`Trainer` role with grant and revoke, but there is no way to *list* trainers. `Class` is still flat
(`src/Domain/Scheduling/Class.cs`), the overlap check is still keyed on `Room`
(`ClassStore.HasRoomConflictAsync`), and four client surfaces mirror the current contract. Unlike
S-05, a backend integration-test project now exists.

## Desired End State

The admin opens `/admin/classes/new` and fills a form of *selections*: a class type and a trainer.
Duration and capacity prefill and stay overridable; there is no name field and no room field. Any
time collision anywhere in the club is refused on the start-time control. Correcting a typo in a
type's name corrects it on every occurrence, past ones included — while editing that type's default
capacity changes nothing about classes that already exist.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| UI reach | Minimal update to **both** views | S-07 rewrites the schedule anyway, but leaving the member view on the old DTO would mean a broken app between the two slices. | Plan |
| Trainer list | New `GET /api/admin/trainers` + `ITrainerQuery` | An explicit contract that filters by role in SQL, instead of shipping every account's email to a dropdown. | Plan |
| Trainer revoked / blocked | No warning, no block | Accepted risk: the schedule can name someone who no longer teaches, and nothing signals it. | Plan |
| `Name` / `Room` / `Instructor` columns | **Dropped in this release** | Reversed mid-implementation by the product owner: an accepted exception to the one-release lag, safe only because `Classes` is empty and nothing is live. | Plan (revised) |
| Type on an existing occurrence | **Immutable**; `400 class_type_immutable` | Keeps the reference stable and makes a client bug loud; a wrong type is fixed by delete-and-recreate while nothing is booked. | Plan |
| Deactivated type | Editing its occurrences still works | FR-006 promises existing occurrences survive deactivation; the active-type check therefore runs on create only. | prd-v2 FR-006 |
| Name resolution | Navigation property `Class.ClassType` | Idiomatic single-statement joins — **against S-05's explicit warning**, so integration tests replace the compile-time barrier. | Plan |
| Entity naming | Keep `Class`, `ScheduledClass`, `/api/classes` | The diff shows the model change instead of a rename tonnage; accepted vocabulary drift from the PRD's "occurrence". | Plan |
| Empty selects | Signposted message + link, form hidden | After the wipe this is literally the first screen the admin sees; an unfillable form with no explanation is the worst possible first use. | prd-v2 empty-state guardrail |
| Tests | `ClassEndpointTests` on the invariants | The navigation property just removed the only structural guard around FR-007's copy semantics. | Plan |

## Scope

**In scope:** the required `ClassTypeId` + `InstructorUserId` references with navigations, two
reversible migrations, the `/api/admin/trainers` endpoint and its query seam, the rewritten class
contract (`time_conflict`, four reference failures, resolved name/description/instructor), the
club-wide overlap rule across create/edit/duplicate, `ClassEndpointTests`, and the rewritten admin
form plus the two de-roomed lists.

**Out of scope:** the calendar (S-07), renaming `Class` → `ClassOccurrence`, changes to trainer
grant/revoke, guest instructors, booking and cancellation notifications (S-08/S-09), a concurrency
token on `Class`, and any member-facing class-type screen.

## Architecture / Approach

```
Domain/Scheduling/Class.cs          ClassTypeId (NOT NULL) + nav, InstructorUserId + nav; Name/Room/Instructor gone
  └─ Infrastructure/Persistence/Configurations/ClassConfiguration.cs   IX_Classes_StartsAt replaces IX_Classes_Room_StartsAt
Application/Members/TrainerEndpoints.cs   GET /api/admin/trainers + ITrainerQuery
  └─ Infrastructure/Members/TrainerQuery.cs
Application/Scheduling/ClassEndpoints.cs  references in, resolved values out; time_conflict
  └─ Infrastructure/Scheduling/{ClassStore.HasTimeConflictAsync, ClassScheduleQuery joins}
tests/po-prostu-silka.Tests/ClassEndpointTests.cs   the FR-007 asymmetry, pinned
app/features/admin/classes/class-form   two selects, prefill-on-select, signposted empty states
```

The rule running through everything: **identity by reference, numbers by copy.** The server loads a
type only to *validate* it; the numbers come from the request, which the client prefilled.

## Phases at a Glance

> **Adapted during implementation.** Phases 1 and 2 were merged: the `NOT NULL` references break the
> old write path the moment the schema lands, so splitting the model from the API would have left
> `POST /api/admin/classes` broken between them for no benefit. Three phases became two.

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Model, schema and API | Required references, two migrations, trainer list, club-wide time rule, `ClassEndpointTests` | A write path reading `DefaultCapacity` through the new navigation — silent, and exactly what the tests exist to catch |
| 2. Screens | Two selects with prefill, signposted empty states, no room anywhere | Prefill firing when loading an existing class would silently overwrite an occurrence's own numbers |

**Prerequisites:** S-03, S-04 and S-05 closed — all three are. Docker SQL Server running for the
migrations and for `dotnet test` (Testcontainers).
**Estimated effort:** ~2 sessions, one per phase; Phase 1 is the largest.

## Open Risks & Assumptions

- **The navigation property overrides a documented warning** in `Class.cs`. The barrier is now a
  comment plus three integration tests. A future slice adding a write path near the type must re-read
  FR-007 first.
- **The two phases are not independently deployable** — the client is broken between them. Ship them
  as one release, or accept a window with a non-functional admin class form.
- **A revoked or blocked trainer leaves a stale reference** on future classes, unflagged, by explicit
  choice. If it bites, the fix is a badge on the admin list, not a schema change.
- **Rollback past this release breaks the schedule outright.** The three dead columns are dropped in
  the same release that stopped writing them, against the repository's one-release lag rule. A
  rollback leaves the pre-S-06 build projecting columns that no longer exist, so `GET /api/classes`
  fails with "invalid column name" rather than degrading — the member schedule stops rendering, not
  just class creation. Accepted knowingly; recovery is rolling forward or running
  `DropDeadClassColumns.Down` by hand.
- The occurrence's numeric bounds (1–480 / 1–200) are **duplicated** from `ClassTypeEndpoints` rather
  than shared — an occurrence may legitimately override its type, so it cannot inherit them.

## Success Criteria (Summary)

- The admin lays out a class without typing a single identity field — type and trainer both come from
  a selection, and there is no room field anywhere.
- Renaming a class type renames it on every existing occurrence; changing its default capacity
  changes none of them.
- Two classes can no longer overlap in time anywhere in the club, on create, on edit, or in a
  duplicate batch — which still skips and reports the colliding weeks rather than failing whole.
