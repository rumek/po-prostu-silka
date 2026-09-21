# The Admin's Class Calendar Is a Desk Tool (S-20): Plan Brief

> Full plan: `context/changes/admin-schedule-web-only/plan.md`
> Research: `context/changes/admin-schedule-web-only/research.md`

## What & Why

`/admin/classes` was built as if it would be used on a phone as well as at a desk. Two problems follow from that.

- **At a desk, the per-class actions are clipped.** Edytuj / Powiel / Zapisani / Odwołaj are drawn inside a 1px-per-minute tile with `overflow: hidden`, so any class shorter than about 90 minutes hides them.
- **On a phone, the screen reads as broken.** `touch-action: none` covers the whole future grid, so the page will not scroll and the drawing and dragging gestures do not work.

This slice makes the screen honestly a desk tool: all actions are reachable, and a phone gets a plain refusal instead of the grid. It also gives the SPA one definition of a breakpoint, which both S-20 and S-21 need (M-7 UX-01 to UX-04).

## Starting Point

- **Actions:** projected into the tile through a `classActions` template. Three of the four confirmations then open as panels below the calendar, and Zapisani opens as an overlay.
- **Tile activation:** the calendar can already turn a tile into a button (`selectable`), but only the member's read-only `/schedule` uses it.
- **Breakpoints:** `30rem`, `30.0001rem` and `40rem` are written as literals across 12 stylesheets, and `48rem` exists only in TS. Nothing shares a value between Sass and TS.

## Desired End State

**Admin at 1024px or wider**
- Clicking any tile, whatever the class's length, opens a class-actions overlay with all four actions.
- Powiel, Usuń and Odwołaj are confirmed inside that overlay.
- Dragging or resizing a class never opens it.

**Admin below 1024px**
- The screen shows a card saying class editing happens at a computer.
- The card links to the schedule, "Dodaj zajęcia" and "Typy zajęć".

**Tablet**
- The grid scrolls under a finger, and drawing is withheld.

**Breakpoints**
- Every width `@media` goes through one Sass partial.
- A spec fails if the desk boundary in Sass and in TS disagree.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Refusal on the phone, not a touch rewrite | Withhold the grid below the boundary | Drawing and resizing half-hour blocks in a scrolling grid is a desk gesture. | Roadmap (user, 2026-09-20) |
| Desk boundary | 64rem (1024px) | It catches every phone, including in landscape, and a tablet in portrait; it also suits S-21's table. | Plan |
| Where actions go | Overlay opened from the tile, with confirmations inside | One surface at every class length, nothing scrolled away below the calendar, and the same pattern `/schedule` uses. | Plan |
| `touch-action` | Drawing only offered with `(pointer: fine)` | `touch-action` cannot be switched mid-touch, so the gesture must simply not be on offer to a finger. | Plan |
| Sharing Sass and TS | Sass partial + `includePaths` + TS constant + sync spec | Stylesheets keep plain `@media`, and drift fails the build. | Plan |
| Existing `30rem` literals | Named in the partial (mixins `narrow` / `above-narrow`), behaviour unchanged | Makes "one definition" true app-wide and encodes the 30 / 30.0001 partition once. | Plan |
| What the refusal offers | Reason + `/schedule` + "Dodaj zajęcia" + "Typy zajęć" | Withdraws only the grid, not the forms that work on a phone. | Plan |
| Past weeks | Tiles stay inert, as today | S-20 adds and removes no capability. | Plan |
| Refusal mechanism | `@if` on the screen, not a guard | A guard redirects instead of saying why, and there is one caller. | Research / Roadmap |
| Click after drag | Calendar ignores a click whose pointer moved | `angular-calendar` does not suppress it, and the tile moves with the drag. | Plan (verified in library source) |
| `classActions` slot | Removed | No caller is left, and a second way to put buttons in a tile would invite the clipping back. | Plan |

## Scope

**In scope:**
- the breakpoint module (Sass + TS + `mediaQuerySignal` helper + spec), with 12 stylesheets migrated;
- the phone refusal on `/admin/classes`;
- drawing gated on a fine pointer;
- click vs drag on a selectable, editable calendar;
- the new `class-actions-overlay`;
- removal of the panels and the `classActions` slot;
- rewritten specs;
- an AGENTS.md section;
- the stale bundle comment in `app.routes.ts`.

**Out of scope:**
- touch reimplementation of the gestures, or a draw-mode toggle;
- an overlay for past weeks;
- any change to tile or segment height, or to visuals (UX-09);
- making `ClassForm` desk-only;
- a route guard;
- `/schedule`;
- merging the calendar's 48rem;
- `cdk/layout`;
- editing `prd-v2.md`;
- navigation changes.

## Architecture / Approach

`core/layout/` gains:
- `breakpoints.ts`: `DESK_MIN_WIDTH`, `DESK_MEDIA_QUERY` and `FINE_POINTER_MEDIA_QUERY`;
- `media-query.ts`: `mediaQuerySignal(query, fallback)`, a guarded `matchMedia` that returns `fallback` in jsdom or on the server.

`src/styles/_breakpoints.scss` is the Sass twin, and `breakpoints.spec.ts` keeps the two equal and bans literal widths in `@media`.

The screen `Classes` switches on `desk()`. The calendar reads `finePointer()` to decide whether drawing is offered, and becomes selectable-and-editable on the admin screen.

The new overlay is presentational: it takes `row`, `busy`, `failed` and `deleteBlocked`, and reports intentions. `Classes` keeps every request, the row list and the toasts.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. One definition of a breakpoint | Sass partial, TS constants, `mediaQuerySignal`, 12 stylesheets migrated, sync spec, AGENTS.md | `includePaths` or `node:fs` behaving differently under the unit-test builder; the 30 / 30.0001 partition must stay exact |
| 2. Phone refusal | Below 64rem: a refusal card with three links and no calendar DOM | Stale overlay or panel state when crossing the boundary with something open |
| 3. Calendar: touch and click | Drawing only with `(pointer: fine)`; a click ending a drag does not select | The `matchMedia` stub in specs must become query-aware |
| 4. Actions overlay | All actions from a tile at any length; panels and `classActions` gone | The rewrite of `classes.spec.ts` must keep every behavioural assertion |

**Prerequisites:**
- S-19 is done (the four-outlet rule), and so is S-23 (the kit and its lint rule).
- No backend or API work.

**Estimated effort:** about 3 to 4 sessions. Phase 4 is the largest.

## Open Risks & Assumptions

- **Narrow desk windows get the refusal.** A desktop window below 1024px, such as a laptop split-screen, sees the refusal card. The user accepted this with the 64rem boundary.
- **Tablets lose drawing.** A tablet with a finger can still move a class (library long-press) and add one through the form, but cannot draw it. This is accepted.
- **The click tolerance is a guess.** The around-4px tolerance assumes the library's drag threshold is larger than a jittery click. Verify manually in Phase 4.
- **Focus is not restored after the overlay swap.** When Zapisani swaps overlays, focus does not return to the tile on close, because the original opener is gone. This is accepted, and nothing works around it.
- **A decision is being reversed.** The F5 decision in the calendar's impl review (2026-09-02) is reversed here. The screen's doc comment records it, so it reads as a decision rather than drift.

## Success Criteria (Summary)

- At a desk, an admin can edit, duplicate, view bookings for, and cancel or delete a 30-minute class.
- On a phone, `/admin/classes` says plainly that it is a desk tool and points to what does work.
- No width breakpoint in the SPA is written anywhere except `_breakpoints.scss`, and the spec suite enforces it.
