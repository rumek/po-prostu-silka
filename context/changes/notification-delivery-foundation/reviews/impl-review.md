<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Notification Delivery Foundation (F-03)

- **Plan**: context/changes/notification-delivery-foundation/plan.md
- **Scope**: Full plan — Phases 1–4 (56/56 Progress rows)
- **Date**: 2026-09-01
- **Verdict**: REJECTED
- **Findings**: 1 critical, 4 warnings, 1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | FAIL |
| Scope Discipline | PASS |
| Safety & Quality | FAIL |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

**On the verdict**: REJECTED is the rubric's response to any critical Safety finding. It is not a
judgement on the change as a whole — 16 of 17 planned contracts matched exactly, scope discipline was
clean, and the deployment works. It reflects one defect in one method: the claim primitive that the
entire outbox design rests on was built in the shape the plan explicitly forbade.

## Evidence gathered

Automated criteria re-run during this review:

| Check | Result |
|---|---|
| `dotnet build po-prostu-silka.slnx -c Release` | 0 warnings, 0 errors |
| `dotnet list package --vulnerable --include-transitive` | clean after two new SDKs |
| `dotnet test po-prostu-silka.slnx` | 35/35 pass |
| `npm test` (Vitest) | 17/17 across 4 files |
| `npm run quality:check` | Prettier and ESLint clean |
| Layering: EF Core in `src/Domain` or `src/Application` | no matches — boundary intact |
| Committed secrets | none; `appsettings.json` placeholders all empty; VAPID private key absent from repo |
| Live `/health` | 200 `Healthy` (includes the outbox check) |
| Live SPA + PWA | `/` 200, manifest 200 `application/manifest+json`, `/api/push/vapid-key` 401 |
| Azure SQL | `SchemaMarkers` absent; `OutboxMessages` and `PushSubscriptions` present |

Scope discipline was checked against every item in "What We're NOT Doing" — no cancellation
notifications, no approval flow, no in-app centre, no offline caching, no custom domain, no SMS, no
App Insights, no real ACS send in CI, no admin test endpoint, and `deploy.yml` has a zero diff since
`bb2b368`. **No violations found.**

## Findings

### F1 — The claim primitive is read-then-update, the exact shape the plan forbade

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality (also Plan Adherence)
- **Location**: src/Infrastructure/Notifications/OutboxDeliveryWorker.cs:118-149
- **Detail**: The plan's Critical Implementation Details states: *"Claiming must be a single atomic
  statement, not read-then-update. Two overlapping app instances exist briefly during every deploy,
  and a `SELECT` followed by an `UPDATE` lets both claim the same row. Use one `UPDATE … OUTPUT
  inserted.*` … so the claim and the read are the same operation."*

  The implementation is `SELECT ids` → `ExecuteUpdate` → `SELECT WHERE ClaimedAt == claimToken`.

  The `ExecuteUpdate` itself is safe: a losing instance's `WHERE Status == Pending` matches zero
  rows. **The hole is the read-back.** It identifies "my batch" by the stored value
  `ClaimedAt == claimToken`, not by which rows this instance actually updated — and `claimToken` is
  just `timeProvider.GetUtcNow()`. Two instances that compute the same eligible set and the same
  timestamp both return the same rows: the loser updated nothing, then selects the winner's rows and
  delivers them anyway. That is a double-send arising from routine deploy overlap, not from the
  crash-recovery path the design knowingly accepts.

  Worse, the code comment at :135 asserts the opposite — *"The Status == Pending predicate is
  re-checked inside the UPDATE, so a row another instance claimed between the SELECT and here is
  simply not claimed by us."* That is true of the write and false of the read-back, and it will stop
  the next reader from looking.

  Real-world probability is low: one B1 instance, overlap only during a deploy, and the timestamps
  must collide. But the plan named this hazard specifically, and the fix is contained.
- **Fix A ⭐ Recommended**: Add a `Guid ClaimToken` column, set a fresh `Guid.NewGuid()` per pass, and
  filter the read-back on it instead of `ClaimedAt`.
  - Strength: Collision-proof by construction, and stays in LINQ — no raw SQL, no change to how the
    rest of the worker reads. A migration adding one nullable column is additive and reversible.
  - Tradeoff: One more column and one more migration; still three round-trips per pass rather than one.
  - Confidence: HIGH — removes the correlation ambiguity entirely; the failure mode is the token, and
    a GUID cannot collide.
  - Blind spot: Does not reduce the round-trip count, so it leaves the "single atomic statement" the
    plan asked for still unimplemented.
- **Fix B**: Replace the three queries with one `FromSqlRaw` doing `UPDATE … SET … OUTPUT inserted.* WHERE Id IN (SELECT TOP(n) …)`.
  - Strength: Exactly what the plan specified — claim and read become one statement, one round-trip,
    no correlation needed at all.
  - Tradeoff: Raw SQL in the worker, which nothing else in this repo does; harder to read and it
    couples the worker to SQL Server syntax.
  - Confidence: MEDIUM — correct, but the raw-SQL shape needs care with EF entity materialisation.
  - Blind spot: Not yet verified how `FromSqlRaw` behaves with `OUTPUT` under the retry execution
    strategy `EnableRetryOnFailure` installs.
- **Decision**: FIXED via Fix A - added a nullable Guid ClaimToken column (migration 20260901134954_AddOutboxClaimToken), set fresh per pass, and switched the read-back to correlate on it. ClaimToken is cleared on every lease-release path. The XML doc and inline comment that asserted an atomicity the read-back did not have were corrected.

### F2 — Subscribe is not scoped to the caller, unlike unsubscribe

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/Infrastructure/Notifications/PushSubscriptionStore.cs:11-33
- **Detail**: `UpsertAsync` finds the row by `Endpoint` **alone**, then unconditionally reassigns
  `UserId` to the caller and overwrites `P256dh`/`Auth`. Any authenticated member who supplies
  another member's endpoint takes over that row: the original owner silently drops out of
  `GetForUserAsync` and stops receiving push, and the row's keys no longer match the real browser so
  sends against it fail to decrypt.

  This contradicts the ownership model enforced one function later — `RemoveAsync` scopes by
  `UserId AND Endpoint`, and there is a test asserting a member cannot unsubscribe another's
  subscription. There is no equivalent test for subscribe.

  Exploitability is bounded: endpoints are long random URLs, and nothing in this codebase logs or
  exposes another member's endpoint. This is a latent IDOR, not a currently reachable one.
- **Fix A ⭐ Recommended**: Scope the lookup to `Endpoint == endpoint && UserId == userId`; if the
  endpoint exists under a different user, delete that row and insert a fresh one.
  - Strength: Closes the takeover while still handling the legitimate shared-device case (a browser
    whose account changed), and matches `RemoveAsync`'s existing ownership model.
  - Tradeoff: Delete-then-insert is two writes, and the new row loses the original `CreatedAt`.
  - Confidence: HIGH — the unique index on `Endpoint` makes the collision case explicit and testable.
  - Blind spot: Have not confirmed how browsers behave when two accounts share one device and both
    expect push.
- **Fix B**: Scope the lookup only, and let a cross-user endpoint collide on the unique index.
  - Strength: Smallest change; the database enforces the invariant.
  - Tradeoff: A shared device produces a 500 on an operation the member cannot understand or fix.
  - Confidence: MEDIUM — correct on ownership, poor as behaviour.
  - Blind spot: None significant.
- **Decision**: SKIPPED - reviewer accepted the risk: push endpoints are never surfaced anywhere in the codebase, so the IDOR is not reachable today.

### F3 — One failing message stalls the rest of its batch for the lease timeout

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Infrastructure/Notifications/OutboxDeliveryWorker.cs:70-77
- **Detail**: The per-message loop has no try/catch of its own; only `RunPassAsync` is guarded, at
  :38-47. If `DeliverAsync` throws for message N of 20 — a transient SQL throttle on
  `SaveChangesAsync` is the obvious candidate on a 5-DTU tier — the pass aborts and messages N+1..20
  stay `Claimed` and undelivered until the 5-minute lease expires, instead of being retried on the
  next 15-second pass. The outer catch prevents host death but not the stall.
- **Fix**: Wrap the per-message `DeliverAsync` call in its own try/catch that logs and continues, so
  one bad row cannot hold up the batch.
- **Decision**: FIXED - per-message try/catch around DeliverAsync; one throwing row is logged and the batch continues.

### F4 — `Guid.ToString()` in the LINQ predicate defeats the primary-key seek

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Infrastructure/Notifications/OutboxDeliveryWorker.cs:172-173
- **Detail**: `FirstOrDefaultAsync(s => s.Id.ToString() == message.Recipient)` translates to a
  server-side `CONVERT` applied to every row of `PushSubscriptions`, so the primary-key index cannot
  be seeked. It runs on every push send. The table is small today (members × devices), but this is a
  hot path on the tightest resource in the stack.
- **Fix**: `Guid.TryParse(message.Recipient, out var id)` then compare `s.Id == id`, restoring the seek.
- **Decision**: FIXED - Guid.TryParse on Recipient then compare s.Id == subscriptionId, restoring the primary-key seek. An unparseable Recipient is treated as a missing subscription.

### F5 — The heartbeat aggregates the whole outbox table every 15 seconds, forever

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Infrastructure/Notifications/OutboxDeliveryWorker.cs:235-248
- **Detail**: `HeartbeatAsync` runs a `GROUP BY Status` over the entire table on every pass. It can
  use the `(Status, NextAttemptAt)` index, but it is still a full index scan every 15 seconds, and
  the cost grows without bound because `Failed` rows are never pruned by design. The cost therefore
  rises fastest exactly when delivery is failing — when you least want the diagnostic query itself
  to be expensive.
- **Fix**: Run the full aggregate on the `PruneInterval` cadence rather than every pass, keeping only
  the cheap `processed` count in the per-pass line.
- **Decision**: FIXED - the full status aggregate now runs on the PruneInterval cadence; every other pass logs only the cheap processed count.

### F6 — The `angular.json` adaptation was recorded outside the plan

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: context/changes/notification-delivery-foundation/plan.md:580-581 vs src/app/angular.json
- **Detail**: The plan's Phase 4 contract specifies `"serviceWorker": true` plus `"ngswConfigPath"`.
  That is the older browser-builder syntax; Angular 22's `@angular/build:application` has no
  `ngswConfigPath` property and fails schema validation outright. What shipped is
  `"serviceWorker": "ngsw-config.json"` — the config path — which is correct.

  The deviation was necessary and is documented, but in `deploy-plan.md`'s gotchas rather than in the
  plan, and the plan carries **zero** "Adapted during implementation" notes. F-02's review raised the
  same pattern (its plan still says `PasswordSignInAsync` where the code correctly uses
  `CheckPasswordSignInAsync`). **Second occurrence across consecutive changes** — a third makes it a
  `/10x-lesson` rule rather than a one-off.
- **Fix**: Add an "Adapted during implementation" note to the plan's Phase 4 §2 contract, matching
  the convention F-01 and F-02 used.
- **Decision**: ACCEPTED-AS-RULE: Record necessary adaptations in the plan, not only in the deploy log (context/foundation/lessons.md) + FIXED - an 'Adapted during implementation' note was added to the plan's Phase 4 §2 contract.
