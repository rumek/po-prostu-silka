---
date: 2026-09-11T21:26:51+02:00
researcher: Claude (Opus 5)
git_commit: 383fdea9c369807be7233ab6608460c92c225ba5
branch: main
repository: PoProstuSilka
topic: "Ground rollout Phase 2 of context/foundation/test-plan.md — retired doors (#3) and resource ownership (#4)"
tags: [research, codebase, authorization, access-control, testing, rollout-phase-2]
status: complete
last_updated: 2026-09-11
last_updated_by: Claude (Opus 5)
---

# Research: Ground rollout Phase 2 — retired doors (#3) and resource ownership (#4)

**Date**: 2026-09-11T21:26:51+02:00
**Researcher**: Claude (Opus 5)
**Git Commit**: 383fdea9c369807be7233ab6608460c92c225ba5
**Branch**: main
**Repository**: PoProstuSilka

GitHub permalinks omitted: the GitHub CLI is not authenticated in this session. References are
repo-relative `path:line` at the commit above.

## Research Question

Ground rollout Phase 2 of `context/foundation/test-plan.md`:

- **#3** — a retired or closed door still opens: member self-book/self-cancel (S-16), the approval
  flow (S-16), registration without an invitation code (S-17), the registration rate limit.
- **#4** — a signed-in user reaches data or actions that are not theirs; password reset reveals
  which addresses are registered.

For each: find the real failure path, locate existing tests, verify or correct the response
guidance, name the cheapest useful layer, flag speculative risks.

## Summary

Most scenario-level protections are **already tested**, several of them carefully (the SPA-shell
hazard, account enumeration, cross-member reads). The rollout's value is not in re-testing those; it
is in the one guarantee nothing currently makes, and which S-18 is precisely positioned to break:

1. **No test proves that every API endpoint is authorized.** There is no authorization
   `FallbackPolicy`, so an endpoint is anonymous unless its `MapGroup` says otherwise. Protection
   lives in 16 `.RequireAuthorization(...)` calls, and per-route refusal is checked only by
   hand-listed theories in six suites — which cannot see a route they do not list. S-18 re-creates
   every group in new files. **This is the phase's highest-signal test, and it is cheap**: an
   in-process inventory over `EndpointDataSource` with an anonymous allowlist whose oracle is the
   PRD, not today's route table.
2. **The highest-privilege admin routes have no every-route refusal theory** — the member group
   (block, grant Trainer, issue/revoke access codes) and the karnet group.
3. **The retired approval routes are unasserted**, and a GET to a retired path answers **200 with
   the SPA shell**, so a naive "not 200 = refused" oracle is wrong for reads.
4. **A product question, not a test:** since S-16 a trainer reads the roster of their own classes,
   and the roster carries each member's **email** and account id. The PRD says member data is visible
   only to the admin and the member. Nothing records this widening as a decision.

Three parts of the test plan's §2 do not hold up against the code and should be backported (see
"Corrections to the test plan").

## Detailed Findings

### 1. How authorization is wired (the S-18 exposure)

- `AddAuthorizationBuilder().AddApplicationPolicies()` registers named policies only; there is no
  `FallbackPolicy` or `DefaultPolicy` override (`src/Program.cs:140`). An endpoint that carries no
  authorization metadata is **anonymous by default**.
- Policies: `ActiveMember`, `Admin`, `TrainerOrAdmin` — each requires an authenticated user, the
  account-status claim `Active`, the membership-status claim `Active`, and a role
  (`src/Infrastructure/Authorization/AuthorizationPolicies.cs:55-82`).
- Every group applies its policy once, at `MapGroup` (inventory below). The deliberate anonymous
  endpoints are exactly: `POST /api/auth/login` (`src/Application/Auth/AuthEndpoints.cs:149`),
  `POST /api/auth/register` (`:153-155`, rate-limited), `POST /api/auth/forgot-password`
  (`:175-177`, rate-limited), `POST /api/auth/reset-password` (`:181`), and `GET /health`
  (`src/Program.cs:366-367`). The SPA fallback `MapFallbackToFile("index.html")` is last
  (`src/Program.cs:405-406`). Testing-only probes `/test/active-member` and `/test/admin-only`
  exist in the `Testing` environment (`src/Program.cs:396-403`) — the integration fixture runs in it.
- Bare `RequireAuthorization()` (authenticated, any status) is deliberate on `/api/auth/logout`,
  `/refresh`, `/me`, `/change-password` (`AuthEndpoints.cs:156-170`), `PUT /api/profile`
  (`src/Application/Members/ProfileEndpoints.cs:51`) and the `/api/push` group
  (`src/Application/Notifications/PushEndpoints.cs:24`).

Route inventory by group and policy:

| Group | Policy | Routes | Source |
|---|---|---|---|
| `/api/admin/members` | Admin | GET `/`, GET/PUT `/{id}`, POST `/`, POST `/{id}/block`, `/unblock`, POST/DELETE `/{id}/roles/trainer`, GET/POST/DELETE `/{id}/access-code` | `MemberAdminEndpoints.cs:234-255` |
| `/api/admin/members/{id}/passes` | Admin | GET, POST, PUT `/{passId}`, DELETE `/{passId}` | `MembershipPassEndpoints.cs:139-146` |
| `/api/admin/trainers` | Admin | GET | `TrainerEndpoints.cs:47-51` |
| `/api/admin/classes` (management) | Admin | GET, GET/PUT/DELETE `/{id}`, POST, `/cancel`, `/duplicate` | `ClassEndpoints.cs:224-234` |
| `/api/admin/classes` (staff booking) | TrainerOrAdmin + inline instructor check | GET/POST `/{classId}/bookings`, DELETE `/{classId}/bookings/{bookingId}` | `BookingEndpoints.cs:232-238` |
| `/api/admin/class-types` | Admin | 6 routes | `ClassTypeEndpoints.cs:115-128` |
| `/api/admin/exercises` | read: TrainerOrAdmin; write: Admin | GET `/`, GET `/{id}`; POST, PUT, activate, deactivate | `ExerciseEndpoints.cs:147-164` |
| `/api/trainer/plans` | TrainerOrAdmin | GET `/`, GET `/members`, GET/PUT `/{id}`, POST | `TrainingPlanEndpoints.cs:247-261` |
| `/api/classes` | ActiveMember | GET `/` | `ClassEndpoints.cs:218-222` |
| `/api/bookings` | ActiveMember | GET `/mine` | `BookingEndpoints.cs:206-210` |
| `/api/passes` | ActiveMember | GET `/mine` | `MyPassEndpoints.cs:34-38` |
| `/api/plans` | ActiveMember | GET `/mine`, GET `/mine/exercises/{id}` | `MyPlanEndpoints.cs:34-39` |

Two groups share the path `/api/admin/classes` with different policies, and so do the two exercise
groups. That is exactly the shape a mechanical move in S-18 can merge wrongly — a staff-booking route
landing in the Admin group (trainers lose booking) or a management route landing in TrainerOrAdmin
(every trainer can cancel and delete classes).

### 2. Existing per-route refusal coverage

Hand-listed every-route theories exist in six suites:
`ClassTypeEndpointTests.cs:72-103`, `ExerciseEndpointTests.cs:94-173` (incl. trainer write refusal),
`MyPlanEndpointTests.cs:127-145`, `TrainingPlanEndpointTests.cs:153-187`,
`BookingEndpointTests.cs:324` (`EveryMemberRoute`), `ClassEndpointTests.cs:183` (`EveryAdminRoute`).

No every-route theory was found in `MemberAdminEndpointTests.cs`, `MembershipPassEndpointTests.cs`
(one spot check, `A_non_admin_is_refused`, `:213`), `MemberAccessCodeTests.cs`/`MemberClaimTests.cs`
(one spot check, `Reading_a_code_requires_an_admin`, `MemberClaimTests.cs:159`), or for
`/api/admin/trainers`. Those are the routes that grant roles, issue credentials and block people.

A hand-listed theory catches a listed route losing its policy. It cannot catch a route that was
never listed — including any route added or re-homed during S-18.

### 3. Retired and closed doors (#3)

- **Member self-book / self-cancel (S-16 MP-01)** — removed; asserted by
  `The_removed_member_booking_routes_are_gone` (`tests/po-prostu-silka.Tests/BookingEndpointTests.cs:369-386`)
  with an ACTIVE member. It asserts **exactly 405**, although its own doc comment states the real
  oracle as "neither a 200 nor a 409, both of which would mean the handler ran" (`:357-362`). 405 is
  an artifact of `MapFallbackToFile` claiming unmatched paths for GET/HEAD only; a route reshuffle in
  S-18 can legitimately turn it into a 404 and fail the test for the wrong reason.
- **Approval flow (S-16 MP-03)** — `GET /api/admin/members/pending` and `POST /{id}/approve` are
  gone, and their tests "went with the routes" (`MemberAdminEndpointTests.cs:16-17`). **Nothing
  asserts their absence.** Because `"pending"` does not satisfy `{memberId:guid}`, a GET falls through
  to the SPA fallback and answers **200 `text/html`**; a POST answers 405. The suite already knows
  this hazard and has the right assertion pattern: `Me_is_401_when_anonymous_and_is_not_the_spa_shell`
  (`AuthEndpointTests.cs:104-116`) checks the media type is not `text/html`.
- **Codeless registration (S-17 IR-05)** — refused at the API: `Registering_without_a_code_is_refused`
  (`MemberClaimTests.cs:267`), plus revoked/expired/blocked/malformed/single-use code tests
  (`:341-435`) and a race (`:454`). In the SPA, `invitationGuard` is specced
  (`src/app/src/app/core/auth/invitation.guard.spec.ts:59-100`), including a blank code → `/login`.
- **Registration rate limit** — 3 per 5 minutes per client address, 429 on refusal
  (`src/Program.cs:180-197`); asserted by `A_burst_of_registrations_from_one_client_is_refused`
  (`RegisterEndpointTests.cs:120-134`). The partition is keyed on `X-Forwarded-For`, which the code
  documents as **spoofable by design** — "a courtesy cap on volume and NOT an authorization control"
  (`src/Program.cs:150-154`). "Bypassing" it is an accepted property, not a defect.
- **Pending accounts** — `AccountStatus.Pending` is declared but never produced; every policy requires
  the `Active` status claim. `MyPlanEndpointTests.Pending_member_is_403_on_every_route` (`:145`) still
  exercises the refusal.
- **SPA retired routes** — `/pending` and `/admin/approvals` were removed (`src/app/src/app/app.routes.ts:22`);
  anything unmatched redirects to `''`, which is behind `authGuard` + `activeMemberGuard` (`:54`, `:188`).
  All five guards have specs (`src/app/src/app/core/auth/*.guard.spec.ts`). One spec title is stale:
  `'sends an authenticated but pending member to /pending'` asserts a redirect to `/login`
  (`active-member.guard.spec.ts:55-67`).

### 4. Resource ownership and enumeration (#4)

Member-facing reads are **cookie-scoped by construction**: no member route takes another person's
id. `/api/bookings/mine`, `/api/passes/mine`, `/api/plans/mine`, `PUT /api/profile`, `/api/auth/me`
all resolve the member from the principal (e.g. `BookingEndpoints.cs:466-481`). Existing tests:

- another member's plan is not readable — `MyPlanEndpointTests.A_member_cannot_see_another_members_plan` (`:257`);
- an exercise outside the caller's plan is 404 — `:300`, archived-plan exercise `:341`;
- "mine" lists nobody else's bookings — `BookingEndpointTests.cs:513`;
- a member cannot unsubscribe another member's push device — `PushEndpointTests.cs:92`;
- profile ignores display name and email sent alongside (mass assignment) — `ProfileEndpointTests.cs:152`;
- a pass addressed under the wrong member is 404 — `MembershipPassEndpointTests.cs:289`;
- trainer ownership on staff booking (book, release, roster) — `AdminBookingEndpointTests.cs:308-398`.

Enumeration:

- password reset answers identically for registered, unregistered, blocked, repeated and null
  addresses, and a bad token equals an unknown address — `PasswordEndpointTests.cs:281-492`;
- login: wrong password and unknown email are indistinguishable — `AuthEndpointTests.cs:68`. A blocked
  user gets a distinguishing reason (`:54`), deliberately;
- registration discloses `email_taken` — but only behind a valid invitation code
  (`RegisterEndpointTests.cs:237-268`), so it is not an anonymous oracle.
- Response **timing** is not tested and is not cheaply testable at this layer.

By design, not IDOR:

- **Trainers read and edit every active plan in the club.** `GET /api/trainer/plans` returns "every
  ACTIVE plan in the club" (`TrainingPlanEndpoints.cs:266-279`); `GetByIdAsync` and `UpdateAsync`
  carry no author check (`:295-303`, `:419-450`). S-11 chose club-wide authoring
  (`context/archive/2026-09-04-training-plans/plan.md:135-139`) and the roadmap records the widening
  (S-11 risk line).
- **The trainer's member picker is minimized on purpose** — `AssignableMember(Id, DisplayName, HasAccount)`
  (`TrainingPlanEndpoints.cs:85`), with the rationale written down: loosening `/api/admin/members`
  "would have handed every trainer the club's email list" (`:281-289`). No test pins that shape;
  `The_member_picker_offers_active_accounts_only` (`TrainingPlanEndpointTests.cs:457`) checks
  eligibility, not fields.

Not recorded as a decision:

- **The roster exposes member email and account id to trainers.** `ClassBooking` carries `UserId`
  and `Email` (`BookingEndpoints.cs:62-68`), and its doc comment still justifies the email with "it
  is admin-only surface, gated by the same policy as every other admin endpoint" (`:41-44`). Since
  S-16 the group is `TrainerOrAdmin` (`:232-234`) and a trainer may read the roster of a class they
  instruct (`AdminBookingEndpointTests.cs:363`). The PRD's privacy NFR says member data is "visible
  only to the admin and the member themselves" (`context/foundation/prd.md:141`). The S-16 plan
  discusses ownership on this route (`plan.md:485`) but not the payload.

## Code References

- `src/Program.cs:140` — policies registered; no fallback policy
- `src/Program.cs:150-197` — rate limiters and the "courtesy cap, not authorization" rationale
- `src/Program.cs:366-406` — anonymous `/health`, endpoint mapping order, Testing probes, SPA fallback
- `src/Infrastructure/Authorization/AuthorizationPolicies.cs:55-82` — the three policies
- `src/Application/Auth/AuthEndpoints.cs:147-181` — the only anonymous API routes
- `src/Application/Scheduling/BookingEndpoints.cs:41-68,206-238` — roster payload; member and staff groups
- `src/Application/Training/TrainingPlanEndpoints.cs:85,247-303,419-450` — picker shape; club-wide plan authoring
- `tests/po-prostu-silka.Tests/AuthEndpointTests.cs:104-116` — the not-the-SPA-shell assertion pattern
- `tests/po-prostu-silka.Tests/BookingEndpointTests.cs:350-386` — retired member routes, exact-405 assertion
- `tests/po-prostu-silka.Tests/IntegrationTestFixture.cs:40` — `Factory` is public, so `EndpointDataSource` is reachable from a test
- `src/app/src/app/app.routes.ts:22,32,54,188` — retired SPA routes, invitation guard, catch-all

## Architecture Insights

- **Authorization is a group-level convention.** One policy per `MapGroup`, never per route — the
  exercise groups were split rather than loosened specifically to keep that true
  (`context/archive/2026-09-04-training-plans/plan.md:141-145`). The convention is what makes a
  metadata inventory test straightforward: every `/api` `RouteEndpoint` should carry
  `IAuthorizeData`, and only the allowlist should carry `IAllowAnonymous`.
- **Resource-level narrowing is inline and exists in exactly one place** — the staff-booking
  instructor check (`BookingEndpoints.cs:212-231`, `:606-608`). A group-policy inventory cannot see
  it; the existing ownership tests are what cover it.
- **Unmatched API GETs return the SPA shell with 200.** Any "refused" assertion on a GET must check
  the media type or body, not only the status class.

## Cheapest layer per gap (recommendation for `/10x-plan`)

| Gap | Behaviour to prove | Oracle | Layer | Anti-pattern to avoid |
|---|---|---|---|---|
| Every `/api` endpoint is authorized | No API endpoint is anonymous except login, register, forgot-password, reset-password; `/health` is the only other anonymous endpoint; `/api/admin/*` requires Admin or TrainerOrAdmin; `/api/trainer/*` requires TrainerOrAdmin | PRD Access Control "unauthenticated access: login/registration only" (`prd.md:174`) + FR-026 reset + the health probe's stated purpose | in-process test over `EndpointDataSource` via `fixture.Factory.Services` (no HTTP) | snapshotting today's route list or deriving expected policies from the endpoints themselves |
| Admin member + pass routes refuse non-admins | Member and trainer get 403, anonymous 401, on every route in those two groups | Admin-only by PRD Access Control and the group comments | integration theory in the existing suite style | listing routes by reading the group file at test time |
| Approval routes stay gone | POST → not 2xx and not 409; GET → not JSON (the SPA shell) | S-16 MP-03 | integration, 2–3 cases | asserting exact 405 / exact 404 |
| Retired member routes | Keep; relax exact 405 to the test's own stated oracle | S-16 MP-01 | edit existing test | — |
| Picker minimization | `/api/trainer/plans/members` payload carries no email, phone or address | `TrainingPlanEndpoints.cs:281-289` rationale + PRD privacy NFR | integration, one test | asserting the full current shape (a mirror) |

Deliberately not worth new tests: codeless registration, reset/login enumeration, cross-member reads,
push unsubscribe, pass nesting — all covered above. SPA guards are specced; the only SPA work is the
stale spec title.

## Corrections to the test plan (for `/10x-test-plan` backport)

1. **#3 "the registration rate limit can be bypassed"** — spoofing `X-Forwarded-For` is an accepted
   property (`src/Program.cs:150-154`), and the cap itself is tested. Reword #3 to drop "bypass";
   the protection to prove is "the cap refuses a burst with 429", which already exists.
2. **#3/#4 test types "+ SPA guard specs"** — every guard is already specced; the SPA half of this
   phase reduces to one stale title. The phase is integration-only.
3. **#4 Source** — likelihood for #4 comes less from the Members/Auth hot-spots than from the
   structural fact that authorization is per-group with no fallback, which S-18 will re-create. The
   hot-spot citation is not wrong, but it is not the evidence that makes #4 likely.

## Historical Context (from prior changes)

- `context/archive/2026-09-09-membership-pass-and-staff-booking/plan.md:702-766` — Phase 7 removed
  self-service booking; `:744` records that removed routes answer 405, not 404.
- `.../plan.md:776-872` — Phase 8 removed the approval surfaces and verified absence by grep only.
- `.../plan.md:485` — ownership (403, not 404) on `GetForClassAsync`; the roster payload not discussed.
- `context/archive/2026-09-04-training-plans/plan.md:135-145` — club-wide trainer authoring; the
  one-policy-per-group convention.
- `context/archive/2026-09-09-invitation-only-registration/` — codeless registration closed at the
  API and the route guard together.

## Related Research

- `context/changes/testing-booking-invariants/plan.md` — rollout Phase 1 (grounded inline, no
  research.md); its §6.1 cookbook entry sets the fixture/DB-assertion conventions this phase follows.

## Open Questions

1. **Should a trainer see members' email addresses on their class roster?** Product decision, owner:
   user. Until decided, the plan should neither pin the current payload nor change it; raise it like
   roadmap Open Question 7.
2. Whether the approval-route absence test earns its keep: code does not resurrect deleted handlers
   by being moved, so its signal is modest. The inventory test covers the more likely S-18 failure.
