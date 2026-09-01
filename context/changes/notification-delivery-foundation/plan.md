# Notification Delivery Foundation (F-03) Implementation Plan

## Overview

Give the app a way to reach members outside it. Provision Azure Communication Services email on an
Azure Managed Domain, add Web Push, and put both behind an outbox table plus a leasing retry worker
that survives App Service recycles — then prove the whole path by wiring FR-021's account-approved
notification through it.

This is roadmap item **F-03**, the third foundation of milestone `first-usable-mvp`. It ships
**transport, not notifications**: S-05 (`class-change-notifications`) owns the cancel/change messages
that are the product's north star. F-03 exists so S-05 has a delivery path that has already been
proven to work.

## Current State Analysis

The app is live with Identity auth and a working test suite. It has no way to send anything.

- **`src/po-prostu-silka.csproj`** carries five packages, all pinned `10.0.11`. No email SDK, no push
  library, no `MailKit`.
- **`src/Program.cs`** (189 lines) registers OpenAPI, `AppDbContext` with `EnableRetryOnFailure`,
  Identity cookie auth, the authorization policies, and the admin seeder. **There is no
  `AddHostedService` call anywhere** — this change adds the first one.
- **`src/Domain/`** holds `AccountStatus`, `ApplicationRoles`, `ApplicationUser`. No outbox entity.
- **`src/Application/`** holds `Auth/AuthEndpoints.cs` and `README.md`, which already names
  `notifications` as one of the four bounded contexts the folder will grow.
- **`src/Infrastructure/`** holds `Authorization/`, `Identity/`, `Persistence/`. Two migrations exist.
- **The Angular app has no PWA surface at all**: no `@angular/service-worker`, no `@angular/pwa`, no
  `ngsw-config.json`, no `manifest.webmanifest`. `src/app/public/` contains only `favicon.ico`, and
  `angular.json` has no `serviceWorker` build option.
- **`.github/workflows/deploy.yml`** has no `az webapp config appsettings set` step — every App
  Service setting so far (`AdminSeed__Email`, `AdminSeed__Password`, the `Default` connection string)
  was applied by hand.
- **`tests/po-prostu-silka.Tests/`** has a working Testcontainers + `WebApplicationFactory` fixture
  that migrates a real SQL Server before the host boots. Nothing notification-specific exists.

### Key Discoveries:

- **The milestone's "#1 blocker" is avoidable.** `roadmap.md:118` and `infrastructure.md:77` treat ACS
  sender-domain verification as multi-day, DNS-gated, critical-path work. ACS also offers an **Azure
  Managed Domain** — a `DoNotReply@<guid>.azurecomm.net` sender that provisions in minutes with zero
  DNS records. Chosen here; a custom domain becomes a later From-address change, not a code change.
- **`Microsoft.Communication` is `NotRegistered` on subscription `1b1298d8-…`** (verified live via
  `az provider show`). No ACS resource exists in `pps-rg` — it holds only `pps-plan`,
  `po-prostu-silka`, and `pps-sql`. Registration is a subscription-level prerequisite that gates
  everything in Phases 3–5.
- **Always On is `true` and HTTPS Only is `true`** (verified live). `infrastructure.md:93` ranks
  "hosted service idle-stops" as the highest-likelihood, highest-impact risk in the register; it is
  already mitigated, so the worker will genuinely run between requests.
- **`EnableRetryOnFailure` constrains transaction design.** `src/Program.cs:20-28` records that the
  resulting execution strategy forbids a user-initiated transaction spanning retries. Enqueueing an
  outbox row atomically with a domain change is exactly that case and throws at runtime, not compile
  time.
- **`SchemaMarkers` must be dropped here, by hand.** The archived F-02 plan
  (`context/archive/2026-08-31-auth-identity-foundation/plan.md:677-685`) is explicit: *"EF will not
  generate that drop for F-03 … F-03 must add an empty migration and hand-write both directions."*
  The table's continued existence in Azure SQL was verified directly.
- **iOS push requires 16.4+ AND a home-screen install.** That is why the manifest is not optional:
  without it, push silently never subscribes on any iPhone.
- **Packages verified on nuget.org**: `Azure.Communication.Email 1.1.0`,
  `Lib.Net.Http.WebPush 3.3.1`, `WebPush 1.0.13`.

## Desired End State

An admin approving an account causes that member to receive an email (and a push notification, if
they subscribed) within minutes — delivered by a worker that resumes correctly if the App Service
recycles mid-send, and that reports its own health.

**Verified by:**

1. A real email arrives in a real inbox, and a real push notification arrives on a real device.
2. `curl https://po-prostu-silka.azurewebsites.net/health` returns 200 and includes the outbox
   failure-count check.
3. The integration suite proves the state machine: claim, retry with backoff, dead-letter after the
   attempt cap, prune Sent rows, and reclaim a lease abandoned by a simulated recycle.
4. `SELECT name FROM sys.tables` against Azure SQL shows the outbox and push-subscription tables and
   **no** `SchemaMarkers`.
5. The app log shows the worker's heartbeat line with pending/failed counts.

## What We're NOT Doing

- **No cancellation or class-change notifications.** S-05 owns those (FR-013, FR-021, US-02). F-03
  ships the transport and exactly one consumer — account-approved.
- **No registration or approval flow.** S-01 owns the admin's approve action. F-03 wires the
  notification that *fires* on approval; until S-01 lands, the trigger is exercised by test and by
  hand.
- **No in-app notification center.** PRD §Non-Goals, FR-022 removed — delivery is email + push only.
- **No offline caching.** The service worker exists for push. `ngsw` asset/data caching is out: PRD
  §Non-Goals locks "no offline-first guarantee", and caching a live schedule would seed stale-data
  bugs into S-03/S-04.
- **No custom sender domain.** Azure Managed Domain now; a custom domain is a later change and only
  a From-address swap.
- **No SMS or any third channel.** FR-021 is email + push.
- **No Application Insights.** `roadmap.md:263` parks observability beyond a heartbeat and failure
  count; `infrastructure.md:97` flags ingestion as the main bill-creep risk.
- **No real ACS send in CI.** Tests use fakes; the real send is verified manually once.
- **No admin-triggered test endpoint.** Production surface whose only purpose is testing — the same
  concern F-02 hit with its policy probes.
- **No `deploy.yml` change to push App Service settings.** Settings continue to be applied by hand,
  matching the existing pattern.

## Implementation Approach

Four phases, ordered so each deploys safely to a live site on its own.

**Phase 1 provisions Azure.** Provider registration, ACS + Email Service + Managed Domain, VAPID
keypair, App Service settings. No code — nothing here can break the running site.

**Phase 2 adds schema only.** Outbox and push-subscription tables, plus the separate hand-written
`SchemaMarkers` drop. No behaviour change; if anything is wrong the site behaves exactly as before.

**Phase 3 builds the machine.** Channels behind interfaces, the leasing worker, retention, heartbeat,
health check — and the integration tests that prove the state machine. Nothing enqueues yet, so the
worker starts up and finds an empty table.

**Phase 4 closes the loop.** The subscription endpoint, the service worker and manifest, the SPA
subscribe flow, and then the account-approved notification — ending with the end-to-end verification
to a real inbox and a real device. Push and the first notification land together because neither is
fully verifiable without the other: a notification with no subscriber proves only half the path, and a
subscriber with nothing to receive proves the other half.

**Why schema precedes the worker.** Migrations run before `webapps-deploy` in the same CI job, so
schema is always ahead of or equal to code. Splitting the tables (P2) from the worker (P3) means a
failure in either leaves a coherent system rather than a worker querying absent tables.

## Critical Implementation Details

**Enqueueing must go through the execution strategy.** With `EnableRetryOnFailure` active, calling
`Database.BeginTransaction()` directly throws `InvalidOperationException` at runtime. Any code that
writes an outbox row atomically with a domain change must wrap the unit of work in
`dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () => { … })`. This is the same
constraint `Program.cs:20-28` already flags for S-04's booking transaction — F-03 is the first change
that actually hits it.

**Claiming must be a single atomic statement, not read-then-update.** Two overlapping app instances
exist briefly during every deploy, and a `SELECT` followed by an `UPDATE` lets both claim the same
row. Use one `UPDATE … SET Status=Claimed, ClaimedAt=… OUTPUT inserted.* WHERE Id IN (SELECT TOP(n) …
WHERE Status=Pending AND NextAttemptAt<=now)` so the claim and the read are the same operation.

**Delivery is at-least-once, and that is the deliberate choice.** A crash between "the provider
accepted the message" and "the row is marked Sent" resends on the next lease expiry. Duplicating a
cancellation email is acceptable; losing one violates the milestone's guardrail. Say so in the code,
or someone will later "fix" it into at-most-once.

**A stale lease must be reclaimable, or a recycle strands rows forever.** Claimed rows whose
`ClaimedAt` is older than a timeout (comfortably longer than the longest plausible send) return to
Pending. Without this, every recycle mid-send permanently orphans whatever was in flight.

**Push failures must not fail the notification.** A `410 Gone` or `404` from the push endpoint means
the subscription is dead — delete it and mark the row Sent, do not retry. Any other push failure is
logged and the row is still Sent, because email is the guaranteed channel. Only email failures drive
the retry machinery.

**The manifest is load-bearing for iOS, not decoration.** iOS 16.4+ delivers push only to a PWA
installed to the home screen, which requires a valid `manifest.webmanifest` with icons. Shipping the
service worker without it silently excludes every iPhone member — and the failure is invisible,
because subscription simply never happens.

## Phase 1: Azure Provisioning

### Overview

Register the Communication provider, create ACS with an Azure Managed Domain, generate the VAPID
keypair, and hand both to App Service. Infrastructure only — no repository changes except the
deployment record.

### Changes Required:

#### 1. Resource provider registration

**Files**: none (Azure CLI)

**Intent**: `Microsoft.Communication` is `NotRegistered` on this subscription, so no ACS resource can
be created until it is. This is a subscription-scoped operation, not a resource-group one.

**Contract**: `az provider register -n Microsoft.Communication`, then poll
`az provider show -n Microsoft.Communication --query registrationState` until it reports `Registered`.
It takes a few minutes. If it fails with an authorization error, the account lacks
`Microsoft.Communication/register/action` and this becomes a human-only escalation.

#### 2. Communication Services and Email Service

**Files**: none (Azure CLI), recorded in `context/deployment/deploy-plan.md`

**Intent**: Create the two resources ACS email needs — the Email Service that owns domains, and the
Communication Service that exposes the send API — in the existing resource group.

**Contract**: `az communication email create` for the Email Service and `az communication create` for
the Communication Service, both in `pps-rg`, following the existing `pps-` naming convention
(`pps-email`, `pps-acs`). **Data location must be an EU region** to keep member data in-EU under the
PRD's GDPR-baseline privacy NFR; the resource itself is `global`. Note that the `communication`
command group may require `az extension add --name communication` on CLI 2.35.0 — if the extension is
unavailable on this vintage, that is the trigger to upgrade the Azure CLI (already a standing
follow-up in `deploy-plan.md`).

#### 3. Azure Managed Domain

**Files**: none (Azure CLI or portal)

**Intent**: Provision the sender domain without DNS. This is the decision that removes the roadmap's
#1 blocker from the critical path.

**Contract**: Create an Azure Managed Domain under the Email Service, then link it to the
Communication Service. The resulting sender is `DoNotReply@<guid>.azurecomm.net`. Record the exact
sender address — it becomes configuration, not a constant. Note the managed domain's send limits in
`deploy-plan.md`; they are far above one gym's volume but they exist, and a future custom-domain
migration should reference them.

#### 4. VAPID keypair

**Files**: none (generated locally; values go to App Service settings)

**Intent**: Web Push signs every message with an application server key. Generate it once — rotating
it later invalidates every stored subscription.

**Contract**: Generate a P-256 keypair (the `Lib.Net.Http.WebPush` library exposes a helper, or use
`openssl`). The **private key** is a secret and goes to App Service; the **public key** is served to
the browser by design. A `mailto:` subject is also required by the VAPID spec — use the club's
contact address.

#### 5. App Service settings

**Files**: none (Azure CLI)

**Intent**: Hand the running app its ACS connection string and VAPID keys, following the
double-underscore convention already established by `AdminSeed__*`.

**Contract**: `az webapp config appsettings set -g pps-rg -n po-prostu-silka --settings` with
`Acs__ConnectionString`, `Acs__SenderAddress`, `VapidKeys__PublicKey`, `VapidKeys__PrivateKey`,
`VapidKeys__Subject`. **This command restarts the app**, so re-check the live site afterwards even
though no code changed. Never commit any of these values.

#### 6. Deployment record

**File**: `context/deployment/deploy-plan.md`

**Intent**: `deploy-plan.md` is the standing audit trail; it currently has no mention of ACS, VAPID,
or an outbox.

**Contract**: Append a section recording the Communication Service and Email Service names, the
managed sender address, the data location, the five new App Service setting **names** (never values),
and the managed-vs-custom domain decision with its rationale. Keep the unresolved follow-ups (az CLI
age, `gh` not installed, Managed Identity for SQL) intact.

### Success Criteria:

#### Automated Verification:

- Provider is registered: `az provider show -n Microsoft.Communication --query registrationState -o tsv` returns `Registered`
- Both resources exist: `az resource list -g pps-rg --query "[?contains(type,'Communication')].name" -o tsv` lists them
- The managed domain is linked: `az communication email domain list` shows a domain in `Completed`/verified state
- All five settings are present: `az webapp config appsettings list -g pps-rg -n po-prostu-silka --query "[?starts_with(name,'Acs__') || starts_with(name,'Vapid')].name" -o tsv` returns five names
- The live site survives the setting-induced restart: `curl https://po-prostu-silka.azurewebsites.net/health` returns 200 `Healthy`
- The SPA still serves: `curl https://po-prostu-silka.azurewebsites.net/` returns 200 with the Angular shell

#### Manual Verification:

- A test email sent from the Azure portal's ACS "Try Email" arrives in a real inbox (proves the resource works before any code depends on it)
- The email's sender address matches the recorded `Acs__SenderAddress`
- The VAPID private key is stored in a password manager and appears nowhere in the repo, shell history, or a chat log
- The ACS data location is an EU region
- `deploy-plan.md` records names only — no connection string or key values

**Implementation Note**: After completing this phase and all automated verification passes, pause here
for manual confirmation from the human that the manual testing was successful before proceeding to the
next phase.

---

## Phase 2: Outbox Schema

### Overview

Add the two tables the transport needs, and drop the orphaned `SchemaMarkers` table F-02 handed over
— in two separate migrations, so the destructive change can be reverted on its own.

### Changes Required:

#### 1. Notification domain types

**Files**: `src/Domain/Notifications/NotificationChannel.cs`, `src/Domain/Notifications/OutboxStatus.cs` (new)

**Intent**: Model the two axes the worker switches on, as domain types rather than magic strings.

**Contract**: `NotificationChannel { Email = 0, Push = 1 }` and
`OutboxStatus { Pending = 0, Claimed = 1, Sent = 2, Failed = 3 }`, both with explicit numeric values —
they persist as `int`, so reordering would silently reinterpret existing rows. `Pending` and the
channel default are `0`.

#### 2. The outbox entity

**File**: `src/Domain/Notifications/OutboxMessage.cs` (new)

**Intent**: One row per recipient per channel, holding the **already-rendered** message. The worker
delivers bytes and never re-renders, so attempt 3 says exactly what attempt 1 said.

**Contract**: `Guid Id`; `NotificationChannel Channel`; `string Recipient` (email address, or the
owning user id for push); `string Subject`; `string Body`; `OutboxStatus Status`; `int AttemptCount`;
`DateTimeOffset CreatedAt`; `DateTimeOffset NextAttemptAt`; `DateTimeOffset? ClaimedAt`;
`DateTimeOffset? SentAt`; `string? LastError`. `NextAttemptAt` is what backoff moves; `ClaimedAt` is
what makes a stale lease detectable.

#### 3. The push subscription entity

**File**: `src/Domain/Notifications/PushSubscription.cs` (new)

**Intent**: Store the browser's push endpoint and keys so the worker can send to a member's device.
One member may have several — phone and laptop are separate subscriptions.

**Contract**: `Guid Id`; `string UserId` (FK to `AspNetUsers`); `string Endpoint` (unique — the
browser re-issues the same endpoint on re-subscribe, and a unique index is what makes upsert-on-
subscribe correct); `string P256dh`; `string Auth`; `DateTimeOffset CreatedAt`. Cascade-delete with
the user.

#### 4. Entity configurations

**Files**: `src/Infrastructure/Persistence/Configurations/OutboxMessageConfiguration.cs`,
`PushSubscriptionConfiguration.cs` (new)

**Intent**: Configure both entities in the established per-entity classes so
`ApplyConfigurationsFromAssembly` discovers them with no edit to `OnModelCreating`.

**Contract**: For `OutboxMessage` — enums stored as `int`, `Recipient`/`Subject` bounded lengths,
`Body` unbounded, `LastError` bounded and nullable, and a **composite index on
`(Status, NextAttemptAt)`**: that is the worker's claim predicate and the only query that runs every
polling interval on a 5-DTU tier. For `PushSubscription` — unique index on `Endpoint`, index on
`UserId`, cascade delete from `ApplicationUser`.

#### 5. DbContext sets

**File**: `src/Infrastructure/Persistence/AppDbContext.cs`

**Intent**: Expose the two new sets.

**Contract**: Add `DbSet<OutboxMessage>` and `DbSet<PushSubscription>`. Do not touch
`OnModelCreating` — the configuration classes are auto-discovered.

#### 6. Additive migration

**Files**: `src/Infrastructure/Persistence/Migrations/*` (generated)

**Intent**: Create the two tables.

**Contract**: `dotnet ef migrations add AddNotificationOutbox -p src/po-prostu-silka.csproj -o Infrastructure/Persistence/Migrations`.
Read it before committing: it must create exactly two tables plus their indexes, and must **not**
mention `SchemaMarkers`. Both `Up` and `Down` non-empty.

#### 7. The SchemaMarkers drop — its own migration

**Files**: `src/Infrastructure/Persistence/Migrations/*` (new, hand-written)

**Intent**: Close F-02's handoff. Kept separate from step 6 so the destructive operation can be
reviewed and reverted without dragging the new schema with it — the point of the deferred-destructive
policy in `infrastructure.md:85`.

**Contract**: Create an **empty** migration (`dotnet ef migrations add DropSchemaMarkers`) — EF
generates nothing, because the entity left the model in F-02 and EF believes the table is already
gone. Hand-write `Up` as `migrationBuilder.DropTable(name: "SchemaMarkers")` and `Down` as the
matching `CreateTable` reproducing F-01's original shape: `Id int NOT NULL IDENTITY` primary key and
`AppliedAt datetimeoffset NOT NULL`. An empty `Down` here would violate the reversibility requirement
on the one migration in this change that actually destroys something.

### Success Criteria:

#### Automated Verification:

- Build passes with no new warnings: `dotnet build po-prostu-silka.slnx -c Release`
- No vulnerable packages: `dotnet list src/po-prostu-silka.csproj package --vulnerable --include-transitive`
- Existing tests still pass: `dotnet test po-prostu-silka.slnx`
- Both migrations apply to a clean container: `docker compose down -v && docker compose up -d` then `dotnet ef database update -p src/po-prostu-silka.csproj --connection "<dev connection string>"` (the `--connection` argument is required — `AppDbContextFactory` takes precedence over the host for every design-time command)
- The additive migration does not touch SchemaMarkers: no `SchemaMarkers` operand in `AddNotificationOutbox`
- The drop migration is reversible: `dotnet ef migrations script DropSchemaMarkers AddNotificationOutbox` emits a `CREATE TABLE [SchemaMarkers]`
- Local schema is correct: `SchemaMarkers` is absent and both new tables present, verified by querying `sys.tables`
- The composite index exists: `sys.indexes` shows an index on `OutboxMessages (Status, NextAttemptAt)`
- App still starts and `/health` returns 200 `Healthy`
- Deployed app still healthy after deploy

#### Manual Verification:

- Both migrations read end to end before commit; the hand-written `Down` genuinely reproduces F-01's original `SchemaMarkers` shape
- `SchemaMarkers` is gone from Azure SQL after deploy, and both new tables are present
- No data loss warning appeared for anything other than the intended `SchemaMarkers` drop

**Implementation Note**: After completing this phase and all automated verification passes, pause here
for manual confirmation from the human that the manual testing was successful before proceeding to the
next phase.

---

## Phase 3: Channels and the Delivery Worker

### Overview

Build the machine: the two channel adapters, the leasing retry worker, retention, the heartbeat, and
the health check — plus the integration tests that prove the state machine under simulated recycles.
Nothing enqueues yet, so the worker starts and finds an empty table.

### Changes Required:

#### 1. Package references

**File**: `src/po-prostu-silka.csproj`

**Intent**: Add the email SDK and the push library.

**Contract**: `Azure.Communication.Email` `1.1.0` and `Lib.Net.Http.WebPush` `3.3.1`, pinned
explicitly like every existing reference. `Lib.Net.Http.WebPush` is chosen over the more widely-cited
`WebPush` because it is actively maintained and integrates with `IHttpClientFactory`; note that in a
comment so the choice is not silently reversed.

#### 2. Channel abstractions

**Files**: `src/Application/Notifications/IEmailSender.cs`, `IPushSender.cs` (new)

**Intent**: Keep the worker independent of the providers, so tests substitute fakes with no network
and the documented SMTP fallback is a one-class swap.

**Contract**: `IEmailSender.SendAsync(string to, string subject, string body, CancellationToken)` and
`IPushSender.SendAsync(PushSubscription subscription, string title, string body, CancellationToken)`.
Both return a small result type distinguishing **transient failure** (retry) from **permanent
failure** (do not retry) — the worker's whole retry decision rests on that distinction, so it must be
in the contract rather than inferred from exception types.

#### 3. ACS email adapter

**File**: `src/Infrastructure/Notifications/AcsEmailSender.cs` (new)

**Intent**: Implement `IEmailSender` against Azure Communication Services.

**Contract**: Constructed from `Acs__ConnectionString` and `Acs__SenderAddress`. Maps ACS failures to
the transient/permanent result: a malformed or rejected recipient is permanent; a throttle, timeout or
5xx is transient. Register `EmailClient` as a singleton — it is thread-safe and holds a connection
pool.

#### 4. Web Push adapter

**File**: `src/Infrastructure/Notifications/WebPushSender.cs` (new)

**Intent**: Implement `IPushSender` with VAPID signing.

**Contract**: Built from `VapidKeys__PublicKey`, `VapidKeys__PrivateKey`, `VapidKeys__Subject`.
**`404`/`410` from the push service means the subscription is dead** — return a permanent failure
carrying that fact so the worker deletes the subscription row rather than retrying it forever.

#### 5. The outbox enqueue service

**File**: `src/Application/Notifications/OutboxEnqueuer.cs` (new)

**Intent**: The single way anything gets into the outbox. S-01 and S-05 will call this.

**Contract**: A method taking a recipient, channel, rendered subject and body, and adding a Pending
row with `NextAttemptAt = now`. Callers needing atomicity with a domain change must wrap in
`Database.CreateExecutionStrategy().ExecuteAsync(...)` — see "Critical Implementation Details".
Document that on the method, because the failure is a runtime exception, not a compile error.

#### 6. The delivery worker

**File**: `src/Infrastructure/Notifications/OutboxDeliveryWorker.cs` (new)

**Intent**: The heart of the change. Claim, send, retry, dead-letter, prune, heartbeat.

**Contract**: A `BackgroundService` looping on a ~15s interval. Each pass:
1. **Reclaim** Claimed rows whose `ClaimedAt` is older than the lease timeout, back to Pending.
2. **Claim** a bounded batch in one atomic `UPDATE … OUTPUT` (see "Critical Implementation Details").
3. **Send** each row via the matching channel.
4. On success mark `Sent` with `SentAt`. On **transient** failure increment `AttemptCount`, set
   `NextAttemptAt` by exponential backoff (~1m, 5m, 15m, 1h, 4h), record `LastError`, return to
   Pending; past 5 attempts mark `Failed`. On **permanent** failure mark `Failed` immediately —
   retrying a rejected address only burns quota.
5. **Push-specific**: a dead subscription deletes the `PushSubscription` row and marks the message
   `Sent`, because push is best-effort and email is the guaranteed channel.
6. **Prune** `Sent` rows older than 30 days (run on a much longer cadence than the send loop — it does
   not need to run every 15 seconds).
7. **Heartbeat**: log one line per pass at Information with pending/claimed/failed counts.

The loop must catch and log its own exceptions per pass. An unhandled exception in a
`BackgroundService` tears down the host — the same failure class as F-02's unguarded seeder.

#### 7. Health check and registration

**Files**: `src/Infrastructure/Notifications/OutboxHealthCheck.cs` (new), `src/Program.cs`

**Intent**: Make silent delivery failure visible from the URL F-01 already built, and register
everything.

**Contract**: A health check reporting `Degraded` (not `Unhealthy`) when `Failed` rows exceed a
configurable threshold, with the count in its description — `Unhealthy` would misrepresent a
delivery backlog as the site being down. Register the check alongside `AddDbContextCheck`, and add the
channel adapters, the enqueuer, and `AddHostedService<OutboxDeliveryWorker>()` before
`var app = builder.Build()`.

#### 8. Configuration placeholders

**Files**: `src/appsettings.json`, `src/appsettings.Development.json`

**Intent**: Document the keys without committing values, mirroring the existing `//AdminSeed` pattern.

**Contract**: `appsettings.json` gains empty `Acs` and `VapidKeys` sections with `//` comment-keys
explaining that production supplies them as App Service settings. `appsettings.Development.json`
leaves them empty — local development uses fakes rather than real sends; the adapters must log and
no-op rather than throw when unconfigured, so a developer without ACS credentials can still run the app.

#### 9. Worker integration tests

**File**: `tests/po-prostu-silka.Tests/OutboxDeliveryTests.cs` (new)

**Intent**: Prove the state machine — the part that actually breaks — with no network.

**Contract**: Extend the existing fixture with fake `IEmailSender`/`IPushSender` whose results are
scripted per test. Cover: a Pending row is sent and marked `Sent`; a transient failure increments
`AttemptCount` and pushes `NextAttemptAt` out; the 6th attempt marks `Failed`; a permanent failure
marks `Failed` on the first attempt without retrying; a row Claimed with a stale `ClaimedAt` is
reclaimed; a dead push subscription is deleted and the message still marked `Sent`; Sent rows past the
retention window are pruned and Failed rows are not.

### Success Criteria:

#### Automated Verification:

- Build passes with no new warnings: `dotnet build po-prostu-silka.slnx -c Release`
- No vulnerable packages after adding two SDKs
- Full suite passes: `dotnet test po-prostu-silka.slnx`
- The new tests genuinely exercise the state machine: the suite fails if the backoff calculation is inverted (verify once by deliberately breaking it, then revert)
- App starts with the worker registered and `/health` returns 200
- The heartbeat appears: the local run log shows the worker's periodic line with counts
- The worker survives an empty table: no exceptions logged over several passes
- `/health` includes the outbox check: the response or log shows it evaluated
- Deployed app healthy after deploy

#### Manual Verification:

- Killing the app mid-send (stop the process while a row is Claimed) and restarting results in the row being reclaimed and delivered, not stranded
- The heartbeat cadence is readable rather than noisy in `az webapp log tail`
- A row manually inserted with a bad email address ends `Failed` after the attempt cap, and `/health` reports `Degraded`
- Running the app locally with no ACS credentials logs a clear "not configured" line rather than throwing

**Implementation Note**: After completing this phase and all automated verification passes, pause here
for manual confirmation from the human that the manual testing was successful before proceeding to the
next phase.

---

## Phase 4: Push Subscription, PWA, and the First Notification

### Overview

Close the loop. Let a browser subscribe — subscription endpoint, Angular service worker, a minimal
manifest so iOS can install the app, and the SPA-side subscribe flow — then wire FR-021's
account-approved notification through the outbox and prove the whole path with a real email and a
real push notification.

### Changes Required:

#### 1. Subscription endpoints

**File**: `src/Application/Notifications/PushEndpoints.cs` (new)

**Intent**: Let an authenticated member register and remove a browser push subscription, and let the
SPA discover the VAPID public key.

**Contract**: A group at `/api/push`:
- `GET /vapid-key` — returns `VapidKeys__PublicKey`. Anonymous is acceptable (the key is public by
  design), but `RequireAuthorization()` costs nothing and keeps the surface uniform.
- `POST /subscribe` — takes endpoint, p256dh, auth; **upserts** on the unique `Endpoint` index, bound
  to the calling user. Re-subscribing must not create a duplicate.
- `POST /unsubscribe` — removes the subscription by endpoint, scoped to the calling user so one member
  cannot delete another's.

All require authentication. Use bare `RequireAuthorization()`, not `ActiveMember` — a Pending member's
device may subscribe before approval, and the account-approved notification is precisely what they
need to receive.

#### 2. Angular service worker

**Files**: `src/app/package.json`, `src/app/angular.json`, `src/app/ngsw-config.json` (new),
`src/app/src/app/app.config.ts`

**Intent**: Register a service worker capable of receiving push. Push only — no offline caching.

**Contract**: Add `@angular/service-worker` at the Angular 22 line, set `"serviceWorker": true` and
`"ngswConfigPath"` in the build target, and add `provideServiceWorker('ngsw-worker.js', { enabled: isDevMode() === false })`
to `app.config.ts`. `ngsw-config.json` declares an **empty** `assetGroups`/`dataGroups` — the PRD locks
"no offline-first guarantee", and caching a live schedule would seed stale-data bugs into S-03/S-04.

#### 3. Web app manifest

**Files**: `src/app/public/manifest.webmanifest` (new), `src/app/public/icons/*` (new),
`src/app/src/index.html`

**Intent**: Make the app installable to the home screen — a hard requirement for push on iOS 16.4+,
not a nicety.

**Contract**: A manifest with `name`, `short_name`, `start_url`, `display: standalone`, theme and
background colours, and at least 192px and 512px icons (a `maskable` variant is worth including).
Link it from `index.html`. Placeholder icons are acceptable to ship, but note them as a visible rough
edge for a later design pass.

#### 4. Push service in the SPA

**File**: `src/app/src/app/core/notifications/push.service.ts` (new)

**Intent**: Own the subscribe/unsubscribe flow, in the `core/` structure F-02 established.

**Contract**: Uses Angular's `SwPush`. Fetches the VAPID public key from `/api/push/vapid-key`,
calls `requestSubscription`, POSTs the result to `/api/push/subscribe`. Must **degrade silently** when
`SwPush.isEnabled` is false — desktop Safari, a non-installed iPhone, or a browser with notifications
denied are all normal, not errors. Expose subscription state as a signal so a later slice can render it.

#### 5. Specs

**File**: `src/app/src/app/core/notifications/push.service.spec.ts` (new)

**Intent**: Lock in the degradation behaviour before any screen depends on it.

**Contract**: Vitest specs using the existing setup: the service no-ops when `SwPush.isEnabled` is
false; a successful subscription POSTs to `/api/push/subscribe`; a denied permission does not throw.

#### 6. Endpoint tests

**File**: `tests/po-prostu-silka.Tests/PushEndpointTests.cs` (new)

**Intent**: Assert the subscription surface's access rules and upsert behaviour.

**Contract**: Anonymous `POST /api/push/subscribe` returns 401; an authenticated member's subscribe
creates one row; subscribing twice with the same endpoint yields exactly one row; a member cannot
unsubscribe another member's subscription.

#### 7. Notification composition

**File**: `src/Application/Notifications/AccountApprovedNotification.cs` (new)

**Intent**: Render the account-approved message and enqueue one outbox row per channel. This is the
first real consumer, and the shape S-05 will copy.

**Contract**: Given a member, render an email (subject and body) and a push payload, then enqueue via
`OutboxEnqueuer` — one Email row addressed to the member's email, plus one Push row per stored
subscription. Rendering happens **here**, not in the worker: the worker delivers already-rendered
bytes. Plain-text or simple HTML body; no template engine — one message does not justify one.

#### 8. Trigger point

**File**: `src/Application/Notifications/AccountApprovedNotification.cs` (the same service)

**Intent**: Fire the notification when a member's status becomes `Active`.

**Contract**: S-01 owns the admin's approve action and does not exist yet, so F-03 exposes the
composition as a callable service and documents the integration point for S-01 to call. Until then it
is exercised by test and by the manual verification below. **Do not** build a speculative approve
endpoint — that is S-01's stated outcome.

#### 9. Notification composition test

**File**: `tests/po-prostu-silka.Tests/AccountApprovedNotificationTests.cs` (new)

**Intent**: Assert the composition produces the right rows.

**Contract**: Approving a member with no push subscriptions enqueues exactly one Email row addressed
to them; with two subscriptions, one Email plus two Push rows; the rendered subject and body are
non-empty and contain the member's display name.

#### 10. Deployment record update

**File**: `context/deployment/deploy-plan.md`

**Intent**: Record that the transport is live and what was verified.

**Contract**: Note the end-to-end verification (inbox and device), the managed sender address in use,
and the outbox retention policy. Add the custom-domain migration as an explicit future follow-up.

### Success Criteria:

#### Automated Verification:

- Frontend quality gate passes: `npm run quality:check` from `src/app/`
- Angular builds, and the build emits `ngsw-worker.js` into `dist/app/browser/`
- The manifest is present in the build output
- Vitest suite passes: `npm test`
- .NET suite passes including the new endpoint tests: `dotnet test po-prostu-silka.slnx`
- Anonymous subscribe is rejected: `POST /api/push/subscribe` without a cookie returns 401
- The composition test proves the fan-out: one email row plus one push row per subscription
- Deployed app healthy and SPA serving after deploy
- `/health` reports the outbox check with zero failures after the end-to-end run

#### Manual Verification:

- Chrome on desktop: the app prompts for notification permission, subscribes, and a row appears in `PushSubscriptions`
- iPhone (iOS 16.4+): the app installs to the home screen from Safari's share sheet, and subscribes once installed
- Denying notification permission leaves the app fully usable with no error surfaced
- Revoking permission and re-subscribing does not create a duplicate row
- Approving a member (by flipping status directly, until S-01 lands) results in a **real email arriving in a real inbox** within minutes
- The same approval results in a **real push notification** on both the desktop browser and the installed iPhone
- The outbox row transitions Pending → Claimed → Sent, observable in the database
- The heartbeat log shows the counts moving as the message is delivered
- `SchemaMarkers` is confirmed absent from Azure SQL
- No secret value appears in any application or deploy log

**Implementation Note**: This is the final phase. After it passes, F-03 is done and S-05
(`class-change-notifications`) has a proven delivery path — as does the account-approved notification
S-01 will trigger.

---

## Testing Strategy

The state machine is what breaks; the providers are what cost money. Tests target the former with
fakes, and the latter is verified once by hand — which is what the roadmap's "one test message
delivered end-to-end to a real inbox and device" always implied.

### Integration Tests (Phase 3 and 4, xUnit + Testcontainers):

- Pending row → sent → `Sent`
- Transient failure increments `AttemptCount` and pushes `NextAttemptAt` out by the backoff schedule
- Attempt cap exceeded → `Failed`
- Permanent failure → `Failed` on the first attempt, no retry
- Stale `ClaimedAt` → reclaimed to Pending (the simulated-recycle case)
- Dead push subscription → subscription row deleted, message still `Sent`
- Retention prunes `Sent` past the window and leaves `Failed` alone
- Push subscribe requires authentication; double-subscribe upserts; cross-member unsubscribe refused

### Unit Tests (Phase 4, Vitest):

- `PushService` no-ops when `SwPush.isEnabled` is false
- A successful subscription POSTs to `/api/push/subscribe`
- Denied permission does not throw

### Manual Testing Steps:

1. `docker compose down -v && docker compose up -d`, then apply migrations
2. Confirm `SchemaMarkers` is gone and both new tables exist
3. Run the app; confirm the heartbeat line appears with zero counts
4. Insert an outbox row by hand with a real email address; confirm it is delivered and marked `Sent`
5. Stop the app while a row is `Claimed`; restart; confirm the row is reclaimed and delivered
6. Insert a row with an invalid address; confirm it reaches `Failed` after the cap and `/health` reports `Degraded`
7. Subscribe from Chrome desktop and from an installed iPhone; confirm both receive a push
8. Approve a member; confirm both email and push arrive

**What this does not cover**: a genuine ACS outage, and the managed domain's send limits. Both are
provider behaviours we can reason about but not exercise cheaply.

## Performance Considerations

- **The claim query runs every ~15 seconds forever.** The composite index on
  `(Status, NextAttemptAt)` is what keeps it a seek rather than a scan; without it this is the single
  most expensive recurring query on a 5-DTU tier. Verify the index exists — do not assume EF created
  a useful one.
- **Batch size bounds the blast radius of a recycle.** A large batch claims many rows that all need
  reclaiming after a crash; a small batch means more round-trips. Start small (10–25) and note the
  chosen number, since S-05 will fan out to every booked member of a class.
- **Retention is what keeps the 2GB cap out of reach.** Rendered bodies are the largest column;
  30 days of one gym's notifications is trivial, but the pruning is what makes that statement stay
  true. F-01 flagged this constraint specifically for this change.
- **Push sends are per-subscription HTTP calls.** A class with 20 booked members and 2 devices each is
  40 outbound requests. `IHttpClientFactory` handles pooling, but batch size should account for it.

## Migration Notes

- **Two migrations, deliberately separate.** `AddNotificationOutbox` is additive; `DropSchemaMarkers`
  is destructive and stands alone so it can be reverted independently — `infrastructure.md:85`.
- **`DropSchemaMarkers` must be written by hand.** EF generates nothing: the entity left the model in
  F-02, so from EF's perspective the table is already gone. Create an empty migration and write both
  directions. `Down` must recreate F-01's original shape (`Id int IDENTITY` PK, `AppliedAt datetimeoffset`).
- **This closes F-02's outstanding handoff.** After this change, no orphan scaffolding remains.
- Every migration must have a working `Down`; reversibility is a merge requirement.

## Open Risks & Assumptions

- **Provider registration may be refused.** `Microsoft.Communication` is `NotRegistered` and
  registering is subscription-scoped. If the account lacks the permission, Phase 1 stops and becomes a
  human-only escalation — everything downstream waits.
- **`az` CLI 2.35.0 may not have the `communication` command group.** It is ~2022-vintage and already
  a standing follow-up. If the extension will not install, that is the trigger to upgrade the CLI
  mid-change.
- **Managed-domain deliverability is weaker than a custom domain.** Mail from `*.azurecomm.net` is
  more likely to be filtered. The guardrail is "no missed cancellations" — if members report missing
  mail, the custom-domain migration moves from optional to required.
- **Delivery is at-least-once by design.** A crash between provider acceptance and the status write
  resends. Duplicating a cancellation email is the acceptable side of that trade; do not "fix" it into
  at-most-once.
- **Push coverage is inherently partial.** iOS needs 16.4+ and a home-screen install; some members
  will never subscribe. Email is the channel the guardrail rests on, and push failures never fail a
  notification.
- **The account-approved trigger has no caller until S-01.** F-03 exposes the composition and
  documents the integration point; if S-01 forgets to call it, the notification silently never fires.
- **Placeholder PWA icons will ship** unless real ones are supplied — a visible rough edge on the
  installed app.

## References

- Roadmap item: `context/foundation/roadmap.md:111-123` — F-03, milestone `first-usable-mvp`
- Requirement: `context/foundation/prd.md:118` (FR-021), NFR "notification promptness", guardrail "no missed cancellations"
- Recycle/outbox rationale: `context/foundation/infrastructure.md:64,77,79,93-94,98`
- Retention constraint handed here: `context/archive/2026-08-31-persistence-foundation/plan.md:542-551`
- `SchemaMarkers` handoff: `context/archive/2026-08-31-auth-identity-foundation/plan.md:677-685`
- Execution-strategy constraint: `src/Program.cs:20-28`
- Test fixture to extend: `tests/po-prostu-silka.Tests/IntegrationTestFixture.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Azure Provisioning

#### Automated

- [x] 1.1 Microsoft.Communication provider reports Registered — 18cf007
- [x] 1.2 Communication Services and Email Service exist in pps-rg — 18cf007
- [x] 1.3 Azure Managed Domain is linked and verified — 18cf007
- [x] 1.4 All five Acs__/Vapid App Service settings present — 18cf007
- [x] 1.5 Live site survives the setting-induced restart — 18cf007
- [x] 1.6 Live SPA still serves — 18cf007

#### Manual

- [x] 1.7 Portal "Try Email" arrives in a real inbox — 18cf007
- [x] 1.8 Sender address matches the recorded Acs__SenderAddress — 18cf007
- [x] 1.9 VAPID private key stored durably and absent from repo — 18cf007
- [x] 1.10 ACS data location is an EU region — 18cf007
- [x] 1.11 deploy-plan.md records names only, no secret values — 18cf007

### Phase 2: Outbox Schema

#### Automated

- [x] 2.1 Build passes in Release with no new warnings — 15bfaae
- [x] 2.2 No vulnerable packages — 15bfaae
- [x] 2.3 Existing tests still pass — 15bfaae
- [x] 2.4 Both migrations apply to a clean local container — 15bfaae
- [x] 2.5 Additive migration does not touch SchemaMarkers — 15bfaae
- [x] 2.6 Drop migration is reversible — 15bfaae
- [x] 2.7 Local schema correct: SchemaMarkers absent, both new tables present — 15bfaae
- [x] 2.8 Composite index on (Status, NextAttemptAt) exists — 15bfaae
- [x] 2.9 App starts and local /health returns 200 Healthy — 15bfaae
- [x] 2.10 Deployed /health returns 200 Healthy — bd98351

#### Manual

- [x] 2.11 Both migrations read end to end; hand-written Down reproduces F-01's shape — 15bfaae
- [x] 2.12 SchemaMarkers gone from Azure SQL; both new tables present — bd98351
- [x] 2.13 No unexpected data-loss warning — 15bfaae

### Phase 3: Channels and the Delivery Worker

#### Automated

- [x] 3.1 Build passes in Release with no new warnings — 8b8603b
- [x] 3.2 No vulnerable packages after adding two SDKs — 8b8603b
- [x] 3.3 Full suite passes — 8b8603b
- [x] 3.4 Suite fails when the backoff calculation is broken (verify, then revert) — 8b8603b
- [x] 3.5 App starts with the worker registered and /health returns 200 — 8b8603b
- [x] 3.6 Heartbeat line appears with counts — 8b8603b
- [x] 3.7 Worker survives an empty table with no exceptions — 8b8603b
- [x] 3.8 /health includes the outbox check — 8b8603b
- [x] 3.9 Deployed /health returns 200 Healthy — bd98351

#### Manual

- [x] 3.10 Killing the app mid-send leaves the row reclaimable, not stranded — 8b8603b
- [x] 3.11 Heartbeat cadence readable rather than noisy in log tail — 8b8603b
- [x] 3.12 Bad address reaches Failed after the cap and /health reports Degraded — 8b8603b
- [x] 3.13 Running locally without ACS credentials logs "not configured" rather than throwing — 8b8603b

### Phase 4: Push Subscription, PWA, and the First Notification

#### Automated

- [x] 4.1 npm run quality:check passes — d3ef1b2
- [x] 4.2 Angular builds and emits ngsw-worker.js — d3ef1b2
- [x] 4.3 Manifest present in the build output — d3ef1b2
- [x] 4.4 Vitest suite passes — d3ef1b2
- [x] 4.5 .NET suite passes including push endpoint tests — d3ef1b2
- [x] 4.6 Anonymous subscribe returns 401 — d3ef1b2
- [x] 4.7 Composition test proves the email + per-subscription push fan-out — d3ef1b2
- [x] 4.8 Deployed /health returns 200 Healthy and SPA serves — bd98351
- [x] 4.9 /health reports the outbox check with zero failures after the end-to-end run — bd98351

#### Manual

- [x] 4.10 Chrome desktop subscribes and a row appears in PushSubscriptions — bd98351
- [x] 4.11 iPhone installs to home screen and subscribes — bd98351
- [x] 4.12 Denying permission leaves the app usable with no error surfaced — bd98351
- [x] 4.13 Re-subscribing does not create a duplicate row — d3ef1b2
- [x] 4.14 Approving a member delivers a real email to a real inbox — bd98351
- [x] 4.15 The same approval delivers a real push notification to desktop and installed iPhone — bd98351
- [x] 4.16 Outbox row observed transitioning Pending → Claimed → Sent — bd98351
- [x] 4.17 Heartbeat counts move as the message is delivered — bd98351
- [x] 4.18 SchemaMarkers confirmed absent from Azure SQL — bd98351
- [x] 4.19 No secret value appears in any application or deploy log — d3ef1b2
