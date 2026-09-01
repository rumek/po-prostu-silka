# Frame Brief: Member management (S-02) — is the slice actually blocked?

> Framing step before /10x-plan. This document captures what is *actually*
> at issue, separated from what was initially assumed.

## Reported Observation

Roadmap slice **S-02 `member-management`** (PRD FR-004 "admin can block and unblock
user accounts", FR-005 "admin can browse members in one searchable list with status
badges and a pending / active / blocked filter") carries `Status: blocked` in
`context/foundation/roadmap.md`, and `context/changes/member-management/change.md` is
still `status: new` with no research and no plan. The recorded blocker is Open Roadmap
Question 1 — *"What happens to a blocked member's existing bookings and assigned
plan?"* (`roadmap.md` §Open Roadmap Questions #1; `prd.md:182`).

## Initial Framing (preserved)

- **User's stated cause or approach**: (from the roadmap, not stated inline by the user)
  Open Roadmap Question 1 blocks the slice — *"planning this slice before it resolves
  would guess at domain rules"* (`roadmap.md` §S-02 Unknowns, Block: yes).
- **User's proposed direction**: run `/10x-frame` on the change to test whether the
  block and the way the slice is cut are the right framing before any planning.
- **Pre-dispatch narrowing**: the leading concern is **"is the scope right?"** — whether
  list + filter + block/unblock is the right cut — judged against **the finished MVP**
  (the end state where a blocked member could have real bookings and an assigned plan),
  and treated as **one observation** (the block itself), not a bundle.

## Dimension Map

The observation could originate at any of these dimensions:

1. **Enforcement locus** — does S-02 have to build blocked-access revocation, or did
   F-02 / S-01 already ship it? If already shipped, S-02 inherits no design decision here.
2. **Dependency direction** — OQ1 names bookings and plans; do those entities exist, and
   does S-02's scope actually touch them? If not, the blocker may be mis-attached.
3. **Slice boundary** — is list + filter + block/unblock one coherent cut, or two things
   bolted together?  ← *the scope framing the user chose*
4. **State-model semantics** — is `Blocked` as a flat enum value sufficient for the
   finished MVP, or would answering OQ1 force a change to the account aggregate?

## Hypothesis Investigation

| Hypothesis | Evidence | Verdict |
| --- | --- | --- |
| **1. Enforcement locus** — revocation already fully shipped, S-02 inherits nothing | Mechanism is shipped: login refuses Blocked (`src/Application/Auth/AuthEndpoints.cs:97-101`); `ActiveMember` + `Admin` policies both `RequireClaim(account_status, "Active")` (`src/Infrastructure/Authorization/AuthorizationPolicies.cs:35-42`); claim minted at sign-in and stamp refresh (`src/Infrastructure/Identity/AppUserClaimsPrincipalFactory.cs:24-30`). **But the trigger does not exist** — no code path anywhere sets `Status = Blocked`, and nothing rotates `SecurityStamp`. F-02's plan assigned that obligation forward by name: *"whichever slice implements block (S-02) must also invalidate the cookie via Identity's security stamp"* (`context/archive/2026-08-31-auth-identity-foundation/plan.md:134-139`). | **PARTIAL** — mechanism present, one real obligation left to S-02 |
| **2. Dependency direction** — OQ1 is mis-attached to S-02 | No `Booking`, `Class`, `TrainingPlan`, or `Exercise` type exists in `src/Domain/` or `src/Application/` (full directory enumeration, not grep). `AppDbContext.cs:20-22` has only `OutboxMessages` + `PushSubscriptions`; no such table in any migration. S-02's own outcome line contains no booking or plan verb. No roadmap dependency edge runs between S-02 and S-04/S-07 in either direction. | **STRONG** |
| **3. Slice boundary** — S-02 is mostly an extension of shipped S-01 code | Endpoint group, admin policy, and `Application`→`Infrastructure` query seam already exist (`src/Application/Members/MemberAdminEndpoints.cs:37-45,130-133`; `src/Infrastructure/Members/PendingMemberQuery.cs:16-29`). `ApproveAsync` (`MemberAdminEndpoints.cs:59-123`) is a directly reusable status-flip + concurrency pattern. Angular admin guard, HTTP service, routes, and a working list screen with loading/error/empty/per-row-busy states all exist (`core/auth/admin.guard.ts`, `core/admin/member-admin.service.ts`, `features/admin/approvals/approvals.ts:1-99`, `app.routes.ts:19-25`). S-01's plan drew this exact line itself: *"No search, no filter, no status badges, no block/unblock… S-02 owns the full member list"* (`context/archive/2026-09-01-registration-and-approval/plan.md:132-139`). Net-new: two endpoints, one broadened query, one component. | **STRONG** (the cut is sound; the work is small) |
| **4. State-model semantics** — flat enum insufficient, OQ1 forces a model change | Inverse confirmed. Status column and `IX_AspNetUsers_Status` already migrated, and the index was created *for this slice*: `builder.HasIndex(x => x.Status)` — *"S-02's member list filters by status (FR-005)"* (`src/Infrastructure/Persistence/Configurations/ApplicationUserConfiguration.cs:29-30`). `ApplicationUser` carries no block-reason / blocked-at / prior-status field and the PRD asks for none (`prd.md` §Access Control, §Non-Goals). Unblock → `Active` is unambiguous; nothing needs restoring. | **NONE** |

## Narrowing Signals

- **The entities OQ1 asks about do not exist.** Booking, Class, and TrainingPlan are all
  unbuilt (S-03/S-04/S-06/S-07 are `proposed`). The cascade question is not *hard* today
  — it is *undecidable in code*, because there is nothing to cascade over.
- **S-02's outcome line never touches bookings or plans.** "browse all members… block a
  member (who then loses access to app content), and unblock them" — and the substantive
  half of "loses access to app content" is already shipped as D2's login refusal.
- **The persistence shape S-02 needs was pre-built for it.** The status index carries an
  explicit "for S-02, FR-005" comment. A slice whose schema was prepared by an earlier
  slice is not schema-blocked.
- **An unprimed investigation reached the same verdict.** A fresh agent asked only *"what
  would actually stop a developer building FR-004+FR-005 today?"* — with no hypothesis
  named — independently concluded the roadmap blocker is not a code blocker, and surfaced
  two concrete decisions that *are* undecided (below).
- **One counter-signal, weighed and discounted:** `MemberAdminEndpoints.cs:80-81` ties
  unblock to OQ1 in a code comment — *"it would have to answer what happens to their old
  bookings."* This is an echo of the same roadmap premise written in the same session, not
  independent evidence; there is no Booking type for that handler to touch.

## Cross-System Convention

This codebase's convention is that an account-status transition is a flat field flip plus
a concurrency-stamp rotation inside one `SaveChangesAsync`, with idempotency judged from
the current status (`ApproveAsync`, `MemberAdminEndpoints.cs:59-123`). Access consequences
are enforced at read time by policy claims, never by stored cascade state
(`AuthorizationPolicies.cs:35-42`). Block/unblock fits that convention exactly — which is
why the state model needs no change, and why the leading hypothesis (a mis-attached
downstream question, not a structural problem) matches how this system already works.

## Reframed (or Confirmed) Problem Statement

> **The actual problem to plan around is**: S-02 is not blocked by Open Question 1 — the
> question is mis-attached to it, while S-02's two genuinely undecided items sit
> unrecorded in the roadmap and PRD, where a planner would never see them.

Open Question 1 belongs to whichever slice first lands a `Booking` or `TrainingPlan`
aggregate (S-04 / S-07): it asks about consequences on entities that do not exist, so no
answer given today can be validated, and no code S-02 writes depends on it. The roadmap's
*sequencing* instinct was sound — it placed S-02 right after S-01 precisely "so the answer
can land before the booking chain hardens around a guess" — but it converted that sequencing
preference into a hard `Block: yes`, which stalls a slice that is otherwise the most
build-ready in the milestone.

Meanwhile the two items that genuinely require a decision are invisible from `roadmap.md`
and `prd.md`:

1. **Session revocation on block.** A blocked member's live cookie keeps passing the
   `ActiveMember`/`Admin` policy for up to **30 minutes** —
   `SecurityStampValidatorOptions.ValidationInterval = TimeSpan.FromMinutes(30)`
   (`src/Program.cs:118-122`, whose own comment says the number "matters to S-02"). F-02's
   plan explicitly deferred the fix to S-02, and S-01's plan logged the window in its risk
   section — both inside `context/archive/`, cross-referenced from nowhere in the roadmap.
   Build strictly off roadmap + PRD and you ship a block feature that leaves the blocked
   member's session live for half an hour.
2. **The admin's own account in the member list.** The seeded admin is an ordinary
   `ApplicationUser` with `Status = Active` in the same table
   (`src/Infrastructure/Identity/AdminSeeder.cs:57-65`), and there is only ever one.
   `PendingMemberQuery` filters on `Status` alone (`PendingMemberQuery.cs:19-21`) — copy
   that pattern for the full list and the admin appears as a blockable "member" who can
   lock the club out of its own app. Nothing in the PRD, roadmap, or any code comment
   raises this.

Smaller, and decidable with a stated default rather than a question: whether block is
permitted from `Pending` (and if so, where unblock returns the account), and the
idempotency/error contract for double-block — both of which `ApproveAsync`'s `409
not_pending` precedent already suggests a shape for.

## Confidence

**HIGH** — strong evidence on the load-bearing hypothesis (the entities OQ1 asks about
verifiably do not exist, by directory enumeration and migration listing, not inference);
it matches the codebase's own convention for status transitions; the decisive narrowing
signal (the status index pre-built and commented "for S-02") is unambiguous; and an
unprimed independent investigation reached the same verdict while adding a finding the
primed agents missed.

## What Changes for /10x-plan

Plan S-02 now, at its real scope: generalise the shipped pending-approvals surface into an
all-members list with search, status badges, and a pending/active/blocked filter, plus
block and unblock endpoints following `ApproveAsync`'s transition pattern. Treat
**security-stamp rotation on block** and **excluding the admin from the blockable member
list** as first-class plan contracts, not footnotes. Move Open Question 1 off S-02 and onto
S-04 (bookings) / S-07 (plans), where the aggregates it asks about will actually exist —
and flip S-02's roadmap status from `blocked` to `proposed`.

## References

- Roadmap: `context/foundation/roadmap.md` §S-02, §Open Roadmap Questions #1, §At a glance
- PRD: `context/foundation/prd.md:78-81` (FR-004/FR-005), `prd.md:182` (Open Question 1),
  §Access Control, §Non-Goals
- Domain / model: `src/Domain/AccountStatus.cs:12-22`, `src/Domain/ApplicationUser.cs:19-31`
- Enforcement: `src/Application/Auth/AuthEndpoints.cs:97-101`,
  `src/Infrastructure/Authorization/AuthorizationPolicies.cs:35-42`,
  `src/Infrastructure/Identity/AppUserClaimsPrincipalFactory.cs:24-30`,
  `src/Program.cs:118-122`
- Reusable surface: `src/Application/Members/MemberAdminEndpoints.cs:37-45,59-123,130-133`,
  `src/Infrastructure/Members/PendingMemberQuery.cs:16-29`,
  `src/Infrastructure/Persistence/Configurations/ApplicationUserConfiguration.cs:29-30`,
  `src/Infrastructure/Identity/AdminSeeder.cs:57-65`
- Frontend: `src/app/src/app/features/admin/approvals/approvals.ts:1-99`,
  `src/app/src/app/core/admin/member-admin.service.ts`,
  `src/app/src/app/core/auth/admin.guard.ts`, `src/app/src/app/app.routes.ts:19-25`
- Deferred obligations: `context/archive/2026-08-31-auth-identity-foundation/plan.md:134-139`,
  `context/archive/2026-09-01-registration-and-approval/plan.md:132-139, 658-659`
- Related research: none — no `research.md` exists for this change
- Investigations: 4 parallel hypothesis agents (enforcement locus, dependency direction,
  slice boundary, state-model semantics) + 1 unprimed cross-check agent
