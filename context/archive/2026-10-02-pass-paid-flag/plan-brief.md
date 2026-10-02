# Mark a Karnet as Paid or Unpaid — Plan Brief

> Full plan: `context/changes/pass-paid-flag/plan.md`
> Research: `context/changes/pass-paid-flag/research.md`

## What & Why

The club's spreadsheet tracks "Zapłacono?" per karnet and flags "BRAK PŁATNOŚCI"; the app cannot.
The client's one must-have is marking a karnet paid. Issuing and paying are separate facts — a karnet
gates booking (S-16), so tying them would force staff to withhold bookings until money arrives.

## Starting Point

A karnet has validity, an entry count and an issue instant, nothing about money; every pass route is
admin-only and a trainer cannot see karnets at all. The member's Start card and the admin list read
today's covering karnet.

## Desired End State

Admin issues a karnet with an optional "Opłacony" + date; admin or trainer marks any non-staff
member's karnet paid / unpaid later; staff see "Nieopłacony" on the member lists (meaning *any* unpaid
karnet) with an admin filter; a trainer has a read-only "Karnety" child screen; the member sees the
status of today's karnet. Booking is unaffected.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Shape | `PaidAt` (date, null = unpaid) + `PaidRecordedBy` (no FK) | Minimal step; audit copies the attendance precedent | change.md |
| Default at issue | Unchecked | Matches the spreadsheet's empty cell | change.md |
| Who may mark | Admin and trainer, any non-staff member | Matches the already-unscoped trainer member list | change.md / Research |
| Marker meaning | Any unpaid karnet, not just today's | Debt outlives validity | change.md |
| Payment date | Editable, defaults to today, not future, ≤ 400 days old | Records late transfers; sanity bound like `MaxValidityDays` | change.md / Plan |
| Where payment changes | One `PUT /api/passes/{id}/paid` (TrainerOrAdmin); edit PUT ignores payment | A four-field edit can never wipe a payment; one write path | Plan |
| Trainer surface | Child screen `/trainer/members/:id/passes` + badge/link on the list row | A row action on today's karnet can't settle an old debt | Plan |
| Member's view | Status of today's karnet only | No new API; older debt is a staff conversation | Plan |
| Blocked member | May be marked paid | Payment is about money, not access | Plan |
| Undo | Admin and trainer, explicit `null`; recorder overwritten | Fixes mistakes symmetrically; explicit value converges | Plan |
| Visual | Outlined word-only badge | No style-guide amendment, 0 kB eager | Plan |
| Checkbox in form | Bound by hand, no CVA | Keeps the kit unchanged for one caller | Plan |
| Migration | Backfill `PaidAt` = club-local issue day; `Down` drops | Nobody becomes a debtor on deploy | change.md / Research |

## Scope

**In scope:** columns + backfill; issue-time payment; payment route; trainer karnet read; `HasUnpaidPass`
on both lists; admin `unpaid` filter; admin karnet screen status + actions; shared overlay; trainer
child screen; dashboard status; PRD/roadmap note; manual cases; one E2E spec.

**Out of scope:** price, method, history, receipts; any booking gate; notifications; showing who
recorded a payment; member-side indicator for older karnets; trainer list filter; CVA, new glyph,
`--danger`.

## Architecture / Approach

`MembershipPass` gains two nullable columns. `IssuePass` writes them from the POST; `SetPassPaid`
(new, `TrainerOrAdmin`) is the only other writer and never rotates the entry-pool stamp.
`MembershipPassView.PaidAt` flows through all three builders, so the admin screen, the trainer screen
and `/api/passes/mine` agree. Lists add a correlated `EXISTS`. The SPA shares one payment overlay and
status component (`shared/passes/`) between the admin and trainer screens.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Data model and issue path | Columns, backfill, issue with payment, view field | Backfill day off by one if not club-local |
| 2. Payment route and staff reads | Shared write, trainer read, list fact + filter | Authorization matrix / persona symmetry |
| 3. Admin and member screens | Issue checkbox, status + actions, list filter, dashboard | `setValue`/`reset` missing the new controls |
| 4. Trainer's karnet screen | Child screen, row badge + link | Guard ↔ policy drift (S-25) |
| 5. Docs, manual plan, E2E | PRD narrowing, S-34 row, PASS-09+, one spec | Leaving the Non-Goal unamended |

**Prerequisites:** `docker compose up -d`; Node 22+; main at `1963708` or later.
**Estimated effort:** ~3–4 sessions across 5 phases.

## Open Risks & Assumptions

- A member granted Trainer keeps old karnets that nobody can re-mark (`member_is_staff`) — accepted,
  consistent with "staff hold no member data".
- Blocked members are reachable on the trainer route by URL but not listed — accepted; admin covers them.
- Revoking an unpaid karnet removes the debt with it — unchanged behaviour.

## Success Criteria (Summary)

- Staff can tell at a glance who owes, and settle it in two taps, admin or trainer.
- Editing a karnet never silently changes whether it is paid.
- On deploy every existing karnet reads "Opłacony".
