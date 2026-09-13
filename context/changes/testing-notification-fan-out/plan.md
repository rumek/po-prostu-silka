# Notification Fan-out Implementation Plan

## Overview

Rollout Phase 4 of `context/foundation/test-plan.md` — risk #5: a class is cancelled or changed and a
booked member receives no email or push, or someone not booked receives one.

The triggers, the silent edits, re-cancel, atomicity and the cancel/booking race are already tested —
by COUNT. This change proves the messages are addressed to the right people, covers the member shapes
S-14 introduced, and pushes real fan-out rows through the delivery worker to the channel. The first
step removes the one thing that makes the last of those impossible today: the integration host runs the
real delivery worker.

No production behaviour changes. One production edit is a comment correction.

## Current State Analysis

From `context/changes/testing-notification-fan-out/research.md`:

- **Recipients** are the class's ACTIVE bookings (`src/Infrastructure/Scheduling/BookingQuery.cs:61-72`).
  Email comes from the MEMBER record (`Member.Email ?? string.Empty`, `:70`), push only through a login
  (`Member.UserId`, `:68`).
- **Fan-out** enqueues one email row per recipient with a non-blank email, and one push row per stored
  subscription for recipients with an account; push recipient is the subscription id
  (`src/Application/Notifications/ClassChangeNotification.cs:164-200`).
- **Triggers**: `CancelAsync` (`src/Application/Scheduling/ClassEndpoints.cs:719-793`) and an edit that
  moves start time, duration or instructor on a `Scheduled` class with bookings (`:600-613`).
- **Existing fan-out assertions count rows** (`tests/po-prostu-silka.Tests/ClassCancellationTests.cs:451-487`,
  `:621-702`). A recipient list with a member swapped for another of the same shape passes.
- **No fan-out test arranges a member without an account**, with or without an email.
- **The integration host runs the real `OutboxDeliveryWorker`** — confirmed by the test log line
  `Outbox worker started (interval 15s, batch 20, lease 5m)`. The test factory overrides no service
  (`tests/po-prostu-silka.Tests/IntegrationTestFixture.cs:378-400`); the `Testing` environment selects
  `AcsEmailSender` and `WebPushSender` (`src/Program.cs:236-244`), both answering Permanent when
  unconfigured, so every fan-out row is dead-lettered within ≤15 s. `Pending` assertions at
  `ClassCancellationTests.cs:485-486` and `:941` pass on timing alone.
- **The worker's retry state machine is fully tested — on synthetic rows only**
  (`tests/po-prostu-silka.Tests/OutboxDeliveryTests.cs`). No test delivers a row the fan-out produced.
- **The claim path keeps the desk-recorded address**: `claimed.Email ??= request.Email`
  (`src/Application/Auth/AuthEndpoints.cs:433`), an S-14 decision already described in
  `tests/po-prostu-silka.Tests/MemberClaimTests.cs:291`. A member whose record says A and whose login is
  B is notified at A.
- **A stale comment** at `BookingQuery.cs:57-60` calls a blank email "not a real case"; since S-14 it is.

## Desired End State

The integration host no longer runs the delivery worker, so outbox rows stay exactly as a test left them.
Every fan-out test identifies its recipients by address and subscription id. A member with no account is
proven to get email only when an address is on file and push never; a claimed member is proven to be
notified at the record's address; two successive edits are proven to produce two rounds, each true when
sent. One test cancels a class through the API and runs the worker with fake channels over the rows that
cancellation wrote, asserting who actually received a message. `test-plan.md` §6.5 tells the next person
how to add a notification test, and §3 marks Phase 4 complete.

Verify with `dotnet test` green from the repo root and the mutation checks named per phase.

### Key Discoveries:

- `IntegrationTestFixture.CreateMemberAsync(displayName, status, email)` (`:214-228`) already arranges a
  member with no account, with or without an email, so no new fixture method is needed for that shape.
- `ClassCancellationTests` finds its own rows by subject — every class type name carries a GUID
  (`:283-291`). The end-to-end test must use the same filter for the channels' `Sent` lists.
- `OutboxDeliveryTests.InitializeAsync` (`:31-69`) already shows how to build a standalone worker over the
  test database with fake channels; `RunPassAsync` is public for exactly this.
- `FakeEmailSender.Sent` records `(To, Subject, Body)`; `FakePushSender.Sent` records
  `(Endpoint, Title, Body)` (`tests/po-prostu-silka.Tests/FakeChannels.cs:12-38`).

## What We're NOT Doing

- No production behaviour change. The only `src/` edit is the comment at `BookingQuery.cs:57-60`.
- No options switch in `Program.cs` to disable the worker — the removal lives in the test factory.
- No second end-to-end test for the edit trigger: after rendering, edit and cancel share `FanOutAsync`
  and the whole delivery path.
- No test that a blocked member is excluded — the block cascade already releases their future bookings
  and is pinned in `AdminBookingEndpointTests.cs:897` and `BookingEndpointTests.cs:904`.
- No re-testing of retry, backoff, dead-letter, lease reclaim or dead push subscriptions —
  `OutboxDeliveryTests` owns those.
- No test for the `subscription_missing` push branch (subscription deleted between enqueue and send) —
  low value; the delete-on-410 path is already tested.
- No roadmap question about which address a claimed member is notified at: the rule was decided in S-14
  and is pinned here as current behaviour.
- No change to `OutboxDeliveryTests`' whole-table cleanup.

## Implementation Approach

Infrastructure first, so the observation everything later relies on — a row stays as written — is true
before any test depends on it. Then identity on the rows, where the arrangement already exists and each
new case is one test. Then the one end-to-end pass that challenges "a row written means the member was
notified", and the cookbook.

Every expected recipient is taken from the ARRANGEMENT — the address a test typed, the subscription rows
it inserted — and never from `BookingQuery`, `ClassChangeNotification` or any production projection.

## Critical Implementation Details

**The end-to-end worker needs a clock at or after the real now.** Fan-out rows are enqueued by the host,
whose `TimeProvider` is real, so their `NextAttemptAt` is the real current time. A worker built on
`OutboxDeliveryTests`' `TestTimeProvider(2026-09-01)` would find every one of them in the future and claim
nothing — and a test asserting "not delivered" would pass for the wrong reason.

**Other tests' rows are in the same table, and after Phase 1 they stay `Pending`.** A worker pass claims
by batch across the whole table, so with the default batch of 20 this test's rows may not be reached.
Build the end-to-end worker with a batch large enough to drain the table, filter the channels' `Sent`
lists by this test's subject, and assert that this test's own rows ended `Sent` in the database — so a row
left unclaimed fails the test instead of passing silently.

## Phase 1: The test host stops running the delivery worker

### Overview

Remove the `OutboxDeliveryWorker` hosted service from the integration host, so outbox rows written by one
test are not claimed, attempted and dead-lettered by a background loop while other assertions read them.

### Changes Required:

#### 1. Test factory

**File**: `tests/po-prostu-silka.Tests/IntegrationTestFixture.cs`

**Intent**: The host is production-shaped by default, which suited the endpoint suites, but its delivery
worker runs with unconfigured senders and turns every `Pending` row into `Failed` within one poll. Tests
that need delivery build their own worker over fake channels (`OutboxDeliveryTests`, and Phase 3 here);
nothing needs the host's.

**Contract**: `TestAppFactory.ConfigureWebHost` adds a `ConfigureTestServices` callback that removes the
`IHostedService` registration whose implementation type is `OutboxDeliveryWorker`, and nothing else —
senders, enqueuer and health check stay as registered. Carry a comment naming the observed effect
(rows dead-lettered within one poll) and the two consumers that build their own worker. The removal is
the non-obvious part:

```csharp
builder.ConfigureTestServices(services =>
{
    var worker = services.Single(d =>
        d.ServiceType == typeof(IHostedService)
        && d.ImplementationType == typeof(OutboxDeliveryWorker));
    services.Remove(worker);
});
```

`Single`, not `RemoveAll`: if the registration ever changes shape, the host should fail to start rather
than silently keep the worker running.

### Success Criteria:

#### Automated Verification:

- Backend tests pass from the repo root: `dotnet test`
- The delivery suite still passes on its own worker: `dotnet test --filter FullyQualifiedName~OutboxDeliveryTests`
- The host no longer starts the worker: `dotnet test --filter "FullyQualifiedName~ClassCancellationTests.Cancelling_a_class_nobody_booked_enqueues_nothing" --logger "console;verbosity=detailed"` prints no `Outbox worker started` line

#### Manual Verification:

- Comment out the removal: the `Outbox worker started` line returns in the detailed log; restore it.

**Implementation Note**: After this phase and its automated verification, pause for manual confirmation
before Phase 2.

---

## Phase 2: Recipients identified on the outbox rows

### Overview

Every fan-out assertion names who a row is addressed to, and the member shapes S-14 introduced get their
own cases.

### Changes Required:

#### 1. Identity on the existing cancel fan-out test

**File**: `tests/po-prostu-silka.Tests/ClassCancellationTests.cs`

**Intent**: `Cancelling_enqueues_one_email_per_member_and_one_push_per_device` keeps its arrangement and
gains identity. Behavior asserted: email rows are addressed to exactly the three booked members' addresses;
push rows to exactly the subscription ids arranged for them; neither the released member's address nor the
bystander's appears on any row. Regression caught: a recipient query returning a different member of the
same count, a join onto the wrong address, push rows keyed on the wrong identity. Anti-pattern avoided:
count-only assertions.

**Contract**: expected email recipients are the addresses `NewMemberAsync` generated; expected push
recipients are the ids of the `PushSubscription` rows that same helper inserted, read back by user id as
arrangement data. Set equality per channel, so the counts follow from identity rather than standing in for
it. `NewMemberAsync` already returns the email; returning the arranged subscription ids is a helper change.

#### 2. Identity on the edit trigger

**File**: `tests/po-prostu-silka.Tests/ClassCancellationTests.cs`

**Intent**: `Moving_the_start_time_notifies_every_booked_member` asserts the two email rows are addressed to
`first` and `second` and the single push row to `second`'s subscription. Regression caught: the edit path
resolving recipients differently from the cancel path. Research source: both call `GetForClassAsync`
(`ClassEndpoints.cs:610`, `:752`) — this is what keeps that true.

**Contract**: same identity assertion as change 1; the duration and trainer tests keep their single-member
shape, where `Assert.Single` already implies the one recipient.

#### 3. A member with no account

**File**: `tests/po-prostu-silka.Tests/ClassCancellationTests.cs`

**Intent**: Two new tests. (a) A member recorded without an account but with an email, booked and then
cancelled on: exactly one email row addressed to that email, and no push row for them. (b) A member recorded
with neither account nor email, booked alongside an account member on the same class: the account member's
row exists and nothing is addressed to the empty member. Behavior asserted: the S-14 identity split —
email from the record, push only through a login, a blank address skipped. Regression caught: push looked
up for a null user, a blank recipient enqueued and burning the retry budget, email read from the login.
Edge case: (b)'s control row proves "nothing for them" is not "nothing at all".

**Contract**: arrange through `fixture.CreateMemberAsync(displayName, email: …)` plus
`fixture.IssuePassAsync(memberId)`, and book through the admin booking route by member id — a new private
helper beside `BookAsync`, which takes a signed-in client and cannot reach a member without one.

#### 4. A claimed member notified at the record's address

**File**: `tests/po-prostu-silka.Tests/ClassCancellationTests.cs`

**Intent**: A member recorded at the desk with address A registers through an invitation code with address
B; booked and cancelled on, their email row is addressed to A and no row to B. Behavior asserted: the
notification follows the member record, per the S-14 rule `claimed.Email ??= request.Email`
(`AuthEndpoints.cs:433`). Regression caught: the fan-out starting to read the login's address. Pinned as
current behaviour by decision in this plan.

**Contract**: arrange the code and registration through the same routes `MemberClaimTests` uses; the
comment on the test cites `AuthEndpoints.cs:433` and `MemberClaimTests.cs:291` as the source of the rule,
so a future change to it lands here knowingly. Both addresses are literals in the test.

#### 5. Two successive edits, two rounds

**File**: `tests/po-prostu-silka.Tests/ClassCancellationTests.cs`

**Intent**: Move a booked class forward two hours, then back to its original time. Two rounds are enqueued,
each addressed to the member, the first naming the original time then the moved one, the second the reverse.
Behavior asserted: every qualifying edit notifies, even when the net effect is nothing — each message was
true when sent, and a member who read the first must receive the second. Regression caught: a "dedupe"
that drops the second round. Pinned as current behaviour by decision in this plan.

**Contract**: the two rounds are told apart by body and ordered by `CreatedAt`; wall-clock expectations use
the file's existing `ClubWallClockOf`, whose comment already explains why it restates the conversion rather
than calling `MessageTime`.

#### 6. The stale comment

**File**: `src/Infrastructure/Scheduling/BookingQuery.cs`

**Intent**: The comment at `:57-60` justifies the coalesce with a claim about `ApplicationUser.Email` that
no longer describes the code, which reads `Member.Email`. Correct it to say what is true: a member recorded
without an address has a null email, the coalesce turns it into the blank string the fan-out skips, and the
test from change 3 is the proof.

**Contract**: comment text only; the query is untouched.

### Success Criteria:

#### Automated Verification:

- The notification suite passes: `dotnet test --filter FullyQualifiedName~ClassCancellationTests`
- Backend tests pass from the repo root: `dotnet test`

#### Manual Verification:

- Change `b.Member!.Email` in the `GetForClassAsync` projection to the account's email: the claimed-member test and the accountless-with-email test turn red; revert.
- Remove the blank-email guard in `FanOutAsync`: the accountless-without-email test turns red; revert.
- Enqueue push rows with `device.Endpoint` instead of `device.Id`: the identity assertion in the cancel fan-out test turns red; revert.

**Implementation Note**: After this phase and its automated verification, pause for manual confirmation
before Phase 3.

---

## Phase 3: Delivered to the channel, and the cookbook

### Overview

One test proves that the rows a real cancellation writes reach the channels, addressed to the right
people — the only test in the repository that challenges "an outbox row written means the member was
notified" — and the test plan records how to add the next one.

### Changes Required:

#### 1. End-to-end cancellation delivery

**File**: `tests/po-prostu-silka.Tests/ClassCancellationTests.cs`

**Intent**: Arrange one class with: an account member with one device, an account member with no device,
a member with no account but an email, a member with no account and no email, and a released booking. Cancel
through the API. Run a standalone `OutboxDeliveryWorker` over the test database with `FakeEmailSender` and
`FakePushSender`. Behavior asserted: the email channel received messages for exactly the two account members
and the accountless member with an address; the push channel received exactly the one device's endpoint;
nothing reached the released member or the empty one; and every row this cancellation wrote is `Sent` in the
database. Regression caught: a recipient format the worker cannot resolve (push keyed on the endpoint, a
malformed id), rows the worker never claims, a channel crossed. Anti-pattern avoided: mocking the dispatcher
— the real worker delivers the real rows, and only the network edge is fake.

**Contract**: the worker is built as `OutboxDeliveryTests.InitializeAsync` builds it, with two differences
that are load-bearing (see Critical Implementation Details): a clock at or after the real current time, and
a batch size large enough to drain rows other tests left `Pending`. `Sent` lists are filtered by this test's
class-type subject. Expected addresses and the expected endpoint come from the arrangement.

#### 2. Cookbook and status

**File**: `context/foundation/test-plan.md`

**Intent**: Fill §6.5 and record what Phase 4 found.

**Contract**:
- §6.5 "Adding a notification fan-out test": location (`ClassCancellationTests` owns both triggers);
  identity, not counts — recipients from the arrangement, never from the query; the member shapes to cover
  (account with and without devices, no account with and without email, claimed); rows matched by a
  GUID-bearing type name; the host does not run the delivery worker, so rows stay as written; to prove
  delivery, build a standalone worker with fake channels, a real-time clock and a draining batch; the
  mutation checks that prove a new test bites.
- §6.6: a Phase 4 note — the fan-out was covered by count only; S-14's identity split was untested; the host
  ran the real worker, which made `Pending` assertions timing-dependent and end-to-end delivery untestable;
  the claimed-member address and the double-edit rounds are pinned as current behaviour by decision.
- §3: Phase 4 Status `complete`. Bump "Last updated".

### Success Criteria:

#### Automated Verification:

- The end-to-end test passes: `dotnet test --filter FullyQualifiedName~ClassCancellationTests`
- Backend tests pass from the repo root: `dotnet test`

#### Manual Verification:

- Enqueue push rows with `device.Endpoint` instead of `device.Id`: the end-to-end test turns red on the push channel (the worker cannot resolve the subscription); revert.
- Build the end-to-end worker on a clock before the real now: the test turns red on the rows left unsent, rather than passing; revert.
- §6.5 reads on its own for someone who has not seen this change.

---

## Testing Strategy

### Integration Tests:

- Identity per channel for both triggers, from arranged addresses and subscription ids.
- The S-14 member shapes: no account with email, no account without email, claimed with a differing login.
- Two successive edits producing two true rounds.
- One cancellation delivered by the real worker over fake channels.

### Manual Testing Steps:

1. Run each mutation named in the phases; the named tests go red, then green on revert.
2. Run one notification test with detailed logging and confirm the host no longer starts the worker.

## Performance Considerations

The end-to-end test drains every `Pending` row in the shared table in one pass; after Phase 1 that grows
with every fan-out test in the collection, which is a few dozen rows at most — negligible, and bounded by
`OutboxDeliveryTests` wiping the table when its class starts.

## Migration Notes

None — no schema, no data, no runtime behaviour.

## References

- Research: `context/changes/testing-notification-fan-out/research.md`
- Test plan: `context/foundation/test-plan.md` (§2 risk #5 and its corrected guidance, §6.5 to fill)
- Worker built over fake channels: `tests/po-prostu-silka.Tests/OutboxDeliveryTests.cs:31-69`
- Claim address rule: `src/Application/Auth/AuthEndpoints.cs:433`, `tests/po-prostu-silka.Tests/MemberClaimTests.cs:291`
- Prior phase plans: `context/archive/2026-09-13-testing-frontend-gate-and-contract/plan.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step
> titles. See `references/progress-format.md`.

### Phase 1: The test host stops running the delivery worker

#### Automated

- [x] 1.1 Backend tests pass (`dotnet test`) — 50f0698
- [x] 1.2 The delivery suite still passes on its own worker — 50f0698
- [x] 1.3 The host no longer starts the worker (no `Outbox worker started` in the detailed log) — 50f0698

#### Manual

- [ ] 1.4 Commenting out the removal brings the log line back

### Phase 2: Recipients identified on the outbox rows

#### Automated

- [x] 2.1 The notification suite passes (`--filter ClassCancellationTests`)
- [ ] 2.2 Backend tests pass (`dotnet test`)

#### Manual

- [ ] 2.3 Reading the account's email in the projection turns the claimed and accountless tests red
- [ ] 2.4 Removing the blank-email guard turns the accountless-without-email test red
- [ ] 2.5 Push rows keyed on the endpoint turn the cancel identity assertion red

### Phase 3: Delivered to the channel, and the cookbook

#### Automated

- [ ] 3.1 The end-to-end test passes (`--filter ClassCancellationTests`)
- [ ] 3.2 Backend tests pass (`dotnet test`)

#### Manual

- [ ] 3.3 Push rows keyed on the endpoint turn the end-to-end test red
- [ ] 3.4 A worker clock before the real now turns the end-to-end test red
- [ ] 3.5 §6.5 reads on its own for a new reader
