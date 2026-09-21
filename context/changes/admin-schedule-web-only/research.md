---
date: 2026-09-21T10:57:28+02:00
researcher: Claude (claude-opus-5)
git_commit: 7689332bf62dbc1c5152fc55d98edb2a088304e6
branch: main
repository: po-prostu-silka
topic: "S-20 admin-schedule-web-only — what the admin class calendar, its tile actions, its gestures and the SPA's breakpoints look like today"
tags: [research, codebase, schedule-calendar, admin-classes, breakpoints, touch-action, overlay, m-7]
status: complete
last_updated: 2026-09-21
last_updated_by: Claude (claude-opus-5)
---

# Research: S-20 admin-schedule-web-only

**Date**: 2026-09-21T10:57:28+02:00
**Researcher**: Claude (claude-opus-5)
**Git Commit**: 7689332bf62dbc1c5152fc55d98edb2a088304e6
**Branch**: main
**Repository**: po-prostu-silka

Paths below are relative to `src/app/src/app/` unless they start with `src/`, `context/` or `node_modules/`.
There are no GitHub permalinks because `gh` is not authenticated on this machine. Every reference is a local path pinned to the commit above.

## Research Question

Roadmap slice S-20 (`context/foundation/roadmap.md:634-661`, anchors UX-01 to UX-04 of M-7) makes four promises:

1. **UX-01:** below a shared mobile breakpoint, `/admin/classes` renders a screen state instead of the editing calendar. The state names the reason and points to `/schedule`.
2. **UX-02:** `touch-action: none` applies only where a gesture is genuinely on offer.
3. **UX-03:** Edytuj, Powiel, Zapisani and Odwołaj are reachable at every class length.
4. **UX-04:** the mobile/web boundary becomes one module that both TypeScript and the stylesheets read. The calendar's 48rem stays separate.

The question is what the code looks like today at each of these points, and which constraints the plan will have to respect.

## Summary

- **UX-03 is worse than the roadmap says.**
  - A tile is 1px per minute. angular-calendar's default `hourSegmentHeight = 30` is not overridden, with two segments per hour.
  - The tile body is time, name, instructor and spots (4 lines), plus padding. That is already about 80px.
  - As a result, even a **60-minute** class clips every action. Only classes of roughly 90 minutes or more show them.
  - The actions are projected through a `#classActions` `ng-template` into a `.calendar-tile` with `height: 100%; overflow: hidden` (`shared/calendar/schedule-calendar.scss:166-179`).
- **The two candidate homes for the actions already exist on this screen.**
  - Duplicate, cancel and delete open **panels below the calendar**.
  - Zapisani opens an **overlay** (`app-class-bookings-overlay`).
  - The member's `/schedule` uses the `selectable` / `classSelected` path, which turns the tile into a `<button>` and opens `app-class-details-overlay`.
  - The calendar component puts actions **only** on the non-selectable tile, because buttons nested in a button would be invalid HTML (`shared/calendar/schedule-calendar.html:149-151`).
  - Choosing the overlay route therefore means actions leave the tile completely and the admin calendar becomes `selectable`. Choosing the panel route means something still has to be clickable inside the tile.
- **UX-02: `.calendar-segment-drawable` is bound to every future segment whenever `readOnly` is false** (`schedule-calendar.html:121`), not while a gesture is in progress.
  - That is the only `touch-action` in the calendar (`schedule-calendar.scss:285-291`).
  - It was added deliberately as "Fix A" to finding F5 of the calendar's impl review.
  - That review rejected "Fix B": record drag-to-create as desk-only. S-20 is effectively taking Fix B now.
- **UX-04: the roadmap's inventory is exact.** Only three width thresholds exist:
  - `30rem`, written literally in 11 stylesheets, plus `30.0001rem` once in the bottom nav, which must partition the axis exactly with the `max-width: 30rem` rules;
  - `40rem`, once;
  - `48rem`, which exists only in TS (`shared/calendar/calendar-breakpoint.ts:14-17`) and is read through `matchMedia`.
- **Nothing shares a value between SCSS and TS today.** There are no SCSS partials, no `@use`, and no `stylePreprocessorOptions.includePaths`.
- **`@angular/cdk/layout` is not used.** The only viewport read in TS is the calendar's guarded `matchMedia`.
- **SSR is installed but not active.** `angular.json` has `outputMode: "static"`, and the calendar comment says "nothing renders on a server today". A TS read must still stay guarded, because jsdom in the specs has no `matchMedia`.
- **Specs will break by construction.**
  - `features/admin/classes/classes.spec.ts` finds every action *inside the tile* (`actionIn(tile, label)`, around line 129).
  - The calendar spec asserts the presence and absence of `.calendar-segment-drawable` and `.calendar-tile-actions`.
  - No E2E test covers `/admin/classes`.

## Detailed Findings

### The shared calendar (`shared/calendar/schedule-calendar.{ts,html,scss}`)

**Structure**
- It wraps `mwl-calendar-week-view`. Day view is `daysInWeek=1`, week view is 7.
- `weekView = signal(false)` (ts:226), so it starts in day view.
- It switches to week view through `matchMedia(WEEK_VIEW_MEDIA_QUERY)` inside `isPlatformBrowser && typeof window.matchMedia === 'function'` (ts:352-362). The listener is removed on destroy.

**Inputs and outputs**
- `classes`, `loading`, `loadFailed` (ts:146-148).
- `readOnly = input(true)` (ts:156). It gates the gestures and the projected actions.
- `selectable = input(false)` (ts:170), kept separate from `readOnly` on purpose.
- `rangeChange` (ts:173).
- `rangeDrawn` (ts:183).
- `classRescheduled` (ts:195).
- `classSelected` (ts:205).
- `classActions = contentChild<TemplateRef<…>>('classActions')` (ts:211).
- A second slot, `[calendarHeaderActions]` (html:54).

**Geometry**
- `SEGMENT_MINUTES = 30` (ts:55) and `[hourSegments]="2"` (html:72).
- The grid runs from `DAY_START_HOUR = 6` to `DAY_END_HOUR = 23` (ts:68-69).
- `hourSegmentHeight` is not bound, so the library defaults apply: `hourSegmentHeight = 30` and `minimumEventHeight = 30` (`node_modules/angular-calendar/fesm2022/angular-calendar.mjs:3048,3052`).
- The height is applied as `[style.height.px]` on `.cal-event-container` (mjs:3852).

**Tile template (html:149-196)**
- `selectable()` renders `<button class="calendar-tile calendar-tile-button" (click)="classSelected.emit(…)">`.
- Otherwise it renders a `<div class="calendar-tile">` with the body. `@if (!readOnly() && classActions())` adds a `.calendar-tile-actions` block with `ngTemplateOutlet` and the row as `$implicit`.
- `#tileBody` (html:183-196) shows time, name, instructor and spots.

**Tile CSS**
- `.calendar-tile { height: 100%; overflow: hidden; padding: var(--space-2); font-size: 0.8125rem; line-height: 1.25 }` (scss:166-179).
- `.calendar-tile-actions { display:flex; flex-wrap:wrap; gap: var(--space-1); margin-top: var(--space-2) }` (scss:217-222).

**Editability**
- Per event (ts:239-263): `editable = !readOnly() && start > now`, which drives `draggable` and `resizable.beforeStart/afterEnd`.

**Drag-to-create**
- This is the only hand-written gesture. On each segment (html:116-125):
  ```html
  [class.calendar-segment-drawable]="!readOnly() && !isPastSegment(segment.date)"
  [class.calendar-segment-past]="!readOnly() && isPastSegment(segment.date)"
  [attr.data-segment]="segment.date.toISOString()"
  (pointerdown)="startDraw(segment.date, $event)"
  ```
- `startDraw` (ts:493-560) does the following:
  - bails out on read-only, on a non-primary pointer, or on a button other than 0;
  - calls `preventDefault()`;
  - refuses a past segment and sets `pastRefusal`;
  - calls `setPointerCapture` (ts:522-524);
  - listens on the document for `pointermove`, `pointerup` and `pointercancel` (ts:557-559).
- `move` resolves the segment through `elementFromPoint` and `closest('[data-segment]')` (ts:563-571).
- `release` calls `finishDraw` and emits `rangeDrawn` (ts:573-582). `cancel` emits nothing.
- The comment at ts:487-491 documents why the class carries `touch-action`.

**Move and resize**
- These are the library's own: `mwlDraggable` with `touchStartLongPress {delay: 300, delta: 30}` (mjs:3870-3892), which uses mouse and touch events rather than pointer events, and `mwlResizable`.
- The component binds `validateEventTimesChanged = fitsInGrid` (ts:406-422) and `eventTimesChanged` to `applyTimesChanged` (ts:436-475). That handler refuses:
  - a read-only calendar;
  - a move into the past;
  - a class outside the grid;
  - a class shorter than 30 minutes.

**`touch-action`**
- Set only at scss:285-291, inside `.calendar-surface ::ng-deep`.
- Its comment says it makes drawing work "on the view built for phones".
- The only other occurrence in the SPA is the unrelated `features/trainer/plans/plan-builder.scss:50`.

### The admin screen (`features/admin/classes/classes.{html,ts,scss}`)

**Calendar binding (html:12-20)**
- `[readOnly]="isPast()"`, where `isPast` is true when the whole visible window has ended (ts:113-117).
- `(rangeDrawn)="openCreate($event)"` and `(classRescheduled)="reschedule($event)"`.
- `selectable` is not set, so it is `false`.

**Projected actions (html:31-76)**

| Action | Trigger | Where its flow renders |
| --- | --- | --- |
| Edytuj | `<a class="link-button" [routerLink]="['/admin/classes', row.id]">` | a separate route, `ClassForm` |
| Powiel | `openDuplicate(row)` (ts:239) | `.card.classes-panel` **below the calendar**, with a weeks input (html:82-104) |
| Zapisani | `openBookings(row)` (ts:248) | the **overlay** `app-class-bookings-overlay` (html:167-174) |
| Odwołaj / Usuń | `confirmCancel` (ts:409) / `confirmDelete` (ts:343). Odwołaj shows when `canCancel(row)`: Scheduled and has bookings (ts:400) | panel **below the calendar** (html:121-140 / 106-117) |

- `failedId` renders an inline `.field-error` **inside the tile** (html:73-75). That is one more thing that lands in a clipped box.
- The comments give the reasons for this layout:
  - html:29-30: "a tile in a week grid has no room for the panels";
  - html:49-52: "A fifth button is what would make this tile wrap on a phone".
- Each opener clears the other panels.
- `load()` closes every panel and overlay when the window changes (ts:125-131).
- Drawing a range opens `app-class-create-overlay` (html:159-161). Reschedule is optimistic and rolls back on failure (ts:195-237).

**Other screen parts**
- There is no top-level state switch.
- `[loadFailed]` is passed to the calendar (html:15).
- `.hint` shows for past weeks (html:5-10).
- `.notice` shows for the has-bookings message (html:144-151).
- A retry `.alert` sits at html:153-157.

### The member's `/schedule` (`features/schedule/`)

- `[selectable]="true"` and `(classSelected)="openDetails($event)"`, with `readOnly` left at its default of true (html:10-17).
- `selected` signal (ts:64), set by `openDetails` (ts:134) and cleared by `closeDetails` (ts:137).
- Selection renders `app-class-details-overlay`.
- **The overlay pattern.** The admin's `class-bookings-overlay` (ts:47,56) and `class-create-overlay` (ts:45,54) already use the same pattern:
  - `<button class="overlay-backdrop">` next to `.card.overlay-panel`, with `role="dialog"`, `aria-modal` and `aria-labelledby`;
  - host `(document:keydown.escape)`;
  - `useOverlayFocus()` (`shared/forms/overlay-focus.ts`);
  - `:host { position: fixed; inset: 0; z-index: var(--z-overlay) }`;
  - global `.overlay-*` classes at `src/app/src/styles.scss:460-497`.

### Breakpoints in the SPA (UX-04)

**30rem: phone layout**
- `max-width: 30rem` appears in:
  - `app.scss:84`: hides `.shell-nav` and pads `.shell-main` for the bar;
  - `shared/toast/toast-host.scss:103`;
  - `features/trainer/plans/plans.scss:30`;
  - `features/trainer/plans/plan-builder.scss:182`;
  - `features/admin/class-types/class-type-form.scss:40`;
  - `features/admin/class-types/class-types.scss:19`;
  - `features/admin/classes/class-form.scss:11`;
  - `features/admin/exercises/exercise-detail.scss:68`;
  - `features/admin/exercises/exercises.scss:54`;
  - `features/admin/exercises/exercise-form.scss:48`;
  - `features/admin/members/members.scss:187`.
- `min-width: 30.0001rem` appears once, in `shared/bottom-nav/bottom-nav.scss:30`.
  - Its comment at lines 26-29 says the two rules "must partition the axis exactly".
  - An earlier value of 30.0625rem left zoomed fractional widths where both navigations rendered at once.

**40rem**
- Once: `features/admin/exercises/exercise-form.scss:34` (`min-width`), which makes a 2-column pair.

**48rem**
- In TS only: `shared/calendar/calendar-breakpoint.ts:14-17`.
- The doc comment (lines 1-12) records that a `--breakpoint-week` custom property was removed. A custom property cannot appear in a media condition, and "a stylesheet that needs the number should take it from here via a host binding".

**Other**
- There are no `@container` queries.
- The `max-width` values 64, 44, 32, 26 and 22rem are content widths, not breakpoints.

**Sharing mechanisms**
- No mechanism exists.
- `styles.scss:75-79` explains why tokens are CSS custom properties rather than Sass variables: component stylesheets compile separately, so a variable would need an `@use` in every file.
- `angular.json` has no `stylePreprocessorOptions`. It does have `inlineStyleLanguage: "scss"` (line 26).

**Bottom nav**
- The switch is CSS only, deliberately.
- `shared/bottom-nav/bottom-nav.ts:41-43` says it hides "in CSS rather than removed in TypeScript", precisely to avoid the calendar's `matchMedia` guard.

**Dependencies**
- `@angular/cdk ^22.1.0`. The imported entry points are `a11y` (toast host) and `drag-drop` (plan builder, lazy).
- `cdk/layout` would be a new import.

### Routing and reachability

- `/admin/classes` (`app.routes.ts:94-98`): lazy, `canActivate: [authGuard, adminGuard]`.
- `/schedule` (`app.routes.ts:82-86`): lazy, `[authGuard, activeMemberGuard]`.
- All guards are functional `CanActivateFn`s in `core/auth/`. They "compose rather than nest" (`app.routes.ts:19-20`).
- `adminGuard` (`core/auth/admin.guard.ts:14-37`) returns `true` on the server and redirects to `/`.
- `admin/classes/new` and `admin/classes/:id` load `ClassForm`, which has its own `max-width: 30rem` rule. Nothing in the slice text says whether the form is also desk-only. Note that "Edytuj" from a phone arrives there.
- How an admin reaches the screen:
  - on a phone, only through `/more` (`features/more/more.html:27`, inside `@if (isAdmin())`), via the "Więcej" bottom tab;
  - also from the dashboard's "Zarządzaj zajęciami" (`features/dashboard/dashboard.html:173`);
  - there is no link in the desktop header (`app.html:21-40`).
- **Precedents for screen-state templates**
  - `features/admin/classes/class-form.html:3-22`: `@if loading` → `app-loading` / `@else if loadFailed` → `.alert` with a `link-button` back / `@else if noClassTypes()` → `.card` holding `app-empty` plus a `.button` link elsewhere / `@else` form. The `noClassTypes` branch is the closest existing shape to UX-01's refusal: a card that says why, plus a link to the surface that works.
  - `features/dashboard/dashboard.html:17-20` has an `app-empty` with an inline `routerLink="/schedule"`.

### Specs that pin the current shape

**`shared/calendar/schedule-calendar.spec.ts`**
- Helpers: `press()` (328-341) stubs `setPointerCapture` and sends a MouseEvent pointerdown; `release()` (343) dispatches the pointerup.
- Drawing: 349, 372, 387.
- Move and resize: 520 to 619.
- Selection: 678, 693, 706.
- 726 "still refuses drag, resize, draw and actions on a selectable calendar" asserts that `.calendar-segment-drawable` is absent (740).
- 743 "renders per-class actions only when … not read-only".
- The spec stubs `matchMedia` (27-99).

**`features/admin/classes/classes.spec.ts`**
- `tiles()` (121), `tileFor` (125) and `actionIn(tile, label)` (129) assume the actions live in the tile.
- These tests depend on those helpers:
  - duplicate: 191, 218;
  - delete: 240, 251, 268;
  - reschedule: 344, 364;
  - past-week withholding: 381, 506, 645, which assert `.calendar-tile-actions` is null;
  - bookings overlay: 430 to 475;
  - cancel and delete: 524 to 611.
- The spec relies on `matchMedia` being absent (line 55).

**`features/schedule/schedule.spec.ts`**: 228, 243.

**E2E**
- `src/app/e2e/` has only `seed.spec.ts` and `guarded-route-redirects-to-login.spec.ts`.
- The Playwright config is `src/app/playwright.config.ts`.

## Code References

- `shared/calendar/schedule-calendar.scss:166-179`: `.calendar-tile` with `height: 100%; overflow: hidden` (UX-03).
- `shared/calendar/schedule-calendar.scss:217-222`: `.calendar-tile-actions`.
- `shared/calendar/schedule-calendar.scss:285-291`: `.calendar-segment-drawable { touch-action: none }` (UX-02).
- `shared/calendar/schedule-calendar.html:116-125`: the segment template that binds the drawable class.
- `shared/calendar/schedule-calendar.html:149-196`: tile variants (button when selectable, div with actions otherwise).
- `shared/calendar/schedule-calendar.ts:156,170,205,211`: the `readOnly`, `selectable`, `classSelected` and `classActions` API.
- `shared/calendar/schedule-calendar.ts:352-362`: the guarded `matchMedia`, which is the precedent for a TS viewport read.
- `shared/calendar/schedule-calendar.ts:487-582`: drag-to-create.
- `shared/calendar/calendar-breakpoint.ts:1-17`: `WEEK_VIEW_MIN_WIDTH` and its "one source" doctrine.
- `features/admin/classes/classes.html:12-20,31-76,82-174`: calendar binding, projected actions, panels and overlays.
- `features/admin/classes/classes.ts:239,248,343,400,409`: action openers.
- `features/schedule/schedule.html:10-17,30-32`: the selectable path and details overlay.
- `features/admin/classes/class-form.html:3-22`: the screen-state precedent.
- `shared/bottom-nav/bottom-nav.scss:26-32` and `app.scss:84`: the exact 30rem partition.
- `shared/bottom-nav/bottom-nav.ts:41-43`: why the nav switch is CSS, not TS.
- `app.routes.ts:78-98`: the lazy calendar routes and guards.
- `features/more/more.html:27`: an admin's only phone path to the screen.
- `features/admin/classes/classes.spec.ts:121-129`: helpers that assume the actions are in the tile.

## Architecture Insights

- **The calendar is a projection host with flags, not modes.**
  - A `mode` input was rejected when it was built. `readOnly` and `selectable` are orthogonal, and admin-only content arrives by projection.
  - Anything S-20 adds should follow the same approach: a flag or a slot, not an "admin mode".
- **Actions and a clickable tile are mutually exclusive in the current template**, because of the invalid-HTML rule. That makes the unknown "overlay or panel" in the roadmap a structural decision:
  - **Overlay:** set `selectable` on the admin calendar. The tile becomes a `<button>` and `classActions` stops being used by any caller, so it could be removed. The actions move into a new admin class overlay that follows `class-details-overlay` / `class-bookings-overlay`. Duplicate and cancel could keep their below-calendar panels, opened from that overlay, or move into it.
  - **Panel:** the tile still needs one clickable element to select the class. That brings back the nested-interactive problem at a smaller size, unless the tile becomes selectable and the panel renders below.
  - Either way, `classes.spec.ts`'s `actionIn(tile, …)` helpers are rewritten.
- **One mechanism already serves drawing on both sides of every breakpoint.** When UX-01 removes the calendar below the phone breakpoint, UX-02 still matters above it: tablets and touch laptops sit between 30rem and 48rem and above.
  - The roadmap's "only while a gesture is genuinely on offer" can be read two ways:
    - (a) only on an admin, non-read-only calendar, which is already true;
    - (b) only while a press is active, so the class toggles during the gesture.
  - Reading (b) is subtle. `touch-action` is evaluated when the touch **starts**, so adding it at `pointerdown` is too late for that touch: the browser has already decided to pan.
  - A realistic fix is (c): keep `touch-action: none` only where pointer input is not coarse, or restrict it to the draft or an explicit "draw mode". The plan must choose, and the current comment's claim "the view built for phones" becomes false once phones get the refusal.
  - The library's own move and resize already use a 300 ms touch long-press, so they do not need `touch-action` on the grid.
- **Two conventions pull against each other for the breakpoint.**
  - Styles in this repo avoid Sass sharing: tokens are custom properties and there is no `@use`.
  - The existing TS breakpoint was made TS-only on purpose.
  - A value that both SCSS media queries and TS read cannot be a custom property. So UX-04 has three options:
    - a Sass partial plus `stylePreprocessorOptions.includePaths` plus a TS constant, kept in sync by a spec;
    - a TS constant bound onto hosts as classes or attributes, so stylesheets style against `:host(.is-mobile)` rather than media queries;
    - a CSS-only switch for layout plus a TS `matchMedia` only where rendering must differ.
  - UX-01 needs the TS side, because it withholds rendering and not just layout. Using `@if` rather than `display:none` keeps angular-calendar's DOM, and its listeners, off phones.
- **Where the boundary sits is not decided by the code.**
  - 30rem (480px) is the only existing "phone" threshold. Using it for UX-01 would give an admin on a phone in landscape (≥ 480px wide) the editing grid.
  - The roadmap says none of the three existing numbers is the right one. The plan owns this number.

## Historical Context (from prior changes)

- `context/archive/2026-09-02-schedule-calendar-view/plan-brief.md:40-42`:
  - admin actions come in by projection: `[calendarHeaderActions]` for screen-level actions and a `classActions` `ng-template` per class;
  - a `mode` flag was rejected;
  - day view by default, promoted to week through a guarded `matchMedia`;
  - the empty-state overlay uses `pointer-events: none` so it does not swallow the gesture.
- `context/archive/2026-09-02-schedule-calendar-view/plan.md:580-585`, "Adapted during implementation":
  - "A tile in a week grid cannot hold a number input and two buttons". This is why the duplicate and delete panels render below the calendar and a range change closes them.
  - This is the origin of the current split between tile buttons and panels below.
- `context/archive/2026-09-02-schedule-calendar-view/plan.md:594-598`: past weeks are `readOnly`, with no action template, no gesture and an inline note.
- `context/archive/2026-09-02-schedule-calendar-view/reviews/impl-review.md:117-147`, F5 "The gesture is mouse-only, on the view that exists because of phones":
  - Fix A was chosen: pointer events, `setPointerCapture` and `touch-action: none`.
  - Fix B was rejected: record FR-019 as desk-only.
  - The review said "Whether admins actually use a phone here is unknown."
  - **S-20 reverses this decision on the user's explicit instruction (2026-09-20, roadmap UX-01).** The plan should cite F5 so the reversal reads as a decision, not as drift.
- `context/foundation/prd-v2.md:318-326`, FR-019: drag across empty time creates a class; an overlay collects type and trainer; past weeks accept no gesture. It names no device. S-20 narrows it, and the PRD is versioned as shipped, so the narrowing goes in the roadmap or plan, not in `prd-v2.md`.
- `context/foundation/prd.md:52` (guardrail) and `prd.md:138`, the NFR:
  - "every **member-facing** screen stays comfortably usable on a phone" and "Mobile-first and installable".
  - `/admin/classes` is not member-facing, and `/schedule`, the surface a member uses, is untouched. That is why the guardrail survives UX-01.
- **The route comment is out of date.** `app.routes.ts:78-81` still says "~424 kB against a 500 kB budget". AGENTS.md records 513.90 kB against a 600 kB warning after S-23. If S-20 touches this file, correct it in passing.

## Related Research

- `context/archive/2026-09-02-schedule-calendar-view/`: the change that built the calendar, the gestures and the tile actions.
- `context/archive/2026-09-19-frontend-error-and-patterns/`: the four-outlet rule. UX-01's refusal is outlet 4, a **screen state**, and never a toast.
- `context/archive/2026-09-20-frontend-presentational-kit/`: `app-empty` and `app-loading`, which the refusal state must use under the `features/**` lint rule.

## Open Questions

1. **The breakpoint value for UX-01 and UX-04.** 30rem is the only existing phone number, but it lets a landscape phone through. Anything wider must still partition exactly against the 30rem nav rules or leave them alone. Owner: `/10x-plan`, with the user deciding the number.
2. **How UX-04 is shared.** The options are a Sass partial with `includePaths` plus a spec-synced TS constant, a TS-only constant with host bindings, or a hybrid. Should the 11 existing `30rem` literals migrate in S-20, or does S-20 only create the module and adopt it where UX-01 needs it? The roadmap says "one definition" but S-21 is its second consumer.
3. **Whether the actions go to an overlay or stay with the below-calendar panels.** This is a structural choice, because the tile cannot be both a button and a container of buttons. The overlay route means `selectable` on the admin calendar, and `classActions` becomes dead.
4. **How UX-02 is fixed.** `touch-action` cannot be toggled mid-touch, so the realistic options are gating by `(pointer: coarse)` or an explicit draw affordance. Or it may be enough to leave it as is, since the phone never renders the grid, and only fix the misleading comment.
5. **Whether `admin/classes/new` and `admin/classes/:id` (`ClassForm`) are also desk-only.** The form works on a phone today and has phone rules. The roadmap's refusal names `/admin/classes` alone.
6. **Whether the refusal is local to the screen or a reusable guard.** A guard would redirect, and the roadmap wants a screen state that says why, so a component-level `@if` fits the wording better. The roadmap already leans toward "one caller today".
