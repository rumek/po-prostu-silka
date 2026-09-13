---
date: 2026-09-13T22:05:37+02:00
researcher: Claude (Opus 5)
git_commit: 66b3480d881c73330d8c0117210c7ea6f3381a75
branch: main
repository: po-prostu-silka
topic: "Ground rollout Phase 4 of test-plan.md — class-change notification fan-out (risk #5)"
tags: [research, codebase, notifications, outbox, fan-out, class-cancellation, integration-tests]
status: complete
last_updated: 2026-09-13
last_updated_by: Claude (Opus 5)
---

# Research: Class-change notification fan-out (test-plan risk #5)

**Date**: 2026-09-13T22:05:37+02:00
**Researcher**: Claude (Opus 5)
**Git Commit**: 66b3480d881c73330d8c0117210c7ea6f3381a75
**Branch**: main
**Repository**: po-prostu-silka

GitHub permalinks were not generated: `gh` is not authenticated on this machine. References are local
paths at the commit above.

## Research Question

Ground rollout Phase 4 of `context/foundation/test-plan.md` — risk #5: *a class is cancelled or changed
and a booked member receives no email or push — or someone not booked receives one.*

Verify, not accept, the response guidance: prove that cancelling or changing a class queues exactly one
message per channel for each actively booked member with an address or device, none for anyone else,
and that a failed send is retried rather than lost; challenge "an outbox row written means the member
was notified"; avoid asserting only on an outbox count and avoid mocking the dispatcher. Establish the
fan-out rule, which edits trigger it, exactly-once semantics, and the retry path.

## Summary

1. **The risk is real, and most of its surface is already tested — by count.** `ClassCancellationTests`
   pins both triggers, the silent edits, re-cancel, atomicity and the cancel/booking race. What it never
   asserts is *who* a row is addressed to: every fan-out assertion counts rows per channel. A recipient
   query that swapped a booked member for a released one would keep the counts and stay green.
2. **Members without an account are untested in the fan-out.** Since S-14 a booking can belong to a
   desk-recorded member with no login and possibly no email. The code handles both (email when on file,
   never push, nothing when blank), and no notification test arranges one.
3. **The integration host runs the real delivery worker — confirmed empirically.** It starts with
   production senders that are unconfigured in tests, so every fan-out row is dead-lettered within
   ≤15 s of being written. Existing assertions on `Pending` hold only because they read in milliseconds.
   This also blocks the one test that would actually challenge "a row written means the member was
   notified": pushing real fan-out rows through the worker with fake channels would race the host's
   worker for the same rows. **Disabling the hosted worker in the test factory is a precondition** for
   that test.
4. **"A failed send is retried rather than lost" is overstated.** Transient failures retry on a 5-step
   backoff and then dead-letter; permanent failures dead-letter on the first attempt by design. Nothing
   is silently dropped — `Failed` rows are never pruned and `/health` reports them — but "retried rather
   than lost" is not what the code promises, and the retry mechanics are already fully tested on
   synthetic rows.
5. **Cheapest useful layer: integration, as planned.** Three increments in rising cost: recipient-
   identity assertions on the existing fan-out shape; accountless-member cases; one end-to-end pass of
   real fan-out rows through the worker with fake channels, behind the test-host change.

## Detailed Findings

### The fan-out rule (who is owed a message)

- **Recipients are the ACTIVE bookings on the class**, ordered by booking time
  (`src/Infrastructure/Scheduling/BookingQuery.cs:61-72`):
  `Where(b => b.ClassId == classId && b.Status == BookingStatus.Active)`. The class's own status is not
  filtered here — correct for both callers, because cancel reads the list *before* flipping the status
  and edit only asks on a `Scheduled` class.
- **Email comes from the member record**, not the account: `b.Member!.Email ?? string.Empty`
  (`BookingQuery.cs:70`). `UserId` comes from the member too (`:68`), null for a member with no login.
- **Email rows**: one per recipient whose email is non-blank; blank is skipped rather than enqueued, so
  it cannot burn the retry budget (`src/Application/Notifications/ClassChangeNotification.cs:172-180`).
- **Push rows**: only for recipients with an account; one row per stored subscription, recipient is the
  subscription id (`ClassChangeNotification.cs:182-198`; store query
  `src/Infrastructure/Notifications/PushSubscriptionStore.cs` `GetForUserAsync` — all subscriptions for
  the user, unfiltered).
- **Released bookings** are excluded by status. Asserted by count in
  `ClassCancellationTests.Cancelling_enqueues_one_email_per_member_and_one_push_per_device`
  (`tests/po-prostu-silka.Tests/ClassCancellationTests.cs:466-469`).
- **Blocked members** hold no active future bookings: blocking cascades them to cancelled in the same
  unit of work as the status flip (`src/Application/Members/MemberAdminEndpoints.cs:506-507`, call to
  `CancelActiveFutureForMemberAsync`). Already covered by
  `AdminBookingEndpointTests.Blocking_an_accountless_member_cancels_their_future_bookings`
  (`AdminBookingEndpointTests.cs:897`) and the block cascade block in `BookingEndpointTests.cs:904`.
  No fan-out test is needed for this case — it is a booking-state question, and that is pinned.

### The two triggers

- **Cancel** — `ClassEndpoints.CancelAsync` (`src/Application/Scheduling/ClassEndpoints.cs:719-793`):
  - `already_cancelled` refusal before anything else (`:738`) — a second cancel reads no recipients and
    enqueues nothing.
  - `class_started` refusal (`:746`).
  - Recipients read before the flip (`:752`), status flipped (`:754`), stamp rotated, messages enqueued
    (`:764`), then ONE `TrySaveChangesAsync`; a lost race returns `conflict` and writes nothing,
    outbox rows included (`:784`).
- **Edit** — `ClassEndpoints.UpdateAsync` (`ClassEndpoints.cs:478` onward):
  - Previous values captured before mutation (`:503`, `:509`).
  - `moved` is true when start time, duration or instructor id changed (`:600-602`); capacity is
    deliberately excluded.
  - Notifies only when `moved && bookedCount > 0 && existing.Status == ClassStatus.Scheduled` (`:608`),
    reading recipients then (`:610`) and enqueuing before the save (`:612`); a lost race is `conflict`
    (`:628`).
- **Not triggers**: duplicate creates new classes with no bookings; deleting a class with bookings is
  refused `has_bookings` (asserted in `ClassEndpointTests.cs:893`), so there is no delete path to notify.

### Exactly-once semantics

- **Re-cancel**: one round only — `already_cancelled` short-circuits before recipient resolution.
  Asserted in `ClassCancellationTests.Cancelling_twice_is_already_cancelled_and_sends_one_round_of_messages`
  (`:428-447`).
- **Repeated edits**: every edit that moves a member-visible field sends a new round. There is no dedupe,
  and none is intended — each move is new information for the member. A PUT that moves nothing is silent
  (`ClassCancellationTests.cs:709-734`).
- **Partial rounds are impossible**: status change and every outbox row share one save; a conflict
  writes neither (`ClassEndpoints.cs:778-785`). Asserted in both directions by
  `The_flip_and_the_messages_are_never_observable_apart` (`ClassCancellationTests.cs:833-874`), and the
  booking race by `A_cancel_racing_a_booking_never_leaves_a_member_untold` (`:895-942`).

### Delivery and the retry path

- Enqueue writes `Pending` rows, `AttemptCount = 0`, `NextAttemptAt = now`, rendered text frozen at
  enqueue (`src/Application/Notifications/OutboxEnqueuer.cs:29-47`).
- The worker (`src/Infrastructure/Notifications/OutboxDeliveryWorker.cs`) per pass: reclaims leases older
  than `LeaseTimeout` (`:103-121`), claims a batch, delivers each message with a per-message try/catch so
  one throw does not strand the batch (`:74-88`), prunes old `Sent` rows on its own cadence (`:94-99`).
  Senders are resolved from a fresh scope every pass (`:66-69`).
- Push delivery looks the subscription up by id; a missing one is `SubscriptionGone("subscription_missing")`
  and a gone one deletes the subscription (`OutboxDeliveryWorker.cs:196-211`).
- Defaults (`src/Application/Notifications/OutboxOptions.cs`): poll 15 s (`:17`), batch 20 (`:24`),
  lease 5 min (`:31`), `MaxAttempts` 5 (`:34`), backoff 1 m / 5 m / 15 m / 1 h / 4 h (`:40-46`),
  `Sent` retention 30 days (`:54`), `FailedThreshold` 10 (`:60`).
- Outcomes: Transient → backoff then `Failed` at the cap; Permanent → `Failed` on the first attempt;
  SubscriptionGone → `Sent`. `Failed` rows are never pruned, and `/health` reports `Degraded` above the
  threshold (`src/Infrastructure/Notifications/OutboxHealthCheck.cs:25-31`).
- **All of this is tested** in `OutboxDeliveryTests` (`tests/po-prostu-silka.Tests/OutboxDeliveryTests.cs`):
  happy path, future-scheduled not claimed, transient backoff, growing backoff, dead-letter at the cap,
  permanent on first attempt, stale lease reclaimed, fresh lease not stolen, dead push subscription
  deleted, push delivered, prune keeps `Failed`. **Every one of those tests inserts synthetic rows**
  (`EnqueueEmailAsync`, `:80-97`; `EnqueuePushAsync`, `:312-343`). No test delivers a row that the real
  fan-out produced.

### The integration host runs the real delivery worker

Confirmed by running one test with detailed logging:

```
info: po_prostu_silka.Infrastructure.Notifications.OutboxDeliveryWorker[0]
      Outbox worker started (interval 15s, batch 20, lease 5m).
  Powodzenie po_prostu_silka.Tests.ClassCancellationTests.Cancelling_a_class_nobody_booked_enqueues_nothing [2 s]
```

- The test factory sets only the environment, connection string, admin seed and `App:BaseUrl`
  (`tests/po-prostu-silka.Tests/IntegrationTestFixture.cs:378-400`). It replaces no service.
- The environment is `Testing`, not `Development`, so `Program.cs` registers `AcsEmailSender` rather
  than the logging sender (`src/Program.cs:236-243`), plus `WebPushSender` (`:244`), and the hosted
  worker (`:320`).
- Unconfigured in tests, both senders answer Permanent: `acs_not_configured`
  (`src/Infrastructure/Notifications/AcsEmailSender.cs:55-60`) and `vapid_not_configured`
  (`src/Infrastructure/Notifications/WebPushSender.cs:50-55`).

Consequences:

1. **Every fan-out row in the test database is dead-lettered within ≤15 s.** The host worker claims it
   and a Permanent result marks it `Failed` with `AttemptCount = 1`.
2. **Existing `Pending` assertions are timing-dependent.** `ClassCancellationTests.cs:485-486`
   (`Status == Pending`, `AttemptCount == 0`) and `:941` pass because the read follows the write by
   milliseconds. `:941` follows six rounds of arrangement and is the most exposed. Not observed failing —
   571/571 green on the last full run — but it is a latent flake, not a guarantee.
3. **`OutboxDeliveryTests`' synthetic rows are eligible for the host worker too.** Their
   `NextAttemptAt` is `2026-09-01T12:00Z` (`OutboxDeliveryTests.cs:23`), in the past for the host's real
   clock. Each test survives because its own `RunPassAsync` runs immediately after the insert.
4. **An end-to-end fan-out test cannot be written against this host as it stands.** Cancelling through
   the API and then driving a standalone `OutboxDeliveryWorker` with `FakeEmailSender`/`FakePushSender`
   (the pattern `OutboxDeliveryTests.InitializeAsync` already builds, `:31-69`) would race the host's
   worker for the same rows, and the host's worker dead-letters them. The test host must stop running
   the delivery worker first — a test-infrastructure change in `TestAppFactory`, not a production one.
5. **`OutboxDeliveryTests.InitializeAsync` deletes the whole `OutboxMessages` and `PushSubscriptions`
   tables** (`OutboxDeliveryTests.cs:66-68`). Safe today because the collection runs serially and
   `ClassCancellationTests` matches its rows by subject, but a new fan-out test that relies on push
   subscriptions must arrange its own rather than assume any survive.

### A stale comment on the recipient query

`BookingQuery.cs:57-60` says the email coalesce is "a contract detail rather than a real case" because
"every account this app creates has one", referring to `ApplicationUser.Email`. The projection reads
`Member.Email` (`:70`), which since S-14 is genuinely null for a member recorded at the desk — the very
case `ClassChangeNotification.cs:172-176` calls "a real case since S-14". The behaviour is correct; the
comment is wrong about why, and would mislead the next person who touches the query.

## Existing coverage against the response guidance

| Claim in the guidance | Proven by | Gap |
|---|---|---|
| One email per actively booked member | `Cancelling_enqueues_one_email_per_member_and_one_push_per_device` (`ClassCancellationTests.cs:451-487`) | Counts only. Recipient addresses are never compared, so a wrong-but-same-size list passes. |
| One push per device | same test, 3 push rows for 0+1+2 devices | Counts only; the subscription-id recipient is asserted for a single device (`:502-523`). |
| None for anyone else (released, other class) | same test, by count | Identity of the excluded members is never asserted. |
| Member with an address or device (accountless) | — | **No test.** Member with email and no account → email only; member with neither → no rows. |
| Blocked member excluded | cascade tests (`AdminBookingEndpointTests.cs:897`, `BookingEndpointTests.cs:904`) | None needed here. |
| Edits trigger fan-out | `Moving_the_start_time…`, `Changing_the_duration…`, `Reassigning_the_trainer…` (`:621-702`) | Counts and body; recipients not identified. |
| Silent edits | capacity/no-op, cancelled class, nobody booked (`:709-779`) | — |
| Exactly once on re-cancel | `Cancelling_twice…` (`:428-447`) | — |
| Flip and messages atomic, race-safe | `:833-874`, `:895-942` | — |
| Failed send retried rather than lost | `OutboxDeliveryTests` (synthetic rows) | Overstated wording (see Summary 4). Real fan-out rows are never delivered by any test. |
| "An outbox row written means notified" (to challenge) | — | **Unchallenged.** Requires the host worker disabled. |

## Code References

- `src/Infrastructure/Scheduling/BookingQuery.cs:52-72` — recipient query: active bookings, member email, member's user id
- `src/Infrastructure/Scheduling/BookingQuery.cs:57-60` — stale comment about the email coalesce
- `src/Application/Notifications/ClassChangeNotification.cs:80-133` — cancel and change rendering
- `src/Application/Notifications/ClassChangeNotification.cs:164-200` — fan-out: blank email skipped, push account-keyed
- `src/Application/Notifications/OutboxEnqueuer.cs:27-48` — enqueue shape, no save
- `src/Application/Scheduling/ClassEndpoints.cs:719-793` — `CancelAsync`, handler order
- `src/Application/Scheduling/ClassEndpoints.cs:600-613` — edit trigger and its three fields
- `src/Application/Scheduling/BookingEndpoints.cs:62-68` — `ClassBooking` projection record
- `src/Application/Members/MemberAdminEndpoints.cs:506-507` — block cascade
- `src/Infrastructure/Notifications/OutboxDeliveryWorker.cs:63-121` — pass, claim, lease reclaim
- `src/Infrastructure/Notifications/OutboxDeliveryWorker.cs:196-211` — push lookup and `subscription_missing`
- `src/Application/Notifications/OutboxOptions.cs:17-60` — poll, lease, attempts, backoff, threshold
- `src/Infrastructure/Notifications/AcsEmailSender.cs:55-60` — `acs_not_configured` is Permanent
- `src/Infrastructure/Notifications/WebPushSender.cs:50-55` — `vapid_not_configured` is Permanent
- `src/Program.cs:236-244`, `:320` — sender selection by environment, hosted worker
- `tests/po-prostu-silka.Tests/IntegrationTestFixture.cs:378-400` — test factory overrides nothing
- `tests/po-prostu-silka.Tests/IntegrationTestFixture.cs:214-228` — `CreateMemberAsync(displayName, status, email)`: an accountless member with or without email
- `tests/po-prostu-silka.Tests/ClassCancellationTests.cs:283-291` — rows matched by subject, not by truncating
- `tests/po-prostu-silka.Tests/OutboxDeliveryTests.cs:31-69` — standalone worker over fake channels
- `tests/po-prostu-silka.Tests/FakeChannels.cs` — `FakeEmailSender`, `FakePushSender` record what was sent

## Architecture Insights

- **One service, two triggers, one unit of work.** `IClassChangeNotification` renders and enqueues but
  never saves; the endpoint commits the domain change and the rows together. That is what makes
  "cancelled but nobody told" unreachable, and it is already pinned.
- **Render at enqueue.** A retry hours later sends what the first attempt would have sent — the test for
  message content can therefore assert on the row, not on the delivered bytes.
- **Identity is split since S-14**: email belongs to the member record, push belongs to the login. Any
  new fan-out test has to arrange both shapes to exercise the rule.
- **The test host is production-shaped by default**, including the background worker. That was a
  reasonable default for the endpoint suites and is the one thing standing between this phase and an
  end-to-end delivery assertion.

## Historical Context (from prior changes)

- `context/archive/2026-09-04-class-change-notifications/plan.md` — S-09: reuse of `GetForClassAsync` as
  the recipient list, one service for both triggers, handler order (recipients before the flip, enqueue
  before the save), capacity excluded from the edit trigger.
- `context/archive/2026-09-08-member-entity-and-accountless-members/plan.md` — S-14: push stays keyed on
  the account; a member without one "gets email when one is on file and no push at all", with an explicit
  instruction not to re-key push on the member.
- `context/foundation/lessons.md` — "Verify a prerequisite against the code, not against an archived plan":
  every rule above is cited from `src/`; the archived plans are recorded as intent only.

## Related Research

- `context/changes/testing-booking-invariants/` — Phase 1; established literal oracles and fixed time
  slots per suite.
- `context/changes/testing-access-surface/research.md` — Phase 2.

## Open Questions

- **How to stop the host running the delivery worker** — remove the `OutboxDeliveryWorker` hosted service
  in `TestAppFactory.ConfigureTestServices`, or add an options switch the factory sets. The first touches
  only tests; the second touches `Program.cs`. For `/10x-plan` to decide.
- **Whether the end-to-end increment earns its cost** once identity assertions exist. It is the only test
  that challenges "row written means notified", but the worker and the fan-out are each already tested.
- **The stale comment at `BookingQuery.cs:57-60`** is production code. Previous rollout phases made no
  production edits; a comment-only correction is for the plan to accept or leave as a note.
- **The `subscription_missing` push path** (subscription deleted between enqueue and send) has no test.
  Low value; listed so its omission is a decision.
