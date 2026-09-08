# S-15: The plan card carries the whole prescription

## Overview

The member's plan screen is where a training plan either works or does not, and today its exercise
card cannot express a whole class of prescription. A plank is measured in seconds, not kilograms, and
there is nowhere to put the seconds. The card also does not say which muscle group an exercise trains,
turns the entire exercise name into a navigation link, and renders the trainer's note as an unmarked
paragraph that reads as a continuation of the parameters above it.

This change adds `TrainingPlanItem.DurationSeconds` as a per-plan prescription and rebuilds the card
around it: muscle group under the name, duration beside weight and rest, a distinct info icon
carrying the navigation, and the trainer's note set off as a callout.

## Current State Analysis

- **Prescription parameters live on the plan item, not the exercise.** `TrainingPlanItem`
  (`src/Domain/Training/TrainingPlanItem.cs`) carries `Sets`, `Reps`, `WeightKg`, `RestSeconds` and
  `Note`; `Exercise` (`src/Domain/Training/Exercise.cs`) carries the library facts — `Name`,
  `MuscleGroup`, `Difficulty`, `Equipment` and the instructional prose. Duration is a prescription,
  so it belongs on the item.
- **`Exercise.MuscleGroup` already exists** and is already projected elsewhere
  (`src/Infrastructure/Training/TrainingPlanQuery.cs:112`, in the exercise-detail read). It is
  missing only from the plan-item view.
- **One DTO serves two screens.** `TrainingPlanItemView`
  (`src/Application/Training/TrainingPlanEndpoints.cs:41`) is deliberately the single shape behind
  both the trainer's edit load and the member's read — its own doc comment says a second contract
  would be "two things to keep in step for no gain". Both new fields therefore surface on both
  screens at once.
- **One projection feeds both reads.** `ProjectDetail`
  (`src/Infrastructure/Training/TrainingPlanQuery.cs:128-150`) is where the item view is constructed,
  once.
- **Bounds are validated in three places by convention.** A server-side constant pair plus a
  `ValidateShape` branch (`TrainingPlanEndpoints.cs:487-549`), a mirrored Angular validator
  (`plan-builder.ts:423-435`), and a failure code the SPA maps to a message
  (`plan-builder.ts:352`). The file's own comment calls the missing-guard failure "the single most
  repeated finding in this repo's review history": without it an out-of-range value reaches SQL
  Server and an ordinary bad input becomes a 500.
- **`hasParameters()` gates the entire parameter list.** `my-plan.ts:63-70` returns false unless one
  of the four current fields is set, and the template renders nothing when it does. A plank
  prescribed with *only* a duration would show an empty card — the exact case this change exists for.
- **An icon primitive already exists.** `src/app/src/app/shared/icons/icon.ts` defines a closed
  `IconName` union and states that adding an icon "means adding a case to the template, and nothing
  else". It is decorative by default (`aria-hidden`), with the *control* carrying the accessible name.
- **The current large tap target is a deliberate, documented decision.** `my-plan.scss` justifies the
  whole-name link: "sized as a real target: this is read between sets, often one-handed, and a small
  tap area is the difference between usable and not."

## Desired End State

A member opens `/my-plan` and each exercise card shows: the exercise name as plain text with a small
info icon beside it that opens the exercise description; the muscle group beneath the name; the
prescribed parameters — now including `Czas` where the trainer set one — and the trainer's note in a
rounded callout prefixed by an info icon. A trainer editing a plan has a `Czas (s)` field beside
`Przerwa (s)`, bounded identically.

Verified by: `dotnet test` (round-trip of the new field, its bounds, and the muscle group in the view),
`npm test` (card rendering including the duration-only case), `npm run quality:check`, and a manual
pass on a phone-width viewport.

### Key Discoveries

- `TrainingPlanItemView` at `src/Application/Training/TrainingPlanEndpoints.cs:41` — one shape, two
  consumers.
- `ProjectDetail` at `src/Infrastructure/Training/TrainingPlanQuery.cs:128` — one projection; a field
  added to the record but missed here compiles and surfaces as a silently empty cell.
- `MaxRestSeconds = 3600` at `TrainingPlanEndpoints.cs:187` with the reasoning "An hour. A longer
  'rest' is not a rest, it is a data-entry slip." Duration reuses the same ceiling.
- `hasParameters()` at `src/app/src/app/features/my-plan/my-plan.ts:63` — must learn about duration.
- `TrainingPlanItemConfiguration.cs:19-20` — `Sets` and `RestSeconds` are configured as bare
  `builder.Property(...)`; a nullable `int` needs no further mapping, so `DurationSeconds` follows.

## What We're NOT Doing

- **Not putting duration on `Exercise`.** Settled in M-3's charter (MS-001): the same plank is 45 s in
  one member's plan and 60 s in another's, exactly as weight already varies per prescription.
- **Not changing how `RestSeconds` renders.** `Przerwa 90 s` stays as it is; duration matches it
  rather than introducing a second time format on the same card.
- **Not adding a "this exercise is measured in time" flag to `Exercise`.** It was offered and not
  chosen; the trainer simply leaves weight blank and fills duration.
- **Not touching the exercise-detail screen** (`plan-exercise-detail.*`) — it already shows the
  muscle group.
- **Not restyling the trainer's plan-builder card** beyond adding the one field.
- **Not correcting the stale bundle-budget figure** in `icon.ts` (its doc says the warning is 500 kB;
  AGENTS.md records it was raised to 550 kB in S-12). Noted here so the next reader knows it is known;
  fixing it belongs to whatever change next has a reason to be in that file's prose.
- **Not enlarging the new info icon's tap target.** See Open Risks — this is an accepted regression,
  chosen deliberately.

## Implementation Approach

Back to front, in the order the data flows: the contract first (so both screens can see the new
fields), then the trainer's input (so a duration can exist), then the member's card (so it can be
read). Each phase leaves `dotnet build`, `dotnet test` and `npm test` green and is independently
deployable — the same expand-then-consume ordering the previous slices in this repo used.

Phase 1 is the only one with a schema change, and it is additive and nullable, so the previous
artifact keeps running against the new schema.

## Critical Implementation Details

**State sequencing — `hasParameters()` must change in the same commit as the duration display.** The
template renders the whole `<ul class="my-plan-params">` block behind that guard. If duration is added
to the template but not to the guard, a prescription consisting only of a duration — a plank, the
motivating case — renders a card with a name and nothing else, and it does so silently.

## Phase 1: The contract carries duration and muscle group

### Overview

One migration, one new domain property, both DTOs, the validation triple, and the single projection.
Nothing reads the new fields yet; the SPA is untouched.

### Changes Required:

#### 1. Domain

**File**: `src/Domain/Training/TrainingPlanItem.cs`

**Intent**: Add the duration a trainer prescribes for an exercise measured in time rather than in
load. It sits with the other prescription parameters, not with the exercise's library facts.

**Contract**: `public int? DurationSeconds { get; set; }`, placed next to `RestSeconds`. Nullable
because every prescription parameter is optional (FR-015) — a trainer may prescribe a bare exercise.

#### 2. Entity configuration

**File**: `src/Infrastructure/Persistence/Configurations/TrainingPlanItemConfiguration.cs`

**Intent**: Map the new column the way its nearest neighbour is mapped.

**Contract**: `builder.Property(x => x.DurationSeconds);` beside the existing `RestSeconds` line. A
nullable `int` needs no length, precision or conversion.

#### 3. Migration

**File**: `src/Infrastructure/Persistence/Migrations/<timestamp>_AddPlanItemDuration.cs`

**Intent**: Add the column. Additive and nullable, so the previously deployed artifact keeps working
against the new schema and no backfill is needed or meaningful — an absent duration is the correct
value for every existing row.

**Contract**: `AddColumn<int>("DurationSeconds", "TrainingPlanItems", nullable: true)` in `Up`;
`DropColumn` in `Down`. The `Down` is genuinely reversible and loses only durations entered after the
migration applied — state that in the migration's doc comment, following the precedent the other
migrations in this folder set. Do not let EF scaffold a `defaultValue`.

#### 4. Wire contract

**File**: `src/Application/Training/TrainingPlanEndpoints.cs`

**Intent**: Carry the duration in both directions and the muscle group outward, so the card has
everything it needs from the one read it already makes.

**Contract**:

- `TrainingPlanItemRequest` (:89) gains `int? DurationSeconds`, appended last.
- `TrainingPlanItemView` (:41) gains `int? DurationSeconds` and `string? MuscleGroup`, appended last.
  `MuscleGroup` is read-only — it is a fact about the exercise, never submitted with a prescription,
  so it deliberately does NOT appear on the request.
- Bounds: `MinDurationSeconds = 1` and `MaxDurationSeconds = 3600`, beside the rest bounds. One second
  is the floor rather than zero — a zero-second exercise is a data-entry slip, whereas a zero-second
  *rest* is a legitimate "no rest", which is why `MinRestSeconds` is 0 and this is not.
- `ValidateShape` (:487) gains a branch refusing `invalid_duration` with 400, matching the shape of
  the `invalid_rest` branch immediately above it.
- The endpoint's failure-vocabulary doc comment (:116-117) lists every reason code; add
  `invalid_duration` to it.

#### 5. Projection

**File**: `src/Infrastructure/Training/TrainingPlanQuery.cs`

**Intent**: Populate the two new view fields in the one place the item view is built.

**Contract**: In `ProjectDetail` (:128), add `i.DurationSeconds` and `i.Exercise.MuscleGroup` to the
`TrainingPlanItemView` construction, in the record's declared order. The navigation is already used in
this file (`x.Exercise.MuscleGroup` at :112), so no `Include` is needed — it is a projection.

#### 6. Write path

**File**: `src/Application/Training/TrainingPlanEndpoints.cs`

**Intent**: Persist the submitted duration on create and on edit.

**Contract**: Wherever `RestSeconds` is copied from the request onto the entity (both the create and
the replace-all edit path), copy `DurationSeconds` alongside it. An edit replaces the entire item
list, so there is one construction site per path and no partial-update case to handle.

#### 7. Tests

**File**: `tests/po-prostu-silka.Tests/TrainingPlanEndpointTests.cs`

**Intent**: Pin the round-trip and the bounds, so a field added to the record but dropped from the
projection or the write path fails loudly rather than reading back as null.

**Contract**: Assign a plan with a duration and read it back asserting the value survives; assert
`invalid_duration` 400 at `0` and at `3601`; assert `1` and `3600` are accepted. Follow the file's
existing bounds-test shape for `invalid_rest`.

**File**: `tests/po-prostu-silka.Tests/MyPlanEndpointTests.cs`

**Intent**: The member's read is a different endpoint through the same projection; pin that it carries
both new fields.

**Contract**: One assertion that the member's own plan read returns the prescribed `durationSeconds`
and the exercise's `muscleGroup`.

**Adapted during implementation (Phase 1).**

- **There is one item-construction site, not one per path.** The plan said to copy `DurationSeconds`
  "wherever `RestSeconds` is copied onto the entity (both the create and the replace-all edit path)"
  and called it "one construction site per path". In fact `BuildItems`
  (`TrainingPlanEndpoints.cs:471`) is a single helper both paths call, so the field is assigned once.
  The instruction is satisfied more simply than it was written; recorded so the next reader does not
  go hunting for a second site that does not exist.
- **Two shared test helpers were widened.** `CreateExerciseAsync` in both
  `TrainingPlanEndpointTests` and `MyPlanEndpointTests` gained an optional `muscleGroup` parameter,
  and `TrainingPlanEndpointTests.Item()` gained an optional `durationSeconds`. Both default to
  `null`, so every existing caller is unaffected — but they are shared fixtures, so the widening is
  worth naming rather than leaving to be discovered in a diff.
- **A third and fourth test were added beyond the plan's contract.** The plan asked for the
  round-trip and the bounds. `An_exercise_without_a_muscle_group_reads_back_null` pins that an absent
  muscle group is `null` rather than `""` — Phase 3's card omits the caption on a truthiness check,
  so an empty string would render an empty line. `The_duration_range_is_enforced` also asserts the
  accepting cases (1 and 3600), not only the refusals, because the floor differing from
  `MinRestSeconds` is the one asymmetry a future edit is likely to "fix" by mistake.

### Success Criteria:

#### Automated Verification:

- Backend builds warning-free: `dotnet build` from `src/`
- Integration tests pass: `dotnet test` from the repo root
- Migration is reversible: `dotnet ef database update <previous>` then forward again against the
  docker database, per AGENTS.md
- Generated SQL shows the column nullable, with no `defaultValue`: `dotnet ef migrations script`

#### Manual Verification:

- `GET /health` returns healthy after the migration applies

---

## Phase 2: The trainer can prescribe a duration

### Overview

One field in the plan builder, bounded identically to the server and mapped through the same
blank-versus-null convention the other numeric fields use.

### Changes Required:

#### 1. Wire model

**File**: `src/app/src/app/core/training/training-plan.models.ts`

**Intent**: Mirror the widened API records so the two cannot drift.

**Contract**: `durationSeconds: number | null` on both the item view and the item request;
`muscleGroup: string | null` on the view only. These are contract mirrors — the file's existing
comment about keeping them in step with the API applies.

#### 2. Builder form

**File**: `src/app/src/app/features/trainer/plans/plan-builder.ts`

**Intent**: Add the control, bound the same way the server bounds it, and carry it in and out of the
API shape.

**Contract**:

- `durationSeconds: FormControl<number | null>` on the item group type (:50-53) and in the group
  factory (:423-435), with `Validators.min` / `Validators.max` fed by module constants matching the
  server's pair (1 and 3600).
- `fromDetail` (:191-194) reads `item.durationSeconds`; `toRequest` (:456-459) writes it through the
  existing `numberOrNull` helper, which already encodes "blank and NaN are the form's absent, null is
  the API's".
- The failure switch (:352) maps `invalid_duration` to a message in the same voice as `invalid_rest`.

#### 3. Builder template

**File**: `src/app/src/app/features/trainer/plans/plan-builder.html`

**Intent**: Put the input where a trainer expects it — beside the other time field.

**Contract**: A `Czas (s)` labelled number input bound to `formControlName="durationSeconds"`, placed
after the `Przerwa (s)` field (:153-158) and following that field's markup exactly, including the
`[for]`/`id` index pairing the other controls use.

#### 4. Tests

**File**: `src/app/src/app/features/trainer/plans/plan-builder.spec.ts`

**Intent**: Pin that the field round-trips and that a blank field does not become a zero.

**Contract**: A duration loaded from an existing plan appears in the control; a submitted plan carries
`durationSeconds` in its payload; a blank field submits `null` rather than `0` or `NaN`.

**Adapted during implementation (Phase 2).**

- **The bounds convention is a QUADRUPLE, not a triple.** The plan named three places — server
  constant, Angular validator, failure message. There is a fourth: `TrainingPlanFailureReason` in
  `training-plan.models.ts` is a typed union of every reason string, and `tsc` refused
  `invalid_duration` in the switch until it was added there. The compiler caught it; a plan that
  only listed three would not have.
- **`my-plan.spec.ts` fixtures were widened HERE, not in Phase 3.** Adding two required fields to
  `TrainingPlanItemView` stopped that suite typechecking the moment the models changed, so Phase 2
  could not be independently green without touching a Phase 3 file. Two `null`s and one muscle
  group; no behaviour, and Phase 3 builds on them.
- **Two shell specs unrelated to this slice were red on arrival** — `app.spec.ts` and
  `bottom-nav.spec.ts`, stale since commit `529520f "UI fixes"` changed the brand to an image and
  gave the nav tabs visible labels. They were fixed under a separate `fix(ui)` commit (`032ecdd`)
  rather than folded into this phase, so the slice's history stays about the slice.

### Success Criteria:

#### Automated Verification:

- Frontend unit tests pass: `npm test` from `src/app/`
- Formatting and linting pass: `npm run quality:check` from `src/app/`
- Production build succeeds within budget: `npm run build` from `src/app/`

#### Manual Verification:

- A trainer can save a plan with a duration and reopen it with the value intact
- Leaving the field blank saves a prescription with no duration, not a zero

---

## Phase 3: The member's card carries the whole prescription

### Overview

Four presentation changes on one screen, plus the shared icon they both need. This is the phase the
milestone exists for.

### Changes Required:

#### 1. Icon primitive

**Files**: `src/app/src/app/shared/icons/icon.ts`, `src/app/src/app/shared/icons/icon.html`

**Intent**: Add the info glyph both the navigation control and the note callout use.

**Contract**: `'info'` joins the `IconName` union and gains a case in the template, per the
component's own documented rule that this is the whole of adding an icon. It uses `currentColor` and
stays `aria-hidden` like its siblings — the *control* carries the accessible name.

#### 2. Card markup

**File**: `src/app/src/app/features/my-plan/my-plan.html`

**Intent**: Rebuild the card so the name is text, the navigation is its own control, the muscle group
is visible, the duration joins the parameters, and the note reads as a note.

**Contract**:

- The exercise name becomes plain text. The `routerLink` moves onto a separate anchor containing only
  `<app-icon name="info" />`, carrying an `aria-label` naming the exercise (e.g.
  `Opis ćwiczenia: {{ item.exerciseName }}`) — an icon-only control must name itself, and a bare
  "Opis ćwiczenia" repeated down the list would be useless in a screen reader's element list.
- `@if (item.muscleGroup)` renders it beneath the name as a subdued caption. Guarded because
  `MuscleGroup` is nullable on `Exercise` and plenty of library entries may not carry one.
- `Czas` joins the parameter list after `Ciężar` and before `Przerwa`, rendered as
  `{{ item.durationSeconds }} s` to match `Przerwa`. Guarded by the same `!== null` test as its
  siblings.
- The note becomes a callout: a container carrying `<app-icon name="info" />` followed by the note
  text, giving the `(i) Treść uwagi` form. The icon here is decorative — the text is the content — so
  it stays `aria-hidden` with no label.

#### 3. Card logic

**File**: `src/app/src/app/features/my-plan/my-plan.ts`

**Intent**: Teach the parameter guard about duration, and import the icon.

**Contract**: `hasParameters()` gains `item.durationSeconds !== null`. Without it a duration-only
prescription renders an empty card — see Critical Implementation Details. `Icon` joins the
component's `imports`.

#### 4. Card styling

**File**: `src/app/src/app/features/my-plan/my-plan.scss`

**Intent**: Style the new elements, and correct the rationale the old rule carried.

**Contract**:

- `.my-plan-link` is replaced by rules for a plain name and a small icon-only anchor. **Its existing
  comment must be rewritten, not carried over**: it currently justifies a large tap target on the
  grounds of one-handed use between sets, and the new icon deliberately does not provide one. Leaving
  the comment would put a stated rationale next to code that contradicts it. Replace it with what is
  now true, including that the small target is an accepted tradeoff.
- A caption rule for the muscle group, reusing `var(--muted)` and the `0.8125rem` size
  `.my-plan-label` already uses.
- A note-callout rule: rounded background, padding, and the icon aligned to the first line of text.
  Keep `white-space: pre-line` and `overflow-wrap: anywhere` from the current `.my-plan-note` — the
  trainer's line breaks are content.

#### 5. Tests

**File**: `src/app/src/app/features/my-plan/my-plan.spec.ts`

**Intent**: Pin the rendering rules that are easy to break silently.

**Contract**: The muscle group renders when present and is absent from the DOM when null; the duration
renders in the parameter list; **a prescription carrying only a duration still renders its parameter
list** (the `hasParameters` regression); the info anchor carries the exercise route and an
exercise-specific accessible name; the note renders inside the callout with its icon.

**Adapted during implementation (Phase 3).**

- **The note callout's background is derived from `--ink`, not a new token.** The first pass reached
  for `--surface-2` / `--radius-2`; neither exists. `styles.scss` builds `--line` and `--muted` with
  `color-mix(... var(--ink) N%)` under a stated rule that the page "never picks up a grey that is
  not part of the identity", so the callout follows it and uses `--radius-sm`.
- **`itemNames()` in the spec now reads `.my-plan-name`.** It selected `.my-plan-link`, which this
  phase deletes; two existing tests depended on it.
- **Phone width was verified by constraining the container, not by resizing the window.** The
  browser window is maximised at 2560 px and `resize_window` had no effect on it. The card carries
  no media queries — its layout is pure flex — so the container was narrowed to 360 px, giving a
  288 px card, narrower than any phone. Recorded because it is a substitute for the stated check,
  not the check itself.

### Success Criteria:

#### Automated Verification:

- Frontend unit tests pass: `npm test` from `src/app/`
- Formatting and linting pass: `npm run quality:check` from `src/app/`
- Production build stays inside the initial-bundle budget: `npm run build` from `src/app/`

#### Manual Verification:

- On a phone-width viewport, the card reads cleanly: name, muscle group, parameters, note callout
- The info icon opens the exercise description for the right exercise
- An exercise prescribed with only a duration shows that duration
- The note is visually distinct from the parameters above it

---

## Testing Strategy

### Unit Tests

- Plan builder: duration round-trips; blank submits `null`; the control carries the server's bounds.
- Plan card: muscle group present/absent; duration rendered; duration-only prescription renders;
  info anchor route and accessible name; note callout.

### Integration Tests

- Assign a plan with a duration, read it back through the trainer's edit load and through the
  member's own read — the same projection, two endpoints.
- Bounds: `0` and `3601` refused as `invalid_duration`; `1` and `3600` accepted.

### Manual Testing Steps

1. Apply the migration; `GET /health`.
2. As a trainer, assign a plan with a plank prescribed as 3 sets x 45 s and no weight.
3. As that member, open `/my-plan` on a phone-width viewport — the plank shows `Czas 45 s`, its
   muscle group, and no weight row.
4. Tap the info icon — the exercise description opens for the plank.
5. Add a note to the plank as the trainer; confirm it renders in the callout with the icon.
6. Roll the migration back and forward against the docker database.

## Performance Considerations

None material. The two new view fields ride the projection that is already issued —
`i.Exercise.MuscleGroup` traverses a navigation the same projection already traverses for
`i.Exercise.Name`, so it adds no join. The icon adds roughly one SVG path to a component that renders
in the eager shell; the build-budget check in each phase's automated verification is what confirms it.

## Migration Notes

One additive, nullable column. No backfill: absent is the correct value for every existing row. The
`Down` drops the column and loses only durations entered after it applied, which is the ordinary cost
of reversing an additive column and should be stated in the migration's doc comment.

## Open Risks & Assumptions

- **The info icon's tap target is a deliberate, accepted regression.** `my-plan.scss` currently
  justifies making the whole exercise name the link because the screen "is read between sets, often
  one-handed, and a small tap area is the difference between usable and not". The chosen design
  replaces that with a visually small icon at its natural size, without padding it out to a 44x44
  target. This was offered with the tradeoff stated and chosen anyway; it is recorded here so a future
  reviewer reads it as a decision rather than an oversight. If it proves awkward in real one-handed
  use, the cheapest reversal is padding on the anchor — no markup change.
- **`MuscleGroup` is free text, not an enum** (`Exercise.MuscleGroup` is `string?` with a 100-char
  cap). The card renders whatever the library holds, so consistency of the displayed value depends on
  how exercises were entered, not on anything this change can enforce.
- **Assumption: no exercise-side "is timed" flag is wanted.** The card shows whichever parameters are
  set, so a trainer who fills both weight and duration gets both. Nothing prevents a nonsensical
  combination, matching how the existing four parameters already behave.

## References

- Roadmap slice: `context/foundation/roadmap.md` → S-15, milestone M-3 (anchors MS-001–MS-004)
- One-shape-two-consumers contract: `src/Application/Training/TrainingPlanEndpoints.cs:41`
- The single projection: `src/Infrastructure/Training/TrainingPlanQuery.cs:128`
- Bounds-validation precedent: `src/Application/Training/TrainingPlanEndpoints.cs:487-549`
- Icon primitive's own rules: `src/app/src/app/shared/icons/icon.ts`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename
> step titles. See `references/progress-format.md`.

### Phase 1: The contract carries duration and muscle group

#### Automated

- [x] 1.1 Backend builds warning-free: `dotnet build` from `src/` — eacacd1
- [x] 1.2 Integration tests pass: `dotnet test` from the repo root — eacacd1
- [x] 1.3 Migration is reversible: down to the previous migration and forward again — eacacd1
- [x] 1.4 Generated SQL shows the column nullable, with no `defaultValue` — eacacd1

#### Manual

- [x] 1.5 `GET /health` returns healthy after the migration applies — eacacd1

### Phase 2: The trainer can prescribe a duration

#### Automated

- [x] 2.1 Frontend unit tests pass: `npm test` from `src/app/` — ef03739
- [x] 2.2 Formatting and linting pass: `npm run quality:check` from `src/app/` — ef03739
- [x] 2.3 Production build succeeds within budget: `npm run build` from `src/app/` — ef03739

#### Manual

- [x] 2.4 A duration saves and reopens intact — ef03739
- [x] 2.5 A blank duration saves as no duration, not a zero — ef03739

### Phase 3: The member's card carries the whole prescription

#### Automated

- [x] 3.1 Frontend unit tests pass: `npm test` from `src/app/` — 94f6218
- [x] 3.2 Formatting and linting pass: `npm run quality:check` from `src/app/` — 94f6218
- [x] 3.3 Production build stays inside the initial-bundle budget: `npm run build` from `src/app/` — 94f6218

#### Manual

- [x] 3.4 The card reads cleanly at phone width — 94f6218
- [x] 3.5 The info icon opens the right exercise's description — 94f6218
- [x] 3.6 A duration-only prescription shows its duration — 94f6218
- [x] 3.7 The note is visually distinct from the parameters — 94f6218
