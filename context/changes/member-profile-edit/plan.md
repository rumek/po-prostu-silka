# Member Profile Edit — Contact Details, Password Change, and Password Reset

## Overview

Members currently register with three fields (email, password, display name) and have no way to see
or change anything about their account afterwards, nor to recover a forgotten password. This plan
adds five contact fields collected at registration, a member-facing profile screen where those
fields (and only those fields) are editable, an in-session password change, and a full
forgot/reset-password flow reachable before login.

## Current State Analysis

- The member **is** `ApplicationUser : IdentityUser` (`src/Domain/ApplicationUser.cs:17-31`) — one
  row in `AspNetUsers`, no separate `Member` entity. `IdentityUser` already supplies an unused
  `PhoneNumber` column.
- `RegisterRequest` is `(Email, Password, DisplayName)` (`src/Application/Auth/AuthEndpoints.cs:10`).
  Validation is hand-rolled with explicit null/whitespace guards and a closed vocabulary of failure
  reason codes returned as `Results.Json(new RegisterFailure(reason), statusCode: N)`.
- **No profile endpoint exists.** `ApplicationUser.cs:19` comments that `DisplayName` is "Editable by
  the member (S-09, FR-006)", but nothing was ever built.
- **No password endpoints exist.** `ChangePasswordAsync`, `GeneratePasswordResetTokenAsync`, and
  `ResetPasswordAsync` are never called anywhere in `src/`.
- `.AddDefaultTokenProviders()` **is** registered (`src/Program.cs:91`), so reset tokens work today
  with no additional wiring. F-02's archived plan omits this call; the code is authoritative.
- Email delivery is a working transactional outbox: `IEmailSender`
  (`src/Application/Notifications/IEmailSender.cs:8-12`) → `IOutboxEnqueuer`
  (`src/Application/Notifications/OutboxEnqueuer.cs:13-25`) → `OutboxDeliveryWorker`
  (`src/Infrastructure/Notifications/OutboxDeliveryWorker.cs:18-320`). `AccountApprovedNotification`
  is the reference consumer. `IEmailSender` is registered at `src/Program.cs:162`.
- `AcsEmailSender` degrades to `DeliveryResult.Permanent("acs_not_configured")` when unconfigured
  (`src/Infrastructure/Notifications/AcsEmailSender.cs:55-61`) — locally, mail goes nowhere.
- **No config key holds a public application URL**, and **no rate limiting exists anywhere** in the
  app.
- Angular: `login` and `register` are the only guard-free routes (`src/app/src/app/app.routes.ts:24-26`);
  `/` is guarded, so anonymous visitors land on `/login`. Reactive forms with per-control failure
  mapping (`register.ts:68-101`), hand-rolled SCSS, hardcoded Polish copy, no i18n library.
- Tests are integration tests against real SQL Server via Testcontainers
  (`tests/po-prostu-silka.Tests/IntegrationTestFixture.cs`), with `FakeChannels.cs` capturing
  outbound email so notification flows are assertable in CI without ACS.

## Desired End State

A member registering for the first time supplies phone number and address alongside their name,
email and password. Once signed in they can open a profile screen from the navigation, see their
name and email as read-only text, edit their contact details, and change their password without
being logged out. A member who has forgotten their password can, from the login screen, request a
reset link by email and set a new password by following that link.

Verified by: `dotnet test` green from the repo root; `npm test` and `npm run quality:check` green
from `src/app/`; and the manual walkthroughs listed per phase.

### Key Discoveries:

- `register.html:17` already labels `displayName` as **"Imię i nazwisko"** — the field the member
  sees as their first and last name is `DisplayName`. No schema split is needed to make first/last
  name non-editable; the requirement is satisfied by rendering `DisplayName` read-only.
- `IdentityUser.PhoneNumber` exists but is unconfigured, so EF gives it `nvarchar(max)`
  (`Migrations/20260831183430_AddIdentitySchema.cs`). Reusing it **requires** an explicit
  `HasMaxLength` in `ApplicationUserConfiguration`, or the new column is unbounded.
- `SecurityStampValidatorOptions.ValidationInterval` is 2 minutes (`src/Program.cs:133-134`). Both
  `ChangePasswordAsync` and `ResetPasswordAsync` rotate the stamp, so other sessions expire on their
  own — but the acting session dies too unless the handler calls `RefreshSignInAsync`, the pattern
  already used by `RefreshAsync` (`AuthEndpoints.cs:236-242`).
- `auth.models.ts:1-3` and `MemberAdminEndpoints.cs:11-12` both state in comments that the TS
  interfaces are a **contract mirror** of the C# records. Every DTO change is a two-sided edit.
- The registration endpoint deliberately discloses `email_taken` (`AuthEndpoints.cs:110-118`) while
  login deliberately does not disclose anything. Forgot-password sits on the login side of that line.
- `IOutboxEnqueuer.Enqueue` does not save; the caller owns the unit of work. An explicit transaction
  around it must go through `Database.CreateExecutionStrategy().ExecuteAsync(...)` because
  `EnableRetryOnFailure` is on (`OutboxEnqueuer.cs:16-22`).

## What We're NOT Doing

- **Not** splitting `DisplayName` into `FirstName`/`LastName`. That is a data migration over existing
  rows touching every projection and template, and nothing in this slice requires it.
- **Not** making `DisplayName` or `Email` editable by anyone — member or admin. Correcting a typo in
  a member's name has no path after this slice; that is accepted and recorded as a risk.
- **Not** surfacing the new contact fields on the admin member list or building an admin member-detail
  screen.
- **Not** adding email confirmation, two-factor auth, social login, or account-lockout tuning.
- **Not** customising the password-reset token lifespan — Identity's 24-hour default stands.
- **Not** adding a global rate limiter. Only `/api/auth/forgot-password` is throttled.
- **Not** backfilling contact details for existing accounts. Their columns stay NULL until the member
  fills them in.
- **Not** adding HTML email or a templating engine. Bodies stay plain-text string interpolation.

## Implementation Approach

Five phases, each independently deployable and each ending with a green test run plus a manual
walkthrough. The order follows the data: the schema and the registration contract first (so new
accounts are complete from day one), then the read/write surface for those fields, then the two
password flows — in-session change before pre-login reset, because change is a strict subset of the
work and proves the session-handling decision before the harder flow depends on it. Documents last,
so the PRD and roadmap describe what actually shipped.

Validation for the contact fields is written once, in `Application`, and consumed by both the
registration endpoint and the profile endpoint. That is what keeps "required at registration" and
"required when saving the profile" from drifting apart.

## Critical Implementation Details

**Timing & lifecycle.** `ChangePasswordAsync` and `ResetPasswordAsync` rotate the security stamp,
which invalidates every cookie for that user — including the caller's. In the change-password
handler, `RefreshSignInAsync` must run **after** the password change succeeds and **before** the
response is returned, or the member is silently logged out within two minutes of a successful
password change.

**State sequencing.** The forgot-password handler must produce a byte-identical response and take a
comparable amount of work whether or not the email matches an account. Concretely: do not return
early on "user not found" before the work an existing user's path performs, and do not let the
per-email throttle change the response — a throttled request still answers exactly as an accepted
one does. The F-02 implementation review flagged precisely this shape as a timing oracle on
`/login` (`context/archive/2026-08-31-auth-identity-foundation/reviews/impl-review.md:93-101`).

**Debug & observability.** The reset link is only reachable locally through the logging email sender
added in Phase 4. Manual verification of Phase 4 means reading the link out of the application log,
not out of an inbox.

---

## Phase 1: Contact details in the model and at registration

### Overview

Add the five contact fields to `ApplicationUser`, migrate the schema, extend the registration
contract on both sides, and enforce the Polish validation rules on the server.

### Changes Required:

#### 1. Domain entity

**File**: `src/Domain/ApplicationUser.cs`

**Intent**: Carry the four address fields on the member. The phone number reuses
`IdentityUser.PhoneNumber` and therefore needs no new property here — add an XML comment recording
that decision and that `PhoneNumberConfirmed` is deliberately unused.

**Contract**: Four new nullable properties — `Street`, `HouseNumber`, `PostalCode`, `City` — all
`string?`. Nullable because existing rows have no values; the API, not the schema, is what makes
them mandatory.

#### 2. Shared validation and normalisation

**File**: `src/Application/Members/ContactDetails.cs` (new)

**Intent**: One place that validates and normalises the five contact fields, so the registration
endpoint and the profile endpoint cannot drift. Returns either normalised values or the failure
reason code the endpoints hand back to the SPA.

**Contract**: A record carrying the five normalised values and a static parse/validate entry point
returning a discriminated success/failure result whose failure carries one reason code from:
`invalid_phone`, `invalid_street`, `invalid_house_number`, `invalid_postal_code`, `invalid_city`.
Rules: every field trimmed and required non-empty; postal code matches `^\d{2}-\d{3}$`; phone
normalised by stripping spaces, dashes and a leading `+48`, then required to be exactly 9 digits and
stored in normalised form; max lengths — phone 20, street 100, house number 20, postal code 6,
city 100.

**Adapted during implementation.** The "discriminated success/failure result" is spelled as
`static bool TryCreate(..., out ContactDetails? details, out string? reason)` — the Try-with-out
shape `YouTubeVideoId.TryParse` already establishes in this codebase, annotated with
`[NotNullWhen]` so nullable analysis stays warning-free. A bespoke result type would have been the
first of its kind here for no gain. Phone normalisation also strips `(` and `)` alongside spaces and
dashes, and drops a leading `48`/`+48` only when the digit string is eleven long — otherwise a real
nine-digit number beginning `48` would lose its first two digits.

#### 3. Registration contract

**File**: `src/Application/Auth/AuthEndpoints.cs`

**Intent**: Collect the contact details at registration and reject an incomplete or malformed
submission with the endpoint's existing failure vocabulary. The new fields are validated through
`ContactDetails` before `CreateAsync` is called, so a rejected registration creates nothing.

**Contract**: `RegisterRequest` gains `PhoneNumber`, `Street`, `HouseNumber`, `PostalCode`, `City`.
`RegisterFailure` reasons gain the five `invalid_*` codes from item 2. Validation order: existing
display-name/email/password guards first (unchanged), then contact details, then the duplicate-email
check. The `ApplicationUser` initialiser sets the normalised values.

#### 4. Persistence configuration and migration

**Files**: `src/Infrastructure/Persistence/Configurations/ApplicationUserConfiguration.cs`,
`src/Infrastructure/Persistence/Migrations/` (new migration)

**Intent**: Bound every new column explicitly, including the inherited `PhoneNumber` — without this
the reused Identity column stays `nvarchar(max)`.

**Contract**: `HasMaxLength` on all five properties matching the limits in item 2; none marked
`IsRequired`. Migration named `AddMemberContactDetails`, adding four nullable columns and altering
`PhoneNumber`'s length, with a `Down` that reverses both. No index — none of these fields is queried.

#### 5. SPA contract mirror

**File**: `src/app/src/app/core/auth/auth.models.ts`

**Intent**: Keep the TypeScript mirror of the API contract in step, as its own header comment demands.

**Contract**: `RegisterRequest` gains the five fields; `RegisterFailureReason` gains the five new
literals. `CurrentUser` is left alone in this phase — it changes in Phase 2.

**Adapted during implementation.** The five literals are declared once as an exported
`ContactFailureReason` union and folded into `RegisterFailureReason`, rather than inlined. Phase 2's
`ProfileFailure` answers with the same five codes from the same `ContactDetails` helper, so a shared
union is what keeps the two sides of that mirror from drifting.

#### 6. Registration form

**Files**: `src/app/src/app/features/auth/register/register.ts`,
`src/app/src/app/features/auth/register/register.html`

**Intent**: Collect the five fields with client-side validation matching the server's rules, and map
each new server reason code onto its own control, following the existing `applyFailure`/`reject`
pattern.

**Contract**: Five new required controls with the postal-code and phone patterns as `Validators`;
module-level constants for the two regexes, commented as mirroring `ContactDetails` the way
`MIN_PASSWORD_LENGTH` mirrors `Program.cs`. Template follows the existing `.field` + `<label>` +
`[attr.aria-invalid]` + `@if` error block shape, with `autocomplete` attributes (`tel`,
`address-line1`, `postal-code`, `address-level2`).

#### 7. Tests

**Files**: `tests/po-prostu-silka.Tests/RegisterEndpointTests.cs`,
`src/app/src/app/features/auth/register/register.spec.ts`

**Intent**: Prove the new fields are persisted on success and that each validation rule produces its
own reason code, and that the form surfaces each on the right control.

**Contract**: Backend cases — a complete registration stores normalised values; a malformed postal
code returns 400 `invalid_postal_code`; a phone with `+48` and spaces is normalised; a blank city
returns 400 `invalid_city`. Frontend cases — the request payload carries all eight fields; a server
`invalid_postal_code` renders on the postal-code control, not the banner.

### Success Criteria:

#### Automated Verification:

- Backend builds clean under `<Nullable>enable</Nullable>`: `dotnet build` from `src/`
- Migration applies and reverses: `dotnet ef database update` then `dotnet ef database update <previous>` with `--connection`
- Integration tests pass: `dotnet test` from the repo root
- Frontend unit tests pass: `npm test` from `src/app/`
- Formatting and lint clean: `npm run quality:check` from `src/app/`

#### Manual Verification:

- Registering with a complete form creates the account and lands on the pending screen
- A malformed postal code shows an error under the postal-code field, not in the banner
- A phone entered as `+48 123 456 789` is stored as nine digits
- Existing accounts still sign in with no contact details present

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase.

---

## Phase 2: Profile read and edit

### Overview

Surface the contact details in the session payload, add the endpoint that updates them, and build
the member-facing profile screen with name and email read-only.

### Changes Required:

#### 1. Session payload

**File**: `src/Application/Auth/AuthEndpoints.cs`

**Intent**: Return the contact details as part of the current user, so the SPA has them without a
second round trip and the profile form can be pre-filled from session state.

**Contract**: `CurrentUser` gains the five fields as nullable strings. `BuildCurrentUserAsync`
populates them from the entity. `GET /api/auth/me` and both sign-in paths return the extended shape
unchanged in every other respect.

**Adapted during implementation.** `BuildCurrentUserAsync` moved from `private` to `internal` so
`ProfileEndpoints` returns the session payload through the same projection instead of writing a
second copy of it — a duplicate is exactly how the two would drift the next time `CurrentUser` grows
a field. `GetCurrentUser` keeps its own inline projection, because it reads roles from the cookie's
claims rather than querying them.

#### 2. Profile endpoint

**File**: `src/Application/Members/ProfileEndpoints.cs` (new)

**Intent**: Let a signed-in member update their own contact details, and nothing else. Placed in
`Members` rather than `Auth` because it is member data, not credentials or session.

**Contract**: `PUT /api/profile` mapped via a `MapProfileEndpoints` extension registered in
`Program.cs` beside `MapAuthEndpoints`. Group carries bare `RequireAuthorization()` — **not** the
`ActiveMember` policy, matching `/me` and `/refresh`, so a member awaiting approval can complete
their profile. Request record carries only the five contact fields; the handler resolves the user
from `UserManager.GetUserAsync(principal)`, validates through `ContactDetails`, saves via
`UpdateAsync`, and returns the same `CurrentUser` shape the SPA already knows. Failure record mirrors
registration's shape with the same five `invalid_*` reason codes. Any attempt to send a display name
or email is ignored by virtue of not being on the request record.

#### 3. SPA contract and service

**Files**: `src/app/src/app/core/auth/auth.models.ts`,
`src/app/src/app/core/auth/auth.service.ts`

**Intent**: Mirror the extended `CurrentUser`, add the update call, and refresh the session signal
from the response so the whole app sees the new values immediately.

**Contract**: `CurrentUser` gains the five optional fields (optional as well as nullable, so the six
existing spec fixtures that build a `CurrentUser` literal stay valid and every consumer checks
truthiness rather than `=== null`); new `ProfileRequest` and `ProfileFailure` types. `AuthService` gains `updateProfile(request)` calling `PUT /api/profile`, setting the user
signal from the response, and — like every other method here — not catching, so the component maps
the failure onto controls.

#### 4. Profile screen

**Files**: `src/app/src/app/features/profile/profile.ts`, `profile.html`, `profile.scss` (new)

**Intent**: One screen showing name and email as read-only text and the contact details as an
editable form, with a prompt for members whose details are still empty.

**Contract**: Lazy route `profile` in `app.routes.ts` guarded by `[authGuard]` only — same reasoning
as the endpoint. Form mirrors the registration validators exactly, pre-filled from `auth.user()`. A
`.notice` block appears when any contact field is empty, telling the member to complete their
details. Name and email render as plain text with a `.hint` explaining they are managed by the gym.
Success sets a transient confirmation message; failures map per control via the same `reject` helper
pattern.

#### 5. Navigation entry

**File**: `src/app/src/app/app.html`

**Intent**: Give the profile screen a way in — there is no account link in the nav today.

**Contract**: A link to `/profile` in the authenticated section of the nav, beside the existing
logout button.

**Adapted during implementation.** It is the only member link in the header NOT gated on
`isActive()` — the enclosing `isAuthenticated()` block is the whole condition. Narrowing it to match
its neighbours would hide the one screen a member awaiting approval needs, which is the same reason
the route and the API group both take `authGuard` / bare `RequireAuthorization()`. `app.spec.ts`
gained two tests pinning that.

#### 6. Tests

**Files**: `tests/po-prostu-silka.Tests/ProfileEndpointTests.cs` (new),
`src/app/src/app/features/profile/profile.spec.ts` (new)

**Intent**: Prove the endpoint updates only what it should, refuses anonymous callers, and validates
identically to registration; and that the screen renders name and email as non-editable.

**Contract**: Backend cases — an authenticated update persists normalised values and returns the
extended `CurrentUser`; an anonymous request gets 401; a pending member can update; a malformed
postal code returns 400 with the matching reason. Frontend cases — the form pre-fills from session
state; no input element exists for display name or email; a server validation failure lands on the
right control.

### Success Criteria:

#### Automated Verification:

- Backend builds clean: `dotnet build` from `src/`
- Integration tests pass: `dotnet test` from the repo root
- Frontend unit tests pass: `npm test` from `src/app/`
- Formatting and lint clean: `npm run quality:check` from `src/app/`

#### Manual Verification:

- The profile screen is reachable from the navigation and pre-fills current values
- Name and email appear as text with no editable input
- Saving valid changes persists them across a page reload
- An account registered before Phase 1 shows the "complete your details" prompt and can save

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase.

---

## Phase 3: Change password while signed in

### Overview

Add the in-session password change and its section on the profile screen, keeping the acting session
alive while every other session expires.

### Changes Required:

#### 1. Change-password endpoint

**File**: `src/Application/Auth/AuthEndpoints.cs`

**Intent**: Let a signed-in member replace their password by proving the current one, without being
logged out of the session they are using.

**Contract**: `POST /api/auth/change-password` on the existing `/api/auth` group with bare
`RequireAuthorization()`. Request record carries current and new password. Handler resolves the user
from the principal, calls `UserManager.ChangePasswordAsync`, and on success calls
`SignInManager.RefreshSignInAsync` before responding — see Critical Implementation Details. Failure
record reuses the reason-code convention with `invalid_current_password` (400) and
`invalid_new_password` (400, covering Identity's password-policy codes); Identity's raw error text is
never forwarded, matching the mapping already in `RegisterAsync`.

**Adapted during implementation.** The handler answers **204 No Content** rather than a body — the
password is not part of any state the SPA holds, and `RefreshSignInAsync` re-issues the cookie
server-side, so there is nothing for a response body to carry. It also guards a null current or new
password before calling `ChangePasswordAsync`, the same compile-time-vs-runtime gap `LoginAsync` and
`RegisterAsync` already guard: without it a JSON null throws and an authenticated caller gets a 500.

#### 2. SPA contract and service

**Files**: `src/app/src/app/core/auth/auth.models.ts`,
`src/app/src/app/core/auth/auth.service.ts`

**Intent**: Mirror the new contract and expose the call.

**Contract**: `ChangePasswordRequest`, `ChangePasswordFailure` and its reason union;
`AuthService.changePassword(request)`.

#### 3. Password section on the profile screen

**Files**: `src/app/src/app/features/profile/profile.ts`, `profile.html`

**Intent**: A second, independent form on the profile screen so a member changes their password
where they manage everything else about their account.

**Contract**: A separate `FormGroup` — independent submit, submitting and error state from the
contact-details form, so one failing does not disable the other. Controls: current password, new
password (`minLength` mirroring `MIN_PASSWORD_LENGTH`), and a confirmation control with a
group-level validator requiring the two to match. The mismatch error stays on the GROUP and the
template reads it through a `confirmationMismatch` getter — put on the confirmation control instead,
it is wiped by that control's own validators the next time either field is edited, and the message
flickers. `autocomplete="current-password"` and
`"new-password"` respectively. On success the form resets and shows a confirmation.

#### 4. Tests

**Files**: `tests/po-prostu-silka.Tests/PasswordEndpointTests.cs` (new),
`src/app/src/app/features/profile/profile.spec.ts`

**Intent**: Prove the password actually changes, the wrong current password is refused, the policy is
enforced, and the caller keeps their session.

**Contract**: Backend cases — a valid change lets the member log in with the new password and not the
old; a wrong current password returns 400 `invalid_current_password`; a 5-character new password
returns 400 `invalid_new_password`; after a successful change the same cookie still authenticates a
subsequent `GET /api/auth/me`. Frontend cases — mismatched confirmation blocks submit without an HTTP
call; a server `invalid_current_password` renders on the current-password control.

### Success Criteria:

#### Automated Verification:

- Backend builds clean: `dotnet build` from `src/`
- Integration tests pass: `dotnet test` from the repo root
- Frontend unit tests pass: `npm test` from `src/app/`
- Formatting and lint clean: `npm run quality:check` from `src/app/`

#### Manual Verification:

- Changing the password succeeds and the session stays signed in
- Signing out and back in works with the new password and fails with the old one
- A second browser session signed in as the same member is signed out within about two minutes
- Mismatched confirmation is refused in the form before any request is sent

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase.

---

## Phase 4: Forgot and reset password before login

### Overview

The full pre-login recovery flow: configuration for the link's absolute address, a development email
sender so the flow is verifiable locally, the reset notification on the existing outbox, a rate
limiter scoped to the request endpoint, the two API endpoints, and the two public screens.

### Changes Required:

#### 1. Application base URL configuration

**Files**: `src/appsettings.json`, `src/Program.cs`, `src/Infrastructure/Notifications/` (options class)

**Intent**: Give the reset email a deterministic absolute address to build the link from. The outbox
renders at enqueue time, and a worker retry has no HTTP request to derive a host from, so this cannot
come from the request.

**Contract**: New `App` section with a `BaseUrl` key, documented in `appsettings.json` in the same
"documented here, never set here" style as `Acs` and `AdminSeed`, with the production setting spelled
out as `App__BaseUrl`. Bound to an options record and registered alongside `AcsOptions`
(`src/Program.cs:145`). `appsettings.Development.json` sets the local dev-server origin. If the value
is empty the forgot-password handler logs an error and still returns the standard response — a
misconfiguration must not become a disclosure.

#### 2. Development email sender

**File**: `src/Infrastructure/Notifications/LoggingEmailSender.cs` (new), `src/Program.cs`

**Intent**: Without this the reset link is unreachable on a developer machine, because
`AcsEmailSender` reports a permanent failure when ACS is unconfigured.

**Contract**: An `IEmailSender` that logs recipient, subject and body at Information and returns
success. Registered in place of `AcsEmailSender` only when the environment is Development **and** ACS
is unconfigured — the existing `acs` local at `src/Program.cs:152-162` already computes that
condition. Production behaviour is untouched.

#### 3. Reset notification

**File**: `src/Application/Notifications/PasswordResetNotification.cs` (new)

**Intent**: Render the reset email and put it on the outbox, following `AccountApprovedNotification`
exactly — rendered at enqueue, plain text, Polish copy.

**Contract**: An interface plus implementation taking the member and the already-generated token,
composing the link from the configured base URL as
`{BaseUrl}/reset-password?email={urlencoded}&token={urlencoded}`, and enqueuing a single email row.
**Email channel only** — a password reset must reach the address being recovered, not a push
subscription on a device that may not belong to the person asking. The token is URL-encoded as a
query-string value; it must never be placed in a path segment.

#### 4. Per-email throttle

**File**: `src/Application/Auth/PasswordResetThrottle.cs` (new)

**Intent**: Stop one address being mailed repeatedly, which the IP limiter alone does not prevent
across changing addresses.

**Contract**: A singleton abstraction with a "may I send to this normalised email now" check backed
by an in-memory sliding window. Injected so tests can substitute it. A throttled request **still
returns the standard success response** — the throttle changes what is sent, never what is answered.
Document in the class that in-memory state is per-instance and therefore correct only while the app
runs single-instance, which matches the Basic-tier App Service it deploys to.

#### 5. Rate limiter

**File**: `src/Program.cs`

**Intent**: Cap request volume per client on the one anonymous endpoint that sends mail on demand.

**Contract**: `AddRateLimiter` with a single named fixed-window policy partitioned on client IP,
applied via `RequireRateLimiting` to the forgot-password endpoint only, plus `UseRateLimiter` in the
pipeline. Rejected requests answer 429. No other endpoint gains a limit.

#### 6. Forgot and reset endpoints

**File**: `src/Application/Auth/AuthEndpoints.cs`

**Intent**: Issue a reset token by email, and consume it to set a new password — without ever
revealing whether an account exists.

**Contract**: `POST /api/auth/forgot-password`, `AllowAnonymous`, rate-limited. Takes an email;
**always** returns 200 with the same empty-bodied result regardless of whether the account exists, is
Pending, or is Blocked. When a user is found and the throttle allows it, generates a token via
`UserManager.GeneratePasswordResetTokenAsync`, calls the notification, and commits through the
existing unit of work so the outbox row lands. See Critical Implementation Details for the
equal-work requirement.

`POST /api/auth/reset-password`, `AllowAnonymous`. Takes email, token and new password. Looks the
user up, calls `UserManager.ResetPasswordAsync`, and returns a failure record with
`invalid_token` (400, covering an unknown email, a bad token and an expired one — deliberately one
code so the endpoint discloses nothing) or `invalid_new_password` (400). Does **not** sign the member
in on success; they are sent to the login screen, which is also the only way an already-signed-in
session would notice the stamp rotation.

#### 7. SPA contract, service, routes and screens

**Files**: `src/app/src/app/core/auth/auth.models.ts`, `auth.service.ts`, `app.routes.ts`,
`features/auth/forgot-password/` (new), `features/auth/reset-password/` (new),
`features/auth/login/login.html`

**Intent**: Two public screens and the link that makes them discoverable.

**Contract**: New request/failure types and `forgotPassword` / `resetPassword` service methods. Two
lazy, guard-free routes registered beside `login` and `register`. The forgot screen submits an email
and then replaces the form with a neutral confirmation that does not confirm the address exists. The
reset screen reads `email` and `token` from the query string, presents new-password and confirmation
controls with the same validators as Phase 3, and on success navigates to `/login` with a
confirmation; a missing or rejected token shows a message and a link back to the forgot screen. The
login template gains `<a routerLink="/forgot-password">Nie pamiętasz hasła?</a>` in the existing
`.panel-footer`.

#### 8. Tests

**Files**: `tests/po-prostu-silka.Tests/PasswordEndpointTests.cs`,
`src/app/src/app/features/auth/forgot-password/forgot-password.spec.ts` (new),
`src/app/src/app/features/auth/reset-password/reset-password.spec.ts` (new)

**Intent**: Prove the round trip works, the non-disclosure holds, and the token is single-use.

**Contract**: Backend cases, using `FakeChannels` to capture the mail — a known address enqueues
exactly one email row whose body contains a link with the configured base URL; an unknown address
returns the identical response and enqueues nothing; a Blocked account is treated the same as an
Active one; the token from the captured mail resets the password; the same token reused a second time
returns `invalid_token`; a garbage token returns `invalid_token`. Frontend cases — the forgot screen
shows the neutral confirmation after submitting; the reset screen reads both query parameters and
sends them; mismatched confirmation blocks submit.

### Success Criteria:

#### Automated Verification:

- Backend builds clean: `dotnet build` from `src/`
- Integration tests pass, including the outbox assertions: `dotnet test` from the repo root
- Frontend unit tests pass: `npm test` from `src/app/`
- Formatting and lint clean: `npm run quality:check` from `src/app/`

#### Manual Verification:

- The login screen shows the forgot-password link and it reaches the form
- Submitting a registered address writes the reset link to the application log
- Following that link sets a new password and the member can sign in with it
- Following the same link a second time is refused
- Submitting an unregistered address produces exactly the same on-screen result as a registered one
- Repeated rapid submissions from one client are refused with 429

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before proceeding
to the next phase.

---

## Phase 5: Documents

### Overview

Bring the PRD, the roadmap and the lessons register in line with what shipped, so no future review
re-flags this scope as undocumented.

### Changes Required:

#### 1. PRD

**File**: `context/foundation/prd.md`

**Intent**: FR-006 currently promises display-name editing, which this slice deliberately does not
deliver, and says nothing about contact details or password recovery.

**Contract**: FR-006 rewritten to "Member can edit their contact details and change their password",
with a resolution note recording that display-name editing was dropped because the gym owns the
member's name. Two requirements added: contact details (phone and address) collected at registration
and editable by the member; and password reset by emailed link before login. Each carries the same
`Socrates:` / `Resolution:` annotation style as its neighbours.

#### 2. Roadmap

**File**: `context/foundation/roadmap.md`

**Intent**: S-13's outcome line and its at-a-glance row describe a smaller slice than what was built.

**Contract**: S-13's `Outcome` and the at-a-glance summary updated to name contact details, password
change and password reset; `PRD refs` extended with the new requirement IDs; the `Risk` line —
currently "smallest slice in the milestone" — corrected. `Status` is left to `/10x-implement` and
`/10x-archive`; frontmatter `updated` bumped.

#### 3. Lessons

**File**: `context/foundation/lessons.md`

**Intent**: This slice's research produced a concrete instance of the existing lesson about plan
drift — F-02's plan omits `AddDefaultTokenProviders()` while the shipped code has it, which almost
produced a false "prerequisite missing" finding.

**Contract**: A new entry recording the rule in the register's established shape (Context / Problem /
Rule / Applies to): when researching a prerequisite, verify it against the code before trusting an
archived plan's contract, because plans are corrected less reliably than code.

### Success Criteria:

#### Automated Verification:

- No PRD requirement in `prd.md` still promises member-editable display name: `grep -n "display name" context/foundation/prd.md`
- The roadmap's S-13 block mentions password reset: `grep -n -A6 "S-13" context/foundation/roadmap.md`

#### Manual Verification:

- Reading FR-006 and its neighbours describes what the app actually does
- The S-13 roadmap entry would not mislead someone picking the slice up cold

**Implementation Note**: This phase changes documents only; the manual check is a read-through.

---

## Testing Strategy

### Unit Tests:

- `ContactDetails` validation rules in isolation are covered indirectly through the endpoint tests;
  no separate unit-test project exists and none is introduced.
- Angular component specs follow `register.spec.ts`: DOM-driven fill and submit helpers,
  `HttpTestingController` for request and response assertions, and `controller.verify()` in
  `afterEach`.

### Integration Tests:

- All backend tests run through `IntegrationTestFixture` against real SQL Server via Testcontainers,
  so the migration and the column constraints are genuinely exercised.
- Email assertions go through `FakeChannels`, which captures outbound mail without ACS credentials.
- Session behaviour after a password change is asserted by reusing the same authenticated client for
  a follow-up request.

### Manual Testing Steps:

1. Register a new account with complete contact details; confirm the pending screen appears.
2. Sign in, open the profile screen from the navigation, confirm name and email are not editable.
3. Change a contact field, reload, confirm it persisted.
4. Change the password; confirm the session survives and the old password no longer works.
5. Sign out. From the login screen follow the forgot-password link, submit the registered address.
6. Read the reset link from the application log, follow it, set a new password, sign in with it.
7. Follow the same link again; confirm it is refused.
8. Submit an unregistered address on the forgot screen; confirm the result is indistinguishable.

## Performance Considerations

The extended `CurrentUser` adds five short strings to a payload the SPA already fetches once per
session load — negligible. The forgot-password endpoint's equal-work requirement means an unknown
address costs roughly what a known one does; that is deliberate, and the rate limiter bounds the
total. The in-memory throttle holds one small entry per address in a bounded window.

## Migration Notes

The migration is purely additive: four nullable columns plus a length change on an existing, entirely
unused `PhoneNumber` column. Its `Down` reverses both, so the rollback constraint in `AGENTS.md` is
satisfied without a deferred-destructive step. Existing rows keep NULL contact details; those members
are prompted on the profile screen and can save at any time. No backfill runs.

## References

- Research: `context/changes/member-profile-edit/research.md`
- Registration endpoint and its failure vocabulary: `src/Application/Auth/AuthEndpoints.cs:123-213`
- Transactional email to copy: `src/Application/Notifications/AccountApprovedNotification.cs:19-58`
- Entity configuration style: `src/Infrastructure/Persistence/Configurations/ApplicationUserConfiguration.cs:12-32`
- Form and failure-mapping pattern: `src/app/src/app/features/auth/register/register.ts:33-101`
- Component spec pattern: `src/app/src/app/features/auth/register/register.spec.ts:27-136`
- Login non-disclosure precedent: `context/archive/2026-08-31-auth-identity-foundation/reviews/impl-review.md:93-101`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Contact details in the model and at registration

#### Automated

- [x] 1.1 Backend builds clean under `<Nullable>enable</Nullable>` — 1b6f5b0
- [x] 1.2 Migration applies and reverses — 1b6f5b0
- [x] 1.3 Integration tests pass — 1b6f5b0
- [x] 1.4 Frontend unit tests pass — 1b6f5b0
- [x] 1.5 Formatting and lint clean — 1b6f5b0

#### Manual

- [x] 1.6 Registering with a complete form creates the account and lands on the pending screen — 1b6f5b0
- [x] 1.7 A malformed postal code shows an error under the postal-code field, not in the banner — 1b6f5b0
- [x] 1.8 A phone entered as `+48 123 456 789` is stored as nine digits — 1b6f5b0
- [x] 1.9 Existing accounts still sign in with no contact details present — 1b6f5b0

### Phase 2: Profile read and edit

#### Automated

- [x] 2.1 Backend builds clean — 11a3c0b
- [x] 2.2 Integration tests pass — 11a3c0b
- [x] 2.3 Frontend unit tests pass — 11a3c0b
- [x] 2.4 Formatting and lint clean — 11a3c0b

#### Manual

- [x] 2.5 The profile screen is reachable from the navigation and pre-fills current values — 11a3c0b
- [x] 2.6 Name and email appear as text with no editable input — 11a3c0b
- [x] 2.7 Saving valid changes persists them across a page reload — 11a3c0b
- [x] 2.8 An account registered before Phase 1 shows the prompt and can save — 11a3c0b

### Phase 3: Change password while signed in

#### Automated

- [x] 3.1 Backend builds clean — fbdd612
- [x] 3.2 Integration tests pass — fbdd612
- [x] 3.3 Frontend unit tests pass — fbdd612
- [x] 3.4 Formatting and lint clean — fbdd612

#### Manual

- [ ] 3.5 Changing the password succeeds and the session stays signed in
- [ ] 3.6 Signing out and back in works with the new password and fails with the old one
- [ ] 3.7 A second session as the same member is signed out within about two minutes
- [ ] 3.8 Mismatched confirmation is refused before any request is sent

### Phase 4: Forgot and reset password before login

#### Automated

- [ ] 4.1 Backend builds clean
- [ ] 4.2 Integration tests pass, including the outbox assertions
- [ ] 4.3 Frontend unit tests pass
- [ ] 4.4 Formatting and lint clean

#### Manual

- [ ] 4.5 The login screen shows the forgot-password link and it reaches the form
- [ ] 4.6 Submitting a registered address writes the reset link to the application log
- [ ] 4.7 Following that link sets a new password and the member can sign in with it
- [ ] 4.8 Following the same link a second time is refused
- [ ] 4.9 An unregistered address produces exactly the same on-screen result as a registered one
- [ ] 4.10 Repeated rapid submissions from one client are refused with 429

### Phase 5: Documents

#### Automated

- [ ] 5.1 No PRD requirement still promises member-editable display name
- [ ] 5.2 The roadmap's S-13 block mentions password reset

#### Manual

- [ ] 5.3 FR-006 and its neighbours describe what the app actually does
- [ ] 5.4 The S-13 roadmap entry would not mislead someone picking the slice up cold
