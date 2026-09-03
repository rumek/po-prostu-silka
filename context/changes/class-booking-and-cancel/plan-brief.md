# Class Booking and Cancellation — Plan Brief

> Full plan: `context/changes/class-booking-and-cancel/plan.md`
> Research: `context/changes/class-booking-and-cancel/research.md`

## What & Why

A member can browse the schedule but cannot sign up for anything — the club is still running
bookings outside the app, which is the pain that started the product. This slice adds the booking
layer: a member books a spot, cancels it, and sees their upcoming classes; an admin sees who signed
up and can release a spot. It carries the milestone's headline guarantee — a class never accepts
more bookings than it has spots, even when two members tap Book at the same instant. Roadmap
**S-08**, delivering `prd.md` US-01, FR-008 – FR-010, FR-014 and `prd-v2.md` FR-014.

## Starting Point

`Booking` does not exist in any form — no entity, no table, no client code. But the slice was
designed for: `FreeSpots` is a documented placeholder in exactly two expressions, `Class.cs:17-19`
reserved the concurrency decision for this plan, `Program.cs:37-40` spells out what the booking
transaction must avoid, `ClassEndpoints.cs:518-520` names the delete guard this slice adds, and the
Testcontainers fixture exists partly for this slice's concurrency tests. The member schedule itself
is currently unreachable — no nav link anywhere.

## Desired End State

A member reaches the schedule from the top nav, taps a class, and an overlay shows its description —
the first place a class type's description is ever displayed — with one Book button. The tile's spot
count moves without a reload. "Moje zajęcia" lists every upcoming booking with a Cancel action. An
admin opens "Zapisani" under the calendar to see who signed up and release a spot. Deleting a booked
class, and cutting its capacity below the signed-up count, are both refused.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| No-overbooking mechanism | Concurrency token on `Class`, rotated per booking write, with a 3-attempt retry loop | Every booking write serializes through one row, so the capacity race and the double-booking race close together — with no explicit transaction and therefore no `CreateExecutionStrategy`. | Plan |
| Blocked member's bookings | Cascade-cancel future bookings, silently | Frees seats the member cannot use; past bookings stay in history and no mail is sent, matching the existing decision that blocking notifies nobody. | Plan (PRD OQ1, open since S-01) |
| Cancelled booking | A status on the row; re-booking creates a new row | `prd.md` FR-009 requires history, and a member who mis-clicked must be able to fix it — which forces a *filtered* unique index, not a plain one. | Plan |
| Booking window | Refused once the class has started; cancel has no time rule | Symmetric with the existing `starts_in_past` refusal, and the calendar lets members navigate into past weeks; free-cancel-anytime is a locked PRD Non-Goal. | Plan |
| Member booking surface | A detail overlay on the schedule, member-only | Adapts the existing `class-create-overlay`; gives the class description somewhere to live; leaves the admin calendar's `readOnly` contract untouched. | Plan |
| Tile selection | New `selectable` input + `classSelected` output | `readOnly` conflates gestures with actions; selection is a third concept and gets its own name rather than loosening a shared gate. | Plan |
| "Moje zajęcia" | Its own lazy route with a plain list | A calendar shows one day or week at a time — the wrong shape for "my next five classes", and it would pull `angular-calendar` into another chunk. | Plan |
| Admin booking list | Panel below the calendar | Copies the duplicate/delete panel pattern already on that screen; the admin keeps the week they were looking at. | Plan |
| Admin releasing a spot | Allowed | Goes beyond FR-014's "view", but the server-side cancel path exists anyway for the block cascade, and without it the capacity guard has no escape. | Plan |
| Capacity below booked count | Refused with `capacity_below_bookings` | The guarantee has to hold across edits too, not only across bookings. | Plan |
| Unique-violation handling | New `SaveOutcome` seam on `IUnitOfWork` | `TrySaveChangesAsync` catches only `DbUpdateConcurrencyException`, so a unique-index hit is a 500 — the gap three reviews raised and deferred. | Research |
| Booked count in the read path | Correlated subquery, no `Class.Bookings` navigation | Keeps one SQL round trip without hanging a collection off the aggregate that a write path could count through. | Plan |

## Scope

**In scope:** the `Booking` entity and its migration, the concurrency token on `Class`, the
`SaveOutcome`/`DiscardChanges` seams, member book/cancel/my-bookings endpoints, real free spots in
both placeholder expressions, the `has_bookings` and `capacity_below_bookings` guards, the block
cascade, admin list-and-release endpoints, the class-detail overlay, the `/my-classes` route, member
navigation, and the admin bookings panel.

**Out of scope:** every notification (S-09), the `ClassStatus.Cancelled` transition (S-09), dashboards
(S-12), attendance tracking, a waitlist, a cancellation deadline, booking from the admin panel, any
change to the shared `ScheduledClass` projection shape, and restoring bookings on unblock.

## Architecture / Approach

```
Domain/Scheduling/Booking.cs + BookingStatus.cs        the new aggregate
Domain/Scheduling/Class.cs                             + ConcurrencyStamp — the guarantee's mechanism
  └─ Infrastructure/Persistence/Configurations/BookingConfiguration.cs
       IX_Bookings_Class_Member_Active  UNIQUE (ClassId, MemberUserId) WHERE Status = 0
Application/Persistence/IUnitOfWork.cs                 + SaveOutcome, DiscardChanges
Application/Scheduling/BookingEndpoints.cs             member group, mine group, admin group
  └─ Infrastructure/Scheduling/{BookingStore, BookingQuery}
Infrastructure/Scheduling/ClassScheduleQuery.cs        Capacity - <correlated count>
Application/Scheduling/ClassEndpoints.cs               has_bookings, capacity_below_bookings
Application/Members/MemberAdminEndpoints.cs            block cascades into scheduling (deliberate)
app/features/schedule/class-details-overlay/           book + cancel, member only
app/features/my-classes/                               plain list, no calendar
```

The rule running through the server half: **the check is atomic with the write, or it is not a
check.** Read capacity, count bookings, insert, and rotate the class's stamp in one `SaveChangesAsync`
— a lost race is a re-read, not a refusal.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Model, schema and the write path | `Booking`, the token, the migration, member endpoints, the racing-members test | Forgetting to rotate the stamp — the guarantee silently disappears and every test still passes except the race |
| 2. Bookings become visible | Real free spots in both expressions, both admin guards, the block cascade, admin endpoints | Missing one of the two twin `FreeSpots` expressions, so the admin and member views disagree |
| 3. The member's screens | Detail overlay, `/my-classes`, the nav that makes both reachable | Touching the shared calendar's `readOnly` and silently changing the admin panel's behaviour |
| 4. The admin's booking list | "Zapisani" panel with release | Panel state drifting from the tile behind it after a release |

**Prerequisites:** S-07 closed (it is). Docker SQL Server running for migrations and for
`dotnet test` (Testcontainers).
**Estimated effort:** ~4 sessions, one per phase; Phase 1 is the largest and the only one carrying
correctness risk.

## Open Risks & Assumptions

- **The whole guarantee rests on one line.** If a future write path changes how many spots are taken
  without rotating `Class.ConcurrencyStamp`, the protection vanishes with no failing test except the
  race. The doc comment on the property is the only barrier; the race test is the only detector.
- **Blocking now reaches across bounded contexts.** `BlockAsync` gaining `IBookingStore` is a
  deliberate exception to the repo's recorded "no stored cascade state" convention, chosen by the
  product owner. If it grows further, the honest fix is a domain event, which this codebase does not
  have.
- **The retry loop is bounded at three attempts.** Under a burst larger than a small club produces, a
  member could see `conflict` rather than a real answer. Acceptable for dozens of members; revisit if
  the club grows.
- **Admin release goes beyond FR-014.** It is scope this plan added on purpose; if it is unwanted, cut
  it from Phase 2 §6 and Phase 4 and the rest stands.
- **A released or cascaded spot notifies nobody** until S-09 exists. A member can lose a booking and
  only find out by looking.

## Success Criteria (Summary)

- Two members racing for the last spot: exactly one gets it, the other is told the class is full, and
  the database holds no more Active bookings than the class has spots.
- A member books, cancels and re-books the same class from their phone, and "Moje zajęcia" always
  matches what the schedule shows.
- An admin sees who signed up, releases a spot, and cannot delete a booked class or shrink it below
  the number of people already in it.
