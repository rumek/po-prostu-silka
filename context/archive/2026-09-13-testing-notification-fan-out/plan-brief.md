# Notification Fan-out — Plan Brief

> Full plan: `context/changes/testing-notification-fan-out/plan.md`
> Research: `context/changes/testing-notification-fan-out/research.md`

## What & Why

Rollout Phase 4 of `context/foundation/test-plan.md`: a class is cancelled or changed and a booked member
receives no email or push — or someone not booked receives one (risk #5). The triggers are tested, but only
by counting rows, and nothing proves a written row ever reaches a channel.

## Starting Point

`ClassCancellationTests` pins both triggers, the silent edits, re-cancel, atomicity and the booking race,
all by count. No fan-out test arranges a member without an account, although S-14 made that common. The
integration host runs the real delivery worker with unconfigured senders, so every fan-out row is
dead-lettered within 15 seconds — which makes `Pending` assertions timing-dependent and end-to-end delivery
untestable.

## Desired End State

The host no longer runs the worker. Every fan-out test names its recipients by address and subscription id.
Members with no account, with and without an email, and a claimed member whose login differs from the
record are each proven. One cancellation is delivered by the real worker over fake channels, and the test
asserts who actually received a message.

## Key Decisions Made

| Decision | Choice | Why | Source |
|---|---|---|---|
| The host's live worker | Removed in `TestAppFactory`, not by an options switch | Test-only change; no production knob that could be turned off in production | Plan |
| End-to-end scope | One test, cancel only | Edit and cancel share the fan-out and the whole delivery path after rendering | Plan |
| Claimed member, record A vs login B | Pin: notified at A | The S-14 rule is deliberate and already described in `MemberClaimTests` | Plan |
| Two edits that cancel out | Pin: two rounds | Each message was true when sent; a member who read the first needs the second | Plan |
| Stale comment in `BookingQuery.cs` | Correct the comment only | It misleads the next reader; no behaviour changes | Plan |
| Expected recipients | From the arrangement, never the query | A test sharing its oracle with the code cannot catch the code | Research / test plan |
| Blocked members | Not re-tested | The block cascade is already pinned | Research |
| Retry mechanics | Not re-tested | `OutboxDeliveryTests` owns them | Research |

## Scope

**In scope:** worker removal in the test factory; identity assertions on the cancel and move tests;
accountless with and without email; claimed-member address; double-edit rounds; comment fix; one end-to-end
delivery test; test-plan §6.5, §6.6 and §3.

**Out of scope:** production behaviour; an options switch; an edit end-to-end test; the
`subscription_missing` branch; `OutboxDeliveryTests` cleanup.

## Architecture / Approach

Infrastructure first so rows stay as written; identity on the rows next, reusing the existing arrangement;
then one pass of the real worker over the rows a real cancellation wrote, with only the network edge faked.

## Phases at a Glance

| Phase | What it delivers | Key risk |
|---|---|---|
| 1. Host without the worker | Rows stay `Pending`; log line gone | Another suite quietly relying on the worker (none found) |
| 2. Identity on the rows | Set-equality recipients; S-14 member shapes; double edit | Claimed-member arrangement is the longest setup |
| 3. Delivery + cookbook | End-to-end cancel test; §6.5, §6.6, §3 | Worker clock and batch: a wrong one passes for the wrong reason |

**Prerequisites:** Docker running (Testcontainers SQL Server).
**Estimated effort:** one session, three phases.

## Open Risks & Assumptions

- The end-to-end worker must use a clock at or after real time and a batch that drains the shared table;
  both are called out in the plan because either mistake produces a green test that proves nothing.
- `context/foundation/test-plan.md` has uncommitted Phase 4 edits (status and the backported guidance) that
  land with this change's first commit.

## Success Criteria (Summary)

- Each mutation named in the plan turns its tests red, and reverting turns them green.
- `dotnet test` is green at the end, with no `Outbox worker started` line from the integration host.
