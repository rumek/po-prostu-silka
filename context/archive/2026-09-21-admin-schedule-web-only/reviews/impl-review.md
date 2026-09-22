<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: The Admin's Class Calendar Is a Desk Tool (S-20)

- **Plan**: context/changes/admin-schedule-web-only/plan.md
- **Scope**: Phases 1–4 of 4 (automated steps complete; manual steps pending)
- **Date**: 2026-09-21
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 4 warnings, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

Automated evidence, 2026-09-21, on Node 24.15:

- `npm run quality:check` passes: Prettier is clean and every file passes lint.
- `npm test`: 63 files and 714 tests pass.
- `npm run build`: the initial bundle is 513.15 kB against the 600 kB warning, and the `classes` lazy chunk is 28.76 kB.
- The width-literal grep matches only `_breakpoints.scss`.
- `classActions` has no matches anywhere.

The shell defaults to Node 18.16, which the Angular CLI refuses. The checks were run with `/c/nvm/v24.15.0` on the PATH.

Benign extras beyond the plan:

- a non-vacuity test in `breakpoints.spec.ts`;
- a "Zamknij" button on the overlay's actions step;
- a "keeps the overlay open when an action fails" spec.

## Findings

### F1 — A late duplicate or delete response closes an overlay opened afterwards

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/app/src/app/features/admin/classes/classes.ts:337, :356, :379
- **Detail**:
  - The writes that land after the request:
    - `duplicate()` runs `selected.set(null)` and then `await this.reload()`. That goes through `load()`, and `load()` calls `closeTransient()`, which also wipes `viewingBookings`, `drawn` and `deleteBlockedBy`.
    - `remove()` runs `selected.set(null)` unconditionally on success.
  - Neither write checks which overlay is open when the response lands. `remove()` is also unfenced.
  - While a request is in flight, the overlay can still be closed with Zamknij, Escape or the backdrop, because none of these respect `busy`. The admin can then open another class's overlay, or draw a class and start filling in the create overlay.
  - When the response lands, that newer overlay vanishes, and a half-filled create form is lost with it.
  - The same code existed with the panels below the calendar, but the overlay flow makes the race much easier to hit.
  - `cancel()` is fenced against window changes, but it has the same `selected.set(null)` issue.
- **Fix A ⭐ Recommended**: Close only what the response belongs to, and refresh rows after a duplicate without `closeTransient()`.
  - Approach: close with `selected.update(s => s?.id === row.id ? null : s)` in duplicate, remove and cancel, and split a `refetchRows()` out of `load()` that does not call `closeTransient()`. `reload()` uses it after a duplicate. The retry path keeps `load()`.
  - Strength: fixes the race at its source. `afterRelease` and `afterAdminBooking` already use the "only if it is still the same id" pattern.
  - Tradeoff: `load()` gains a sibling, and the fence handling has to be shared between the two.
  - Confidence: HIGH. The pattern already exists in this file.
  - Blind spot: none of the specs covers "close mid-request, open another". Add one per flow.
- **Fix B**: Disable closing (Zamknij, Escape, backdrop) while `busy` is set.
  - Strength: a small change, local to the overlay.
  - Tradeoff: the admin is stuck behind a slow request, and the create overlay can still be reached by keyboard (see F5).
  - Confidence: MED.
  - Blind spot: an unresponsive Escape key is an accessibility smell.
- **Decision**: FIXED (Fix A). `fetchRows()` was split out of `load()`, so `reload()` no longer closes overlays. Duplicate, remove and cancel now go through `closeIfShowing(row)`. Two specs were added to `classes.spec.ts`; both fail on the old code and pass now.

### F2 — The overlay's failure marker uses hard-coded words and a second `role="alert"`

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/app/src/app/features/admin/classes/class-actions-overlay.html:158-160
- **Detail**:
  - The markup is `<p class="field-error" role="alert">Nie udało się. Spróbuj ponownie.</p>`, carried over from the old tile.
  - It breaks the S-19 rule that words come from a table. The sibling `class-bookings-overlay.html:50` renders `{{ failure() }}` instead.
  - The screen also raises a toast carrying the real reason. So one failure produces two `role="alert"` announcements, and the one inside the overlay says less.
  - It is styled as `.field-error` but belongs to no field.
- **Fix**: Replace `failed: boolean` with a `failure: string | null` input fed from the screen's `messageFor(failure)`, and render it without `role="alert"` (the toast announces). Alternatively, drop the text and keep only a visual marker.
- **Decision**: FIXED. The screen now holds `failed: { id, message }` plus `selectedFailure` (computed), and the overlay's `failure` input renders the toast's own sentence without `role="alert"`. Specs updated in both files.

### F3 — `invalid_weeks` goes to a toast although the weeks field is on screen

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/app/src/app/features/admin/classes/classes.ts:360-363; class-actions-overlay.html (weeks `app-field`)
- **Detail**:
  - The weeks input now sits in an `app-field` inside the overlay, so under the four-outlets rule this refusal names a control in front of the admin, which is outlet 1 (a field error).
  - `min`/`max` are not enforced client-side, so typing 20 reaches the server, and the refusal comes back as a toast that is detached from the field.
  - Before S-20 the input lived in a panel below the calendar, so the toast was the defensible outlet then.
- **Fix**: Validate 1–8 in the overlay before emitting `duplicateRequested`, and render the server's `invalid_weeks` as the field's error with words from `classFailureMessage`.
- **Decision**: FIXED.
  - The overlay's `confirmDuplicate()` validates 1–8 and shows `classFailureMessage('invalid_weeks')` under the field, wired with `aria-invalid` and `aria-describedby`.
  - The screen's special `invalid_weeks` → toast branch was removed. A server-side refusal can now only come from the two ranges diverging, and it falls through to the generic path (a toast plus the overlay marker, with the same words).
  - A new spec was added to `class-actions-overlay.spec.ts`.

### F4 — The keyboard-activation mechanism differs from the plan, and the plan was not updated

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: context/changes/admin-schedule-web-only/plan.md (Phase 3 §2, Critical Implementation Details); src/app/src/app/shared/calendar/schedule-calendar.ts:395-417
- **Detail**:
  - The plan says a click emits "when no pointerdown preceded it (keyboard)".
  - The code checks `event.detail !== 0` instead. The reason is documented at `:396-400`: a drag released off the tile would leave a stale press behind.
  - The code's approach is better than the plan's, but no **Adapted during implementation.** note records it. This is a direct hit on the lessons.md rule "Record necessary adaptations in the plan".
  - The spec pins "keyboard click with a stale press". Per the plan it should also pin "click with no press at all", and a mouse click (`detail` ≥ 1) after a stale press is untested.
  - `schedule-calendar.ts:104` (141 characters) is a run-on sentence left from the edit.
- **Fix**: Add the adaptation note to Phase 3 §2 and to Critical Implementation Details, add the two missing spec cases, and reflow line 104.
- **Decision**: FIXED.
  - plan.md now carries two "Adapted during implementation." notes.
  - Two specs were added: a keyboard click with no press, and a fresh press replacing a stale one.
  - The class doc sentence was reflowed.
  - Still untested: a synthesized click with `detail` ≥ 1 and no pointerdown before it (some assistive-technology paths). That case is unverified.

### F5 — `select()` does not clear `drawn`, so two modals can stack

- **Severity**: 🔍 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/app/src/app/features/admin/classes/classes.ts:268-273, :284-288
- **Detail**:
  - `openCreate()` clears `selected`, but `select()` and `openBookings()` do not clear `drawn`.
  - `useOverlayFocus` does not trap Tab. From the create overlay, the keyboard can therefore reach a tile behind it, and Enter opens the actions overlay on top: two `aria-modal` dialogs, each with its own Escape handler.
  - A related problem: when `selected` goes from row A straight to row B, `@if (selected(); as row)` keeps the component instance, so the overlay's `step` and `weeks` carry over. The target is still correct, because the copy shows B's name.
- **Fix**: Have every overlay opener call `closeTransient()` before setting its own signal, and key the overlay on `row.id` (or reset `step` when the id changes).
- **Decision**: FIXED.
  - `openCreate`, `select` and `openBookings` now call `closeTransient()` before setting their own signal.
  - The overlay renders through `@for (row of selectedKeyed(); track row.id)`.
  - Two specs were added to `classes.spec.ts`.

### F6 — The `desk` and `below-desk` mixins are unused, and 63.9999rem is hand-written

- **Severity**: 🔍 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/app/src/styles/_breakpoints.scss:49-60
- **Detail**:
  - No stylesheet includes either mixin, because the desk decision is made in TypeScript with `@if`. The plan asked for them anyway.
  - `below-desk` hard-codes `63.9999rem` rather than deriving it from `$desk-min-width`, so the sync spec would not catch the pair drifting apart.
- **Fix**: Derive it as `$desk-min-width - 0.0001rem`.
- **Decision**: FIXED. `below-desk` now uses `#{$desk-min-width - 0.0001rem}`, and a probe compile still emits `max-width: 63.9999rem`. The mixins stay, because the plan asked for them.

### F7 — All 13 manual verification steps are still pending

- **Severity**: 🔍 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: plan.md Progress 1.5–1.6, 2.4–2.6, 3.4–3.5, 4.5–4.9
- **Detail**:
  - Nothing is rubber-stamped: every manual item is honestly `[ ]`.
  - Every phase carried a "pause for manual confirmation" note, yet all four phases were committed back to back.
  - The browser-only risks (click vs drag on a real `angular-calendar`, touch scrolling on a tablet, the 480px nav partition) are exactly what the unit specs cannot see.
- **Fix**: Run the Testing Strategy's manual steps (at minimum 2, 4 and 5) before archiving, or take click-vs-drag to `/10x-e2e` as the plan suggests.
- **Decision**: QUEUED. The user will run the steps. The checklist is in `follow-ups/review-fixes.md`, with two extra checks added for the F1 and F3 fixes.

## Triage summary (2026-09-21)

- Fixed: F1 (Fix A), F2, F3, F4, F5, F6
- Queued for the user: F7 (manual verification)
- After the fixes, run on Node 24.15:
  - `quality:check` passes;
  - `npm test`: 721/721 pass (+7 specs);
  - `npm run build`: the initial bundle is 513.15 kB.
