# E2E: attendance and a trainer's plan — Implementation Plan

## Overview

Two browser-level specs for roadmap S-32, on the pattern S-31 established (`support/fixtures`,
`club` builders, persona contexts):

1. **Attendance** — a trainer marks a booked member "Nieobecny" on a class that has started; the
   member sees the class as absent in Historia and the entry back on their karnet
   (`ATT-01`, `ATT-02`, `MBR-02`).
2. **Plan** — a trainer builds a plan in the builder (library search, add, parameters, a real drag
   to reorder, save); the member sees it on Mój plan in that order with those parameters, and opens
   an exercise's detail from it (`PLAN-01`, `MBR-05`, `MBR-06`).

Before them, the support layer learns three things it cannot do today: produce a started class,
survive cleaning one up, and create exercises.

## Current State Analysis

- **A started class cannot be created, but can be edited into existence.** `CreateClass.cs:36`
  refuses `starts_in_past`; `UpdateClass.cs` deliberately does not ("Correcting a class that already
  ran is a legitimate thing for an admin to do"). Booking needs a future class
  (`BookingProtocol.cs:93`, `class_started`), so the only deterministic order is: create future →
  book → `PUT` the start into the past. Moving a booked class enqueues a change notification; locally
  that is `LoggingEmailSender` (log only).
- **Attendance** is `PUT /api/admin/classes/{classId}/bookings/{bookingId}/attendance`
  (`BookingEndpoints.cs:106`), refused with `class_not_started` before the start
  (`RecordAttendance.cs:106`). In the SPA it is two `aria-pressed` buttons, "Obecny" / "Nieobecny",
  inside a group named `Obecność: <displayName>` in the bookings overlay
  (`class-bookings-overlay.html:87-112`), with the tally `Obecni: X · Nieobecni: Y · Nieoznaczeni: Z`.
- **Cleanup would fail on a started class.** `Club.removeClass` deletes, and on 409 cancels. A
  started class with bookings answers 409 to both (`CancelClass.cs:78`, `class_started`), and
  `Cleanup.runAll` throws "Cleanup left data behind" — the test would go red after passing.
- **The staff schedule reads the past.** `GetSchedule.cs:35`: a `from` in the past is legitimate,
  so the trainer's `/schedule` shows last week. `showWeekOf` (`support/slots.ts`) only steps
  forward; both the phone strip and the desktop calendar label the back step "Poprzedni tydzień".
- **A karnet issued today would not cover a class moved to last week.** `club.issuePass` uses
  `validFrom = today`; `IssuePass` accepts a past `validFrom` (no rule against it in
  `MembershipPassRules.cs`).
- **Member screens.** Historia is the second tab (`role="tab"`, "Historia") of `/my-classes`
  (`my-classes.html`); each row shows the outcome word ("Nieobecny") with an icon
  (`attendance-history.html:40-47`). The karnet balance is on Start, region
  `Twoje zajęcia i karnet`, as `N z M` (asserted that way in `staff-booking-reaches-member.spec.ts`).
- **Exercises cannot be deleted**, only deactivated (`ExerciseEndpoints.cs`, `DeactivateExercise.cs`,
  idempotent). Names must be unique among active exercises (`name_taken`). Plans cannot be deleted
  either (`TrainingPlanEndpoints.cs`: "NO DELETE").
- **The builder** (`trainer/plans/plan-builder.html`) is reached from `/trainer/members` (search
  "Szukaj członka", a link on the member's name). Library: search box "Szukaj ćwiczenia", one button
  per pickable exercise. Each row has the fields labelled by `shared/plan-parameters` (ids
  `sets-<i>`, `reps-<i>`, `weight-<i>`, `duration-<i>`, `rest-<i>`, `note-<i>`) and a cdk drag
  handle that is `aria-hidden` but carries `title="Przeciągnij"`. Save is "Przypisz plan", then the
  toast "Plan zapisany." and the screen stays put. The members list then reads `Plan: <name>`.
- **`onDrop` has no unit test** (`plan-builder.spec.ts` never drives it) — the drag exists only in
  the rendered UI, which is why it is driven here.
- **Mój plan** (`my-plan/my-plan.html`) is an `<ol>` of cards, in plan order, each with a link named
  `Opis ćwiczenia: <name>` to `/my-plan/exercises/:id`. The detail (`plan-exercise-detail.html`)
  has the name as `h1` (hidden on a phone only), sections headed `Opis`, `Przygotowanie`,
  `Pozycja startowa`, `Wykonanie` (`shared/exercise-view/exercise-view.ts:74-77`), and
  "Wróć do planu".

## Desired End State

- `npx playwright test e2e/absence-returns-karnet-entry.spec.ts e2e/trainer-plan-reaches-member.spec.ts`
  is green, twice in a row on the same database, and with `--repeat-each=2` in parallel.
- The full suite (`npm run e2e`) stays green: no existing spec regresses on the support changes.
- `e2e/CLAUDE.md` tells the next spec author how to get a started class and an exercise, and what
  stays behind.

### Key Discoveries:

- `UpdateClass.cs` "No starts_in_past check here" — the door the attendance spec uses.
- `CancelClass.cs:78` — why a started class must be left behind rather than cancelled.
- `GetSchedule.cs:35` — a past window is legitimate, so the trainer can open the moved class.
- `class-bookings-overlay.html:89` — `aria-label="'Obecność: ' + displayName"` scopes the toggle to
  one person without a test id.
- `plan-builder.html` drag handle: `cdkDragHandle aria-hidden="true" title="Przeciągnij"`.

## What We're NOT Doing

- No production code changes. If a spec exposes a bug, it is recorded and fixed as its own change.
- Not driving the booking through the UI in the attendance spec — S-31 covers that; here it is
  arranged through the API.
- Not the full `ATT-02` round trip (absent → present re-spends); `RecordAttendance`'s re-spend is
  covered by integration tests.
- Not `ATT-03` to `ATT-06`, `PLAN-02` to `PLAN-06`, `MBR-07`, `MBR-08`.
- Not the video player (`MBR-06`'s YouTube playback) — it stays in the manual plan so the local
  gate never depends on an external network.
- Not all five parameters on every row; field mapping is covered by the builder's and card's unit
  specs.
- Not the phone drag (`PLAN-01` step 4 "także na telefonie") — the builder spec runs at the desktop
  default viewport.
- No new cleanup for plans or members — they have no delete endpoint and stay behind, named `E2E …`.

## Implementation Approach

Arrange through the admin API with `club`, drive through the UI only where the risk lives
(attendance buttons, builder, member screens), one persona per context. The attendance spec runs
under `test.use(PHONE)` so it can reuse `openClassBookings`; the plan spec runs at the desktop
default, where the builder's drag and the detail's `h1` are both in view.

## Critical Implementation Details

- **Ordering in the attendance spec is load-bearing:** karnet (valid from a week ago) → class →
  book → `startClass`. Booking after the move is refused (`class_started`); the karnet must precede
  the class so the class's removal runs first and records the booked member (existing
  `bookedMembers` rule).
- **The drag:** cdk starts a drag only after the pointer moves past its threshold, and tracks
  intermediate moves. Drive it with `hover` on the handle, `mouse.down()`, a `mouse.move()` to the
  target row with `steps` (≥10), and `mouse.up()` — not `dragTo` alone. Assert the builder's order
  before saving, so a failed drag fails there rather than on the member's screen.
- **Unstable drag fallback:** if the drag cannot be made reliable (fails a `--repeat-each=5` run),
  the spec keeps the add order, an `onDrop` unit test goes into `plan-builder.spec.ts`, and this
  plan's Phase 3 contract gets an "**Adapted during implementation.**" note (lessons.md).

## Phase 1: Support layer for started classes and exercises

### Overview

Teach `support/` to make a started class, clean up around one, and create exercises. No spec yet.

### Changes Required:

#### 1. Past slots and the week step back

**File**: `src/app/e2e/support/slots.ts`

**Intent**: A past twin of `randomClassStart` and a `showWeekOf` that steps back as well as forward.

**Contract**: `randomPastClassStart(): Date` — 1 to 6 days ago, same 07:00–20:45 grid on 15 minutes,
local time. `showWeekOf(page, startsAt)` clicks "Poprzedni tydzień" when `startsAt`'s Monday is
before the current week's, "Następny tydzień" when after (at most one step either way). Update the
file header: the "never today" rule now has a stated exception for classes that are meant to have
started.

#### 2. Karnet valid from the past

**File**: `src/app/e2e/support/club.ts`

**Intent**: Let a karnet cover a class moved into last week.

**Contract**: `issuePass(memberId, entries = 5, validDays = 30, options?: { validFromDaysAgo?: number })`
— `validFrom = today − validFromDaysAgo` (default 0), `validTo` still `today + validDays`. Existing
callers unchanged.

#### 3. Starting a booked class

**File**: `src/app/e2e/support/club.ts`

**Intent**: Move a booked class's start into the past through the admin edit, retrying on the
club-wide overlap rule.

**Contract**: `CreatedClass` gains `classTypeId`, `durationMinutes`, `instructorMemberId` (what the
`PUT` body needs). New `startClass(created: CreatedClass): Promise<CreatedClass>` — up to
`SLOT_ATTEMPTS` `PUT /api/admin/classes/{id}` with `randomPastClassStart()`, continuing on 409
`time_conflict`; returns the class with its new `startsAt`. Documented as: call after every booking,
because a started class takes none.

#### 4. Leaving a started class behind

**File**: `src/app/e2e/support/club.ts`

**Intent**: A started class is history the API keeps; cleanup must not report it as a failure.

**Contract**: `removeClass`: delete → on 409 cancel → on 409 `class_started`, return nothing (stays
behind by decision). Any other refusal still fails cleanup. The class type is still deactivated.
Update `createClass`'s docblock and the file header's "what stays behind" list.

#### 5. Exercises

**File**: `src/app/e2e/support/club.ts`

**Intent**: A library entry a spec can find by a unique name, retired after the test.

**Contract**: `createExercise(name: string, fields?: Partial<{ description; muscleGroup; preparation;
startingPosition; execution }>): Promise<string>` — `POST /api/admin/exercises` (all other
`ExerciseRequest` fields `null`), registers `POST /api/admin/exercises/{id}/deactivate` as its
removal. Returns the id.

#### 6. The rules file

**File**: `src/app/e2e/CLAUDE.md`

**Intent**: The next author learns the two new builders and what they leave behind.

**Contract**: Under "Arranging data": a started class is `createClass` → `book` → `startClass`
(never a sleep, never a fixed time), and it stays behind as history; a karnet for it needs
`validFromDaysAgo`; exercises through `club.createExercise`, named `E2E …`, deactivated in cleanup;
plans stay behind on their member.

### Success Criteria:

#### Automated Verification:

- Lint and format pass: `npm run quality:check` (from `src/app/`)
- The existing suite stays green on the changed support layer: `npm run e2e` (from `src/app/`)

#### Manual Verification:

- `e2e/CLAUDE.md` reads correctly for someone who has not seen this plan

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation before proceeding to the next phase.

---

## Phase 2: Attendance spec — an absence returns the entry

### Overview

`absence-returns-karnet-entry.spec.ts`: one test, trainer and member personas, phone width.

### Changes Required:

#### 1. The spec

**File**: `src/app/e2e/absence-returns-karnet-entry.spec.ts`

**Intent**: Prove that a trainer's "Nieobecny" on a started class reaches the member's two screens:
the class reads absent in Historia, and the karnet is one entry up again.

**Contract**: Provenance header (risk: an absence recorded in the overlay does not return the entry
or does not reach Historia — `ATT-01`, `ATT-02`, `MBR-02`, test-plan risk on derived entries; seed:
`seed.spec.ts`; what it creates and what stays behind). `test.use(PHONE)`. Steps:

1. Arrange: member `E2E nieobecność <ts>` with an `@example.test` account; karnet of 5 with
   `validFromDaysAgo: 7`; class `E2E klasa obecności <ts>`; `club.book`; `club.startClass`.
2. Member: Start shows `4 z 5` in `Twoje zajęcia i karnet` — the unmarked started class counts.
3. Trainer: `openClassBookings` on the moved class; in the group `Obecność: <name>` press
   "Nieobecny"; it reads `aria-pressed="true"` and the tally shows `Nieobecni: 1`.
4. Member (same context, reloaded): Start shows `5 z 5`; `/my-classes`, tab "Historia", the row of
   the class carries "Nieobecny".

### Success Criteria:

#### Automated Verification:

- The spec passes: `npx playwright test e2e/absence-returns-karnet-entry.spec.ts`
- It passes again on the same database, and in parallel with itself:
  `npx playwright test e2e/absence-returns-karnet-entry.spec.ts --repeat-each=3`
- Lint and format pass: `npm run quality:check`

#### Manual Verification:

- The spec fails when the outcome is broken: temporarily make `RecordAttendance` ignore
  `absent` (or assert `4 z 5` after the mark) and see it go red at the right step, then revert
- The trace (`--trace on`) shows no sleeps and no fixed times

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation before proceeding to the next phase.

---

## Phase 3: Plan spec — a trainer's plan reaches the member

### Overview

`trainer-plan-reaches-member.spec.ts`: one test, trainer and member personas, desktop default.

### Changes Required:

#### 1. The spec

**File**: `src/app/e2e/trainer-plan-reaches-member.spec.ts`

**Intent**: Prove that a plan built in the builder — searched, added, parametrised, reordered by
drag, saved — is what the member reads, in that order, with those parameters, and that its exercise
detail opens from it.

**Contract**: Provenance header (risk: the builder's order or a row's parameters do not survive the
drag and the save, or the member's plan links to the wrong detail — `PLAN-01`, `MBR-05`, `MBR-06`;
seed; what stays behind: member, account, plan; exercises deactivated). Steps:

1. Arrange: member `E2E plan <ts>` with an account and no plan; three exercises
   `E2E ćw A <ts>`, `E2E ćw B <ts>`, `E2E ćw C <ts>` — A with a description and an execution text.
2. Trainer: `/trainer/members`, search the member, open them; name the plan `E2E plan <ts>`; search
   the library by `<ts>` and add A, B, C in that order; fill one parameter set per row — A: sets and
   reps `8-12`; B: weight; C: duration, plus a note.
3. Drag C's handle (`getByTitle('Przeciągnij')` inside C's row) above A; the builder now lists
   C, A, B.
4. "Przypisz plan" → toast "Plan zapisany."; `/trainer/members` shows `Plan: E2E plan <ts>` for
   the member.
5. Member: `/my-plan` shows the plan's name, cards in order C, A, B; C carries its duration and the
   note, A its sets and `8-12`, B its weight (values as `shared/plan-parameters` renders them).
6. Member: "Opis ćwiczenia: E2E ćw A <ts>" opens the detail — `h1` with the name, `Opis` and
   `Wykonanie` with A's texts; "Wróć do planu" returns to `/my-plan`.

**Adapted during implementation.**
- Step 4 waits on the save's `POST /api/trainer/plans` response (`waitForResponse`), not on the
  toast. The toast host is `aria-hidden`, and its words are on the page a second time in CDK's
  visually hidden LiveAnnouncer region, so `getByText('Plan zapisany.')` is ambiguous. The business
  outcome is still asserted: `Plan: <name>` on Członkowie.
- The plan is named `E2E plan treningowy <ts>`, so it differs from the member's `E2E plan <ts>`.
- The spec sets a 1280×1600 viewport, so all three builder rows are in view for the drag.
- Both specs take `<ts>` from `uniqueSuffix()` (`support/club.ts`), not `Date.now()`. Under
  `--repeat-each=5`, two parallel workers got the same millisecond and created two members with the
  same name. The S-31 specs still use `Date.now()` and carry the same latent risk; they are not
  changed here.

### Success Criteria:

#### Automated Verification:

- The spec passes: `npx playwright test e2e/trainer-plan-reaches-member.spec.ts`
- The drag is stable: `npx playwright test e2e/trainer-plan-reaches-member.spec.ts --repeat-each=5`
- The whole suite is green: `npm run e2e`
- Lint and format pass: `npm run quality:check`

#### Manual Verification:

- The spec fails when the order is broken: temporarily make `onDrop` a no-op and see it go red at
  the builder's order assertion, then revert
- Roadmap S-32 and the manual cases it protects read correctly against what was built

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation.

---

## Testing Strategy

### Unit Tests:

- None planned. Only if the drag proves unstable: an `onDrop` test in `plan-builder.spec.ts`
  (see Critical Implementation Details).

### Integration Tests:

- None new. Attendance's entry arithmetic and `class_not_started` are already covered in
  `tests/po-prostu-silka.Tests/`.

### Manual Testing Steps:

1. Run each new spec twice on the same local database; both runs green.
2. Break each outcome once (absence ignored; `onDrop` no-op) and confirm the matching spec fails
   at the step named after the risk.
3. Open the local trainer schedule's previous week and confirm leftover `E2E klasa obecności …`
   classes are the only thing the attendance spec leaves on it.

## Performance Considerations

Each spec opens three contexts (admin API, trainer, member). Expect ~10–20 s each; the pre-push gate
grows by that much.

## Migration Notes

None — no schema or production change.

## References

- Roadmap: `context/foundation/roadmap.md`, S-32
- Precedent: `context/archive/2026-09-28-e2e-member-onboarding-and-booking/plan.md`,
  `src/app/e2e/staff-booking-reaches-member.spec.ts`
- Rules: `src/app/e2e/CLAUDE.md`, `context/foundation/test-plan.md`
- Manual cases: `context/testing/04-schedule-bookings-attendance.md` (ATT-01, ATT-02),
  `context/testing/05-exercises-and-plans.md` (PLAN-01), `context/testing/02-member-screens.md`
  (MBR-05, MBR-06)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Support layer for started classes and exercises

#### Automated

- [x] 1.1 Lint and format pass: `npm run quality:check` (from `src/app/`) — 3fb2336
- [x] 1.2 The existing suite stays green on the changed support layer: `npm run e2e` (from `src/app/`) — 3fb2336

#### Manual

- [x] 1.3 `e2e/CLAUDE.md` reads correctly for someone who has not seen this plan — 3fb2336

### Phase 2: Attendance spec — an absence returns the entry

#### Automated

- [x] 2.1 The spec passes: `npx playwright test e2e/absence-returns-karnet-entry.spec.ts` — 5664cde
- [x] 2.2 It passes again on the same database, and in parallel with itself: `--repeat-each=3` — 5664cde
- [x] 2.3 Lint and format pass: `npm run quality:check` — 5664cde

#### Manual

- [x] 2.4 The spec fails when the outcome is broken, at the right step
- [x] 2.5 The trace shows no sleeps and no fixed times

### Phase 3: Plan spec — a trainer's plan reaches the member

#### Automated

- [x] 3.1 The spec passes: `npx playwright test e2e/trainer-plan-reaches-member.spec.ts` — c9249ea
- [x] 3.2 The drag is stable: `--repeat-each=5` — c9249ea
- [x] 3.3 The whole suite is green: `npm run e2e` — c9249ea
- [x] 3.4 Lint and format pass: `npm run quality:check` — c9249ea

#### Manual

- [x] 3.5 The spec fails when the order is broken, at the builder's order assertion
- [x] 3.6 Roadmap S-32 and the manual cases it protects read correctly against what was built
