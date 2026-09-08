# The plan card carries the whole prescription — Plan Brief

> Full plan: `context/changes/2026-09-08-plan-card-prescription-detail/plan.md`
> Roadmap slice: `context/foundation/roadmap.md` → S-15 (milestone M-3, anchors MS-001–MS-004)

## What & Why

The member's plan screen cannot express a whole class of prescription: a plank is measured in seconds,
not kilograms, and there is nowhere to put the seconds. The same card also hides which muscle group an
exercise trains, makes the entire exercise name a navigation link, and renders the trainer's note as
an unmarked paragraph that reads as a continuation of the numbers above it.

## Starting Point

Prescription parameters — sets, reps, weight, rest, note — already live on `TrainingPlanItem`, one row
per exercise in a plan. `Exercise.MuscleGroup` already exists and is already projected on the
exercise-detail read; it is missing only from the plan-item view. A shared icon primitive exists with
a closed `IconName` union. Every parameter is optional, and the card omits absent ones rather than
showing a dash.

## Desired End State

A member opens `/my-plan` and each card shows the exercise name as plain text with a small info icon
beside it, the muscle group beneath the name, the prescribed parameters including `Czas` where the
trainer set one, and the trainer's note in a rounded callout prefixed by an info icon. A trainer
editing a plan has a `Czas (s)` field beside `Przerwa (s)`.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| Where duration lives | `TrainingPlanItem.DurationSeconds` | The same plank is 45 s in one plan and 60 s in another — exactly as weight already varies per prescription. On `Exercise` it would be fixed library-wide. | Roadmap charter (MS-001) |
| Unit and bounds | Seconds, 1–3600, shown as `45 s` | Symmetric with `RestSeconds` in the same form; no second time format on one card. Floor is 1, not 0 — a zero-second exercise is a slip, while a zero-second *rest* is legitimate. | Plan |
| Card layout | Duration joins the parameter row; muscle group sits under the name | Duration is the trainer's prescription and belongs with weight; muscle group is a fact about the exercise, and mixing them would imply the trainer chose it. | Plan |
| Info icon | One `info` glyph, two roles | The icon primitive is decorative by default with the *control* carrying the accessible name — exactly this split. One glyph, not two, keeps the eager bundle honest. | Plan |
| Info icon tap target | Small, at natural size | Chosen deliberately over a padded 44x44 target. **Accepted regression** — see Open Risks. | Plan |
| Slice split | One slice, not two | User's call: the data change and the card rebuild ship together. | Roadmap charter |

## Scope

**In scope:** a nullable `DurationSeconds` column and its migration; both plan-item DTOs; the
validation triple (server constant, `ValidateShape` branch, mirrored Angular validator +
`invalid_duration` message); the shared projection; a `Czas (s)` field in the trainer's builder; an
`info` icon; and the four card changes — name as text, info icon, muscle group, duration, note
callout.

**Out of scope:** duration on `Exercise`; an "is timed" flag; changing how `RestSeconds` renders; the
exercise-detail screen; restyling the builder beyond the one field; correcting the stale bundle-budget
figure in `icon.ts`.

## Architecture / Approach

Back to front, in the order the data flows. The contract widens first so both screens can see the new
fields, then the trainer gains an input so a duration can exist, then the member's card can read it.
The critical structural fact: `TrainingPlanItemView` is deliberately **one shape serving two
consumers** (the trainer's edit load and the member's read), built by **one projection**
(`ProjectDetail`) — so both new fields land on both screens through a single change, and a field added
to the record but missed in the projection compiles fine and shows as a silently empty cell.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Contract | Column + migration, both DTOs, bounds + `invalid_duration`, projection | A field added to the record but missed in the projection reads back null with no error |
| 2. Trainer input | `Czas (s)` in the plan builder, bounded like the server | A blank field submitting `0` instead of `null` |
| 3. Member card | `info` icon, name as text, muscle group, duration, note callout | `hasParameters()` not learning about duration — a duration-only plank renders an empty card |

**Prerequisites:** S-10 (exercise library — `MuscleGroup` lives there) and S-11 (the plan screen and
builder), both `done`. Docker running for `dotnet test`; Node 22+ for the frontend commands.

**Estimated effort:** three phases, each independently deployable and each leaving the build and both
test suites green.

## Open Risks & Assumptions

- **The info icon's tap target is an accepted regression.** `my-plan.scss` currently justifies making
  the whole name the link because the screen "is read between sets, often one-handed, and a small tap
  area is the difference between usable and not". The chosen design drops that. Offered with the
  tradeoff stated and chosen anyway — recorded so a reviewer reads it as a decision, not an oversight.
  Cheapest reversal is padding on the anchor, no markup change. The plan also requires rewriting that
  SCSS comment, so the file does not keep a rationale its code contradicts.
- **`MuscleGroup` is free text**, not an enum, so display consistency depends on how exercises were
  entered.
- **Nothing prevents a nonsensical combination** — a trainer filling both weight and duration gets
  both, matching how the existing four parameters already behave.

## Success Criteria (Summary)

- A trainer prescribes a plank as 3 sets x 45 s with no weight, and the member sees `Czas 45 s` and no
  weight row.
- An exercise prescribed with *only* a duration still shows its parameter list.
- The member can reach the exercise description from a distinct info control, and the trainer's note
  reads as a note rather than as more numbers.
