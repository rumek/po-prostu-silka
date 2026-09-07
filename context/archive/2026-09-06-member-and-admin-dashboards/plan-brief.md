# Member and Admin Dashboards with a Mobile Bottom Bar — Plan Brief

> Full plan: `context/changes/member-and-admin-dashboards/plan.md`
> Research: `context/changes/member-and-admin-dashboards/research.md`

## What & Why

S-12 is the last slice of the milestone and the one that gives both audiences a front door. A member
should land on their nearest classes and their training plan rather than a placeholder greeting; an
admin should land on what needs attention today. Alongside it, the navigation moves into a bottom bar
on phones — the PRD's "mobile-first and installable, behaving like an app" NFR, made concrete.

## Starting Point

`/` renders a placeholder whose own comment names S-12 as its replacement. All the data already exists:
every card is served by an endpoint shipped in S-01, S-07, S-08 or S-11, so this slice writes no
backend code. `app.html` is the entire navigation system — six role-gated links and a logout button in
a top header — and four admin screens (`/admin/members`, `/admin/classes`, `/admin/class-types`,
`/admin/exercises`) have no navigation at all, reachable only by typing a URL. S-10 deferred that entry
to this slice by name.

## Desired End State

A member opens the app and sees their three nearest booked classes and their active plan, each linking
to the full screen. An admin also sees pending approvals, today's classes and what's coming. On a phone
all navigation is a fixed five-tab bar — Start, Grafik, Moje zajęcia, Mój plan, Więcej — sitting above
the iOS home indicator. "Więcej" holds the account screen, logout, and for admins the six panel links,
including the four that previously had none. On desktop the header is unchanged and the bar is absent.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Aggregated `/api/dashboard` endpoint | No — use existing endpoints | 2–3 cheap indexed round-trips don't justify forking a second projection of four DTOs the SPA already mirrors. | Research |
| Bar shape | 4 fixed tabs + "Więcej" | A role-independent tab set removes the visibility matrix from the bar entirely — the exact bug class the S-01 review caught. | Plan |
| Top header on mobile | Slimmed to brand only | Keeps the skip-link target and the `<header>` landmark, gives orientation in standalone mode, and avoids duplicating navigation. | Plan |
| Admin dashboard location | Same component at `/`, branched on `isAdmin()` | `adminGuard` already redirects admins to `/`, and an admin is also a member who books classes. | Plan |
| Card scope | Exactly the roadmap set, read-only | 1:1 with FR-023/FR-024; adding in-card actions would drag in per-row state and stop being "minimal". | Plan |
| S-10's deferred admin links | Delivered here, on "Więcej" | S-10 recorded that they land in S-12 with an `isAdmin() && isActive()` condition; the hub screen exists anyway, so the cost is near zero. | Research |
| Card markup | Shared components in `shared/` | One source of truth for how a class row and a plan header look. | Plan |
| Admin "today" window | Explicit local-midnight bounds | The endpoint's default window opens at `now`, so a parameterless call silently drops classes that already started today. | Research |

## Scope

**In scope:** member dashboard cards (nearest 3 classes, active plan); admin section (pending count,
today, upcoming); two shared summary components with `my-classes`/`my-plan` rewired onto them; the
"Więcej" hub screen including the four deferred admin links; the bottom bar; `viewport-fit=cover` and
safe-area groundwork; `aria-current`; the `app.spec.ts` rework.

**Out of scope:** any backend change or new endpoint; actions on dashboard cards (cancel, approve);
promoting `.link-button` out of the 17 files that declare it; offline caching; activating SSR; any
desktop header redesign beyond hiding its links on phones.

## Architecture / Approach

Four sequential phases, each leaving the app working and `npm test` green. The refactor goes first and
alone, so the existing `my-classes` and `my-plan` specs act as the safety net for the extraction. The
dashboard then consumes proven components. Navigation lands last, and the "Więcej" screen is built
before the bar that links to it, so the bar never ships pointing at a missing route. Every card loads
independently — one failure does not blank the screen.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Shared summary components | `class-summary` + `plan-summary` in `shared/`, two screens rewired | A refactor of working, tested screens — a spec needing logic changes to stay green means behaviour drifted |
| 2. Dashboard at `/` | Both dashboards live, `Home` deleted | The local-midnight window; and an eager route landing in a bundle twice pushed against the 500 kB budget |
| 3. "Więcej" screen | Account, logout, six panel links | The six-row role matrix must match its guards exactly — this is the S-01 F5 bug class |
| 4. Bottom bar and shell | Bar, slim mobile header, safe-area, `aria-current` | `viewport-fit=cover` is global; z-index vs. three existing modals; 11 shell tests to retarget |

**Prerequisites:** S-01, S-07, S-08, S-11 — all `done`. Nothing else blocks; the backend needs no work.
**Estimated effort:** ~4 sessions, one per phase; Phase 4 is the largest.

## Open Risks & Assumptions

- **Bundle budget.** The dashboard is eager and the initial bundle has hit 502.88 kB before. Mitigation
  is stated: the dashboard must not import `date-fns`, `/more` is lazy, and the fallback is deferring
  the admin section into its own chunk rather than making `/` lazy.
- **`viewport-fit=cover` affects every screen**, not just the ones with a bar. Manual verification
  explicitly covers `/schedule`, `/admin/members` and a form screen.
- **iOS safe-area cannot be verified in unit tests.** It needs a real device or simulator installed to
  the home screen; nothing in CI will catch a regression here.
- **Assumption:** an admin wants their own booked classes on the same screen as the admin section. If
  that proves wrong in use, the admin section becomes its own route — a small, later change.
- **`aria-current` is a first for this codebase**, so there is no house pattern to copy and no existing
  test to model the assertion on.

## Success Criteria (Summary)

- An approved member lands on a screen showing their nearest classes and their plan, not a placeholder.
- An admin sees pending approvals and today's classes at a glance — including a class that started
  earlier today.
- On a phone, navigation is one bottom bar of five tabs that does not change shape between roles, sits
  clear of the iOS home indicator, and reaches every screen in the app including the four admin screens
  that previously had no entry point.
