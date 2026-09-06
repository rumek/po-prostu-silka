<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Member profile edit with address/phone fields, password change, and password reset

- **Plan**: context/changes/member-profile-edit/plan.md
- **Scope**: Phases 1–5 of 5 (all phases; 5.3/5.4 manual still pending)
- **Date**: 2026-09-06
- **Verdict**: REJECTED at review; all 10 findings triaged and fixed on 2026-09-06
- **Findings**: 1 critical, 4 warnings, 5 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | FAIL |
| Scope Discipline | PASS |
| Safety & Quality | FAIL |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

Automated criteria re-run for this review: `dotnet build` 0 warnings / 0 errors; `dotnet test` 403/403;
`npm test` 368/368 across 37 files; `npm run quality:check` clean; migration `Down` present and
complete. Phase 5's two greps both behave as the plan specifies. Manual rows 5.3 and 5.4 are still
`- [ ]` — a read-through, not evidence of rubber-stamping.

## Findings

### F1 — The IP rate limiter never triggers behind App Service

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/Program.cs:162-166
- **Detail**: The partition key is `X-Forwarded-For`'s first segment, used verbatim. App Service and
  Front Door write that header as `client-ip:ephemeral-port` (e.g. `203.0.113.5:51422`), and the port
  changes on every connection — so each request from one caller lands in its own partition and the
  5-per-minute cap never fires in production. The integration test passes only because it injects a
  bare `203.0.113.N` with no port, which is not the production header shape. A second defect on the
  same lines: an empty `X-Forwarded-For:` header yields `clientIp == ""` (the `??` does not fire on an
  empty string), collapsing those callers into one shared partition. Blast radius is bounded — the
  per-address throttle still caps mail per mailbox — but the volume cap on the only anonymous endpoint
  that spends ACS quota is effectively absent, while the comment above it asserts it works.
- **Fix A ⭐ Recommended**: Parse the forwarded value and partition on the address alone
  (`IPEndPoint.TryParse` / `IPAddress.TryParse`, falling back to the raw trimmed value), and treat an
  empty string like a missing header.
  - Strength: Local to the one lambda, no pipeline change, and testable — add a case whose header is
    `203.0.113.5:51422` then `203.0.113.5:51423` and assert they share a partition.
  - Tradeoff: Keeps trusting a spoofable header, which the existing comment already concedes.
  - Confidence: HIGH — the port suffix is documented App Service behaviour and the current code
    demonstrably keeps it.
  - Blind spot: Have not confirmed the exact header shape this specific App Service instance emits;
    worth one look at a real request log before shipping the fix.
- **Fix B**: Adopt `UseForwardedHeaders` with `KnownProxies`/`ForwardLimit` and partition on
  `context.Connection.RemoteIpAddress`.
  - Strength: Fixes it once for every future consumer of the client IP, not just this limiter.
  - Tradeoff: Middleware ordering and proxy configuration is a bigger change than this slice's
    remaining scope, and misconfiguring `KnownProxies` silently drops back to the proxy's own address.
  - Confidence: MEDIUM — correct in principle, more moving parts to get wrong on Basic-tier hosting.
  - Blind spot: The App Service outbound proxy set is not pinned anywhere in this repo.
- **Decision**: FIXED via Fix A — partition key parsed with `IPEndPoint.TryParse` in `RateLimitPolicies.PartitionKey`; empty header treated as missing; regression test `One_caller_shares_a_partition_across_ephemeral_ports` sends the production `ip:port` shape

### F2 — /forgot-password returns early on "user not found", against an explicit plan contract, and the docblock claims otherwise

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Plan Adherence
- **Location**: src/Application/Auth/AuthEndpoints.cs:456-471 (comment at 427-431)
- **Detail**: The plan's Critical Implementation Details (plan.md:108-113) says in as many words: "do
  not return early on 'user not found' before the work an existing user's path performs". The handler
  does exactly that — blank email, unknown user and throttled repeat each `return Results.Ok()` before
  `GeneratePasswordResetTokenAsync`, `Notify` and `SaveChangesAsync`. The response *shape* is
  identical (which is the property that matters most, and it is tested), but the latency is not: an
  unknown address costs one indexed SELECT, a known one a Data-Protection token mint plus an INSERT
  and commit against Azure SQL Basic. No "**Adapted during implementation.**" note covers this, and
  the XML doc at 427-431 asserts the opposite is true — a future reader will trust it rather than
  re-derive it. No automated test can catch this: the non-disclosure tests assert status, body and
  outbox, never timing.
- **Fix A ⭐ Recommended**: Correct the comment to state what actually holds (identical status and
  body; latency deliberately not equalised) and record the decision as an accepted risk with an
  "Adapted during implementation." note on the Phase 4 item-6 contract in plan.md.
  - Strength: Cheap, honest, and closes the worse half of the problem — a false security comment is
    more dangerous than the millisecond gap, because it stops the next reader from looking.
  - Tradeoff: Leaves a measurable (if noisy over the internet) timing oracle in place.
  - Confidence: HIGH — the gap is real but the practical exploit needs many samples through the
    5/minute cap and App Service jitter.
  - Blind spot: Have not measured the actual delta against Azure SQL Basic; if it is tens of
    milliseconds rather than single digits, the risk assessment changes.
- **Fix B**: Close the gap — keep the early returns but hold every response to a fixed time budget,
  or move the enqueue and commit off the request path.
  - Strength: Delivers the invariant the plan asked for and the comment already promises.
  - Tradeoff: A fixed delay occupies a request thread on the one endpoint an anonymous caller can
    spam; moving work off-request means a background queue this slice does not have.
  - Confidence: MEDIUM — correct in principle, and the second variant is real new machinery.
  - Blind spot: A fixed budget interacts with the rate limiter in F1; both change the same endpoint.
- **Decision**: FIXED via Fix A — docblock corrected to state what actually holds, and the latency gap recorded as an accepted risk in plan.md (Phase 4 item 6)

### F3 — An exception on the found-user path turns the endpoint into a 500-vs-200 oracle

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Application/Auth/AuthEndpoints.cs:473-478
- **Detail**: The app registers no `UseExceptionHandler` and no `AddProblemDetails`. If
  `GeneratePasswordResetTokenAsync`, `Notify` or `SaveChangesAsync` throws — SQL throttling on Basic
  DTU, retry exhaustion, an outbox constraint — a registered address answers 500 while an
  unregistered one still answers 200. That differential appears exactly under load, which is when
  someone is most likely probing. This is the same class of failure F2 describes, but with a far
  larger and more reliable signal.
- **Fix**: Wrap the token/notify/save block in try/catch, log the exception, and return `Results.Ok()`
  regardless — the one place in this codebase where swallowing an exception is correct, and the
  docblock already promises it.
- **Decision**: FIXED — token/notify/save wrapped in try/catch, exception logged, `Results.Ok()` returned regardless

### F4 — Live reset tokens travel in a query string that gets logged

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/Application/Notifications/PasswordResetNotification.cs:63-66,
  src/app/src/app/features/auth/reset-password/reset-password.ts:76-80
- **Detail**: The link is same-origin with the API, so opening it is an ordinary request that
  `MapFallbackToFile` serves — and ASP.NET Core request logs plus App Service HTTP logs record query
  strings verbatim. The token also lands in browser history and in any synced session. The
  query-string choice itself is right and well argued (Identity tokens contain `+` and `/`, which a
  path segment mangles), `index.html` loads no third-party resources so there is no `Referer` leak,
  and the token dies on first use — so this is a log-hygiene problem, not a broken design.
- **Fix**: In `ResetPassword`'s constructor, after reading the parameters, call
  `router.navigate([], { queryParams: {}, replaceUrl: true })` to scrub browser history; and either
  suppress the query string for `/reset-password` in request logging or document the log exposure as
  accepted.
- **Decision**: FIXED — `ResetPassword` scrubs the query string with `router.navigate([], { queryParams: {}, replaceUrl: true })`; server-side log exposure documented as accepted

### F5 — The profile save discards Identity's failure without logging it

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/Application/Members/ProfileEndpoints.cs:88-95
- **Detail**: `UpdateAsync` failing returns `Results.Problem(..., 500)` with the error collection
  thrown away. The sibling that predates this slice does the opposite — `AuthEndpoints.RegisterAsync`
  (:311-318) takes an `ILoggerFactory`, logs `Errors`, then returns the 500. A member reporting "saving
  my address 500s" currently leaves nothing to diagnose from.
- **Fix**: Inject `ILoggerFactory` as `RegisterAsync` does and log `updated.Errors` before returning.
- **Decision**: FIXED — `ILoggerFactory` injected, `updated.Errors` logged before the 500, matching `RegisterAsync`

### F6 — The phone regex is duplicated in two components and is stricter than the server

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/app/src/app/features/auth/register/register.ts:23,
  src/app/src/app/features/profile/profile.ts:21
- **Detail**: `PHONE_PATTERN` is copied verbatim into both components, guarded only by a comment
  saying the copies must stay identical — the exact drift `ContactDetails` was created to prevent on
  the server. It is also not the mirror it claims to be: the server strips `(` and `)`, so
  `(12) 345 67 89` is accepted by the API and rejected by the form. Client-stricter fails safe, but
  the comment overstates the relationship.
- **Fix**: Hoist `PHONE_PATTERN`, `POSTAL_CODE_PATTERN` and `MIN_PASSWORD_LENGTH` into a shared
  `core/auth/` constants module, and either widen the pattern or soften the comment to "approximates".
- **Decision**: FIXED — constants hoisted to `core/auth/validation.ts`, consumed by register, profile and reset-password; the stricter-than-server phone rule documented as deliberate

### F7 — The BaseUrl misconfiguration is logged one layer below where the plan put it

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: src/Application/Notifications/PasswordResetNotification.cs:47-57
- **Detail**: The plan (plan.md:497-498) says "the forgot-password handler logs an error and still
  returns the standard response". In the code the handler is unaware; the notification logs and
  returns, and the handler still commits an empty unit of work and answers 200. Behaviourally
  identical and arguably better placed — the notification is what needs the URL — but it relocates a
  named responsibility without a note.
- **Fix**: Add an "Adapted during implementation." line to the Phase 4 item-1 contract saying the
  error is logged in the notification.
- **Decision**: FIXED — "Adapted during implementation." note added to the Phase 4 item-1 contract

### F8 — The roadmap's backlog row still points at a phase that has landed

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: context/foundation/roadmap.md (Backlog Handoff, S-13 row)
- **Detail**: The row reads `Planned — run /10x-implement member-profile-edit phase 5`, but Phase 5
  landed in 40a1294. `Status: in-progress` on the S-13 block is still correct while 5.3/5.4 are
  unchecked, but the handoff row would send someone to re-run finished work.
- **Fix**: Update the row when the change closes out — or let `/10x-archive` do it.
- **Decision**: FIXED — Backlog Handoff row now reads "Implemented and reviewed — awaiting close-out"

### F9 — The throttle consumes its window before the email is committed

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Application/Auth/PasswordResetThrottle.cs:99-115, src/Application/Auth/AuthEndpoints.cs:468-478
- **Detail**: `TryAcquire` runs before the enqueue and commit, so a failed save locks the member out
  of retrying for two minutes with no email sent. Also, `Prune` walks the whole dictionary on every
  call once `Count >= 1000` and removes nothing if all entries are fresh. Both are acceptable at this
  app's scale, and the key set is bounded by member count because `TryAcquire` is reached only after
  the user lookup succeeds — a good ordering choice worth keeping deliberately.
- **Fix**: Note both properties in the class docs.
- **Decision**: FIXED — both properties documented on `TryAcquire` and `Prune`

### F10 — Two accepted asymmetries worth stating rather than leaving implicit

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Application/Auth/AuthEndpoints.cs:137,148,
  src/Infrastructure/Persistence/Migrations/20260905190052_AddMemberContactDetails.cs:13-21
- **Detail**: (a) `/change-password` does not feed Identity's lockout on a wrong current password,
  unlike `/login`, so a stolen session can enumerate the current password without tripping lockout —
  low impact, the session is already compromised. (b) `Up` narrows `PhoneNumber` from `nvarchar(max)`
  to `nvarchar(20)`; this is safe only because no application code ever wrote that column before this
  slice, so every existing row is NULL. The safety is not self-evident from the file. Related:
  `PostalCode` is `nvarchar(6)` for a value that is always exactly 6 characters — zero headroom, so
  any format change is a migration.
- **Fix**: Add a line to the migration recording why the narrowing is safe; leave the lockout
  asymmetry as documented behaviour.
- **Decision**: FIXED — migration records why narrowing `PhoneNumber` is safe; the lockout asymmetry left as documented behaviour

## What was verified clean

- **Layering**: the only `Microsoft.EntityFrameworkCore` mention under `Domain`/`Application` is a
  comment. `ForgotPasswordAsync` reaches persistence through `IUnitOfWork`, as
  `MemberAdminEndpoints.ApproveAsync` does. `LoggingEmailSender` sits in `Infrastructure/Notifications/`
  beside `AcsEmailSender`.
- **Entity config**: all five mappings live in `ApplicationUserConfiguration`; `OnModelCreating`
  untouched. `Down` is complete and reversible.
- **Authorization on `/api/profile`**: the subject comes only from `GetUserAsync(principal)` — no id
  in route or body — so cross-user edits are structurally impossible, and `ProfileRequest` carries
  five fields, making `DisplayName`/`Email`/`Status`/roles unreachable by construction rather than by
  a filter.
- **Security stamp**: `RefreshSignInAsync` runs after `ChangePasswordAsync` and before the response,
  with `The_acting_session_survives_the_change` pinning it. `/reset-password` deliberately does not
  sign in.
- **Guardrails**: no global rate limiter, no editable DisplayName/Email, no HTML email, no backfill,
  no custom token lifespan, no admin surface for the new fields. All eight "What We're NOT Doing"
  items hold.
- **Injection, secrets, CORS, N+1**: nothing found.
