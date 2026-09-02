# Trainer Role and Assignment Implementation Plan

## Overview

Add a third application role, `Trainer`, that the admin grants to and revokes from active accounts
on the existing member screen. The role is **additive** — it takes nothing away from the account
that receives it — and it confers **no new permission** in this change. Its only purpose is to make
"who runs this class" a person the system knows, so `S-06` can populate an instructor selection
instead of accepting free text.

## Current State Analysis

The identity surface shipped in `F-02` and was generalised by `S-02`. What exists today:

- **Two roles.** `ApplicationRoles` (`src/Domain/ApplicationRoles.cs`) declares `User` and `Admin`
  and an `All` array. Registration grants `User` (`src/Application/Auth/AuthEndpoints.cs:190`); the
  seeder grants `Admin` and nothing else (`src/Infrastructure/Identity/AdminSeeder.cs:78`). So role
  membership is already a *set*, and the seeded admin is already not a `User`.
- **`ApplicationRoles.All` does two unrelated jobs.** `AdminSeeder.cs:23` iterates it to create
  missing roles; `AuthorizationPolicies.cs:40` passes it to `RequireRole` as the set of roles that
  satisfy `ActiveMember`. One array, two meanings, and nothing marks the difference.
- **Admins are structurally excluded from the member list.** `MemberQuery`
  (`src/Infrastructure/Members/MemberQuery.cs:33-45`) filters them out by role, and `BlockAsync`
  (`src/Application/Members/MemberAdminEndpoints.cs:215`) refuses them again with `is_admin`/409.
  The query's own comment states why: the seeded admin is an ordinary row in the same table, and
  blocking the only admin locks the club out of its own application with no route back through the
  UI.
- **A settled mutation shape.** `ApproveAsync`, `BlockAsync` and `UnblockAsync` all follow the same
  sequence: idempotency check → refuse wrong source states with a named 409 → manual
  `ConcurrencyStamp` rotation → a single `TrySaveChangesAsync`. They deliberately bypass
  `UserManager.UpdateAsync` so a status flip and its outbox rows commit in one transaction.
- **Two mirrored contracts.** `MemberSummary` (`MemberAdminEndpoints.cs:28`) is mirrored field for
  field by `Member` in `src/app/src/app/core/admin/member-admin.models.ts`. Both files carry
  comments calling this a contract.
- **Real test infrastructure.** `tests/po-prostu-silka.Tests/` runs xunit against a
  `WebApplicationFactory` with `Testcontainers.MsSql` — a real SQL Server, not an in-memory
  provider. `MemberAdminEndpointTests.cs` already covers this endpoint group. (`AGENTS.md` still
  says "No test project exists yet"; that line is stale.)

**The collision this plan resolves.** `prd-v2` FR-001 puts the grant action on the member list, and
FR-003 requires an account holding both `Admin` and `Trainer` — an owner who teaches. Those two are
incompatible while the member list excludes admins. This plan removes the exclusion from the
*query* and keeps it in the *action*: admins become visible and role-actionable, and stay
un-blockable.

## Desired End State

The admin opens the member screen and sees every account, including their own admin account, each
row showing its status badge and its role badges. A row menu offers "Nadaj rolę Trenera" on an
active account that lacks it and "Odbierz rolę Trenera" on one that has it. Granting a
non-active account is refused with a named reason. Blocking an admin is still refused. Nothing
else about what any account can do changes.

Verify by: signing in as the admin, granting `Trainer` to an active member and to the admin
account itself, confirming both rows show the badge, revoking one, and confirming that block on
the admin row is still refused.

### Key Discoveries:

- `ApplicationRoles.All` is consumed by both `AdminSeeder.cs:23` (seeding) and
  `AuthorizationPolicies.cs:40` (the `ActiveMember` role set) — adding a role to it silently widens
  who passes the policy.
- The seeded admin holds only `Admin` (`AdminSeeder.cs:78`), never `User`, so an admin does **not**
  satisfy `ActiveMember` via the `User` role — it satisfies it because `Admin` is also in `All`.
- `MemberQuery.cs:33` matches on `NormalizedName`, not `Name`, and its comment explains that this
  is to agree with `UserManager.IsInRoleAsync`. Any new role query must follow the same convention.
- `BlockAsync` is the API-side half of a two-layer admin guard; the query is the other half. This
  plan deliberately reduces that to one layer and compensates with an explicit manual check.
- Role membership rides the Identity principal, so a granted role does not reach the browser's
  cookie until the security-stamp validation interval fires or `POST /api/auth/refresh` is called.

## What We're NOT Doing

- **No trainer-facing screen or permission.** The role grants nothing beyond `User` in this change;
  no new authorization policy, no trainer view. (`prd-v2` §Non-Goals.)
- **No instructor selection.** Consuming the role to populate a dropdown is `S-06`.
- **No session invalidation on role change.** Neither grant nor revoke rotates the security stamp.
- **No trainer registration path and no admin-created accounts.** The role is only ever granted to
  an account that already registered and was approved.
- **No role filter on the member list.** Search and the status filter stay as they are.
- **No change to `ActiveMember` semantics.** The policy must admit exactly the same accounts after
  this change as before it.

## Implementation Approach

Two phases. The first carries every backend change — the role constant split, the seeded role, the
two endpoints, the widened query, and the contract field — because the query change and the policy
change are two halves of one risk and are best verified together against real endpoints. The second
adds the admin screen.

The role split is the load-bearing decision. `ApplicationRoles.All` becomes two named sets with
different jobs, so that adding `Trainer` to the seeded set cannot, as a side effect, change who
passes `ActiveMember`.

The mutation itself deviates from the `ApproveAsync`/`BlockAsync` shape deliberately: it goes
through `UserManager.AddToRoleAsync` / `RemoveFromRoleAsync` and does not rotate any stamp. That
pattern exists to bind a status flip to its outbox rows in one transaction; a role grant enqueues
nothing, so there is no second write to bind, and re-implementing Identity's own join-table
handling would buy nothing but a chance to disagree with it about name normalisation.

## Critical Implementation Details

**Role-set semantics.** `ActiveMember` currently reads `RequireRole(ApplicationRoles.All)`, which
today means `User` or `Admin`. `Trainer` must **not** join that set: a trainer is additive, so a
real trainer already holds `User` and passes anyway, while an account holding only `Trainer` is a
state this change does not create and must not silently authorise. The seeded set and the
policy set must therefore be different arrays, and both must be commented so the next reader knows
which one a new role belongs in.

**Two-layer guard becomes one.** Removing the admin exclusion from `MemberQuery` leaves
`BlockAsync`'s `is_admin` check as the only thing preventing an admin from being blocked. That
check is by role and already handles a second seeded admin, so it holds — but the query comment
that currently explains the exclusion must be rewritten rather than deleted, so the next reader
learns that the protection moved rather than that it vanished.

## Phase 1: Role, policy, and the admin API

### Overview

Introduce the `Trainer` role without changing who can do what, add grant and revoke endpoints, and
widen the member list to carry roles and include admins.

### Changes Required:

#### 1. Role constants

**File**: `src/Domain/ApplicationRoles.cs`

**Intent**: Add `Trainer`, and split the single `All` array into two sets so that seeding a role and
authorising a role stop being the same decision.

**Contract**: `Trainer` constant added alongside `User` and `Admin`. `All` keeps its name and its
meaning as "every role that must exist in the database" and gains `Trainer`. A second array —
naming the roles that satisfy `ActiveMember`, i.e. `User` and `Admin` — is added for the policy to
consume. Both arrays carry a comment stating which one a future role belongs in and why the
distinction exists. `AdminSeeder.cs:23` continues to iterate `All` and therefore seeds `Trainer`
with no edit of its own.

#### 2. Authorization policy

**File**: `src/Infrastructure/Authorization/AuthorizationPolicies.cs`

**Intent**: Point `ActiveMember` at the new policy set so its behaviour is unchanged by the added
role.

**Contract**: the `RequireRole` argument in the `ActiveMember` policy changes from `All` to the
member-facing set. `Admin` policy is untouched. Net effect on which principals pass either policy:
none.

#### 3. Grant and revoke endpoints

**File**: `src/Application/Members/MemberAdminEndpoints.cs`

**Intent**: Let the admin add and remove `Trainer` on an account, refusing anything that is not
active.

**Contract**: two routes on the existing `/api/admin/members` group, which already carries
`RequireAuthorization(AuthorizationPolicyNames.Admin)` at group level —
`POST /{id}/roles/trainer` to grant and `DELETE /{id}/roles/trainer` to revoke. Both resolve the
user with `FindByIdAsync` and return 404 when absent. Both are idempotent: granting a role the
account already holds, or revoking one it does not hold, returns 200 and writes nothing. A target
whose `Status` is not `Active` is refused with a 409 carrying a named reason, following the shape
of `ApproveFailure`/`BlockFailure` — a `TrainerRoleFailure` record with reason `not_active`. The
mutation itself uses `UserManager.AddToRoleAsync` / `RemoveFromRoleAsync`; no `ConcurrencyStamp`
rotation, no `SecurityStamp` rotation, no outbox enqueue. A comment states why this departs from
the surrounding transition shape.

**Adapted during implementation.** Three points where the code is narrower or wider than the
contract above:

1. `TrainerRoleFailure` carries a second reason, `failed`, for a non-succeeding `IdentityResult`.
   The contract named only `not_active`, but `AddToRoleAsync` / `RemoveFromRoleAsync` return a
   result that must not be silently discarded. In practice the role is seeded on every start, so
   the realistic cause is a concurrent grant that already produced the outcome the caller wanted.
2. The **idempotency check runs before the status guard**, not after. An account that was approved,
   granted the role, and later blocked therefore gets `200` from a re-grant (it already holds the
   role, nothing is written) rather than `409 not_active`. Refusing there would report a conflict
   about a change that would be a no-op.
3. The status guard applies to **revoke as well as grant**. The contract stated it for the grant
   direction; applying it to both keeps the two from disagreeing about which accounts this surface
   may touch. Consequence: a blocked account keeps a role it already holds. That is safe because
   `S-06` filters the instructor selection by status, so a blocked trainer is unselectable there.

#### 4. Member list contract

**File**: `src/Application/Members/MemberAdminEndpoints.cs`

**Intent**: Carry each account's roles on the row so the screen can render badges and decide which
actions apply.

**Contract**: `MemberSummary` gains `IReadOnlyList<string> Roles`, holding role **names** as stored
(`"User"`, `"Admin"`, `"Trainer"`) — matching the existing convention that `Status` crosses the
wire as its enum name rather than a number. The record's contract comment is extended to cover the
new field.

#### 5. Member query

**File**: `src/Infrastructure/Members/MemberQuery.cs`

**Intent**: Stop excluding admins, and project each account's roles.

**Contract**: the `adminIds` subquery and the `Where(u => !adminIds.Contains(u.Id))` filter are
removed; the query returns every account. A join across `UserRoles` and `Roles` projects role names
per user. Role-name matching continues to use `NormalizedName` where a comparison is needed, per
the existing comment. Ordering (`DisplayName`), the status filter and its index usage, and
`AsNoTracking` are unchanged. The class comment that currently explains the admin exclusion is
rewritten to state that the protection now lives solely in `BlockAsync`'s `is_admin` check.

#### 6. Endpoint tests

**File**: `tests/po-prostu-silka.Tests/MemberAdminEndpointTests.cs`

**Intent**: Cover the rules this phase decides, using the existing fixture.

**Contract**: tests for granting to an active account; revoking; idempotency in both directions;
409 `not_active` for a pending target and for a blocked target; 404 for an unknown id; and that the
member list now returns admins with their roles populated.

### Success Criteria:

#### Automated Verification:

- Solution builds warning-free: `dotnet build` from `src/`
- Backend tests pass: `dotnet test` from the repository root
- New endpoint tests cover grant, revoke, idempotency, `not_active`, and 404

#### Manual Verification:

- Granting `Trainer` to an active member succeeds; the member list shows the role
- Granting `Trainer` to the seeded admin account succeeds — FR-003's owner-who-teaches case
- Blocking the admin account is still refused with `is_admin`, proving the guard survived the
  query change
- An account holding only `Trainer` (constructed by hand) is admitted or refused by `ActiveMember`
  exactly as it was before this change — the check that the role-set split did not widen access
- A member who was already signed in when the role was granted keeps working normally; the role
  appears in their principal after `POST /api/auth/refresh`

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before
proceeding to the next phase.

---

## Phase 2: Member screen

### Overview

Surface roles and the grant/revoke action on the admin's member list.

### Changes Required:

#### 1. Client contract

**File**: `src/app/src/app/core/admin/member-admin.models.ts`

**Intent**: Mirror the API's new field and failure reason.

**Contract**: `Member` gains `roles: string[]`. A `TrainerRoleFailure` interface mirrors the API's
record with reason `'not_active'`. The existing contract comments are extended.

#### 2. Client service

**File**: `src/app/src/app/core/admin/member-admin.service.ts`

**Intent**: Call the two new routes.

**Contract**: `grantTrainer(id)` and `revokeTrainer(id)` methods following the file's existing
shape — relative `/api` paths, `encodeURIComponent` on the id, `firstValueFrom`, and no catching, so
a failed mutation reaches the screen.

#### 3. Member screen

**File**: `src/app/src/app/features/admin/members/members.ts`, `members.html`, `members.scss`

**Intent**: Render role badges and offer the role action from a per-row menu.

**Contract**: each row renders its role badges beside the existing status badge. A per-row menu
holds the row's actions; the trainer entry reads "Nadaj rolę Trenera" or "Odbierz rolę Trenera"
depending on the row's roles, and is absent on a row whose status is not `Active`. The mutation
reuses the component's existing `busy` / `failedId` / `notice` signals and its `generation` guard,
exactly as block and unblock do. Admin rows now appear in the list; block and unblock must not be
offered on them.

This is the first menu in the SPA — no existing component has one. It must be operable by keyboard
(open, arrow between entries, `Escape` to close, focus returned to the trigger) and must close on
outside click, since nothing in the codebase provides that behaviour yet.

**Adapted during implementation.** Four points the contract above did not anticipate:

1. **`mutate` was generalised.** Its `becomes: MemberStatus` parameter became `patch: (row) =>
   Member`. A role change does not touch `status`, so the helper could not be reused as written.
   All three existing callers were updated; the generation guard, busy set and 409 handling are
   unchanged.
2. **Outside-click is detected by inspecting the click target**, not by `stopPropagation` in the
   template. The template handler would have had to sit on a plain `div`, which
   `@angular-eslint/template/interactive-supports-focus` and `click-events-have-key-events` both
   reject — correctly, since a non-focusable element with a click handler is unreachable by
   keyboard. The document handler now returns early when the target is inside `.row-menu`. The
   menu container carries `tabindex="-1"` for the same reason.
3. **The screen's hint text was wrong after Phase 1.** It read "Administratorzy nie są tu
   wyświetlani", which Phase 1 made false. Rewritten to say that all accounts appear, that the role
   is granted here, and that an admin cannot be blocked.
4. **The existing component tests had to be rewritten.** Every row action moved behind the menu, so
   the spec's `buttonIn` helper stopped finding them; it was replaced by `menuItemIn`, which opens
   the row's menu first. One assertion moved from the action button to the menu trigger, because
   busy-state now lives on the trigger. `member-admin.service.spec.ts` also needed its `Member`
   fixture updated and gained tests for the two new methods — the contract named only
   `members.spec.ts`.

Also decided during implementation: the `User` role gets **no badge**. Every member holds it, so a
badge on every row distinguishes nothing and only crowds the row at phone width.

#### 4. Component tests

**File**: `src/app/src/app/features/admin/members/members.spec.ts`

**Intent**: Cover badge rendering, the menu's contents, and per-row failure handling.

**Contract**: tests that role badges render from the new field; that the trainer entry's label
follows the row's roles; that it is absent on non-active rows; that block and unblock are absent on
admin rows; and that a failed mutation leaves the row unchanged and surfaces the notice.

### Success Criteria:

#### Automated Verification:

- Frontend tests pass: `npm test` from `src/app/`
- Lint and formatting pass: `npm run quality:check` from `src/app/`

#### Manual Verification:

- The role badge appears on a row immediately after granting, without a manual page reload
- The row menu opens, closes on `Escape` and on outside click, and returns focus to its trigger
- The menu is usable on a phone-width viewport, per the product's mobile-first requirement
- Block and unblock are not offered on an admin row

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful.

---

## Testing Strategy

### Unit Tests:

- Component behaviour in `members.spec.ts`: badge rendering, menu contents per row state, failure
  handling per row.

### Integration Tests:

- `MemberAdminEndpointTests.cs`, against a real SQL Server via the existing Testcontainers fixture:
  grant, revoke, both idempotency directions, `not_active` refusal for pending and blocked targets,
  404 for an unknown id, and admins appearing in the member list with roles.

### Manual Testing Steps:

1. Sign in as the admin and open the member screen; confirm the admin's own account now appears.
2. Grant `Trainer` to an active member; confirm the badge appears without reloading.
3. Grant `Trainer` to the admin account; confirm it succeeds (FR-003).
4. Attempt to block the admin account; confirm it is still refused.
5. Confirm the trainer action is absent on a pending row and on a blocked row.
6. Revoke the role; confirm the badge disappears.
7. Construct an account holding only `Trainer` and confirm `ActiveMember` treats it exactly as it
   did before this change.
8. Exercise the menu with the keyboard alone, and at phone width.

## Performance Considerations

The member list gains a join across `UserRoles` and `Roles`. A single club's user table is small
and the list is already unpaginated by deliberate decision, so this is not a hotspot. The status
filter continues to hit the `Status` index.

## Migration Notes

No schema change. `Trainer` is created by the existing seeder on the next start, because
`AdminSeeder` iterates `ApplicationRoles.All` and that array gains the role. Seeding is already
idempotent and runs on every start, so no migration or manual step is required. Rolling back the
code leaves an unused role row in `AspNetRoles`, which is harmless.

## References

- Requirements: `context/foundation/prd-v2.md` — FR-001, FR-002, FR-003, `## Access Control Changes`
- Roadmap item: `context/foundation/roadmap.md` — `S-04`
- Mutation shape to follow (and the reason this change departs from it):
  `src/Application/Members/MemberAdminEndpoints.cs:180-300`
- Admin-exclusion rationale being revised: `src/Infrastructure/Members/MemberQuery.cs:14-25`
- Prior slice that established this surface: `context/archive/2026-09-01-member-management/`

## Open Risks & Assumptions

- **The admin guard drops from two layers to one.** After this change only `BlockAsync`'s
  `is_admin` check prevents the club from blocking its own admin. The check is by role and survives
  a second seeded admin, but it is now the sole protection. No automated test covers it — this was
  a deliberate scoping decision; manual step 4 is the compensating check.
- **The role-set split is the one change that could silently widen authorization.** No automated
  regression test covers `ActiveMember` — also a deliberate scoping decision; manual step 7 is the
  compensating check.
- **A granted role is not visible in the holder's own session immediately.** Accepted, because the
  role confers nothing in this change. A later slice that gives `Trainer` real permissions must
  revisit revocation timing.
- **The row menu is a new UI pattern** with no precedent in this SPA, so its accessibility and
  mobile behaviour are unverified by any existing component.

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Role, policy, and the admin API

#### Automated

- [x] 1.1 Solution builds warning-free: `dotnet build` from `src/` — 831f0e2
- [x] 1.2 Backend tests pass: `dotnet test` from the repository root — 831f0e2
- [x] 1.3 New endpoint tests cover grant, revoke, idempotency, `not_active`, and 404 — 831f0e2

#### Manual

- [ ] 1.4 Granting `Trainer` to an active member succeeds; the member list shows the role
- [ ] 1.5 Granting `Trainer` to the seeded admin account succeeds — FR-003's owner-who-teaches case
- [ ] 1.6 Blocking the admin account is still refused with `is_admin`, proving the guard survived the query change
- [ ] 1.7 An account holding only `Trainer` is admitted or refused by `ActiveMember` exactly as before this change
- [ ] 1.8 An already-signed-in member keeps working; the role appears after `POST /api/auth/refresh`

### Phase 2: Member screen

#### Automated

- [x] 2.1 Frontend tests pass: `npm test` from `src/app/`
- [x] 2.2 Lint and formatting pass: `npm run quality:check` from `src/app/`

#### Manual

- [ ] 2.3 The role badge appears on a row immediately after granting, without a manual page reload
- [ ] 2.4 The row menu opens, closes on `Escape` and on outside click, and returns focus to its trigger
- [ ] 2.5 The menu is usable on a phone-width viewport
- [ ] 2.6 Block and unblock are not offered on an admin row
