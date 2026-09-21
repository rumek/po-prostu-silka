# The Admin's Class Calendar Is a Desk Tool (S-20) Implementation Plan

## Overview

This plan makes `/admin/classes` a tool meant for a desk.

- **Every width:** every per-class action (Edytuj, Powiel, Zapisani, Odwołaj/Usuń) is reachable at every class length. The actions move out of the clipped tile into an overlay the tile opens.
- **Below 64rem:** the screen renders a plain refusal with a way through to the read-only schedule, not a grid that neither scrolls nor responds to gestures.
- **Touch:** drawing a new class is offered only to a precise pointer, so the grid scrolls under a finger.
- **Breakpoints:** every width threshold in the SPA gets one definition, which both TypeScript and the stylesheets read.

This covers roadmap slice S-20, anchors M-7 UX-01 to UX-04. It narrows prd-v2 FR-019 to the desk.

## Current State Analysis

The full evidence is in `context/changes/admin-schedule-web-only/research.md`.

**The actions are clipped.** They are projected through `#classActions` into a `.calendar-tile` with `height: 100%; overflow: hidden` (`shared/calendar/schedule-calendar.scss:166-179`). Two numbers explain why:

- Tiles are 1px per minute, from the library defaults `hourSegmentHeight = 30` and `minimumEventHeight = 30` at `node_modules/angular-calendar/fesm2022/angular-calendar.mjs:3048,3052`.
- The tile body alone needs about 80px.

So the actions are clipped for any class shorter than about 90 minutes.

**The screen mixes two mechanisms.**

- Powiel, Usuń and Odwołaj open panels below the calendar (`features/admin/classes/classes.html:82-151`).
- Zapisani opens an overlay (`classes.html:167-174`).
- The member's `/schedule` uses `selectable` + `classSelected`. That makes the tile a `<button>` and opens `class-details-overlay`.
- Actions exist only on the non-selectable tile, because buttons nested in a button would be invalid HTML (`schedule-calendar.html:146-151`).

**`touch-action: none` covers the whole grid.** It sits on `.calendar-segment-drawable` (`schedule-calendar.scss:285-291`), which is bound to every future segment whenever `readOnly` is false (`schedule-calendar.html:121`). It was added deliberately as Fix A to F5 in `context/archive/2026-09-02-schedule-calendar-view/reviews/impl-review.md:117-147`. This plan reverses that decision on the user's instruction of 2026-09-20.

**Breakpoints have no shared source.**

- `30rem` is written as a literal in 11 stylesheets.
- `30.0001rem` appears once (`shared/bottom-nav/bottom-nav.scss:30`) and must partition the axis exactly with the `max-width: 30rem` rules.
- `40rem` appears once (`features/admin/exercises/exercise-form.scss:34`).
- `48rem` exists only in TS (`shared/calendar/calendar-breakpoint.ts:14-17`).
- There are no SCSS partials, no `@use` and no `stylePreprocessorOptions`.
- The only viewport read in TS is the calendar's guarded `matchMedia` (`schedule-calendar.ts:352-362`).

**The library does not suppress the click that ends a drag.** `angular-calendar` 0.32.2 has none of this logic: `dragEnded` at `angular-calendar.mjs:3407` only emits `eventTimesChanged`. The tile element itself moves during a drag (`snapDraggedEvents`, so `ghostDragEnabled` is false). A tile that is also a `<button>` would therefore receive a `click` at the end of every move.

**Tests assume the current shape.** `features/admin/classes/classes.spec.ts:121-129` finds every action inside the tile (`actionIn(tile, label)`). `schedule-calendar.spec.ts` stubs `matchMedia` with one answer for every query (`:27-50`).

## Desired End State

**Admin at a desk (viewport ≥ 64rem)**

- The admin sees the calendar as today.
- Activating a tile, by click or keyboard, opens a class-actions overlay. It shows the class (name, when, instructor, spots) and Edytuj / Powiel / Zapisani / Odwołaj-or-Usuń.
- Powiel, Usuń and Odwołaj confirm inside that overlay.
- Zapisani swaps it for the existing bookings overlay.
- This works identically for a 30-minute and a 3-hour class.
- Dragging or resizing a tile never opens the overlay.
- Past weeks stay inert: no overlay, and the "Ten tydzień już minął" note remains.

**Admin below 64rem**

- `/admin/classes` renders the page header and a card instead of the calendar.
- The card says class editing happens at a computer.
- It links to `/schedule`, "Dodaj zajęcia" (`/admin/classes/new`) and "Typy zajęć" (`/admin/class-types`).
- No `angular-calendar` DOM is rendered.

**On a device whose primary pointer is coarse (tablets)**

- The grid scrolls under a finger.
- No segment carries `.calendar-segment-drawable`.
- A tap on empty grid does nothing.
- Moving a class by long-press still works (the library's own touch path).

**Breakpoints**

- Every width `@media` in `src/app/src/**/*.scss` goes through `src/app/src/styles/_breakpoints.scss`.
- The desk boundary exists as a Sass value and a TS constant, and a spec fails when they disagree.

**Verification:** `npm run quality:check`, `npm test` and `npm run build` pass from `src/app/`, plus the manual steps under Testing Strategy.

### Key Discoveries:

- `shared/calendar/schedule-calendar.ts:156-170`: `readOnly` and `selectable` are deliberately orthogonal. `selectable && !readOnly` has never been used by any caller, and this plan makes the admin screen the first.
- `shared/calendar/schedule-calendar.html:116-125`: the segment template, the single place the drawable class and `startDraw` are bound.
- `features/schedule/class-details-overlay/*`: the overlay structure to copy. That means the backdrop `<button>`, `.card.overlay-panel` with `role="dialog"`, host `(document:keydown.escape)`, `useOverlayFocus()`, and `:host { position: fixed; inset: 0; z-index: var(--z-overlay) }`.
- `features/admin/classes/classes.ts:122-131`: `load()` already clears every transient panel and overlay signal. The same clearing is what leaving the desk viewport needs.
- `features/admin/classes/class-form.html:3-22`: the precedent for a screen state, a card that says why plus a link to the surface that works.
- `src/app/tools/eslint-rules/no-hand-rolled-presentational.spec.ts:1-9`: the precedent for a spec reaching Node through `declare function require` without adding `node` to `tsconfig.spec.json` types.
- `.github/workflows/deploy.yml:39,43,50`: CI runs `quality:check`, `build` and `npm test`, so a sync spec gates deploys.

## What We're NOT Doing

- **No touch reimplementation of the calendar gestures.** Drawing is withheld from coarse pointers, and the phone gets a refusal (UX-01, user decision 2026-09-20).
- **No "draw mode" toggle** for tablets. It was rejected in planning as a new feature rather than a fix.
- **No overlay on past-week tiles.** A read-only history view of who was booked is a separate capability.
- **No change to tile height, segment height or the tile body** (UX-09: no visual redesign).
- **`ClassForm` (`/admin/classes/new`, `/admin/classes/:id`) is not made desk-only.** It works on a phone, and the refusal links to it.
- **No route guard for the refusal.** It is a component-level screen state, because a guard would redirect rather than say why, and there is one caller.
- **No change to `/schedule` or the member overlay.**
- **The calendar's `WEEK_VIEW_MIN_WIDTH` (48rem) is not merged into the new desk boundary.** It answers a different question: how many day columns stay legible.
- **No `@angular/cdk/layout`.** The hand-rolled guarded `matchMedia` precedent is extracted instead.
- **No edit to `prd-v2.md`.** It is versioned as shipped. The FR-019 narrowing is recorded in the roadmap (UX-01) and in this plan.
- **No navigation change** (a role-aware bottom nav is explicitly out of M-7).

## Implementation Approach

Build the shared breakpoint first, because both the refusal (UX-01) and S-21 read it. The breakpoint migration changes no behaviour, so it lands and verifies on its own.

The refusal comes next. It is a single `@if` on the screen and does not depend on how actions are delivered.

The calendar's two new behaviours come third, while the admin screen still uses projected actions: drawing only with a fine pointer, and a click that ends a drag does not select. This keeps the shared component's changes reviewable on their own and covered by its own spec.

Finally, the admin screen switches to `selectable`, and a new `class-actions-overlay` absorbs the actions and the three confirmation panels. The now-unused `classActions` slot is then removed from the calendar.

The screen keeps owning every mutation, the row list, the busy set and the toasts. The overlay renders and reports intentions, as the calendar and the existing overlays do.

## Critical Implementation Details

- **Media reads default to "desk" and "fine" when `matchMedia` is absent (jsdom, server).**
  - `mediaQuerySignal(query, fallback)` returns `fallback` when `matchMedia` is unavailable.
  - The desk check and the fine-pointer check both pass `true`. The calendar's week-view read keeps `false`, its existing mobile-first default.
  - This keeps `classes.spec.ts` and the drawing tests working unstubbed. The phone and coarse cases are exercised by explicit stubs.
- **`stubMatchMedia` must become query-aware.** It currently answers every query with one boolean (`schedule-calendar.spec.ts:32-50`). Once the calendar also asks `(pointer: fine)`, a day-view test stubbing `false` would also switch drawing off.
- **A click that ends a drag or resize must not select.**
  - Enforce this inside the calendar, only where `selectable() && !readOnly()`.
  - Record where the pointer went down on the tile. On `click`, emit `classSelected` only if the pointer moved no more than a few pixels.
  - Keyboard activation (Enter/Space) has no preceding pointerdown and always emits.
  - Resize handles are siblings of the tile button (library markup), so their clicks never reach it.
- **Leaving the desk viewport clears transient state.** When `desk()` turns false, the screen clears the same signals `load()` clears: duplicate/delete/cancel/blocked state, selected class, bookings overlay and drawn range. Returning to desk re-creates the calendar, which emits `rangeChange` and reloads.
- **Swapping overlays and focus.**
  - Opening Zapisani from the actions overlay destroys the overlay whose button was the opener. `useOverlayFocus` then skips returning focus, because the opener is gone.
  - That is acceptable. Do not add a focus-restoration workaround in this slice.
- **Sass partition.**
  - The `30rem` / `30.0001rem` pair must stay exact.
  - Encode it once as two mixins in the partial rather than two values callers combine:

  ```scss
  // _breakpoints.scss
  $narrow-max: 30rem;
  @mixin narrow { @media (max-width: $narrow-max) { @content; } }
  // 30.0001rem, not 30.0625rem — see the gap bug recorded in bottom-nav.scss.
  @mixin above-narrow { @media (min-width: 30.0001rem) { @content; } }
  ```

## Phase 1: One definition of a breakpoint (UX-04)

### Overview

Create the shared breakpoint module on both sides (Sass and TS) and migrate every existing width `@media` onto it with identical values. Extract the guarded `matchMedia` read into one helper. Behaviour does not change anywhere.

### Changes Required:

#### 1. Sass partial and include path

**File**: `src/app/src/styles/_breakpoints.scss` (new), `src/app/angular.json`

**Intent**: This is the single stylesheet-side definition of every width threshold. It is reachable from any component stylesheet through `@use 'breakpoints' as bp;`.

**Contract**:
- The partial contains:
  - the mixins `narrow` and `above-narrow` for the 30rem phone-layout partition;
  - a mixin `form-columns` for `min-width: 40rem`;
  - `$desk-min-width: 64rem` with a mixin `desk` for `min-width` and `below-desk` for the complement.
- The complement's partition value mirrors the 30.0001rem reasoning: `below-desk` uses `max-width: 63.9999rem`.
- A header comment explains:
  - why these are Sass values and not custom properties (a custom property is illegal in a media condition);
  - that `$desk-min-width` has a TS twin guarded by a spec;
  - that 48rem (week view) deliberately lives elsewhere.
- `angular.json` → `projects.app.architect.build.options.stylePreprocessorOptions.includePaths: ["src/styles"]`. The unit-test builder uses the build target, so specs compile with it too.

#### 2. Migrate the existing media queries

**Files**:
- `src/app/src/app/app.scss:84`
- `shared/bottom-nav/bottom-nav.scss:30`
- `shared/toast/toast-host.scss:103`
- `features/trainer/plans/plans.scss:30`
- `features/trainer/plans/plan-builder.scss:182`
- `features/admin/class-types/class-type-form.scss:40`
- `features/admin/class-types/class-types.scss:19`
- `features/admin/classes/class-form.scss:11`
- `features/admin/exercises/exercise-detail.scss:68`
- `features/admin/exercises/exercises.scss:54`
- `features/admin/exercises/exercise-form.scss:34,48`
- `features/admin/members/members.scss:187`

All paths except the first are relative to `src/app/src/app/`.

**Intent**: Replace each literal width media query with the partial's mixin. The emitted CSS is identical.

**Contract**:
- `max-width: 30rem` becomes `@include bp.narrow`.
- `min-width: 30.0001rem` becomes `@include bp.above-narrow`. Move the bottom nav's explanatory comment into the partial, leaving a one-line pointer.
- `min-width: 40rem` becomes `@include bp.form-columns`.
- `prefers-reduced-motion` queries are untouched (not width thresholds).

#### 3. TS constants and the media-query signal

**File**: `src/app/src/app/core/layout/breakpoints.ts` (new), `src/app/src/app/core/layout/media-query.ts` (new)

**Intent**:
- The TS side holds only the desk boundary, which is the one value TS needs.
- A single helper replaces the calendar's hand-written guarded `matchMedia` block, so the next viewport read is one line.

**Contract**:
- `breakpoints.ts` exports `DESK_MIN_WIDTH = '64rem'` and `DESK_MEDIA_QUERY = '(min-width: 64rem)'`, built from the constant. It also exports `FINE_POINTER_MEDIA_QUERY = '(pointer: fine)'`.
- Its doc comment names `_breakpoints.scss` as the twin and the spec as the guard.
- `media-query.ts` exports `mediaQuerySignal(query: string, fallback: boolean): Signal<boolean>`. It:
  - must be called in an injection context;
  - reads `matchMedia` only when `isPlatformBrowser` and `typeof window.matchMedia === 'function'`;
  - otherwise returns a constant `fallback`;
  - listens for `change` and removes the listener through `DestroyRef`.

#### 4. The calendar onto the helper

**File**: `src/app/src/app/shared/calendar/schedule-calendar.ts:352-362`

**Intent**: Replace the constructor's guarded `matchMedia` block with `mediaQuerySignal(WEEK_VIEW_MEDIA_QUERY, false)`. `weekView` becomes that signal, or is derived from it. `calendar-breakpoint.ts` stays as it is. Its "THE only definition" comment stays true for 48rem.

**Contract**: `weekView` keeps its current semantics and its `false` default. The existing week/day tests in `schedule-calendar.spec.ts` pass unchanged.

#### 5. The sync and no-literal spec

**File**: `src/app/src/app/core/layout/breakpoints.spec.ts` (new)

**Intent**: This is where "one definition" is enforced, in the same spirit as S-23's lint rule. The two copies of the desk value cannot drift, and a new literal width media query cannot appear.

**Contract**:
- Using `declare function require` as the precedent in `tools/eslint-rules/no-hand-rolled-presentational.spec.ts` does, read `src/styles/_breakpoints.scss` with `node:fs`.
- Assert that `$desk-min-width` equals `DESK_MIN_WIDTH`.
- Walk `src/**/*.scss` and assert that no file other than `_breakpoints.scss` contains `@media` with a `min-width`/`max-width` literal.
- Paths resolve from `process.cwd()`, which is `src/app/` under `npm test`.
- If the unit-test builder does not expose `require('node:fs')`, record the adaptation in this plan ("**Adapted during implementation.**") before the phase commit.

#### 6. Document it

**File**: `AGENTS.md` (the Style section, beside "The presentational kit (S-23)")

**Intent**: Record the rule a future screen needs: where a width breakpoint comes from, why there are both a Sass and a TS side, which values exist, and that 48rem is the calendar's own.

**Contract**: A new subsection "Breakpoints (S-20)", of a few paragraphs, naming `_breakpoints.scss`, `core/layout/breakpoints.ts`, `mediaQuerySignal`, and the spec that enforces them.

### Success Criteria:

#### Automated Verification:

- Formatting and lint pass: `cd src/app && npm run quality:check`
- Unit tests pass, including the new `breakpoints.spec.ts`: `cd src/app && npm test`
- Production build succeeds with no new budget warning: `cd src/app && npm run build`
- No literal width media query remains outside the partial. The spec asserts this, and `grep -rnE "@media[^{]*(min|max)-width: *[0-9]" src/app/src --include=*.scss` lists only `_breakpoints.scss`.

#### Manual Verification:

- Narrowing a desktop browser through 480px swaps the header nav for the bottom bar exactly as before. At a zoomed fractional width near 480px, only one navigation shows.
- The exercise form's two-column pair still appears from 640px up.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: The phone gets a refusal, not a grid (UX-01)

### Overview

Below 64rem, `/admin/classes` renders a screen state that says why and where to go instead, and none of the calendar.

### Changes Required:

#### 1. The desk switch on the screen

**File**: `src/app/src/app/features/admin/classes/classes.ts`, `classes.html`

**Intent**:
- The screen reads `desk = mediaQuerySignal(DESK_MEDIA_QUERY, true)`.
- Everything that belongs to the grid renders only inside `@if (desk())`:
  - the past-week note;
  - the calendar;
  - panels, blocked notice, retry alert and overlays.
- `@else` renders the refusal.
- `.page-header` stays outside the switch.

**Contract**:
- Leaving the desk viewport clears transient state. Factor the clearing lines of `load()` (`classes.ts:125-131`) into one private method that both `load()` and an `effect` on `desk()` becoming false call.
- The class doc comment gains a short "Desk only (S-20)" section. It records that this narrows prd-v2 FR-019, and that F5 of the calendar's impl review chose the opposite on 2026-09-02, reversed here on the user's decision of 2026-09-20.

#### 2. The refusal state

**File**: `src/app/src/app/features/admin/classes/classes.html`, `classes.scss`

**Intent**: Outlet 4 of the S-19 rule (a screen state, never a toast). It is a `.card` holding a `.notice` paragraph and links, following `class-form.html:3-22`.

**Contract**:
- The copy says, in Polish, that the class calendar is edited at a computer, because arranging classes in the grid needs a mouse.
- The card offers:
  - `routerLink="/schedule"` "Zobacz grafik zajęć";
  - `/admin/classes/new` "Dodaj zajęcia";
  - `/admin/class-types` "Typy zajęć".
- Use existing global classes (`.card`, `.notice`, `.button`, `.link-button`). Add layout-only SCSS if spacing needs it. No hand-rolled kit markup (the lint rule applies).

#### 3. Specs

**File**: `src/app/src/app/features/admin/classes/classes.spec.ts`

**Intent**: Pin the refusal and the absence of the calendar below the boundary. Existing tests keep running unstubbed on the desk fallback.

**Contract**: New tests, with `matchMedia` stubbed so `DESK_MEDIA_QUERY` is false:
- no `app-schedule-calendar` element;
- the refusal links point to `/schedule`, `/admin/classes/new` and `/admin/class-types`;
- no class request is made.

Another test flips the stub from desk to phone with a panel open, and asserts the panel state is cleared.

### Success Criteria:

#### Automated Verification:

- Lint and format pass: `cd src/app && npm run quality:check`
- Unit tests pass, including the new refusal cases: `cd src/app && npm test`
- Build succeeds: `cd src/app && npm run build`

#### Manual Verification:

- On a phone (or DevTools device mode, portrait and landscape), `/admin/classes` shows the refusal card and its three links work.
- In a desktop window resized below 1024px, the refusal appears. Resized back up, the calendar returns on the current week with no stale panel.
- At ≥ 1024px the screen looks and behaves exactly as before.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: The calendar stops fighting touch, and tells a click from a drag (UX-02)

### Overview

Two changes in the shared calendar, both covered by its own spec, with no change to any screen yet:

- drawing is offered only to a fine pointer;
- a selectable, editable calendar never selects on the click that ends a drag.

### Changes Required:

#### 1. Drawing only with a fine pointer

**File**: `src/app/src/app/shared/calendar/schedule-calendar.{ts,html,scss}`

**Intent**:
- The draw gesture is on offer only where it can work. The component holds `finePointer = mediaQuerySignal(FINE_POINTER_MEDIA_QUERY, true)`.
- `.calendar-segment-drawable` is bound only when `!readOnly() && finePointer() && !isPastSegment(...)`.
- `startDraw` returns early when `!finePointer()`.
- `touch-action: none` stays on the drawable class, which now exists only for fine pointers.

**Contract**:
- Replace the SCSS comment at `schedule-calendar.scss:288-289` ("It is what makes drawing work on the view built for phones") with the current truth: drawing is withheld from coarse pointers, and phones never render the admin grid (S-20).
- Update the `startDraw` doc at `ts:487-491` to match.
- `.calendar-segment-past` keeps its current binding.

#### 2. A click that ends a drag does not select

**File**: `src/app/src/app/shared/calendar/schedule-calendar.{ts,html}`

**Intent**: This makes `selectable && !readOnly` a valid combination.

**Contract**:
- The tile button records its `pointerdown` coordinates.
- Its click handler emits `classSelected` only when the pointer moved at most a small tolerance, around 4px, or when no pointerdown preceded it (keyboard).
- The `selectable` input doc drops the "cannot happen" note at `schedule-calendar.html:149-151`.
- The class doc gains one line saying that selectable and editable coexist on the admin screen, and how a drag is told apart.

#### 3. Specs

**File**: `src/app/src/app/shared/calendar/schedule-calendar.spec.ts`

**Intent**: Make the `matchMedia` stub query-aware, and pin both behaviours.

**Contract**:
- `stubMatchMedia` takes a per-query answer, for example a map or a predicate. Existing call sites keep their meaning for `WEEK_VIEW_MEDIA_QUERY`.
- New tests:
  - with `(pointer: fine)` false on a non-read-only calendar: no `.calendar-segment-drawable`, and a pointerdown on a future segment emits no `rangeDrawn`;
  - with it true, drawing works as today;
  - on `selectable` + `readOnly=false`: pointerdown at (0,0) then click at (40,0) emits nothing;
  - on `selectable` + `readOnly=false`: pointerdown and click at the same point emits once;
  - on `selectable` + `readOnly=false`: a click with no pointerdown (keyboard) emits once;
  - the existing "still refuses drag, resize, draw and actions on a selectable calendar" test keeps passing.

### Success Criteria:

#### Automated Verification:

- Lint and format pass: `cd src/app && npm run quality:check`
- Unit tests pass, including the new calendar cases: `cd src/app && npm test`
- Build succeeds: `cd src/app && npm run build`

#### Manual Verification:

- At ≥ 1024px with a mouse, drawing a class on empty grid still opens the create overlay, and dragging and resizing still work.
- On a tablet in landscape (≥ 1024px, touch), the grid scrolls vertically under a finger, and a tap on empty grid opens nothing. A long-press drag still moves a future class.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 4: The class's actions live in an overlay (UX-03)

### Overview

The admin calendar becomes selectable, except in past weeks. Activating a tile opens a new class-actions overlay that carries all four actions and the three confirmations. The panels below the calendar and the calendar's `classActions` slot are removed.

### Changes Required:

#### 1. The class-actions overlay

**File**: `src/app/src/app/features/admin/classes/class-actions-overlay.{ts,html,scss,spec.ts}` (new)

**Intent**:
- One surface for everything an admin does to one class, built exactly like `features/schedule/class-details-overlay`:
  - the backdrop button;
  - `.card.overlay-panel` with `role="dialog"`, `aria-modal` and `aria-labelledby`;
  - Escape on the host;
  - `useOverlayFocus()`;
  - the fixed `:host`.
- It renders and reports. The screen performs every request.

**Contract**:
- Inputs:
  - `row: ScheduledClass` (required);
  - `busy: boolean`;
  - `failed: boolean`, which shows the "Nie udało się. Spróbuj ponownie." field error;
  - `deleteBlocked: boolean`, which shows the has-bookings "Odwołaj zamiast tego" way out now at `classes.html:144-151`.
- Outputs:
  - `duplicateRequested: number` (weeks);
  - `deleteRequested`;
  - `cancelRequested`;
  - `bookingsRequested`;
  - `closed`.
- Local state is one `step` signal: `'actions' | 'duplicate' | 'delete' | 'cancel'`.
- The actions step shows:
  - the class summary (name, day and time range, instructor, spots);
  - "Edytuj" as a `routerLink` to `/admin/classes/:id`;
  - Powiel;
  - Zapisani;
  - Odwołaj or Usuń, under the same one-button rule and `canCancel` logic as today.
- The confirmation steps carry today's copy verbatim from `classes.html:82-140`, including the cancel step's "Powiadomimy N zapisanych osób…" line, and offer a way back to the actions step.
- The weeks input uses `app-field`.
- Move `canCancel` and `bookedCount` here from the screen, or share them, so the rule lives in one place.

#### 2. The screen switches to selection

**File**: `src/app/src/app/features/admin/classes/classes.{ts,html,scss}`

**Intent**: Tile activation replaces the projected buttons.

**Contract**:
- The calendar binding gains `[selectable]="!isPast()"` and `(classSelected)="select($event)"`.
- The `#classActions` template and the three `.classes-panel` blocks are deleted.
- A `selected` signal holds the class whose overlay is open.
- The screen wires the overlay outputs to its existing methods (`duplicate`, `remove`, `cancel`, `openBookings`, `cancelInstead`).
  - **Adapted during implementation.** `cancelInstead` is gone rather than wired: the has_bookings way out is now a step change inside the overlay (`deleteBlocked` → "Odwołaj zamiast tego" → the cancel step), so it needs no screen method, and `cancel()` still clears `deleteBlockedBy`. Likewise `weeks` moved into the overlay and reaches the screen as `duplicate(row, weeks)` through `duplicateRequested`. `canCancel` / `bookedCount` are exported functions from `class-actions-overlay.ts`, which the screen imports for the cancel toast.
- Each flow closes the overlay exactly where it closes its panel today. For example, a successful cancel or delete drops the row and closes it.
- Zapisani closes the actions overlay and opens the bookings overlay.
- The remaining panel signals collapse into what the overlay needs (`selected`, `failedId`, `deleteBlockedBy`, `viewingBookings`, `drawn`).
- The clearing method from Phase 2 includes `selected`.
- Remove panel CSS that no longer has a selector from `classes.scss`, and move the duplicate input styles that are still needed into the overlay.
- Update the comments that justified the old layout (`classes.html:29-30, 49-52, 79-81, 163-166`, `classes.ts:390-393`).

#### 3. Remove the calendar's action slot

**File**: `src/app/src/app/shared/calendar/schedule-calendar.{ts,html,scss,spec.ts}`

**Intent**: No caller projects `classActions` any more, so delete it rather than keep a second way to put buttons in a tile.

**Contract**:
- Remove:
  - the `classActions` `contentChild`;
  - the `.calendar-tile-actions` block and its CSS (`scss:217-222`);
  - the host's `#classActions` template in the spec;
  - the "renders per-class actions only when the screen is not read-only" test (`spec:743`).
- The "still refuses drag, resize, draw and actions on a selectable calendar" test drops its action assertion.
- `[calendarHeaderActions]` stays.

#### 4. Specs

**File**: `src/app/src/app/features/admin/classes/classes.spec.ts`, `class-actions-overlay.spec.ts`

**Intent**:
- Rewrite the `tiles` / `tileFor` / `actionIn` helpers into "activate tile → find action in `.overlay-panel`".
- Every existing behavioural test keeps its assertion and changes only its route to the button:
  - duplicate: 191, 218;
  - delete: 240-268;
  - cancel: 524-611;
  - bookings: 430-475.

**Contract**:
- Past-week withholding tests (381, 506, 645) assert that the past-week tile is not a `button` and that activating it opens nothing.
- New tests:
  - a 30-minute class's tile opens the overlay and all four actions are present;
  - Zapisani swaps the overlays;
  - Escape and the backdrop close the overlay;
  - a window change (`load`) closes an open overlay.
- `class-actions-overlay.spec.ts` covers:
  - the step transitions;
  - Odwołaj vs Usuń by bookings;
  - the cancel copy with and without bookings;
  - the disabled state while `busy`;
  - `deleteBlocked` leading to the cancel step.

#### 5. Stale route comment

**File**: `src/app/src/app/app.routes.ts:78-81`

**Intent**: Correct the bundle figure ("~424 kB against a 500 kB budget") to the measured number after this phase's `npm run build`, against the 600 kB warning.

**Contract**: Comment-only change.

### Success Criteria:

#### Automated Verification:

- Lint and format pass. That includes the kit rule on the new overlay's template: `cd src/app && npm run quality:check`
- Unit tests pass, with the rewritten `classes.spec.ts` and the new `class-actions-overlay.spec.ts`: `cd src/app && npm test`
- Build succeeds, and the initial bundle stays under the 600 kB warning (the overlay lives in the lazy classes chunk): `cd src/app && npm run build`
- No `classActions` reference remains: `grep -rn "classActions" src/app/src` returns nothing.

#### Manual Verification:

- At ≥ 1024px, create a 30-minute class. Clicking its tile opens the overlay with Edytuj, Powiel, Zapisani and Usuń.
- Book someone into a class. Its overlay now offers Odwołaj, and the confirmation names the number of people to be notified.
- Each flow completes from the overlay:
  - Edytuj navigates to the form;
  - Powiel for 2 weeks reports through the toast;
  - Zapisani shows the list;
  - Usuń and Odwołaj remove the tile.
- Dragging a class to a new time and resizing it never opens the overlay. A plain click always does. Tab to a tile and press Enter, and the overlay opens.
- In a past week, tiles do not open and the note is shown.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Testing Strategy

### Unit Tests:

- **`breakpoints.spec.ts`:** the Sass and TS desk values match, and no literal width media query exists outside the partial.
- **`schedule-calendar.spec.ts`:**
  - drawing is gated on a fine pointer;
  - a click is told apart from a drag on a selectable, editable calendar;
  - keyboard activation works;
  - week/day behaviour is unchanged on the new helper.
- **`classes.spec.ts`:**
  - the refusal below desk, with its links and no fetch;
  - state is cleared on leaving desk;
  - every flow through the overlay;
  - past weeks stay inert;
  - a 30-minute class is fully actionable.
- **`class-actions-overlay.spec.ts`:** steps, the Odwołaj vs Usuń rule, the copy, busy, failed and deleteBlocked.

### Integration Tests:

None. No API contract changes, and the .NET suite is untouched. There is no E2E for `/admin/classes` today. A browser-level risk (click vs drag on a real `angular-calendar`) is a candidate for `/10x-e2e` after this slice, not a gate on it.

### Manual Testing Steps:

1. At ≥ 1024px with a mouse, run every flow from the overlay on a 30-minute and a 2-hour class.
2. Drag and resize several classes, and confirm the overlay never opens as a side effect.
3. In DevTools device mode on an iPhone in portrait and landscape, confirm the refusal card and its three links.
4. Resize a desktop window across 1024px both ways with an overlay open. Confirm there is no stale overlay, and that the calendar reloads the current week.
5. On a touch tablet in landscape, confirm the grid scrolls, a tap draws nothing, and a long-press move still works.
6. Narrow through 480px and confirm there is exactly one navigation at every width (the Phase 1 regression check).

## Performance Considerations

- The desk switch keeps the whole `angular-calendar` DOM and its listeners off phones, which is a small win.
- Two `matchMedia` listeners per calendar instance are negligible.
- The overlay is in the lazy `/admin/classes` chunk.
- The eager bundle is unaffected except by `media-query.ts` and `breakpoints.ts`, if something eager imports them. Nothing does in this slice.

## Migration Notes

There is no data or API migration. Rollback is a redeploy of the previous artifact. The change is SPA-only and carries no schema.

## References

- Research: `context/changes/admin-schedule-web-only/research.md`
- Roadmap slice: `context/foundation/roadmap.md` (S-20, M-7 UX-01 to UX-04, UX-09)
- Reversed decision: `context/archive/2026-09-02-schedule-calendar-view/reviews/impl-review.md:117-147` (F5)
- Tile-actions origin: `context/archive/2026-09-02-schedule-calendar-view/plan.md:580-585`
- Overlay to copy: `src/app/src/app/features/schedule/class-details-overlay/`
- Screen-state precedent: `src/app/src/app/features/admin/classes/class-form.html:3-22`
- Node-in-spec precedent: `src/app/tools/eslint-rules/no-hand-rolled-presentational.spec.ts:1-9`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: One definition of a breakpoint (UX-04)

#### Automated

- [x] 1.1 Formatting and lint pass: `cd src/app && npm run quality:check` — 5270846
- [x] 1.2 Unit tests pass, including the new `breakpoints.spec.ts`: `cd src/app && npm test` — 5270846
- [x] 1.3 Production build succeeds with no new budget warning: `cd src/app && npm run build` — 5270846
- [x] 1.4 No literal width media query remains outside the partial — 5270846

#### Manual

- [ ] 1.5 Header nav / bottom bar swap at 480px unchanged, one navigation at fractional zoom widths
- [ ] 1.6 Exercise form two-column pair still appears from 640px up

### Phase 2: The phone gets a refusal, not a grid (UX-01)

#### Automated

- [x] 2.1 Lint and format pass: `cd src/app && npm run quality:check` — 486918f
- [x] 2.2 Unit tests pass, including the new refusal cases: `cd src/app && npm test` — 486918f
- [x] 2.3 Build succeeds: `cd src/app && npm run build` — 486918f

#### Manual

- [ ] 2.4 Phone (portrait and landscape) shows the refusal card, and its three links work
- [ ] 2.5 Desktop resize across 1024px swaps refusal and calendar with no stale panel
- [ ] 2.6 At ≥ 1024px the screen is unchanged

### Phase 3: The calendar stops fighting touch, and tells a click from a drag (UX-02)

#### Automated

- [x] 3.1 Lint and format pass: `cd src/app && npm run quality:check` — 4cf83c5
- [x] 3.2 Unit tests pass, including the new calendar cases: `cd src/app && npm test` — 4cf83c5
- [x] 3.3 Build succeeds: `cd src/app && npm run build` — 4cf83c5

#### Manual

- [ ] 3.4 Mouse at ≥ 1024px: draw, drag and resize all still work
- [ ] 3.5 Touch tablet in landscape: grid scrolls, tap draws nothing, long-press move works

### Phase 4: The class's actions live in an overlay (UX-03)

#### Automated

- [x] 4.1 Lint and format pass, including the kit rule on the new overlay: `cd src/app && npm run quality:check`
- [x] 4.2 Unit tests pass with the rewritten `classes.spec.ts` and the new `class-actions-overlay.spec.ts`: `cd src/app && npm test`
- [x] 4.3 Build succeeds and the initial bundle stays under the 600 kB warning: `cd src/app && npm run build`
- [x] 4.4 No `classActions` reference remains

#### Manual

- [ ] 4.5 A 30-minute class's tile opens the overlay with all four actions
- [ ] 4.6 A booked class offers Odwołaj with the notified count
- [ ] 4.7 Each flow (Edytuj, Powiel, Zapisani, Usuń, Odwołaj) completes from the overlay
- [ ] 4.8 Drag and resize never open the overlay; click and keyboard Enter do
- [ ] 4.9 Past-week tiles do not open, and the note is shown
