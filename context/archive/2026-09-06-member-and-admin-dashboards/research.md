---
date: 2026-09-06T21:25:22+02:00
researcher: Karol Rumianowski
git_commit: 88f8c2e7e78ff45957d7d544b7581341c7ed4b21
branch: main
repository: po-prostu-silka
topic: "S-12 member and admin dashboards, with a global mobile bottom navigation bar"
tags: [research, codebase, dashboard, navigation, angular-shell, pwa, design-system]
status: complete
last_updated: 2026-09-06
last_updated_by: Karol Rumianowski
---

# Research: S-12 member and admin dashboards, with a global mobile bottom navigation bar

**Date**: 2026-09-06T21:25:22+02:00
**Researcher**: Karol Rumianowski
**Git Commit**: 88f8c2e7e78ff45957d7d544b7581341c7ed4b21
**Branch**: main
**Repository**: po-prostu-silka (github.com/rumek/po-prostu-silka)

## Research Question

What does the codebase already provide for S-12 — the member dashboard (nearest upcoming classes,
active training plan) and the admin dashboard (pending approvals, today's and upcoming classes) — and
what does it provide for turning the shell's top navigation into a global bottom bar on mobile?

Scope agreed before research: the bottom bar is a **global navigation shell** change, not a
dashboard-local widget; the backend was audited in full for whether an aggregated endpoint is needed;
UI/design-system patterns and the PWA/installability surface were included.

## Summary

**This is a front-end-only slice.** All four dashboard cards are already served by existing,
already-authorised, already-bounded endpoints. No new endpoint, no new query parameter, no migration.

1. **No backend work is required, and an aggregated `/api/dashboard` is not justified.** The member
   dashboard costs 2 round-trips (`GET /api/bookings/mine`, `GET /api/plans/mine`); the admin
   dashboard costs 2–3 (`GET /api/admin/members/pending`, plus one or two windowed calls to
   `GET /api/admin/classes`). Every query is `AsNoTracking`, indexed, and bounded. An aggregator would
   save round-trips, not database cost, and would fork a second projection of four DTOs the SPA
   already mirrors.
2. **The one backend subtlety is the "today" window.** `GET /api/admin/classes` supports explicit
   `from`/`to`, but its *no-parameter default* starts at `now`, not at local midnight. An admin card
   labelled "dzisiaj" must therefore pass explicit bounds, or it silently drops classes that already
   started today.
3. **The client patterns to copy are uniform and already settled.** Every screen uses plain
   `signal()` + `async/await` over `Promise`-returning services, with a `loading` / `loadFailed` /
   empty / content tri-state in the template. No `resource()`, no `httpResource()`, no `AsyncPipe`
   anywhere. The dashboard must not introduce a fifth pattern.
4. **Three of the four cards already exist as inline template blocks** on `my-classes`, `my-plan` and
   `approvals`. None is extracted into a shared component yet — that extraction is the real design
   decision of this slice.
5. **The bottom bar is greenfield.** The codebase has zero `env(safe-area-inset-*)`, no
   `viewport-fit=cover`, no `position: fixed` navigation (only three modal overlays), and no
   `aria-current` anywhere. The manifest does declare `display: standalone`, which is exactly what
   makes the iOS home-indicator safe-area matter.
6. **The load-bearing open question is not technical — it is how many destinations the bar holds.**
   An active admin sees 6 nav destinations today; S-10 explicitly deferred 4 more admin links to this
   slice, which would make 10. A tab bar comfortably holds 3–5. See "Open Questions".
7. **SSR is scaffolded but dormant** (`outputMode: "static"`, no `server`/`ssr` key in
   `angular.json`), which relaxes — but does not remove — the constraint on viewport-measuring code.

## Detailed Findings

### 1. Backend — every card is already served

| Card | Endpoint | Policy | Backend work |
| --- | --- | --- | --- |
| Member: nearest upcoming classes | `GET /api/bookings/mine` | `ActiveMember` | **none** — already returns the caller's active bookings on scheduled classes, ordered by class start ascending |
| Member: active training plan | `GET /api/plans/mine` | `ActiveMember` | **none** — returns the single active plan, or **204** when there is none |
| Admin: pending approvals | `GET /api/admin/members/pending` | `Admin` | **none** — full queue, oldest-first; list length is the count |
| Admin: today's classes | `GET /api/admin/classes?from=&to=` | `Admin` | **none** — but the caller must pass explicit local-midnight bounds (see below) |
| Admin: upcoming classes | `GET /api/admin/classes` | `Admin` | **none** — no-parameter default is `[now, now+62d)` |

- `GET /api/bookings/mine` — `src/Application/Scheduling/BookingEndpoints.cs:352-367`, group and policy at
  `:140-142`. Response `IReadOnlyList<MyBooking>`, record at `:25-33`:
  `(Guid BookingId, Guid ClassId, string Name, string? Description, DateTimeOffset StartsAt, int DurationMinutes, string Instructor, DateTimeOffset BookedAt)`.
  Ordering is guaranteed server-side by `OrderBy(b => b.Class.StartsAt)` in
  `src/Infrastructure/Scheduling/BookingQuery.cs:34-50`, so a "nearest N" card is a client-side
  `slice(0, N)` and nothing more.
- `GET /api/plans/mine` — `src/Application/Training/MyPlanEndpoints.cs:53-68`, group `:33-35`.
  **Returns 204, not an empty body or a 404**, when the member has no active plan; the SPA already
  coalesces that to `null` in `src/app/src/app/core/training/training-plan.service.ts:82-86`. The
  dashboard's "no plan yet" state is therefore a successful load, not an error — matching how
  `my-plan.html:48-57` already renders it as a plain card rather than an alert.
- `GET /api/admin/members/pending` — `src/Application/Members/MemberAdminEndpoints.cs:119-122`, group
  `:97-99`. Response `IReadOnlyList<PendingMember>` (`:13`). No paging, deliberately — the query at
  `src/Infrastructure/Members/PendingMemberQuery.cs:18-29` carries a documented "a single gym's
  pending queue is small" justification. It is the only dashboard query with no explicit ceiling of
  any kind; worth a note in the plan, not a blocker.
- `GET /api/admin/classes` — `src/Application/Scheduling/ClassEndpoints.cs:288-305`, group `:220-222`.
  `from`/`to` are both-or-neither (`ResolveRange`, `:326-355`), the window is hard-capped at 62 days
  (`MaxRangeDays`, `:189`), and **the no-parameter default lower bound is `now`** (`:295-297`).
  Response `ScheduledClass` at `:48-59`, including `Capacity` and `FreeSpots`.

**The "today" trap.** Because the default window opens at `now`, a naive parameterless call for an
admin "dzisiaj" card omits every class that started earlier today — precisely the classes an admin
running the day cares about. The endpoint needs no change; the *client* must compute local midnight
and pass `from`/`to` explicitly. This is the single most likely correctness bug in the slice.

**Query cost.** `ClassScheduleQuery.ProjectAsync`
(`src/Infrastructure/Scheduling/ClassScheduleQuery.cs:39-79`) runs a correlated
`db.Bookings.Count(...)` subquery per row for `BookedCount`, seeking `IX_Bookings_Class_Member_Active`,
with the window capped at 62 days. For a single club that is "a few dozen rows" per the code's own
comment at `ClassEndpoints.cs:168-175`, which cites the ~1 s perceived-response NFR directly. All
dashboard queries are single-round-trip, indexed and `AsNoTracking`.

**Evidence against a new endpoint.** 2 round-trips (member) and 2–3 (admin), all cheap; no
cross-entity query that the existing endpoints cannot express; and four response records
(`MyBooking`, `TrainingPlanDetail`, `PendingMember`, `ScheduledClass`) that are documented in-file as
"a CONTRACT the SPA mirrors" and would have to be duplicated. An aggregator becomes justified only if
round-trip *latency* is measured as a problem, or a future card needs a genuinely new query. Neither
is true today. The admin's two class calls can also collapse to one wide window bucketed client-side,
bringing both dashboards to 2 round-trips.

If a new endpoint is ever built, the house conventions are: a static `Map<Area>Endpoints` extension
under `src/Application/<Area>/`, registered in `src/Program.cs:332-342`; `RequireAuthorization` applied
**at the group, never per route**; policy names from `src/Domain/AuthorizationPolicyNames.cs:17-38`;
reads behind a narrow `IXQuery` interface implemented in `Infrastructure` so `Application` stays
EF-Core-free.

### 2. Client data layer — the pattern to copy verbatim

All four services return `Promise<T>` via `firstValueFrom` and do not catch:
`BookingService.getMine()` (`core/scheduling/booking.service.ts:49-51`),
`TrainingPlanService.getMine()` (`core/training/training-plan.service.ts:82-86`),
`MemberAdminService.getPending()` (`core/admin/member-admin.service.ts:21-23`),
`ClassService.getAdminClasses(from?, to?)` (`core/scheduling/class.service.ts:50-54`).

The canonical component shape — identical skeleton in `my-classes.ts:31-77`, `my-plan.ts:32-55`,
`approvals.ts:24-50`, `schedule.ts:40-115`:

```ts
protected readonly rows = signal<T[]>([]);
protected readonly loading = signal(true);
protected readonly loadFailed = signal(false);

async ngOnInit(): Promise<void> { await this.load(); }

protected async load(): Promise<void> {
  this.loading.set(true);
  this.loadFailed.set(false);
  try { this.rows.set(await this.service.getX()); }
  catch { this.loadFailed.set(true); }
  finally { this.loading.set(false); }
}
```

Template tri-state, in this exact order, every screen (`my-classes.html:7-21`, `my-plan.html:1-8`,
`approvals.html:8-21`): `@if (loading())` → `.notice role="status"`; `@else if (loadFailed())` →
`.alert role="alert"` with a "Spróbuj ponownie" button calling `load()`; `@else if (empty)` →
`.notice`; `@else` → content.

Two refinements a dashboard should inherit:

- **Generation fence.** `schedule.ts:75-114` and `my-classes.ts:46-76` keep a `private generation = 0`,
  increment per load and re-check after each `await`, guarding against out-of-order responses. A
  dashboard firing 2–3 parallel loads should decide per card whether it needs this; a card that never
  reloads on navigation does not.
- **Failure→Polish message tables.** `core/scheduling/booking-failure.ts:19-48` and `class-failure.ts:28-73`
  export an exhaustively-keyed `Record<Reason, string>` (so the build breaks when a reason is added)
  plus a lookup using `Object.hasOwn` with an `UNKNOWN` fallback. Dashboard cards are read-only, so
  they likely need only the generic "nie udało się wczytać" copy already used elsewhere.

**Dates.** `core/scheduling/local-datetime.ts` is for `<input type="datetime-local">` round-tripping
only — not display. Screens format with Angular's `DatePipe` in the template
(`{{ row.startsAt | date: 'EEEE, d MMMM' }}`, `my-classes.html:27`). The one existing end-time
derivation is `my-classes.ts:112-114`
(`new Date(new Date(startsAt).getTime() + durationMinutes * 60_000)`), reusable verbatim.
`shared/calendar/polish-date-formatter.ts` is scoped to `angular-calendar` and registered only on
`ScheduleCalendar`'s own providers — **a dashboard card must not import it**, or it drags the calendar
library into the initial bundle. There is no relative-time ("za 2 godziny") helper anywhere; if the
plan wants one, it is new code.

### 3. Cards that already exist as inline blocks

- **Upcoming classes list** — `my-classes.html:22-51`, fed by `BookingService.getMine()`. A "nearest N"
  card is the same markup over a shorter array.
- **Active plan summary** — `my-plan.html:9-14` (name, `assignedByDisplayName`, `createdAt`), plus the
  "no plan yet" card at `:48-57` whose copy the dashboard needs too.
- **Pending approvals count** — `approvals.ts:39-50`; `pending().length` is literally the number the
  admin card wants.

None is extracted. If the dashboard renders more than one of them, extracting a small presentational
component per card (state-fetching staying in the existing services) is the clean move; duplicating
the markup is the drift risk.

### 4. Auth, guards, and what each account sees at `/`

`AuthService` (`core/auth/auth.service.ts`) exposes `user` (`:27-30`), `isAuthenticated` (`:32`),
`isActive` (`:35`), `isAdmin` (`:37`), `isTrainer` (`:44`, additive only — every gate in the codebase
writes `(isTrainer() || isAdmin()) && isActive()`), and **`sessionResolved` (`:50-51`)**.

`sessionResolved` is the trap for both the dashboard and the bottom bar: before it flips true, `user`
is `null`, so `isAdmin()` / `isTrainer()` / `isActive()` all read `false` **regardless of the actual
role**. Guards await `loadCurrentUser()` first, so a component reached through routing can trust the
signals. The shell — which renders before any guard runs — cannot, which is exactly why `app.html:6`
wraps the whole nav in `@if (auth.isAuthenticated())`. A bottom bar lives in the same position and
inherits the same requirement.

Guards, and their redirects:

| Guard | Checks | Redirects to |
| --- | --- | --- |
| `authGuard` (`auth.guard.ts:14-38`) | `isAuthenticated()` | `/login` |
| `activeMemberGuard` (`active-member.guard.ts:15-42`) | `isActive()` | `/pending`, or `/login` if not authenticated |
| `adminGuard` (`admin.guard.ts:14-36`) | `isAdmin() && isActive()` | `/` |
| `trainerGuard` (`trainer.guard.ts:15-37`) | `(isTrainer() \|\| isAdmin()) && isActive()` | `/` |

`/` carries `[authGuard, activeMemberGuard]` (`app.routes.ts:45`). So a Pending account never reaches
the dashboard (→ `/pending`), and **admin vs member is not distinguished by the route at all** — the
role branch for "member dashboard vs admin dashboard" must happen *inside* the component via
`auth.isAdmin()`, exactly as `app.html` already branches its links. All four guards short-circuit
`true` on the server via `isPlatformServer`.

### 5. The shell today, and the bottom-bar gap

`app.html` is the entire navigation system — there is no layout or shell component beyond it and
`app.ts`. Current link inventory:

| Line | Route | Label | Condition |
| --- | --- | --- | --- |
| 4 | `/` | Po Prostu Siłka (brand) | always |
| 24 | `/schedule` | Grafik | `isActive()` |
| 25 | `/my-classes` | Moje zajęcia | `isActive()` |
| 29 | `/my-plan` | Mój plan | `isActive()` |
| 33 | `/admin/approvals` | Zgłoszenia | `isAdmin() && isActive()` |
| 40 | `/trainer/plans` | Plany | `(isTrainer() \|\| isAdmin()) && isActive()` |
| 47 | `/profile` | Moje konto | `isAuthenticated()` only |
| 49 | — | Wyloguj się (button) | `isAuthenticated()` |

Notes that constrain the redesign:

- **`/` has no nav entry of its own** — only the brand links home. A tab bar almost certainly wants an
  explicit "Start" / home tab, which is a new destination, not a moved one.
- **`/profile` is deliberately wider than its neighbours** (`isAuthenticated()`, not `isActive()`) —
  `app.html:43-46` explains a Pending member needs that screen to supply S-13's contact details. But
  the enclosing `@if (auth.isAuthenticated())` is the only gate, and a Pending member is redirected
  away from `/` — so on mobile the bar must still render something for a Pending account, or the one
  screen they need becomes unreachable. This is a real edge case, not a theoretical one.
- **Every visibility condition MUST match its guard exactly.** This is not style: the S-01 impl review
  (`context/archive/2026-09-01-registration-and-approval/reviews/impl-review.md:125-133`, finding F5)
  caught a shipped bug where the admin link tested `isAdmin()` alone, so a pending admin saw a link
  that bounced them `/admin/approvals → / → /pending`. The fix is the origin of the long comment now
  at `app.html:8-15`. Re-homing these links into a bar is exactly the operation that re-opens that
  bug class.
- **Logout is a `<button>`, not a link**, on purpose (`app.scss:55-56`): it mutates server state and
  must not be prefetchable or middle-clickable. It cannot become a plain tab.

Shell CSS: `:host` is already `display:flex; flex-direction:column; min-height:100dvh`
(`app.scss:4-8`) — a fixed bottom bar drops in without restructuring the layout. `.shell-main` is
`max-width:64rem` with `padding: var(--space-7) var(--space-5)`, narrowed at
`@media (max-width: 30rem)` (`app.scss:74-88`).

### 6. Design system vocabulary

`src/app/src/styles.scss` is the only global stylesheet. Tokens (`:root`, `:81-114`): `--ground`
`#f7f3ef`, `--section-warm` `#ece3dc`, `--section-cool` `#edeae5`, `--ink` `#272321`, `--accent`
`#654b45`, `--line` / `--line-strong` / `--muted` as ink mixes, `--danger` `#8c2f22`; fonts
`--font-display` (Cormorant Garamond, self-hosted) and `--font-body` (Plus Jakarta Sans); radii
`--radius` 10px / `--radius-sm` 6px; spacing `--space-1` 0.25rem → `--space-7` 3rem; `--shadow-card`.

Shared classes a dashboard should reuse rather than restyle: `.card` (`:194-200`), `.badge`
(`:309-316`), `.button` with `min-height:44px` for iOS tap targets (`:318-339`), `.button--block`
(`:352-354`), `.alert` (`:358-366`), `.notice` (`:368-375`), `.hint` (`:296-299`). One global focus
ring for the whole app: `:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px }`
(`:184-187`).

**Breakpoints — the codebase is desktop-first.** `@media (max-width: 30rem)` (480px) appears in 11
files and is the established phone threshold. The only `min-width` CSS breakpoint is a one-off at
`exercise-form.scss:34` (`40rem`). Separately, `shared/calendar/calendar-breakpoint.ts:14,17` exports
`WEEK_VIEW_MIN_WIDTH = '48rem'` and `WEEK_VIEW_MEDIA_QUERY = '(min-width: 48rem)'` as the single
source of truth for the calendar's day/week switch — a **TypeScript** breakpoint consumed via
`matchMedia`, not a CSS one, and deliberately not mirrored as a custom property because a CSS custom
property cannot legally appear in a media condition. A bottom bar shown only on phones should reuse
`30rem` in CSS; if it needs a TS-side decision, `schedule-calendar.ts:342-362` is the pattern:

```ts
if (isPlatformBrowser(this.platformId) && typeof window.matchMedia === 'function') { … }
```

double-guarded because jsdom counts as a browser platform but has no `matchMedia`, with cleanup via
`destroyRef.onDestroy`.

Accessibility conventions in place: `routerLinkActive="is-active"` (only in `app.html`, never with
`[routerLinkActiveOptions]`); `role="dialog" aria-modal="true" aria-labelledby` on overlays;
`role="group" aria-label` on control clusters; `[attr.aria-invalid]` on every form control.
**`aria-current` is used nowhere in the codebase** — a tab bar's `aria-current="page"` is a first, and
the existing `<nav class="shell-nav">` has no `aria-label`, so two navs will need distinguishing
labels.

### 7. PWA, safe area, and the fixed-position gap

- **Manifest** (`src/app/public/manifest.webmanifest:1-21`): `display: "standalone"`,
  `orientation: "portrait"`, `start_url` / `scope` `"/"`, `theme_color` `#1f2937`, three icons
  including a maskable 512. `standalone` is what makes a bottom bar read as native — and what makes
  the iOS home indicator overlap the bar without safe-area padding.
- **Viewport meta** (`src/app/src/index.html:7`), verbatim:
  `<meta name="viewport" content="width=device-width, initial-scale=1" />` — **no `viewport-fit=cover`**.
  Without it `env(safe-area-inset-bottom)` resolves to `0px` on iOS and the padding silently does
  nothing.
- **Zero safe-area groundwork.** No `env(safe-area-inset-*)` anywhere in `src/app/src`. No `100vh`
  (good); `100dvh` is used twice, at `app.scss:7` and `styles.scss:134`. `position: fixed` appears
  three times, all modal overlays (`class-bookings-overlay.scss:7`, `class-create-overlay.scss:4`,
  `class-details-overlay/class-details-overlay.scss:7`) — none is navigation, so there is no z-index
  scale to slot a bar into, and the bar-vs-overlay stacking order is a decision the plan must make.
- **Service worker carries push only.** `angular.json:38` has `"serviceWorker": "ngsw-config.json"`
  (the bare-string form — the syntax `lessons.md:13-14` records as the correction to F-03's plan text,
  confirmed in the file today), registered at `app.config.ts:37` as
  `provideServiceWorker('ngsw-worker.js', { enabled: !isDevMode() })`. `ngsw-config.json:1-6` declares
  **empty** `assetGroups` and `dataGroups` — no app-shell caching, no API caching, deliberately,
  because the PRD locks "no offline-first guarantee" and caching a live schedule seeds stale-data
  bugs. **The bottom bar therefore gets no offline benefit and must not assume one.**
- **`<app-push-prompt>`** (`app.html:60`, inside `.shell-main`, above the outlet, gated on
  `isActive()`) is in normal flow — `push-prompt.scss:1-36` uses no `position: fixed` and no z-index —
  so a fixed bar will not overlap it. Nothing coordinates the two visually on a first mobile load.
- **SSR is dormant.** `app.config.server.ts`, `app.routes.server.ts` (`{ path: '**', renderMode: Prerender }`),
  `main.server.ts` and `server.ts` all exist, but `angular.json` has `outputMode: "static"` and no
  `server`/`ssr` key — verified directly. `schedule-calendar.ts:343-346` says as much: "nothing renders
  on a server today … one angular.json key away from being live." So SSR is a latent constraint, not a
  live one; new viewport code should still follow the `isPlatformBrowser` pattern rather than assume
  CSR is permanent.

### 8. Bundle budget

`angular.json:42-53` (production): initial `maximumWarning: "500kB"`, `maximumError: "1MB"`;
`anyComponentStyle` warning 6kB / error 8kB. **`/` is one of only four eager routes** (`login`,
`register`, `pending`, `''` — `app.routes.ts:45`); every later screen was made lazy specifically to
stay under budget, with the history recorded in the route comments: 424 kB (S-07), then 475 kB →
502.88 kB when the exercise screens were eager (S-10), which is what pushed them lazy. A dashboard
replacing `Home` inherits eager status and lands directly in that bundle. The plan should state
whether the dashboard stays eager and re-check `npm run build` against the budget.

## Code References

| Reference | What is there |
| --- | --- |
| [`src/Application/Scheduling/BookingEndpoints.cs:352-367`](https://github.com/rumek/po-prostu-silka/blob/88f8c2e7e78ff45957d7d544b7581341c7ed4b21/src/Application/Scheduling/BookingEndpoints.cs#L352-L367) | `GET /api/bookings/mine` — the member's upcoming-classes card, as-is |
| [`src/Infrastructure/Scheduling/BookingQuery.cs:34-50`](https://github.com/rumek/po-prostu-silka/blob/88f8c2e7e78ff45957d7d544b7581341c7ed4b21/src/Infrastructure/Scheduling/BookingQuery.cs#L34-L50) | Server-side `OrderBy(StartsAt)` — why "nearest N" is a client-side slice |
| [`src/Application/Training/MyPlanEndpoints.cs:53-68`](https://github.com/rumek/po-prostu-silka/blob/88f8c2e7e78ff45957d7d544b7581341c7ed4b21/src/Application/Training/MyPlanEndpoints.cs#L53-L68) | `GET /api/plans/mine` — 204 when there is no active plan |
| [`src/Application/Scheduling/ClassEndpoints.cs:288-305`](https://github.com/rumek/po-prostu-silka/blob/88f8c2e7e78ff45957d7d544b7581341c7ed4b21/src/Application/Scheduling/ClassEndpoints.cs#L288-L305) | `GET /api/admin/classes` — default window opens at `now`, not local midnight |
| [`src/Application/Members/MemberAdminEndpoints.cs:119-122`](https://github.com/rumek/po-prostu-silka/blob/88f8c2e7e78ff45957d7d544b7581341c7ed4b21/src/Application/Members/MemberAdminEndpoints.cs#L119-L122) | `GET /api/admin/members/pending` — unpaged by design |
| [`src/app/src/app/app.html`](https://github.com/rumek/po-prostu-silka/blob/88f8c2e7e78ff45957d7d544b7581341c7ed4b21/src/app/src/app/app.html) | The entire navigation system, with the guard-matching comments |
| [`src/app/src/app/app.scss:4-8`](https://github.com/rumek/po-prostu-silka/blob/88f8c2e7e78ff45957d7d544b7581341c7ed4b21/src/app/src/app/app.scss#L4-L8) | Shell is already a `100dvh` flex column — a fixed bar drops in cleanly |
| [`src/app/src/app/features/home/home.ts`](https://github.com/rumek/po-prostu-silka/blob/88f8c2e7e78ff45957d7d544b7581341c7ed4b21/src/app/src/app/features/home/home.ts) | The placeholder this slice replaces; its comment names S-12 explicitly |
| [`src/app/src/app/features/my-classes/my-classes.ts:31-77`](https://github.com/rumek/po-prostu-silka/blob/88f8c2e7e78ff45957d7d544b7581341c7ed4b21/src/app/src/app/features/my-classes/my-classes.ts#L31-L77) | Canonical signal + async/await + tri-state load pattern, with generation fence |
| [`src/app/src/app/core/auth/auth.service.ts:50-51`](https://github.com/rumek/po-prostu-silka/blob/88f8c2e7e78ff45957d7d544b7581341c7ed4b21/src/app/src/app/core/auth/auth.service.ts#L50-L51) | `sessionResolved` — role signals read `false` before it flips |
| [`src/app/src/app/shared/calendar/calendar-breakpoint.ts`](https://github.com/rumek/po-prostu-silka/blob/88f8c2e7e78ff45957d7d544b7581341c7ed4b21/src/app/src/app/shared/calendar/calendar-breakpoint.ts) | The only TS-side breakpoint constant, and why it is not a CSS variable |
| [`src/app/src/app/shared/calendar/schedule-calendar.ts:342-362`](https://github.com/rumek/po-prostu-silka/blob/88f8c2e7e78ff45957d7d544b7581341c7ed4b21/src/app/src/app/shared/calendar/schedule-calendar.ts#L342-L362) | The double-guarded `matchMedia` pattern to copy |
| [`src/app/src/styles.scss`](https://github.com/rumek/po-prostu-silka/blob/88f8c2e7e78ff45957d7d544b7581341c7ed4b21/src/app/src/styles.scss) | All design tokens and shared classes; `.button` min-height 44px |
| [`src/app/src/index.html#L7`](https://github.com/rumek/po-prostu-silka/blob/88f8c2e7e78ff45957d7d544b7581341c7ed4b21/src/app/src/index.html#L7) | Viewport meta without `viewport-fit=cover` |
| [`src/app/public/manifest.webmanifest`](https://github.com/rumek/po-prostu-silka/blob/88f8c2e7e78ff45957d7d544b7581341c7ed4b21/src/app/public/manifest.webmanifest) | `display: standalone` — why safe-area matters |
| [`src/app/ngsw-config.json`](https://github.com/rumek/po-prostu-silka/blob/88f8c2e7e78ff45957d7d544b7581341c7ed4b21/src/app/ngsw-config.json) | Empty asset/data groups — push only, no caching |
| [`src/app/angular.json#L42-L53`](https://github.com/rumek/po-prostu-silka/blob/88f8c2e7e78ff45957d7d544b7581341c7ed4b21/src/app/angular.json#L42-L53) | 500 kB initial-bundle warning budget |
| [`src/app/src/app/app.routes.ts#L45`](https://github.com/rumek/po-prostu-silka/blob/88f8c2e7e78ff45957d7d544b7581341c7ed4b21/src/app/src/app/app.routes.ts#L45) | `/` is eager — the dashboard inherits that |

## Architecture Insights

- **The layering rule is not at risk in this slice.** Nothing here touches `Domain`, `Application` or
  EF Core. That is itself the finding: a slice with no backend work cannot violate the layering
  convention `AGENTS.md` warns rots silently.
- **Read contracts are duplicated on purpose.** Endpoint DTOs are `record`s declared above their
  endpoint class and documented as "a CONTRACT the SPA mirrors"; integration tests re-declare them a
  third time as private records so the wire shape is asserted rather than assumed. Adding an
  aggregated endpoint means adding a fourth and fifth copy of shapes that already exist — the
  strongest structural argument against it.
- **Navigation conditions are duplicated logic held in sync by comment.** Guard, template condition and
  backend policy each restate the same rule in three places, kept aligned only by convention and by
  the S-01 review that caught them drifting. Moving links into a bar duplicates them a fourth time
  unless the plan derives the destination list from one place.
- **Mobile-first is the stated intent; desktop-first is the implemented CSS.** Eleven `max-width: 30rem`
  overrides against one `min-width` outlier. The bottom bar sits at the intersection of that
  contradiction, and the plan should pick a side explicitly rather than adding a twelfth override.
- **The service worker's emptiness is a deliberate decision, not an oversight** — the PRD's "no
  offline-first guarantee" non-goal, restated in code.

## Historical Context (from prior changes)

- `context/archive/2026-09-01-registration-and-approval/plan.md:507-512` — `/` → `Home` established as
  a deliberate placeholder from the first slice.
- `context/archive/2026-09-01-registration-and-approval/reviews/impl-review.md:125-133` (F5) — the
  shipped bug where the admin nav condition disagreed with `adminGuard`. Origin of the
  "condition MUST match the guard" comments in `app.html`. **The most directly applicable prior
  finding for this slice.**
- `context/archive/2026-09-03-class-booking-and-cancel/plan.md:618-628` — the only prior change that
  added nav entries (`Grafik`, `Moje zajęcia`), gated on `isActive()`; and `:105` / `plan-brief.md:56-57`,
  the explicit non-goal "**No dashboard work.** `Home` keeps its placeholder; the nearest-classes card
  is S-12."
- `context/archive/2026-09-04-training-plans/plan.md:99` — "**No dashboard cards.** The member's plan
  card on Home is S-12."
- `context/archive/2026-09-04-exercise-library/research.md:225-233` and `plan-brief.md:36` — a top-nav
  entry for the exercise library was **raised as a decision and deliberately dropped**, deferred to
  this slice: "When the entry is eventually added (S-12 or a dedicated nav change), its condition must
  be `isAdmin() && isActive()`." This is the strongest prior statement of what S-12 owes navigation:
  four admin screens (`/admin/members`, `/admin/classes`, `/admin/class-types`, `/admin/exercises`)
  are reachable only by typing a URL today.
- `context/archive/2026-09-02-schedule-calendar-view/plan.md:160` — the guarded `matchMedia` mobile-first
  default, the closest precedent for viewport-dependent behaviour.
- `context/foundation/prd.md:133,164,179` — "mobile-first and installable … behaving like an app", "no
  offline-first guarantee", and "no native mobile apps — the installable web app is the mobile
  experience". The last raises the stakes on the bar feeling native.
- **A bottom bar has never been proposed or rejected before.** An exhaustive search of
  `context/archive/`, `context/changes/` and `context/foundation/` for "bottom" returns only unrelated
  code-placement idioms and this change's own title. The top-nav-only shell has simply never been
  challenged; this is the first time.

## Related Research

- `context/archive/2026-09-04-exercise-library/research.md` — the deferred admin-nav decision.
- `context/archive/2026-09-02-schedule-calendar-view/research.md` — responsive/calendar breakpoint work.
- `context/foundation/lessons.md` — both entries applied here: the `serviceWorker` syntax was verified
  against `angular.json`, not against F-03's plan text; and every prerequisite claim above cites code
  in `src/`, never an archived plan.

## Open Questions

1. **How many destinations does the bar hold, and what happens to the rest?** An active member sees 4
   destinations (Grafik, Moje zajęcia, Mój plan, Moje konto), a trainer 5, an admin 6 — and S-10
   deferred 4 more admin links to this slice, which would make **10**. A tab bar comfortably holds
   3–5. The plan must choose: a fixed member-shaped bar with admin screens reached elsewhere, a
   role-dependent bar with different tab counts, or a bar plus an overflow. This is the slice's
   central design decision and the one most likely to make it stop being "minimal".
2. **Does `/` get its own tab?** It has no nav entry today. A "Start" tab is a new destination, and
   without it the dashboard is reachable only via the brand.
3. **What does a Pending account see?** `/profile` is deliberately visible to merely-authenticated
   accounts, but a Pending member is redirected off `/` to `/pending`. Does the bar render on
   `/pending`, and with what?
4. **Where does logout go?** It is a `<button>` on purpose and cannot become a tab. Into "Moje konto",
   or does the top header survive on mobile carrying it?
5. **Top header on mobile: keep, shrink, or drop?** If both survive, the brand and the bar duplicate
   navigation; if the header goes, the skip-link target and the logout button need new homes.
6. **Does the admin dashboard live at `/` or at its own route?** The guards do not distinguish admin at
   `/`, so a single component branching on `auth.isAdmin()` is the path of least resistance — but it
   puts both dashboards in the eager initial bundle.
7. **Bar vs. overlay stacking.** Three modal overlays use `position: fixed` with no shared z-index
   scale. Does the bar sit above or below an open class-details overlay on a phone?
8. **`viewport-fit=cover` is a global change.** Adding it to `index.html` affects every screen, not just
   the bar — worth an explicit check that no existing full-width layout breaks.
