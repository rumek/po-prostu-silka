# Class Change Notifications — Plan Brief

> Full plan: `context/changes/class-change-notifications/plan.md`

## What & Why

When a class is cancelled or moved, the club currently has no reliable way to reach the people who
signed up — the single most-felt pain in the PRD, and the reason this slice is the milestone's
declared north star (roadmap S-09). This makes cancellation a visible state transition rather than a
delete, and guarantees that every booked member is told by email and by push within minutes of the
admin's action.

## Starting Point

Everything this needs already exists and is deliberately unused. F-03 shipped the outbox transport
with retry, backoff and a delivery worker, and `AccountApprovedNotification` is its one working
consumer. S-03 defined `ClassStatus.Cancelled` and wrote a member-schedule query that already filters
it out; nothing has ever set it. S-08 built the booking aggregate, the `ConcurrencyStamp` mechanism
protecting the no-overbooking guarantee, and a per-class active-bookings query that is exactly the
recipient list needed here. There is no cancel endpoint — only a delete, which refuses once a class
has been booked, with a comment naming this slice as the thing that owes those members a message.

Research also turned up two defects the roadmap item does not mention: the member's bookings query
never looks at the class's status, and **push notifications are structurally unable to appear** — the
payload is shaped `{ title, body }` where the Angular service worker requires
`{ notification: { title … } }`, and no component in the SPA has ever called `PushService`, so nobody
has ever been asked for permission.

## Desired End State

A class somebody signed up for offers the admin "Odwołaj" where an empty one offers "Usuń";
confirming names the class, says how many people will be told, and warns that it cannot be undone.
The class moves to `Cancelled`, leaves the member schedule and "Moje zajęcia", stays on the admin
calendar, and keeps every booking row intact. Each booked member gets an email naming the class, its
date and its club-local time, and — on any device that granted permission — a push notification that
actually appears and opens the app when tapped. Moving a booked class's time, duration or trainer
sends the same kind of message, stating the old value and the new one.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Which edits notify | Start time, duration, instructor — not capacity | Exactly the fields a member sees in "Moje zajęcia"; capacity is administrative and would be noise that erodes trust in every later message. |
| Bookings on cancellation | Stay `Active`; screens filter on `Class.Status` | Cancellation is a state of the CLASS — cascading would record that the member cancelled, which is false. |
| Undo | None; the transition is one-way | The notifications cannot be recalled and the bookings would have to be reconstructed; confirmation carries the weight instead. |
| Member visibility | Cancelled classes disappear from both screens | What the roadmap outcome states, and FR-022 already rejected an in-app notification surface. |
| Atomicity | One `SaveChangesAsync` for transition + all outbox rows | Makes "cancelled but nobody told" unreachable — the failure the outbox exists to prevent. |
| Push payload | Fixed here, in the ngsw-required shape, with a click target | Without it the north-star slice would ship with half its promised delivery silently invisible. |
| Push opt-in | Own explanation screen after login, then the browser prompt | A browser-level "block" is permanent per device, so the undecided member must be able to decline something that isn't the browser's prompt. |
| Message content | Plain text with full details | Keeps `Body` one column serving both channels; HTML would cost a second column, a migration, an `IEmailSender` change and double rendering for three messages. |
| Past classes | Cancellation refused with `class_started` | Telling members a class that already happened is cancelled is disinformation, and it cannot be undone. |
| Admin action placement | "Odwołaj" replaces "Usuń" when the class has active bookings | Keeps a phone-tight tile at four buttons; the wider server-side `has_bookings` refusal gains a route into cancelling so no class becomes unmanageable. |
| Outbox throughput | Leave `BatchSize` at 20; measure and record | A full class clears in one or two 15-second passes against an NFR of "within minutes" — tuning without a measurement is guessing. |
| Test coverage | Fan-out, atomicity, concurrency, edit triggers, read-path visibility | The first three are the correctness core; the triggers are a pure product rule invisible in the code. |

## Scope

**In scope:** the cancel endpoint and state transition; a shared notification service serving both
triggers; the edit trigger on three fields; cancelled classes leaving the member's bookings list; the
push payload repair and its regression test; the pre-permission opt-in flow; the admin's cancel action
and confirmation.

**Out of scope:** un-cancelling; cancelling past classes; a third booking status; capacity-only
notifications; HTML email or a template engine; any in-app notification list or badge (FR-022);
changes to `OutboxOptions`; block/unblock or trainer-facing notifications; a profile notification
toggle (that surface is S-13 and is not built).

## Architecture / Approach

The cancel endpoint mirrors the shape `UpdateAsync` already established — load tracked, validate,
mutate, rotate `ConcurrencyStamp`, enqueue, `TrySaveChangesAsync`, answer `409` on a lost race. A new
Application service, `ClassChangeNotification`, is built as a sibling of `AccountApprovedNotification`
and serves both triggers, because recipient resolution and channel fan-out are identical and only the
rendered text differs. Recipients come from the existing per-class active-bookings query. Rendering
stays at enqueue time, so a retry hours later says what the first attempt said. **No migration:** the
status column, the enum value and the outbox schema all already exist.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Cancellation + fan-out | The cancel endpoint, the notification service, one-save atomicity | The `ConcurrencyStamp` rotation — omit it and a cancel racing the last booking lets both win, silently |
| 2. Change trigger + read paths | Edit notifications; cancelled classes leave "Moje zajęcia" | Old field values must be captured before mutation, or every message reads "18:00 → 18:00" |
| 3. Push that appears | ngsw-shaped payload with a pinned test; the opt-in flow | A wrong payload delivers successfully, is marked `Sent`, and is never seen — invisible without the test |
| 4. Admin cancel action | The tile action, the informed confirmation | An irreversible action on a phone-sized tile; the tile sees only active bookings, the server guard is wider |

**Prerequisites:** F-03 and S-08 are `done`; ACS credentials and VAPID keys configured for real
end-to-end verification; a device able to receive Web Push (an installed PWA on iOS 16.4+, or any
desktop Chrome).

**Estimated effort:** ~4 sessions, one per phase; phase 1 is the largest and carries the correctness
work.

## Open Risks & Assumptions

- Push verification needs a genuinely subscribable device; without one, phase 3's manual criteria
  cannot be honestly checked and the payload test is the only evidence.
- A member who dismissed the prompt and never returns relies on email alone — accepted, since email is
  the channel the "no missed cancellations" guarantee rests on.
- `BatchSize` of 20 is assumed sufficient for this club's class sizes. Phase 1 records a real
  measurement so a later change starts from evidence.
- The `has_bookings` guard being wider than what the tile can see is handled by a route out of the
  refusal rather than by widening the wire contract — if a "has ever been booked" flag is wanted on
  the tile later, that is a deliberate contract change, not a bug fix.

## Success Criteria (Summary)

- An admin cancels a booked class and every member holding a spot is told by email — and on a
  subscribed device by a visible push notification — within minutes, with no way for the cancellation
  to commit without its messages.
- The cancelled class leaves the member's schedule and bookings list while its booking history stays
  intact and visible to the admin.
- Moving a booked class's time, duration or trainer sends the same kind of message, naming what
  changed; changing only its capacity sends nothing.
