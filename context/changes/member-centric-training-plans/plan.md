# A Member's Plan Is Reached Through the Member — Implementation Plan (S-22)

## Overview

This change moves the training plan off its own axis and onto the member's. Today the plan has its own list at `/trainer/plans`, and that list is a member list in disguise.

- **Admin:** opens Członkowie → a member → **Plan**, at `/admin/members/:id/plan`. This mirrors `/admin/members/:id/passes` (UX-07).
- **Trainer:** gets a member list of their own at `/trainer/members`, with one action: opening a member's plan (UX-08).
- **Retired:** `/trainer/plans`, its API list, the member picker, and both "Plany" nav entries.

FR-015/FR-016 are unchanged in substance. Only the surface they are delivered on changes.

## Current State Analysis

The full map is in `context/changes/member-centric-training-plans/research.md`. The facts this plan rests on:

- **Plans are keyed to the member and not owned.**
  - `TrainingPlan.MemberId` is a Member id (`src/Domain/Training/TrainingPlan.cs:36-40`).
  - `AssignedByMemberId` is display-only (`:52-59`).
  - Any Trainer or Admin may create or edit any plan for any active member (`src/Api/Endpoints/Training/TrainingPlanEndpoints.cs:20-27`).
  - A filtered unique index enforces one active plan per member (`TrainingPlanConfiguration.cs:67-71`).
- **Create replaces, and edit is checked against the stored member.**
  - Create archives the member's current plan and inserts the new one (`CreateTrainingPlan.cs:74-122`).
  - Edit refuses a body whose `MemberId` differs from the stored one with 409 `member_changed` (`UpdateTrainingPlan.cs:54-57`).
- **A by-member read already exists below HTTP.**
  - `ITrainingPlanQuery.FindActiveForMemberAsync(memberId)` (`src/Application/Training/ITrainingPlanQuery.cs:27`) backs `/api/plans/mine`.
  - No route takes a member id.
- **The member API is Admin-only for the whole group, by design.**
  - The policy sits on the group (`MemberAdminEndpoints.cs:55-59`).
  - Its rows carry e-mail, account id, roles and statuses (`MemberSummary.cs:32-41`).
  - Trainers get minimized projections under `/api/trainer/...`, such as `AssignableMember(Id, DisplayName, HasAccount)`. The "no e-mail" guarantee is pinned on the raw response body (`TrainingPlanEndpointTests.cs:471`).
- **The member search lives in `MemberQuery.Searched`.**
  - It matches name OR e-mail under `Latin1_General_100_CI_AI`, with a hand fold of `ł` (`src/Infrastructure/Members/MemberQuery.cs:38`, `:248-265`).
  - Paging bounds and refusals live in `GetMembers.HandleAsync` (`src/Application/Members/GetMembers.cs:42-70`) with `MemberListFailure`.
- **SPA facts.**
  - The builder decides create vs edit from a plan `:id` (`plan-builder.ts:194-199`).
  - It picks the member from `GET /api/trainer/plans/members` (`:233-242`).
  - It hard-codes every exit to `/trainer/plans` (`:339`; `plan-builder.html:3, 11, 256`).
  - `trainerGuard` admits admins (`core/auth/trainer.guard.ts:30`).
  - The admin row menu offers "Karnety" (`members.html:163-170`).
  - "Plany" appears in the header (`app.html:32-34`) and on `/more` (`more.html:43-47`).
- **Every commit on `main` deploys** (`.github/workflows/deploy.yml`). Each phase must therefore leave API and SPA consistent with each other. That is what orders the phases below.

## Desired End State

- **Admin:** Członkowie → row menu **Plan** → `/admin/members/:id/plan`.
  - The screen is the plan builder for that member.
  - With no plan: an empty builder whose submit says "Przypisz plan".
  - With a plan: that plan, editable.
  - Saving keeps the screen, shows a success toast, and (after a create) the screen becomes the edit view.
  - "Wróć do listy członków" goes to `/admin/members`.
- **Trainer (no Admin role):** the header and `/more` show **Członkowie** → `/trainer/members`.
  - The list is paged and searched by name only. It shows active members only.
  - Each row shows the name, "bez konta" where it applies, and the active plan's name or "brak planu".
  - A row opens `/trainer/members/:id/plan`, the same builder.
- **Retired:**
  - `/trainer/plans`, `/trainer/plans/new` and `/trainer/plans/:id` are gone from the SPA.
  - `GET /api/trainer/plans` and `GET /api/trainer/plans/members` are gone from the API.
  - "Plany" is gone from both nav places.
- **Unchanged:** `/api/admin/members*` stays Admin-only. A trainer receives no e-mail, status, phone or account id from any new endpoint, and a test on the raw response body proves it.

Verification: `dotnet test` and `npm test`/`npm run quality:check` are green, and the manual steps in the Testing Strategy pass.

### Key Discoveries:

- `FindActiveForMemberAsync` makes the by-member read a thin handler (`ITrainingPlanQuery.cs:27`, `TrainingPlanQuery.cs:65-70`).
- The `TrainingPlanQuery.Assignable` predicate (`TrainingPlanQuery.cs:58-60`) is the single definition of "a member a plan may be given to". The trainer list reuses it, so a blocked member drops off the trainer's list exactly as they dropped off the picker.
- `EndpointAuthorizationTests.Trainer_routes_require_the_trainer_or_admin_policy` (`:196`) asserts that `/api/trainer/*` is non-empty and that every route there is `TrainerOrAdmin`. New routes under `/api/trainer/members` satisfy it automatically, and `TrainerAdminRoutes` (`:71-78`) needs no edit.
- The retired-route rules are in `context/foundation/test-plan.md:155-176`:
  - assert as the caller who used to be allowed;
  - a GET must not answer JSON;
  - never pin 405 vs 404.
- The builder spec uses `provideRouter([])` and does not assert the post-save target (`plan-builder.spec.ts:97`). Its API-URL pins do move (`:108`, `:240`, `:272`, `:306`).
- `plan-builder.html:76` checks `hasError('memberUnavailable')`, which nothing sets. It goes away with the picker.

## What We're NOT Doing

- **No trainer↔member relationship.** The trainer sees the whole club's active members, as the picker already allowed. S-11 and S-16 stances stand.
- **No role filter on the trainer's list.** It shows the trainer themselves and every admin, because every account has a Member row and `Assignable` does not look at roles. This is the same set the picker offered, and admins train too.
- **No e-mail, status, phone or account id on any trainer surface.** Open Roadmap Question 8 (e-mail on the class roster) stays open and untouched. This slice neither widens nor narrows the roster.
- **No change to `/api/admin/members*` authorization**, and no edit to `TrainerAdminRoutes`.
- **No change to the plan write API** (`POST /api/trainer/plans`, `PUT /api/trainer/plans/{id}`) or its validation. `member_changed` keeps its current meaning.
- **No "new plan instead of this one" action.** Create happens only when the member has no plan. Replacing a plan means editing it, since archived plans are visible nowhere.
- **No server-side change for blocked members.** An admin can still edit a blocked member's plan, and create stays refused with `member_not_active`.
- **No table for the trainer's list** (UX-06 is about the admin's list). Two fields per row fit `app-list`.
- **No preserving of the admin list's `q`/`filter`/`page` state on the back link.** It behaves as passes do.
- **No keyboard reordering, no role-aware bottom nav, no visual redesign** (M-7 exclusions, UX-09).
- **No extraction of the members list's URL-state logic** (`members.ts:38-65`, `:288-383`). The trainer list carries a smaller second copy (no filter, no row actions), recorded here as a known duplicate. Extracting it would put the freshly reviewed S-21 screen back under change for a structural gain nothing needs yet.

## Implementation Approach

The work is additive first, then the consumers switch, then the old routes are retired. Each phase is one deployable commit on `main`:

1. **API:** add the trainer member surface next to the old one. Nothing is removed.
2. **SPA:** reach the builder through a member id; rewire the builder, add both routes, and add the admin's row-menu item.
3. **SPA:** the trainer's member list replaces the Plany screen and both nav entries.
4. **API:** retire the two endpoints nothing calls any more.

Two design choices hold across all phases:

- **Reads move to the member; writes stay by plan id.** `PUT /api/trainer/plans/{id}` keeps protecting a stale tab. If someone replaced the plan in the meantime, the old id is archived and the PUT gets a 404 instead of silently overwriting the new plan. The SPA sends the member id from the URL, so `member_changed` becomes the URL/body agreement check on edit that the roadmap's risk note asks for (`roadmap.md:718-721`).
- **One builder, mounted twice.** The admin route sits behind `adminGuard` and the trainer route behind `trainerGuard`. The mounts differ only by route data naming the list to return to, which keeps "the URL says who can use it" (S-11) true.

## Critical Implementation Details

- **Route order.**
  - `admin/members/:id/plan` must be declared before `admin/members/:id` (`app.routes.ts:56-58`, `:64-66`).
  - On the API, `/api/trainer/members/{memberId:guid}/plan` is a separate route template from `/`, so no ordering trap exists. Keep the literal-before-parameter habit anyway.
- **Search is name-only on purpose.** If the trainer's search matched e-mail as `Searched` does, a trainer could test "does anyone's address contain x" and read the answer from the result count. That makes it an e-mail oracle even though no address is ever returned. A test pins that a phrase present only in an e-mail matches nothing.
- **After a create, the builder switches to edit using the plan id in the response.** Otherwise a second save would POST again, archiving the plan just created and creating another. That would be harmless to data but wrong. `create` returns `TrainingPlanDetail`, so its `id` becomes `editingId`.

## Phase 1: The Trainer's Member API (additive)

### Overview

Add the two trainer-facing reads that the SPA phases consume. Share the member-search internals and the paging validation with the admin list rather than copying them.

### Changes Required:

#### 1. Shared member search internals

**File**: `src/Infrastructure/Members/MemberSearch.cs` (new), `src/Infrastructure/Members/MemberQuery.cs`

**Intent**: Move the collation constant and the `ł` fold out of `MemberQuery` into one internal static helper, and give it a name-only filter the trainer list can use. The admin search and the trainer search can then never disagree on what "matches" means.

**Contract**:
- `internal static class MemberSearch` exposes the collation constant, `Fold(string)`, and `IQueryable<Member> ByName(IQueryable<Member>, string? term)`.
- `ByName` returns the source untouched for a null term.
- `MemberQuery.Searched` keeps its name-OR-e-mail behaviour and uses the shared constant and fold.
- `MemberQuery`'s doc comments about the collation and `ł` move with the code.

#### 2. Shared paging validation

**File**: `src/Application/Members/GetMembers.cs`, `src/Application/Members/MemberListRequest.cs` (new)

The helper lives in `Members`, not in `Paging`. It returns `MemberListFailure`, and both of its callers are member lists. `Application/Paging` stays free of any bounded context.

**Intent**: Extract the page, page-size and search-length checks so the trainer list refuses exactly what the admin list refuses, with the same `MemberListFailure` reasons.

**Contract**:
- One static method takes `(int? page, int? pageSize, string? search)` and returns either a 400 `MemberListFailure` (`invalid_page` / `invalid_search`) or the normalized `(page, size, term)`.
- It keeps the existing semantics: default 25, max 100, max search length 100, whitespace means no search, and the int-overflow guard.
- `GetMembers.HandleAsync` uses it with no behaviour change. Existing `MemberAdminEndpointTests` stay green unmodified.

#### 3. Trainer member row and member plan payloads

**File**: `src/Application/Training/TrainerMemberSummary.cs`, `src/Application/Training/MemberPlan.cs` (new)

**Intent**: Define the minimized shapes a trainer receives.

**Contract**:
- `record TrainerMemberSummary(Guid Id, string DisplayName, bool HasAccount, string? PlanName)`. `PlanName` is the active plan's name or null.
- `record MemberPlan(AssignableMember Member, TrainingPlanDetail? Plan)`.
- The doc comments state that these fields are deliberately all a trainer gets, and cite the PRD privacy NFR, `AssignableMember`'s precedent, and this plan.
- Update `AssignableMember`'s doc comment (`AssignableMember.cs`). It now also carries a member who is NOT assignable (a blocked member read by an admin through `MemberPlan`), so it is "the trainer-safe identity of a member". Fix the "two fields" line; it has held three since S-14.

#### 4. Query methods

**File**: `src/Application/Training/ITrainingPlanQuery.cs`, `src/Infrastructure/Training/TrainingPlanQuery.cs`

**Intent**: Add the paged, name-searched list over the `Assignable` predicate, and a lookup of one member by id regardless of status, for the plan screen's header.

**Contract**:
- `Task<PagedResult<TrainerMemberSummary>> GetTrainerMembersAsync(string? search, int page, int pageSize, CancellationToken)`:
  - applies the `Assignable` predicate, then `MemberSearch.ByName`;
  - counts, orders by `DisplayName` then `Id`, pages, and projects `PlanName` from the member's `Status == Active` plan, as a correlated subquery or a left join.
- `Task<AssignableMember?> FindMemberAsync(Guid memberId, CancellationToken)`: any status, null if the member does not exist.
- While in the file, remove the duplicate `<summary>` at `ITrainingPlanQuery.cs:29-43`.
- The doc comment on `TrainingPlanQuery.Assignable` (`:56`, "shared by the picker") names its new user, the trainer's member list.

#### 5. Handlers and endpoint group

**File**: `src/Application/Training/GetTrainerMembers.cs`, `src/Application/Training/GetMemberPlan.cs` (new); `src/Api/Endpoints/Training/TrainerMemberEndpoints.cs` (new); `src/Api/Program.cs` (mapping, next to `:386-387`)

**Intent**: Expose both reads under a new `/api/trainer/members` group with the `TrainerOrAdmin` policy on the group.

**Contract**:
- `GET /api/trainer/members?search&page&pageSize` returns `PagedResult<TrainerMemberSummary>`, or a 400 `MemberListFailure` from the shared validator.
- `GET /api/trainer/members/{memberId:guid}/plan` returns 200 `MemberPlan` or 404 when no such member exists.
  - The plan comes from `FindActiveForMemberAsync` and may be null.
  - Status is not filtered, because an admin must reach a blocked member's plan (plan decision). A trainer reaching one by a typed id learns only a name.
- The group doc comment explains:
  - why this is not `/api/admin/members` loosened (the policy is set on the whole group, and its rows carry contact data);
  - why the search is name-only (the e-mail oracle).

#### 6. Integration tests

**File**: `tests/po-prostu-silka.Tests/TrainerMemberEndpointTests.cs` (new)

**Intent**: Pin the access, the payload, the scope and the refusals of both routes.

**Contract** — tests, named in the repo's sentence style:
- An anonymous caller gets 401 and a plain member gets 403 on both routes (`EveryRoute` theory). A trainer and an admin get 200.
- `The_member_list_never_carries_an_email_address`, asserted on the raw body. It follows `TrainingPlanEndpointTests.cs:471`.
- `A_phrase_found_only_in_an_email_matches_nobody`.
- The list includes trainers and admins who are active members (`Assignable` has no role filter). This is pinned so it reads as a decision, not an accident.
- A name search folds case, accents and `ł` ("lukasz" finds "Łukasz").
- The list offers active members only: a blocked member is absent, and an accountless active member is present with `HasAccount == false`.
- `PlanName` is set for a member with an active plan, and null for a member without one or whose plan was replaced. Only the active plan counts.
- Paging: `Total` and page slicing are correct, and ordering is by name. `invalid_page` and `invalid_search` return 400.
- Member plan: 200 with `Plan == null` for a member with no plan, 200 with the plan for a member with one, 200 for a blocked member read by an admin, and 404 for an unknown id.

### Success Criteria:

#### Automated Verification:

- Solution builds warning-free: `dotnet build po-prostu-silka.slnx`
- New tests pass: `dotnet test --filter FullyQualifiedName~TrainerMemberEndpointTests`
- Admin member-list tests unchanged and green: `dotnet test --filter FullyQualifiedName~MemberAdminEndpointTests`
- Access surface green with no allowlist edit: `dotnet test --filter FullyQualifiedName~EndpointAuthorizationTests`
- Full suite green: `dotnet test`

#### Manual Verification:

- `GET /api/trainer/members` as the seeded trainer (e.g. via the Scalar/OpenAPI UI or curl with the auth cookie) returns names and plan names, and no e-mail appears anywhere in the response.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: The Plan Is Reached Through the Member (SPA)

### Overview

The builder takes its member from the URL, and the member picker is gone. It is mounted at `/admin/members/:id/plan` and `/trainer/members/:id/plan`. The admin row menu gains **Plan**. The old builder routes go. The old list screen survives this phase only as a bridge: its rows now open the member-scoped route.

### Changes Required:

#### 1. Plan service and models

**File**: `src/app/src/app/core/training/training-plan.service.ts`, `training-plan.models.ts`

**Intent**: Add the member-plan read, and the trainer-list read that Phase 3 consumes, next to the existing calls.

**Contract**:
- `getMemberPlan(memberId): Promise<MemberPlan>` calls `GET /api/trainer/members/{id}/plan`.
- `getTrainerMembers({search?, page?, pageSize?}): Promise<TrainerMemberPage>` calls `GET /api/trainer/members`.
- Types:
  - `MemberPlan { member: AssignableMember; plan: TrainingPlanDetail | null }`;
  - `TrainerMember { id, displayName, hasAccount, planName: string | null }`;
  - `TrainerMemberPage { items: TrainerMember[]; total; page; pageSize }`, mirroring `MemberPage` (`member-admin.models.ts:78`). The SPA has no generic envelope, and making one is not this slice's job. Its doc comment points at `PagedResult.cs` as `MemberPage`'s does.
- Update the `training-plan.service.ts:14,31` doc comments, which describe the list and picker calls, when Phase 3 deletes those calls.
- `getAssignableMembers` and `getAll` stay until Phase 3 and Phase 4 remove their last users.

#### 2. Builder rewired to the member

**File**: `src/app/src/app/features/trainer/plans/plan-builder.ts`, `plan-builder.html`, `plan-builder.scss`

**Intent**: Load by member id instead of plan id; drop the picker; stay on the screen after a save.

**Contract**:
- **On init:** read `:id` (the MEMBER id) and call `getMemberPlan`.
  - A 404 sets a not-found screen state (outlet 4). The words come from `transport-messages`, never a toast.
  - Any other failure sets `loadFailed` with a retry.
  - Member name and "bez konta" come from `member`.
  - A non-null `plan` fills the form and sets `editingId = plan.id`.
- **`memberId` is no longer a form control.** It is the route's id and is sent in the request body on both create and edit. On edit, the server's `member_changed` is the URL/body check.
- **Remove the picker:** `loadMembers`, `members`, `membersFailed`, the `app-select` branch, and the dead `memberUnavailable` check. The member is shown as static text in the page heading.
- **On save success:**
  - after a create, set `editingId` from the returned detail;
  - toast success ("Plan zapisany." on create and edit alike; the words live in the component, like other success toasts);
  - stay on the screen and mark the form pristine.
- **`applyFailure`:** `member_not_found` and `member_not_active` move to the form banner only, since there is no control to name any more (outlet 2). Other branches are unchanged.
  - **Adapted during implementation.** Their sentences in `core/training/training-plan-failure.ts` also changed: the old words ended "Wybierz innego członka", advice to use a picker this phase removes. They now say no plan can be assigned to this person and point back to the member list. The words still come from the table.
- **Back link and cancel** go to the route data's `membersLink`. Header text is "Wróć do listy członków".
- **Title:** "Plan — {displayName}", in the `.panel` header shape `member-passes.html:1-2` uses.
- **Docblock:** update the "THE MEMBER CANNOT BE CHANGED WHILE EDITING" paragraph. The member is now fixed by the URL, and `member_changed` is what catches a body that disagrees with it.

#### 3. Routes

**File**: `src/app/src/app/app.routes.ts`

**Intent**: Mount the builder under both member surfaces and remove the plan-id routes.

**Contract**:
- `admin/members/:id/plan` → `PlanBuilder`: lazy, `[authGuard, adminGuard]`, `data: { membersLink: '/admin/members' }`. Declared before `admin/members/:id`.
- `trainer/members/:id/plan` → `PlanBuilder`: lazy, `[authGuard, trainerGuard]`, `data: { membersLink: '/trainer/members' }`.
- Remove `trainer/plans/new` and `trainer/plans/:id`.
- The `trainer/plans` list route stays until Phase 3.
- The drag-drop chunk comment (`:134-136`) moves with the builder routes.

#### 4. Admin row menu

**File**: `src/app/src/app/features/admin/members/members.html`

**Intent**: Add **Plan** next to **Karnety**, on every row. Karnety's rule is the precedent; the screen itself explains a blocked member's refusal on create.

**Contract**: a `role="menuitem"` link to `['/admin/members', member.id, 'plan']`, with the same markup and keyboard behaviour as the Karnety item (`:163-170`).

#### 5. Bridge on the old list

**File**: `src/app/src/app/features/trainer/plans/plans.html`, `plans.ts`

**Intent**: Keep the old list working for this one deploy only.

**Contract**:
- A row's "Otwórz" goes to `['/trainer/members', row.memberId, 'plan']`.
- The "Nowy plan" header link is removed, because its route no longer exists.
- The whole screen is deleted in Phase 3.

#### 6. Specs

**File**: `plan-builder.spec.ts`, `members.spec.ts`, `plans.spec.ts`

**Intent**: Pin the new builder behaviour and follow the moved links.

**Contract** — `plan-builder.spec.ts`:
- **Member with no plan:** the screen renders the name, submit says "Przypisz plan", and it POSTs with the route's member id.
- **Member with a plan:** the form is filled, and it PUTs `/api/trainer/plans/{planId}` with the route's member id.
- **After a create:** a second save PUTs rather than POSTs, and a success toast appears.
- **A 404 on load:** the not-found state appears, with no toast.
- **`member_changed` / `member_not_active`:** the text appears in the form banner.
- **Back link:** it follows the route data. Both mounts are covered.
- Remove the picker specs.

`members.spec.ts:276`: the row-menu reachability test includes Plan.

`plans.spec.ts`: row link expectations point at `/trainer/members/{memberId}/plan`; the "Nowy plan" expectation is removed.

### Success Criteria:

#### Automated Verification:

- SPA unit tests pass: `npm test` (from `src/app/`)
- Lint and format pass, including the presentational-kit rule: `npm run quality:check`
- Production build succeeds under the bundle budget: `npx ng build` (from `src/app/`)

#### Manual Verification:

- As admin: Członkowie → row menu → Plan on a member with no plan → add two exercises → Przypisz plan. A toast appears, the button now reads "Zapisz zmiany", and reloading shows the plan.
- As admin: open Plan on a member with a plan, edit a weight and save. The member sees the change on `/my-plan`.
- As admin: open Plan on a blocked member who has a plan. The plan displays and saves.
- As trainer: the old Plany list's "Otwórz" lands on `/trainer/members/:id/plan` and saves.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: The Trainer's Member List Replaces Plany (SPA)

### Overview

Add `/trainer/members`, point the trainer's nav at it, and delete the Plany screen along with both of its nav entries.

### Changes Required:

#### 1. Trainer member list screen

**File**: `src/app/src/app/features/trainer/members/trainer-members.{ts,html,scss}` (new)

**Intent**: A paged, name-searched list of active members. Each row shows whether the member has a plan and opens that member's plan.

**Contract**:
- **URL state.** `q` and `page` live in the query string, following `members.ts`'s pattern:
  - `readState`/canonicalization, the debounced search replacing the history entry, and the pager pushing (`members.ts:38-65`, `:189-383`);
  - one load fence;
  - the "page ran out" fallback;
  - no filter chips and no row actions beyond the link.
- **Search box.** It mirrors `members.html:47-56`: a `<label>` wrapping a visually-hidden text span and an `<input type="search">`. That is the list-search precedent, and the kit's lint rule allows it.
- **Pager.** Plain markup copied from `members.html:278-303` (`nav`, two buttons, a `role="status"` range). There is no shared pager. Avoid any class token ending in `-row` outside `li[appRow]`, which the kit rule flags. Media queries go through `@include bp.*`.
- **Kit.** `app-loading`, `app-empty` (no members / no matches), and `app-list` with `li[appRow].card` rows:
  - `.row-name` holds the member's name as a link to `['/trainer/members', id, 'plan']`;
  - `.row-meta` holds "bez konta" where it applies, and `planName` or "brak planu".
- **Failures.** A load failure is a screen state with retry (outlet 4). `invalid_page` / `invalid_search` go through the existing `member-list-failure.ts` table, which is already registered in `failure-contract.spec.ts`, so no new union is added. Update its "both callers" comment (`member-list-failure.ts:7-11`, and `member-admin.models.ts:354-355`) to name the third.
- **Page header.** `.page-header` with "Członkowie", plus a hint line that the list shows active members.

#### 2. Route

**File**: `src/app/src/app/app.routes.ts`

**Intent**: Mount the list and retire the Plany screen.

**Contract**:
- `trainer/members` → `TrainerMembers`: lazy, `[authGuard, trainerGuard]`, declared before `trainer/members/:id/plan`.
- Remove `trainer/plans`.

#### 3. Navigation

**File**: `src/app/src/app/app.html`, `src/app/src/app/features/more/more.{ts,html}`

**Intent**:
- A trainer without the Admin role sees **Członkowie** → `/trainer/members` in the header and in `/more`'s Panel section.
- An admin loses "Plany" in both places and keeps their existing Członkowie → `/admin/members` on `/more`.
- Each role has exactly one path to one member list.

**Contract**:
- New condition `isTrainer() && !isAdmin() && isActive()`, as `more.ts`'s `isTrainerOnly()` and inline in `app.html`.
- The comments explain that this is STRICTER than `trainerGuard`, so it cannot produce the bounce the S-01 F5 comment warns about. Admins are excluded so they are not offered a second member list.
- `canSeePanel` is unchanged. The "Plany" entries and `isTrainerOrAdmin()` are removed if nothing else uses them.
- Update `more.ts:17-20`'s guard-matching comment to name the one deliberately stricter condition and why.

#### 4. Delete the Plany screen and dead client calls

**File**: `features/trainer/plans/plans.{ts,html,scss,spec.ts}` (delete); `core/training/training-plan.service.ts`, `training-plan.models.ts`

**Intent**: Remove the screen and the calls only it and the picker used.

**Contract**:
- Delete `getAll()` and `getAssignableMembers()`, and `TrainingPlanSummary` if nothing else references it.
- `AssignableMember` stays, because `MemberPlan.member` uses it.

#### 5. Specs

**File**: `trainer-members.spec.ts` (new), `app.spec.ts`, `more.spec.ts`

**Intent**: Pin the list's behaviour and the new nav matrix.

**Contract** — `trainer-members.spec.ts`, templated on `members.spec.ts`:
- it renders name, "bez konta" and plan name / "brak planu";
- a row links to `/trainer/members/{id}/plan`;
- typing debounces into `?q=` and sends `search`;
- the pager moves `?page=`;
- a load failure shows the retry state;
- no e-mail text renders (defensive, since the model has none).

`app.spec.ts` (`:128-238`) and `more.spec.ts` (`:92-137`) matrices:
- trainer: Członkowie `/trainer/members`, no `/trainer/plans`;
- admin: no `/trainer/plans` and no `/trainer/members`, while `ADMIN_HREFS` still includes `/admin/members`;
- inactive trainer and plain member: neither link.
- **Admin + Trainer (a new fixture in both specs; none exists today).** No `/trainer/members` link anywhere, and `/more` still shows `/admin/members`. This is the only user for whom `!isAdmin()` does anything, so it must have a test.
- **Every absence assertion is retargeted.** Today's tests asserting that `a[href="/trainer/plans"]` is absent (`app.spec.ts:128, :147, :183, :225`; `more.spec.ts:99, :137`) would stay green while testing nothing. Each is rewritten to assert against `/trainer/members` instead.
- The tests that fail outright are rewritten to the new matrix: `app.spec.ts:164` and the `it.each` at `:206`, where the admin case flips to "no link", and `more.spec.ts:102`, `:112`.

### Success Criteria:

#### Automated Verification:

- SPA unit tests pass: `npm test`
- Lint and format pass: `npm run quality:check`
- Production build succeeds, with the initial bundle noted against the 600 kB warning: `npx ng build`
- No reference to `/trainer/plans` remains in the SPA: `grep -rn "trainer/plans" src/app/src` returns only API URLs (`/api/trainer/plans`)
  - **Adapted during implementation.** The grep also matches three kinds of line that are not routes. These are the builder's import path (`features/trainer/plans/plan-builder`, since the file was not moved), comments naming the retired screen, and spec assertions that `a[href="/trainer/plans"]` is absent. Each of those assertions sits next to one that checks the new link, so none of them passes vacuously. No route and no link targets `/trainer/plans`. Measured initial bundle: 520.33 kB.
  - **Also adapted:** `getById` was deleted with `getAll`/`getAssignableMembers`. Its only caller was the plan-id builder load that Phase 2 replaced.

#### Manual Verification:

- As trainer on desktop, header Członkowie opens the list. Searching "lukasz" finds "Łukasz". Searching part of someone's e-mail finds nothing. A row opens that member's plan, and Back returns to the same search and page.
- As trainer on a phone-width viewport, `/more` → Członkowie works and the list is readable.
- As admin, neither the header nor `/more` shows Plany, and `/more` → Członkowie still reaches `/admin/members`.
- Typing `/trainer/plans` in the address bar does not render the old screen.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 4: Retire the Old Plan-List Endpoints (API)

### Overview

Remove `GET /api/trainer/plans` and `GET /api/trainer/plans/members`, which no longer have consumers, and move the tests that read them onto the new routes.

### Changes Required:

#### 1. Endpoints, handlers, queries

**File**: `src/Api/Endpoints/Training/TrainingPlanEndpoints.cs`; `src/Application/Training/GetTrainingPlans.cs`, `GetAssignableMembers.cs`, `TrainingPlanSummary.cs` (delete); `ITrainingPlanQuery.cs` / `TrainingPlanQuery.cs`

**Intent**: Delete the two routes and anything that only they used.

**Contract**:
- Remove `plans.MapGet("/", …)` and `plans.MapGet("/members", …)` and their comment.
- Delete `GetActiveAsync` and `GetAssignableMembersAsync` from the query.
- Keep the `Assignable` predicate, which `IsAssignableAsync` and `GetTrainerMembersAsync` use, and keep the `AssignableMember` record.
- Update the group doc comment to say where the list moved: `/api/trainer/members`.
- Fix the drifted index name in comments (`IX_TrainingPlans_Member_Active` → `IX_TrainingPlans_MemberId_Active`, at `CreateTrainingPlan.cs:24,111` and `TrainingPlanEndpointTests.cs:13,313`).
- Remove the `<see cref="GetAssignableMembersAsync"/>` at `ITrainingPlanQuery.cs:40`. It goes stale without a warning, because the build does not generate a documentation file.
- Update the test DTO comments at `TrainingPlanEndpointTests.cs:59, :69` that describe the list and picker payloads.

#### 2. Tests

**File**: `tests/po-prostu-silka.Tests/TrainingPlanEndpointTests.cs`, `MyPlanEndpointTests.cs`

**Intent**: Follow the retirement, and keep the assertions that still say something.

**Contract**:
- `EveryRoute` drops `GET /` and `GET /members`.
- `The_list_counts_items_without_returning_them`, `The_member_picker_offers_active_accounts_only`, `The_member_picker_never_carries_an_email_address` and `The_members_route_is_not_swallowed_by_the_id_route` are deleted. Their guarantees now live in `TrainerMemberEndpointTests` (Phase 1).
- A new `The_retired_plan_list_and_picker_answer_no_json` test, per `test-plan.md:155-176`:
  - as the seeded trainer, `GET /api/trainer/plans` and `GET /api/trainer/plans/members` are not successful JSON;
  - no 405 vs 404 pin.
- `MyPlanEndpointTests.Blocking_refuses_the_member_but_leaves_the_plan_standing` (`:384-398`) reads the plan through `GET /api/trainer/members/{memberId}/plan` as admin, asserting that the plan is still there.
- **Two more tests read the retired list, and neither may simply lose its assertion:**
  - `Assigning_again_replaces_the_previous_plan` (`TrainingPlanEndpointTests.cs:290`) asserts through `GET /api/trainer/members/{memberId}/plan` that the member's plan IS the second POST's plan, by id and name.
  - `Concurrent_assignments_leave_exactly_one_active_plan` (`:343`) is the suite's core invariant test. It must keep asserting a **count**. The member-plan read returns one plan by construction and cannot prove there are not two. It counts `TrainingPlans` rows with `Status == Active` for the member directly through the fixture's DbContext scope, the way other tests seed through it.

### Success Criteria:

#### Automated Verification:

- Build warning-free: `dotnet build po-prostu-silka.slnx`
- Plan tests green: `dotnet test --filter "FullyQualifiedName~TrainingPlanEndpointTests|FullyQualifiedName~MyPlanEndpointTests|FullyQualifiedName~MemberClaimTests"`
- Access surface green (the `/api/trainer/*` set is still non-empty): `dotnet test --filter FullyQualifiedName~EndpointAuthorizationTests`
- Full suite green: `dotnet test`
- No server reference to the removed handlers: `grep -rn "GetAssignableMembers\|GetTrainingPlans\b" src tests` returns nothing

#### Manual Verification:

- After deploy, the trainer and admin flows from Phases 2 and 3 still work end to end against the deployed API.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful.

---

## Testing Strategy

### Unit Tests (SPA, Vitest):

- Builder: member-from-URL load for both mounts; create-then-edit switching; the four failure outlets (field outlet no longer used for the member; banner, toast on success, screen state on 404/load failure).
- Trainer list: URL state, debounce, pager, rendering, links.
- Nav matrices for trainer-only, admin, admin+trainer, inactive and plain member.

### Integration Tests (xUnit, Testcontainers SQL Server):

- `TrainerMemberEndpointTests`: access, the no-e-mail raw-body pin, the e-mail-oracle negative, the `ł` fold, the active-only scope, `PlanName` reflecting only the active plan, paging refusals, and member plan returning 200 with or without a plan and 404 for an unknown member.
- Retired-route test for the two removed GETs.
- `MyPlanEndpointTests` blocked-member case moved to the new read.

### Manual Testing Steps:

1. As admin, assign a plan to a member with none via Członkowie → Plan; then edit it; confirm on the member's `/my-plan`.
2. As admin, open a blocked member's Plan: the plan shows and saves. On a blocked member with no plan, Przypisz plan shows the `member_not_active` sentence in the banner.
3. As trainer, open Członkowie from the header. Search by a name with `Ł`, search by part of an e-mail (no results), open a plan, and return with Back to the same search.
4. Two tabs on the same member's plan: save in tab A, then save in tab B. Tab B's save succeeds and its content wins, the same last-write-wins behaviour on edit as today. The stale-id 404 path, where a plan was replaced underneath the tab, can no longer be triggered from the UI and stays covered by the existing archived-plan API test (`TrainingPlanEndpointTests.cs:392`).

## Performance Considerations

- The trainer list is one COUNT plus one page query over `Members`, with a correlated lookup of the active plan's name per row on the page. `IX_TrainingPlans_MemberId_Active` makes that lookup a seek.
- The name search is a scan, the same accepted cost as the admin list, bounded by the debounce.
- The SPA bundle: the list is lazy, and the builder's drag-drop chunk stays lazy. No eager growth is expected beyond the nav change.

## Migration Notes

- No schema change and no migration.
- Deploy order is the phase order.
- **The Phase 2 and Phase 3 commits are pushed to `main` together.** Phase 2 removes `/trainer/plans/new`, and the bridged Plany list only leads to members who already have a plan. Deployed alone, Phase 2 would leave trainers unable to create a plan until Phase 3 lands. Each phase keeps its own commit and its own verification; only the push is joined. The bridge still matters for review, since it keeps Phase 2's commit self-consistent.
- An SPA tab left open across the Phase 4 deploy that still runs pre-Phase-3 code shows the old list's load-failure state until it is reloaded. This is accepted, as in S-21.
- Rollback of any phase is a redeploy of the previous artifact. Only Phase 4 removes routes, and nothing in the Phase 3 SPA calls them.

## References

- Research: `context/changes/member-centric-training-plans/research.md`
- Roadmap slice: `context/foundation/roadmap.md:695-722`; anchors UX-07/UX-08 at `:120-127`; Open Question 8 at `:798`
- Passes precedent: `src/app/src/app/features/admin/members/member-passes.ts:34-152`; S-16 contract `context/archive/2026-09-09-membership-pass-and-staff-booking/plan.md:525-550`
- Minimized trainer projection precedent: `src/Application/Training/AssignableMember.cs:22`, `tests/po-prostu-silka.Tests/TrainingPlanEndpointTests.cs:471`
- URL-state list pattern: `src/app/src/app/features/admin/members/members.ts:38-383`
- Retired-route test rules: `context/foundation/test-plan.md:155-176`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: The Trainer's Member API (additive)

#### Automated

- [x] 1.1 Solution builds warning-free — f26c082
- [x] 1.2 New TrainerMemberEndpointTests pass — f26c082
- [x] 1.3 Admin member-list tests unchanged and green — f26c082
- [x] 1.4 Access surface green with no allowlist edit — f26c082
- [x] 1.5 Full suite green — f26c082

#### Manual

- [ ] 1.6 Trainer member list returns names and plan names with no e-mail

### Phase 2: The Plan Is Reached Through the Member (SPA)

#### Automated

- [x] 2.1 SPA unit tests pass — 0ae4019
- [x] 2.2 Lint and format pass — 0ae4019
- [x] 2.3 Production build succeeds under the bundle budget — 0ae4019

#### Manual

- [ ] 2.4 Admin assigns a plan to a plan-less member through Członkowie → Plan
- [ ] 2.5 Admin edits an existing plan and the member sees it
- [ ] 2.6 Admin opens and saves a blocked member's plan
- [ ] 2.7 Trainer's bridged Plany row opens the member-scoped builder

### Phase 3: The Trainer's Member List Replaces Plany (SPA)

#### Automated

- [x] 3.1 SPA unit tests pass
- [x] 3.2 Lint and format pass
- [x] 3.3 Production build succeeds with bundle size noted
- [x] 3.4 No SPA reference to /trainer/plans remains

#### Manual

- [ ] 3.5 Trainer desktop flow: header Członkowie, ł search, e-mail search finds nothing, Back keeps state
- [ ] 3.6 Trainer phone flow via /more
- [ ] 3.7 Admin sees no Plany and still reaches /admin/members
- [ ] 3.8 /trainer/plans no longer renders the old screen

### Phase 4: Retire the Old Plan-List Endpoints (API)

#### Automated

- [ ] 4.1 Build warning-free
- [ ] 4.2 Plan, my-plan and member-claim tests green
- [ ] 4.3 Access surface green
- [ ] 4.4 Full suite green
- [ ] 4.5 No server reference to the removed handlers

#### Manual

- [ ] 4.6 Trainer and admin flows work end to end after deploy
