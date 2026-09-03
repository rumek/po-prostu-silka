<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Member and admin browse the schedule as a calendar

- **Plan**: `context/changes/schedule-calendar-view/plan.md`
- **Scope**: Phases 1–5 of 5 (full plan)
- **Date**: 2026-09-03
- **Verdict**: NEEDS ATTENTION → all 10 findings fixed during triage (2026-09-03)
- **Findings**: 0 critical, 6 warnings, 4 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

### Success criteria re-run (2026-09-03)

| Command | Result |
|---|---|
| `dotnet build` from `src/` | PASS — 0 warnings, 0 errors |
| `npm test` from `src/app/` | PASS — 199 tests, 21 files |
| `npm run quality:check` from `src/app/` | PASS |
| `npm run build` from `src/app/` | PASS — 470.30 kB initial (budget 500 kB); calendar in a 114.7 kB lazy chunk |
| `dotnet test` from repo root | PASS — 146 passed, 0 failed, 25 s (re-run once Docker Desktop was up; the first attempt failed at Testcontainers startup, not on any assertion) |

Manual steps 1.5–5.13 are all `[x]`. Every one has observable evidence in the diff; none look
rubber-stamped.

## Triage outcome

All ten findings were fixed. Verified after the last edit: `dotnet build` clean, `dotnet test`
146 passed, `npm test` 203 passed (7 new), `npm run quality:check` clean, `npm run build`
470.08 kB initial.

## Findings

### F1 — Nothing in this app tears anything down, and the calendar is the first component that needs it

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `src/app/src/app/shared/calendar/schedule-calendar.ts:319`, `:470-471`
- **Detail**: The `MediaQueryList` `change` listener is registered in the constructor and never
  removed. `grep -rn "DestroyRef|ngOnDestroy|takeUntilDestroyed" src/app/src/app` returns nothing —
  no component in this SPA has ever needed teardown. This slice changed that twice over: it made
  `/schedule` and `/admin/classes` lazy (`app.routes.ts:35-44`), so the calendar is now destroyed on
  every navigation away, and each visit leaves another listener holding a dead component instance.
  The drag gesture has the same shape at `:470-471` — `document.addEventListener('mousemove'/'mouseup')`
  is undone only inside `release`, so a route change mid-drag, or a pointer released outside the
  browser window, leaves both listeners attached for the life of the page.
- **Fix**: Inject `DestroyRef` and register both teardowns — `query.removeEventListener('change', handler)`
  for the media query, and the same pair the drag's `release` removes.
  - Strength: One idiom, both leaks, and it establishes the destroy pattern this SPA will need again
    the moment a second component subscribes to anything global.
  - Tradeoff: Requires naming the media-query handler instead of passing an inline arrow.
  - Confidence: HIGH — verified there is no teardown anywhere, and verified both routes are lazy.
  - Blind spot: Not measured how much is actually retained per navigation; the leak is certain, its
    size is not.
- **Decision**: FIXED

### F2 — The member schedule lost its retry control, which the plan said to keep

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `src/app/src/app/features/schedule/schedule.html`
- **Detail**: Plan Phase 2 §6 says, in as many words, "Keep the retry affordance on `loadFailed`."
  Before this slice `schedule.html` carried `<button class="link-button" (click)="load()">Spróbuj
  ponownie</button>` (verified at `af534dd:schedule.html:9`). It is gone. The member now sees only the
  calendar's own `<p class="alert">Nie udało się wczytać grafiku.</p>` with no click target, so the
  sole recovery from a failed load is a browser reload. The admin screen kept its own retry
  (`classes.html:100-104`), so the two failure paths diverged. No "Adapted" note covers the removal,
  and `schedule.spec.ts:122` only asserts `.alert` exists — the loss is untested in both directions.
- **Fix**: Restore the retry block in `schedule.html`, mirroring `classes.html`, and add a spec that
  clicks it and asserts a refetch.
- **Decision**: FIXED

### F3 — "What We're NOT Doing" still forbids the move/resize this slice shipped

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: `context/changes/schedule-calendar-view/plan.md:131-132`
- **Detail**: The guardrail list still reads "**No drag-to-move or drag-to-resize of existing
  classes.** Only drag-to-*create* on empty grid space was accepted. Moving an existing class stays an
  edit-form operation." The slice ships exactly that (`schedule-calendar.ts:229-230`,
  `classes.ts:152`). The work was authorised and recorded — `prd-v2.md` FR-020, the roadmap's S-07
  refs, and a Phase 4 "Added after manual verification" note — but the exclusion list was never
  reconciled, so two sections of one plan now contradict each other. This is the failure
  `lessons.md`'s accepted rule names: a plan left asserting something untrue, whose cost lands on the
  next reader and on every future review, which will re-flag working code as a defect.
- **Fix**: Amend the bullet in place to record that the exclusion was lifted after manual
  verification, pointing at FR-020 and the Phase 4 note.
- **Decision**: FIXED

### F4 — The optimistic rollback ignores the generation guard the rest of the screen uses

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `src/app/src/app/features/admin/classes/classes.ts:155,179`
- **Detail**: `reschedule()` snapshots `const before = this.rows()` and restores it wholesale on
  failure. Every other write path on this screen is fenced by the `generation` counter
  (`classes.ts:80,90`) precisely because a window change makes out-of-order responses reachable. This
  one is not: drag a class, navigate to another week, and a refused PUT restores the *previous*
  week's rows over the freshly loaded window — the calendar then shows classes outside the range it
  says it is showing, and the failure notice names a class that is not on screen.
- **Fix**: Capture `const generation = this.generation` before the call and skip both the rollback and
  the notice when `generation !== this.generation`.
- **Decision**: FIXED

### F5 — The gesture is mouse-only, on the view that exists because of phones

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Pattern Consistency
- **Location**: `src/app/src/app/shared/calendar/schedule-calendar.ts:433`, `.html:124`
- **Detail**: `startDraw` is bound to `mousedown`, with `mousemove`/`mouseup` on the document; there is
  no pointer or touch path. The day view exists specifically because the product is mobile-first
  (`prd-v2` FR-015), so on a touch device drag-to-create degrades to "tap creates one 30-minute
  class" through synthesised mouse events, and a real drag never extends the range. The library's own
  move/resize path does handle touch (`touchStartLongPress`), so the two gestures behave differently
  on the same screen. Note this is an admin-facing gap, and admins plausibly work at a desk — which is
  why this is a warning and not a defect.
- **Fix A ⭐ Recommended**: Switch `startDraw` to `pointerdown`/`pointermove`/`pointerup` with
  `setPointerCapture`.
  - Strength: One code path for mouse, pen and touch; `setPointerCapture` also removes the
    "released outside the window" hole in F1 for free.
  - Tradeoff: `elementFromPoint` under a captured pointer needs re-checking on a real device; the
    specs cannot cover it.
  - Confidence: MEDIUM — the API is right, but this repo has no pointer-event precedent to copy.
  - Blind spot: Not tested whether the page scrolls under a touch drag on the grid.
- **Fix B**: Leave it, and record drag-to-create as a desk-only affordance in `prd-v2` FR-019.
  - Strength: Honest about what ships; no untestable code added.
  - Tradeoff: The admin panel is then quietly desktop-only for its headline gesture.
  - Confidence: HIGH — nothing breaks, the tap path still creates a class.
  - Blind spot: Whether admins actually use a phone here is unknown.
- **Decision**: FIXED via Fix A — pointer events with `setPointerCapture`, plus `touch-action: none`
  on a drawable segment. The second half turned out to be load-bearing: without it the browser pans
  the page and cancels the gesture before a single move lands. `pointercancel` now ends a gesture
  without emitting.

### F6 — The two create surfaces validate the same two numbers differently

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/app/src/app/features/admin/classes/class-create-overlay.ts:36`
- **Detail**: `class-form.ts` uses `ReactiveFormsModule` with `Validators.min/max` and puts a refusal
  on the offending control. The overlay uses `FormsModule` with one-way `[ngModel]`/`(ngModelChange)`
  and `min`/`max` as bare HTML attributes, so nothing blocks a submit: an emptied number input yields
  `+'' === 0`, and the only feedback is the server's `invalid_duration` banner. The plan predicted
  exactly this class of drift ("It is a second create surface, which is a real cost: two forms
  drift") and pinned the *words* against it via `classFailureMessage` — but not the bounds.
- **Fix**: Add the two bounds to `canSubmit()` in the overlay, matching `class-form`'s limits.
- **Decision**: FIXED — the bounds are now exported from `class-form.ts` and imported, rather than
  retyped, so the two surfaces cannot drift apart again.

### F7 — `classFailureMessage` reads the prototype chain

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `src/app/src/app/core/scheduling/class-failure.ts:56`
- **Detail**: `reason in MESSAGES` matches inherited keys, so a server reason of `"constructor"`,
  `"toString"` or `"valueOf"` returns a *function* typed as `string` and renders as JS source instead
  of the `UNKNOWN` fallback the doc comment promises. The function's whole reason to take `unknown` is
  to survive a server one version ahead, which is the case that breaks it.
- **Fix**: `Object.hasOwn(MESSAGES, reason)` instead of `reason in MESSAGES`.
- **Decision**: FIXED

### F8 — `--breakpoint-week` is dead, and cannot do what its comment claims

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/app/src/styles.scss:120`
- **Detail**: `grep -rn "breakpoint-week"` finds the declaration and one comment referring to it —
  no stylesheet reads it, and `styles.scss` contains no `@media` rule at all. Worse, the comment says
  it exists "for stylesheet use" beside a TS twin because "a TS constant cannot appear in a media
  query" — but a CSS custom property cannot legally appear in a media condition either, so the token
  could never have played that role. `WEEK_VIEW_MIN_WIDTH` alone carries the number.
- **Fix**: Delete the custom property and trim `calendar-breakpoint.ts`'s comment to stop pointing at
  a twin that no longer exists.
- **Decision**: FIXED

### F9 — `applyTimesChanged` trusts the library's flags instead of re-checking `readOnly`

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `src/app/src/app/shared/calendar/schedule-calendar.ts:389`
- **Detail**: The handler refuses the past and out-of-grid drops but never checks `readOnly()`. It is
  unreachable today only because `draggable`/`resizable` are false on a read-only calendar. Its
  sibling `startDraw` (`:435`) checks `readOnly()` as its first statement, so the component gates the
  two gestures inconsistently — and the one that skips the check is the one that writes to an
  existing class.
- **Fix**: Add `if (this.readOnly()) return;` as the first line of `applyTimesChanged`.
- **Decision**: FIXED

### F10 — Three plan lines the implementation quietly outgrew

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `plan.md:455`, `plan.md:789-790`, `ClassEndpoints.cs:265-281`
- **Detail**: Three small places where the record is ahead of or behind the code. (1) Plan L455 says
  the event's `title` carries "name and instructor"; it carries `row.name` only — harmless, because
  the custom `eventTemplate` renders both from `meta`, but the line is stale. (2) The Testing Strategy
  lists a DST-week unit test; no spec exercises a week containing a transition, though `fitsInGrid`
  and the range computation are exactly the DST-sensitive code. (3) `GET /api/admin/classes` with no
  parameters still returns an unbounded list (`to == null`, no `Take`). That is deliberate and
  documented as backwards compatibility — but the SPA now always sends a window, so the unbounded
  path has no caller left and is pure surface.
- **Fix**: Correct the two plan lines; either write the DST spec or strike the bullet; and decide
  whether the admin endpoint's unbounded fallback still earns its keep.
- **Decision**: FIXED — all three. The plan line now says the title carries the name; two DST specs
  landed (a week measured in calendar days, and the grid edges on a 25-hour day), pinning the
  invariant that every boundary is a local midnight; and the admin fallback is bounded at
  `MaxRangeDays` rather than unbounded, which let `GetUpcomingForAdminAsync` drop its nullable `to`
  and the Infrastructure query drop its `to == null` branch. Two backend tests were re-pointed: the
  fallback test now asserts two different default widths, neither unbounded.
