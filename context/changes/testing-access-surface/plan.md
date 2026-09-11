# Access Surface — Test Rollout Phase 2 Implementation Plan

## Overview

Rollout Phase 2 of `context/foundation/test-plan.md` (risks #3 retired doors, #4 ownership). The
scenario-level protections are already tested (see research). This change adds the guarantees nothing
makes today and that roadmap S-18 is positioned to break: every API endpoint is authorized by the right
policy, the admin member and karnet routes refuse non-admins route by route, retired doors stay shut, and
the trainer's member picker carries no personal data. No production code changes.

## Current State Analysis

From `context/changes/testing-access-surface/research.md`:

- No authorization `FallbackPolicy` (`src/Program.cs:140`) — an endpoint without metadata is anonymous.
  Policy lives in one `RequireAuthorization` per `MapGroup`; the deliberate anonymous API routes are the
  four auth routes (`src/Application/Auth/AuthEndpoints.cs:149-181`) plus `/health` (`src/Program.cs:367`).
- Two path prefixes carry two policies each: `/api/admin/classes` (management = Admin,
  `ClassEndpoints.cs:224-234`; staff booking = TrainerOrAdmin, `BookingEndpoints.cs:232-238`) and
  `/api/admin/exercises` (read = TrainerOrAdmin, write = Admin, `ExerciseEndpoints.cs:147-164`).
- Hand-listed every-route refusal theories exist in six suites; the member group and the karnet group
  are probed on one route only (`MemberAdminEndpointTests.cs:77-110`, `MembershipPassEndpointTests.cs:213`,
  `MemberClaimTests.cs:159`).
- Retired approval routes are unasserted; a GET to one answers 200 with the SPA shell. The retired
  member-booking test asserts exactly 405 (`BookingEndpointTests.cs:383`) although its stated oracle is
  "neither 200 nor 409" (`:357-362`).
- The picker's minimization (`AssignableMember(Id, DisplayName, HasAccount)`,
  `TrainingPlanEndpoints.cs:85,281-289`) is unasserted.
- The roster shows trainers member emails (`BookingEndpoints.cs:41-68`) — decided: raise as a roadmap
  question, do not test.

Confirmed against current docs (Context7, `/dotnet/aspnetcore.docs`, 2026-09-11): `RequireAuthorization`
on `MapGroup` adds endpoint metadata to every endpoint in the group, and endpoints are `RouteEndpoint`s
exposing `RoutePattern.RawText`.

## Desired End State

- One in-process test fails if any `/api` endpoint is anonymous outside the four auth routes, if any
  `/api/admin/*` endpoint is not Admin (other than five named trainer exceptions), or if any
  `/api/trainer/*` endpoint is not TrainerOrAdmin. Demonstrated by mutation.
- Every route in the member and karnet admin groups answers 401 anonymous and 403 to a member and to a
  trainer.
- Retired approval routes are asserted gone; no retired-route test depends on 405 vs 404.
- The trainer picker's payload is proven free of a member's email.
- Roadmap Open Question 8 records the roster question; `test-plan.md` §6.2 describes how to add an
  authorization test.

### Key Discoveries:

- Existing refusal-theory shape to mirror: `ClassEndpointTests.cs:171-195` (`Guid.Empty` ids, empty JSON
  body, one theory per role).
- Not-the-SPA-shell assertion: `AuthEndpointTests.cs:104-116`.
- `fixture.Factory` is public (`IntegrationTestFixture.cs:40`) — `EndpointDataSource` is reachable
  without HTTP.
- Test users: `TestUsers.ActiveMemberEmail`, `ActiveTrainerEmail` (holds User + Trainer),
  `ActiveAdminEmail`.

## What We're NOT Doing

- No production change — including the roster payload and the missing fallback policy.
- No full route→policy snapshot (mirrors today's state; rejected in planning).
- No new tests for already-covered cases: codeless registration, reset/login enumeration, cross-member
  reads, push unsubscribe, pass nesting, staff-booking ownership, SPA guards.
- No rate-limit bypass test — spoofable by accepted design (`src/Program.cs:150-154`).
- No removal of the existing single-route refusal facts in `MemberAdminEndpointTests`.

## Implementation Approach

Two layers, each catching what the other cannot. The **inventory test** reads endpoint metadata and sees
every route, including ones no one listed — the S-18 failure. The **HTTP theories** prove the policy is
actually enforced for real users — a policy redefined to admit trainers would still pass the inventory.
Every expected value comes from a product source: PRD Access Control (`prd.md:174`, "login/registration
only", plus FR-026 reset at `prd.md:91`), S-16 MP-02 (trainers book into their own classes), S-11
(trainers read the exercise library and author plans).

## Critical Implementation Details

- **Normalize patterns before matching.** A group route mapped as `MapGet("/")` may surface with a
  trailing slash (`/api/admin/exercises/`); trim it, and compare the HTTP method from
  `HttpMethodMetadata` alongside the pattern, because the staff-booking GET and the management GET share
  the `/api/admin/classes` prefix.
- **Every allowlist and exception entry must also be asserted to exist.** Otherwise a renamed route
  silently turns the rule into a no-op — the same trap `MemberAdminEndpointTests.cs:67-76` documents
  for a policy test pointing at a deleted route.

## Phase 1: Authorization inventory

### Overview

The S-18 guard: one test over all endpoints.

### Changes Required:

#### 1. Inventory test

**File**: `tests/po-prostu-silka.Tests/EndpointAuthorizationTests.cs` (new, `[Collection(nameof(IntegrationCollection))]`)

**Intent**: Read every `RouteEndpoint` from `EndpointDataSource` via `fixture.Factory.Services` and assert
four rules whose expected values are written from the product sources, never derived from the endpoints.

**Contract**:
- **Anonymous allowlist** — exactly `POST /api/auth/login`, `POST /api/auth/register`,
  `POST /api/auth/forgot-password`, `POST /api/auth/reset-password` carry `IAllowAnonymous`; each exists.
  Every other `/api` endpoint carries at least one `IAuthorizeData` and no `IAllowAnonymous`.
- **Outside `/api`** — the only endpoints without authorization metadata are `/health` and the SPA
  fallback (`{*path:nonfile}`).
- **`/api/admin/*`** — policy `Admin`, except these five, which must exist and be `TrainerOrAdmin`:
  `GET /api/admin/classes/{classId:guid}/bookings`, `POST` same, `DELETE
  /api/admin/classes/{classId:guid}/bookings/{bookingId:guid}` (S-16 MP-02),
  `GET /api/admin/exercises`, `GET /api/admin/exercises/{id:guid}` (S-11).
- **`/api/trainer/*`** — policy `TrainerOrAdmin`.
- Failure messages name the offending method + pattern.

### Success Criteria:

#### Automated Verification:

- The test project builds warning-free: `dotnet build tests/po-prostu-silka.Tests`
- The inventory test passes: `dotnet test --filter FullyQualifiedName~EndpointAuthorizationTests`
- Mutation check: (a) delete `.RequireAuthorization(...)` from the `/api/admin/trainers` group; (b) change the staff-booking group's policy to `Admin`; (c) change the class-management group's policy to `TrainerOrAdmin` — each run is red; revert (`git diff --stat src/` empty)

#### Manual Verification:

- Every expected value in the test cites its product source in a comment, and none is read from the endpoint data source

**Implementation Note**: After completing this phase and all automated verification passes, pause for
manual confirmation before proceeding.

---

## Phase 2: Refusals route by route, and retired doors

### Overview

Prove enforcement for real users on the highest-privilege routes, and pin the retired doors with the
right oracle.

### Changes Required:

#### 1. Member admin group refusal theories

**File**: `tests/po-prostu-silka.Tests/MemberAdminEndpointTests.cs`

**Intent**: Every route in the group refuses anonymous (401), a member (403) and a trainer (403).

**Contract**: `EveryAdminRoute` theory data over the 11 routes (`MemberAdminEndpoints.cs:241-255`) with
`Guid.Empty` ids and an empty JSON body, three theories, in the `ClassEndpointTests.cs:171-195` shape.

#### 2. Karnet admin group refusal theories

**File**: `tests/po-prostu-silka.Tests/MembershipPassEndpointTests.cs`

**Intent**: Same three refusals on the four karnet routes (`MembershipPassEndpoints.cs:143-146`).

**Contract**: `EveryRoute` theory data + three theories.

#### 3. Retired approval routes

**File**: `tests/po-prostu-silka.Tests/MemberAdminEndpointTests.cs`

**Intent**: As an active admin (the caller who used to be allowed): `POST /api/admin/members/{id}/approve`
is not a success and not a 409; `GET /api/admin/members/pending` is not JSON (it is the SPA shell).

**Contract**: two facts; oracle S-16 MP-03; media-type check as in `AuthEndpointTests.cs:104-116`.

#### 4. Relax the exact-405 assertion

**File**: `tests/po-prostu-silka.Tests/BookingEndpointTests.cs`

**Intent**: `The_removed_member_booking_routes_are_gone` asserts its own stated oracle — not 2xx and not
409 — so a legitimate 404 after S-18 does not fail it.

**Contract**: assertion change only (`:383`); doc comment updated to match.

#### 5. Stale SPA spec title

**File**: `src/app/src/app/core/auth/active-member.guard.spec.ts`

**Intent**: Rename `'sends an authenticated but pending member to /pending'` (`:55`) to describe the
`/login` redirect it asserts.

**Contract**: title only.

### Success Criteria:

#### Automated Verification:

- The test project builds warning-free: `dotnet build tests/po-prostu-silka.Tests`
- The touched suites pass: `dotnet test --filter "FullyQualifiedName~MemberAdminEndpointTests|FullyQualifiedName~MembershipPassEndpointTests|FullyQualifiedName~BookingEndpointTests"`
- The SPA specs pass: `npm test` from `src/app/` (Node 22+)
- Mutation check: add `ApplicationRoles.Trainer` to the `Admin` policy's `RequireRole` (`AuthorizationPolicies.cs:69`); the new trainer theories are red while the inventory test stays green; revert (`git diff --stat src/` empty)

#### Manual Verification:

- The retired-route tests use an admin client, so a refusal cannot be explained by authorization

**Implementation Note**: After completing this phase and all automated verification passes, pause for
manual confirmation before proceeding.

---

## Phase 3: Picker privacy, the open question, the cookbook

### Overview

Pin the one deliberate data minimization, carry the roster question to its owner, and leave the pattern
behind.

### Changes Required:

#### 1. Picker carries no email

**File**: `tests/po-prostu-silka.Tests/TrainingPlanEndpointTests.cs`

**Intent**: A trainer's `GET /api/trainer/plans/members` response body does not contain a known member's
email address anywhere, while the member is listed.

**Contract**: one fact; arrange a member with a unique email; assert on the raw response string, not on a
deserialized shape (asserting the current fields would be a mirror). Oracle:
`TrainingPlanEndpoints.cs:281-289` rationale and the PRD privacy NFR (`prd.md:141`).

#### 2. Roadmap Open Question 8

**File**: `context/foundation/roadmap.md`

**Intent**: Append: should a trainer see members' email addresses on their class roster? Since S-16 they
do; the PRD privacy NFR says only the admin and the member see member data; no decision recorded.
Owner: user. Block: none.

**Contract**: one appended item.

#### 3. Cookbook §6.2 and a §6.6 note

**File**: `context/foundation/test-plan.md`

**Intent**: Replace the §6.2 placeholder with the authorization recipe; append a §6.6 Phase 2 note.

**Contract**: §6.2 carries Location, the two-layer pattern (inventory rule vs HTTP theory — when a new
route needs which), the oracle rule (allowlists and exceptions from product sources, asserted to exist),
the SPA-shell caveat for GET refusals, reference tests, run command. §1–§5 untouched.

### Success Criteria:

#### Automated Verification:

- The full suite passes from the repo root: `dotnet test`

#### Manual Verification:

- §6.2 tells someone adding an endpoint in S-18 exactly which rule or theory to touch

---

## Testing Strategy

### Integration Tests:

- Endpoint inventory: allowlist, admin prefix + five exceptions, trainer prefix (Phase 1).
- Refusal theories: 11 member-admin routes, 4 karnet routes × anonymous/member/trainer (Phase 2).
- Retired approval routes; relaxed retired-booking oracle (Phase 2).
- Picker privacy (Phase 3).

### Manual Testing Steps:

1. Run each mutation check and watch the named tests go red, then revert.

## Performance Considerations

The inventory test makes no HTTP calls. The theories add ~45 cases of single HTTP calls — seconds.

## References

- Research: `context/changes/testing-access-surface/research.md`
- Test plan: `context/foundation/test-plan.md` §2 risks #3–#4, §3 Phase 2
- Refusal-theory shape: `tests/po-prostu-silka.Tests/ClassEndpointTests.cs:171-195`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Authorization inventory

#### Automated

- [x] 1.1 The test project builds warning-free — b977e40
- [x] 1.2 The inventory test passes — b977e40
- [x] 1.3 Mutation check: dropped policy, staff booking as Admin, management as TrainerOrAdmin are each red — b977e40

#### Manual

- [x] 1.4 Every expected value cites its product source and none is read from the endpoint data source — b977e40

### Phase 2: Refusals route by route, and retired doors

#### Automated

- [x] 2.1 The test project builds warning-free
- [x] 2.2 The touched suites pass
- [x] 2.3 The SPA specs pass
- [x] 2.4 Mutation check: Admin policy admitting trainers turns the trainer theories red

#### Manual

- [x] 2.5 The retired-route tests use an admin client

### Phase 3: Picker privacy, the open question, the cookbook

#### Automated

- [x] 3.1 The full suite passes

#### Manual

- [x] 3.2 §6.2 tells an S-18 implementer which rule or theory to touch
