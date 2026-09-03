# Schedule Calendar View — Plan Brief

> Full plan: `context/changes/schedule-calendar-view/plan.md`

## What & Why

The schedule is a day-grouped list with no week navigation and no way to reach a chosen date. This
change turns it into a calendar — day view on a phone, week view from the tablet breakpoint up —
shared by the member schedule and the admin class panel so the two cannot drift apart. It delivers
`prd-v2` US-02 and FR-015 – FR-018 (roadmap S-07), and adds one thing they do not ask for: the admin
creates a class by dragging a time range.

## Starting Point

S-06 settled the class model: a type supplies the name and description by reference, the occurrence
owns copies of duration and capacity, the instructor is an account, and there is no room. The member
schedule (`features/schedule/`) groups a flat fortnight into day sections client-side; the admin panel
(`features/admin/classes/`) is a flat unbounded list with per-row edit, duplicate and delete. Neither
read endpoint takes parameters. The app is zoneless and client-rendered — SSR is scaffolded but the
build never wires it — the design system is hand-rolled, and there is no shared-component directory yet.

## Desired End State

A member opens `/schedule` on a phone and gets today's day view with controls to move by day and week
and to jump to a date; on a tablet or desktop the whole week. The admin gets that same calendar with
their actions on it, can look at past weeks read-only, and can drag an empty range to create a class,
completing it in an overlay that supplies the type and the trainer. An empty week says so, over the
grid.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Calendar implementation | `angular-calendar` 0.32.2, adopted for both breakpoints | User's call; verified on npm as Angular 20.2+, standalone, zoneless-aware, date-fns v4. |
| Drag-to-create | In scope for S-07 | User's call; it is the main reason the library was chosen. |
| Gesture completion | Overlay on the calendar | Keeps the gesture where it started, at the cost of a second class form. |
| Data window | `from`/`to` on both read endpoints, omitted = today's behaviour | The only option that lets the backward arrow show anything. |
| Backward navigation | Unlimited, both roles; past weeks read-only for admin | The arrow always does something, and the admin regains lost visibility. |
| PRD conflict | Amend `prd-v2` (FR-015, FR-016, guardrail, Non-Goal) + new FR-019 | The PRD stays the source of truth instead of contradicting shipped code. |
| Component sharing | Shared core + content projection for admin actions | FR-017's stated failure mode is two calendars drifting; a `mode` flag was the runner-up and was rejected during shaping. |
| Day/week switching | Default to the day view, promote via a guarded `matchMedia` read | Mobile-first: the narrow device gets the right view with no reflow, and the default is what specs see in jsdom. |
| Empty state | Message overlaid on the grid, `pointer-events: none` | An empty grid otherwise looks identical to a failed load — and the overlay must not eat the gesture. |
| Test scope | Own logic + API contract; library DOM untested | Tests pinned to the library's internal markup break on every cosmetic release. |

## Scope

**In scope:** date-ranged read endpoints; a shared calendar component with day/week views, week and
day navigation and jump-to-date; the member schedule and the admin panel both on it; past-week
read-only for the admin; drag-to-create with a type/trainer overlay; the PRD and roadmap amendments.

**Out of scope:** month view; booking or cancelling from the calendar; class cancellation and its
notifications (S-09); drag-to-move or drag-to-resize existing classes; recurring series; any trainer
screen; changes to the class model, the overlap rule, or the free-spot projection.

## Architecture / Approach

`shared/calendar/schedule-calendar` becomes the app's first shared component: it owns the visible
range, the view mode, the mapping from `ScheduledClass` to the library's events, and the empty state,
and it knows nothing about roles. It emits `rangeChange`; each screen fetches for that range and feeds
the rows back. The admin screen projects its actions in through content projection and, in Phase 4,
listens for `rangeDrawn` to open the create overlay. Server-side, both read endpoints gain optional
`from`/`to` with the existing behaviour preserved when they are omitted.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Documented truth + date range | Amended `prd-v2` and roadmap; `from`/`to` on both endpoints | Dropping the omitted-range fallback would empty the schedule for every client |
| 2. Calendar core + member schedule | The shared component and `/schedule` on it (FR-015, 016, 018) | The library's bundle cost against a 500 kB budget with ~76 kB of headroom |
| 3. Admin panel on the same calendar | FR-017; past weeks read-only | Losing an existing panel behaviour (partial-success report, inline confirm) in the rewrite |
| 4. Drag-to-create | Gesture plus type/trainer overlay | A second class form drifting from `class-form` |

**Prerequisites:** S-06 (`occurrences-from-class-types`) — done. Local SQL Server via
`docker compose up -d` for the backend tests.
**Estimated effort:** ~4 sessions, one per phase; Phase 2 is the largest.

## Open Risks & Assumptions

- **SSR is scaffolded but never wired**, and this change deliberately leaves it that way. Whether
  `angular-calendar` survives a server render is therefore untested and irrelevant today — it becomes a
  real question the day someone adds the `server`/`ssr` keys to `angular.json`.
- **The overlay is a second class form.** Extracting the failure-message table first mitigates the
  wording drift but not the structural duplication; if the two forms diverge further, merging them is
  the follow-up.
- **`prd-v2` Open Question 2 — how dense the week view can get — is answered by the library**, not by
  us, and only becomes visible with a genuinely busy week.
- **48rem as the day→week threshold is a judgement call**, not a measured one; it lives in two places
  (a CSS token and a TS constant) that must move together.
- **Bundle size drove a routing decision.** The initial bundle is 433,848 B against a 500 kB budget, and
  the library is ~731 KB unpacked, so `/schedule` and `/admin/classes` are lazy-loaded from Phase 2
  rather than eagerly. The exact gzipped cost is still unmeasured; if the lazy chunk itself proves
  heavy, splitting the week view from the day view is the next lever.

## Success Criteria (Summary)

- A member navigates weeks and days on a phone, jumps to a date, and sees the whole week on a larger
  screen — with an empty week reading as deliberately empty.
- The admin panel and the member schedule are visibly the same calendar, and every admin action that
  worked on the list still works on it.
- The admin drags an empty range and completes it into a real class, with conflicts refused in the
  same words the existing form uses.
