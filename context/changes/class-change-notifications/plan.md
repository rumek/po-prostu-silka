# Class Change Notifications Implementation Plan

## Overview

An admin cancelling a class stops being a delete and becomes a visible state transition; every
member holding an active booking on that class is told by email and by push, within minutes, in the
same unit of work that performed the transition. A material edit to a booked class does the same. On
the way, two defects that make the push half of FR-021 unreachable today are repaired: the payload
shape the Angular service worker requires, and the fact that nothing in the SPA ever asks a member
for notification permission.

This is roadmap slice **S-09**, the milestone's declared north star.

## Current State Analysis

Every piece this slice needs already exists and is deliberately unused.

- **The outbox transport is complete (F-03).** `IOutboxEnqueuer.Enqueue` writes one already-rendered
  row per recipient per channel and does NOT save — the caller owns the unit of work, precisely so an
  enqueue can be atomic with the domain change that triggered it
  (`src/Application/Notifications/OutboxEnqueuer.cs:26`). `OutboxDeliveryWorker` claims, delivers,
  backs off and prunes. `AccountApprovedNotification` is the working precedent for a notification
  built on top of it, and its own doc comment names this slice as the one that would copy its shape.
- **`ClassStatus.Cancelled` is defined and never written.** `src/Domain/Scheduling/ClassStatus.cs`
  documents that S-03 defines the state and this slice adds the transition. The member schedule query
  already filters it out (`src/Infrastructure/Scheduling/ClassScheduleQuery.cs:17`); the admin query
  deliberately does not (`:24`). Neither needs changing.
- **There is no cancel endpoint.** `ClassEndpoints` maps create, update, delete and duplicate.
  `DeleteAsync` (`:584`) refuses with `has_bookings` once the class has ever been booked, and its doc
  comment states outright that taking a booked class off the schedule is a CANCELLATION owed to this
  slice.
- **The recipient list is already a query.** `IBookingQuery.GetForClassAsync` returns
  `ClassBooking(bookingId, memberUserId, displayName, email, bookedAt)` for the active bookings on a
  class — the admin's "Zapisani" list, and exactly the fan-out list this slice needs.
- **`Class.ConcurrencyStamp` is the guardrail every write path must respect.** Its doc comment
  (`src/Domain/Scheduling/Class.cs`) states the rule: every writer that moves either side of the
  capacity inequality rotates the stamp. A cancellation moves the class out of bookable existence
  while a booking may be in flight for its last spot, so it rotates.
- **No migration is needed.** The `Status` column landed with `AddClassSchedule`, `Booking` is
  unchanged, and the outbox schema is untouched. This slice adds behaviour to an existing schema.

Two defects surfaced during research that the roadmap item does not mention:

- **`BookingQuery.GetMineAsync` filters on `Booking.Status` only** (`:24-30`). It never looks at
  `Class.Status`, so a cancelled class would remain in "Moje zajęcia" — contradicting the slice's own
  outcome statement.
- **Push notifications cannot display in their current form.** `WebPushSender.cs:63` serialises
  `{ title, body }`. The Angular service worker calls `showNotification` only for a payload shaped
  `{ "notification": { "title": … } }` (confirmed against Angular's `SwPush` documentation); anything
  else is delivered to the `SwPush.messages` stream and silently never shown. Compounding it,
  `PushService` is referenced by no component anywhere in the SPA — nothing ever requests permission
  or subscribes, so `PushSubscriptions` is empty in practice. Both are in scope here: a slice named
  "email and push" cannot ship with push structurally unable to appear.

## Desired End State

An admin opens the class calendar, and a class somebody has signed up for offers "Odwołaj" where an
unbooked one offers "Usuń". Confirming names the class and states how many people will be told and
that the action cannot be undone. On confirm the class moves to `Cancelled`: it leaves the member
schedule and leaves "Moje zajęcia", stays visible to the admin, and keeps every booking row intact.
Within a minute each booked member has an email naming the class, its date and its time, and — on any
device that granted permission — a push notification that actually appears on the lock screen and
opens the app when tapped. Editing a booked class's start time, duration or trainer sends the same
kind of message, stating the old value and the new one.

Verified by: `dotnet test` (fan-out count, atomicity, the concurrency race, the four edit triggers,
and the read-path filters), plus manual confirmation that a real email arrives and a real push
notification is displayed on a subscribed device.

### Key Discoveries:

- Rendering happens at enqueue time, never at send time — `src/Application/Notifications/OutboxEnqueuer.cs:14`.
  A retry hours later must say what the first attempt said, so message text is frozen into the row.
- `AccountApprovedNotification.cs:44-60` is the exact shape to copy: one row for the email, one row
  per push subscription, recipient carrying the subscription id rather than the endpoint.
- `Class.ConcurrencyStamp` is not bookkeeping, it IS the no-overbooking mechanism — and it only works
  if the write path assigns a new value, because EF issues no UPDATE against `Classes` otherwise
  (`src/Domain/Scheduling/Class.cs`, the `ConcurrencyStamp` comment).
- `UpdateAsync` already calls `TrySaveChangesAsync` and answers `409 conflict` on a lost race
  (`ClassEndpoints.cs:551`) — the cancel path follows that precedent rather than inventing one.
- `BookingEndpoints` already refuses booking a class that started, with reason `class_started`
  (`:212`). Reusing that reason name for the cancel guard keeps one vocabulary across the API.
- `ClubTime` (`src/Domain/Scheduling/ClubTime.cs`) is the only place a timezone is named, and its
  comment says it is deliberately not used on read paths. Rendering a message body is not a read
  path — the server has no browser locale to defer to, so club-local wall clock is the only honest
  choice for text a member reads in an email.
- `OutboxOptions.BatchSize` is 20 with a 15-second poll, and F-03's own comment names this slice as
  the moment to revisit it. Decision: leave it, measure it.

## What We're NOT Doing

- **No un-cancel.** The transition is one-way. A class cancelled by mistake is corrected by creating a
  new one; the notifications already sent cannot be recalled and the bookings would have to be
  reconstructed.
- **No cancellation of past classes.** Refused with `class_started`.
- **No third booking status.** Bookings stay `Active` when their class is cancelled; visibility is
  driven by `Class.Status`.
- **No notification on a capacity-only edit,** and none on a PUT that changes nothing.
- **No HTML email and no template engine.** Plain text, as `AccountApprovedNotification` does, for the
  reason its comment gives: three messages do not justify a second body column, a migration, a change
  to `IEmailSender` and double rendering.
- **No in-app notification list, badge, or read/unread state.** Explicitly rejected by the PRD
  (FR-022).
- **No change to `OutboxOptions`.** Measured and recorded instead.
- **No block/unblock notification, and no notification to the trainer.** Neither is in any PRD.
- **No profile-level notification toggle.** That surface is S-13 and is not built.

## Implementation Approach

The cancellation is a new write path on `ClassEndpoints` that mirrors the shape `UpdateAsync` already
established: load tracked, validate, mutate, rotate the stamp, enqueue, `TrySaveChangesAsync`, answer
`409` on a lost race. The notification itself is a new Application service,
`ClassChangeNotification`, built as a sibling of `AccountApprovedNotification` — same constructor
dependencies, same one-row-per-recipient-per-channel fan-out, same render-at-enqueue discipline. It
serves both triggers, cancellation and change, because the recipient resolution and the channel
fan-out are identical and only the rendered text differs.

The edit trigger is a comparison of three fields captured before mutation against the request. Fields
that a member can see in "Moje zajęcia" are the fields worth a message; capacity is administrative
and stays silent.

The two push repairs are deliberately sequenced together in their own phase so that "push works" is a
single verifiable claim rather than two half-claims spread across the plan.

## Critical Implementation Details

**State sequencing in the cancel handler.** The recipient list must be read BEFORE the status flip is
saved, and the enqueue must happen before `TrySaveChangesAsync`, not after — an enqueue after the save
is a separate unit of work and reintroduces exactly the "cancelled with nobody told" window the
outbox exists to close. The whole handler is one `TrySaveChangesAsync`; there is no explicit
transaction, because `EnableRetryOnFailure` is on and a user-initiated transaction must go through
`Database.CreateExecutionStrategy().ExecuteAsync(...)` or it throws at runtime
(`OutboxEnqueuer.cs:19`).

**Capturing the old values for a change message.** `store.FindAsync` returns a TRACKED entity, so
reading `existing.StartsAt` after assigning the new value yields the new value. The three old values
must be captured into locals before any mutation, or the message renders "18:00 → 18:00".

**Push payload shape is a contract with a component this repo does not own.** The Angular service
worker displays a notification only for `{ "notification": { "title": … } }`. A test that pins the
serialised shape is what stops a future refactor from silently returning push to invisibility — the
failure mode is a message that delivers successfully, is marked `Sent`, and is never seen.

---

## Phase 1: Cancellation as a state transition, with notification fan-out

### Overview

Introduces the cancel endpoint, the shared notification service, and the guarantee that the
transition and its messages commit together or not at all.

### Changes Required:

#### 1. The notification service

**File**: `src/Application/Notifications/ClassChangeNotification.cs` (new)

**Intent**: Render and enqueue the cancellation and change messages for every member holding an
active booking on a class. It is the sibling of `AccountApprovedNotification` and follows its
contract exactly: rendered at enqueue time, one outbox row for each member's email and one for each
of that member's push subscriptions, no save.

**Contract**: An interface `IClassChangeNotification` with two methods — one for a cancellation, one
for a change — each taking the class's rendered identity (type name, start, duration, instructor
display name), the recipient list, and for the change method the previous values of the three
compared fields. Both return `Task` and neither saves. Registered scoped in `Program.cs` beside
`IAccountApprovedNotification` (`Program.cs:166`).

**Adapted during implementation.** The four identity fields travel as one `ClassDescription` record
rather than as four loose parameters, and the change method takes a `ClassDescription` for BOTH the
previous and the current state instead of previous values plus the entity's new ones. Four
positional arguments of which two are strings is a call site where a swapped pair compiles silently,
and the change method would otherwise have taken seven. The record carries the same doc comment the
loose arguments would have needed.

Passing the class's display fields as arguments rather than passing the `Class` entity is deliberate
and mirrors `CreateAsync`'s reasoning at `ClassEndpoints.cs:452`: the occurrence carries neither its
type's name nor its instructor's display name, and after a trainer reassignment the tracked entity's
`Instructor` navigation still points at the previous account.

#### 2. Club-local formatting for message bodies

**File**: `src/Domain/Scheduling/ClubTime.cs`

**Intent**: Add a helper that renders a UTC instant as the club's wall-clock date and time, so an
email says the hour a member will actually turn up for.

**Contract**: A method converting a `DateTimeOffset` to `ClubTime.Zone` and formatting it with the
Polish culture. Extend the type's doc comment: it currently states the type is "deliberately NOT used
on any read path", which stays true — a rendered notification body is not a read path, and unlike a
read path the server has no browser locale to defer to.

#### 3. The recipient query

**File**: `src/Application/Scheduling/BookingEndpoints.cs`, `src/Infrastructure/Scheduling/BookingQuery.cs`

**Intent**: Make the existing active-bookings-for-a-class projection reachable from the cancel and
update handlers.

**Contract**: `IBookingQuery.GetForClassAsync` already returns exactly the needed shape
(`ClassBooking` carries `memberUserId` and `email`) and already filters to `Active`
(`BookingQuery.cs:53`). Reuse it as-is; no new query. If its XML comment scopes it to the admin
screen, widen the comment to name this second caller.

#### 4. The cancel endpoint

**File**: `src/Application/Scheduling/ClassEndpoints.cs`

**Intent**: Add `POST /api/admin/classes/{id}/cancel` — the state transition FR-013 asks for,
replacing delete as the way a booked class leaves the schedule.

**Contract**: Mapped on the existing admin group (`:210`), so it inherits
`AuthorizationPolicyNames.Admin`. Refusals, all reusing the `ClassFailure` record: `404` when the
class does not exist; `409 class_started` when `StartsAt` is not in the future per the injected
`TimeProvider`; `409 already_cancelled` when `Status` is already `Cancelled`; `409 conflict` when
`TrySaveChangesAsync` returns false. Success returns the updated `ScheduledClass`, matching what
`UpdateAsync` returns so the calendar can refresh a tile from the response.

Handler order, which is the load-bearing part: resolve recipients via `GetForClassAsync` → set
`Status = ClassStatus.Cancelled` → rotate `ConcurrencyStamp` → call the notification service → single
`TrySaveChangesAsync`. Cancelling a class with no active bookings is allowed and simply enqueues
nothing.

The rotation is not optional. Per `Class.ConcurrencyStamp`'s doc comment, a cancel and a booking
racing for the same last spot must not both believe they won; the cancel changes whether spots exist
at all, so it rotates like every other writer that moves either side of the inequality.

#### 5. Add the new refusal reasons to the wire contract

**File**: `src/app/src/app/core/scheduling/class-failure.ts`, `class.models.ts`

**Intent**: Keep the SPA's `ClassFailure` union exhaustive, and give the two new reasons Polish
messages.

**Contract**: Add `class_started` and `already_cancelled` to the `ClassFailure` union and to the
message map, following the pattern the S-08 reasons established. `class-failure.spec.ts` covers the
map exhaustively — extend it.

#### 6. Tests

**File**: `tests/po-prostu-silka.Tests/ClassCancellationTests.cs` (new — `ClassEndpointTests.cs` was
already 920 lines, which is the "grows unwieldy" escape this line anticipated)

**Intent**: Pin the fan-out arithmetic, the atomicity, and the concurrency guarantee.

**Contract**: Cancelling a class with N active bookings produces exactly N email rows plus one push
row per subscription across those members, all `Pending`, in the same save as the status flip — with
cancelled bookings and other classes' bookings excluded from the count. A cancel racing a booking for
the last spot leaves exactly one winner, and that test must FAIL if the stamp rotation is removed
(the discipline `BookingEndpointTests` already applies). Cancelling a past class is refused with
`class_started`; cancelling twice is refused with `already_cancelled`. `AccountApprovedNotificationTests`
and `FakeChannels` are the existing harness for asserting on outbox rows.

### Success Criteria:

#### Automated Verification:

- Solution builds warning-free under `<Nullable>enable</Nullable>`: `dotnet build` from `src/`
- All tests pass: `dotnet test` from the repo root
- Cancelling a class with N booked members enqueues exactly N email rows plus the matching push rows
- The status flip and the outbox rows land in one save — neither is observable without the other
- The concurrency test fails when the `ConcurrencyStamp` rotation is removed from the cancel handler
- Cancelling a past class is refused with `class_started`; a second cancel with `already_cancelled`
- No `using Microsoft.EntityFrameworkCore` appears in `Domain` or `Application`
- Frontend lint and format clean: `npm run quality:check` from `src/app/`

**Adapted during implementation.** `npm run quality:check` does NOT pass as a whole, and did not
before this phase either: `features/admin/classes/classes.ts`, `features/schedule/schedule.ts` and
`features/schedule/schedule.spec.ts` fail `prettier --check` at `HEAD`, untouched by this slice.
`ng lint` passes clean across the workspace, and `prettier --check` passes for every file this phase
edited. Reformatting three unrelated committed files was left out rather than folded into a
notification commit; a later change should run `quality:fix` over them on its own.

#### Manual Verification:

- `GET /health` reports a healthy database connection and a healthy outbox
- Cancelling a booked class from an API client flips its status and leaves every booking row `Active`
- The cancelled class disappears from the member schedule and remains on the admin calendar
- With ACS configured, a real email arrives at a booked member's address within a minute, naming the
  class, its date and its club-local time

**Implementation Note**: Pause here for manual confirmation before starting Phase 2.

---

## Phase 2: Change notifications, and cancelled classes leaving the member's screens

### Overview

Wires the second trigger into the existing edit path, and closes the read-path gap that would
otherwise leave a cancelled class sitting in "Moje zajęcia".

### Changes Required:

#### 1. The edit trigger

**File**: `src/Application/Scheduling/ClassEndpoints.cs` — `UpdateAsync`

**Intent**: After a successful edit of a class with active bookings, notify those members when any of
the three member-visible fields moved.

**Contract**: Capture `StartsAt`, `DurationMinutes` and `InstructorUserId` into locals BEFORE the
mutation block at `:522`. After the existing validation and before `TrySaveChangesAsync`, if any of
the three differs from the request, resolve recipients through `GetForClassAsync` and call the change
method on `IClassChangeNotification`. A capacity-only edit, and a PUT that changes nothing, enqueue
nothing. A class already `Cancelled` notifies nothing — there is no live booking to inform.

The existing `capacity_below_bookings` guard already calls `bookings.CountActiveAsync` at `:513`; when
the count is zero there is no recipient list to fetch and the notification step is skipped entirely.

Instructor comparison is on `InstructorUserId`, not on display name — the id is what changed, and the
display name for the message comes from the account `ValidateInstructorAsync` already resolved.

#### 2. Cancelled classes leave the member's bookings list

**File**: `src/Infrastructure/Scheduling/BookingQuery.cs` — `GetMineAsync`

**Intent**: Stop returning bookings whose class has been cancelled.

**Contract**: Add `b.Class.Status == ClassStatus.Scheduled` to the predicate at `:28`. The booking row
itself stays `Active`; visibility is a property of the class. Record in the method's comment that this
filter is the reason bookings are not cascaded — it is the single point where the chosen model can be
silently broken by a future query that forgets it.

The member schedule needs no change: `ClassScheduleQuery.GetScheduleAsync` already filters on
`Scheduled`, and the admin query already deliberately does not.

#### 3. Tests

**File**: `tests/po-prostu-silka.Tests/ClassEndpointTests.cs`, `BookingEndpointTests.cs`

**Intent**: Pin the product rule that lives only in a comparison, and the read-path filter.

**Contract**: Four cases on the trigger — start time, duration and instructor each notify; capacity
alone and an unchanged PUT do not. Two on the read path — a cancelled class drops out of
`GET /api/bookings/mine` while its booking row remains `Active`, and it stays absent from the member
schedule.

### Success Criteria:

#### Automated Verification:

- Solution builds warning-free
- All tests pass
- Editing start time, duration or instructor on a booked class enqueues one message per booked member
- Editing only capacity, or submitting an unchanged PUT, enqueues nothing
- A cancelled class is absent from `GET /api/bookings/mine`, and its booking rows are still `Active`
- The member schedule still excludes cancelled classes

#### Manual Verification:

- Moving a booked class an hour later sends an email stating both the old and the new time
- Reassigning the trainer sends an email naming both trainers
- After cancelling, the class is gone from "Moje zajęcia" without a reload beyond the normal refresh
- A member with two bookings loses only the cancelled one from the list

**Implementation Note**: Pause here for manual confirmation before starting Phase 3.

---

## Phase 3: Push that actually appears

### Overview

Repairs the two independent reasons no member can receive a visible push notification today: the
payload shape, and the absence of any opt-in path.

### Changes Required:

#### 1. Payload shape

**File**: `src/Infrastructure/Notifications/WebPushSender.cs`

**Intent**: Emit the payload the Angular service worker requires in order to display a notification,
and give a tap somewhere to land.

**Contract**: Replace the `{ title, body }` serialisation at `:63` with the documented shape —
`{ "notification": { "title", "body", "data": { "onActionClick"/"url" } } }` — where the destination
is the member's bookings screen. Only `title` is required by the spec; `body` and the click target are
what make the notification useful.

This edits code F-03 shipped and reviewed. Per `context/foundation/lessons.md`, the change is
recorded here in the plan, not only in a commit message: F-03's payload was never wrong on its own
terms, it simply predated any client that had to render it.

**File**: `src/app/src/app/app.config.ts` (verify only)

`provideServiceWorker` is already registered at `:37`, enabled outside dev mode, with `ngsw-config.json`
declaring empty caching groups — push registration, not offline caching. No change expected; confirm
the click-through behaviour against how the registered worker handles the notification data.

#### 2. Pin the shape with a test

**File**: `tests/po-prostu-silka.Tests/` (extend the push coverage)

**Intent**: Make a regression to an undisplayable payload fail the build rather than fail silently in
production.

**Contract**: Assert the serialised push body parses to an object with a `notification` property
carrying a non-empty `title`. This is the one assertion that distinguishes "delivered and shown" from
"delivered, marked `Sent`, and never seen".

#### 3. The opt-in flow

**File**: `src/app/src/app/features/notifications/push-prompt.ts` / `.html` / `.scss` (new), rendered from the authenticated shell

**Intent**: Ask an approved member, once and with an explanation, before triggering the browser's own
permission prompt.

**Contract**: A pre-permission component: shown to an active member who has neither subscribed nor
dismissed it, explaining in Polish why the club wants to notify them. "Włącz" calls
`PushService.subscribe()`, which is the first caller that service has ever had. "Nie teraz" records a
dismissal in `localStorage` and the component asks again on a later session.

The pre-permission step is what makes "Nie teraz" recoverable: a browser-level "block" is permanent
for that device and cannot be undone from the app, so the undecided member must be able to decline
something that is not the browser's prompt.

It must render nothing at all when `PushService.isSupported` is false or `subscribe()` fails —
`push.service.ts:12-20` lists the legitimate reasons (desktop Safari, a non-installed iPhone, a dev
build with the worker off, a server without VAPID keys), and push is best-effort by design because
email is the channel the guarantee rests on.

#### 4. Tests

**File**: `src/app/src/app/features/notifications/push-prompt.spec.ts` (new)

**Contract**: The prompt is hidden when push is unsupported; "Włącz" calls `subscribe()`; "Nie teraz"
hides it for the session and persists the dismissal; a successful subscribe hides it permanently.

### Success Criteria:

#### Automated Verification:

- Backend builds warning-free and all `dotnet test` tests pass
- The push payload test asserts a `notification` object with a non-empty `title`
- Frontend unit tests pass: `npm test` from `src/app/`
- Lint and format clean: `npm run quality:check`
- Frontend builds within budget: `npm run build`

#### Manual Verification:

- A logged-in member sees the explanation prompt once; "Nie teraz" hides it and it does not reappear
  in the same session
- "Włącz" triggers the browser permission prompt and creates a `PushSubscriptions` row
- Cancelling a class the subscribed member booked produces a **visible** notification on that device
- Tapping the notification opens the app on the member's bookings screen
- A browser with push unavailable shows no prompt and no error, and the member still gets the email

**Implementation Note**: Pause here for manual confirmation before starting Phase 4.

---

## Phase 4: The admin's cancel action

### Overview

Puts the transition behind a deliberate, informed click on the calendar tile the admin already uses.

### Changes Required:

#### 1. Service method

**File**: `src/app/src/app/core/scheduling/class.service.ts`

**Intent**: Expose the cancel endpoint.

**Contract**: `cancel(id: string): Promise<ScheduledClass>` posting to
`/api/admin/classes/{id}/cancel`, following the shape of `duplicate` at `:83` and returning the
updated class so the caller can refresh its tile.

#### 2. The tile action

**File**: `src/app/src/app/features/admin/classes/classes.html` / `classes.ts`

**Intent**: Offer "Odwołaj" on a class somebody is signed up for, and "Usuń" on one nobody is, without
adding a fifth button to a tile that is already tight on a phone.

**Contract**: In the `#classActions` template, the fourth button renders as "Odwołaj" when
`row.freeSpots < row.capacity` and as "Usuń" otherwise, each opening its own confirmation panel below
the calendar, alongside the existing duplicate and delete panels. The read-only past-week rule is
unchanged: `[readOnly]="isPast()"` already withholds tile actions.

**The gap this leaves, and how it is closed.** The tile can only see ACTIVE bookings, while the
server's `has_bookings` guard is wider — it refuses a delete once a class has ever been booked,
cancelled bookings included (`ClassEndpoints.cs:584`). A class everybody has since cancelled therefore
shows "Usuń" and is refused. Rather than widening the wire contract with a "has ever been booked"
field, the existing `has_bookings` refusal message gains one action: "Odwołaj zamiast tego", which
opens the cancel confirmation. The dead end becomes one click, and the tile keeps four buttons.

#### 3. The confirmation panel

**File**: `src/app/src/app/features/admin/classes/classes.html` / `classes.ts` / `classes.scss`

**Intent**: Make an irreversible action an informed one.

**Contract**: A panel below the calendar in the shape of the existing delete panel, naming the class
and its date and stating both how many members will be notified (derivable as
`row.capacity - row.freeSpots`) and that the action cannot be undone. On success the calendar
refreshes and the notice line reports what happened. `class_started` and `already_cancelled` render
through the extended failure map from Phase 1.

#### 4. Tests

**File**: `src/app/src/app/features/admin/classes/classes.spec.ts`

**Contract**: A class with active bookings offers "Odwołaj"; one without offers "Usuń". Confirming
calls `cancel` and refreshes. The confirmation states the number of members to be notified. A past
week offers neither. The `has_bookings` refusal offers the route into cancelling.

### Success Criteria:

#### Automated Verification:

- Frontend unit tests pass: `npm test`
- Lint and format clean: `npm run quality:check`
- Frontend builds within budget: `npm run build`
- Backend tests still pass: `dotnet test`

#### Manual Verification:

- A booked class shows "Odwołaj"; an unbooked one shows "Usuń"
- The confirmation names the class, its date, and the number of members who will be told
- Cancelling refreshes the calendar; the class stays visible to the admin marked as cancelled
- Deleting a class whose bookings were all cancelled is refused and offers the cancel route, which works
- A past week still hides both actions
- The whole flow is usable on a phone in the day view

---

## Testing Strategy

### Unit Tests:

- Frontend: the push prompt's four states; the admin tile's action swap and confirmation copy; the
  extended failure-message map.
- `ClubTime` formatting across a DST boundary, so an email never states an hour the club does not
  recognise.

### Integration Tests:

These boot the real app against real SQL Server via Testcontainers (`IntegrationTestFixture`), which
is what makes the concurrency assertions meaningful.

- Fan-out arithmetic: N booked members and their devices produce exactly the matching outbox rows;
  cancelled bookings and other classes are excluded.
- Atomicity: the status flip and the outbox rows are never observable independently.
- Concurrency: a cancel and a booking for the last spot produce exactly one winner, and the test fails
  without the stamp rotation.
- Edit triggers: three fields notify, capacity and a no-op do not.
- Guards: `class_started`, `already_cancelled`.
- Read paths: a cancelled class leaves `GET /api/bookings/mine` and the member schedule while its
  bookings stay `Active`.
- Push payload shape.

### Manual Testing Steps:

1. Seed a class in the future, book it from two member accounts, subscribe one of them to push.
2. Cancel it from the admin calendar; confirm the panel states two members and irreversibility.
3. Confirm both members receive an email within a minute naming the class, its date and its
   club-local time; confirm the subscribed device displays a notification and that tapping it opens
   the bookings screen.
4. Confirm the class is gone from both members' "Moje zajęcia" and from the member schedule, still
   present on the admin calendar, and that both booking rows are still `Active` in the database.
5. Move another booked class an hour later; confirm the email states the old time and the new one.
6. Change only that class's capacity; confirm nothing is sent.
7. Attempt to cancel a class in the past; confirm the refusal message.
8. Cancel a class whose only booking was already cancelled by the member, via the `has_bookings`
   refusal route.

## Performance Considerations

Cancelling a class writes one row per booked member plus one per subscribed device in a single save —
for this club's class sizes, tens of inserts. `OutboxOptions.BatchSize` stays at 20 with a 15-second
poll: a full class clears in one or two passes, i.e. seconds, against an NFR of "within minutes". F-03
flagged this as the number to revisit here; the decision is to measure rather than tune blind.
**Record the observed end-to-end delivery time for a full class in the phase 1 manual verification**,
so a future adjustment starts from a measurement.

The read-path change adds one predicate on an already-joined `Class` row and does not change the
statement count.

## Migration Notes

None. `ClassStatus.Cancelled` and the `Status` column already exist, `Booking` is unchanged, and the
outbox schema is untouched — S-03 and F-03 left this ready deliberately. Existing rows keep
`Scheduled`, which is `0` and the default, so no data is reinterpreted.

Rollback is a plain redeploy of the previous artifact: no schema moves, and classes already cancelled
would simply reappear as `Scheduled` to the code that does not know the state — acceptable, since the
column and the enum value predate this slice.

## References

- Roadmap item: `context/foundation/roadmap.md` — S-09 (the milestone's north star)
- PRD: `context/foundation/prd.md` — US-02, FR-011, FR-013, FR-021, NFR "notification promptness";
  `context/foundation/prd-v2.md` — FR-014
- Notification transport: `context/archive/2026-08-31-notification-delivery-foundation/plan.md`
- The booking aggregate and its concurrency design: `context/changes/class-booking-and-cancel/plan.md`
- Precedent to copy: `src/Application/Notifications/AccountApprovedNotification.cs`
- The guarantee every write path must respect: `src/Domain/Scheduling/Class.cs` (`ConcurrencyStamp`)
- Plan-vs-implementation discipline: `context/foundation/lessons.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Cancellation as a state transition, with notification fan-out

#### Automated

- [x] 1.1 Solution builds warning-free
- [x] 1.2 All tests pass
- [x] 1.3 Cancelling a class with N booked members enqueues exactly N email rows plus the matching push rows
- [x] 1.4 The status flip and the outbox rows land in one save
- [x] 1.5 The concurrency test fails when the `ConcurrencyStamp` rotation is removed
- [x] 1.6 Cancelling a past class is refused with `class_started`; a second cancel with `already_cancelled`
- [x] 1.7 No EF Core reference in `Domain` or `Application`
- [x] 1.8 Frontend lint and format clean

#### Manual

- [ ] 1.9 `GET /health` reports a healthy database connection and a healthy outbox
- [ ] 1.10 Cancelling a booked class flips its status and leaves every booking row `Active`
- [ ] 1.11 The cancelled class disappears from the member schedule and remains on the admin calendar
- [ ] 1.12 A real email arrives within a minute with the class, its date and its club-local time
- [ ] 1.13 The observed end-to-end delivery time for a full class is recorded in this plan

### Phase 2: Change notifications, and cancelled classes leaving the member's screens

#### Automated

- [ ] 2.1 Solution builds warning-free
- [ ] 2.2 All tests pass
- [ ] 2.3 Editing start time, duration or instructor enqueues one message per booked member
- [ ] 2.4 Editing only capacity, or an unchanged PUT, enqueues nothing
- [ ] 2.5 A cancelled class is absent from `GET /api/bookings/mine`, its booking rows still `Active`
- [ ] 2.6 The member schedule still excludes cancelled classes

#### Manual

- [ ] 2.7 Moving a booked class an hour later emails both the old and the new time
- [ ] 2.8 Reassigning the trainer emails both trainers
- [ ] 2.9 After cancelling, the class is gone from "Moje zajęcia"
- [ ] 2.10 A member with two bookings loses only the cancelled one

### Phase 3: Push that actually appears

#### Automated

- [ ] 3.1 Backend builds warning-free and all tests pass
- [ ] 3.2 The push payload test asserts a `notification` object with a non-empty `title`
- [ ] 3.3 Frontend unit tests pass
- [ ] 3.4 Lint and format clean
- [ ] 3.5 Frontend builds within budget

#### Manual

- [ ] 3.6 The prompt appears once; "Nie teraz" hides it for the session
- [ ] 3.7 "Włącz" triggers the browser prompt and creates a `PushSubscriptions` row
- [ ] 3.8 Cancelling a booked class produces a visible notification on the subscribed device
- [ ] 3.9 Tapping the notification opens the app on the member's bookings screen
- [ ] 3.10 A browser without push support shows no prompt and no error, and the email still arrives

### Phase 4: The admin's cancel action

#### Automated

- [ ] 4.1 Frontend unit tests pass
- [ ] 4.2 Lint and format clean
- [ ] 4.3 Frontend builds within budget
- [ ] 4.4 Backend tests still pass

#### Manual

- [ ] 4.5 A booked class shows "Odwołaj"; an unbooked one shows "Usuń"
- [ ] 4.6 The confirmation names the class, its date, and the number of members to be told
- [ ] 4.7 Cancelling refreshes the calendar; the class stays visible to the admin as cancelled
- [ ] 4.8 A delete refused with `has_bookings` offers the cancel route, which works
- [ ] 4.9 A past week hides both actions
- [ ] 4.10 The flow is usable on a phone in the day view
