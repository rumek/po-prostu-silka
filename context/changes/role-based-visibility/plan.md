# Role-based Visibility Implementation Plan

## Overview

Every active account sees the same app today: the whole-gym schedule, "Moje zajęcia", "Mój plan"
and the karnet card, whether it belongs to a member, a trainer or an admin. The admin screens, meanwhile,
are linked from no desktop menu at all. This slice gives the app three **personas**. Each is derived
from the additive role set with precedence **Admin > Trainer > Member**, and each persona is one
predicate enforced identically in the menu, the route guard and the API policy:

- **Member** sees their own classes, own plan, own karnet (dashboard card) and own profile. They get
  **no schedule**.
- **Trainer** sees a schedule of only the classes they instruct, and books members into them from
  that schedule. They also get their member list and a dashboard of their upcoming classes. They
  have no plan, no karnet and no bookings of their own.
- **Admin** (including Admin+Trainer) sees the whole schedule, the management screens and a dashboard
  of the classes they instruct. They have no plan, no karnet and no bookings of their own.

Staff are also **forbidden in the domain** from receiving a new karnet, a booking or a training plan.
Every top-level screen becomes reachable from the menu at every width.

## Current State Analysis

(Full detail in `research.md`.)

**Navigation.**
- The header (`src/app/src/app/app.html:20-44`) shows Grafik/Zajęcia/Plan on `isActive()` alone,
  Członkowie for a trainer without Admin, and Moje konto. It has no admin link and no link to `/more`.
- The bottom bar is a role-blind constant (`shared/bottom-nav/bottom-nav.ts:26-32`) and is hidden
  above 30rem.
- `/more` (`features/more/more.html:15-49`) is the only place the four admin lists are linked, so on
  a desktop screen an admin reaches `/admin/members`, `/admin/class-types` and `/admin/exercises`
  from no menu.

**Authorization.**
- `ActiveMember` = active account + active membership + `RequireRole(User, Admin)`
  (`src/Infrastructure/Authorization/AuthorizationPolicies.cs:60-64`). A trainer holds `User`, so
  no policy tells a member from staff.
- Exactly four product route groups use `ActiveMember`:
  - `GET /api/classes` (`ClassEndpoints.cs:46-50`, returns every class);
  - `GET /api/passes/mine` (`MyPassEndpoints.cs:33-37`);
  - `GET /api/plans/mine[/exercises/{id}]` (`MyPlanEndpoints.cs:32-37`);
  - `GET /api/bookings/mine` (`BookingEndpoints.cs:65-69`).
- Push, profile and auth are bare-authorized.

**Roles.**
- An account holds a set of roles (`src/Domain/ApplicationRoles.cs`). The seeded admin holds only
  `Admin` (`AdminSeeder.cs:88`), a trainer holds `User`+`Trainer`, and Admin+Trainer is reachable
  via `ChangeTrainerRole`.
- Every account, staff included, has a `Member` row, which is what every ownership FK points at.

**Staff as participants.**
- `IssuePass` (`IssuePass.cs:46-49`), `BookForMember` (`BookForMember.cs:61-73`) and plan assignment
  (`TrainingPlanQuery.Assignable`, `:29-31`) check only status. Staff can hold all three today.

**Dashboard.**
- Every account gets the bookings, karnet and plan cards (`dashboard.ts:112-128`).
- An admin additionally gets "Wymaga uwagi" fed by the club-wide `GET /api/admin/classes`
  (`dashboard.ts:225-255`). The admin calendar at `/admin/classes` depends on that same endpoint
  (`classes.ts:193`).

**Trainer booking.**
- The API already lets a trainer read the roster, book and release on the classes they instruct:
  `BookingEndpoints.cs:91-97` under `TrainerOrAdmin`, with `BookingAuthorization.MayActOn`
  (`BookingAuthorization.cs:49-51`).
- The only UI for it is `features/admin/classes/class-bookings-overlay`, behind `adminGuard`. Its
  member picker calls the Admin-only `GET /api/admin/members` (`class-bookings-overlay.ts:179-206`).

## Desired End State

Access matrix. Every row is enforced in the SPA menu, the SPA guard and the API. A blocked account
keeps only Moje konto and logout.

| Surface | Member | Trainer | Admin / Admin+Trainer |
| --- | --- | --- | --- |
| `/` dashboard | nearest bookings, karnet, plan | "Twoje zajęcia" (today + 7 days, classes I instruct) | "Twoje zajęcia" (same feed) |
| `/schedule` (`GET /api/classes`) | route: redirect to `/`; API: 403 | own instructed classes, tile opens the bookings overlay | all classes, tile opens the bookings overlay |
| `/my-classes`, `/my-plan`, `/my-plan/exercises/:id` (`/api/*/mine`) | yes | route: redirect to `/`; API: 403 | route: redirect to `/`; API: 403 |
| `/trainer/members` | no | yes (lists only non-staff members) | no (uses `/admin/members`) |
| `/admin/*` | no | no | yes |
| `/profile`, `/more` | yes | yes | yes |

Menus, from one table in `core/layout/navigation.ts`:

```
MEMBER   header: Start · Zajęcia · Plan · Moje konto · [Wyloguj]
         bar:    Start | Zajęcia | Plan | Więcej          Więcej: Moje konto, Wyloguj
TRAINER  header: Start · Grafik · Członkowie · Moje konto · [Wyloguj]
         bar:    Start | Grafik | Członkowie | Więcej     Więcej: Moje konto, Wyloguj
ADMIN    header: Start · Grafik · Członkowie · Typy zajęć · Ćwiczenia · Moje konto · [Wyloguj]
         bar:    Start | Grafik | Członkowie | Ćwiczenia | Więcej
         Więcej: Typy zajęć, Moje konto, Wyloguj
         Grafik → /admin/classes at ≥64rem (DESK_MEDIA_QUERY), /schedule below
```

Domain rules:
- Issuing a karnet to, booking, or creating a training plan for a staff member (whose account holds
  Trainer or Admin) is refused with `409 member_is_staff`.
- Granting the Trainer role stays allowed even when the member already holds such data. That data
  becomes invisible to them, and no migration touches existing rows.

Verification: `dotnet test` and `npm test` green, `npm run quality:check` green, and the manual matrix
walk-through in each phase.

### Key Discoveries

- **Test pattern for correlated role EXISTS:** `src/Infrastructure/Members/TrainerQuery.cs:23,32-39`
  compares `NormalizedName`, not `Name`. Reuse it for "is staff".
- **Handler ports:** `IMemberStore` is already injected into `IssuePass` and `BookForMember`. Adding
  `IsStaffAsync` there leaves both handler signatures unchanged.
- **Booking protocol stays pure:** `BookingProtocol.TryBookAsync` takes no principal by design
  (`BookingProtocol.cs:55-62`), so the staff check sits in `BookForMember` next to `member_blocked`
  (`:70-73`).
- **Plan validation:** `TrainingPlanValidator.ValidateMemberAsync` (`:168-181`) maps
  `IsAssignableAsync`'s `bool?` to two reasons. `UpdateTrainingPlan` never re-validates the member.
- **SPA failure contract:** each failure union is mirrored in the SPA in three compiler-checked
  places (union type, `*_REASONS` object, `MESSAGES` table). `core/http/failure-contract.spec.ts`
  fails on a fallback sentence or an undeclared duplicate sentence.
- **Staff member picker:** `TrainerMemberSummary(Id, DisplayName, HasAccount, PlanName)`
  (`TrainerMemberSummary.cs:18`) is enough for a picker. Once `Assignable` excludes staff, it is
  exactly the set of people staff may book.
- **Admin members list:** already has `isAdmin(member)` and `isTrainer(member)`
  (`members.ts:416-426`), and `members.html:234` already hides block for admins. That is the
  precedent for hiding Karnety/Plan.
- **UX-01 refusal still valid:** the refusal on `/admin/classes` below desk links to `/schedule`
  (`classes.html:73-86`). That stays valid, because the admin keeps `/schedule`.
- **Guard redirects:** `activeMemberGuard` redirects to `/login`, which is wrong for a
  signed-in-but-wrong-persona user. The new persona guards redirect to `/`.
- **Spec stubs:** every spec stubs `AuthService` as an object literal that already exposes `user`. A
  pure `personaOf(user)` keeps those stubs working, where a new signal on `AuthService` would not.

## What We're NOT Doing

- **No data migration.** Existing passes, bookings and plans held by staff stay. A runbook query
  lists them for manual clean-up. A cleanup migration would have no working `Down`.
- **No cascade on granting Trainer.** The grant stays allowed and leaves any member data invisible
  to its holder.
- **No karnet page.** The karnet stays the dashboard card.
- **No member-facing schedule in any form.** A member does not get a "my classes as a calendar" view
  either. `/my-classes` stays the list.
- **No change to `GET /api/admin/classes`** or to the admin calendar at `/admin/classes`.
- **No new trainer search endpoint.** The picker reuses `/api/trainer/members`, which searches by
  name only.
- **No visual redesign** of header or bar beyond their link sets (UX-09). Only one new icon is added.
- **No change to how quickly a role grant reaches a signed-in session.** A member granted Trainer
  keeps member access until their cookie refreshes, as with every existing role change.
- **No retirement of the `ActiveMember` policy.** It stops guarding product routes but stays as the
  Testing probe's policy. See Phase 2.
- **Open roadmap question 8 is not addressed.** Trainer seeing member emails on rosters is out of
  scope.

## Implementation Approach

Documents first, so the reversals are recorded before code enacts them. Then the server, whose
access matrix is the contract. Then the domain rule. Then the SPA in two passes: the shell and
guards, then the screens.

The persona is computed in one place per side:
- **SPA:** `personaOf(user)` in `core/auth/persona.ts`.
- **API:** the policy set in `AuthorizationPolicies.cs` plus a handler-level branch in the schedule
  query that mirrors `MayActOn`.

"Is staff" for a member row has one Infrastructure definition, shared by `IMemberStore.IsStaffAsync`
and `TrainingPlanQuery.Assignable`.

## Critical Implementation Details

**Ship Phases 2–5 together.** CI deploys `main` on every merge. After Phase 2 alone, a member's SPA
would still open `/schedule` and get 403, and staff dashboards would fire three 403s. Implement on
one branch and merge once, after Phase 5. Phase 1 (docs only) may merge on its own.

**Persona precedence must never require `User` for staff.** The seeded admin holds `Admin` only.
`personaOf` and the policies test for Admin first, then Trainer, and treat "member" as holding `User`
and neither of the others. An account with no recognised role has no persona and sees only the
account links.

**The admin "Grafik" target depends on the viewport.** It resolves through
`mediaQuerySignal(DESK_MEDIA_QUERY, true)`, read in the shell component, not in the navigation table.
The table stays pure data plus a function of `(persona, desk)`. On the server and in jsdom the
fallback `true` yields `/admin/classes`. Specs that care stub `matchMedia` the way `classes.spec.ts:64-66`
does.

**Staff never see member data.** The dashboard must not fire `/api/bookings/mine`, `/api/plans/mine`
or `/api/passes/mine` for staff at all, not just hide their cards. The spec asserts this with
`HttpTestingController.expectNone`, following `dashboard.spec.ts:239`.

---

## Phase 1: Record the product decision

### Overview

The PRD, the roadmap and `AGENTS.md` are amended to state the persona model and name the decisions
it supersedes. Otherwise every later review would flag the code as drifting from the PRD.

### Changes Required

#### 1. PRD v2 amendment

**File**: `context/foundation/prd-v2.md`

**Intent**: Add a dated amendment and mark superseded requirements in place, using the supersession
note style of `prd.md:59-62` (a quoted "**Superseded by … (roadmap S-25), 2026-09-22.**" block).

**Contract**:
- A new section "Amendment: role-based visibility (S-25)" under `## Access Control Changes` (or next
  to it). It states:
  - the three personas and their precedence;
  - the access matrix from this plan's Desired End State;
  - the domain rule that staff may not receive a karnet, booking or plan;
  - the menu-reaches-every-page rule.
- Supersession notes on:
  - **FR-002** (roles additive, trainer keeps member capabilities). Record that the old objection,
    "a non-admin trainer gets an empty application", is answered by the trainer's schedule, booking
    UI, member list and dashboard.
  - **FR-018** (member schedule calendar).
  - The member-browsing statements at `:76-80, 98-99`.
  - **Non-Goal** `:441-442`: the parked trainer "my classes" screen is now delivered as the
    trainer's filtered schedule.
- Open Question at `:474-476` ("what does a trainer see after signing in?") answered with a pointer
  to the amendment.

#### 2. PRD v1 cross-references

**File**: `context/foundation/prd.md`

**Intent**: Add supersession notes to FR-007 (`:95-96`), FR-024 (`:133-134`) and success criterion
2 (`:36`) that point at the v2 amendment.

**Contract**: One quoted note per item. Nothing else in v1 changes.

#### 3. Roadmap entry

**File**: `context/foundation/roadmap.md`

**Intent**: Add `### S-25: Each role sees its own app, and the menu reaches every page` in the S-NN
block format of S-24 (`:759-782`), plus a row in `## At a glance` and the Backlog Handoff table.
Bump the frontmatter `updated`.

**Contract**:
- `Change ID: role-based-visibility`, `Status: planning`.
  **Adapted during implementation.** Written as `in-progress`: the item was added by `/10x-implement`,
  whose entry step flips the matching roadmap item to `in-progress`, so `planning` would have been
  stale on arrival.
- `Prerequisites`: S-16, S-22.
- `PRD refs`: the v2 amendment.
- `Milestone`: unassigned (as S-24).
- The M-7 "Not in scope" item "a role-aware bottom nav" (`:133-139`) and the Parked "Trainer screen
  ('my classes')" (`:831`) each get a one-line note pointing to S-25. Open Question 5 (`:821`) is
  marked answered.

#### 4. Contributor guidance

**File**: `AGENTS.md` (and a one-line pointer in `CLAUDE.md`'s rule list)

**Intent**: Add a hard rule about personas, so the next screen does not gate on `isActive()` alone.

**Contract**: One "Hard rules" bullet and a short "### Personas (S-25)" subsection under Style. It
states:
- the precedence;
- that a screen or endpoint picks exactly one persona predicate, identical in nav, guard and policy;
- where the predicate lives (`core/auth/persona.ts`, `core/layout/navigation.ts`, and
  `AuthorizationPolicies.cs` with `MemberOnly` and `TrainerOrAdmin`);
- that staff never hold a karnet, a booking or a plan.

### Success Criteria

#### Automated Verification

- No file under `context/archive/` is modified: `git diff --name-only | grep context/archive` prints nothing

#### Manual Verification

- The amendment reads as a complete statement of the access matrix without reference to this plan
- Every superseded requirement carries a note pointing to the amendment

**Implementation Note**: After this phase, pause for the human to confirm the documents before
touching code.

---

## Phase 2: API — access per persona

### Overview

Add the member-only policy, make `GET /api/classes` staff-only and persona-filtered, and add the
dashboard feed of the caller's instructed classes. Rewrite the tests that pinned the old matrix.

### Changes Required

#### 1. Policy names and roles

**File**: `src/Domain/AuthorizationPolicyNames.cs`, `src/Domain/ApplicationRoles.cs`

**Intent**: Declare the new policy name. Declare the staff role set as a named array next to
`MemberFacing`, so "staff" has one server-side spelling.

**Contract**:
- `AuthorizationPolicyNames.MemberOnly`.
- `ApplicationRoles.Staff = [Trainer, Admin]`.
- The doc comments on `ActiveMember` and `MemberFacing` state that no product route uses them after
  S-25, and why they remain (the Testing probe).

#### 2. MemberOnly policy

**File**: `src/Infrastructure/Authorization/AuthorizationPolicies.cs`

**Intent**: A policy that admits exactly the member persona.

**Contract**:
- The same two status claims as its siblings.
- `RequireRole(User)`.
- `RequireAssertion(ctx => !ApplicationRoles.Staff.Any(ctx.User.IsInRole))`.
- A failed assertion on an authenticated user yields 403 through the existing access-denied
  override. This is the repo's first `RequireAssertion`, so the comment says why a role set alone
  cannot express a negative.

#### 3. Member-only endpoint groups

**Files**: `src/Api/Endpoints/Members/MyPassEndpoints.cs`, `src/Api/Endpoints/Training/MyPlanEndpoints.cs`,
`src/Api/Endpoints/Scheduling/BookingEndpoints.cs` (the `/api/bookings/mine` group only)

**Intent**: Staff get 403 on their own pass, plan and bookings.

**Contract**: The group policy changes from `ActiveMember` to `MemberOnly`. Handlers are unchanged.

#### 4. The schedule becomes staff-only and persona-filtered

**Files**: `src/Api/Endpoints/Scheduling/ClassEndpoints.cs`,
`src/Application/Scheduling/GetSchedule.cs`, `src/Application/Scheduling/IClassScheduleQuery.cs`,
`src/Infrastructure/Scheduling/ClassScheduleQuery.cs`

**Intent**: A member is refused. An admin reads every class. A trainer without Admin reads only the
classes they instruct. The rule is the one `BookingAuthorization.MayActOn` already states.

**Contract**:
- The `/api/classes` group policy becomes `TrainerOrAdmin`.
- `GetSchedule.HandleAsync` gains a `ClaimsPrincipal`. When the principal is not Admin, it passes
  `principal.GetMemberId()` as an instructor filter.
- `IClassScheduleQuery` gains `GetForInstructorAsync(Guid instructorMemberId, DateTimeOffset from,
  DateTimeOffset to, CancellationToken)`, implemented through the existing private `ProjectAsync`
  with `c.InstructorMemberId == instructorMemberId` added to the scheduled-in-range predicate.
- A trainer principal with no `member_id` claim cannot pass `TrainerOrAdmin`, because the status
  claims come from the member row. The handler still treats a missing id as an empty list rather
  than as "all".
- Default window and `ClassRangeResolver` behaviour are unchanged.

#### 5. Instructed-classes feed for the dashboard

**Files**: new `src/Api/Endpoints/Scheduling/TrainerClassEndpoints.cs` (or a group added to
`ClassEndpoints.cs`, following the file-per-group convention used there), new
`src/Application/Scheduling/GetInstructedClasses.cs`

**Intent**: One feed answering "which classes do I teach in this window", for trainers and admins
alike. An Admin+Trainer gets their own classes and an admin who teaches nothing gets an empty list.

**Contract**:
- `GET /api/trainer/classes?from&to` under `TrainerOrAdmin`, returning `ScheduledClass[]`.
- Implemented with `GetForInstructorAsync(principal.GetMemberId(), …)` regardless of Admin.
- Range resolution uses `ClassRangeResolver` with the schedule defaults.
- Living under `/api/trainer/` puts it under `EndpointAuthorizationTests`'
  `Trainer_routes_require_the_trainer_or_admin_policy` (`:196`) automatically.

#### 6. Access-matrix tests

**File**: `tests/po-prostu-silka.Tests/EndpointAuthorizationTests.cs`

**Intent**: Pin the new matrix by hand, as the file's rule demands (`:20-24`), citing S-25 in the
comment.

**Contract**:
- A new hand-written `MemberOnlyRoutes` list: `/api/passes/mine`, `/api/plans/mine`,
  `/api/plans/mine/exercises/{exerciseId:guid}`, `/api/bookings/mine`.
- A test asserting each of them carries `MemberOnly`.
- A test asserting `GET /api/classes` carries `TrainerOrAdmin`.
- `Every_listed_route_exists` covers the new list.

#### 7. Behavioural tests

**Files**: `ClassEndpointTests.cs`, `BookingEndpointTests.cs`, `ClassCancellationTests.cs`,
`MyPlanEndpointTests.cs`, `MembershipPassEndpointTests.cs`, `MemberClaimTests.cs`, and a new or
extended test class for the feed

**Intent**: Move every schedule read from a member client to a trainer or admin client, and add the
refusals and filters the matrix promises.

**Contract**:
- Schedule reads by a `member` client are moved to an admin client: `ClassEndpointTests.cs:671, 710,
  740, 771, 779-786` (the malformed-range theory's `/api/classes` case), `:848`;
  `BookingEndpointTests.cs:759-772`; `ClassCancellationTests.cs:416`. Assertions are unchanged.
- New tests:
  - `A_member_is_refused_the_schedule`;
  - `A_trainer_reads_only_the_classes_they_instruct` (two classes, two instructors);
  - `An_admin_reads_every_class`;
  - `An_admin_who_is_also_a_trainer_reads_every_class`;
  - on the feed: `The_feed_returns_only_the_callers_instructed_classes`,
    `An_admin_who_teaches_nothing_gets_an_empty_feed`, `A_member_is_refused_the_feed`;
  - refusals: `A_trainer_is_refused_their_own_pass|plan|bookings` and
    `An_admin_is_refused_their_own_pass|plan|bookings` (403).
- An **Admin+Trainer fixture user** is added to `IntegrationTestFixture` (none exists today,
  `:123-137`, `TestUsers :459-469`).
- Grep the write tests for `FindMemberIdAsync(…, TestUsers.ActiveTrainerEmail|ActiveAdminEmail)`
  used as a *booking, pass or plan target*. Phase 3 refuses those, so any hit moves to a plain member
  there.

**Adapted during implementation.** The staff refusals on the four `/mine` routes live in one new
theory class, `PersonaAccessTests.cs` (every staff fixture user, the Admin+Trainer one included, ×
every own-data route, plus a member-admitted case), rather than as separate facts in
`MyPlanEndpointTests` / `MembershipPassEndpointTests`. The member refusal is one theory over both
routes in `ClassEndpointTests` (`A_member_is_refused_the_schedule_and_the_feed`). The extra
`NextSlot()` draws moved `Duplicate_skips_and_reports_the_colliding_week_and_creates_the_rest` onto
a fortnight crossing a DST change, exposing that it planted its collision with `AddDays(14)` (same
instant) while the duplicate counts club-local days; it now plants with `ClubTime.AddLocalDays`.

### Success Criteria

#### Automated Verification

- Backend builds warning-free: `dotnet build po-prostu-silka.slnx`
- All integration tests pass: `dotnet test po-prostu-silka.slnx`
- `EndpointAuthorizationTests` includes the `MemberOnly` route list and the `/api/classes` policy assertion

#### Manual Verification

- With the API running against Docker SQL, `GET /api/classes` returns 403 for a member, only own classes for a trainer, and everything for an admin (checked via the SPA dev proxy or an HTTP client with each account's cookie)

**Implementation Note**: Pause for manual confirmation after this phase. Do not merge until Phase 5
(see Critical Implementation Details).

---

## Phase 3: Domain — staff hold no karnet, booking or plan

### Overview

New karnets, bookings and plan assignments for staff are refused. The trainer's member list stops
offering staff. The admin member list stops offering Karnety/Plan on staff rows. The Staging seed
demonstrates the new dashboard.

### Changes Required

#### 1. One definition of "is staff"

**Files**: new `src/Infrastructure/Members/StaffPredicate.cs` (or a static member on an existing
Infrastructure/Members type), `src/Application/Members/IMemberStore.cs`,
`src/Infrastructure/Members/MemberStore.cs`

**Intent**: The correlated EXISTS from `TrainerQuery.cs:32-39`, widened to Trainer or Admin, lives
in one place. `IsStaffAsync` and `Assignable` both use it.

**Contract**:
- `IMemberStore.IsStaffAsync(Guid memberId, CancellationToken) → Task<bool>`.
- A member without an account is never staff.
- Role comparison uses `NormalizedName` over `ApplicationRoles.Staff`.

#### 2. Refusals

**Files**: `src/Application/Members/IssuePass.cs`, `src/Application/Members/MembershipPassFailure.cs`,
`src/Application/Scheduling/BookForMember.cs`, `src/Application/Scheduling/BookingFailure.cs`,
`src/Infrastructure/Training/TrainingPlanQuery.cs`, `src/Application/Training/ITrainingPlanQuery.cs`,
`src/Application/Training/TrainingPlanValidator.cs`, `src/Application/Training/TrainingPlanFailure.cs`

**Intent**: Each write path refuses a staff member with `409 member_is_staff`, placed next to its
existing `member_blocked` / `member_not_active` check.

**Contract**:
- **`IssuePass`**: after the `member_blocked` check (`:46-49`), before the overlap probe. The new
  reason goes in `MembershipPassFailure`.
- **`BookForMember`**: after `member_blocked` (`:70-73`) and **before** `BookingProtocol.TryBookAsync`.
  The protocol is not touched. The new reason goes in `BookingFailure`.
- **`TrainingPlanQuery.Assignable`**:
  - excludes staff;
  - its "NO ROLE FILTER … admins train too" comment (`:36-41`) is rewritten, citing S-25;
  - `IsAssignableAsync` returns a small result (NotFound / NotActive / Staff / Ok) instead of `bool?`;
  - `ValidateMemberAsync` maps Staff to `member_is_staff`, and the new reason goes in
    `TrainingPlanFailure`.
- `GetTrainerMembersAsync` inherits the exclusion. `FindMemberAsync` (`:79-84`, used by
  `GET /api/trainer/members/{id}/plan`) stays unfiltered, so an admin can still open a staff
  member's historic plan read-only.

#### 3. SPA failure contracts

**Files**: `src/app/src/app/core/admin/member-admin.models.ts` and `core/admin/membership-pass-failure.ts`;
`core/scheduling/booking.models.ts` and `core/scheduling/booking-failure.ts`;
`core/training/training-plan.models.ts` and `core/training/training-plan-failure.ts`

**Intent**: Mirror the new reason in all three unions, with a sentence each.

**Contract**:
- `member_is_staff` is added to each union, each `*_REASONS` object and each `MESSAGES` table.
- Suggested words:
  - pass: "Karnetu nie wystawia się trenerom ani administratorom.";
  - booking: "Trenerów i administratorów nie zapisuje się na zajęcia jako uczestników.";
  - plan: "Planu treningowego nie przypisuje się trenerom ani administratorom."
- Each sentence is distinct from its union's others, so `failure-contract.spec.ts` needs no new
  `deliberateDuplicates` entry. The union count stays 18.

#### 4. Admin member list

**File**: `src/app/src/app/features/admin/members/members.html` (+ `members.spec.ts`)

**Intent**: Staff rows stop offering actions the API now refuses.

**Contract**: The "Karnety" (`:163-170`) and "Plan" (`:175-182`) menu items are wrapped in
`@if (!isAdmin(member) && !isTrainer(member))`, following the block item at `:234`. The routes
themselves stay reachable by URL: the passes screen shows existing rows, and issuing is refused
with the new message.

#### 5. Tests

**Files**: `MembershipPassEndpointTests.cs`, `AdminBookingEndpointTests.cs`, `TrainingPlanEndpointTests.cs`,
`TrainerMemberEndpointTests.cs`, and SPA `members.spec.ts`, `class-bookings-overlay.spec.ts` (if it
asserts the full reason list)

**Contract**:
- New tests:
  - `Issuing_a_karnet_to_a_trainer_is_refused`, `…_to_an_admin_is_refused`;
  - `Booking_a_trainer_is_refused`, `Booking_an_admin_is_refused` (also for a trainer booking into
    their own class);
  - `Assigning_a_plan_to_a_trainer_is_refused`;
  - `Granting_trainer_to_a_member_with_a_karnet_still_succeeds` (grant stays allowed).
- `TrainerMemberEndpointTests.The_list_includes_trainers_and_admins_who_are_active_members`
  (`:184-205`) is inverted into `The_list_excludes_trainers_and_admins`.
- SPA: a members-list spec case asserting that staff rows carry no `passes` or `plan` link and that
  a member row carries both.

#### 6. Staging seed

**Files**: `src/Infrastructure/TestData/TestDataGenerator.cs`,
`tests/po-prostu-silka.Tests/TestDataSeederTests.cs`

**Intent**: `admin2` becomes Admin+Trainer and instructs part of the schedule, so the admin dashboard
and the precedence rule are visible on Staging. `admin1` stays the "teaches nothing" case.

**Contract**:
- `admin2` is created with `[Admin, Trainer]`.
- The instructor pick pool (`:399`) becomes `_trainers` plus `admin2`. This shifts later random
  draws, which the class doc allows (`:22-26`).
- `ClubMembers()` exclusion is unchanged, so staff still get no pass, booking or plan.
- `TestDataSeederTests.cs:99` expects 3 Trainer holders.

#### 7. Runbook clean-up query

**File**: `context/deployment/deploy-plan.md`

**Intent**: Let the operator find staff data that predates the rule.

**Contract**: A short subsection, "Staff holding member data (S-25)", with one read-only SQL query
listing members whose account holds Trainer or Admin **and** who have a pass with `ValidTo >=
today`, an active booking on a future class, or an active plan. It says that clean-up is manual in
the admin UI.

**Adapted during implementation.** The app has no route that ends a training plan (only create and
update), so the runbook says a staff-held plan stays in place, harmless because `/api/plans/mine`
refuses its holder. Bookings are released from the bookings overlay and karnets shortened or revoked
on the passes screen. In the tests, the staff refusals are theories over all three staff fixture
users (Trainer, Admin, Admin+Trainer). The booking refusal plants a karnet straight in the database
first, so `member_is_staff` cannot pass as `no_valid_pass`. `IsAssignableAsync` returns a new
`MemberAssignability` enum (`src/Application/Training/MemberAssignability.cs`).

### Success Criteria

#### Automated Verification

- Backend builds warning-free: `dotnet build po-prostu-silka.slnx`
- All integration tests pass, including the new refusal tests: `dotnet test po-prostu-silka.slnx`
- SPA unit tests pass, including `failure-contract.spec.ts` and `members.spec.ts`: `npm test` (from `src/app/`)
- Lint and format pass: `npm run quality:check` (from `src/app/`)

#### Manual Verification

- As admin on `/admin/members`: a trainer's row shows no Karnety/Plan items, and a plain member's row shows both
- Issuing a karnet to a staff member via `/admin/members/:id/passes` (typed URL) shows the new message in the form banner
- As a trainer on `/trainer/members`: neither the trainer themselves nor any admin appears
- With `TestDataSeed:Reset=true` on a local run, `admin2` holds Admin+Trainer and instructs some classes

**Implementation Note**: Pause for manual confirmation after this phase.

---

## Phase 4: SPA — personas, guards and navigation

### Overview

Add the persona function and the two persona guards, re-guard the routes, and drive the header, the
bottom bar and `/more` from one per-persona link table.

### Changes Required

#### 1. Persona

**File**: new `src/app/src/app/core/auth/persona.ts` (+ `persona.spec.ts`)

**Intent**: The SPA's single definition of the persona, the twin of the server's policies.

**Contract**:
- `type Persona = 'member' | 'trainer' | 'admin'`.
- `personaOf(user: CurrentUser | null): Persona | null` returns:
  - null for no user, or for a user who is not active (same test as `AuthService.isActive`);
  - 'admin' if roles include Admin;
  - else 'trainer' if they include Trainer;
  - else 'member' if they include User;
  - else null.
- Specs cover Admin-only (the seeded shape), User+Admin, User+Trainer, Admin+Trainer, User, blocked,
  and an empty role list.

#### 2. Persona guards

**Files**: new `src/app/src/app/core/auth/member.guard.ts`, `core/auth/staff.guard.ts` (+ specs)

**Intent**: Refuse a signed-in user whose persona does not own the route, redirecting to `/`, not to
`/login`.

**Contract**:
- Same shape as `trainer.guard.ts`:
  - true on the server;
  - awaits `loadCurrentUser()` when the session is unresolved;
  - then `personaOf(auth.user())`.
- `memberGuard` passes on `'member'`. `staffGuard` passes on `'trainer' | 'admin'`. Anything else
  goes to `UrlTree('/')`.
- Specs follow `trainer.guard.spec.ts:10-34`.

#### 3. Routes

**File**: `src/app/src/app/app.routes.ts`

**Intent**: Re-guard the persona-owned routes and update their comments, which currently state the
opposite ("every approved account has a plan surface, the trainer's own included", `:164-165`).

**Contract**:
- `schedule`: `[authGuard, staffGuard]`.
- `my-classes`, `my-plan`, `my-plan/exercises/:id`: `[authGuard, memberGuard]`.
- `''` keeps `[authGuard, activeMemberGuard]`, since every persona has a dashboard.
- Admin and trainer routes are unchanged.

#### 4. Navigation table

**File**: new `src/app/src/app/core/layout/navigation.ts` (+ `navigation.spec.ts`)

**Intent**: One table of links per persona that all three menus read, so header, bar and `/more`
cannot drift and each link's condition is its route's guard by construction.

**Contract**:
- `interface NavLink { route: string; label: string; icon: IconName; exact: boolean }`.
- `navigationFor(persona: Persona | null, desk: boolean): { header: NavLink[]; bar: NavLink[];
  more: NavLink[] }`, returning exactly the Desired End State table.
- The admin "Grafik" route is `desk ? '/admin/classes' : '/schedule'`.
- A null persona yields empty header, bar and more lists. The shell still renders Moje konto and
  logout for any authenticated user, as today.
- Moje konto and logout are not table entries: the shell and `/more` render them for every
  authenticated account (a blocked account included, per `app.html`'s existing comment).
- Specs:
  - each persona's exact lists, in order;
  - every `header` route appears in `bar ∪ more` (the phone reaches everything);
  - the bar never exceeds 5 entries;
  - the admin Grafik flips with `desk`.

#### 5. Icon

**File**: `src/app/src/app/shared/icons/icon.ts` (+ its SVG set)

**Intent**: Członkowie needs a tab icon. Ćwiczenia uses the existing `'hantle'`.

**Contract**: A new `IconName` `'members'` with an SVG drawn in the existing icons' stroke style.

#### 6. Shell header

**Files**: `src/app/src/app/app.html`, `app.ts`, `app.spec.ts`

**Intent**: The header renders the persona's `header` links, then Moje konto and logout. The stale
admin-links comment (`:16-19`) is replaced with one stating where the table lives.

**Contract**:
- `App` holds `desk = mediaQuerySignal(DESK_MEDIA_QUERY, true)` and
  `nav = computed(() => navigationFor(personaOf(auth.user()), desk()))`.
- The header `@for`s over `nav().header` with `routerLinkActive`. "Start" uses `exact`.
- The bottom bar receives `nav().bar`.
- `app.spec.ts`: the cases pinning Grafik/Zajęcia/Plan for every active account (`:168`, `:306`) are
  replaced by per-persona cases:
  - a member sees no `/schedule`;
  - a trainer sees no `/my-plan`;
  - an admin sees `/admin/members`, `/admin/class-types` and `/admin/exercises` in the header;
  - Admin+Trainer sees the admin set and no `/trainer/members`;
  - a blocked account sees only Moje konto and logout.
- The bar still renders only with a session (`:261-290`).

#### 7. Bottom bar

**Files**: `src/app/src/app/shared/bottom-nav/bottom-nav.ts`, `.html`, `bottom-nav.spec.ts`

**Intent**: The bar stays a component that cannot see a role. It renders whatever links it is given.

**Contract**:
- `links = input.required<readonly NavLink[]>()` replaces `BOTTOM_NAV_TABS`.
- The docblocks (`:14-25`, `:34-49`) are rewritten to say the *set* is now per persona, decided by
  the shell from `core/layout/navigation.ts`, while the component stays role-blind.
- The specs `'renders exactly the five declared tabs'` (`:46`) and `'carries no role-conditional
  destination'` (`:86`) are replaced by "renders the links it is given, in order". The aria-current,
  label and icon cases stay.

#### 8. `/more`

**Files**: `src/app/src/app/features/more/more.ts`, `more.html`, `more.spec.ts`

**Intent**: `/more` lists the persona's `more` links followed by Moje konto and logout. The
hand-written Panel conditions (`more.ts:42-53`) go away in favour of the table.

**Contract**:
- `more.ts` computes `navigationFor(personaOf(auth.user()), desk()).more`.
- The "Panel" heading renders only when that list is non-empty.
- `more.spec.ts` cases are rewritten per persona:
  - admin → Typy zajęć only;
  - member and trainer → no panel;
  - blocked → account and logout only.

### Success Criteria

#### Automated Verification

- SPA unit tests pass (persona, guards, navigation, app, bottom-nav, more): `npm test` (from `src/app/`)
- Lint and format pass: `npm run quality:check` (from `src/app/`)
- Production build succeeds within the bundle budget: `npm run build` (from `src/app/`) — record the initial-bundle size in the plan's Progress note, as AGENTS.md does for every eager-chunk change

#### Manual Verification

- At desktop width, as admin: the header shows Start, Grafik, Członkowie, Typy zajęć, Ćwiczenia, Moje konto, Wyloguj, and Grafik opens `/admin/classes`
- At phone width, as admin: the bar shows Start, Grafik, Członkowie, Ćwiczenia, Więcej, Grafik opens `/schedule`, and Więcej lists Typy zajęć
- As member: typing `/schedule` lands on `/`. As trainer or admin: typing `/my-plan` lands on `/`
- At 30–64rem as admin (header visible, below desk), Grafik opens `/schedule`, not the UX-01 refusal
- Header and bar are never both visible at any width

**Implementation Note**: Pause for manual confirmation after this phase.

---

## Phase 5: SPA — screens per persona

### Overview

The dashboard, the schedule and the member's list screens are adapted to their personas, and the
bookings overlay becomes the staff schedule's click action.

### Changes Required

#### 1. Move and parameterise the bookings overlay

**Files**: `src/app/src/app/features/admin/classes/class-bookings-overlay.*` → new
`src/app/src/app/features/class-bookings/class-bookings-overlay.*`;
`features/admin/classes/classes.html` / `classes.ts` (import path); new
`src/app/src/app/core/scheduling/booking-candidates.ts`

**Intent**: One overlay serves the admin calendar and the staff schedule. It stays under
`features/**`, so the presentational-kit lint still covers it (moving it to `shared/` would take it
out, per AGENTS.md). It lives in a neutral feature folder so neither screen imports from the other's
feature.

**Contract**:
- `core/scheduling/booking-candidates.ts` exports:
  - `interface BookingCandidate { id: string; displayName: string; hasAccount: boolean }`;
  - `type BookingCandidateSearch = (phrase: string) => Promise<BookingCandidate[]>`;
  - two factories: `adminCandidateSearch` over `MemberAdminService.getMembers({ filter: 'Active',
    search, pageSize: 20 })`, mapping `hasAccount = userId !== null`, and `trainerCandidateSearch`
    over `TrainingPlanService.getTrainerMembers({ search, pageSize: 20 })`.
- The overlay gains `search = input.required<BookingCandidateSearch>()` and drops its direct
  `MemberAdminService` injection. It reads `hasAccount` for the "— bez konta" suffix.
- The search placeholder becomes "Imię i nazwisko" for the trainer source. The overlay takes the
  placeholder with the search, or a second input.
- `/admin/classes` passes the admin search. `/schedule` passes the admin search for the admin
  persona and the trainer search for the trainer persona.
- Admin candidates may include staff rows. The server refuses them with `member_is_staff`, and the
  booking toast shows the Phase 3 sentence.
- `class-bookings-overlay.spec.ts` moves with it. Its admin-URL assertions (`:124`, `:271`, `:294`)
  test the admin factory, and a new case covers the trainer factory hitting `/api/trainer/members`.
- `BookingService`'s stale "Admin only" JSDoc (`booking.service.ts:40,48,63`) is corrected to "a
  trainer on their own classes, or an admin".

#### 2. Staff schedule

**Files**: `src/app/src/app/features/schedule/schedule.ts`, `schedule.html`, `schedule.spec.ts`;
delete `features/schedule/class-details-overlay/`

**Intent**: `/schedule` is now a staff screen. It shows what `GET /api/classes` returns for the
persona, and a selected tile opens the bookings overlay.

**Contract**:
- `load()` fetches only `getSchedule(from, to)`. The `getMine()` call, `bookedClassIds`, `isBooked`
  and the `booked` input go.
- `ClassDetailsOverlay` is deleted, since no persona uses it any more.
- Selecting a class opens `app-class-bookings-overlay`, with `released` and `booked` updating the
  row's free spots the way `classes.ts:345-370` does.
- The hint at `schedule.html:2` becomes staff wording, e.g. "Wybierz zajęcia, aby zobaczyć listę
  zapisanych i zapisać członka."
- The trainer persona gets a page subtitle saying the schedule shows only their classes.
- `schedule.spec.ts`:
  - drop the booked-set and "Zapisów dokonuje klub" cases (`:228`, `:243`);
  - add "a selected class opens the bookings overlay";
  - add "a trainer's overlay searches /api/trainer/members";
  - add "no request to /api/bookings/mine is made".

#### 3. Dashboard

**Files**: `src/app/src/app/features/dashboard/dashboard.ts`, `dashboard.html`, `dashboard.spec.ts`;
`src/app/src/app/core/scheduling/class.service.ts`

**Intent**: A member gets their three cards and nothing else. Staff get only "Twoje zajęcia", fed
by the new instructed-classes feed.

**Contract**:
- `ClassService.getInstructedClasses(from, to)` → `GET /api/trainer/classes`.
- `dashboard.ts`:
  - `persona = computed(() => personaOf(auth.user()))`;
  - `ngOnInit` fires `loadBookings`, `loadPlan` and `loadPass` only for `'member'`, and `loadClasses`
    only for `'trainer' | 'admin'`;
  - `loadClasses` calls `getInstructedClasses` over the unchanged `todayWindow()`;
  - the four fences stay four.
- `dashboard.html`:
  - the member section renders only for the member persona;
  - the staff section's heading becomes "Twoje zajęcia", keeping the "Dzisiaj" / "Nadchodzące" cards;
  - empty states: "Nie prowadzisz dziś zajęć." / "Nie prowadzisz zajęć w najbliższym tygodniu.";
  - "Zarządzaj zajęciami" becomes "Zobacz grafik", pointing at the persona's Grafik route from the
    navigation table (desk-aware), and renders regardless of whether the cards have rows.
- The member's "Najbliższe zajęcia" empty state (`:17-20`) drops the `/schedule` link and says that
  the club books classes, reusing `my-classes`' wording.
- The comment at `:3-4` ("the admin's included, because an admin books classes like anyone else")
  is rewritten.
- `dashboard.spec.ts`:
  - the admin case (`:253`) is replaced by admin and trainer cases that flush
    `/api/trainer/classes` and `expectNone` the three member URLs;
  - the member case keeps `expectNone` for `/api/trainer/classes` and `/api/admin/classes`;
  - the window-bucketing case (`:274`) moves to the new URL.

#### 4. Member's classes

**Files**: `src/app/src/app/features/my-classes/my-classes.html`, `my-classes.spec.ts`

**Intent**: Remove the link to a screen members can no longer open.

**Contract**: The empty state (`:21-25`) keeps its "Zapisy prowadzi klub — odezwij się w recepcji."
sentence and drops the `/schedule` link. A spec case asserts no `/schedule` link.

#### 5. E2E sanity

**File**: `src/app/e2e/guarded-route-redirects-to-login.spec.ts`

**Intent**: The spec opens `/my-classes` anonymously. The redirect to `/login` still holds
(`authGuard` runs first), so it stays valid. Re-run it, and leave it unchanged if green.

**Contract**: No change unless it fails. If it fails, switch the probe route to `/profile`, a route
every persona can open, and note why.

### Success Criteria

#### Automated Verification

- SPA unit tests pass (dashboard, schedule, class-bookings overlay, my-classes, classes): `npm test` (from `src/app/`)
- Lint and format pass, including the presentational-kit rule over the moved overlay: `npm run quality:check` (from `src/app/`)
- Production build succeeds within budget: `npm run build` (from `src/app/`)
- Backend suite still green: `dotnet test po-prostu-silka.slnx`
- E2E passes against the local stack: `npm run e2e` (from `src/app/`)

#### Manual Verification

- As member: the dashboard shows nearest bookings, karnet and plan, no link leads to a schedule, and the menu has no Grafik
- As trainer (seed `trener1`): the dashboard shows only "Twoje zajęcia" with their classes, `/schedule` shows only their classes, clicking one opens the roster, and booking a member works and updates free spots
- As trainer: the member picker offers no staff and searches by name only
- As `admin1` (teaches nothing): "Twoje zajęcia" shows its empty states, and "Zobacz grafik" opens the full calendar
- As `admin2` (Admin+Trainer): "Twoje zajęcia" shows their own classes, the header is the admin set, and `/schedule` below desk shows every class
- As admin: booking a trainer from the admin calendar shows the `member_is_staff` toast
- Browser network tab: no request to `/api/*/mine` while signed in as staff, and none to `/api/classes` or `/api/trainer/classes` as a member

**Implementation Note**: Pause for manual confirmation. Then merge Phases 2–5 together.

---

## Testing Strategy

### Unit Tests (SPA, Vitest)

- `personaOf`: every role combination, including Admin-only and the empty set.
- Guards: pass and redirect-to-`/` per persona, server-platform pass-through.
- `navigationFor`: exact lists per persona, bar ≤ 5, header ⊆ bar ∪ more, desk flip.
- Shell, bar and `/more`: the rendered links per persona, and a blocked account.
- Dashboard: which requests fire per persona (`expectNone` for the forbidden ones), today/upcoming
  bucketing on the new URL.
- Schedule: no member-data request, tile opens the overlay, trainer search source.
- Failure contract: the three new reasons have distinct, non-fallback sentences.

### Integration Tests (API, real SQL Server via Testcontainers)

- Policies: `MemberOnly` on the four `/mine` routes, `TrainerOrAdmin` on `/api/classes` and
  `/api/trainer/classes`, written by hand in `EndpointAuthorizationTests`.
- Behaviour:
  - schedule filtering per persona, including Admin+Trainer;
  - feed returns only instructed classes;
  - 403s on `/mine` for trainer and admin;
  - `member_is_staff` on pass, booking and plan;
  - trainer member list excludes staff;
  - granting Trainer to a member with a karnet still succeeds.
- Seed: trainer-holder count 3.

### Manual Testing Steps

1. `docker compose up -d`, run the API with `TestDataSeed:Enabled=true` and `Reset=true` once, then
   switch Reset off.
2. Walk the access matrix as `czlonek001`, `trener1`, `admin1` and `admin2` at 360px, 800px and
   1280px widths.
3. As `trener1`, book a member with a valid karnet into an own class. Try booking a member without
   one and expect the `no_valid_pass` toast.
4. As `admin1`, try issuing a karnet to `trener1` via a typed URL and expect the `member_is_staff`
   banner.

## Performance Considerations

- The instructor filter adds one equality predicate to an already range-bounded query. No index change
  is required at club scale. `Classes.InstructorMemberId` is an FK and SQL Server indexes FKs only if
  EF created the index. Check the model snapshot, and add an index only if the query plan shows a
  scan over more than the range.
- `IsStaffAsync` is one EXISTS round trip on three already-rare write paths.
- The eager bundle gains `persona.ts`, `navigation.ts` and a guard, a few hundred bytes. Record the
  measured size.

## Migration Notes

- No schema change and no data migration.
- Existing staff-held passes, bookings and plans remain. Their holders can no longer see them, and
  the admin can release or end them through the existing screens. `deploy-plan.md` gets the query
  that finds them.
- Staging needs one `TestDataSeed:Reset=true` run for `admin2`'s new role and classes.
- A signed-in user whose persona changes (Trainer granted or revoked) sees the new menu after their
  session refreshes. This matches every existing role change.

## References

- Research: `context/changes/role-based-visibility/research.md`
- Ownership rule reused for filtering: `src/Application/Scheduling/BookingAuthorization.cs:49-51`
- Role EXISTS pattern: `src/Infrastructure/Members/TrainerQuery.cs:23,32-39`
- Desk-aware precedent: `src/app/src/app/features/admin/classes/classes.ts:153-163`, `classes.html:73-86`
- Hide-per-role precedent in the member list: `src/app/src/app/features/admin/members/members.html:234`
- Superseded design: `context/archive/2026-09-06-member-and-admin-dashboards/plan.md:89-92, 201-217`
- Nav-equals-guard rule: `context/archive/2026-09-01-registration-and-approval/reviews/impl-review.md:125-133`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Record the product decision

#### Automated

- [x] 1.1 No file under `context/archive/` is modified — ec34b23

#### Manual

- [ ] 1.2 The amendment reads as a complete statement of the access matrix without reference to this plan
- [ ] 1.3 Every superseded requirement carries a note pointing to the amendment

### Phase 2: API — access per persona

#### Automated

- [x] 2.1 Backend builds warning-free — 52e962d
- [x] 2.2 All integration tests pass — 52e962d
- [x] 2.3 `EndpointAuthorizationTests` includes the `MemberOnly` route list and the `/api/classes` policy assertion — 52e962d

#### Manual

- [ ] 2.4 `GET /api/classes` returns 403 for a member, only own classes for a trainer, and everything for an admin

### Phase 3: Domain — staff hold no karnet, booking or plan

#### Automated

- [x] 3.1 Backend builds warning-free
- [x] 3.2 All integration tests pass, including the new refusal tests
- [x] 3.3 SPA unit tests pass, including `failure-contract.spec.ts` and `members.spec.ts`
- [x] 3.4 Lint and format pass

#### Manual

- [ ] 3.5 A trainer's row on `/admin/members` shows no Karnety/Plan items, and a plain member's row shows both
- [ ] 3.6 Issuing a karnet to a staff member via a typed URL shows the new message
- [ ] 3.7 `/trainer/members` lists neither the trainer nor any admin
- [ ] 3.8 After a seed reset, `admin2` holds Admin+Trainer and instructs some classes

### Phase 4: SPA — personas, guards and navigation

#### Automated

- [ ] 4.1 SPA unit tests pass (persona, guards, navigation, app, bottom-nav, more)
- [ ] 4.2 Lint and format pass
- [ ] 4.3 Production build succeeds within the bundle budget, with the size recorded

#### Manual

- [ ] 4.4 Admin desktop header shows the full admin set, and Grafik opens `/admin/classes`
- [ ] 4.5 Admin phone bar shows Start, Grafik, Członkowie, Ćwiczenia, Więcej, Grafik opens `/schedule`, and Więcej lists Typy zajęć
- [ ] 4.6 A member typing `/schedule` and staff typing `/my-plan` each land on `/`
- [ ] 4.7 At 30–64rem the admin's Grafik opens `/schedule`
- [ ] 4.8 Header and bar are never both visible

### Phase 5: SPA — screens per persona

#### Automated

- [ ] 5.1 SPA unit tests pass (dashboard, schedule, class-bookings overlay, my-classes, classes)
- [ ] 5.2 Lint and format pass, including the moved overlay
- [ ] 5.3 Production build succeeds within budget
- [ ] 5.4 Backend suite still green
- [ ] 5.5 E2E passes against the local stack

#### Manual

- [ ] 5.6 Member dashboard shows three cards and no schedule link anywhere
- [ ] 5.7 Trainer dashboard and schedule show only own classes, and booking from the schedule works
- [ ] 5.8 Trainer member picker offers no staff and searches by name only
- [ ] 5.9 `admin1` sees the empty "Twoje zajęcia" and "Zobacz grafik" opens the full calendar
- [ ] 5.10 `admin2` sees own classes on the dashboard, the admin header, and every class on `/schedule` below desk
- [ ] 5.11 Booking a trainer from the admin calendar shows the `member_is_staff` toast
- [ ] 5.12 No `/api/*/mine` request as staff, and no schedule or feed request as a member
