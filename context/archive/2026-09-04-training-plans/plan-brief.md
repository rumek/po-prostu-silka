# Training Plans — Plan Brief

> Full plan: `context/changes/training-plans/plan.md`
> Research: `context/changes/training-plans/research.md`

## What & Why

A trainer or admin builds a named, ordered list of exercises — sets, reps, weight, rest, note — and
assigns it to a member. A member has at most one active plan; a new assignment archives the old. The
member reads their plan and opens any exercise's instructions and video from inside it. This is
roadmap slice S-11 (`prd.md` FR-015, FR-016, FR-017, FR-020), and it is the first change that gives
the `Trainer` role a capability of its own.

## Starting Point

The `Training` bounded context already exists in all three layers, landed by `exercise-library`, which
deliberately left the seams this slice consumes: exercises are deactivated rather than deleted
*specifically* so plan rows cannot be orphaned, and the read-only exercise-detail screen was written to
be adapted for the member's plan view. The `Trainer` role exists, is seeded and is granted by admins —
but confers nothing: no authorization policy, no `isTrainer()` signal, no guard, no route.

## Desired End State

A trainer opens `/trainer/plans`, picks an active member, names a plan, adds exercises from the library
with per-exercise parameters, drags them into order, and saves — replacing that member's previous plan
in one atomic step. The member opens "Mój plan" from the top navigation, sees the plan in the trainer's
order with the author's name, and taps any exercise to read its instructions and watch its video.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Who authors plans | Trainer **and** Admin | Matches prd-v2's additive role model without taking from Admin what FR-015/FR-016 granted. | Research |
| Assignment scope | Any active member | No trainer↔member relationship entity; nothing in the product needs one yet. | Research |
| Blocked member's plan | Untouched | Blocking cuts access at read time; it does not destroy training data. | Research |
| Plan structure | Flat ordered list | FR-015 specifies a list, but items are a separately mapped entity with `Position` so a future day/session split is a migration, not a rewrite. | Plan |
| Field types | `int` sets, **string** reps, `decimal(5,2)` weight, `int` rest seconds | "8-12" and "do upadku" are how prescriptions are really written; an int would push them into the note where nothing validates them. | Plan |
| URL namespace | `/trainer/plans`, `/api/trainer/plans` | The URL tells the truth about who enters; `/admin/*` keeps meaning "admin only". | Plan |
| Reordering | `@angular/cdk/drag-drop` | Mature library with the expected gesture; lands in the builder's lazy chunk. | Plan |
| Keyboard reordering | **Not implemented** | Explicit user decision; CDK offers no keyboard path and none is being hand-built. | Plan |
| Deactivated exercise in an assigned plan | Still shown | A member's plan does not rearrange itself because of library housekeeping. | Plan |
| Member surface | `/my-plan` + navigation entry | The plan is a daily screen, unlike `/admin/exercises` which is a tool. | Plan |
| Plan identity | Required name, silent archive | A name gives the trainer something to refer to; the archived row enables history later without a migration. | Plan |
| Trainer access to the library | Split the group, not the route | Read group behind `TrainerOrAdmin`, write group behind `Admin` — one policy per group, so the convention and the `EveryRoute` test hold. | Plan |
| Invariant testing | Deterministic + concurrent race test | The race test is what caught the CRITICAL in `class-booking-and-cancel`. | Plan |

## Scope

**In scope:** two new entities and one additive migration; the `TrainerOrAdmin` policy; the authoring
API; the member's own-plan read plus a plan-scoped exercise read; the trainer's list and builder
screens; the member's plan and exercise-detail screens; navigation entries; integration and unit tests
including a concurrency race test.

**Out of scope:** day/session split; plan history UI; trainer↔member relationships; notifications on
assignment; plan deletion; standalone exercise browsing for members; keyboard reordering; dashboard
cards (S-12); progress tracking or weight progression.

## Architecture / Approach

`TrainingPlan` (aggregate root, one per member when active) owns `TrainingPlanItem` rows carrying an
explicit `Position` and a `Restrict` FK to `Exercise`. Writes replace the entire item list, so
reordering never updates positions row by row. "One active plan per member" is enforced twice: a
filtered unique index (`[Status] = 0`) that makes two active plans unrepresentable, and a bounded
retry loop over `IUnitOfWork.TrySaveAsync` with an explicitly rotated `ConcurrencyStamp` on the plan
being archived. The index is the load-bearing half — measured, not assumed: the race test survives
removing the rotation and fails without the index, the reverse of the booking slice, because every
assignment INSERTs a new active row. The rotation makes a loser fail earlier and is the only guard on
the edit path. Two API surfaces: `/api/trainer/plans` behind `TrainerOrAdmin`, `/api/plans` behind
`ActiveMember` and scoped to the caller's own id — including the exercise read, which serves an
exercise only when it sits in the caller's plan, enforcing the "no standalone library browsing"
Non-Goal rather than merely respecting it.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Domain, schema, migration | Two entities, EF configurations, filtered unique index, first `decimal` column, reversible migration | Two FKs to `AspNetUsers` need `Restrict` on both or SQL Server refuses the multiple-cascade path |
| 2. Authorization + API | `TrainerOrAdmin` policy, split exercise group, authoring and member-read surfaces, the invariant and its race test | `IsConcurrencyToken()` does not self-rotate — the exact CRITICAL that only a race test catches |
| 3. Both Angular surfaces | Trainer list and builder with CDK drag-reorder, member plan and exercise detail, guard, routes, navigation | A new dependency against ~25 kB of bundle headroom; every route must be lazy |

**Prerequisites:** S-10 `exercise-library` (done); local Docker SQL Server running for the migration
round trip; an account holding the Trainer role for manual verification.
**Estimated effort:** ~3 sessions, one per phase.

## Open Risks & Assumptions

- **Reordering has no keyboard path.** Decided deliberately; recorded here so review reads it as a
  choice rather than an oversight. Two prior reviews raised CRITICAL findings on keyboard-inaccessible
  widgets, so expect this to be flagged again.
- **The builder pulls the full exercise library**, prose and all. Accepted at a single gym's scale;
  `exercise-library` named this slice as the trigger for splitting that endpoint, and the split is
  deferred rather than forgotten.
- **`decimal(5,2)` is the schema's first decimal** and silently becomes the convention for every one
  that follows.
- **A freshly granted Trainer role** does not reach an existing session for up to two minutes — manual
  verification must sign out and back in.

## Success Criteria (Summary)

- A trainer builds, orders and assigns a plan; assigning a second one to the same member leaves exactly
  one active plan, under concurrent requests as well as sequential ones.
- A member sees their plan in the trainer's order and reaches every exercise's instructions and video
  from it — and only those exercises.
- A member with no plan sees an empty state, not an error; a blocked member's plan survives the block.
