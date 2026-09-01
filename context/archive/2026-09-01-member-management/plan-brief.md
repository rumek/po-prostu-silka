# Member Management (S-02) — Plan Brief

> Full plan: `context/changes/member-management/plan.md`
> Frame brief: `context/changes/member-management/frame.md`

## What & Why

Build the admin's member management surface: a searchable list of all members with status badges and
a pending / active / blocked filter (FR-005), plus block and unblock (FR-004). The slice was marked
`blocked` in the roadmap on an open question about blocked members' bookings and plans; the frame
brief established that **the question is mis-attached — S-02 can't answer it and doesn't need to,
while its two real decisions were unrecorded**. Those two decisions are what this plan makes
first-class.

## Starting Point

S-01 shipped an admin approvals queue, and the ground under this slice was prepared by earlier work:
the `/api/admin/members` endpoint group with its `Admin` policy, an Application→Infrastructure read
seam, a complete status-transition template in `ApproveAsync`, a working list screen with a Vitest
spec, and a `Status` index added by name *for FR-005*. What is missing is narrow but real: nothing
anywhere sets `Status = Blocked`, and nothing would cut a blocked member's live session if it did.

## Desired End State

An admin opens `/admin/members`, sees every member except themselves, filters by status, types to
narrow, and blocks or unblocks any row with the badge updating in place. A blocked member is refused
at login, and one already signed in loses access within about two minutes rather than half an hour.
The existing approvals queue keeps working untouched.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Is the slice blocked? | No — OQ1 reassigned to S-04/S-07 | `Booking` and `TrainingPlan` don't exist, so the question is undecidable today and constrains nothing S-02 ships. | Frame |
| Slice scope | Extension, not new construction | Endpoint group, query seam, transition pattern, screen pattern and status index all already shipped. | Frame |
| State model | Flat enum unchanged | Nothing needs restoring on unblock; PRD Non-Goals rule out extra lifecycle state. | Frame |
| Session revocation | Rotate security stamp + `ValidationInterval` 30 min → 2 min | Rotating alone still leaves a 30-minute window, because the interval governs how often the rotation is noticed. | Plan |
| Admin protection | Excluded by role in the query **and** refused by the endpoint | The button never renders and the API refuses, so a hand-crafted request can't lock a solo-admin club out. | Plan |
| Block from `Pending` | Allowed; unblock always → `Active` | Lets the admin kill a junk registration without approving it first, with no prior-status column. | Plan |
| Search & filter locus | Status filter server-side, text search client-side | Uses the index that exists for it, while search stays instant with no debounce logic. | Plan |
| Existing approvals screen | Kept; members screen added alongside | Doesn't disturb a shipped, tested screen, and avoids a pending row with no available action. | Plan |

## Scope

**In scope:** all-members query with admin exclusion and status filter; `GET /api/admin/members`;
block and unblock endpoints; security-stamp rotation on block; `ValidationInterval` change; the
`/admin/members` screen with badges, filter, search and per-row actions; Vitest specs; correcting the
stale "S-02 is blocked" comments left in code from S-01.

**Out of scope:** answering PRD Open Question 1 (now S-04/S-07); any schema change or migration;
pagination; block audit trail or reason; replacing claim-based authorization with per-request DB
checks; changes to `/admin/approvals`; a member-facing blocked screen beyond the existing login
refusal.

## Architecture / Approach

`MemberSummary` + `IMemberQuery` in Application, implemented in Infrastructure with a DB-side
projection, a status filter on the existing index, and an anti-join against the `Admin` role. Two new
endpoints join the existing group, each following `ApproveAsync`'s shape — idempotency check,
concurrency-stamp rotation, single `SaveChangesAsync`, lost-race fallback. Block additionally rotates
`SecurityStamp` in the same tracked block, so one save carries all three fields. The Angular screen
mirrors the approvals component's state model, refetching on filter change and filtering in a
computed signal on search.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Member read surface | `MemberSummary`, `IMemberQuery`, `GET /api/admin/members` | The role anti-join is the first multi-table query in this codebase |
| 2. Block, unblock, revocation | Two endpoints + stamp rotation + interval change | The only change that adds recurring DB load; the 2-minute window is verifiable only in real time |
| 3. Members screen | `/admin/members` with badges, filter, search, actions | Approve now lives on two screens and their failure handling must stay consistent |

**Prerequisites:** S-01 complete (done and archived); local SQL Server via `docker compose up -d`;
two browsers for the session-revocation check in Phase 2.
**Estimated effort:** ~2-3 sessions across 3 phases; Phase 3 is the largest.

## Open Risks & Assumptions

- **The 2-minute interval is a judgment call, not a PRD requirement.** The PRD sets no promptness bar
  for blocking. If the added lookups ever matter, the interval is a one-line change.
- **Unblocking a once-pending account silently approves it.** Accepted to avoid a prior-status
  column; the UI must say so on the button or the admin will be surprised.
- **Backend has no test project**, so Phase 1 and 2 verification is `dotnet build` plus manual
  endpoint checks. The block → session-refused path in particular cannot be automated here.
- **The role anti-join assumes admins are identified by role**, not by a flag on the user. True today
  (`AdminSeeder`), and the anti-join keeps holding if a second admin is ever seeded.

## Success Criteria (Summary)

- The admin can find any member by name, email, or status, and can block and unblock them — without
  ever seeing their own account in the list.
- A blocked member is refused at login, and an already-signed-in one loses access within ~2 minutes.
- The approvals queue continues to work exactly as it did before this slice.
