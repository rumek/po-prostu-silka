---
date: 2026-09-21T18:40:24+02:00
researcher: Karol Rumianowski (with Claude)
git_commit: d1fa6e80f0286c460cb0e848f90ff259f0a261b0
branch: main
repository: po-prostu-silka
topic: "S-22 member-centric-training-plans — how a training plan is reached, authorized and displayed today, and what moving it under the member touches"
tags: [research, codebase, training-plans, members, authorization, trainer, plan-builder, navigation]
status: complete
last_updated: 2026-09-21
last_updated_by: Karol Rumianowski (with Claude)
---

# Research: S-22 — a member's plan is reached through the member

**Date**: 2026-09-21T18:40:24+02:00
**Researcher**: Karol Rumianowski (with Claude)
**Git Commit**: d1fa6e80f0286c460cb0e848f90ff259f0a261b0
**Branch**: main
**Repository**: po-prostu-silka

## Research Question

The roadmap defines S-22 (`context/foundation/roadmap.md:695-722`, anchors UX-07/UX-08 at `:120-127`).
An admin sets a member's training plan by opening Członkowie, then the member, then Plan, at
`/admin/members/:id/plan`, which mirrors `/admin/members/:id/passes`. A trainer does the same from a
member list of their own. `/trainer/plans` and the "Plany" entry on `/more` are retired.

This research maps what exists today:
- the plan backend: model, endpoints and authorization;
- the SPA surfaces: routes, guards, the plan list, the builder, the member list and the passes precedent;
- what member data a trainer can currently reach;
- which tests and specs pin the current shape;
- which prior decisions constrain the two open unknowns: what a trainer may see, and whether the builder route moves.

## Summary

- **The backend is nearly ready for a by-member read.** `ITrainingPlanQuery.FindActiveForMemberAsync(memberId)`
  (`src/Application/Training/ITrainingPlanQuery.cs:27`) already exists and backs `/api/plans/mine`. No
  HTTP route takes a member id yet.
- **Creating a plan replaces the old one; it never conflicts.** A new plan archives the member's
  current active plan (`CreateTrainingPlan.cs:78-89`). A filtered unique index enforces one active
  plan per member (`TrainingPlanConfiguration.cs:67-71`).
- **`member_changed` is ready to act as a URL/body check.** It is already raised on PUT when the body's
  `MemberId` differs from the stored one (`UpdateTrainingPlan.cs:54-57`).
- **Plans have no ownership rule, by design.** Any Trainer or Admin may create or edit any plan for
  any active member (`TrainingPlanEndpoints.cs:20-27`). `AssignedByMemberId` is display-only. The
  only ownership check in the codebase is class-scoped: `BookingAuthorization.MayActOn`
  (`src/Application/Scheduling/BookingAuthorization.cs:49-51`). "A trainer's members" does not exist
  as a concept or a query.
- **The member API is Admin-only as a whole group, so it cannot be opened to trainers route by route.**
  - The policy is set on the whole `/api/admin/members` group (`MemberAdminEndpoints.cs:57-59`).
  - Every row carries `Email`, `UserId`, statuses, `Roles` and `HasAccessCode` (`MemberSummary.cs:32-41`).
    The detail adds phone and postal address (`MemberDetail.cs:16-29`).
  - Widening the group would expose all of that, and every write route with it.
  - The precedent is a separate, minimized projection under `/api/trainer/...`: `AssignableMember(Id,
    DisplayName, HasAccount)`, whose "no email" is pinned by a raw-body test.
- **The SPA has no member-detail screen.** `/admin/members/:id` is the edit form (`MemberForm`).
  - A row reaches its record only through the "Akcje" row menu (`members.html:128-245`). "Karnety" is
    one of its items (`:163-170`), and "Plan" would be a sibling.
  - The passes screen (`member-passes.ts/html`) is the template to copy: it reads `:id` from a snapshot,
    fetches the member for the name, uses one load fence, has a `.panel` header, and a plain
    "Wróć do listy członków" back link.
- **`trainerGuard` already admits admins** (`core/auth/trainer.guard.ts:30`).
  - Today every trainer-facing SPA surface is plans: `/trainer/plans*`.
  - The trainer's API rights to rosters and booking exist, but no SPA screen uses them. The booking
    picker calls the Admin-only members endpoint (`class-bookings-overlay.ts:180-187`).
- **Retiring the list breaks known tests and specs.** Details are listed under "What the change
  breaks" below. The largest are `EndpointAuthorizationTests` (the `TrainerAdminRoutes` allowlist
  and the `NotEmpty` check over `/api/trainer/*`), `MyPlanEndpointTests:397`, which reads the retired
  list, and the `app.spec.ts` and `more.spec.ts` nav matrices.

## Detailed Findings

### Plan domain and persistence

- `TrainingPlan` (`src/Domain/Training/TrainingPlan.cs:26-124`) has these fields:
  - `MemberId`, a **Member** id, not an account id (`:36-40`);
  - `AssignedByMemberId`, which is "DISPLAY ONLY, NOT AN AUTHORIZATION BOUNDARY" (`:52-59`);
  - `Status` (Active=0 / Archived=1, pinned values), `ArchivedAt`;
  - `ConcurrencyStamp`, a concurrency token (`:108`);
  - `Items`, replaced wholesale on each write (`:123`).
- `TrainingPlanItem` (`TrainingPlanItem.cs:21-127`) has `Position` taken from array order. Its item ids
  are not stable across edits.
- One active plan per member is enforced by `IX_TrainingPlans_MemberId_Active`, a unique index filtered
  on `[Status] = 0` (`src/Infrastructure/Persistence/Configurations/TrainingPlanConfiguration.cs:67-71`).
  - Create archives the current plan and inserts the new one inside a retry loop (`CreateTrainingPlan.cs:74-122`).
  - Comments in `CreateTrainingPlan.cs:24,111` and `TrainingPlanEndpointTests.cs:13,313` misname the
    index `IX_TrainingPlans_Member_Active`. This is cosmetic drift.

### Plan endpoints (`src/Api/Endpoints/Training/TrainingPlanEndpoints.cs:51-65`, group policy `TrainerOrAdmin`)

| Verb / route | Handler | Returns |
| --- | --- | --- |
| `GET /api/trainer/plans` | `GetTrainingPlans` | `TrainingPlanSummary[]`: every active plan in the club, unpaged, ordered by member name |
| `GET /api/trainer/plans/members` | `GetAssignableMembers` | `AssignableMember(Id, DisplayName, HasAccount)` for the whole club, unpaged, with the `Assignable` predicate (`TrainingPlanQuery.cs:58-60`) |
| `GET /api/trainer/plans/{id}` | `GetTrainingPlan` | `TrainingPlanDetail` (active or archived) or 404 |
| `POST /api/trainer/plans` | `CreateTrainingPlan` | `TrainingPlanDetail`. The body is `TrainingPlanRequest(Name, MemberId, Items)`, and the author comes from the `member_id` claim |
| `PUT /api/trainer/plans/{id}` | `UpdateTrainingPlan` | `TrainingPlanDetail`. 404 if the plan is archived or missing; 409 `member_changed` |
| `GET /api/plans/mine` (`ActiveMember`, `MyPlanEndpoints.cs:32-37`) | `GetMyPlan` | the caller's plan, or 204 |

- There is no DELETE; `TrainingPlanEndpointTests.cs:844` pins the 405.
- The failure body is `TrainingPlanFailure(Reason)` (`TrainingPlanFailure.cs:32`). Its 409 reasons are
  `member_not_found`, `member_not_active`, `member_changed` and `conflict`.
- Update checks run in this order: shape → find (404) → archived (404) → member match → exercises.
- **Why plans sit under `/trainer`:** the URL should say who can use it, and `/admin/*` keeps meaning
  admin-only (`context/archive/2026-09-04-training-plans/plan-brief.md:38`).

### Authorization

- Policies are defined in `src/Infrastructure/Authorization/AuthorizationPolicies.cs:55-82`.
  - Every policy requires an active account and an active membership.
  - `TrainerOrAdmin` adds `RequireRole(Trainer, Admin)`.
  - Registration is at `src/Api/Program.cs:147`.
  - Roles are additive: a real trainer also holds User (`src/Domain/ApplicationRoles.cs`, `IntegrationTestFixture.cs:127-137`).
- The only resource-level check is `BookingAuthorization.MayActOn(principal, class)`. Admins pass it;
  trainers pass only when `GetMemberId() == InstructorMemberId`, and a refusal is 403.
  - Each handler calls it inline (`GetClassBookings.cs:103`, `BookForMember.cs`, `ReleaseBooking.cs:55`).
  - There is no `IAuthorizationHandler`.

### What member data a trainer can reach today

| Endpoint | Scope | Member fields |
| --- | --- | --- |
| `GET /api/trainer/plans/members` | whole club | id, name, hasAccount. Email is pinned absent (`TrainingPlanEndpointTests.cs:471`) |
| `GET /api/trainer/plans` | whole club | member id and name, author name |
| `GET /api/admin/classes/{id}/bookings` | own classes only | `ClassBooking(BookingId, MemberId, UserId, DisplayName, Email, BookedAt)` (`ClassBooking.cs:146-152`). **Email and account id are exposed and pinned by no test.** This is Open Roadmap Question 8 (`roadmap.md:798`). The same projection feeds the notification fan-out (`IBookingQuery.cs:30-48`) |
| `GET /api/admin/members*` | — | 403 (`MemberAdminEndpointTests.cs:105`, `:139-165`) |

- **Nothing scopes members to a trainer.** There is no "members of classes I instruct" query and no
  "plans I authored" filter. A grep for `InstructorMemberId ==` or `AssignedByMemberId ==` in
  Application or Infrastructure finds nothing.
- The pieces to build one exist: `Bookings ⋈ Classes` on `InstructorMemberId` (`BookingQuery`), or
  `TrainingPlans.AssignedByMemberId`.
- S-11 and S-16 both explicitly refused a trainer↔member relationship
  (`archive/2026-09-09-membership-pass-and-staff-booking/plan.md:106-107`, `plan-brief.md:55`).

### Members API (after S-21)

- `GET /api/admin/members?filter&search&page&pageSize`: `GetMembers.HandleAsync` (`src/Application/Members/GetMembers.cs:42-70`).
  - Page size defaults to 25, with a maximum of 100 and a maximum search length of 100.
  - A bad page returns 400 `invalid_page`; an overlong search returns 400 `invalid_search`.
  - The response is `PagedResult<T>(Items, Total, Page, PageSize)` (`src/Application/Paging/PagedResult.cs:19`),
    built for reuse.
- `MemberQuery.GetMembersAsync` (`src/Infrastructure/Members/MemberQuery.cs:56-122`):
  - `Filtered` (`:205-231`) handles the filter;
  - `Searched` (`:248-263`) matches name or email with collation `Latin1_General_100_CI_AI`, folds
    `ł`/`Ł` on both sides, and escapes `%` and `_`;
  - results are ordered by `DisplayName, Id`.
  - A trainer member list could reuse `Searched`/`Filtered` with a narrower projection.
- The group routes are all Admin (`MemberAdminEndpoints.cs:64-78`): `GET/PUT /{id}`, `POST /`,
  block/unblock, trainer-role grant/revoke, and access-code GET/POST/DELETE.
- Passes live at `/api/admin/members/{memberId:guid}/passes` and are Admin (`MembershipPassEndpoints.cs:43-50`).

### SPA routes and guards (`src/app/src/app/app.routes.ts`)

| Path | Line | Component | Guards |
| --- | --- | --- | --- |
| `admin/members` | 55 | `Members` (eager) | auth, admin |
| `admin/members/new` | 59-63 | `MemberForm` | auth, admin |
| `admin/members/:id/passes` | 67-72 | `MemberPasses` | auth, admin |
| `admin/members/:id` | 73-77 | `MemberForm` (edit) | auth, admin |
| `trainer/plans` | 137-141 | `Plans` | auth, trainer |
| `trainer/plans/new` | 143-147 | `PlanBuilder` | auth, trainer |
| `trainer/plans/:id` | 148-152 | `PlanBuilder` | auth, trainer |
| `my-plan`, `my-plan/exercises/:id` | 155-165 | `MyPlan`, `PlanExerciseDetail` | auth, activeMember |
| `more` | 183-187 | `More` | auth |

- A new `admin/members/:id/plan` must be declared **before** `admin/members/:id` (comments at `:56-58`, `:64-66`).
- `@angular/cdk/drag-drop` must stay in the builder's lazy chunk (`:134-136`).
- `trainerGuard` checks `(isTrainer() || isAdmin()) && isActive()` (`core/auth/trainer.guard.ts:30`),
  pinned by `trainer.guard.spec.ts:54`.
- `adminGuard` checks `isAdmin() && isActive()` (`admin.guard.ts:29`).
- SSR prerenders `**`, and every guard returns `true` on the server (`app.routes.server.ts:3-8`).

### The plan list screen (to be retired): `features/trainer/plans/plans.{ts,html,scss}`

- It loads `GET /api/trainer/plans` behind one load fence and filters on member or plan name on the
  client (`plans.ts:51-65`, `:83`).
- **The first line of each row is `memberDisplayName`** (`plans.html:45`). This is the "member list in
  disguise" UX-08 names.
- Actions are "Nowy plan" and "Otwórz" only (`plans.html:1-4`, `:54`).
- The search box is a hand-rolled `<label class="plans-search">` (`plans.html:10-21`). It disappears
  with the screen.

### The plan builder: `features/trainer/plans/plan-builder.{ts,html}`

- **Create or edit** is decided by `paramMap.get('id')` (`plan-builder.ts:194-199`). Edit loads through
  `getById` (`:203`).
- **The member picker** is fed by `getAssignableMembers()`, which is `GET /api/trainer/plans/members`.
  It is loaded without awaiting (`:191`, `:233-242`) and rendered as
  `app-select > select#plan-member` (`html:53-70`).
- **On edit the member is locked:**
  - `memberId.disable()` (`:208`);
  - the name comes from the loaded plan, not the picker, because a blocked member is missing from the
    picker (`:147-152`, `:209`);
  - it renders as static text with `span[slot=label]` (`html:35-52`);
  - `getRawValue()` still sends the id (`:320-323`).
- **Every exit is hard-coded to `/trainer/plans`:** post-save `router.navigate` (`:339`), "Wróć do listy"
  (`html:3`), the load-failure link (`html:11`), and cancel (`html:256`).
- **Failure display** (`applyFailure`, `:356-393`):
  - a transport failure goes to the banner;
  - `name_too_long` goes to the name field;
  - `member_not_*` goes to the member control and the banner;
  - `member_changed`, `conflict` and the per-item reasons show in the banner only.
- **Latent bug:** `html:76` tests `hasError('memberUnavailable')`, but nothing sets that key; `reject`
  sets `server`. After a `member_not_*` refusal the field shows its generic "required" text, and the
  banner carries the real reason. Verified by grep: it is the only occurrence.
- Drag-reordering is pointer-only, an accepted gap (`plan-builder.ts:79-82`, `html:101-102`), and is out
  of M-7 by the roadmap (`roadmap.md:133-135`).
- The failure words come from `core/training/training-plan-failure.ts` (`MESSAGES` `:16-46`, with
  `member_changed` at `:43-44`). They are registered in `core/http/failure-contract.spec.ts:94-102`.

### Admin member list and the passes precedent

- The list (`features/admin/members/members.{ts,html}`):
  - its state lives in the URL (`q`, `filter`, `page`);
  - it is a semantic `<table>` that collapses to compact rows below `form-columns`;
  - the name cell is not a link;
  - the "Akcje" row menu (`members.html:128-245`) holds "Edytuj dane" (`:151-158`),
    **"Karnety" (`:163-170`)**, the access-code actions, the trainer toggle, and block/unblock;
  - `members.spec.ts:276` pins that every row action stays reachable.
- The passes screen (`features/admin/members/member-passes.{ts,html}`) is the shape UX-07 mirrors:
  - the docblock at `:34-55` explains why it is a screen and not a section of the form;
  - it reads `route.snapshot.paramMap.get('id')` and sets `loadFailed` when the id is null (`:106-113`);
  - it fetches `getMember(id)` only for the name, and a failure there does not fail the screen (`:117-121`);
  - `load()` runs behind a fence (`:126-152`);
  - the header is `<div class="panel"><h1>Karnety — {name}</h1>`, not `.page-header` (`html:1-2`);
  - the back link goes to `/admin/members` and **drops** the list's `q`/`filter`/`page` state (`html:4-6`);
  - the S-16 design contract is at `archive/2026-09-09-membership-pass-and-staff-booking/plan.md:525-550`;
  - its spec, `member-passes.spec.ts:60`, `:69-70`, is the template for a member-plan spec.
- `/admin/members/:id` (`member-form.ts`) is an edit form under a `.panel` header. It links neither to
  passes nor to a plan.

### Navigation

- `/more` (`features/more/more.html`):
  - the Panel section shows when `canSeePanel()` is true, meaning trainer-or-admin and active (`:15-50`);
  - the admin-only entries are Członkowie, Zajęcia, Typy zajęć and Ćwiczenia (`:20-39`);
  - **"Plany" → `/trainer/plans` is gated on `isTrainerOrAdmin()`** (`:43-47`);
  - the conditions must match the guards exactly (`more.ts:17-20`, `:37-47`; the S-01 F5 bug class).
  - **Once Plany is removed, a plain trainer's Panel section is empty.** It must either get a new
    entry (the trainer's member list) or be hidden.
- Header nav (`app.html`) shows **"Plany" → `/trainer/plans` to trainer-or-admin** (`:32-34`) and has
  no Członkowie link. The roadmap names only `/more` for removal, but this header link targets the
  same retired route.
- The bottom nav is role-blind by design and pinned (`shared/bottom-nav/bottom-nav.spec.ts:86`). S-22
  does not touch it; a role-aware bottom nav is out of M-7 (`roadmap.md:136-138`).

### SPA services

- `core/training/training-plan.service.ts` has these methods: `getAll` (`:34`), `getAssignableMembers`
  (`:44`), `getById` (`:49`), `create` (`:59`), `update` (`:64`), `getMine` (`:82`, where 204 becomes
  null) and `getMyExercise` (`:95`). **It has no by-member lookup.**
- `core/admin/member-admin.service.ts` wraps the Admin-only member API: `getMembers` (`:48-68`),
  `getMember` (`:72`), and the passes methods (`:185-226`). A trainer can call none of it.

## What the change breaks (tests and specs pinned to today's shape)

### Backend (`tests/po-prostu-silka.Tests/`)

- `EndpointAuthorizationTests.cs`:
  - `TrainerAdminRoutes` (`:71-78`) is the allowlist for `/api/admin/*`. Any trainer-reachable
    `/api/admin/members/...` route must be added to it with a cited decision.
  - `Trainer_routes_require_the_trainer_or_admin_policy` (`:196`) runs `Assert.NotEmpty` over the
    `/api/trainer/*` routes. **If every `/api/trainer/` route is removed, it fails.**
  - `Every_listed_route_exists` (`:115`).
- `TrainingPlanEndpointTests.cs`: `EveryRoute` (`:110-117`/`:144-151`) includes `GET /` and `GET /members`.
  `The_list_counts_items_without_returning_them` (`:821`) and the picker tests (`:447`, `:471`, `:490`)
  depend on routes that may be retired.
- `MyPlanEndpointTests.cs:384-398`: `Blocking_refuses_the_member_but_leaves_the_plan_standing` **reads the
  retired list** `GET /api/trainer/plans`.
- `MemberClaimTests.cs:176`, `:521-533` seeds plans through `POST /api/trainer/plans`. That survives if
  POST is kept.
- `MemberAdminEndpointTests.cs:119-165` (`EveryAdminRoute` refuses a trainer) and
  `MembershipPassEndpointTests.cs:457-493` pin that a trainer is refused on member and pass routes.
- The retired-route test rules are in `context/foundation/test-plan.md:155-176`.

### SPA

- `src/app/app.spec.ts:128-238`: the header "Plany" link matrix, which queries `a[href="/trainer/plans"]`.
- `features/more/more.spec.ts:92-137`: the role matrix (a trainer sees Plany "and nothing else"; an admin
  sees `ADMIN_HREFS` plus `/trainer/plans`).
- `features/trainer/plans/plans.spec.ts`: removed with the screen.
- `features/trainer/plans/plan-builder.spec.ts`:
  - it pins the API URLs (`:108`, `:240`, `:272`, `:306`) and the static member text on edit (`:322-333`);
  - it uses `provideRouter([])` and **does not assert the post-save target**, so moving the route does
    not break it.
- `features/admin/members/members.spec.ts:276` needs updating if a "Plan" menu item is added.

## Code References

- `src/Domain/Training/TrainingPlan.cs:36-59` - member id vs author; the author is display-only
- `src/Infrastructure/Persistence/Configurations/TrainingPlanConfiguration.cs:67-71` - one active plan per member
- `src/Api/Endpoints/Training/TrainingPlanEndpoints.cs:20-27` - "THERE IS NO OWNERSHIP RULE"
- `src/Api/Endpoints/Training/TrainingPlanEndpoints.cs:51-65` - plan route group, `TrainerOrAdmin`
- `src/Application/Training/ITrainingPlanQuery.cs:27` - `FindActiveForMemberAsync(memberId)`, reusable for a by-member read
- `src/Application/Training/CreateTrainingPlan.cs:74-122` - archive-and-insert with retry
- `src/Application/Training/UpdateTrainingPlan.cs:54-57` - `member_changed`
- `src/Application/Training/AssignableMember.cs:22` - minimized trainer projection
- `src/Application/Training/GetAssignableMembers.cs:22-30` - why the admin list was not loosened
- `src/Api/Endpoints/Members/MemberAdminEndpoints.cs:57-59` - member group policy `Admin`
- `src/Application/Members/MemberSummary.cs:32-41`, `MemberDetail.cs:16-29` - member payloads
- `src/Infrastructure/Members/MemberQuery.cs:205-263` - `Filtered` / `Searched`, reusable for a trainer list
- `src/Application/Scheduling/BookingAuthorization.cs:49-51` - the only ownership check
- `src/Application/Scheduling/ClassBooking.cs:146-152` - roster rows expose email to trainers (Q8)
- `tests/po-prostu-silka.Tests/EndpointAuthorizationTests.cs:71-78`, `:163`, `:196` - access-surface pins
- `src/app/src/app/app.routes.ts:55-77`, `:134-152` - member and plan routes
- `src/app/src/app/core/auth/trainer.guard.ts:30` - trainerGuard admits admins
- `src/app/src/app/features/trainer/plans/plan-builder.ts:194-209`, `:339`, `:356-393` - mode, lock, exits, failures
- `src/app/src/app/features/trainer/plans/plan-builder.html:76` - dead `memberUnavailable` check
- `src/app/src/app/features/admin/members/members.html:163-170` - "Karnety" row-menu item (precedent)
- `src/app/src/app/features/admin/members/member-passes.ts:34-152` - the screen to mirror
- `src/app/src/app/features/more/more.html:43-47`, `src/app/src/app/app.html:32-34` - the two "Plany" nav entries

## Architecture Insights

- **One policy per route group** is the codebase's way to keep an endpoint from shipping under the wrong
  policy (`MemberAdminEndpoints.cs:55-56`). Opening something to trainers has meant a **new group with a
  minimized projection** (`/api/trainer/plans/members`), or a group split into a readable half and a
  write half (the exercise library in S-11). Loosening an Admin group has never been the method.
  A trainer member list therefore most naturally belongs under `/api/trainer/...`, which needs no
  allowlist edit and keeps `Trainer_routes_require…`'s `NotEmpty` satisfied.
- **URLs say who can use them.** `/admin/*` means admin-only (S-11, `plan-brief.md:38`). UX-07 fixes the
  admin route as `/admin/members/:id/plan`, so the trainer's way into the same builder needs its own
  URL or a route outside `/admin`. That shapes the builder-route unknown.
- **A resource-level check is inline in the handler, returns 403, and applies to trainers only**
  (`BookingAuthorization`). If a trainer's list is scoped, for example to members of their classes,
  this is the pattern to copy. It would be the first *member-scoped* check, and it would reverse the
  explicit "no trainer↔member relationship" stance of S-11 and S-16. That would be a decision, not an
  implementation detail.
- **A payload shown to trainers is pinned on the raw body** (`TrainingPlanEndpointTests.cs:471`), not
  only by status code. A new trainer member list should carry the same kind of test.
- **When the member comes from the URL, the picker becomes redundant on that path.** Create would take
  `memberId` from the route. `member_changed` already turns a URL/body mismatch on edit into a 409, the
  check the roadmap's risk note asks for (`roadmap.md:718-721`). Create has no equivalent: a POST body
  can name any member regardless of the URL, unless the new route carries the member id in its path
  and the handler compares the two.

## Historical Context (from prior changes)

- `context/archive/2026-09-04-training-plans/` (S-11):
  - trainers and admins both author plans for any active member (`research.md:35-39`, `:523-525`;
    `plan-brief.md:33-34`);
  - plans are not owned (`plan.md:92-93`);
  - routes live under `/trainer` because of URL honesty (`plan-brief.md:38`);
  - the picker is minimized to id and name (`plan.md:380-382`, `:629-631`);
  - one active plan per member rests on the index plus retry (`plan.md:116-133`);
  - `member_changed` and the static member name on edit came from reviews F2 and F4 (`reviews/impl-review.md:64-115`);
  - pointer-only reorder is an accepted gap (`plan-brief.md:39-40`).
- `context/archive/2026-09-07-member-entity-and-accountless-members/` (S-14): plans are keyed to `Member`;
  `HasAccount` was added to the picker (`plan.md:329-331`, `:352-354`); the shared `Assignable`
  predicate (`plan.md:248-254`).
- `context/archive/2026-09-09-membership-pass-and-staff-booking/` (S-16):
  - `/admin/members/:id/passes` became a screen and not a row panel (`plan-brief.md:42`, `plan.md:525-550`);
  - trainer booking is class-scoped with inline 403 checks (`plan.md:463-512`);
  - "trainer scoping is class-scoped only; … stays true for plans" (`plan.md:106-107`).
- `context/archive/2026-09-11-testing-access-surface/`:
  - the roster emails issue became Open Roadmap Question 8 and was deliberately left unpinned
    (`research.md:189-197`, `plan.md:222-230`);
  - the picker's "no email" pin (`plan.md:211-220`);
  - any-trainer-edits-any-plan is filed as "by design, not IDOR" (`research.md:178-182`).
- `context/archive/2026-09-06-member-and-admin-dashboards/` (S-12): the `/more` role matrix, where a
  trainer sees only Plany and an admin sees all six; the nav condition must equal the guard (`plan.md:275-311`).
- `context/changes/member-list-at-scale/` (S-21): it built the paged, searched members API and the table.
  It explicitly deferred "the trainer's member list and member-centric plans" to S-22 (`plan.md:86`,
  `plan-brief.md:54`). `change.md` is `impl_reviewed`, with 12 manual checks still unchecked in
  `reviews/impl-review.md`.
- `context/changes/admin-schedule-web-only/` (S-20): `_breakpoints.scss` is the single breakpoint source,
  and `plans.scss:30` and `plan-builder.scss:182` use it. On a phone, `/more` is the admin's only path
  to admin screens (`research.md:227-230`).
- `context/foundation/prd.md`:
  - FR-015/016/017 (`:113-118`) are unchanged in substance;
  - the privacy NFR at `:141` says member data (names, emails, plans) is visible only to the admin and
    the member themselves. **A trainer member list is in tension with the NFR's literal text, as is the
    existing trainer plan list and picker.** The NFR predates the Trainer role.

## Related Research

- `context/archive/2026-09-04-training-plans/research.md` - original plan research
- `context/archive/2026-09-11-testing-access-surface/research.md` - access surface and trainer visibility
- `context/changes/member-list-at-scale/plan.md` - S-21, the list this slice hangs off
- `context/changes/admin-schedule-web-only/research.md` - S-20, breakpoints and nav reachability

## Open Questions

For `/10x-plan` or the user to decide:

1. **What a trainer may see on their member list (UX-08, the substance of the slice).** Two decisions
   are involved.
   - *Scope of rows:*
     - (a) the whole club's active members, which matches today's picker and the "no ownership" stance;
     - (b) members of classes the trainer instructs, which introduces a new trainer↔member relationship
       and reverses S-11 and S-16;
     - (c) members whose plan they authored, which turns a display-only field into an authorization
       boundary.
   - *Columns:* name plus hasAccount, as `AssignableMember` does today, is the minimum. Adding email,
     status or phone widens past both the picker and the PRD's privacy NFR.
   - Open Roadmap Question 8 (roster emails) is adjacent. Answering one without the other leaves two
     standards for the same trainer.
2. **The trainer's route into a member's plan, and whether the builder moves.**
   - `/admin/members/:id/plan` is fixed for admins by UX-07. It sits behind `adminGuard`, and `/admin`
     means admin-only.
   - Options: (a) a trainer route such as `/trainer/members/:id/plan`, with one builder mounted twice;
     (b) a neutral `/members/:id/plan` behind `trainerGuard`, which admits admins, although that
     contradicts UX-07's literal route; (c) keep `/trainer/plans/:id` for editing and retire only the
     list.
3. **API shape for the by-member plan.**
   - Options: (a) `GET/PUT /api/trainer/members/{memberId}/plan` under `TrainerOrAdmin`, with the member
     in the path and a body/URL agreement check on create as well as edit; (b) keep
     `/api/trainer/plans/{id}` and add only `GET .../members/{memberId}/plan` for the lookup. Admins
     already pass `TrainerOrAdmin`, so one API can serve both SPA routes, and `/api/admin/members` stays
     Admin-only and untouched.
   - Which of today's routes are retired determines which tests must be rewritten as retired-route tests.
4. **What becomes of `GET /api/trainer/plans` and `GET /api/trainer/plans/members`.**
   - The list has no SPA consumer once `/trainer/plans` is gone. `MyPlanEndpointTests:397` reads it.
   - The picker becomes redundant if the member always comes from the URL. Keep, retire, or repurpose
     it as the trainer member list.
5. **Header "Plany" link (`app.html:32-34`).** The roadmap names only `/more`, but this link targets the
   same retired route. It needs a replacement target or removal, bearing in mind UX-09 (no redesign) and
   the out-of-scope role-aware nav.
6. **The plain trainer's `/more` Panel.** It is empty once Plany goes, unless the trainer member list
   takes its place.
7. **Plan-less members.** The admin opens a member with no plan, and the plan screen offers "create".
   After saving, the flow could stay on `/admin/members/:id/plan` or return to the list. The passes
   precedent stays on the screen.
8. **The dead `memberUnavailable` check** (`plan-builder.html:76`). Fix it in passing or leave it. It
   becomes moot if the picker disappears from the member-scoped path.
