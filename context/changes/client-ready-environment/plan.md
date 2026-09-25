# Client-Ready Environment Implementation Plan

## Overview

Roadmap S-28 (M-9, anchors GL-01–GL-06). The single Azure environment becomes something the owner
can put in front of a prospective club. Every flow works end to end. Every response carries baseline
security. `/health` tells a real failure apart from a known limit, and an alert, an exception feed
and a budget reach the owner before a client notices anything. `main` changes only through checked
pull requests. A bad deploy and a lost row each have a procedure that has already been rehearsed
once. The custom sender domain that lifts the e-mail cap is prepared as its own phase, blocked until
a domain is bought.

Nothing a member sees changes, apart from the CSP having to leave every screen working.

## Current State Analysis

Verified live and in code on 2026-09-25:

- **No observability resources.** `pps-rg` holds the plan, the site, the SQL server and database,
  `pps-email` and `pps-acs`. It has no Application Insights, no Log Analytics workspace, no alert and
  no budget (`az consumption budget list` is empty). Logs reach only the container log files
  (`deploy-plan.md` "Reseed procedure").
- **`/health`** (`src/Api/Program.cs:395`) is anonymous and answers `Healthy` today. `Degraded`
  also answers 200 (`OutboxHealthCheck.cs:27-30`), so an alert has to match the body text. App
  Service's built-in Health check is off (`healthCheckPath: null`), which is correct, because it only
  reacts to non-2xx.
- **`OutboxHealthCheck`** (`src/Infrastructure/Notifications/OutboxHealthCheck.cs:41-88`) has three
  signals: failed rows over a threshold, the oldest undelivered row over `MaxUndeliveredAge`
  (30 min), and no finished pass for `WorkerStallAfter` (5 min). It cannot tell a throttled e-mail
  lane from a stuck one. A throttled row is re-queued as `Pending` with `LastError = "acs_429"` and
  `NextAttemptAt` = the provider's `Retry-After`, clamped to 15 s–1 h (`OutboxDeliveryWorker.cs:321-331`).
  A throttle ends the email lane for the pass, and the rest of the claimed batch is released with
  the same `NextAttemptAt` but no `LastError` (`OutboxDeliveryWorker.cs:145-153`). So after a
  throttle nothing is sent, and nothing is re-throttled, for up to an hour.
- **The e-mail cap is hard.** The Azure-managed domain allows **5 emails/min and 10/hour per
  subscription, and higher limits are not available**. A verified custom domain starts at 30/min
  and 100/hour, and those can be raised through a support request (up to 72 h), but only while the
  failure rate stays under 1%
  ([ACS service limits](https://learn.microsoft.com/en-us/azure/communication-services/concepts/service-limits),
  read 2026-09-25). One cancelled class with 15 booked members already exceeds the hourly cap.
  Password resets draw from the same quota.
- **No security headers.** A live `GET /` returns `Server: Kestrel` and no HSTS, CSP,
  `X-Content-Type-Options`, `Referrer-Policy` or frame protection. There is no `UseExceptionHandler`.
  Outside Development an unhandled throw gives an empty-bodied 500, so nothing leaks today, but the
  body is not a deliberate contract either. OpenAPI maps only in Development (`Program.cs:372-376`;
  the live `/openapi/v1.json` returns 404), and the authorization probes only in `Testing`
  (`Program.cs:425`).
- **CSP hazards in the built SPA.** The production build inlines critical CSS as a
  `<style>` block plus `<link … media="print" onload="this.media='all'">`, and that `onload` is an
  inline script. Angular also injects component styles as runtime `<style>` elements. External
  origins in use are `https://www.youtube-nocookie.com/embed/…` (iframe), `https://img.youtube.com`
  (thumbnails) and `https://www.youtube.com/watch` (a plain link, which CSP does not govern)
  (`src/app/src/app/core/training/youtube.ts`). Fonts are self-hosted (`/fonts/*.woff2`). A service
  worker (`ngsw-worker.js`) and a manifest are served from `self`.
- **CI** (`.github/workflows/deploy.yml`) runs only on push to `main` and `workflow_dispatch`. It
  covers lint, build, SPA specs, `dotnet test`, publish, migrations and deploy. Nothing runs on a pull
  request, and the `./publish` output is **not** uploaded, so a rollback has no artifact to redeploy.
  The only uploaded artifact is `migrations.sql`. The deploy authenticates with
  `AZURE_WEBAPP_PUBLISH_PROFILE`, and the migration step with `AZURE_CREDENTIALS` (`pps-ci`, scoped
  to `pps-rg`).
- **Repo `rumek/po-prostu-silka` is public** (anonymous API returns 200), so branch protection with
  required status checks costs nothing. `gh` is installed but not authenticated. `az` is logged
  in to "Subskrypcja platformy Azure 1".
- **Database backups.** `pps-db` is Basic DTU with zone-redundant backup storage and PITR retention
  of 7 days. It has never been restored.
- **App settings** today: `Acs__*`, `AdminSeed__*`, `App__BaseUrl`, `VapidKeys__*`,
  `ASPNETCORE_ENVIRONMENT` (=`Staging`), `TestDataSeed__Enabled/Reset/Password`,
  `WEBSITE_HTTPLOGGING_RETENTION_DAYS`. HTTPS Only is on, min TLS is 1.2 and FTPS is FtpsOnly.
- **The Staging reset hazard** (`deploy-plan.md` "Warnings"): `TestDataSeed__Reset=true` left on
  wipes the database on every recycle. The user chose to keep `Staging` + the test club for demos,
  and a client's hand-added accounts would be wiped too.

## Desired End State

- Every response, the SPA shell and static files included, carries HSTS, an enforced CSP,
  `X-Content-Type-Options: nosniff`, `Referrer-Policy`, frame-ancestors protection and a
  `Permissions-Policy`, and no `Server` header. An unhandled exception answers a generic RFC 7807
  body with no exception detail, and the exception is recorded in Application Insights.
- Every screen of every persona works under the enforced CSP: exercise videos play, thumbnails load,
  push subscribes, the service worker registers.
- `/health` reports `Degraded` for a dead worker, dead-lettered mail, push stuck beyond 30 min, or
  e-mail stuck beyond 30 min *while not throttled*. A throttled e-mail lane reports `Degraded` only
  past a longer threshold (3 h). It also reports `Degraded` while `TestDataSeed:Reset` is `true`.
- An availability test on `/health` with a content match on `Healthy`, an alert on server errors and
  exceptions, and a $25 budget all e-mail the owner through one action group. The path has been
  proven once by forcing `Degraded`.
- A pull request runs the same checks as the deploy. `main` rejects direct pushes and requires those
  checks. Each deploy uploads its publish output, and a `rollback.yml` workflow redeploys a named
  earlier run's artifact without touching the schema.
- Rollback and a point-in-time restore to a new database have each been done once, timed and written
  down in `deploy-plan.md`. The e-mail limits and the demo checklist are written down too.
- **Blocked phase:** once a domain exists, the ACS sender is a verified custom domain, and nothing in
  the code changed to make it so.

Verify by `dotnet test`, `npm test`, `npm run quality:check`, `curl -sI` against the live URL, the
Azure portal's alert history, and the manual demo walk-through in Phase 5.

### Key Discoveries:

- A throttled row keeps `LastError = "acs_429"` (`AcsEmailSender.cs:97`) and stays `Pending` (or
  `Claimed` on its retry) until it is sent, and a successful send overwrites `LastError` with null.
  So "some undelivered email carries `acs_429`" is a durable signal that the email lane is waiting on
  the cap. It survives a recycle, and it lasts as long as the `Retry-After` wait (up to 1 h). An
  in-memory "throttled at" stamp in `OutboxWorkerHeartbeat` would not: nothing re-throttles during
  that wait to refresh it, and a recycle loses it. The throttle signal is derived from the table,
  and the heartbeat is unchanged.
- `IntegrationTestFixture` boots the app with `UseEnvironment("Testing")`
  (`tests/po-prostu-silka.Tests/IntegrationTestFixture.cs:423`). Header and exception-body tests run
  through it. Application Insights must stay off there, and it is, as long as it is registered only
  when a connection string exists.
- `MapFallbackToFile` answers 200 for every unmatched route (deploy-plan.md, F-01 gotchas). A
  header test on `/`, on a static file and on `/api/*` covers all three pipeline branches.
- `deploy.yml` already takes the app name, resource group and SQL server from `env:`. GL-01 holds as
  long as the new workflows do the same.

## What We're NOT Doing

- A second (production) environment, or artifact promotion between environments (parked by the user,
  GL-01).
- OIDC federated credentials for CI, Managed Identity for SQL, and tightening `AllowAzureServices`.
  All three are **recorded as accepted risks** in `infrastructure.md`, not changed (user decision
  2026-09-25).
- A custom domain for the app itself (`*.azurewebsites.net` stays). Phase 6 covers only the
  **sender** domain, and only once a domain is bought.
- Swapping the live database for the restored one during the rehearsal. The rehearsal restores to a
  new database, verifies a row, and deletes it; the swap is written as a runbook step, not performed.
- CSP nonces for styles. `style-src 'unsafe-inline'` stays, because Angular's runtime component
  styles need a nonce that only SSR can supply, and this app ships static.
- A CSP report endpoint or a Report-Only period.
- Scaling past one instance, or deployment slots (B1 has none).
- Any change to member-visible behaviour.

## Implementation Approach

Code first, cloud second, process third. Phases 1–2 are ordinary code changes with integration
tests, shipped through the existing pipeline. Phase 3 creates the Azure resources and turns on the
SDK, and it needs Phase 2's health signals to already be live, or its forced-`Degraded` proof would
page on the old semantics. Phase 4 changes how code reaches `main`, and lands last among the code
changes so Phases 1–3 do not have to go through a gate that is still being built. Phase 5 is
rehearsal and paperwork against the finished environment. Phase 6 waits for a domain.

Azure resources are created with `az` commands recorded in `deploy-plan.md`, the convention every
earlier infra change followed. There is no infrastructure-as-code, and GL-01 is served by writing
each command with its parameters named, so a second environment is a re-run with other names.

## Critical Implementation Details

**HSTS cannot use `UseHsts()`.** App Service terminates TLS and forwards plain HTTP, and
`UseHsts` writes the header only when `Request.IsHttps` is true. That holds only if forwarded
headers are processed, and this app does not configure them. Write HSTS in the same
security-headers middleware as the rest, unconditionally outside Development. HTTPS Only on the site
guarantees the browser only ever sees it over TLS.

**Critical-CSS inlining must be off before CSP ships, in the same deploy.** If the header lands
first, `onload="this.media='all'"` is blocked and the stylesheet stays `media="print"`, and every
screen renders unstyled. Set `optimization.styles.inlineCritical: false` in the production
configuration of `src/app/angular.json`, and check the built `index.html` has no `onload` and no
`<style>` block.

**The worker's polling is the telemetry cost hotspot.** Two lanes poll SQL every 15 s, which
produces thousands of parentless SQL dependency spans a day, plus an Information heartbeat per pass.
Export only logs at Warning and above, and drop dependency spans that have no parent request (or
disable SQL client instrumentation for the worker). Otherwise the daily cap trips on background noise
and hides the real exceptions.

**Every Azure change in Phases 3 and 5 restarts, or touches, the URL a client may be looking at.**
App settings restart the app. Schedule the forced-`Degraded` proof and the rollback rehearsal
outside any planned demo, and restore only to a **new** database name.

## Phase 1: Response hygiene & security headers

### Overview

Every response carries baseline security headers and an enforced CSP. The `Server` header is gone.
An unhandled exception answers a generic body. The SPA build stops emitting inline script.

### Changes Required:

#### 1. Security headers middleware

**File**: `src/Api/Http/SecurityHeaders.cs` (new), wired in `src/Api/Program.cs`

**Intent**: One middleware sets the header set on every response, before `UseDefaultFiles`, so
static files and the SPA fallback get it as well as the API. In Development it skips HSTS, so
localhost is not pinned to HTTPS. The headers are written from a `Response.OnStarting` callback, not
directly before `next()`. `UseExceptionHandler` calls `Response.Clear()`, which wipes headers already
set, but it keeps `OnStarting` callbacks, so this way the generic 500 carries the set too.

**Contract**: headers on every non-Development response:
`Strict-Transport-Security: max-age=31536000` (no `includeSubDomains`: `azurewebsites.net` is not
ours, and a future custom domain decides for itself), `X-Content-Type-Options: nosniff`,
`Referrer-Policy: strict-origin-when-cross-origin`, `X-Frame-Options: DENY`,
`Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=()`, and
`Content-Security-Policy`:

```
default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https://img.youtube.com;
font-src 'self'; connect-src 'self' https://img.youtube.com; frame-src https://www.youtube-nocookie.com; worker-src 'self';
manifest-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'
```

The CSP string lives in one constant with a comment that ties each external origin to its caller
(`youtube.ts`). A new external origin is a CSP change, reviewed as such.

`img.youtube.com` appears in `connect-src` as well as `img-src`, and that is deliberate. The service
worker gets its CSP from `ngsw-worker.js`'s own response headers, which this middleware also sets.
`ngsw-config.json` declares no groups, so ngsw answers every GET, the thumbnails included, with a
`fetch()` from the worker. A `fetch()` is governed by `connect-src`, not `img-src`, so without the
entry the thumbnails vanish as soon as the service worker controls the page.

#### 2. Server header and generic error body

**File**: `src/Api/Program.cs`

**Intent**: Stop advertising the server, and make the 500 body a deliberate contract instead of an
accident of the environment.

**Contract**: Kestrel `AddServerHeader = false`. `AddProblemDetails()` plus `UseExceptionHandler()`
outside Development, which answers `application/problem+json` with status 500 and a fixed title,
with no `detail`, no `exception` and no stack. Development keeps the developer exception page. The
existing handlers that return clean 409s are unaffected. This catches only what is unhandled today.

#### 3. SPA build: no inline script

**File**: `src/app/angular.json`

**Intent**: Remove the one inline script the build emits, so `script-src 'self'` holds.

**Contract**: production configuration `optimization.styles.inlineCritical: false`, with minify
unchanged. The built `dist/app/browser/index.html` contains no `onload=` attribute and no `<style>`
element.

#### 4. Tests

**File**: `tests/po-prostu-silka.Tests/SecurityHeadersTests.cs` (new)

**Intent**: Pin the header set, and the generic error body, on the three pipeline branches.

**Contract**: under `Testing` (non-Development), `GET /`, `GET /favicon.ico` (or another shipped
static file) and `GET /api/<anonymous route>` each carry every header above with the exact CSP
directives, and no `Server` header. `/` and the static file also assert 200 and their content type
(`text/html`, and the file's own type), so the test cannot pass on a 404 fallback when `wwwroot` is
empty. `src/Api/wwwroot` is git-ignored and is only filled by the pipeline's staging step. An endpoint that throws answers 500 `application/problem+json`
whose body has no `detail`/`exception`/stack text, and which still carries the full header set. If no route throws on demand, add a
`Testing`-only probe next to the existing `/test/*` ones and document it in the same comment block.

**Adapted during implementation.** The probe is `GET /test/throw`, and it is anonymous, so
`EndpointAuthorizationTests`' non-API allowlist (`AnonymousNonApiPatterns`) gained it with a comment:
that test enumerates the `Testing` host's routes and would otherwise fail. The API-branch case uses
`GET /api/auth/me` anonymously (a 401), since no anonymous `GET` exists under `/api`. The "no
`Server` header" assertion holds trivially under TestServer; the live `curl -sI` (1.6) is what proves
Kestrel's header is gone.

### Success Criteria:

#### Automated Verification:

- Backend builds warning-free: `dotnet build po-prostu-silka.slnx`
- All backend tests pass, including the new `SecurityHeadersTests`: `dotnet test po-prostu-silka.slnx`
- SPA lint and format pass: `npm run quality:check` (in `src/app`)
- SPA specs pass: `npm test` (in `src/app`)
- The production build's `index.html` has no inline `onload` handler and no `<style>` block: `npm run build` then grep `src/app/dist/app/browser/index.html`

#### Manual Verification:

- After deploy, `curl -sI https://po-prostu-silka.azurewebsites.net/` shows every header and no `Server`
- Clicking through as a member, a trainer and an admin (`admin1`, `trener1`, `czlonek001`) on a phone width and a desk width leaves the browser console free of CSP violations: dashboard, schedule and calendar, roster and attendance, karnet, plan card, exercise detail with a playing YouTube embed, the exercise list's thumbnails (checked again after a reload, once the service worker controls the page), Moje konto, password change, push subscribe
- The styles load on first paint (no flash of unstyled content beyond what was there before)

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before
proceeding to the next phase.

---

## Phase 2: Health signals that mean something

### Overview

`/health` separates "e-mail is waiting on a known provider cap" from "delivery is broken", and warns
while the test-data reset is armed.

### Changes Required:

#### 1. The email throttle, derived from the table

**File**: `src/Infrastructure/Notifications/OutboxHealthCheck.cs`

**Intent**: The health check tells that emails are waiting on the cap, not stuck, from state that
survives a recycle and lasts as long as the wait. See Key Discoveries: an in-memory stamp is
refreshed by nothing during a `Retry-After` wait of up to 1 h.

**Contract**: the email lane counts as **throttled** while at least one email row that is `Pending`
or `Claimed` has `LastError = "acs_429"`. That is one extra query per `/health`. The worker and
`OutboxWorkerHeartbeat` are unchanged. The `"acs_429"` literal gets one shared constant, used by
`AcsEmailSender` and by the check.

#### 2. Per-channel undelivered age, with a throttle-aware threshold

**File**: `src/Infrastructure/Notifications/OutboxHealthCheck.cs`,
`src/Infrastructure/Notifications/OutboxOptions.cs`

**Intent**: Push and email are judged separately. A throttled email lane gets a longer allowance.
Everything else keeps today's 30 min.

**Contract**: new option `Outbox:ThrottledMaxUndeliveredAge`, default 3 h. The oldest undelivered
**push** is judged against `MaxUndeliveredAge`. The oldest undelivered **email** is judged against
`ThrottledMaxUndeliveredAge` while the lane is throttled (§1), and against `MaxUndeliveredAge`
otherwise. `data` reports both ages and whether email is throttled, so
the `/health` JSON (if the writer is ever switched to JSON) and the log both say why. The failed-rows
and worker-stall signals are unchanged. The class comment's "THREE SIGNALS" paragraph is updated
rather than appended to.

#### 3. Reset-armed signal

**File**: `src/Infrastructure/TestData/TestDataResetHealthCheck.cs` (new), registered in
`src/Api/Program.cs` beside `OutboxHealthCheck`

**Intent**: A `TestDataSeed:Reset=true` left on (the step-3 omission `deploy-plan.md` warns about)
becomes an alert rather than a silent wipe on the next recycle.

**Contract**: `Degraded` with the message "TestDataSeed:Reset is on: the next restart wipes the
database" exactly when the next startup's seeder would wipe. `Healthy` otherwise. It reads
configuration only and never touches the DB. "Would wipe" has **one** definition, a static
predicate on `TestDataSeeder` (for example `WouldWipe(options, environment)`: `Enabled`,
Development or Staging, a non-blank `Password`, and `Reset`). `SeedAsync`'s own gate
(`TestDataSeeder.cs:40-84`) and the health check both call it, so the alarm cannot drift from the
wipe. The password-policy check stays in `SeedAsync` only: a password that fails it refuses the seed,
and the alarm would then over-report, which is the safe direction.

#### 4. Tests

**File**: `tests/po-prostu-silka.Tests/OutboxDeliveryTests.cs` (extend) or a new
`OutboxHealthTests.cs`; `tests/po-prostu-silka.Tests/TestDataSeederTests.cs` (extend)

**Contract**, as named cases:
- email pending 45 min, one pending email carries `acs_429` → `Healthy`
- email pending 45 min, throttled 40 min ago with a 60 min `Retry-After` still running → `Healthy`
  (the wait outlasts `MaxUndeliveredAge`)
- the same state read by a fresh heartbeat (a recycle) → `Healthy`
- email pending 45 min, no `acs_429` on any undelivered email → `Degraded`
- email pending 4 h, lane throttled → `Degraded`
- push pending 45 min while the email lane is throttled → `Degraded` (the throttle never exempts push)
- `TestDataSeed:Reset=true` (with `Enabled` and `Password`) under Staging → `Degraded`; under
  Production → `Healthy`; `Reset=true` with `Enabled=false` under Staging → `Healthy`. These cases
  construct the check directly with a stub environment, as `OutboxDeliveryTests.cs:462` does. They
  **never** boot the fixture as Staging with Reset on, because that would run the wipe against the
  test database.

**Adapted during implementation.** The cases went into `OutboxDeliveryTests.cs` and
`TestDataSeederTests.cs`. The existing `Health_is_Degraded_when_a_message_has_waited_too_long`
(a 31-minute wait behind a throttle) asserted exactly the old semantics, so it was replaced by the
"45 min, nothing throttled → Degraded" case rather than kept. The reset theory has a fourth row
(`Reset=false` under Staging → `Healthy`). `TestDataSeeder` also gained `ReadOptions`, so the check
and the seeder bind the section the same way.

### Success Criteria:

#### Automated Verification:

- Backend builds warning-free: `dotnet build po-prostu-silka.slnx`
- All backend tests pass, including the named health cases: `dotnet test po-prostu-silka.slnx`

#### Manual Verification:

- After deploy, `/health` on the live URL answers `Healthy` (Reset is `false` there)

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before
proceeding to the next phase.

---

## Phase 3: Telemetry, alerts & budget

### Overview

The owner hears first. Exceptions and server errors are searchable, `/health` is watched from
outside, the bill is capped by an alert, and the whole alert path has been proven once.

### Changes Required:

#### 1. Azure resources

**File**: `context/deployment/deploy-plan.md`, new section "Client-ready environment (S-28)", with
every `az` command as executed

**Intent**: Create the observability plumbing in `pps-rg`, named so a second environment repeats it
with other names (GL-01).

**Contract**:
- A Log Analytics workspace `pps-logs` with a **daily cap** of 0.1 GB and 30-day retention, and a
  workspace-based Application Insights `pps-ai` on it.
- App setting `APPLICATIONINSIGHTS_CONNECTION_STRING` on the site (a restart).
- An action group `pps-owner` that e-mails the owner.
- A **Standard availability test** on `https://po-prostu-silka.azurewebsites.net/health`: every
  5 min, from 3 locations or more, content match "must contain `Healthy`", with the default alert
  (2 or more locations failing) wired to `pps-owner`.
- A log/metric alert on server exceptions (any exception or any 5xx request in 5 min) → `pps-owner`.
- A budget of $25/month on `pps-rg`, with notifications at 80% actual and 100% forecast →
  the owner's e-mail. Plain `az consumption budget create` is not known to accept notification
  thresholds, so use `az consumption budget create-with-rg --notifications …`, or `az rest` against
  `Microsoft.Consumption/budgets`. **Check the current syntax when implementing.** Whichever command
  works is the one recorded, and 3.4 confirms that the notifications exist, not only the budget.

#### 2. OpenTelemetry in the host

**File**: `src/Api/po-prostu-silka.Api.csproj`, `src/Api/Program.cs`

**Intent**: Send requests, exceptions and warnings to Application Insights without letting the
worker's polling fill the cap.

**Contract**: package `Azure.Monitor.OpenTelemetry.AspNetCore`, registered with
`AddOpenTelemetry().UseAzureMonitor(...)` **only when `APPLICATIONINSIGHTS_CONNECTION_STRING` is
set**, so tests, Development and CI stay unregistered. Rate-limited sampling (`TracesPerSecond`
kept low, for example 1). The OpenTelemetry log filter is at `Warning`. SQL dependency spans with no
parent request (the worker's polling) are dropped. The package goes through
`dotnet list package --vulnerable` before commit, per the bootstrap audit. Check the current API
shape against the Azure Monitor OpenTelemetry docs when implementing (Context7
`/microsoftdocs/azure-monitor-docs`). Sampling option names differ between the distro and SDK 3.x.

**Adapted during implementation.** `Azure.Monitor.OpenTelemetry.AspNetCore` 1.6.0, registered in
`src/Api/Telemetry/Telemetry.cs`. `TracesPerSecond = 1`. Also `EnableTraceBasedLogsSampler = false`,
which the plan did not name: the distro drops by default any log whose request was sampled out, and
that would have dropped exactly the exceptions this phase exists to keep. Logs are cut to Warning, so
keeping all of them is cheap. "Parentless SQL span" means a span tagged `db.system` /
`db.system.name` with no parent, and a processor registered before `UseAzureMonitor` clears its
`Recorded` flag. Verified locally that the host boots with a dummy connection string set.

#### 3. Proof of the alert path

**Intent**: Prove, once, that a `Degraded` body reaches the owner's inbox.

**Contract**: set `Outbox__WorkerStallAfter=00:00:01` (deploy-plan.md's procedure), wait for the
alert e-mail, then remove the setting and confirm the alert resolves. The exception alert cannot
be forced the same way: Phase 1's throwing probe exists only under `Testing` (`Program.cs:425`), and
Staging has none. So prove its two halves separately. First, fire the exception rule's action
through the portal's **Test action group**, which must deliver the e-mail. Second, run the rule's
own query once by hand against the `exceptions` and `requests` tables, and confirm it parses and
returns rows when widened to a period that has any. Record both results.

### Success Criteria:

#### Automated Verification:

- Backend builds warning-free and tests pass with no connection string set: `dotnet test po-prostu-silka.slnx`
- No new vulnerable package: `dotnet list package --vulnerable`
- The resources exist: `az resource list -g pps-rg -o table` lists the workspace, Application Insights, the availability test, the alert rules and the action group
- The budget exists: `az consumption budget list -o table` lists the $25 budget

#### Manual Verification:

- Requests appear in App Insights "Live metrics" / `requests` within minutes of the deploy
- The forced `Degraded` produces an alert e-mail, and the alert resolves after the setting is removed
- After 24 h, the workspace's daily ingestion is well under the 0.1 GB cap (Usage and estimated costs)

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before
proceeding to the next phase.

---

## Phase 4: PR gate & rollback pipeline

### Overview

A change reaches `main` only through a pull request whose checks already ran. Every deploy keeps
the artifact a rollback needs.

### Changes Required:

#### 1. Pull-request checks

**File**: `.github/workflows/ci.yml` (new)

**Intent**: The same checks the deploy runs, run on every pull request to `main`, with no Azure
credentials.

**Contract**: triggers on `pull_request` to `main`. One job (or two parallel: SPA, backend) running
`npm ci`, `npm run quality:check`, `npm run build`, `npm test`, the same "Stage Angular output into
wwwroot" step as `deploy.yml` (**before** the backend tests, because `SecurityHeadersTests` needs a
real `index.html` and static file), `dotnet test po-prostu-silka.slnx -c Release`, plus the idempotent-migration-script generation as a build check. The job names are
stable, because branch protection references them by name. No secrets, since a PR from a fork must
not reach them.

#### 2. Keep the artifact, add a rollback

**File**: `.github/workflows/deploy.yml`, `.github/workflows/rollback.yml` (new)

**Intent**: A rollback redeploys bytes that already ran, instead of rebuilding an old commit.

**Contract**: `deploy.yml`'s `push` trigger gains `paths-ignore: ['context/**', '**/*.md']`.
Once `main` is protected, every Progress update and every Phase 5 document arrives as a merged PR,
and without the filter each one would rebuild, migrate and restart the URL a client may be looking
at. `workflow_dispatch` still deploys on demand. **`ci.yml` gets no path filter**: a required check
that never runs stays pending forever, and the PR could never merge. `deploy.yml` uploads `./publish` as `publish-${{ github.run_id }}` (30-day retention,
matching `migrations.sql`). `rollback.yml` is `workflow_dispatch` with input `run_id`. It downloads
that run's `publish-*` artifact and deploys it with the same publish-profile step and the same
`env:` app name. **It runs no migration**: schema stays ahead of code, which the reversible-migration
policy guarantees is safe. Its summary names the run and commit it deployed.

#### 3. Branch protection

**File**: `context/deployment/deploy-plan.md` (the command as executed)

**Intent**: GL-04. `main` accepts only merged PRs with green checks.

**Contract**: after `gh auth login` (the user runs it: `! gh auth login`), protect `main` with the
`ci.yml` job names as required status checks, "require branches to be up to date", no required
reviewers, "include administrators" on (no bypass), and force-push and deletion blocked. The
`staging-is-main` memory is updated: "push na staging" now means merging a PR into `main`.

### Success Criteria:

#### Automated Verification:

- Workflow files are valid YAML and reference only existing secrets: the first PR's `ci.yml` run is green
- A direct `git push origin main` is rejected by GitHub (tried with an empty commit on a throwaway branch pushed as `main`, or confirmed with `gh api repos/rumek/po-prostu-silka/branches/main/protection`)

#### Manual Verification:

- The PR carrying this phase shows the required checks and cannot merge until they pass
- The deploy triggered by that merge uploads a `publish-<run id>` artifact

**Implementation Note**: This phase's own changes go through the first PR, which is the gate being
proven. After completing this phase and all automated verification passes, pause here for manual
confirmation from the human that the manual testing was successful before proceeding to the next
phase.

---

## Phase 5: Rehearsals & client-demo readiness

### Overview

Recovery is a procedure that has been done, not one that has been written. The demo is a checklist
that has been walked through.

### Changes Required:

#### 1. Rollback rehearsal

**Intent**: Prove `rollback.yml` and time it.

**Contract**: run `rollback.yml` with the run id of the deploy before the latest, confirm `/health`
is `Healthy` and the previous build is serving (check the body of a route that differs, not a status
code), then run it again with the latest run id. Record the time-to-revert in `deploy-plan.md`.

#### 2. Point-in-time restore rehearsal

**Intent**: Prove a lost row can be recovered without ever writing over the live database.

**Contract**: `az sql db restore` of `pps-db` to a point ~1 h back, as `pps-db-restore-<yyyymmdd>`
on the same server. Query one known row in the restored copy (JIT firewall rule for the workstation,
as CI does), then delete the copy. The runbook records the command, how long it took, the retention
(7 days), and, **as written steps only**, how to copy a row back or repoint the `Default` connection
string (type `SQLAzure`).

#### 3. Documentation

**File**: `context/deployment/deploy-plan.md`, `context/foundation/infrastructure.md`

**Intent**: GL-06 and GL-01 written down. The accepted risks are recorded as decisions.

**Contract**:
- deploy-plan.md: the managed-domain limits (5/min, 10/h per subscription, not raisable), the
  consequence (a class of N booked members delivers its last emails N/10 hours later) and the demo
  rule that follows until Phase 6 (demo cancellations on classes with at most a handful of
  real-address members). A **demo checklist**: set Reset to false, `/health` is `Healthy`, a
  client account is added with a real address, and push is subscribed on the demo phone (iOS needs
  the installed PWA).
- infrastructure.md risk register: OIDC not adopted, SQL auth instead of Managed Identity, and
  `AllowAzureServices` are each recorded as **accepted until the second environment**, with the reason.
  The App Insights bill-creep row is updated to "mitigated: daily cap + budget".

#### 4. End-to-end demo walk-through

**Intent**: GL-02, done once, on the live URL, with real addresses.

**Contract**: one real-address member account (owner-controlled) and the seeded staff. The walk-through
covers admin issues a karnet and books the member, a trainer marks attendance, the member sees the
karnet and attendance history and the plan with a playing video, the admin cancels the class and
the member receives the **e-mail and the push**, and the member resets a forgotten password from the
received link. Everything is checked on a phone.

### Success Criteria:

#### Automated Verification:

- `/health` answers `Healthy` after both rollback runs: `curl -s https://po-prostu-silka.azurewebsites.net/health`
- The restored copy is gone again: `az sql db list -g pps-rg -s pps-sql -o table` shows only `pps-db` and `master`

#### Manual Verification:

- Rollback performed and timed, with the time recorded
- PITR to a new database performed, a row verified, and the copy deleted
- The demo walk-through passes end to end, including the e-mail and push actually arriving
- deploy-plan.md and infrastructure.md updated as specified

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before
proceeding to the next phase.

---

## Phase 6: Custom sender domain (BLOCKED until a domain is bought)

### Overview

Lift the e-mail cap from 10/h to 100/h (raisable), and move mail off the unbranded `azurecomm.net`
sender. Configuration only. Do not start this phase until the owner has a domain with DNS access.

### Changes Required:

#### 1. ACS custom domain

**File**: `context/deployment/deploy-plan.md` (the "Before production: ACS sender domain and quota"
section becomes a record of what was done)

**Intent**: Follow that section's steps 1–5 against the bought domain, preferably a subdomain such
as `mail.<domain>`.

**Contract**: the domain is `Verified` for ownership, SPF and both DKIM records, and is connected to
`pps-acs`. A sender username `powiadomienia` exists. `Acs__SenderAddress` points at it. No code
changes. The managed domain stays linked until the first mail from the new sender arrives, then it is
unlinked.

#### 2. Limits and health threshold

**Intent**: Re-read the current custom-domain limits, compare them with the worst case, and retune.

**Contract**: the worst case recorded (largest class capacity × cancellations per hour + resets). A
quota request is filed if the default 100/h is below it. `Outbox:ThrottledMaxUndeliveredAge` is
reconsidered (it can come down now) and the decision is written down.

### Success Criteria:

#### Automated Verification:

- `az communication email domain list --email-service-name pps-email -g pps-rg -o table` shows the custom domain `Verified`

#### Manual Verification:

- A cancellation e-mail from the new sender reaches a Gmail and an Outlook inbox, not spam
- The limits and the worst-case comparison are recorded in deploy-plan.md

---

## Testing Strategy

### Unit Tests:

- None new at unit level. This repo's backend tests are integration tests over the real app, and
  that is where headers and health belong.

### Integration Tests:

- `SecurityHeadersTests`: the full header set and exact CSP on `/`, a static file and an API route;
  no `Server` header; a generic `problem+json` 500 with no exception detail.
- Outbox health: the named cases in Phase 2 §4, including the long-`Retry-After` and recycle cases and the one that proves a throttle
  never exempts push.
- Test-data reset health: `Degraded` under Staging with Reset on, `Healthy` under Production.

### Manual Testing Steps:

1. Click through all three personas at phone and desk widths with the console open. There must be
   zero CSP violations, the YouTube embed plays, and thumbnails load.
2. Force `Degraded` via `Outbox__WorkerStallAfter`. The alert e-mail arrives, and resolves once the
   setting is removed.
3. Roll back to the previous run and forward again, timing it.
4. Restore to a new database, read a row, delete the copy.
5. Run the demo walk-through with a real address, and check that the e-mail and the push arrive.

## Performance Considerations

- The security headers are a fixed set written per response, which costs nothing measurable.
- Turning off critical-CSS inlining trades a slightly later first styled paint for a working CSP.
  The stylesheet is already preloaded via `<link rel="stylesheet">`, so the difference on B1 is small.
  Check it in Phase 1's manual step.
- App Insights: rate-limited sampling, Warning-level logs, parentless SQL spans dropped, a 0.1 GB
  daily cap. The cap is the backstop, not the plan.

## Migration Notes

No schema change. No EF migration. Configuration added to the App Service:
`APPLICATIONINSIGHTS_CONNECTION_STRING` (Phase 3), and in Phase 6 a changed `Acs__SenderAddress`.
`Outbox__ThrottledMaxUndeliveredAge` stays at its code default unless retuned.

## References

- Roadmap item: `context/foundation/roadmap.md` S-28, M-9 GL-01–GL-06
- Operational inputs: `context/deployment/deploy-plan.md` "Notification delivery hardening" and its
  three "Before production" sections; `context/foundation/infrastructure.md` risk register
- ACS limits: https://learn.microsoft.com/en-us/azure/communication-services/concepts/service-limits
- Health check: `src/Infrastructure/Notifications/OutboxHealthCheck.cs:41-88`
- Throttle handling: `src/Infrastructure/Notifications/OutboxDeliveryWorker.cs:321-331`
- Pipeline: `src/Api/Program.cs:372-435`, `.github/workflows/deploy.yml`
- External origins: `src/app/src/app/core/training/youtube.ts`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Response hygiene & security headers

#### Automated

- [x] 1.1 Backend builds warning-free — 9943603
- [x] 1.2 All backend tests pass, including the new SecurityHeadersTests — 9943603
- [x] 1.3 SPA lint and format pass — 9943603
- [x] 1.4 SPA specs pass — 9943603
- [x] 1.5 The production build's index.html has no inline onload handler and no style block — 9943603

#### Manual

- [ ] 1.6 After deploy, the live URL shows every header and no Server header
- [ ] 1.7 Click-through as member, trainer and admin at phone and desk widths leaves the console free of CSP violations
- [ ] 1.8 The styles load on first paint

### Phase 2: Health signals that mean something

#### Automated

- [x] 2.1 Backend builds warning-free — 1d71681
- [x] 2.2 All backend tests pass, including the named health cases — 1d71681

#### Manual

- [ ] 2.3 After deploy, /health on the live URL answers Healthy

### Phase 3: Telemetry, alerts & budget

#### Automated

- [x] 3.1 Backend builds warning-free and tests pass with no connection string set
- [x] 3.2 No new vulnerable package
- [ ] 3.3 The resources exist in pps-rg
- [ ] 3.4 The budget exists

#### Manual

- [ ] 3.5 Requests appear in App Insights within minutes of the deploy
- [ ] 3.6 The forced Degraded produces an alert e-mail, and the alert resolves after the setting is removed
- [ ] 3.7 After 24 h, daily ingestion is well under the cap

### Phase 4: PR gate & rollback pipeline

#### Automated

- [ ] 4.1 The first PR's ci.yml run is green
- [ ] 4.2 A direct push to main is rejected

#### Manual

- [ ] 4.3 The PR shows the required checks and cannot merge until they pass
- [ ] 4.4 The deploy triggered by the merge uploads a publish artifact

### Phase 5: Rehearsals & client-demo readiness

#### Automated

- [ ] 5.1 /health answers Healthy after both rollback runs
- [ ] 5.2 The restored copy is gone again

#### Manual

- [ ] 5.3 Rollback performed and timed, with the time recorded
- [ ] 5.4 PITR to a new database performed, a row verified, and the copy deleted
- [ ] 5.5 The demo walk-through passes end to end, including e-mail and push arriving
- [ ] 5.6 deploy-plan.md and infrastructure.md updated as specified

### Phase 6: Custom sender domain (BLOCKED until a domain is bought)

#### Automated

- [ ] 6.1 The custom domain shows Verified

#### Manual

- [ ] 6.2 A cancellation e-mail from the new sender reaches a Gmail and an Outlook inbox, not spam
- [ ] 6.3 The limits and the worst-case comparison are recorded in deploy-plan.md
