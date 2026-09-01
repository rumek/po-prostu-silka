# Notification Delivery Foundation (F-03) — Plan Brief

> Full plan: `context/changes/notification-delivery-foundation/plan.md`

## What & Why

Give the app a way to reach members outside it. Azure Communication Services email plus Web Push,
both behind an outbox table and a leasing retry worker that survives App Service recycles. This is
transport, not notifications: S-05 — the milestone's north star, "booked member learns their class
was cancelled" — is built directly on top of it, and exists only if this works.

## Starting Point

The app is live with Identity auth, a working test suite, and no way to send anything at all: no
email SDK, no push library, no `AddHostedService` call anywhere in `Program.cs`, and an Angular app
with no service worker, no manifest, and only a `favicon.ico` in `public/`. `Microsoft.Communication`
is not even a registered provider on the subscription.

## Desired End State

An admin approving an account causes that member to receive an email — and a push notification if
they subscribed — within minutes, delivered by a worker that resumes correctly if the platform
recycles mid-send and that reports its own health at `/health`.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Sender domain | Azure Managed Domain now, custom later | ACS managed domains provision in minutes with **zero DNS records**, converting the milestone's "#1 blocker, multi-day lead time" into a non-event; a custom domain later is a From-address change, not a code change. |
| Email transport | ACS SDK behind `IEmailSender` | First-party and same-subscription, while the interface keeps the documented SMTP fallback a one-class swap and lets tests run with no network. |
| Channels | Email + push together | FR-021 says both, and S-05 needs the full transport; a second channel is also what proves the abstraction. |
| Outbox rows | Rendered messages, one per recipient per channel | The worker delivers bytes and never re-renders, so attempt 3 says what attempt 1 said — and partial failure has somewhere to record itself. |
| Worker trigger | Poll every ~15s | Survives recycles by construction: there is no in-memory signal to lose, so a restart just resumes from the table. |
| Retry | Exponential backoff, 5 attempts, then dead-letter | Rides out a provider outage without hammering it; a terminal `Failed` state is what makes the failure-count metric mean anything. |
| Idempotency | Atomic claim with a reclaimable lease | One `UPDATE … OUTPUT` stops two instances double-sending; a stale lease returns to Pending so a recycle strands nothing. **At-least-once by design.** |
| Retention | Prune `Sent` after 30 days, keep `Failed` | Bounds growth against the 2GB Basic cap that F-01 flagged for this change, while preserving the rows worth looking at. |
| PWA | Service worker + minimal manifest | iOS 16.4+ delivers push **only** to a home-screen-installed PWA — without a manifest, push is silently dead on every iPhone. No offline caching (PRD Non-Goal). |
| VAPID keys | Generated once, App Service settings | Mirrors the existing `AdminSeed__*` pattern; the public key reaching the browser is by design. |
| Observability | Heartbeat log + failure count on `/health` | Reuses the probe F-01 built, so silent delivery failure is visible from a URL rather than only in the log stream. |
| Send scope | Transport + account-approved notification | Proves the path with something the product actually needs rather than a throwaway message; S-05 then adds cancellations to a proven pipeline. |
| Testing | Fake channels in CI; one manual real send | Tests the state machine (what breaks) with no credentials or cost in CI; the real inbox/device check was always inherently manual. |
| `SchemaMarkers` | Its own migration, hand-written | Isolates the destructive change so it can be reverted without dragging the new schema with it. |

## Scope

**In scope:** ACS provisioning with a managed domain; VAPID keypair; `OutboxMessage` and
`PushSubscription` tables; the hand-written `SchemaMarkers` drop; `IEmailSender`/`IPushSender` plus
ACS and Web Push adapters; the leasing delivery worker with backoff, dead-lettering and pruning;
heartbeat and `/health` failure count; push subscribe/unsubscribe endpoints; Angular service worker,
manifest and push service; the account-approved notification.

**Out of scope:** cancellation/class-change notifications (S-05); the approve action itself (S-01);
in-app notification centre (PRD Non-Goal); offline caching (PRD Non-Goal); custom sender domain; SMS;
Application Insights; real ACS sends in CI; any `deploy.yml` change.

## Architecture / Approach

Anything wanting to notify calls `OutboxEnqueuer`, which writes **rendered** rows — one per recipient
per channel. A `BackgroundService` polls every ~15s: it reclaims leases abandoned by a recycle, claims
a bounded batch in a single atomic `UPDATE … OUTPUT`, sends through the matching channel adapter, then
marks `Sent` or schedules a backoff retry. Email failures drive the retry machinery; push failures
never fail a notification, and a `410 Gone` deletes the dead subscription. A periodic sweep prunes
`Sent` rows past 30 days. The whole thing is observable through one heartbeat log line and a
`/health` check that goes `Degraded` when failures pile up.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Azure provisioning | Provider registered, ACS + managed domain, VAPID keys, App Service settings | Provider registration is subscription-scoped and may be refused; `az` 2.35.0 may lack the `communication` group |
| 2. Outbox schema | Two tables + the hand-written `SchemaMarkers` drop | EF generates nothing for the drop — it must be written by hand in both directions |
| 3. Channels & worker | Adapters, leasing worker, retention, heartbeat, health check, state-machine tests | The correctness core: an unhandled exception in a `BackgroundService` tears down the host |
| 4. Push, PWA & first notification | Subscribe endpoints, service worker, manifest, SPA push service, account-approved email + push, end-to-end verification | iOS needs 16.4+ *and* a home-screen install; failure to subscribe is silent. The trigger has no caller until S-01, so it must be exercised by hand |

**Prerequisites:** F-01 and F-02 (both done). Always On confirmed `true` — the worker will actually
run. **`Microsoft.Communication` must be registered before Phases 3–4 can be verified at all.**
**Estimated effort:** ~4 sessions across 4 phases; Phase 3 is the largest and the one that matters,
Phase 4 the broadest (backend, frontend and manual device testing in one).

## Open Risks & Assumptions

- **Provider registration may be refused** — subscription-scoped; if the account lacks the permission,
  Phase 1 stops and everything downstream waits.
- **`az` CLI 2.35.0 (~2022) may not have the `communication` command group** — already a standing
  follow-up; this change may force the upgrade.
- **Managed-domain deliverability is weaker** than a verified custom domain; mail from
  `*.azurecomm.net` is more likely to be filtered, against a "no missed cancellations" guardrail.
- **Delivery is at-least-once by design** — a crash between provider acceptance and the status write
  resends. Duplicating a cancellation is the acceptable side of that trade.
- **Push coverage is inherently partial** — email is the channel the guardrail rests on.
- **The account-approved trigger has no caller until S-01**; if S-01 forgets to call it, the
  notification silently never fires.
- **Placeholder PWA icons will ship** unless real ones are supplied.

## Success Criteria (Summary)

- A real email arrives in a real inbox and a real push notification on a real device, triggered by an
  account approval.
- Killing the app mid-send and restarting delivers the message rather than stranding it.
- `/health` reports the outbox failure count, and the heartbeat shows the worker alive.
