---
date: 2026-09-22T15:46:32+02:00
researcher: Claude (Opus 5) for Karol Rumianowski
git_commit: 429c6f0e4f6b950f5bc92ec68cec7b6748d4f008
branch: main
repository: po-prostu-silka
topic: "Per-role feature visibility (member / trainer / admin) and a main menu that reaches every page"
tags: [research, codebase, navigation, authorization, dashboard, schedule, roles, bottom-nav, more-hub]
status: complete
last_updated: 2026-09-22
last_updated_by: Claude (Opus 5)
---

# Research: Per-role feature visibility and a main menu that reaches every page

**Date**: 2026-09-22T15:46:32+02:00
**Researcher**: Claude (Opus 5) for Karol Rumianowski
**Git Commit**: 429c6f0e4f6b950f5bc92ec68cec7b6748d4f008
**Branch**: main
**Repository**: po-prostu-silka

GitHub permalinks were not generated (`gh repo view` unavailable in this shell); references are
repo-relative paths at the commit above.

## Research Question

User's request (Polish, verbatim in `change.md`), with scope confirmed on 2026-09-22:

- **Member** sees their own plan, own classes, own karnet, own profile — and NOT the whole-gym
  schedule.
- **Trainer** has no own training plan, no own karnet, no own bookings as a participant
  ("Moje zajęcia"); the schedule shows only the classes they instruct.
- **Admin** has no own plan and no own karnet (confirmed: "Admin powinien mieć swojego planu" was a
  typo for "nie powinien"); the schedule shows every class; the dashboard shows upcoming /
  "Wymaga uwagi" items only where the admin is actually assigned — as instructor or participant.
- **Every page is reachable from the main menu.** Admin pages have no direct links today.
- **Enforcement at UI + API** (confirmed): hiding a link is not enough if the endpoint still returns
  the data.

## Summary

1. **The navigation gap is real and desktop-specific.** The admin screens are linked only from
   `/more` (`src/app/src/app/features/more/more.html:15-35`), and `/more` is linked only from the
   bottom bar, which is hidden above 30rem (`shared/bottom-nav/bottom-nav.scss:28-30`). The desktop
   header (`app.html:20-44`) has Grafik, Zajęcia, Plan, the trainer-only Członkowie, Moje konto and
   logout — no admin link and no link to `/more`. On a desktop screen an admin can reach
   `/admin/members`, `/admin/class-types` and `/admin/exercises` from **no** menu, and
   `/admin/classes` only via the dashboard's "Zarządzaj zajęciami", which renders only when the
   "Nadchodzące" card has rows (`features/dashboard/dashboard.html:173`). The header's own comment
   (`app.html:16-19`) still describes admin links that no longer exist.

2. **Nothing is role-filtered today, in the SPA or the API.** Every active account — member,
   trainer, admin — gets the same header links, the same role-blind bottom bar, the same three
   member dashboard cards and the same whole-gym `/schedule`. On the server, `ActiveMember` is
   `RequireRole(User, Admin)` (`src/Domain/ApplicationRoles.cs:50`,
   `src/Infrastructure/Authorization/AuthorizationPolicies.cs:60-64`) and a trainer holds `User`, so
   **no existing policy can tell a member from staff**. That is the crux of the slice.

3. **Roles are a set, and the combinations are legal.** `User` (member), `Admin`, `Trainer`; the
   seeded admin holds `Admin` only (`src/Infrastructure/Identity/AdminSeeder.cs:88`), a trainer holds
   `User`+`Trainer`, and nothing prevents `Admin`+`Trainer`
   (`src/Application/Members/ChangeTrainerRole.cs:95-111`). The slice therefore needs an explicit
   **precedence rule** — the natural one is Admin > Trainer > Member, i.e. an account is a "member" for
   visibility only when it holds `User` and neither `Trainer` nor `Admin`.

4. **The schedule needs a role-aware server answer.** `GET /api/classes`
   (`src/Api/Endpoints/Scheduling/ClassEndpoints.cs:46-50`) returns every scheduled class to anyone
   passing `ActiveMember` (`src/Infrastructure/Scheduling/ClassScheduleQuery.cs:15-22`). No query
   filters by instructor, but the shared projection already carries `InstructorMemberId`
   (`ClassScheduleQuery.cs:39-98`), and the trainer ownership rule already exists as
   `BookingAuthorization.MayActOn` (`src/Application/Scheduling/BookingAuthorization.cs:49-51`:
   admin, or `GetMemberId() == InstructorMemberId`) — the same rule, as a SQL predicate, is the
   trainer's schedule.

5. **The admin dashboard needs a new feed, not a narrowed old one.** "Wymaga uwagi" / "Dzisiaj" /
   "Nadchodzące" come from `GET /api/admin/classes` (`features/dashboard/dashboard.ts:125-127,
   163-193, 240-241`), unfiltered. The admin calendar at `/admin/classes` depends on the same
   endpoint (`features/admin/classes/classes.ts:193`), so it must not be narrowed in place. "Assigned"
   = `InstructorMemberId == me` OR an active booking with `MemberId == me`.

6. **The karnet is not a page.** It exists only as the dashboard card "Twój karnet"
   (`dashboard.html:46-72`, `GET /api/passes/mine`). "Every page from the menu" does not reach it
   unless the plan decides it should become one.

7. **This slice reverses several recorded product decisions** (see Historical Context): member
   schedule browsing (v1 FR-007, v2 FR-018, kept by S-16), role additivity (v2 FR-002, whose
   exclusive alternative was tried and rejected), the club-wide admin dashboard (v1 FR-024, S-12),
   the role-blind bottom bar (S-12, M-7 "not in scope"), and "admins train too" (S-22). The plan must
   record these reversals in the foundation docs, not only in code comments.

## Detailed Findings

### Navigation surfaces today

**Header** (`src/app/src/app/app.html`), rendered on `auth.isAuthenticated()` (:6), hidden below
30rem (`app.scss:86-101`):

| Link | Route | Condition | Line |
| --- | --- | --- | --- |
| logo | `/` | always | :4 |
| Grafik | `/schedule` | `auth.isActive()` | :20-21 |
| Zajęcia | `/my-classes` | `auth.isActive()` | :22 |
| Plan | `/my-plan` | `auth.isActive()` | :26 |
| Członkowie | `/trainer/members` | `isTrainer() && !isAdmin() && isActive()` | :34-35 |
| Moje konto | `/profile` | authenticated | :42 |
| Wyloguj się | — | authenticated | :44 |

**Bottom bar** (`shared/bottom-nav/bottom-nav.ts:26-32`) — a fixed `BOTTOM_NAV_TABS` constant, no
`AuthService` injected, shown only at ≤30rem: Start `/`, Grafik `/schedule`, Zajęcia `/my-classes`,
Plan `/my-plan`, Więcej `/more`. Its docblock (:34-49) records role-blindness as the design.

**`/more` hub** (`features/more/more.html`, `more.ts`): Moje konto (always), a "Panel" section on
`(isTrainer() || isAdmin()) && isActive()` (`more.ts:42-44`) holding Członkowie `/admin/members`,
Zajęcia `/admin/classes`, Typy zajęć `/admin/class-types`, Ćwiczenia `/admin/exercises` for
`isAdmin() && isActive()` (`more.ts:46-48`), or Członkowie `/trainer/members` for trainer-only
(`more.ts:51-53`); logout.

**Link × role matrix (active accounts):**

| Link | Member | Trainer (User+Trainer) | Admin | Admin+Trainer |
| --- | --- | --- | --- | --- |
| Grafik, Zajęcia, Plan (header + bar) | yes | yes | yes | yes |
| Start `/` (bar, logo) | yes | yes | yes | yes |
| Więcej `/more` | phone only | phone only | phone only | phone only |
| Moje konto | yes | yes | yes | yes |
| `/trainer/members` | no | header + /more | no | no |
| four `/admin/*` lists | no | no | **phone only** | **phone only** |

### Routes and how each is reached

Routes are in `src/app/src/app/app.routes.ts`. Top-level screens with no desktop menu path:
`/admin/members` (:55), `/admin/classes` (:106 — dashboard link only), `/admin/class-types` (:114 —
via `classes.html:30` / the class form), `/admin/exercises` (:122 — unreachable on desktop except by
URL), `/more` (:194). Sub-screens (`new`, `:id`, `:id/passes`, `:id/plan`, exercise detail,
`/my-plan/exercises/:id`, `/trainer/members/:id/plan`) are reached from their list screens, which is
fine — "every page from the menu" reasonably means every top-level screen.

### Guards and the SPA's view of roles

- `authGuard` → `isAuthenticated()`; `activeMemberGuard` → `isActive()` (not role-based,
  `core/auth/active-member.guard.ts:39`); `adminGuard` → `isAdmin() && isActive()`
  (`admin.guard.ts:29`); `trainerGuard` → `(isTrainer() || isAdmin()) && isActive()`
  (`trainer.guard.ts:30`, admits admins — pinned by `trainer.guard.spec.ts:54`).
- `AuthService` (`core/auth/auth.service.ts`): `isActive` (:45-49), `isAdmin` (:51), `isTrainer`
  (:58); **no `isMember`** signal. Session from `GET /api/auth/me` (:174); payload `CurrentUser`
  (`core/auth/auth.models.ts:5-44`) carries `roles: string[]` and `memberId`.
- Role names: `core/auth/roles.ts:13-22` mirrors `src/Domain/ApplicationRoles.cs`.

### Server role model and policies

- Roles: `src/Domain/ApplicationRoles.cs:23,26,33`; `All` (:39); `MemberFacing = [User, Admin]` (:50).
  Its docblock (:41-49) says a new role joins `MemberFacing` only if holding it alone should grant
  member-facing access — Trainer deliberately does not, and passes only through `User`.
- Claims: `account_status`, `member_status`, `member_id`
  (`src/Infrastructure/Identity/AppUserClaimsPrincipalFactory.cs:42, 63-66`). Every account, staff
  included, has a `Member` row, and every ownership FK (`Class.InstructorMemberId`,
  `Booking.MemberId`, `MembershipPass.MemberId`, `TrainingPlan.MemberId`) points at it.
- Policies (`src/Infrastructure/Authorization/AuthorizationPolicies.cs`): `ActiveMember` (:60-64),
  `Admin` (:65-69), `TrainerOrAdmin` (:78-82), registered at `src/Api/Program.cs:148`.
- Testing-only probes `GET /test/active-member`, `GET /test/admin-only` (`Program.cs:423-430`).

### Endpoints involved

| Endpoint | Policy | Returns | Change needed |
| --- | --- | --- | --- |
| `GET /api/classes` (`ClassEndpoints.cs:46-50`, `GetSchedule.cs:47`) | ActiveMember | every scheduled class | member → refused; trainer → own instructed; admin → all |
| `GET /api/admin/classes` (`ClassEndpoints.cs:52-62`, `GetAdminClasses.cs:49`) | Admin | every class (≤62 days) | keep — the admin calendar uses it |
| `GET /api/bookings/mine` (`BookingEndpoints.cs:65-69`, `BookingQuery.cs:34-50`) | ActiveMember | caller's own bookings | member only |
| `GET /api/passes/mine` (`MyPassEndpoints.cs:33-37`, `GetMyPass.cs:38-48`) | ActiveMember | caller's covering pass | member only |
| `GET /api/plans/mine`, `/mine/exercises/{id}` (`MyPlanEndpoints.cs:32-37`) | ActiveMember | caller's active plan | member only |
| `/api/admin/classes/{id}/bookings` (`BookingEndpoints.cs:91-97`) | TrainerOrAdmin + `MayActOn` | roster / book / release | unchanged |
| `/api/trainer/members` (`TrainerMemberEndpoints.cs:35-40`) | TrainerOrAdmin | every assignable member, staff included | see open question on staff as plan holders |
| `GET /api/auth/me`, `PUT /api/profile` | authenticated | own profile | unchanged |
| dashboard "assigned classes" | — | **does not exist** | new: instructed ∪ booked for the caller |

There is no server-side dashboard endpoint; the dashboard composes client-side loads
(`features/dashboard/dashboard.ts:112-118`).

### Schedule

- SPA: `features/schedule/schedule.ts:78-116` calls `GET /api/classes` and `GET /api/bookings/mine`
  on every range change; no `AuthService`, no role branch. Selecting a class opens
  `ClassDetailsOverlay` (read-only; "Zapisów dokonuje klub…", `class-details-overlay.html:45-51`).
  The hint at `schedule.html:2` still says "…i się zapisać" (stale since S-16).
- For a trainer the `bookings/mine` call becomes meaningless (no participant bookings) and should be
  dropped or gated.
- UX-01 (S-20) sends an admin on a phone from the refused `/admin/classes` to `/schedule` as the
  phone surface; that stays valid because the admin keeps the full schedule.
- Links into `/schedule` that a member will lose: `dashboard.html:19` (empty "Najbliższe zajęcia"),
  `my-classes.html:24` (empty state), `classes.html:82`.

### Dashboard

- For everyone (`dashboard.html:6-106`): "Najbliższe zajęcia" (`/api/bookings/mine`, first 3),
  "Twój karnet" (`/api/passes/mine`), plan card (`/api/plans/mine`). The comment at `dashboard.html:3-4`
  records "the admin's included, because an admin books classes like anyone else".
- For an admin (`dashboard.html:109-177`, `@if (isAdmin())`, `dashboard.ts:58`): one request to
  `GET /api/admin/classes` over local midnight → +8 days (`todayWindow`, `dashboard.ts:265-276`), split
  into "Dzisiaj" and "Nadchodzące" (`dashboard.ts:240-241`), plus "Zarządzaj zajęciami". Club-wide.
- For a trainer: nothing trainer-specific.
- Target shape implied by the request: member keeps the three cards; admin loses karnet and plan,
  and the class section shows only assigned classes (which also subsumes the admin's participant
  bookings); trainer loses karnet, plan and bookings, and has nothing left unless the slice gives
  them an "assigned classes" card too — the same feed serves both.

### Can staff be a participant, pass holder or plan holder?

Yes, all three, today:

- Passes: `IssuePass.cs:31-49` — member exists, is Active, no overlap. No role check.
- Bookings: `BookForMember.cs:61-72` and `BookingProtocol.TryBookAsync` (`no_valid_pass` at ~:124) —
  no role check, no check against booking the instructor into their own class. A staff member with a
  covering pass can be booked.
- Plans: `TrainingPlanValidator.ValidateMemberAsync` via `Assignable`
  (`src/Infrastructure/Training/TrainingPlanQuery.cs:29-31`) — status only, no role filter; staff
  appear in `/api/trainer/members` and `/admin/members` as plan holders.

This collides with the request. The admin dashboard is supposed to show classes where the admin is
a *participant*, yet an admin has no karnet surface — and being booked requires a covering pass.
Either staff keep the ability to hold a pass and be booked (hidden only from their own menus), or the
domain forbids it, and "participant" drops out of the admin dashboard rule. See Open Questions.

### Tests that pin today's behaviour

**SPA specs** (will change):

- `app.spec.ts` :168 "shows the own-plan link to any active member", :306 "gives an admin the same
  five tabs as a member"; also :128, :149, :193, :215, :232, :261-290.
- `shared/bottom-nav/bottom-nav.spec.ts` :46 exactly five tabs in order; :86 "carries no
  role-conditional destination".
- `features/more/more.spec.ts` :103-164 panel per role.
- `features/dashboard/dashboard.spec.ts` :239 (no admin request for a member), :253 (admin gets
  "Wymaga uwagi" + `/api/admin/classes` AND still loads bookings, plan, pass), :274 window bucketing.
- `features/schedule/schedule.spec.ts` — no role cases today; needs them.
- Guard specs: `core/auth/{trainer,admin,active-member}.guard.spec.ts`.

**Playwright** (`src/app/e2e/`): `guarded-route-redirects-to-login.spec.ts` opens `/my-classes`
anonymously, signs in as admin and expects `/`. After this slice `/my-classes` stays guarded, but the
admin must no longer be able to reach it — the test still passes because it only checks the redirect
target is `/`; worth re-reading at plan time. `seed.spec.ts` checks the admin session on `/`. The only
e2e user is the admin.

**API integration tests** (will change):

- Member reads `/api/classes`: `ClassEndpointTests.cs` :660-671, :696-716, :724-740, :752-771,
  :779-785 (`InlineData("/api/classes")`), :822-848; `BookingEndpointTests.cs` :744-772;
  `ClassCancellationTests.cs` :402-416. These must switch to a trainer/admin caller or become refusal
  tests.
- Own-data reads stay valid for members; add refusal cases for trainer and admin next to them:
  `MembershipPassEndpointTests.cs` :502, :535; `MyPlanEndpointTests.cs` (whole file);
  `ClassCancellationTests.cs:1029-1034`; `MemberClaimTests.cs:251-254`.
- `EndpointAuthorizationTests.cs`: :71 `TrainerAdminRoutes`, :163 (every `/api/admin/*` route is
  `Admin` unless listed), :196 trainer routes. A new policy or route must be registered here — per
  `context/foundation/test-plan.md:157-162`, touch it only when the product changes who may reach
  something, citing the decision. This slice is exactly that case.
- Probes assume `User` and `Admin` both pass `ActiveMember`: `AuthEndpointTests.cs:150-274` (:178),
  `MemberBlockPolicyTests.cs:86-220`, `RegisterEndpointTests.cs:102`.
- Fixture users (`tests/po-prostu-silka.Tests/IntegrationTestFixture.cs:123-137`, 459-469):
  ActiveAdmin `[Admin]`, ActiveMember/BlockedMember `[User]`, ActiveTrainer `[User, Trainer]`.
  **No Admin+Trainer fixture** — the precedence rule needs one.

### Test data seeder (S-24)

`src/Infrastructure/TestData/TestDataGenerator.cs`: admins `[Admin]` (:146), trainers
`[User, Trainer]` (:151-152), 160 members `[User]` (:164); `ClubMembers()` (:662-663) excludes staff,
so passes (:273), bookings (:411) and plans (:569) never go to staff; instructors are always trainers
(:399). **Consequence: under "only where assigned", both seeded admins' dashboards are empty in
Staging.** Nothing seeds an Admin+Trainer. The plan should decide whether the seed gains one (e.g.
`admin2` also `Trainer` and instructing some classes) so the new dashboard rule is demonstrable.

## Code References

- `src/app/src/app/app.html:6-45` — header nav; :16-19 stale comment about admin links.
- `src/app/src/app/app.routes.ts:54-199` — every route and guard; :164-165 "every approved account has a plan surface, the trainer's own included".
- `src/app/src/app/shared/bottom-nav/bottom-nav.ts:26-49` — role-blind tab constant and its rationale.
- `src/app/src/app/shared/bottom-nav/bottom-nav.scss:26-30` — bar hidden `above-narrow`.
- `src/app/src/app/features/more/more.ts:42-53`, `more.html:10-56` — the only place admin lists are linked.
- `src/app/src/app/core/auth/auth.service.ts:45-58, 174` — role and status signals, session load.
- `src/app/src/app/core/auth/*.guard.ts` — the four guards.
- `src/app/src/app/features/dashboard/dashboard.ts:58, 112-127, 163-193, 240-241, 265-276` — card loads, admin section, window.
- `src/app/src/app/features/dashboard/dashboard.html:3-4, 6-177` — cards, karnet, admin section, "Zarządzaj zajęciami".
- `src/app/src/app/features/schedule/schedule.ts:78-116`, `schedule.html:2, 14` — schedule loads, stale hint.
- `src/app/src/app/features/admin/classes/classes.ts:193` — admin calendar on `GET /api/admin/classes`.
- `src/Domain/ApplicationRoles.cs:20-51` — roles, `All`, `MemberFacing`.
- `src/Infrastructure/Authorization/AuthorizationPolicies.cs:60-82` — the three policies.
- `src/Infrastructure/Identity/AppUserClaimsPrincipalFactory.cs:42, 63-66` — status and member claims.
- `src/Api/Endpoints/Scheduling/ClassEndpoints.cs:46-62` — member schedule and admin class groups.
- `src/Infrastructure/Scheduling/ClassScheduleQuery.cs:15-98` — schedule queries and shared projection.
- `src/Application/Scheduling/IClassScheduleQuery.cs:18-31` — query port (no instructor filter).
- `src/Application/Scheduling/BookingAuthorization.cs:49-51` — `MayActOn`, the instructor ownership rule.
- `src/Api/Endpoints/Scheduling/BookingEndpoints.cs:65-97` — `bookings/mine` and the staff booking group.
- `src/Api/Endpoints/Members/MyPassEndpoints.cs:33-37`, `src/Api/Endpoints/Training/MyPlanEndpoints.cs:32-37` — own pass and own plan.
- `src/Application/Members/ChangeTrainerRole.cs:95-111` — Trainer is additive, Admin+Trainer reachable.
- `src/Infrastructure/TestData/TestDataGenerator.cs:142-164, 273, 399, 411, 569, 662-663` — staff seeding.
- `tests/po-prostu-silka.Tests/EndpointAuthorizationTests.cs:71, 131, 163, 196` — the access-matrix guards.

## Architecture Insights

- **"Active" and "role" are two different axes, and the app only uses the first for member-facing
  surfaces.** Every member-facing gate — `activeMemberGuard`, the header's `isActive()`, the
  `ActiveMember` policy — answers "may this person use the club", never "is this person a member
  rather than staff". The slice introduces the second question; it should get one name on each side
  (e.g. an `isMemberOnly`/`persona` signal in `AuthService` and a matching server policy), not
  scattered `!isTrainer() && !isAdmin()` checks.
- **Nav condition must equal guard condition** (S-01 review F5, "the S-01 bug class"). Every new or
  re-gated link must use exactly its route guard's condition, and the guard must equal the API
  policy. Three layers, one predicate per persona.
- **Precedence must be one rule.** With additive roles, "member / trainer / admin" is a *derived*
  persona, not a stored role. Admin > Trainer > Member resolves `Admin+Trainer` (admin schedule,
  admin menu, dashboard includes the classes they instruct) and `User+Trainer` (trainer).
- **Server-side role-aware reads already have a precedent**: `MayActOn` branches on Admin vs
  `member_id == InstructorMemberId`. The schedule endpoint can follow the same shape — a handler that
  picks the predicate from the principal — rather than three endpoints.
- **Don't narrow shared endpoints in place.** `GET /api/admin/classes` serves both the admin calendar
  and the dashboard; the dashboard needs its own "assigned classes" feed.
- **A role-aware bottom bar reverses a documented design.** The bar was made role-blind to remove the
  visibility matrix from it (S-12); the M-7 roadmap already named the role-aware bar as a real,
  deferred need. With three personas and five slots the bar will differ per persona — the design
  must keep `narrow`/`above-narrow` as an exact partition and keep header and bar driven from the
  same per-persona link table, so they cannot drift.

## Historical Context (from prior changes)

- `context/foundation/prd.md:95-96` (v1 FR-007) and `context/foundation/prd-v2.md:76-80, 98-99, 314-317`
  (FR-018) — members browse the schedule as a calendar. **Reversed by this slice.**
- `context/archive/2026-09-09-membership-pass-and-staff-booking/plan.md:706-707, 734-735, 770` (S-16)
  — members lost booking, but "the schedule stays browsable"; bottom nav unchanged.
- `context/foundation/prd-v2.md:196-200, 420-423, 433-435` (FR-002) and `foundation/shape-notes.md:20-21`
  — roles are additive; a trainer keeps every member capability; exclusivity "was tried and abandoned
  because it left a non-admin trainer with an empty application". **Reversed** — and the
  empty-application objection is answered only if the trainer gets a real surface (filtered schedule,
  member list, and ideally an assigned-classes dashboard card).
- `context/foundation/prd-v2.md:201-204` (FR-003) — Admin+Trainer is legal by design.
- `context/archive/2026-09-06-member-and-admin-dashboards/plan-brief.md:26-27, 35, 37, 83-84`,
  `plan.md:89-92, 201-217, 277-285, 337-341`, `research.md:418-423, 433-435` (S-12) — role-blind five-tab
  bar (role-dependent bar rejected), admin panel links on `/more` only, no desktop header redesign,
  one dashboard component with an admin section, club-wide today/upcoming. The brief explicitly
  anticipated "if that proves wrong, the admin section becomes its own route — a small, later change".
- `context/foundation/prd.md:133-134` (v1 FR-024) — admin dashboard shows items needing attention
  including today's and upcoming classes, club-wide. **Narrowed by this slice.**
- `context/foundation/roadmap.md:133-139` (M-7 "Not in scope") — "a role-aware bottom nav (an admin
  carries `/my-plan` and `/my-classes` in the bar while Członkowie and Zajęcia sit two taps away under
  Więcej) - real, and a navigation change rather than a surface one." This slice is that change.
- `context/archive/2026-09-02-class-type-definitions/plan.md:56-57, 110-111`,
  `2026-09-04-exercise-library/plan-brief.md:36`, `research.md:223-233` — admin screens deliberately got
  no global nav entry, deferred to S-12; S-12 then linked them from `/more` only.
- `context/archive/2026-09-01-registration-and-approval/reviews/impl-review.md:125-133` (S-01 F5) —
  nav condition must equal guard condition.
- `context/archive/2026-09-21-member-centric-training-plans/plan.md:79, 383-390, 421`, `plan-brief.md:45`
  (S-22) — the trainer list includes the trainer and every admin, "admins train too" (pinned by a
  test); `Członkowie` → `/trainer/members` for trainer-without-admin. **"Admins train too" is reversed.**
- `context/archive/2026-09-21-admin-schedule-web-only/research.md:223-230` (S-20, UX-01) — the admin on a
  phone is sent to `/schedule`; notes no desktop header link to `/admin/classes`.
- `context/foundation/prd-v2.md:441-442, 474-476`, `roadmap.md:821, 831` — "Trainer screen ('my
  classes')" parked; OQ "what does a trainer eventually see after signing in?" still open. **Answered
  in part by this slice.**
- `context/foundation/roadmap.md:824` — OQ 8: should trainers see member emails on their rosters —
  unaffected, but adjacent.
- `context/foundation/prd.md:141` — privacy NFR (member data visible only to admin and the member).
  Hiding the full schedule from members is consistent with it; the instructor name stays visible on a
  member's own classes.
- `context/archive/2026-09-07-member-entity-and-accountless-members/plan.md:118` claims
  "`/admin/members` is already in `app.html`" — contradicted by the code; per the lessons rule, trust
  the code.

## Related Research

- `context/archive/2026-09-06-member-and-admin-dashboards/research.md` — the nav and dashboard design
  this slice revisits.
- `context/archive/2026-09-21-admin-schedule-web-only/research.md` — the admin's phone vs desk surfaces.
- `context/archive/2026-09-21-member-centric-training-plans/research.md` — trainer member list, plans reached through members.
- `context/archive/2026-09-11-testing-access-surface/research.md` — the access-matrix tests.

## Open Questions

1. **Staff as participants / pass holders / plan holders — hide or forbid?** The request removes the
   karnet, plan and "Moje zajęcia" from trainers and admins, yet the admin dashboard should show
   classes where the admin is a *participant*, and booking requires a covering pass. Options:
   (a) hide only — staff can still be issued a pass and booked by someone else, their own menu just
   does not show it (the dashboard's "assigned" feed covers participation); (b) forbid in the domain —
   `IssuePass`, `BookForMember` and plan assignment refuse staff, the member lists exclude them, and
   the dashboard rule collapses to "instructed only". (b) reverses S-22's "admins train too" outright
   and touches the no-overbooking booking path; (a) leaves data a staff member can't see themselves.
2. **Does the admin still need `/schedule` next to `/admin/classes`?** Two calendars of the same
   classes. `/schedule` is the admin's phone surface (UX-01), `/admin/classes` the desk tool. One menu
   entry that routes by breakpoint, or two entries?
3. **What does a member's schedule-less app link to?** `dashboard.html:19` and `my-classes.html:24`
   send members to `/schedule` in empty states; the copy must change ("Zapisów dokonuje klub" already
   exists as the wording).
4. **Does the karnet become a page with a menu entry**, or does the dashboard card satisfy "swój
   karnet"? The request says members should *see* it; it does not say it needs a route.
5. **Trainer dashboard content.** With karnet, plan and bookings gone, the trainer's `/` has nothing
   but a greeting unless it gets the same "assigned classes" card as the admin. Recommended, since
   the same feed serves both.
6. **Trainer staff-booking UI.** The API already lets a trainer see the roster and book into their
   own classes (`BookingEndpoints.cs:91-97`), but the SPA exposes it only under `/admin/classes`.
   A trainer-filtered `/schedule` is the natural place for it — in scope here or a follow-up?
7. **Menu contents per persona.** Proposed starting point for the plan:
   member — Start, Zajęcia, Plan, Moje konto (karnet on Start);
   trainer — Start, Grafik (own classes), Członkowie (`/trainer/members`), Moje konto;
   admin — Start, Grafik (all), Członkowie, Zajęcia (`/admin/classes`), Typy zajęć, Ćwiczenia, Moje
   konto. The desktop header must carry all of them; the phone bar has five slots, so the admin's
   overflow still goes through Więcej — which then needs a desktop link too, or no overflow at all on
   desktop.
8. **Foundation docs.** Reversals of FR-002, FR-007/FR-018, FR-024 and the S-12 bar design need a PRD
   amendment and a roadmap entry; decide whether the plan carries that as its own phase.
