# Frame Brief: The karnet decides who trains — staff-only booking, no approval gate

> Framing step before /10x-plan. This document captures what is *actually*
> at issue, separated from what was initially assumed.

## Reported Observation

The app runs on the access model M-1 built: a member books themselves into a class, and a new
account passes through admin approval before it can act. The club works differently — the karnet
(membership pass) and the front desk decide who trains. Today nothing stops a member without a
karnet, because no such concept exists.

## Initial Framing (preserved)

- **User's stated cause or approach**: Three moves that are one product decision — remove
  self-service booking, remove admin approval, introduce `MembershipPass` as the booking gate.
  Therefore one milestone, one slice (M-4 / S-16).
- **User's proposed direction**: Plan and implement S-16 as a single change
  `membership-pass-and-staff-booking`; anchors MP-01–MP-07 treated as settled before planning.
- **Pre-dispatch narrowing**: "Nie rozdzielałem tego" — the three moves were carried as one
  indivisible decision, with no view on which is primary. Scope to investigate: all of S-16.
  Observable state today: "nic go nie zatrzymuje" — an active account is full access.

## Dimension Map

The observation could originate at any of these dimensions:

1. **Entitlement modelling** — "may this person train" is not modelled as its own axis; the two
   existing status axes are standing in for it, and a pass is a *third*, orthogonal axis rather
   than a replacement for either.
2. **The booking write path** — the entry pool is a second read-then-write invariant landing
   inside the transaction that already carries the no-overbooking guarantee.
3. **Subtraction, not addition** — `AccountStatus.Pending` may carry load beyond "waiting for
   approval", so deleting the gate removes things nobody intended to remove.  ← where the framing breaks
4. **Slice cohesion** — the three moves may not share a technical dependency, making
   "one product decision" a product claim rather than a technical one.  ← initial framing

## Hypothesis Investigation

| Hypothesis | Evidence | Verdict |
| --- | --- | --- |
| D1: Entitlement is an unmodelled third axis | 18 "may this person do X" decision points across `src/` and the SPA; every one reduces to `AccountStatus`, `MembershipStatus`, role, or class capacity. `Domain/Members/MembershipStatus.cs:14-28` asserts a *closed* two-axis contract ("may this LOGIN be used" × "may this PERSON use the club"); `Infrastructure/Authorization/AuthorizationPolicies.cs:20-24` — "SINCE S-14 EVERY POLICY CHECKS TWO STATUSES". No validity range, allowance or quota exists for training anywhere. The one "valid on a date" shape that exists (`Member.AccessCodeExpiresAt`, `Domain/Members/Member.cs:101-110`) governs account claiming. Policies are claim-based and DB-free by design (`AuthorizationPolicies.cs:15-18`; claims minted in `Infrastructure/Identity/AppUserClaimsPrincipalFactory.cs:42-66`), so a per-class-per-date gate cannot ride the claim mechanism at all. | **STRONG** |
| D2: The write path is where the difficulty concentrates | `BookingEndpoints.cs:253-341` — a 10-attempt discard-and-reread loop rotating exactly ONE stamp (`Class.ConcurrencyStamp`, L309), one `SaveChangesAsync` per attempt (L311), no explicit transaction by design (`Application/Persistence/IUnitOfWork.cs:44-49`). Two-stamp writes exist (`MemberAdminEndpoints.cs:384/400`, `554/569`, `640/651`) but *never inside a retry loop*. Four booking write sites total (`BookingEndpoints.cs:298,438,548`; `Infrastructure/Scheduling/BookingStore.cs:69`) — each would have to return an entry. Concurrency tests race one aggregate only (`BookingEndpointTests.cs:519-541`, `AdminBookingEndpointTests.cs:322-337`); nothing races two independently-guarded aggregates. | **STRONG** (as difficulty, not as the misframing) |
| D3: Approval removal carries hidden, unbudgeted load | `AuthEndpoints.cs:233-234` verbatim: *"There is no rate limiting and no CAPTCHA either: FR-001 names the approval gate itself as the mitigation."* Confirmed against `RateLimitPolicies.cs:14-19` ("The ONLY rate-limited endpoint in this app" — `/forgot-password`) and `AuthEndpoints.cs:139-140` (`/register` is `AllowAnonymous`, no `RequireRateLimiting`). Trainer-grant vetting degrades: `MemberAdminEndpoints.cs:744` refuses `!= Active`, whose *stated* purpose (L146-149) is refusing Pending; with Pending gone only Blocked remains refusable. `IAccountApprovedNotification` (email + push via outbox, `AccountApprovedNotification.cs:19-58`) loses its only trigger, and `UnblockAsync`'s reasoning (L613) depends on that transition being singular. `/register`'s deliberate email-enumeration disclosure is justified *by the approval gate's existence* (`AuthEndpoints.cs:227-232`). `Pending = 0` is the persisted int default (`Domain/AccountStatus.cs:12-22`), and approval is the only transition out of it. | **STRONG** |
| D4: The three moves are technically independent | No file is touched by all three. B (approval) shares **no backend file** with A or C. Overlap is narrow: A∩C = `BookingEndpoints.cs` (`TryBookAsync`); A∩B = `active-member.guard.ts`, `app.routes.ts`. MP-02's admin half is **already shipped** (`BookForMemberAsync` L362-388, `ReleaseAsync` L516-562, delivered in S-14 Phase 7); only the trainer-scoped half is new, and no resource-ownership authorization primitive exists anywhere in `Application` — `TrainerOrAdmin` (`AuthorizationPolicies.cs:78-82`) is global, not class-scoped. Prior multi-move slices (S-14: 10 phases; S-08: 4; S-01: 3) all split by dependency layer and **deferred destructive steps to a later phase or release**. | **PARTIAL** |

## Narrowing Signals

- **Registration is publicly reachable by anyone from the internet** (user, decisive). This converts
  D3 from a theoretical concern into a live one: the app has exactly one open, unmoderated,
  unthrottled door, and the approval gate is the only thing currently behind it.
- **`AccountStatus.Pending` is, to the club, "a formality to delete"** (user). It is *empty as a
  vetting act* — and that is precisely why its second, unnamed job is invisible. The user's read of
  it is accurate about what it does socially and incomplete about what it does structurally.
- **A karnet may exist for a member with no account** (user). This is the decisive one. The karnet
  hangs off `Member`; approval hangs off `ApplicationUser`. They are not the same subject, so the
  karnet cannot be "the gate now" in the sense MP-03 assumes.
- An independent cross-check — an agent given only the observation and never the hypothesis —
  reproduced the anti-abuse finding without being pointed at it, and additionally surfaced two
  items the anchors do not close (listed under *What Changes for /10x-plan*).

## Cross-System Convention

This codebase's own doctrine already answers this class of question, and answers it against the
initial framing. `MembershipStatus.cs:14-28` and `AuthorizationPolicies.cs:20-24` insist that "may
this login be used" and "may this person use the club" are *different questions that both have to
exist*, and that neither implies the other. A karnet asks a third question — "is this person
entitled to train on this date" — which is narrower than both and subject-bound to `Member`. By the
codebase's own convention, adding a third axis is not grounds for deleting a first one.

The one prior decision on record cuts the same way: S-14 split `Member` from `ApplicationUser`
precisely so that club-shaped facts stop hanging off a login. A karnet is a club-shaped fact.
Approval is a login-shaped fact.

## Reframed (or Confirmed) Problem Statement

> **The actual problem to plan around is**: entitlement to train has never been modelled, and
> account status has been standing in for it — but removing admin approval is a *separate* decision
> about who may create an account on a publicly reachable endpoint, and the karnet does not answer
> that question.

MP-01, MP-02, MP-04, MP-05, MP-06 and MP-07 survive the investigation intact. **MP-03 does not**, in
the form it is stated. The M-4 intent line — "the pass is the gate now" — is true of *training* and
false of *account creation*: the two gates stand on different entities (`Member` vs
`ApplicationUser`), and the user confirmed a karnet may exist for a member with no account at all.
Removing approval therefore does not transfer a gate; it deletes one and adds a different one
somewhere else, leaving an open unthrottled registration endpoint with nothing behind it. The change
is still very likely the right product call — the approval step genuinely is an empty formality as a
vetting act — but it has to be planned as its own decision carrying its own deliverable, not carried
along as a consequence of the karnet.

## Confidence

**HIGH** — strong evidence at three of four dimensions, all with file:line; the reframe matches the
codebase's explicitly stated two-axis convention rather than contradicting it; the decisive
narrowing signal (karnet without an account) came from the user; and an independent agent that was
never told the hypothesis reproduced its central finding.

## What Changes for /10x-plan

Plan S-16 as the *addition* of a third axis (MP-01, MP-02, MP-04–MP-07), and treat MP-03 as a
separate decision that must carry its own answer to "what stands behind an open registration
endpoint once approval is gone" — the plan owes a deliverable there, not an assumption. Two
questions the anchors do not close and the plan must:

1. **Pass attribution.** `Booking` carries no link to the pass it was made against
   (`Domain/Scheduling/Booking.cs`). Deriving entries purely from date ranges means editing a pass's
   validity silently reattributes historical bookings.
2. **Trainer scoping.** No resource-ownership check exists anywhere in `Application`; widening the
   admin booking group to `TrainerOrAdmin` without a per-request instructor match would let any
   trainer book any class.

Also stale and worth correcting as part of the stream: `roadmap.md:69-72` (AM-005, "the account is
still created `pending`") directly contradicts MP-03 in the same document, and
`context/foundation/prd.md:140,153` still states the approval lifecycle unqualified.

## References

- Source files: `src/Domain/Members/MembershipStatus.cs:14-28`, `src/Domain/Members/Member.cs:101-130`,
  `src/Domain/AccountStatus.cs:12-22`,
  `src/Infrastructure/Authorization/AuthorizationPolicies.cs:15-24,55-82`,
  `src/Infrastructure/Identity/AppUserClaimsPrincipalFactory.cs:42-66`,
  `src/Application/Auth/AuthEndpoints.cs:139-140,206-213,227-235,323`,
  `src/Application/Auth/RateLimitPolicies.cs:14-19`,
  `src/Application/Scheduling/BookingEndpoints.cs:172-191,253-341,362-388,406-458,516-562`,
  `src/Application/Members/MemberAdminEndpoints.cs:146-149,413-492,518-603,744`,
  `src/Application/Notifications/AccountApprovedNotification.cs:19-58`,
  `src/Infrastructure/Scheduling/BookingStore.cs:46-72`,
  `src/Application/Persistence/IUnitOfWork.cs:44-49`,
  `src/app/src/app/core/auth/active-member.guard.ts:32-39`, `src/app/src/app/app.routes.ts`
- Tests: `tests/po-prostu-silka.Tests/BookingEndpointTests.cs:519-541,554-579,621-644`,
  `tests/po-prostu-silka.Tests/AdminBookingEndpointTests.cs:322-337`
- Related research: none — no `research.md` exists for this change
- Prior art: `context/archive/2026-09-07-member-entity-and-accountless-members/plan.md` (S-14,
  10 phases, destructive drops deferred one release)
- Investigation: four parallel dimension agents plus one independent cross-check agent given the
  observation only
