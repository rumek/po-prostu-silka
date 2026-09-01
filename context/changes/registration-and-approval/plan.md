# Registration and approval (S-01)

- **Change ID**: `registration-and-approval`
- **Roadmap item**: S-01 — Member registers, and admin approves the account
- **PRD refs**: FR-001, FR-002, FR-003, Access Control (account lifecycle, pending state), Business Logic (approval gates everything)
- **Prerequisites**: F-01 (persistence), F-02 (auth/identity), F-03 (notification delivery) — all archived
- **Created**: 2026-09-01

---

## Overview

This is the first slice that ships a screen. Everything before it was foundation: a database, an
authentication surface with no UI, and a notification transport with no caller. S-01 turns those
into a product a member can actually use — they register, wait, get approved, and get in.

Three things make it larger than "two forms and a list":

1. **It resolves a contradiction F-02 shipped.** The PRD's Access Control section and the roadmap's
   own S-01 outcome both say a pending user *can log in* and sees only an awaiting-approval screen.
   `src/Application/Auth/AuthEndpoints.cs:64` refuses login for any non-Active status. F-02's code is
   the outlier against two upstream documents, not a deliberate deferral — and three of its own
   accommodations for authenticated-but-pending members (`/me`'s bare `RequireAuthorization()`, the
   whole `/api/push` group, the `ActiveMember` policy) are unreachable until this is fixed.

2. **It gives the authorization policies their first production consumer.** `ActiveMember` and
   `Admin` have existed since F-02 and are exercised only by the `IsEnvironment("Testing")` probes at
   `src/Program.cs:217,220`. The admin endpoints here are the first real endpoints behind them.

3. **It gives F-03's outbox its first production caller.** `IAccountApprovedNotification` was built,
   DI-registered (`src/Program.cs:153`) and deployed with an explicit `INTEGRATION POINT FOR S-01`
   comment and no caller. The roadmap's risk note — written before F-03 landed — says no notification
   is required for this slice. That assumption is stale: the transport is ready, and wiring it is one
   call that closes a documented open risk and gives S-05 a reference implementation.

The frontend has no visual language at all: `app.html` is the 353-line Angular CLI scaffold,
`app.scss` is empty, `styles.scss` is a one-line stub, and no CSS framework is installed. This slice
lays the typographic and colour foundation every later screen inherits.

---

## Current state

### Backend

| File | Relevant state |
| --- | --- |
| `src/Application/Auth/AuthEndpoints.cs` | `/login`, `/logout`, `/me`. Registration deliberately absent (documented, lines 22–23). Login refuses any non-Active status at line 64. |
| `src/Domain/AccountStatus.cs` | `Pending = 0, Active = 1, Blocked = 2` |
| `src/Domain/ApplicationRoles.cs` | `User`, `Admin`, `All` |
| `src/Infrastructure/Authorization/AuthorizationPolicies.cs` | `ActiveMember`, `Admin` — zero production consumers |
| `src/Application/Notifications/AccountApprovedNotification.cs` | Renders the Polish approval email plus one push row per device. `Enqueue` does **not** save — the caller owns the unit of work. |
| `src/Program.cs:201` | `app.MapPushEndpoints();` — new endpoint groups go after this, before `MapFallbackToFile` (line 224) |
| `src/Infrastructure/Persistence/Configurations/ApplicationUserConfiguration.cs:30` | `HasIndex(x => x.Status)` already exists — the pending list's query is covered |
| Identity options (`src/Program.cs`) | Password min length 8, no composition rules, `RequireUniqueEmail = true` |

### Frontend

| File | Relevant state |
| --- | --- |
| `src/app/src/app/app.html` | Unmodified 353-line CLI scaffold |
| `src/app/src/app/app.scss` | 0 bytes |
| `src/app/src/styles.scss` | One comment line |
| `src/app/src/app/app.spec.ts:21` | Asserts `<h1>` contains `"Hello, app"` — **breaks** when the scaffold goes |
| `src/app/src/app/app.routes.ts` | `/login` and `/` on placeholder components, both marked for S-01 replacement |
| `src/app/src/app/core/auth/auth.guard.ts:10-12` | Explicitly defers status routing to S-01 |
| `src/app/src/app/core/auth/auth.service.ts` | Signals `user`, `isAuthenticated`, `isActive`, `isAdmin`, `sessionResolved`. `login()` does not catch — errors reach the caller. |
| `src/app/package.json` | `@angular/forms` is a dependency, imported nowhere. No CSS framework. |
| `src/app/angular.json` | `outputMode: "static"`, all routes `RenderMode.Prerender` |

### Tests

**Three existing tests are affected by D1**, two of which must change:

- `AuthEndpointTests.cs:38` (`Pending_user_is_refused_with_a_distinguishing_reason`) asserts the
  behaviour this slice inverts. **Rewrite.**
- `AuthEndpointTests.cs:52` (`Blocked_user_is_refused_with_a_distinguishing_reason`) stays correct
  and untouched (D2).
- `PushEndpointTests.cs:61-72` (`Pending_member_can_also_subscribe`) is named for the behaviour D1
  enables but currently asserts `Unauthorized`, with the comment *"A pending member cannot log in at
  all… Assert the current contract rather than a hoped-for one."* **Complete it** — this is a
  placeholder written for this slice, not collateral damage. It is also a fourth F-02/F-03
  accommodation for authenticated-pending members, alongside the three the Overview lists.

`IntegrationTestFixture` seeds `ActiveAdminEmail`, `ActiveMemberEmail`, `PendingMemberEmail`,
`BlockedMemberEmail`, and exposes `CreateClient()` (BaseAddress `https://localhost`),
`CreateAuthenticatedClientAsync(email)`, `CreateUserAsync(email, status, role)`.

---

## Decisions

Settled during planning. Each records the alternative rejected, so a later reader does not
re-litigate it.

### D1 — A pending member gets a session

Login issues a cookie for `Pending`. The `ActiveMember` policy keeps them out of content; the SPA
routes them to the awaiting-approval screen.

*Why*: the PRD and the roadmap both specify it, and F-02 already built three accommodations that
assume it. *Cost*: changes shipped login behaviour and rewrites one existing test.

### D2 — Blocked stays refused at login

Only `Pending` gains a session. `Blocked` keeps its `401 blocked`.

*Why*: Blocked exists to deny access; handing that person a valid 30-day cookie inverts its purpose,
and the security-stamp refresh interval means a mid-session block takes up to 30 minutes to bite.
There is no "blocked screen" for them to reach, unlike Pending's. *Rejected*: uniform
"login authenticates, policy authorises" — cleaner as a rule, worse as a security posture.

### D3 — Registration discloses that an email is taken

`POST /api/auth/register` returns `409 { "reason": "email_taken" }`.

*Why*: silence strands a real member who forgot they signed up — they retry, see success, and wait
forever for the approval of an account that was never created. With no email confirmation in scope,
nothing else would ever tell them. For a single gym, "this address belongs to a member here" is close
to worthless to an attacker. *Acknowledged*: this is an enumeration oracle, and it is asymmetric with
login's deliberate `invalid_credentials` non-disclosure. **Both endpoints must carry a comment saying
so**, or a future reader will "fix" one to match the other.

### D4 — No abuse protection beyond the approval gate

No rate limiting, no CAPTCHA, no email confirmation.

*Why*: FR-001's own Socratic resolution names the approval gate as the mitigation. *Accepted risk*:
junk registrations accumulate as Pending rows forever. Nothing prunes them, and S-02's member list
will surface them. Recorded in Open Risks.

### D5 — Admin surface is the pending list plus approve

No search, no filter, no status badges, no block/unblock, no reject.

*Why*: exactly S-01's stated outcome. FR-003 explicitly dropped reject from the MVP. S-02 owns the
full member list and is `blocked` on PRD Open Question 1 ("what happens to a blocked member's
existing bookings and assigned plan?") — building the list now would guess at semantics that question
has not answered.

### D6 — Approve calls `NotifyAsync`

In the same unit of work as the status flip.

*Why*: closes F-03's "no production caller" risk and makes the transport live. *Constraint*:
`EnableRetryOnFailure` is on, so any explicit transaction must go through
`Database.CreateExecutionStrategy().ExecuteAsync(...)`. That failure throws at runtime, not compile
time.

### D7 — Hand-rolled SCSS with design tokens

No Tailwind, no Angular Material.

Typography: **Cormorant Garamond** (headings), **Plus Jakarta Sans** (body and UI). Palette:

```scss
$ground:        #f7f3ef;  // page background
$section-warm:  #ece3dc;  // section accent
$section-cool:  #edeae5;  // section accent
$ink:           #272321;  // body text, links
$accent:        #654b45;  // accent text, link hover
```

*Why*: this is a specific editorial identity. Material would have to be overridden to erase Material;
Tailwind would still need a config declaring exactly these six colours and two families, buying only
a spacing scale. **Fonts are self-hosted** under `src/app/public/fonts/`, not fetched from
`fonts.googleapis.com` — F-02 deliberately made the SPA same-origin with the API, and a third-party
font request on first paint would reintroduce a cross-origin dependency into that.

### D8 — Reactive forms

`FormBuilder`, typed `FormGroup`, validators in TypeScript. This decides the idiom for the project.

*Why*: server-returned field errors (D3's `email_taken`) map cleanly onto control errors; validation
is testable without a DOM; later slices have harder forms than these.

### D9 — Polish UI copy, hardcoded, no i18n

*Why*: a single Polish gym, and F-03's approval email already committed to Polish. *Rejected*:
Angular i18n set up now — cost paid across eleven more slices against a second locale that is not on
the roadmap.

### D10 — Route paths stay English; only copy is Polish

`/login`, `/register`, `/pending`, `/admin/approvals`.

*Why*: `/login` already exists and `authGuard` redirects to it. Polish paths would mean editing the
guard and its spec for no user benefit, and every identifier in the codebase is already English.

### D11 — The awaiting screen refreshes manually

A "Sprawdź ponownie" button calls `POST /api/auth/refresh` and routes onward if the member is now
Active. The approval email is the real signal.

**The button must call `/refresh`, not `/me`.** `/me` reads the database and would report Active
while the cookie's `account_status` claim still said Pending — routing the member into an app that
then refuses every request for up to 30 minutes. `/refresh` re-mints the claim. This is the same
staleness mechanism D2 invokes for revocation, running in the opposite direction.

*Why*: approval is a human action that happens hours later. Polling every abandoned pending tab
forever is unbounded load on a 5-DTU database — and by D4, junk accounts sit Pending indefinitely.
*Rejected*: 30-second polling; push-wakes-the-tab (needs notification permission granted *before*
approval, so it needs a fallback anyway).

### D12 — Status routing lives in guards beside `authGuard`

`authGuard` stays authentication-only, untouched. A new `activeMemberGuard` redirects Pending to
`/pending`; a new `adminGuard` covers the approvals route.

*Why*: mirrors the backend, where authentication and authorization are also separate. A future route
that should admit a Pending member (the awaiting screen itself) simply does not list the guard.

---

## What we're NOT doing

- **No reject action** — FR-003 dropped it from the MVP.
- **No block/unblock** — S-02.
- **No member list beyond pending** — S-02, and it is blocked on PRD Open Question 1.
- **No password reset, no email confirmation, no "remember me" toggle** — not in FR-001/002.
- **No rate limiting or CAPTCHA** — D4.
- **No i18n machinery** — D9.
- **No CSS framework** — D7.
- **No in-app notification center** — explicitly rejected in the PRD.
- **No changes to `authGuard`, the auth interceptor, or the push service** — F-02/F-03 artifacts this
  slice consumes unchanged.
- **No `ngsw-config.json` asset groups** — `assetGroups` is `[]` today; expanding service-worker
  caching is not this slice's problem.
- **No admin UI for anything but approval** — no settings, no profile editing.

---

## Phase 1: Registration, the pending session, and approval

### Overview

The whole API surface for this slice, backed by integration tests, before any screen is built against
it. This phase inverts the pending-login rule, adds registration, adds the two admin endpoints, and
wires the approval notification.

### Changes required

**`src/Application/Auth/AuthEndpoints.cs`**

- Add `RegisterRequest(string Email, string Password, string DisplayName)` and
  `RegisterFailure(string Reason)`.
- Map `group.MapPost("/register", RegisterAsync).AllowAnonymous();`
- `RegisterAsync`:
  - Reject blank/whitespace `DisplayName` with `400 { "reason": "invalid_display_name" }`. Trim it
    before storing.
  - `FindByEmailAsync` → if found, `409 { "reason": "email_taken" }` (D3).
  - Create `ApplicationUser { DisplayName, Status = AccountStatus.Pending, CreatedAt = now }` via
    `UserManager.CreateAsync`. On Identity failure (weak password, malformed email) return
    `400 { "reason": "invalid_password" | "invalid_email" }` derived from the returned error codes —
    do not echo Identity's raw error text to the client.

    **Adapted during implementation.** A third reason, `invalid_registration`, was added as the
    fallback branch. Identity's error codes are an open set (`DuplicateUserName`,
    `InvalidUserName`, custom validators added later), so mapping only two codes would mean any
    unrecognised failure silently reported itself as `invalid_email` — a wrong message on a screen
    the member cannot get past. Phase 2's `RegisterFailureReason` union must carry it.
  - `AddToRoleAsync(user, ApplicationRoles.User)`. **If this fails, the account must not be left
    role-less** — log at error and still return success, or delete the user and return 500. Pick
    deletion: a member with no role cannot be repaired by the admin UI this slice ships.
  - `SignInManager.SignInAsync(user, isPersistent: true)` — establishes the pending session (D1).
  - Return `200` with the same `CurrentUser` shape login returns, so the SPA has one code path.
- **Change the status branch at line 64** to refuse only `AccountStatus.Blocked` (D1, D2):

  ```csharp
  // Pending is deliberately NOT refused: the PRD's Access Control section and roadmap S-01 both
  // specify that a pending member signs in and sees an awaiting-approval screen. Content is gated
  // by the ActiveMember policy, not by refusing the session. Blocked stays refused — handing a
  // 30-day cookie to someone whose access was revoked inverts what Blocked is for.
  if (user.Status == AccountStatus.Blocked)
  {
      return Results.Json(new LoginFailure("blocked"), statusCode: 401);
  }
  ```

- Add `group.MapPost("/refresh", RefreshAsync).RequireAuthorization();` — **bare
  `RequireAuthorization()`, never `ActiveMember`**, or a pending member cannot call the one endpoint
  that un-pends them.

  ```csharp
  // Why this exists: the ActiveMember/Admin policies read the account_status CLAIM from the cookie,
  // not the database (AuthorizationPolicies.cs), and that claim is re-minted only when the
  // security-stamp validator refreshes — every 30 minutes (Program.cs). So a member approved by the
  // admin keeps a cookie that says Pending for up to half an hour, while /me (which reads the
  // database) correctly reports Active. Without this endpoint the SPA routes them into the app on
  // the strength of /me and every ActiveMember call then returns 403.
  //
  // RefreshSignInAsync re-runs AppUserClaimsPrincipalFactory against the current entity, so status
  // and roles are both corrected in one round-trip without ending the session.
  private static async Task<IResult> RefreshAsync(
      ClaimsPrincipal principal,
      SignInManager<ApplicationUser> signInManager,
      UserManager<ApplicationUser> userManager)
  {
      var user = await userManager.GetUserAsync(principal);
      if (user is null)
      {
          return Results.Unauthorized();
      }

      await signInManager.RefreshSignInAsync(user);
      return Results.Ok(await BuildCurrentUserAsync(user, userManager));
  }
  ```

  It is safe to call while still Pending — it simply re-mints Pending claims — so the awaiting
  screen's button can call it unconditionally and read the status from the response.
- Update the class comment (lines 22–23) — registration is no longer absent.
- Update the `LoginFailure` comment (lines 10–14): `pending_approval` is no longer a login outcome.
  Leave the type and the literal in place — the SPA's `LoginFailureReason` union still carries it and
  removing it is churn for no gain; say in the comment that it is now unreachable from `/login`.
- Add the D3 asymmetry comment on both `LoginAsync` and `RegisterAsync`, each pointing at the other.

**`src/Application/Members/MemberAdminEndpoints.cs`** (new)

**Adapted during implementation.** The plan did not say how an Application-layer handler reads the
pending queue or commits a unit of work without touching EF Core, which AGENTS.md reserves for
Infrastructure. Two narrow seams were added, following the `IOutboxWriter` / `IPushSubscriptionStore`
pattern F-03 established:

- `IPendingMemberQuery` (`Application/Members/MemberAdminEndpoints.cs`) →
  `Infrastructure/Members/PendingMemberQuery.cs` — the pending list, projected in the database.
- `IUnitOfWork` (`Application/Persistence/IUnitOfWork.cs`) →
  `Infrastructure/Persistence/UnitOfWork.cs` — the single `SaveChangesAsync` that commits the status
  flip and the outbox rows together. Without it the only way to persist the status change is
  `UserManager.UpdateAsync`, which saves on its own and would split the approval and the queued email
  into two transactions — the exact atomicity this phase exists to guarantee.

Both are registered in `Program.cs` beside F-03's scoped services.

One upward reference remains and is deliberate: `MemberAdminEndpoints` names
`Infrastructure.Authorization.AuthorizationPolicies` for the policy constant. The alternative is a
bare `"Admin"` string literal, which is the typo-that-never-matches those constants exist to prevent.
A layering note in the file records the escalation if Application ever grows a second such reference:
move the name constants into `Domain` and leave `AddApplicationPolicies` in Infrastructure. AGENTS.md's
hard rule — EF Core only in Infrastructure — is not affected.

- `MapGroup("/api/admin/members").WithTags("Members").RequireAuthorization(AuthorizationPolicies.Admin)`
  — group-level, so no endpoint can be added later without the policy.
- `GET /pending` → `PendingMember(string Id, string Email, string DisplayName, DateTimeOffset CreatedAt)[]`,
  ordered by `CreatedAt` ascending (oldest waiting first). Filtered on `Status == Pending`, which the
  existing index covers. No pagination — a single gym's pending queue is small, and D5 rules out the
  filtering UI that would make pagination meaningful.
- `POST /{id}/approve`:
  - Load the user. Not found → `404`.
  - **Already `Active` → `200` with no enqueue** (idempotent). Two admins clicking approve must not
    send two emails.
  - `Blocked` → `409 { "reason": "not_pending" }`. Approving a blocked member is S-02's unblock, not
    this endpoint.
  - `Pending` → set `Status = Active`, call `IAccountApprovedNotification.NotifyAsync`, then one
    `SaveChangesAsync`. `Enqueue` does not save, so the status flip and the outbox rows land in a
    single transaction — either the member is approved and the email is queued, or neither happened.
  - **Do not open an explicit transaction.** A single `SaveChangesAsync` is already atomic and is
    covered by the retry strategy. An explicit transaction here would require
    `Database.CreateExecutionStrategy().ExecuteAsync(...)` and throws at runtime without it (D6);
    there is no reason to take that risk for one save.
  - Add a comment recording exactly that, so a future edit that *does* need a transaction knows the
    rule before it writes one.

**`src/Program.cs`**

- `app.MapMemberAdminEndpoints();` after `app.MapPushEndpoints();` (line 201).
- Update the `IsEnvironment("Testing")` probe comment (lines ~208–215): `ActiveMember` and `Admin`
  now have production consumers. Keep the probes — they still test the policies in isolation.

**`src/Application/Notifications/AccountApprovedNotification.cs`**

- Adjust the body copy: the member is already signed in by the time this arrives, so
  "Możesz się teraz zalogować" is stale. Replace with wording that sends them back to the app.

**Tests** — `tests/po-prostu-silka.Tests/`

- `AuthEndpointTests.cs:38` — rewrite `Pending_user_is_refused_with_a_distinguishing_reason` into
  `Pending_user_receives_a_session`: asserts `200`, a `Set-Cookie`, and `status == "Pending"` in the
  body. Rename the test to state the new contract.
- `AuthEndpointTests.cs:52` — unchanged.
- Add: pending session reaches `/api/auth/me` but receives `403` from `/test/active-member`. This is
  the assertion that proves D1 is safe.
- Add the **claim-refresh regression test**, the definitive check on the F1 defect and the reason the
  `IsEnvironment("Testing")` probes exist: as a pending member, `GET /test/active-member` → `403`;
  approve them as admin; **without** calling `/refresh`, `/test/active-member` still → `403` (the
  cookie claim is stale, and asserting this pins the mechanism rather than the symptom); call
  `POST /api/auth/refresh`; `/test/active-member` → `200`.

  This must be an automated test rather than a manual one, because **production has no
  `ActiveMember` endpoint to check it against** — `/api/push` uses bare `RequireAuthorization()`, and
  `Home` is a placeholder. The probes are the only surface where this is observable until S-03.
- Add: `POST /api/auth/refresh` returns `401` when unauthenticated, and succeeds for a pending member
  (returning `"status":"Pending"`) — it must not require `ActiveMember`.
- `PushEndpointTests.cs:61-72` — complete `Pending_member_can_also_subscribe`: the pending member now
  logs in, `POST /api/push/subscribe` returns `204`, and the row exists. Delete the comment that says
  they cannot log in; replace it with one noting this is why the group uses bare
  `RequireAuthorization()`.
- New `RegisterEndpointTests.cs`: success creates a Pending user in the `User` role and sets a
  cookie; duplicate email returns `409 email_taken`; short password returns `400`; blank display name
  returns `400`.
- New `MemberAdminEndpointTests.cs`: anonymous → `401`; active non-admin → `403`; **pending member →
  `403`** (the policy requires Active *and* Admin); admin sees the seeded pending member in `/pending`;
  approve flips status to Active **and writes an outbox row**; approving twice writes exactly one row;
  approve on a blocked member returns `409`; approve on an unknown id returns `404`.

### Success criteria

#### Automated Verification:

- `dotnet build` from `src/` — no warnings
- `dotnet test` from repo root — all pass, including the rewritten `AuthEndpointTests`
- `MemberAdminEndpointTests` proves the double-approve case writes exactly one outbox row

#### Manual Verification:

- With the app running locally over `https://localhost:5201`, register a new account via
  `POST /api/auth/register` and confirm the response carries a cookie and `"status":"Pending"`
- As the seeded admin, `GET /api/admin/members/pending` lists that account
- `POST /api/admin/members/{id}/approve` returns `200`; a follow-up `GET /api/auth/me` on the member's
  cookie now reports `"status":"Active"`
- Query `OutboxMessages` directly and confirm exactly one Email row was written for the approval

---

## Phase 2: Frontend foundation and the member screens

### Overview

Replaces the CLI scaffold with the real shell, lays the token and typography layer, and builds the
three member-facing screens on reactive forms.

### Changes required

**Design foundation**

- `src/app/public/fonts/` — self-hosted `.woff2` for Cormorant Garamond (600) and Plus Jakarta Sans
  (400, 500, 600). Subset to `latin` + `latin-ext`; **`latin-ext` is not optional** — Polish copy
  needs ł, ż, ś, ę, ą, ć, ń, ó, ź.

  **Adapted during implementation.** Four files, not six. Plus Jakarta Sans is a variable font: Google
  serves byte-identical files for 400, 500 and 600, so the three weights ship as one
  `plus-jakarta-sans-variable-<subset>.woff2` per subset declared `font-weight: 200 800`. Shipping
  them separately would have been ~98 KB of duplicate bytes. Cormorant Garamond stays one file per
  subset at weight 600.
- `src/app/src/styles.scss` — `@font-face` declarations with `font-display: swap`, the D7 token
  variables, a small reset, base typography, and shared form/button/card classes. Every font stack
  carries a real fallback (`Cormorant Garamond, Georgia, serif`).
- `src/app/src/app/app.html` — replace the 353-line scaffold with a shell: header (product name, and
  a logout control when authenticated) plus `<router-outlet />`. The admin link belongs to Phase 3,
  which is also where the route it points at is added — shipping it here would be a link to a path
  that redirects to `/`.
- `src/app/src/app/app.scss` — shell layout only.
- `src/app/src/app/app.spec.ts:21` — update the `"Hello, app"` assertion to the real shell heading.

**Auth models and service**

- `src/app/src/app/core/auth/auth.models.ts` — add `RegisterRequest`, `RegisterFailureReason`
  (`'email_taken' | 'invalid_password' | 'invalid_email' | 'invalid_display_name'`),
  `RegisterFailure`. Note in the comment that `pending_approval` is now unreachable from `/login`
  but kept for the union's completeness.
- `auth.service.ts` — add `register(request)`, mirroring `login()`: sets `currentUser`, sets
  `resolved`, does not catch. Add `refresh()` calling `POST /api/auth/refresh` and setting
  `currentUser` from the response — **not** an alias over `loadCurrentUser()`, which hits `/me` and
  would leave the cookie claim stale (D11).

**Guards**

- `core/auth/active-member.guard.ts` — `activeMemberGuard`: server platform passes (same reason
  `authGuard` does, `auth.guard.ts:20-24`); resolves the session if unresolved; Active → true;
  authenticated but Pending → `createUrlTree(['/pending'])`; unauthenticated → `createUrlTree(['/login'])`.
- `core/auth/admin.guard.ts` — `adminGuard`: same shape, requires `isAdmin() && isActive()`,
  otherwise redirects to `/`.
- `core/auth/auth.guard.ts` — **untouched**, including its comment, which correctly described the
  split this phase implements.

**Screens** — `src/app/src/app/features/auth/`

- `login/login.ts|html|scss` — reactive form (email, password). On `blocked`, show the blocked
  message. On `invalid_credentials`, a single non-specific message. On success, route by status:
  Pending → `/pending`, Active → `/`.
- `register/register.ts|html|scss` — reactive form (display name, email, password with the 8-char
  minimum mirrored client-side). `409 email_taken` sets an error on the email control (D8's stated
  payoff). On success, route to `/pending`.

  **Adapted during implementation.** Setting the error is not enough: the template reveals a field
  error only once the control is `touched`, and submitting an otherwise-valid form never touches it —
  so `setErrors` alone left the member facing a form that had refused them and said nothing. Every
  server-mapped failure goes through a `reject(control, errors)` helper that also calls
  `markAsTouched`. Caught by `register.spec.ts`.
- `pending/pending.ts|html|scss` — the awaiting-approval screen. Explains the wait, states that an
  email will arrive, and offers "Sprawdź ponownie" which calls `refresh()` and navigates to `/` when
  the status has become Active (D11). If a refresh finds the member still Pending, say so — a button
  that appears to do nothing is worse than one that reports no change.

  On load, redirect to `/` when `isActive()` — otherwise an already-approved member who types
  `/pending` sees a screen telling them to await approval they already have. The route deliberately
  carries only `authGuard` (a Pending member must reach it), so this belongs in the component rather
  than in a guard.
- Delete `core/auth/route-placeholders.ts`. Its own comment says S-01 replaces both components.

**`src/app/src/app/app.routes.ts`**

```
/login       → Login          (no guard)
/register    → Register       (no guard)
/pending     → Pending        (authGuard only — a Pending member must reach it)
/            → Home           (authGuard + activeMemberGuard)
/**          → redirect to ''
```

`Home` is a minimal placeholder for now: a greeting and a logout control. S-03 replaces it with the
schedule. Keep it in `features/home/` rather than resurrecting `route-placeholders.ts`.

**Prerendering**: all routes are static paths, so `RenderMode.Prerender` on `**` needs no
`getPrerenderParams`. Both new guards must return `true` on the server platform or the build breaks —
this is the trap `auth.guard.ts:20-24` documents.

**Adapted during implementation.** Prerendering does not actually run. `angular.json`'s build target
has `outputMode: "static"` but **no `server` entry point and no `ssr`/`prerender` options**, so
`main.server.ts`, `server.ts` and `app.routes.server.ts` are not part of the build: `npm run build`
emits a single `dist/app/browser/index.html` and no server bundle. The "Current state" table above
overstates the baseline — it reads `app.routes.server.ts` as if the builder consumed it.

Both guards still carry the `isPlatformServer` early return. It is correct, it matches `authGuard`,
and it is what makes them safe the day SSR is switched on — but today it is defensive, not
load-bearing. The Phase 2 criterion below therefore verifies only that the build succeeds. Wiring SSR
up was rejected as scope: it changes what is deployed into App Service's `wwwroot`, which no part of
this slice needs.

**Specs** — Vitest

- `login.spec.ts` — validation blocks submit; `invalid_credentials` renders the generic message;
  `blocked` renders the blocked message; a Pending login navigates to `/pending`.
- `register.spec.ts` — `409 email_taken` surfaces as an error on the email control; success navigates
  to `/pending`.
- `pending.spec.ts` — the button calls `/api/auth/refresh` (**not** `/me` — F1); still-Pending stays
  put and reports it; now-Active navigates to `/`; loading the screen while already Active redirects
  to `/` (F5).
- `active-member.guard.spec.ts`, `admin.guard.spec.ts` — each redirect branch, plus the
  server-platform pass-through.
- Use `await vi.waitFor(() => controller.expectOne(...))` for HTTP assertions — the microtask-drain
  failure that cost time in F-03.

### Success criteria

#### Automated Verification:

- `npm run quality:check` from `src/app/` — clean
- `npm test` from `src/app/` — all pass, including the updated `app.spec.ts`
- `npm run build` from `src/app/` — clean. NOT a prerender check: prerendering is not wired in
  `angular.json` (see the adaptation note above), so this verifies the bundle builds and the
  font assets land in `dist/app/browser/fonts/`

#### Manual Verification:

- Register through the UI; land on the awaiting-approval screen
- Confirm the fonts render and Polish diacritics are correct (ł, ż, ś, ę, ą, ć, ń) — a missing
  `latin-ext` subset shows here and nowhere else
- Navigating directly to `/` while Pending redirects to `/pending`
- Log out, log back in as the same pending member, land on `/pending` again
- Check the screens at a phone width — members book from phones (FR-002's mobile note)

---

## Phase 3: Admin approvals screen, deploy, and end-to-end verification

### Overview

The admin's pending list, then the first production run of the whole path — including F-03's outbox
delivering a real email to a real inbox.

### Changes required

**`src/app/src/app/core/admin/member-admin.service.ts`** (new)

- `getPending(): Promise<PendingMember[]>` and `approve(id): Promise<void>` over
  `/api/admin/members`. Models mirror the API records, with the same "this is a contract" comment
  `auth.models.ts` carries.

**`src/app/src/app/features/admin/approvals/`** (new)

- Lists pending members — display name, email, waiting since — with an Approve button per row.

  **Adapted during implementation.** "Waiting since" is a formatted Polish date, which means
  `DatePipe` needs CLDR data for `pl` or it throws *"Missing locale data for the locale pl"* — at
  runtime, on this screen only. `app.config.ts` now calls `registerLocaleData(localePl)` and provides
  `LOCALE_ID: 'pl'`. This is locale DATA, not the i18n machinery D9 rejected: no translation files, no
  build configuration, no second locale.
- After a successful approve, remove the row locally rather than refetching the list.
- Empty state: an explicit "no pending registrations" message, not a blank page.
- A failed approve leaves the row in place and shows the error. Silent failure here means the admin
  believes someone was approved when they were not.

**`src/app/src/app/app.routes.ts`** — add `/admin/approvals` behind `[authGuard, adminGuard]`.

**Shell** — show a link to `/admin/approvals` only when `isAdmin()`.

**Specs**

- `approvals.spec.ts` — renders the list; approve removes the row; a failed approve keeps it and
  surfaces the error; the empty state renders.
- `member-admin.service.spec.ts` — both calls hit the right URLs.

**Deploy**

- Push to `main`; App Service picks it up. No migration in this slice — no schema change.
- No new app settings. ACS and VAPID were configured in F-03.

### Success criteria

#### Automated Verification:

- `npm run quality:check`, `npm test`, `npm run build` from `src/app/`
- `dotnet build` and `dotnet test` — still clean

#### Manual Verification:

- Deployed site loads and `GET /health` is healthy
- Register a real account on the deployed site
- Sign in as admin, see it in the approvals list, approve it
- **The approval email actually arrives** — this is the first time F-03's transport delivers for a
  real product action
- The approved member presses "Sprawdź ponownie" and reaches the app. The claim refresh itself is
  proved by the Phase 1 probe test, not here — production has no `ActiveMember` endpoint to observe
  it against until S-03.
- Confirm in App Service logs that the outbox heartbeat processed the row and it is marked `Sent`
- A second approve on the same member sends no second email

---

## Open risks and assumptions

- **A role-less user is unrecoverable** — *accepted at plan review, F3.* `CreateAsync` and
  `AddToRoleAsync` are two saves with no transaction, and the "delete the user" fallback is a third
  save that can fail the same way. The residue is a Pending account with no role: it passes
  `ActiveMember`'s status check, fails its `RequireRole(ApplicationRoles.All)`, and has no admin
  surface to repair it, since D5 ships approve only. Judged unlikely enough to live with — it needs a
  DB fault landing between two consecutive saves. If it ever happens, the repair is a manual row
  insert into `AspNetUserRoles`. F-02's review skipped a related role-seeding finding, so this is the
  second accepted risk on the same mechanism; a third should prompt a real fix rather than another
  acceptance.
- **Simultaneous registration of the same email returns 500** — *accepted at implementation review
  (F9).* Two concurrent `POST /api/auth/register` calls for one address pass both the
  `FindByEmailAsync` pre-check and Identity's `UserValidator`, and the INSERT then violates
  `UserNameIndex`: `Cannot insert duplicate key row in object 'dbo.AspNetUsers'`. It surfaces as an
  unhandled `DbUpdateException` on an anonymous endpoint. Proven with a throwaway concurrent test.
  Not fixed: catching it needs EF Core types in Application (AGENTS.md's hard rule), and
  `UserManager.CreateAsync` owns its own save, so the `IUnitOfWork` seam cannot intercept it — a new
  `IUserRegistrar` seam is a lot of abstraction for one race. The window is a few milliseconds, and a
  member who retries lands on the clean `409 email_taken` path. The mapper does now answer
  `409 email_taken` for Identity's `Duplicate*` codes, which covers the wider validator-caught case.

- **Junk registrations accumulate unbounded** (D4). Nothing prunes Pending rows and no rate limit
  exists. S-02's member list will surface them. If the gym is targeted, the mitigation is manual.
- **Registration is an email-enumeration oracle** (D3), deliberately, and asymmetric with login's
  non-disclosure. Both endpoints carry comments explaining why; a reviewer who reads only one will
  flag it.
- **A blocked member's live cookie survives up to 30 minutes** — the security-stamp validation
  interval. Out of scope here (nothing blocks anyone until S-02) but it lands with S-02's block
  action, and D2 is what keeps the window from being 30 days.
- **`AccountApprovedNotification` sends push to every registered subscription.** F-03's known IDOR in
  `PushSubscriptionStore.UpsertAsync` (finds by `Endpoint` alone, then reassigns `UserId`) was
  accepted as latent because endpoints are never surfaced. This slice does not change that, but it
  makes the approval push the first real traffic through that path.
- **Self-hosted font files are a manual step** — the `.woff2` files must be downloaded and committed.
  A missing `latin-ext` subset degrades silently into wrong glyphs for Polish text; the Phase 2
  manual check exists specifically to catch it.
- **No test covers F-03's double-claim scenario** — carried over from F-03's review, unchanged here.
- **`Home` is a placeholder.** An approved member reaches a near-empty screen until S-03. That is the
  correct scope, but it means the slice's own success criterion ("reaches the app proper") is
  satisfied by very little app — and, more sharply, that **no production endpoint exercises the
  `ActiveMember` policy end to end**. The `IsEnvironment("Testing")` probes carry that weight alone
  until S-03. This is why the F1 claim-refresh check is an automated test rather than a manual one.

---

## Progress

### Phase 1: Registration, the pending session, and approval

#### Automated

- [x] 1.1 Add `/api/auth/register` with duplicate-email, password, and display-name handling — 3e9296c
- [x] 1.2 Narrow login's status refusal to Blocked only; update the surrounding comments — 3e9296c
- [x] 1.3 Add `POST /api/auth/refresh` under bare `RequireAuthorization()` (F1) — 3e9296c
- [x] 1.4 Add `MemberAdminEndpoints` — pending list and idempotent approve — behind the Admin policy — 3e9296c
- [x] 1.5 Wire `IAccountApprovedNotification` into approve in a single unit of work — 3e9296c
- [x] 1.6 Adjust the approval email copy for an already-signed-in member — 3e9296c
- [x] 1.7 Map the group in `Program.cs`; update the Testing-probe comment — 3e9296c
- [x] 1.8 Rewrite `AuthEndpointTests` pending case; add the pending-session-vs-ActiveMember test — 3e9296c
- [x] 1.9 Add the claim-refresh regression test against `/test/active-member` (F1) — 3e9296c
- [x] 1.10 Complete `PushEndpointTests.Pending_member_can_also_subscribe` (F2) — 3e9296c
- [x] 1.11 Add `RegisterEndpointTests` and `MemberAdminEndpointTests` — 3e9296c
- [x] 1.12 `dotnet build` and `dotnet test` clean — 3e9296c

#### Manual

- [x] 1.13 Register locally over https; confirm cookie and `"status":"Pending"` — 3e9296c
- [x] 1.14 Admin lists and approves; member's `/me` reports Active — 3e9296c
- [x] 1.15 Exactly one outbox Email row per approval, verified in the database — 3e9296c

### Phase 2: Frontend foundation and the member screens

#### Automated

- [x] 2.1 Self-host the two font families with `latin` + `latin-ext` subsets — 399202b
- [x] 2.2 Write the token layer, reset, and base typography in `styles.scss` — 399202b
- [x] 2.3 Replace the CLI scaffold shell; update `app.spec.ts` — 399202b
- [x] 2.4 Extend `auth.models.ts` and `auth.service.ts` with register and refresh — 399202b
- [x] 2.5 Add `activeMemberGuard` and `adminGuard`; leave `authGuard` untouched — 399202b
- [x] 2.6 Build the login, register, and awaiting-approval screens on reactive forms — 399202b
- [x] 2.7 Rewire `app.routes.ts`; delete `route-placeholders.ts`; add the `Home` placeholder — 399202b
- [x] 2.8 Add Vitest specs for all three screens and both new guards — 399202b
- [x] 2.9 `quality:check`, `npm test`, and `npm run build` clean — 399202b

#### Manual

- [x] 2.10 Register through the UI and land on the awaiting screen — 399202b
- [x] 2.11 Polish diacritics render correctly in both font families — 399202b
- [x] 2.12 `/` while Pending redirects to `/pending`; re-login returns there — 399202b
- [x] 2.13 Screens usable at phone width — 399202b

### Phase 3: Admin approvals screen, deploy, and end-to-end verification

#### Automated

- [x] 3.1 Add `member-admin.service.ts` with its contract-mirroring models — 96210d6
- [x] 3.2 Build the approvals screen with empty and error states — 96210d6
- [x] 3.3 Add the guarded route and the admin-only shell link — 96210d6
- [x] 3.4 Add specs for the screen and the service — 96210d6
- [x] 3.5 Frontend and backend suites clean — 96210d6

#### Manual

- [x] 3.6 Deploy; `/health` healthy — 96210d6
- [x] 3.7 Register on the deployed site; approve as admin — 96210d6
- [x] 3.8 The approval email arrives in a real inbox — 96210d6
- [x] 3.9 The approved member refreshes and reaches the app — 96210d6
- [x] 3.10 Outbox row reaches `Sent` in the App Service logs — 96210d6
- [x] 3.11 A second approve sends no second email — 96210d6
