# Schedule Calendar View Implementation Plan

## Overview

The member schedule and the admin class panel both stop being day-grouped lists and become one
calendar, built on `angular-calendar` and shared by both screens. On a phone it renders the day
view; from the tablet breakpoint up, the week view. The admin panel layers its existing actions on
top of that same calendar and gains a new one: creating a class by dragging a time range, completed
in an overlay that supplies the class type and the trainer.

This delivers `prd-v2` US-02 and FR-015 – FR-018 (roadmap S-07), and deliberately exceeds them in
two places the user decided on during planning. Phase 1 amends the PRD rather than leaving it
contradicted.

## Current State Analysis

**The member schedule** (`src/app/src/app/features/schedule/schedule.ts`) fetches a flat, time-ordered
list from `GET /api/classes` and groups it into day sections in a `computed`, keyed on the browser's
local date. Its template renders sticky day headings over a card per class. There is no week
navigation, no date picker, and no way to see anything outside the server's window.

**The admin class panel** (`src/app/src/app/features/admin/classes/classes.ts`) is a flat list over
`GET /api/admin/classes`, with per-row edit / duplicate / delete, a per-row busy `Set`, a `failedId`,
a list-level `notice`, and a generation guard against out-of-order refetches. Its duplicate control
reports partial success per week.

**The read endpoints take no parameters** (`src/Application/Scheduling/ClassEndpoints.cs`). The
member schedule is hard-bounded to `ScheduleWindowDays = 14` from now; the admin list is unbounded
forward and starts at now, so the past is invisible on both. `IClassScheduleQuery.GetScheduleAsync`
already accepts `from`/`to` — only the endpoint fixes them.

**The class model is final.** S-06 settled it: name and description resolve through `ClassType`,
`instructor` resolves through the assigned account, `capacity` and `durationMinutes` are copies owned
by the occurrence, there is no room, and `freeSpots` equals capacity until S-08 introduces `Booking`.
This slice renders that shape and does not change it.

**Timezone discipline is established and must survive.** `ClubTime` (`src/Domain/Scheduling/ClubTime.cs`)
is used on the *write* path only, for DST-safe weekly duplication. Every read path returns UTC and the
SPA renders in the browser's clock. The calendar's week boundaries are therefore a browser-local
computation, not a server one.

**The app is zoneless.** `package.json` carries no `zone.js` at all, and `app.config.ts` provides
neither `provideZoneChangeDetection` nor an explicit zoneless provider — Angular 22's default applies.

**SSR is scaffolded but not wired, and that was verified rather than assumed.** `main.server.ts`,
`server.ts`, `app.config.server.ts` and `app.routes.server.ts` all exist, `@angular/ssr` is a
dependency, `app.config.ts` calls `provideClientHydration()`, and `app.routes.server.ts` declares a
single `path: '**'` with `RenderMode.Prerender`. None of it runs: the `build` target in
`angular.json` has no `server`, `ssr` or `prerender` key, so `dist/app/prerendered-routes.json` is
`{"routes": {}}`, there is no `dist/app/server/`, and `dist/app/browser/index.html` ships a bare
`<app-root></app-root>`. The app is client-rendered today.

This change does **not** wire SSR — that is infrastructure work unrelated to S-07. It does mean the
day/week decision cannot be justified by "there is no window at prerender time", because nothing
renders without a window. See "Critical Implementation Details" for the reason that actually holds.

**There is no shared-component category.** `core/` holds services, models and guards only; every
component lives under `features/`. FR-017's single calendar is the first component that belongs to
neither screen.

**Responsive design barely exists.** One media query in the whole front end
(`features/schedule/schedule.scss`, `max-width: 30rem`) and no breakpoint tokens in `styles.scss`.

**The design system is deliberately hand-rolled** (S-01 decision D7): Material and Tailwind were both
rejected, fonts are self-hosted, and component stylesheets carry layout only while colour and
typography come from tokens in `styles.scss`.

### External verification of the library

Checked against the npm registry and the published changelog rather than assumed:

- `angular-calendar@0.32.2`, last published 2026-04-08. Peer `@angular/core: >=20.2.0` — satisfied by
  this app's `^22.1.0`.
- 0.32.0: "angular 20.2.0 or higher is now required", "convert the library to standalone and
  deprecate the NgModules", "date-fns v4 is now required", "migrate away from @angular/animations to
  css animations".
- 0.32.2: "make current time marker update in zoneless mode" — the library is aware of zoneless,
  which this app requires.
- Non-optional peers: `angular-draggable-droppable@^9.0.1`, `angular-resizable-element@^8.0.0`.
  `date-fns@^4` and `moment@^2` are both marked optional in `peerDependenciesMeta`; this plan uses
  the `date-fns` adapter and does not install `moment`.
- Unpacked size ~731 KB.
- **SSR behaviour is unknown and does not matter here.** The changelog says nothing about
  server rendering, and this app performs none (see "SSR is scaffolded but not wired" above). It
  becomes a real question only if SSR is wired later, which this change does not do.

## Desired End State

A member opens `/schedule` on a phone and sees a day view for today, with controls to move by day
and week and to jump to a chosen date; the same route on a tablet or desktop shows the whole week.
An admin opens `/admin/classes` and gets that same calendar with edit, duplicate and delete on each
class, plus the ability to drag a time range on an empty part of the grid and complete it into a real
class through an overlay. Both roles can navigate backwards past today. A week or day with nothing in
it says so, over the grid, without blocking the admin's gesture.

Verified by: the automated criteria under each phase, plus the manual walkthrough in "Testing
Strategy".

### Key Discoveries

- `GET /api/classes` is hard-bounded to 14 days and takes no parameters —
  `src/Application/Scheduling/ClassEndpoints.cs:ScheduleWindowDays`. The backward arrow in FR-015 has
  nothing to show until this changes.
- `IClassScheduleQuery.GetScheduleAsync` already takes `from`/`to` (interface at the bottom of
  `ClassEndpoints.cs`). The query seam needs no new method for the member path; only
  `GetUpcomingForAdminAsync` gains an upper bound.
- `ClassScheduleQuery` filters `Status == Scheduled` on the member path and deliberately does not
  filter status for the admin path (`src/Infrastructure/Scheduling/ClassScheduleQuery.cs`). Both
  behaviours must survive the date-range change untouched.
- The generation guard against out-of-order refetches exists in
  `features/admin/classes/classes.ts:load()` and is absent from `features/schedule/schedule.ts` —
  which was safe only because the schedule fetched exactly once. Week navigation makes it a
  requirement on both.
- `class-form.ts:applyFailure()` maps all eleven `ClassFailure` reasons onto form controls. The
  overlay needs the same vocabulary, and duplicating that switch is how the two forms drift apart.
- `local-datetime.ts` already owns the "wall clock ↔ UTC instant" conversion and documents why the
  mistake is silent. The overlay's prefill goes through it rather than reinventing it.
- `TrainerSummary` and `getTrainers()` already exist in `core/admin/member-admin.service.ts` over
  `GET /api/admin/trainers`, as does `ClassTypeService` — the overlay's two selects need no new API.
- `styles.scss` owns colour and typography tokens; component stylesheets are layout-only by
  convention. The library's CSS has to be bent to those tokens, not shipped as a second visual
  language.

## What We're NOT Doing

- **No month view.** The library ships one; no route, control or view mode reaches it (`prd-v2`
  Non-Goals).
- **No booking from the calendar.** `freeSpots` is displayed; booking and cancelling are S-08.
- **No class cancellation.** The `Cancelled` status transition and its notifications are S-09; the
  admin's destructive action here remains `DELETE`, as it is today.
- **No drag-to-move or drag-to-resize of existing classes.** Only drag-to-*create* on empty grid space
  was accepted. Moving an existing class stays an edit-form operation.
- **No recurring series.** Weekly duplication stands, unchanged (`prd-v2` FR-013).
- **No trainer-facing screen.** The `Trainer` role still confers nothing (`prd-v2` Non-Goals).
- **No offline caching of the calendar.** `ngsw-config.json` stays empty by design.
- **No change to the class model, the overlap rule, or the free-spot projection.**

## Implementation Approach

Four phases, each leaving the app working end to end.

Phase 1 moves the two sources of truth — the PRD and the API contract — before any UI depends on
them. Phase 2 builds the shared calendar and puts the member schedule on it, which is the whole of
FR-015, FR-016 and FR-018. Phase 3 puts the admin panel on the same component through content
projection, which is FR-017. Phase 4 adds the gesture and the overlay, which is the accepted scope
expansion and the only part of this slice that writes.

Phases 1–3 can ship without Phase 4; cutting Phase 4 leaves FR-015 – FR-018 fully delivered.

## Critical Implementation Details

**The view mode has a first-paint default, and `matchMedia` still needs a guard.** The day and week
renderers are different components, so the choice between them cannot be made in CSS — it is a `matchMedia`
read. The default is the **day** view, on two grounds that survive the SSR finding above: the product is
mobile-first, so the narrow device gets the correct view with no reflow; and the initial value must be
usable before any measurement, since `matchMedia` is absent in the jsdom environment Vitest runs the
specs in.

The `matchMedia` read is wrapped in `isPlatformBrowser` — the pattern the three route guards already
use — even though nothing renders on a server today. `main.server.ts`, `server.ts` and
`app.routes.server.ts` are all present and one `angular.json` key away from being live; an unguarded
`matchMedia` would turn wiring SSR later into a runtime crash whose cause is three files away from the
change that triggered it. The guard costs one import.

**The empty-state overlay must not eat the gesture.** It is painted over the grid and needs
`pointer-events: none`, or Phase 4's drag-to-create is dead in exactly the week that most needs
filling — an empty one.

**Ordering: the failure-message table is extracted before the overlay uses it, not after.** Phase 4
creates the second place in the app that interprets `ClassFailure`. Writing the overlay's own switch
first and unifying later is how the two messages drift; the extraction is the first step of that
phase, not a cleanup at its end.

**Week boundaries are browser-local, and the request is UTC.** The calendar computes the visible
range in the browser's clock (`date-fns` `startOfWeek` with `weekStartsOn: 1`), then converts to UTC
instants for the query string. Computing the range in UTC would put a Monday-00:00 boundary an hour
inside Sunday for half the year, and classes would silently appear in the wrong week.

## Phase 1: Documented truth and a date-ranged read contract

### Overview

Amend `prd-v2` and the roadmap so the departure this slice makes is recorded where the next reader
looks, and give both read endpoints an explicit date range so the calendar has something to navigate
over.

### Changes Required:

#### 1. PRD amendment

**File**: `context/foundation/prd-v2.md`

**Intent**: FR-015 as written promises a phone day-strip with weekday chips and a list beneath. This
slice ships a day view from a calendar library instead. Leaving the document asserting the strip
would leave a future reader — and a future review — treating working code as a defect.

**Contract**: Rewrite FR-015 to describe the day view plus its navigation (today, move by day and
week, jump to a date). Amend the `## Success Criteria` → Guardrails entry "Mobile usability" to state
what replaced the day-picker and why the phone still does not get a week grid. Amend FR-016 to name
the breakpoint behaviour. Add a new `FR-019` under `### Calendar` for admin drag-to-create, marked
`[new]`, priority must-have, recording that it exceeds the original slice scope by explicit decision.
Amend the Non-Goal "No month view" to say the library's month view exists but is not routed to. Every
amendment keeps the file's existing style: the `[new]`/`[modified]` tag, the priority, and a short
rationale line.

#### 2. Roadmap S-07

**File**: `context/foundation/roadmap.md`

**Intent**: The S-07 outcome sentence describes a week strip and calls the slice presentation-only.
Both are now false.

**Contract**: Rewrite the `### S-07` `- **Outcome:**` line for the day/week views and the
drag-to-create addition; amend its `- **Risk:**` line, which currently opens "presentation-only" — it
now carries a write path. Add `v2 FR-019` to the item's `- **PRD refs:**` line, or the new requirement
is untraceable from the roadmap. Set `- **Status:** planning` and the matching `## At a glance` cell,
and bump the frontmatter `updated:`.

#### 3. Date range on the read endpoints

**File**: `src/Application/Scheduling/ClassEndpoints.cs`

**Intent**: Let both read endpoints answer for an explicit window, so the calendar can request the
week it is showing and can look backwards, while an omitted range keeps today's behaviour exactly.

**Contract**: `GetScheduleAsync` and `GetAdminClassesAsync` take optional `from` and `to`
`DateTimeOffset?` query parameters. Omitted, they fall back to what each endpoint does today: the
member path to `[now, now + ScheduleWindowDays)`, the admin path to `[now, +∞)`. Supplied, both are
honoured, including a `from` in the past — this is what makes the backward arrow work for both roles.
A range is refused with `400` and `ClassFailure("invalid_range")` when `to <= from`, or when the span
exceeds a new `MaxRangeDays` constant (62 — two months, comfortably above any week or day the UI asks
for, low enough that a malformed client cannot ask for a decade). The server reuses the `ClassFailure`
record as the response shape; add `invalid_range` to its doc-comment list, noting there that it is the
one reason produced by the **read** endpoints and never by a write — which is why the client models it
separately (see §5).

**Adapted during implementation.** A third refusal was added that this contract did not name: a
**half-supplied** range (`from` without `to`, or the reverse) is refused with the same
`invalid_range`. Pairing one supplied bound with the endpoint's default would answer a window nobody
asked for — a fortnight from an arbitrary date on the member path, or everything to the end of time on
the admin path — and would do it silently. Both parameters or neither. The shared
`ResolveRange` helper implements all four checks for both endpoints.

`IClassScheduleQuery.GetUpcomingForAdminAsync` gains a nullable `to`; `GetScheduleAsync` is already
shaped correctly and does not change.

#### 4. Query implementation

**File**: `src/Infrastructure/Scheduling/ClassScheduleQuery.cs`

**Intent**: Apply the admin path's new upper bound without touching either path's status filtering.

**Contract**: `GetUpcomingForAdminAsync(from, to, ct)` adds `c.StartsAt < to` when `to` is non-null.
The member path keeps `Status == ClassStatus.Scheduled`; the admin path keeps no status filter, so a
class S-09 later cancels stays visible to the admin. Neither `AsNoTracking`, the `OrderBy`, nor the
projection changes.

#### 5. Client contract

**File**: `src/app/src/app/core/scheduling/class.service.ts`,
`src/app/src/app/core/scheduling/class.models.ts`

**Intent**: Let the SPA ask for a window.

**Contract**: `getSchedule(from?: Date, to?: Date)` and `getAdminClasses(from?: Date, to?: Date)`
serialise to ISO-8601 UTC via `HttpParams`, omitting both when absent.

`invalid_range` gets its **own** client type — `ScheduleReadFailure { reason: 'invalid_range' }` — and
is deliberately **not** added to the `ClassFailure` union. `ClassFailure` mirrors the write contract
field for field, which is what its doc comment promises and what lets Phase 4's message table be
exhaustive over it; folding a read-only refusal in would force a write-form banner for a reason no
write path can return. The server still returns one `ClassFailure` record shape — the split is
client-side modelling of two different endpoint groups, matching how `ClassEndpoints.cs` already
separates them.

#### 6. Backend tests

**File**: `tests/po-prostu-silka.Tests/ClassEndpointTests.cs`

**Intent**: Pin the new parameter, and pin the fallback — a regression that silently drops the default
window would empty the schedule for every client that sends no range.

**Contract**: Cases for: omitted range on both endpoints reproduces today's window; an explicit range
returns only classes inside it; a `from` in the past returns past classes on both endpoints;
`to <= from` and a span over `MaxRangeDays` both return 400 `invalid_range`; the member endpoint still
excludes `Cancelled` and the admin endpoint still includes it, with a range supplied.

**Adapted during implementation.** The `Cancelled` case has no API path to reach it — no endpoint sets
that status until S-09 — so the test seeds it by writing `ClassStatus.Cancelled` straight to the
database through `fixture.Factory.Services`, then asserts through both HTTP endpoints. Writing to the
database from a test is a departure from this file's HTTP-only style; the alternative was leaving the
one behaviour that differs between the two read paths unpinned across a change that rewrites both.
Also added beyond the listed cases: the half-supplied range from §3, exercised on both endpoints.

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build` from `src/`
- Backend tests pass, new range cases included: `dotnet test` from the repo root
- Front end type-checks and tests pass: `npm test` from `src/app/`
- Front-end lint and format pass: `npm run quality:check` from `src/app/`

#### Manual Verification:

- `GET /api/classes?from=…&to=…` with a past `from` returns past classes; without parameters returns
  the same fortnight it does today
- `prd-v2.md` FR-015, FR-016, FR-019, the mobile guardrail and the month-view Non-Goal read as a
  coherent document, with no requirement left contradicting what this plan builds
- Roadmap S-07 outcome and risk lines describe the slice as planned

**Implementation Note**: After completing this phase and all automated verification passes, pause here
for manual confirmation from the human before proceeding to the next phase. Phase blocks use plain
bullets; the corresponding checkboxes live in the `## Progress` section at the bottom.

---

## Phase 2: Calendar core and the member schedule

### Overview

Install the library, create the app's first shared component, and put `/schedule` on it. This phase
delivers FR-015, FR-016 and FR-018 in full.

### Changes Required:

#### 1. Dependencies

**File**: `src/app/package.json`

**Intent**: Add the library and the peers it genuinely needs, and no more.

**Contract**: `angular-calendar@^0.32.2`, `date-fns@^4`, `angular-draggable-droppable@^9.0.1`,
`angular-resizable-element@^8.0.0`. `moment` is optional in `peerDependenciesMeta` and is not
installed — the `date-fns` adapter is the one this app uses. If npm resolves anything other than the
above, record what and why here as an "**Adapted during implementation.**" note.

#### 2. Calendar providers and stylesheet

**File**: `src/app/src/app/app.config.ts`, `src/app/angular.json`

**Intent**: Register the date adapter once, and load the library's base CSS so the component
stylesheets only have to override it.

**Contract**: Register the `date-fns` `DateAdapter` in `appConfig.providers`. 0.32 converted the
library to standalone and deprecated the NgModules, so prefer its standalone provider function; if the
published API still requires
`CalendarModule.forRoot({ provide: DateAdapter, useFactory: adapterFactory })`, use that and add an
"**Adapted during implementation.**" note to this contract rather than leaving it asserting something
untrue (`context/foundation/lessons.md`). Add `angular-calendar/css/angular-calendar.css` to the
`styles` array in `angular.json`, before the app's own `styles.scss`, so app tokens win.

`LOCALE_ID` is already `'pl'` and `registerLocaleData(localePl)` already runs; the calendar's `locale`
input is set from that, and the week starts on Monday.

**Adapted during implementation.** The providers do **not** go in `app.config.ts`. Registering them
there imports `angular-calendar` from the application config, which pulls the whole library into the
INITIAL bundle — measured: 562.61 kB against the 500 kB budget, with the two lazy chunks left at
~10 kB each. `provideCalendar({ provide: DateAdapter, useFactory: adapterFactory })` is declared on
the `ScheduleCalendar` component instead, so it ships in the lazy chunk of whichever route rendered
it: 465.95 kB initial, library in the 107 kB `schedule` chunk, budget met. The published API is
`provideCalendar(...)` as the standalone path predicted; `CalendarModule.forRoot` was not needed.

#### 3. Breakpoint token

**File**: `src/app/src/styles.scss`, `src/app/src/app/shared/calendar/calendar-breakpoint.ts`

**Intent**: One number for the day→week threshold, in the two places that cannot share a value — CSS
cannot be read by `matchMedia`, and a TS constant cannot be used in a media query.

**Contract**: A `--breakpoint-week: 48rem` custom property in `styles.scss` for stylesheet use, and an
exported `WEEK_VIEW_MIN_WIDTH = '48rem'` constant for the media-query string, each commenting that the
other exists and must move with it. 48rem (768px) is the first width at which seven day columns are
legible; the existing `30rem` query in `schedule.scss` goes away with the list it styles.

#### 4. The shared calendar component

**File**: `src/app/src/app/shared/calendar/schedule-calendar.ts` (+ `.html`, `.scss`)

**Intent**: The single calendar FR-017 requires — navigation, view switching, rendering, and the empty
state — with nothing role-specific inside it. This creates `shared/` as a new category in the front
end; note in the file header why it is neither `core/` (which holds no components) nor `features/`
(which would give one screen ownership of the other's calendar).

**Contract**:

- Inputs: `classes: ScheduledClass[]`, `loading: boolean`, `loadFailed: boolean`,
  `readOnly: boolean` (Phase 3 uses it; here it is `true` for the member).
- Outputs: `rangeChange: EventEmitter<{ from: Date; to: Date }>` — emitted whenever the visible window
  changes, including on init. That is the only output in this slice: a `classSelected` was considered
  and left out, because nothing consumes it — the member has no details view until booking lands in
  S-08, and the admin reaches a class through the projected action template.
- Content projection: `<ng-content select="[calendarHeaderActions]">` for screen-level buttons, and a
  projected `ng-template` for per-class actions, receiving the `ScheduledClass` as context. The member
  screen passes neither.
- State: `viewDate` signal; `viewMode` signal initialised to `'day'`; navigation by day and by week,
  plus a native `<input type="date">` for the jump control (no third-party date picker — the design
  system is hand-rolled).

  **Adapted during implementation.** Both views are ONE renderer, not two: `mwl-calendar-week-view`
  with `daysInWeek` bound to 1 or 7. The library does ship a separate day-view component, but one
  renderer means one event template, one set of style overrides and — in Phase 4 — one drag path.
  `viewDate` is bound to the computed range start rather than to a free-floating anchor, so what the
  grid draws and what was fetched cannot disagree. (`weekStartsOn` is ignored by the library whenever
  `daysInWeek` is set, which is exactly why the range start is computed here and passed in.)
- View switching: inside an `isPlatformBrowser` guard, subscribe to
  `matchMedia('(min-width: ' + WEEK_VIEW_MIN_WIDTH + ')')` and set `viewMode` from its `matches`,
  including on later resizes. The signal's initial `'day'` is what renders before the first read and
  in specs, where jsdom provides no `matchMedia` — see "Critical Implementation Details".

  **Adapted during implementation.** The platform check alone is not enough: jsdom IS a browser
  platform and still has no `matchMedia`, so `isPlatformBrowser` passed and the call threw. The guard
  is `isPlatformBrowser(...) && typeof window.matchMedia === 'function'`. The day-first default has to
  actually survive the API's absence rather than merely be documented as doing so — the specs found
  this, which is the coverage the build cannot give (see the Phase 2 build criterion).

  Also adapted: the `locale` input is bound to the injected `LOCALE_ID`, not to a hardcoded `'pl'`.
  Hardcoding it made the component demand CLDR data only `app.config.ts` registers, which failed with
  NG0701 anywhere that config does not run.
- Range computation: from `viewDate` and `viewMode`, using `date-fns` `startOfDay`/`startOfWeek`
  (`weekStartsOn: 1`) in the **browser's** local clock, converted to UTC instants on emit.
- Mapping: `ScheduledClass` → `CalendarEvent` with `start` from `startsAt`,
  `end = start + durationMinutes`, the title carrying name and instructor, and `meta` carrying the row
  so the projected action template gets the real object rather than a reconstruction.
- Empty state: when `classes` is empty and neither `loading` nor `loadFailed`, an overlay across the
  grid with an explanatory message, `pointer-events: none` (see "Critical Implementation Details").
  `loading` and `loadFailed` keep their own distinct treatments — "nothing scheduled" and "it failed to
  load" must not look alike, which the current `schedule.html` is careful about and this must preserve.

#### 5. Calendar styling against the design system

**File**: `src/app/src/app/shared/calendar/schedule-calendar.scss`

**Intent**: The library arrives with its own visual language; the app has a deliberate one (S-01 D7).

**Contract**: Override the library's colour, border, font and hour-row metrics to the `styles.scss`
tokens (`--ink`, `--line`, `--muted`, `--accent`, `--space-*`, `--radius-*`, the two font families).
Layout and overrides only — no new colour literals. A class at capacity keeps the existing `--danger`
treatment.

#### 6. Member schedule on the calendar

**File**: `src/app/src/app/features/schedule/schedule.ts` (+ `.html`, `.scss`)

**Intent**: Replace the day-grouped list with the shared calendar and fetch per visible range.

**Contract**: The `days()` grouping `computed` and the day-section template go away — the calendar owns
grouping now. `load(range)` calls `getSchedule(range.from, range.to)` and is driven by `(rangeChange)`.
**Add the generation guard** from `classes.ts:load()`: week navigation makes out-of-order responses
reachable here for the first time, and without it a slow fetch for last week can overwrite this week's
rows. `endsAt()` moves into the calendar's event mapping. `readOnly` is `true`; no action template is
projected. Keep the retry affordance on `loadFailed`.

#### 7. Front-end tests

**File**: `src/app/src/app/features/schedule/schedule.spec.ts`,
`src/app/src/app/shared/calendar/schedule-calendar.spec.ts`

**Intent**: Cover what this codebase wrote — navigation, range emission, mapping and view switching —
not the library's own DOM.

**Contract**: `schedule.spec.ts` is rewritten against the new fetch shape: the request URL carries the
expected `from`/`to` for the initial range, moving a week refetches with the shifted range, and a stale
response cannot overwrite a fresher one. `schedule-calendar.spec.ts` covers: `rangeChange` emitting a
Monday-start week in local time; `ScheduledClass` → `CalendarEvent` mapping including the computed end;
the empty overlay appearing only when not loading and not failed; and `viewMode` defaulting to `'day'`
with no `matchMedia` present, then promoting to `'week'` when a stubbed `matchMedia` matches. The
existing `at()` helper is reused.

#### 8. Lazy-load the two calendar routes

**File**: `src/app/src/app/app.routes.ts`

**Intent**: Keep the library out of the initial bundle, which has roughly 76 kB of headroom against a
500 kB budget — see "Performance Considerations". This is the default, not a fallback.

**Contract**: `/schedule` and `/admin/classes` switch from `component:` to
`loadComponent: () => import(...).then(m => m.X)`. Their guards (`authGuard`, `activeMemberGuard`,
`adminGuard`) and the route order — `'admin/classes/new'` before `'admin/classes/:id'` — are unchanged;
only the component reference becomes lazy. `/admin/classes/new` and `/admin/classes/:id` may stay eager
(`class-form` does not import the calendar), but converting them costs nothing and keeps the admin
class routes consistent. The library is imported only by `shared/calendar/`, so no other route pulls it
in transitively.

### Success Criteria:

#### Automated Verification:

- Front-end tests pass: `npm test` from `src/app/`
- Lint and format pass: `npm run quality:check` from `src/app/`
- Initial bundle stays inside the 500 kB warning budget, with the calendar in its own lazy chunk —
  read from `npm run build` output; a budget warning naming the initial bundle fails this criterion
- Production build succeeds: `npm run build` from `src/app/`. Note what this does **not** prove: the
  build performs no server render (see Current State Analysis), so it cannot catch a `matchMedia` or
  `window` reference escaping the platform guard. The specs do — jsdom provides no `matchMedia`, so an
  unguarded read fails `npm test`.
- Backend tests still pass: `dotnet test` from the repo root

#### Manual Verification:

- `/schedule` on a narrow window shows the day view for today; widening past 48rem switches to the week
  view, and narrowing switches back
- Backward and forward navigation moves by week, the day controls move by day, and the date input jumps
  to a chosen date — each refetching, with times shown in the browser's clock
- A day and a week with no classes each show the message over the grid, distinct from the loading and
  the failed states
- Classes render at the right times with name, instructor and free spots; a full class reads as full
- On a phone, first paint is the day view with no layout flash; on a wide screen the promotion to the
  week view happens once, immediately, and does not oscillate on resize
- Navigating to `/schedule` fetches the calendar's lazy chunk (visible in the network panel); loading
  `/login` and `/register` fetches no such chunk

**Implementation Note**: Pause here for manual confirmation from the human before proceeding to Phase 3.

---

## Phase 3: The admin panel on the same calendar

### Overview

FR-017: the admin class panel uses the same navigation and rendering as the member schedule, with its
actions on top, and gains visibility into past weeks.

### Changes Required:

#### 1. Admin panel on the shared calendar

**File**: `src/app/src/app/features/admin/classes/classes.ts` (+ `.html`, `.scss`)

**Intent**: Swap the flat list for the shared component without losing any of the panel's existing
behaviour.

**Contract**: Render `<app-schedule-calendar>` with the "Typy zajęć" and "Dodaj zajęcia" links projected
into `[calendarHeaderActions]`, and edit / duplicate / delete projected into the per-class action
template. Every existing behaviour survives intact: the per-row `busy` set, `failedId`, the list-level
`notice`, the inline duplicate control with its per-week partial-success message, the inline delete
confirmation (never `confirm()`), and the `copiesWord` Polish plural. `load()` becomes range-driven
through `(rangeChange)` and calls `getAdminClasses(from, to)`; its generation guard stays. After a
successful duplicate the current range is refetched — copies land in *later* weeks, so some will be
outside the visible one, and the existing "created N, skipped weeks X" message remains the report of
what happened.

**Adapted during implementation.** The duplicate and delete-confirm panels do **not** go inside the
projected per-class template. A tile in a week grid cannot hold a number input and two buttons. They
render BELOW the calendar instead, one at a time, each naming the class and start time it acts on —
the naming is what replaces the positional context an inline strip used to give. The tile keeps three
plain link-buttons and the per-row error. Also: a range change closes any open panel, because the
class it refers to may no longer be on screen.

#### 2. Past weeks are read-only

**File**: `src/app/src/app/features/admin/classes/classes.ts`,
`src/app/src/app/shared/calendar/schedule-calendar.ts`

**Intent**: Navigating backwards is now possible for the admin too. Editing history is not what that is
for, and in Phase 4 an unguarded past week would let a gesture create a class before today — which the
API refuses with `starts_in_past` anyway, so the UI should not offer it.

**Contract**: The admin screen passes `readOnly` computed as "the visible range ends before now". When
`readOnly` is true the calendar renders no action template and (Phase 4) accepts no gesture. A short
inline note says the week has passed, so a missing button reads as deliberate rather than broken.

#### 3. Tests

**File**: `src/app/src/app/features/admin/classes/classes.spec.ts`

**Intent**: Keep the panel's existing coverage honest through the rewrite, and pin the new rule.

**Contract**: Existing cases are re-pointed at the calendar's projected actions rather than the list
rows — duplicate partial success, delete confirmation, per-row busy, failure isolation. New cases: the
range drives the fetch; a past-week range renders no actions.

### Success Criteria:

#### Automated Verification:

- Front-end tests pass: `npm test` from `src/app/`
- Lint and format pass: `npm run quality:check` from `src/app/`
- Production build succeeds: `npm run build` from `src/app/`

#### Manual Verification:

- `/admin/classes` and `/schedule` show visibly the same calendar and navigate identically
- Edit, duplicate and delete all still work from the calendar, including the partial-success message
  naming skipped weeks and the inline delete confirmation
- Navigating to a past week shows its classes with no actions and an explanatory note
- A duplicate whose copies land in later weeks reports correctly, and navigating forward finds them

**Implementation Note**: Pause here for manual confirmation from the human before proceeding to Phase 4.

---

## Phase 4: Creating a class by dragging

### Overview

The accepted scope expansion: the admin drags a time range on empty grid space and completes it into a
class through an overlay carrying the class type and the trainer. Nothing in FR-015 – FR-018 depends on
this phase.

### Changes Required:

#### 1. Shared failure vocabulary — first, not last

**File**: `src/app/src/app/core/scheduling/class-failure.ts` (new),
`src/app/src/app/features/admin/classes/class-form.ts`

**Intent**: This phase creates the app's second interpreter of `ClassFailure`. Two independent `switch`
statements over eleven reasons is how the two forms come to say different things about the same
refusal. Extracted before the overlay is written, so the overlay never has its own copy.

**Contract**: A `classFailureMessage(reason: ClassFailure['reason']): string` returning the Polish
banner text for each reason, exhaustive over the union so adding a reason breaks the build rather than
falling through to a generic message. `class-form.ts` keeps `applyFailure()` — mapping a reason onto a
*control* is form-shape-specific and cannot be shared — but sources its banner strings from this table
instead of holding literals. Behaviour is unchanged; only the strings move.

#### 2. Drag-to-create on the calendar

**File**: `src/app/src/app/shared/calendar/schedule-calendar.ts` (+ `.html`, `.scss`)

**Intent**: Turn a drag on empty grid space into a start time and a duration.

**Contract**: A new output `rangeDrawn: EventEmitter<{ startsAt: Date; durationMinutes: number }>`,
emitted only when `readOnly` is false. Built on the library's hour-segment interaction together with
`angular-draggable-droppable` — the pattern its own drag-to-create demo uses — showing a provisional
block while the pointer is down.

**Adapted during implementation.** `angular-draggable-droppable` is not used for the gesture; it stays
installed only as a required peer of the library. The gesture is a custom `hourSegmentTemplate` that
stamps each segment with `data-segment="<iso>"` plus a `mousedown` handler, with move and release
listeners on the DOCUMENT so a drag that leaves the grid still ends cleanly. The segment under the
pointer is read with `elementFromPoint(...).closest('[data-segment]')` rather than computed from pixel
offsets and segment heights — that arithmetic would have to track the stylesheet to stay correct.
`hourSegments` is bound to 2 explicitly (30-minute snapping) because the drawn duration is measured in
whole segments. The custom template also re-renders the library's own time-label branch; dropping it
empties the hour column. The provisional block is view state only; nothing is persisted until
the overlay submits. Snap to the grid's hour-segment size and enforce a minimum of one segment, so a
stray click cannot emit a zero-minute class. The gesture is ignored when it starts on an existing
class, and the empty-state overlay's `pointer-events: none` is what keeps it alive on an empty week.

#### 3. The create overlay

**File**: `src/app/src/app/features/admin/classes/class-create-overlay.ts` (+ `.html`, `.scss`)

**Intent**: Supply what the gesture cannot: which class type, and which trainer.

**Contract**: Opened with the `startsAt` and `durationMinutes` from `rangeDrawn`. Two selects — class
type and trainer — plus editable duration and capacity. The type select is filled from
`ClassTypeService.getAll()` filtered **client-side** on `isActive`, mirroring `class-form.ts:135`;
`getAll()` is deliberately unfiltered (its doc comment says so, because the types list screen needs the
inactive ones) and stays that way. Trainers come from `MemberAdminService.getTrainers()`, which already
returns active trainers only. Selecting a type prefills duration and capacity from its defaults,
**copying** them into the request exactly as `class-form` does; the drawn duration wins over the type's
default, since the admin just expressed it with the gesture. Submits `ClassRequest` through
`ClassService.create()`. Refusals are rendered with `classFailureMessage`, with `time_conflict` — the
one the gesture can plausibly hit — additionally highlighted on the time row. Success closes the
overlay and refetches the visible range. Escape and a cancel control both close it without writing. The
empty-trainer-list case behaves as `class-form`'s `noTrainers` does: say so, and do not offer a submit
that cannot succeed. Field styling comes from the shared `.field` / `.button` classes in `styles.scss`.

**Adapted during implementation.** The refusal is shown as a banner only; there is no per-control
highlight for `time_conflict`. The overlay has no editable time control to attach one to — the time
comes from the gesture and is displayed, not edited. To change it, redraw. Also: `classFailureMessage`
takes `unknown` rather than the reason union, so a server one version ahead produces the fallback
message instead of `undefined`; and the backdrop is a `<button>` with an aria-label rather than a
click-handling `<div>`, with Escape bound on the host so it closes wherever focus is.

#### 4. Wiring

**File**: `src/app/src/app/features/admin/classes/classes.ts` (+ `.html`)

**Intent**: Connect the gesture to the overlay on the admin screen only.

**Contract**: `(rangeDrawn)` opens the overlay with the emitted values; its success event triggers the
same range refetch `load()` already performs. The member schedule passes `readOnly` and never wires
this.

#### 5. Tests

**File**: `src/app/src/app/features/admin/classes/class-create-overlay.spec.ts`,
`src/app/src/app/core/scheduling/class-failure.spec.ts`

**Intent**: Cover the new write path's logic without simulating pointer gestures in jsdom, per the
agreed test scope.

**Contract**: The overlay is tested from its inputs down: prefill from the drawn range and the selected
type, the request body it submits, `time_conflict` and `instructor_not_trainer` rendering the right
message, and the empty-trainer-list case. `class-failure.spec.ts` asserts every reason in the union has
a message. The drag gesture itself is covered manually — `rangeDrawn` is invoked directly in tests
rather than synthesised from pointer events.

### Success Criteria:

#### Automated Verification:

- Front-end tests pass: `npm test` from `src/app/`
- Lint and format pass: `npm run quality:check` from `src/app/`
- Production build succeeds: `npm run build` from `src/app/`
- Backend tests still pass: `dotnet test` from the repo root

#### Manual Verification:

- Dragging on empty grid space in the admin week view opens the overlay with the drawn start and
  duration prefilled
- Choosing a type prefills capacity, and the drawn duration is preserved rather than overwritten
- Submitting creates the class, closes the overlay, and the class appears in place
- Dragging over an existing class does not start a gesture; a past week accepts no gesture at all
- A gesture into an occupied slot is refused with the time-conflict message, and the overlay stays open
  with the values intact
- The same refusal produces the same wording in the overlay and in `class-form`

**Implementation Note**: This is the final phase. Confirm the full manual walkthrough below before
closing the change.

---

## Testing Strategy

### Unit Tests:

- Visible-range computation: Monday-start weeks in local time, day ranges, and the DST week — a week
  containing a transition must still be seven days and must not shift the boundary
- `ScheduledClass` → `CalendarEvent` mapping, including the derived end time
- View-mode default (`day`) with no `matchMedia`, promotion to `week` when one matches
- Generation guard on both screens: a stale response cannot overwrite a fresher one
- Overlay prefill, submitted request body, and failure rendering
- `classFailureMessage` exhaustive over the reason union

### Integration Tests:

- `from`/`to` honoured, omitted-range fallback preserved on both endpoints, past `from` accepted,
  `to <= from` and over-long spans refused with `invalid_range`
- Member path still excludes `Cancelled`; admin path still includes it

### Manual Testing Steps:

1. Open `/schedule` on a phone-width window: day view for today, navigation by day and week, jump-to-date
2. Widen past 48rem: the week view takes over; narrow again: it reverts
3. Navigate to an empty week and an empty day: the message appears over the grid, distinct from loading
   and from failure
4. Navigate backwards past today as a member: past classes are visible
5. Open `/admin/classes`: the same calendar, with the header links and per-class actions
6. Edit, duplicate (with a week that collides, to see the partial-success report) and delete from the
   calendar
7. Navigate to a past week as admin: classes visible, no actions, explanatory note
8. Drag an empty range in a future week: overlay opens prefilled; pick a type and a trainer; submit; the
   class appears
9. Drag onto a slot already occupied and submit: the time-conflict message appears and the overlay keeps
   the values
10. Reload with the network throttled: loading, then content — never an empty grid presented as an empty
    week

## Performance Considerations

The PRD commits to ~1 s perceived response for browsing the schedule, and this slice changes the shape
of that request. Each navigation now fetches one day or one week instead of one fixed fortnight —
strictly less data per request, more requests. `MaxRangeDays = 62` caps what any client can ask for. The
admin list stops being unbounded, which removes a query that grew with every class the club ever
scheduled.

**The library does not fit in the initial bundle, so it is not put there.** `angular.json` sets the
initial-bundle budget at 500 kB warning / 1 MB error, and today's `dist/app/browser/main-*.js` is
433,848 bytes — about 76 kB of headroom against a library that is ~731 KB unpacked before `date-fns`
v4 and the two drag/resize peers. Phase 2 therefore lazy-loads `/schedule` and `/admin/classes` from
the start (see Phase 2 §8) rather than treating it as a contingency, and Phase 2 verifies the budget
rather than waiting for the build to complain.

The upside is not only the budget: login, register and the pending screen are what an unapproved
member sees, and none of them should pay for a calendar. The cost is one extra chunk fetch on the two
routes that do use it.

## Migration Notes

No schema change and no data migration: this slice is presentation plus one write path over the model
S-06 settled.

The read-endpoint change is backward-compatible by construction — omitting both parameters reproduces
today's behaviour exactly, which is what the Phase 1 test pins. The SPA and the API ship together (the
SPA is served from the API's own wwwroot), so there is no window in which an old client meets a new
server.

Rollback is a redeploy of the previous artifact, with no schema to reverse.

## References

- Requirements: `context/foundation/prd-v2.md` — US-02, FR-015 – FR-018 (amended by this change's
  Phase 1); `context/foundation/prd.md` FR-007
- Roadmap item: `context/foundation/roadmap.md` — S-07
- Predecessor: `context/archive/2026-09-02-occurrences-from-class-types/plan.md` (S-06) — the class
  model this renders
- Patterns reused: `src/app/src/app/features/admin/classes/classes.ts` (generation guard, per-row busy,
  inline confirmation), `src/app/src/app/features/admin/classes/class-form.ts:applyFailure`
  (failure mapping), `src/app/src/app/core/scheduling/local-datetime.ts` (wall clock ↔ UTC)
- Library: `angular-calendar` 0.32.2 — https://mattlewis92.github.io/angular-calendar/ ; changelog
  https://mattlewis92.github.io/angular-calendar/docs/changelog.html
- Recurring rules: `context/foundation/lessons.md` — record necessary adaptations in the plan itself

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Documented truth and a date-ranged read contract

#### Automated

- [x] 1.1 Backend builds: `dotnet build` from `src/` — e29367c
- [x] 1.2 Backend tests pass, new range cases included: `dotnet test` from the repo root — e29367c
- [x] 1.3 Front end type-checks and tests pass: `npm test` from `src/app/` — e29367c
- [x] 1.4 Front-end lint and format pass: `npm run quality:check` from `src/app/` — e29367c

#### Manual

- [ ] 1.5 Date-ranged and omitted-range requests behave as specified
- [ ] 1.6 `prd-v2.md` amendments read as a coherent document
- [ ] 1.7 Roadmap S-07 outcome and risk lines describe the slice as planned

### Phase 2: Calendar core and the member schedule

#### Automated

- [x] 2.1 Front-end tests pass: `npm test` from `src/app/` — 4c0b911
- [x] 2.2 Lint and format pass: `npm run quality:check` from `src/app/` — 4c0b911
- [x] 2.3 Initial bundle inside the 500 kB budget, calendar in its own lazy chunk — 4c0b911
- [x] 2.4 Production build succeeds: `npm run build` from `src/app/` — 4c0b911
- [x] 2.5 Backend tests still pass: `dotnet test` from the repo root — 4c0b911

#### Manual

- [ ] 2.6 Day view narrow, week view from 48rem, reverting on narrowing
- [ ] 2.7 Day, week and jump-to-date navigation each refetch, times in the browser's clock
- [ ] 2.8 Empty day and empty week show the message over the grid, distinct from loading and failure
- [ ] 2.9 Classes render with name, instructor and free spots; a full class reads as full
- [ ] 2.10 Day view at first paint on a phone with no flash; single, immediate promotion on a wide screen
- [ ] 2.11 Navigating to /schedule fetches the calendar chunk; login and register do not

### Phase 3: The admin panel on the same calendar

#### Automated

- [x] 3.1 Front-end tests pass: `npm test` from `src/app/` — 302cfe0
- [x] 3.2 Lint and format pass: `npm run quality:check` from `src/app/` — 302cfe0
- [x] 3.3 Production build succeeds: `npm run build` from `src/app/` — 302cfe0

#### Manual

- [ ] 3.4 Both screens show the same calendar and navigate identically
- [ ] 3.5 Edit, duplicate and delete work from the calendar, partial success and inline confirmation intact
- [ ] 3.6 A past week shows classes with no actions and an explanatory note
- [ ] 3.7 Duplicate copies land in later weeks and are found by navigating forward

### Phase 4: Creating a class by dragging

#### Automated

- [x] 4.1 Front-end tests pass: `npm test` from `src/app/`
- [x] 4.2 Lint and format pass: `npm run quality:check` from `src/app/`
- [x] 4.3 Production build succeeds: `npm run build` from `src/app/`
- [x] 4.4 Backend tests still pass: `dotnet test` from the repo root

#### Manual

- [ ] 4.5 Dragging empty grid space opens the overlay with start and duration prefilled
- [ ] 4.6 Type selection prefills capacity and preserves the drawn duration
- [ ] 4.7 Submitting creates the class and it appears in place
- [ ] 4.8 No gesture over an existing class, and none at all in a past week
- [ ] 4.9 A conflicting slot is refused, the overlay stays open with values intact
- [ ] 4.10 The same refusal reads identically in the overlay and in `class-form`
