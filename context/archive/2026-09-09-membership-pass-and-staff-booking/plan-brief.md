# Membership Pass and Staff Booking — Plan Brief

> Full plan: `context/changes/membership-pass-and-staff-booking/plan.md`
> Frame brief: `context/changes/membership-pass-and-staff-booking/frame.md`

## What & Why

Entitlement to train has never been modelled, and account status has been standing in for it — but
removing admin approval is a *separate* decision about who may create an account on a publicly
reachable endpoint, and the karnet does not answer that question. This stream adds the karnet
(`MembershipPass`) as a third access axis and makes it the booking gate, moves booking onto staff
routes, and retires approval as its own decision carrying its own replacement mitigation.

## Starting Point

Two access axes exist and the codebase documents them as a closed set: `AccountStatus` ("may this
login be used") and `MembershipStatus` ("may this person use the club"). Nothing models a
time-bounded or counted entitlement. Booking runs through a single shared retry loop that rotates one
concurrency stamp. Staff booking for admins is already shipped; the trainer half is not, and no
resource-ownership check exists anywhere in the codebase. Approval is the only anti-abuse control on
an open, unthrottled registration endpoint.

## Desired End State

An admin issues a member a karnet — type name, inclusive date range, entry count — and books them
into a class; a trainer does the same for classes they instruct and is refused elsewhere. The member
sees their karnet and entries left on the dashboard, sees their upcoming classes, and can book or
cancel nothing. A booking is refused when no pass covers the class's date or the entries are spent.
Registering produces an immediately active, rate-limited account, and there is no approvals tab.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| Problem framing | Karnet is a third axis; approval removal is separate | Karnet hangs off `Member`, approval off `ApplicationUser` — different subjects, and a karnet may exist with no account | Frame |
| Open registration | Per-IP rate limiting on `/register` | Approval was the only stated mitigation; the `/forgot-password` limiter is a ready pattern | Plan |
| Validity range | `DateOnly` + `date` column | The type should not carry a time a karnet does not have; removes DST ambiguity at range edges | Plan |
| Entry attribution | `Booking.MembershipPassId`, count still derived | Editing a range must not silently reattribute history; no stored counter, so MP-06 holds | Plan |
| Entry-pool concurrency | Second stamp inside the existing retry loop | Keeps both invariants in one atomic `SaveChangesAsync`; the loop is already race-tested | Plan |
| Trainer scoping | Inline instructor check on widened routes | Matches convention — all authorization here is a group policy or a hand-rolled field check | Plan |
| Non-overlap (MP-07) | Handler check + `Member.ConcurrencyStamp` | The protocol `Member` already uses for code-claiming and blocking; no index expresses a range rule | Plan |
| Admin karnet UI | New lazy route `admin/members/:id/passes` | History plus issue form is too much for a row panel, and the members screen is already the densest | Plan |
| Member pass view | Dashboard card | The karnet is the first thing a member wants to know; the dashboard is eager for exactly that reason | Plan |
| Phase order | Add, subtract, drop last | The S-14 shape; keeps the app deployable at every step and honours the rollback rule | Plan |

## Scope

**In scope:** `MembershipPass` entity, schema and admin CRUD; the pass gate and entry pool inside the
booking write path; `Booking.MembershipPassId`; trainer-scoped staff booking; admin karnet screen and
member dashboard card; registration rate limiting and the data migration activating pending accounts;
removal of member self-booking and the whole approval surface; reconciliation of `roadmap.md`,
`prd.md` and `AGENTS.md`.

**Out of scope:** money in any form; unlimited passes; a stored entry counter; removing the
`AccountStatus.Pending` enum member; a trainer-to-member relationship; member-facing pass renewal;
changes to the block/unblock lever; any notification about passes.

## Architecture / Approach

The karnet is a normal entity behind a Store/Query pair, following the codebase's Application-declares
/ Infrastructure-implements seam. Its two invariants are enforced differently because they are
different shapes: **non-overlapping ranges** is an "at most one row per date" rule checked in the
handler and made atomic by rotating `Member.ConcurrencyStamp`; **the entry pool** is an "at most N
rows" rule no index can express, so it joins the capacity check inside `TryBookAsync` and rotates a
second stamp in the same save. Entries left is always `COUNT` of active bookings carrying the pass id
— derived, never stored. Every path that cancels a booking must return the entry by rotating that
pass's stamp, including the block cascade.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Entity and schema | `MembershipPass`, `date` columns, overlap probe | First `DateOnly` mapping in the model — no precedent proves it |
| 2. Admin pass API | Issue, history, edit, revoke; entries derived | Overlap race across two concurrent issues |
| 3. The gate | Pass check and entry pool inside the write path | Two stamps in one retry loop — no precedent; retry exhaustion under contention |
| 4. Trainer scoping | Widened routes with an inline instructor check | The rule lives in handler bodies, so a future endpoint can forget it |
| 5. Karnet on screen | Admin passes route, member dashboard card | Eager dashboard against a 550 kB bundle budget |
| 6. Active registration | Data migration, rate limiter, Active accounts | Ordering: the migration must land before the code stops producing Pending |
| 7. Remove self-booking | Member routes and SPA actions gone | Losing race coverage while moving tests to the admin route |
| 8. Remove approval, fix docs | Approval surface deleted, documents reconciled | Half-removal leaving accounts that log in but pass no policy |

**Prerequisites:** S-01, S-04, S-08 and S-14 are all shipped and archived. Local SQL Server via
`docker compose up -d`; Node 22+ for the Angular workspace.
**Estimated effort:** ~8 phases; the three carrying real novelty are 3, 4 and 6.

## Open Risks & Assumptions

- Two independently-checked concurrency stamps inside a retry loop is a new pattern here. Under
  contention the loop may exhaust its 10 attempts and return 409 more often than the single-stamp
  path does.
- `DateOnly` mapped to a `date` column is unproven in this repo against its `EnableRetryOnFailure`
  configuration; Phase 1 verifies it before anything depends on it.
- The inline trainer check is convention-matching but not structurally enforced — a new endpoint
  added to that group could silently ship without it.
- Bookings made before this stream carry no pass and consume no entry. A member's first karnet is
  therefore not retroactively debited.
- A cancelled class leaves its bookings Active, so those entries stay consumed. Existing behaviour,
  not introduced here, but it will look like a bug to a member the first time it happens.

## Success Criteria (Summary)

- A member with a valid karnet is booked in by staff; one whose karnet ran out is refused — the M-4
  north star, and the refusal is the half that matters.
- Members can see their karnet and their upcoming classes, and can book or cancel nothing.
- Registering produces an active account with no approval step, and the open endpoint is throttled.
