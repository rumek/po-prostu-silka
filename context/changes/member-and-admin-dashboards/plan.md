# Member and Admin Dashboards with a Mobile Bottom Bar — Implementation Plan

## Overview

Replace the `Home` placeholder with the two dashboards S-12 owes (FR-023, FR-024), and restructure the
application shell so that on a phone the navigation lives in a fixed bottom bar instead of the top
header. Both dashboards read data that already exists; no backend work, no migration.

## Current State Analysis

- **The backend is complete for this slice.** Every card is served by an existing, already-authorised
  endpoint: `GET /api/bookings/mine`, `GET /api/plans/mine`, `GET /api/admin/members/pending`, and
  `GET /api/admin/classes?from=&to=`. Member dashboard costs 2 round-trips, admin 3. See
  `research.md` §1.
- **`/` renders a placeholder.** `features/home/home.ts` is a greeting plus two links, and its own doc
  comment names S-12 as the slice that replaces it.
- **`app.html` is the entire navigation system.** There is no layout component beyond it and `app.ts`.
  It carries 6 role-gated links plus a logout `<button>`, each condition duplicating a guard.
- **Four admin screens have no navigation at all.** `/admin/members`, `/admin/classes`,
  `/admin/class-types` and `/admin/exercises` are reachable only by typing a URL. S-10 raised the
  nav entry as a decision, dropped it, and deferred it to this slice by name.
- **A bottom bar is greenfield.** No `env(safe-area-inset-*)`, no `viewport-fit=cover`, no
  `position: fixed` navigation, no shared z-index scale, and no `aria-current` anywhere in the app.
- **The manifest declares `display: standalone`**, so on iOS the home indicator overlaps a bottom bar
  unless safe-area padding is applied.
- **`/` is eager** and the initial bundle has twice been pushed against the 500 kB warning budget
  (424 kB → 475 kB → 502.88 kB, which is what made S-10's screens lazy).

## Desired End State

An approved member opening the app lands on a dashboard showing their three nearest booked classes and
their active training plan, each linking to the full screen. An admin sees that plus a section of items
needing attention: how many registrations await approval, what runs today, and what is coming up. On a
phone all navigation is a fixed bottom bar of five tabs — Start, Grafik, Moje zajęcia, Mój plan, Więcej
— sitting above the iOS home indicator, with the current tab marked for assistive technology. "Więcej"
holds the account screen, logout, and for an admin or trainer the panel links, including the four admin
screens that previously had no entry point. On a tablet or desktop the top header is unchanged and the
bar is not rendered.

Verify by: signing in as a member and as an admin on a narrow viewport and on a wide one; confirming
the tab set does not change shape between roles; confirming an admin can reach all six panel screens
from "Więcej"; and confirming `npm run build` stays inside the 500 kB budget.

### Key Discoveries:

- `GET /api/admin/classes` defaults its window to `[now, now+62d)` — the no-parameter form **starts at
  `now`, not local midnight** (`src/Application/Scheduling/ClassEndpoints.cs:295-297`). A "dzisiaj"
  card must pass explicit bounds or it silently omits classes that already started today.
- `GET /api/plans/mine` answers **204** when there is no active plan, already coalesced to `null` in
  `core/training/training-plan.service.ts:82-86`. "No plan" is a successful load, not an error —
  `my-plan.html:48-57` already renders it as a plain card rather than an alert.
- `AuthService.sessionResolved` (`core/auth/auth.service.ts:50-51`) gates the role signals: before it
  flips, `isAdmin()`/`isActive()` read `false` regardless of the real role. The shell renders before
  any guard runs, which is why `app.html:6` wraps the nav in `@if (auth.isAuthenticated())`.
- The S-01 implementation review
  (`context/archive/2026-09-01-registration-and-approval/reviews/impl-review.md:125-133`, F5) caught a
  shipped bug where a nav condition disagreed with its guard. Every condition moved in this plan
  re-opens that bug class.
- `date-fns` is pulled in **only** by the lazy calendar chunk. The dashboard is eager and must not
  import it.
- `.link-button` is declared locally in 17 `.scss` files; there is no global rule for it.
- `app.spec.ts` (230 lines) is 11 tests, all asserting per-role link visibility in the shell.

## What We're NOT Doing

- **No new API endpoint and no aggregated `/api/dashboard`.** 2–3 cheap round-trips do not justify
  forking a second projection of four DTOs the SPA already mirrors.
- **No actions on dashboard cards.** The dashboard is read-only: no cancelling a booking and no
  approving a registration from a card. Cards link to the screens that own those actions.
- **No promotion of `.link-button` to `styles.scss`.** Seventeen files is a separate change; the new
  components declare their own copy like every existing feature.
- **No offline behaviour.** `ngsw-config.json` has empty `assetGroups` and `dataGroups` by design; the
  bar gets no caching and must not assume any.
- **No SSR activation.** SSR stays dormant (`outputMode: "static"`); browser-API guards are written as
  if it could be switched on, but nothing here turns it on.
- **No in-app notification centre.** Explicitly removed by the PRD (FR-022).
- **No redesign of the desktop header** beyond hiding its links on phones.

## Implementation Approach

Four phases, each leaving the app working and `npm test` green.

The refactor comes first and alone: extracting the two summary components while `my-classes` and
`my-plan` are the only consumers means their existing tests are the safety net for the extraction. The
dashboard then consumes components that are already proven. Navigation lands last, and the "Więcej"
screen is built before the bar that links to it, so the bar never ships pointing at a route that does
not exist.

The bar's tab set is deliberately **role-independent**: five fixed tabs for everyone, with every
role-conditional destination behind "Więcej". This removes the visibility matrix from the bar entirely,
which is the S-01 bug class; the conditions survive in exactly one place, the "Więcej" screen, where
they can be tested as a unit.

## Critical Implementation Details

**Local midnight without `date-fns`.** The admin "today" window must be computed with native `Date`
arithmetic — construct a `Date`, `setHours(0, 0, 0, 0)` for `from`, and add one day for `to`, then
serialise with `toISOString()`. This matches how `core/scheduling/local-datetime.ts` already works with
native getters, and avoids pulling `date-fns` into the eager initial bundle, where it currently reaches
only through the lazy calendar chunk.

**Heading levels in a shared component.** `my-plan.html:9-14` wraps the plan name in an `<h1>`. On the
dashboard the same block sits under the page's own `<h1>`, so the shared component must accept the
heading level as an input rather than hard-coding one. Shipping it as a fixed `<h1>` puts two `h1`
elements on the dashboard.

**Stacking order.** The three existing overlays (`class-bookings-overlay.scss:7`,
`class-create-overlay.scss:4`, `class-details-overlay.scss:7`) are `position: fixed` and
`aria-modal="true"`. A modal must cover the bar, so the bar's `z-index` goes **below** theirs. There is
no shared z-index scale today; introduce the bar's value alongside a comment naming the overlays it
must sit under.

**`viewport-fit=cover` is global.** Adding it to `index.html` changes layout insets on every screen,
not just the ones with the bar. It is required for `env(safe-area-inset-bottom)` to resolve to anything
other than `0px` on iOS, so it must land — but the manual check covers existing full-width screens.

---

## Phase 1: Shared summary components

### Overview

Extract the two blocks the dashboard needs from the screens that own them today, and rewire those
screens onto the extracted components. No user-visible change; the existing specs are the proof.

### Changes Required:

#### 1. Class summary component

**File**: `src/app/src/app/shared/class-summary/class-summary.ts` (+ `.html`, `.scss`, `.spec.ts`)

**Intent**: Render the "when / class name / instructor" block that `my-classes` shows today, so the
dashboard's upcoming-classes card and the admin's today/upcoming cards can reuse it. This is the
details block only — the cancel button and its per-row failure message stay in `my-classes`, because
the dashboard is read-only.

**Contract**: Inputs are **primitives, not a model**: `name: string`, `startsAt: string` (ISO UTC),
`durationMinutes: number`, `instructor: string`. This is load-bearing — the member card feeds it from
`MyBooking` and the admin cards from `ScheduledClass`, which are different records. The end-time
derivation currently at `my-classes.ts:112-114` moves into this component. Markup and the
`.my-classes-when` / `-name` / `-meta` rules move from `my-classes.html:25-33` and
`my-classes.scss:22-47`, renamed to the new component's prefix.

#### 2. Plan summary component

**File**: `src/app/src/app/shared/plan-summary/plan-summary.ts` (+ `.html`, `.scss`, `.spec.ts`)

**Intent**: Render a training plan's identity line — name, who assigned it, when — shared by the
`my-plan` header and the dashboard's plan card.

**Contract**: Inputs `name: string`, `assignedByDisplayName: string`, `createdAt: string`, plus a
heading-level input so the dashboard can render the name as `h2` while `my-plan` keeps `h1`. Markup
from `my-plan.html:9-14`, styles from `my-plan.scss:3-13`.

#### 3. Rewire the owning screens

**File**: `src/app/src/app/features/my-classes/my-classes.html`, `.ts`, `.scss` and
`src/app/src/app/features/my-plan/my-plan.html`, `.ts`, `.scss`

**Intent**: Replace the inlined blocks with the new components and delete the markup, styles and
helper that moved. Behaviour is unchanged.

**Contract**: `my-classes.ts` loses its `endsAt` helper; both components are added to the respective
`imports` arrays. The existing specs in `my-classes.spec.ts` and `my-plan.spec.ts` must pass
**unmodified except where they assert on markup that moved** — a spec that needed logic changes to
stay green means the extraction changed behaviour.

### Success Criteria:

#### Automated Verification:

- Unit tests pass: `npm test` from `src/app/`
- Formatting and linting pass: `npm run quality:check` from `src/app/`
- Production build succeeds and stays within budget: `npm run build` from `src/app/`
- The two new components each have a spec file

#### Manual Verification:

- `/my-classes` renders identically to before, and cancelling a booking still works
- `/my-plan` renders identically to before, including the "no plan yet" state

**Implementation Note**: After completing this phase and all automated verification passes, pause here
for manual confirmation from the human before proceeding.

---

## Phase 2: The dashboard at `/`

### Overview

Replace the `Home` placeholder with the member dashboard, plus an admin section rendered on the same
route for an admin. Uses existing services and the components from Phase 1.

### Changes Required:

#### 1. Dashboard component replacing Home

**File**: `src/app/src/app/features/dashboard/dashboard.ts` (+ `.html`, `.scss`, `.spec.ts`); delete
`src/app/src/app/features/home/`

**Intent**: The approved member's landing screen. Greets by display name, then renders the member cards
and — for `auth.isAdmin()` — the admin section. Each card links to the screen that owns the full view.

**Contract**: Follows the canonical load pattern exactly (`my-classes.ts:31-77`): per-card
`signal()` state with `loading` / `loadFailed`, `async ngOnInit`, `try/catch/finally`, and the
loading → error → empty → content tri-state in the template in that order. Cards load
**independently**, so one failing card does not blank the others.

Member cards: nearest classes from `BookingService.getMine()`, sliced to the first 3 client-side (the
API already orders by start ascending, so no sorting here); active plan from
`TrainingPlanService.getMine()`, where `null` is the successful "no plan" state, not an error.

Admin section, rendered only when `auth.isAdmin()`: pending count from
`MemberAdminService.getPending()` (`.length`); today's and upcoming classes from
`ClassService.getAdminClasses(from, to)`. **Fetch one window from local midnight forward and bucket
"today" versus "later" client-side** — one round-trip rather than two, and it forces the local-midnight
lower bound that the endpoint's default would otherwise get wrong.

#### 2. Route

**File**: `src/app/src/app/app.routes.ts`

**Intent**: Point `/` at the new component. Guards are unchanged — `[authGuard, activeMemberGuard]`
already admits exactly the accounts that should see a dashboard, and `adminGuard` already redirects
admins to `/`.

**Contract**: `{ path: '', component: Dashboard, canActivate: [authGuard, activeMemberGuard] }`. Stays
**eager**, consistent with what `Home` was; the budget check in Success Criteria is what governs
whether that holds.

### Success Criteria:

#### Automated Verification:

- Unit tests pass: `npm test` from `src/app/`
- Quality gate passes: `npm run quality:check` from `src/app/`
- Build stays inside the 500 kB initial budget with no warning: `npm run build` from `src/app/`
- Specs cover: member sees both cards; admin additionally sees the admin section; a non-admin never
  sees it; the "no plan" state renders as a card and not an alert; one card failing leaves the others
  rendered

#### Manual Verification:

- A member with bookings sees at most three, nearest first, each linking to `/my-classes`
- A member with no bookings and no plan sees empty states that lead somewhere, not errors
- An admin sees the pending count matching `/admin/approvals`, and a class that started **earlier
  today** still appears under "dzisiaj"
- Dashboard renders within about a second on a phone-sized viewport

**Implementation Note**: Pause here for manual confirmation before proceeding.

---

## Phase 3: The "Więcej" screen

### Overview

A hub screen holding everything that is not a primary tab: the account screen, logout, and the panel
links for admins and trainers — including the four admin screens S-10 deferred here. Built before the
bar so the bar never links to a missing route.

### Changes Required:

#### 1. More screen

**File**: `src/app/src/app/features/more/more.ts` (+ `.html`, `.scss`, `.spec.ts`)

**Intent**: Give every destination that does not earn a tab a single, discoverable home, and give
logout a place to live now that it cannot be a tab.

**Contract**: Two sections. Always: a link to `/profile` and the logout `<button>` moved from
`app.html:49` (still a `<button>`, never a link — it mutates server state and must not be prefetchable
or middle-clickable). Conditionally, a panel section whose entries and conditions are:

| Entry | Route | Condition |
| --- | --- | --- |
| Zgłoszenia | `/admin/approvals` | `isAdmin() && isActive()` |
| Członkowie | `/admin/members` | `isAdmin() && isActive()` |
| Zajęcia | `/admin/classes` | `isAdmin() && isActive()` |
| Typy zajęć | `/admin/class-types` | `isAdmin() && isActive()` |
| Ćwiczenia | `/admin/exercises` | `isAdmin() && isActive()` |
| Plany | `/trainer/plans` | `(isTrainer() \|\| isAdmin()) && isActive()` |

Each condition **must match its guard exactly** — `adminGuard` is `isAdmin() && isActive()`,
`trainerGuard` is `(isTrainer() || isAdmin()) && isActive()`. This is the table the S-01 review's F5
finding was about; a mismatch shows a link that bounces the user back to `/`.

The profile link is gated on `isAuthenticated()` only, not `isActive()` — `app.html:43-46` records why:
a Pending member needs `/profile` to supply the contact details S-13 made mandatory.

#### 2. Route

**File**: `src/app/src/app/app.routes.ts`

**Intent**: Register the screen. `authGuard` only — a Pending account must reach it, since it is the
only path to `/profile` once the header's links are gone on mobile.

**Contract**: `{ path: 'more', loadComponent: …, canActivate: [authGuard] }`. **Lazy**, matching the
convention for screens most members open rarely, and keeping it out of the eager bundle the dashboard
now occupies.

### Success Criteria:

#### Automated Verification:

- Unit tests pass: `npm test` from `src/app/`
- Quality gate passes: `npm run quality:check` from `src/app/`
- Build succeeds within budget: `npm run build` from `src/app/`
- Specs assert the full role matrix: member sees profile + logout and no panel; trainer sees Plany
  only; admin sees all six panel entries; an admin whose account is not active sees none of them; a
  Pending account still sees the profile link

#### Manual Verification:

- Logging out from `/more` ends the session and lands on `/login`
- An admin reaches all four previously URL-only screens from here
- A Pending member can open `/more` and reach `/profile`

**Implementation Note**: Pause here for manual confirmation before proceeding.

---

## Phase 4: Bottom bar and shell restructure

### Overview

Move navigation into a fixed bottom bar on phones, slim the header down to the brand there, and lay the
PWA groundwork the bar needs — `viewport-fit=cover`, safe-area padding, and a stacking order that keeps
modals above it.

### Changes Required:

#### 1. Bottom navigation component

**File**: `src/app/src/app/shared/bottom-nav/bottom-nav.ts` (+ `.html`, `.scss`, `.spec.ts`)

**Intent**: The phone-sized primary navigation: five fixed tabs, identical for every role.

**Contract**: Tabs, in order: Start `/`, Grafik `/schedule`, Moje zajęcia `/my-classes`, Mój plan
`/my-plan`, Więcej `/more`. **The set does not vary by role** — that is the point of the design, and it
is what keeps the S-01 visibility-matrix bug class out of the bar entirely.

Rendered inside `@if (auth.isAuthenticated())` in the shell, for the reason `app.html:6` already gives:
before `sessionResolved` flips, every role signal reads `false`.

Markup is a `<nav>` with its own `aria-label`, distinguishing it from the header's unlabelled `<nav>`.
Each tab is an `<a routerLink>` with `routerLinkActive` and **`aria-current="page"` when active** — the
first use of `aria-current` in this codebase, so there is no house pattern to copy. The Start tab needs
`[routerLinkActiveOptions]="{ exact: true }"`, or `/` matches every route as a prefix and every tab
reads as active.

Styling: `position: fixed; bottom: 0`, full width, `padding-bottom: env(safe-area-inset-bottom)`, each
tab at least 44px tall to match the `.button` tap-target rule in `styles.scss:318-339`. Hidden above the
`30rem` breakpoint the codebase already uses in 11 files. `z-index` **below** the three overlays named
in Critical Implementation Details, with a comment saying so.

#### 2. Shell restructure

**File**: `src/app/src/app/app.html`, `app.ts`, `app.scss`

**Intent**: Render the bar, and on phones reduce the header to the brand alone so the same links do not
appear twice.

**Contract**: `<app-bottom-nav />` is added after `<main>`. The header keeps brand, links and logout at
`30rem` and above; below it, `.shell-nav` and `.shell-logout` are hidden by CSS — the header element and
therefore the skip-link target survive. `.shell-main` gains bottom padding on phones equal to the bar's
height plus `env(safe-area-inset-bottom)`, or the last item of every scrolling screen sits under the
bar.

The logout button remains in the header markup for the desktop case; `/more` carries the phone case.
Both call the same `logout()` in `app.ts:22-25`.

#### 3. Viewport meta

**File**: `src/app/src/index.html`

**Intent**: Let `env(safe-area-inset-bottom)` resolve to a real value on notched iOS devices. Without
this the safe-area padding silently computes to `0px` and the bar sits under the home indicator.

**Contract**: `content="width=device-width, initial-scale=1, viewport-fit=cover"` on line 7. This is a
global change affecting inset behaviour on every screen — see Manual Verification.

#### 4. Shell spec rework

**File**: `src/app/src/app/app.spec.ts`

**Intent**: The 11 existing tests assert per-role link visibility in the header. Those assertions do not
disappear — they move to where the links now live.

**Contract**: Header tests keep the desktop expectations. The role-conditional cases
(`shows the approvals link to an admin`, `hides the approvals link from an admin whose account is not
active`, `shows the own-plan link to any active member`, `hides the plans link from a trainer whose
account is not active`, `shows the profile link to a member who is not yet approved`) are retargeted at
the `/more` screen's spec in Phase 3 and replaced here by bar-level assertions: the bar renders for an
authenticated account, does not render for an anonymous visitor, and shows the same five tabs
regardless of role.

### Success Criteria:

#### Automated Verification:

- Unit tests pass: `npm test` from `src/app/`
- Quality gate passes: `npm run quality:check` from `src/app/`
- Build succeeds within the 500 kB initial budget: `npm run build` from `src/app/`
- No component stylesheet exceeds the 6 kB warning budget
- Specs assert: the bar shows five tabs for member, trainer and admin alike; no bar for an anonymous
  visitor; `aria-current="page"` on the active tab; the Start tab is not active while on `/schedule`

#### Manual Verification:

- On a phone viewport the header shows only the brand, and all navigation is in the bar
- At `30rem` and above the header is unchanged and the bar is absent
- Installed to an iOS home screen, the bar sits **above** the home indicator, not under it
- Opening a class-details overlay covers the bar rather than sitting under it
- No screen scrolls its last element under the bar
- `viewport-fit=cover` has not introduced edge-clipping or horizontal scroll on existing screens —
  check `/schedule`, `/admin/members` and a form screen specifically
- The push prompt and the bar do not fight for space on a first load on a narrow phone

**Implementation Note**: This is the final phase. After manual confirmation, the slice is ready for
`/10x-impl-review`.

---

## Testing Strategy

### Unit Tests:

- **Shared components**: `class-summary` renders the time range from `startsAt` + `durationMinutes`;
  `plan-summary` honours the heading-level input.
- **Dashboard**: each card's loading / error / empty / content states independently; the admin section
  appears only for `isAdmin()`; a class that started earlier today is bucketed as "today".
- **More screen**: the full six-row role matrix, plus the Pending-account profile-link case.
- **Bottom nav**: five tabs for every role; hidden for anonymous; `aria-current` on the active tab;
  Start not active while on another route.

### Integration Tests:

None. No backend change, so `tests/po-prostu-silka.Tests/` is untouched — its existing suite is a
regression check only.

### Manual Testing Steps:

1. Sign in as a member on a phone-sized viewport: confirm five tabs, dashboard cards, and that each
   card links to its full screen.
2. Sign in as an admin: confirm the admin section, and that the tab set is unchanged from the member's.
3. Create a class starting an hour ago today, then reload the admin dashboard: it must appear under
   "dzisiaj".
4. Open `/more` as admin and visit all six panel entries.
5. Resize across `30rem` in both directions and confirm exactly one navigation surface is visible at a
   time.
6. Install to an iOS home screen and confirm the bar clears the home indicator.
7. Open a class-details overlay on a phone and confirm it covers the bar.

## Performance Considerations

The dashboard is eager, so its code lands in an initial bundle that has twice been pushed against the
500 kB warning. Two things protect it: the `/more` screen is lazy, and the dashboard must not import
`date-fns`. If `npm run build` warns, the fallback is deferring the admin section into its own chunk
rather than making `/` lazy — a lazy landing route would delay the first paint for every member.

Each dashboard card issues its own request, so the member dashboard is 2 round-trips and the admin 3,
all indexed and `AsNoTracking`. Against the ~1 s perceived-response NFR this is comfortable.

## Migration Notes

None. No schema change, no migration, no data backfill. `features/home/` is deleted and its route
repointed; no URL changes, so no redirects and no bookmarks broken.

## References

- Research: `context/changes/member-and-admin-dashboards/research.md`
- Roadmap item: `context/foundation/roadmap.md` §S-12
- PRD: `context/foundation/prd.md` FR-023, FR-024
- The nav-condition bug class: `context/archive/2026-09-01-registration-and-approval/reviews/impl-review.md:125-133`
- The deferred admin nav entry: `context/archive/2026-09-04-exercise-library/research.md:225-233`
- Canonical load pattern: `src/app/src/app/features/my-classes/my-classes.ts:31-77`
- Guarded `matchMedia` pattern: `src/app/src/app/shared/calendar/schedule-calendar.ts:342-362`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Shared summary components

#### Automated

- [x] 1.1 Unit tests pass: `npm test` from `src/app/` — a361ead
- [x] 1.2 Formatting and linting pass: `npm run quality:check` from `src/app/` — a361ead
- [x] 1.3 Production build succeeds and stays within budget: `npm run build` from `src/app/` — a361ead
- [x] 1.4 The two new components each have a spec file — a361ead

#### Manual

- [ ] 1.5 `/my-classes` renders identically to before, and cancelling a booking still works
- [ ] 1.6 `/my-plan` renders identically to before, including the "no plan yet" state

### Phase 2: The dashboard at `/`

#### Automated

- [x] 2.1 Unit tests pass: `npm test` from `src/app/` — 79a383f
- [x] 2.2 Quality gate passes: `npm run quality:check` from `src/app/` — 79a383f
- [x] 2.3 Build stays inside the 500 kB initial budget with no warning: `npm run build` from `src/app/` — 79a383f
- [x] 2.4 Specs cover member cards, admin section gating, the "no plan" card, and independent card failure — 79a383f

#### Manual

- [ ] 2.5 A member with bookings sees at most three, nearest first, each linking to `/my-classes`
- [ ] 2.6 A member with no bookings and no plan sees empty states that lead somewhere, not errors
- [ ] 2.7 An admin sees the correct pending count, and a class that started earlier today appears under "dzisiaj"
- [ ] 2.8 Dashboard renders within about a second on a phone-sized viewport

### Phase 3: The "Więcej" screen

#### Automated

- [x] 3.1 Unit tests pass: `npm test` from `src/app/`
- [x] 3.2 Quality gate passes: `npm run quality:check` from `src/app/`
- [x] 3.3 Build succeeds within budget: `npm run build` from `src/app/`
- [x] 3.4 Specs assert the full six-row role matrix plus the Pending profile-link case

#### Manual

- [ ] 3.5 Logging out from `/more` ends the session and lands on `/login`
- [ ] 3.6 An admin reaches all four previously URL-only screens from here
- [ ] 3.7 A Pending member can open `/more` and reach `/profile`

### Phase 4: Bottom bar and shell restructure

#### Automated

- [ ] 4.1 Unit tests pass: `npm test` from `src/app/`
- [ ] 4.2 Quality gate passes: `npm run quality:check` from `src/app/`
- [ ] 4.3 Build succeeds within the 500 kB initial budget: `npm run build` from `src/app/`
- [ ] 4.4 No component stylesheet exceeds the 6 kB warning budget
- [ ] 4.5 Specs assert five tabs for every role, no bar for anonymous, `aria-current` on the active tab, and Start inactive while on `/schedule`

#### Manual

- [ ] 4.6 On a phone viewport the header shows only the brand, and all navigation is in the bar
- [ ] 4.7 At `30rem` and above the header is unchanged and the bar is absent
- [ ] 4.8 Installed to an iOS home screen, the bar sits above the home indicator
- [ ] 4.9 Opening a class-details overlay covers the bar rather than sitting under it
- [ ] 4.10 No screen scrolls its last element under the bar
- [ ] 4.11 `viewport-fit=cover` has not introduced edge-clipping or horizontal scroll on existing screens
- [ ] 4.12 The push prompt and the bar do not fight for space on a first load on a narrow phone
