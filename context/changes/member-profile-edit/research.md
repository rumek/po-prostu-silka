---
date: 2026-09-05T09:40:12+02:00
researcher: Karol Rumianowski
git_commit: 2ae79df6c7578ef08b5f3d56307afac0e7d4dcd0
branch: main
repository: po-prostu-silka
topic: "Member profile edit: address/phone fields at registration, password change, and pre-login password reset"
tags: [research, codebase, auth, identity, members, registration, password-reset, notifications, angular]
status: complete
last_updated: 2026-09-05
last_updated_by: Karol Rumianowski
---

# Research: Member profile edit — address/phone fields, password change, password reset

**Date**: 2026-09-05T09:40:12+02:00
**Researcher**: Karol Rumianowski
**Git Commit**: `2ae79df6c7578ef08b5f3d56307afac0e7d4dcd0`
**Branch**: `main`
**Repository**: `po-prostu-silka` (https://github.com/rumek/po-prostu-silka)

## Research Question

The slice as stated in `change.md`:

1. Add new fields collected **during registration** — phone number, street, house/apartment number,
   postal code, city. **Required in the registration form, nullable in the database.**
2. A member profile edit screen: the new fields are editable; **first/last name and email are not**.
3. Change password from within profile edit.
4. Forgot/reset password on the public entry page, before login.

What does the codebase already provide, what is missing, and what prior decisions constrain the plan?

## Summary

**The good news.** Three of the four items sit on infrastructure that already exists and needs no new
dependency:

- The member *is* `ApplicationUser : IdentityUser` — a single row in `AspNetUsers`, no separate
  `Member` entity. New profile columns are additive properties on one class plus one migration.
- **`.AddDefaultTokenProviders()` is already registered** (`src/Program.cs:91`), so
  `GeneratePasswordResetTokenAsync` / `ResetPasswordAsync` / `ChangePasswordAsync` work today. This
  contradicts F-02's archived plan text, which never mentions token providers — an instance of the
  exact plan-drift pattern recorded in `context/foundation/lessons.md:5-29`. **The live code is
  authoritative: the provider is there.**
- Transactional email already ships through `IEmailSender` → outbox → `OutboxDeliveryWorker`, with
  `AccountApprovedNotification` as a working template to copy.

**The blockers.** Four things genuinely do not exist and must be decided before or during planning:

- **There are no first/last-name fields.** The model has one free-text `DisplayName`
  (`src/Domain/ApplicationUser.cs:19`). "First and last name are not editable" presupposes a schema
  that has never existed. → Open question 1.
- **There is no logged-out landing page.** `/` is behind `[authGuard, activeMemberGuard]`
  (`src/app/src/app/app.routes.ts:28`), so an anonymous visitor is redirected to `/login`. "Password
  reset on the home page before login" in practice means *on the login page*. → Open question 3.
- **There is no config key for a public/frontend base URL** anywhere in `src/appsettings*.json`. A
  reset link has nothing to build an absolute URL from. → Open question 4.
- **There is no rate limiting at all** in the app, and `/api/auth/forgot-password` would be the most
  abusable anonymous endpoint in it. → Open question 6.

**The scope question.** Roadmap slice **S-13 `member-profile-edit` is `ready`**, but its authorized
outcome is *"edit display name and change their password"* (`roadmap.md:281-291`), tracing to PRD
**FR-006**, which was explicitly *trimmed* to name + password. Address/phone fields and the whole
forgot-password flow are **net-new scope beyond both the PRD and the roadmap**. That is a decision to
make deliberately, not to smuggle into an existing slice. → Open question 8.

## Detailed Findings

### 1. The member model — one Identity row, no `Member` entity

`src/Domain/ApplicationUser.cs:17-31`:

```csharp
public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
    public AccountStatus Status { get; set; } = AccountStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; }
}
```

- Extends `IdentityUser` (namespace `Microsoft.AspNetCore.Identity` — **not** EF Core, which is why
  it is legal in `Domain/` under the AGENTS.md layering rule).
- `IdentityUser` already carries a **`PhoneNumber`** column plus `PhoneNumberConfirmed`. It is
  currently unused and unconfigured. The new phone field can reuse it or be a fresh property — a
  deliberate choice, not an accident. Reusing it inherits Identity's `nvarchar(max)` default unless
  reconfigured, and drags in `PhoneNumberConfirmed` semantics the product does not use.
- There is no `Member` table, no FK, no shared-PK split. `src/Infrastructure/Members/` contains only
  read-side query classes (`MemberQuery.cs`, `PendingMemberQuery.cs`, `TrainerQuery.cs`), not a
  persisted model.
- Supporting types: `src/Domain/AccountStatus.cs` (Pending=0 / Active=1 / Blocked=2, values pinned),
  `src/Domain/ApplicationRoles.cs`.

### 2. Registration today

Endpoint map at `src/Application/Auth/AuthEndpoints.cs:38-56`, mounted from `src/Program.cs:260`.

| Method + route | Handler | Line | Auth |
| --- | --- | --- | --- |
| `POST /api/auth/login` | `LoginAsync` | `:42` / impl `:58-107` | `AllowAnonymous` |
| `POST /api/auth/register` | `RegisterAsync` | `:43` / impl `:123-213` | `AllowAnonymous` |
| `POST /api/auth/logout` | `LogoutAsync` | `:44` / impl `:246-250` | `RequireAuthorization` |
| `POST /api/auth/refresh` | `RefreshAsync` | `:48` / impl `:231-244` | `RequireAuthorization` |
| `GET /api/auth/me` | `GetCurrentUser` | `:53` / impl `:252-277` | `RequireAuthorization` |

- Request DTO: `record RegisterRequest(string Email, string Password, string DisplayName);`
  (`AuthEndpoints.cs:10`). **Three fields.** No phone, no address.
- Response: `record CurrentUser(string Id, string Email, string DisplayName, string Status, string[] Roles);`
  (`AuthEndpoints.cs:26`).
- **Validation is hand-rolled.** No DataAnnotations, no FluentValidation, no `AddProblemDetails()`.
  Failures are `record RegisterFailure(string Reason)` (`:24`) returned via
  `Results.Json(..., statusCode: N)` with a stable machine-readable reason string the SPA switches
  on. Identity's own error text is never forwarded raw (`:163-188`).
- Null/whitespace guards are explicit (`:130-134`, `:139-147`) because a positional record of
  non-nullable `string`s still deserialises `{"email": null}` happily. The registration impl-review
  logged this as a real 500 (`context/archive/2026-09-01-registration-and-approval/reviews/impl-review.md:151-153`).
  **New required-at-registration fields must repeat this guard.**
- Write path (`:154-209`): construct `ApplicationUser` → `CreateAsync` → `AddToRoleAsync(User)` →
  on role failure `DeleteAsync` to avoid a role-less orphan → `SignInAsync(isPersistent: true)`.

### 3. Persistence and migrations

`src/Infrastructure/Persistence/AppDbContext.cs:19-46` — `AppDbContext : IdentityDbContext<ApplicationUser>`.
`OnModelCreating` (`:38-45`) calls `base.OnModelCreating` **first**, then
`ApplyConfigurationsFromAssembly` — a new config class is auto-discovered, no registration needed.

The style to mirror, `src/Infrastructure/Persistence/Configurations/ApplicationUserConfiguration.cs:12-32`:

```csharp
builder.Property(x => x.DisplayName).IsRequired().HasMaxLength(100);
builder.Property(x => x.Status).IsRequired().HasConversion<int>().HasDefaultValue(AccountStatus.Pending);
builder.Property(x => x.CreatedAt).IsRequired();
builder.HasIndex(x => x.Status);
```

The comment at `:7-11` warns explicitly against re-declaring Identity's own keys and indexes here.

Twelve migrations exist, newest `20260904215505_AddTrainingPlans`. The user table and its custom
columns were created in `20260831183430_AddIdentitySchema` (`Up` at `:40-66`, `Down` at `:220-245`).
Convention per `AGENTS.md` and `context/archive/2026-08-31-persistence-foundation/plan-brief.md:102`:
**one focused migration per schema addition, with a working `Down`.** For additive nullable columns
the `Down` is a straightforward `DropColumn` set — no deferred-destructive complication.

### 4. Read surfaces that will need extending

All member reads use `AsNoTracking()` + explicit `.Select(...)` projections, never the whole entity.
Adding a property to `ApplicationUser` therefore surfaces **nowhere** until each projection is edited:

- `GET /api/auth/me` → `CurrentUser` (`AuthEndpoints.cs:26`, `:252-277`). Roles come from cookie
  claims, not a DB re-query.
- `GET /api/admin/members/` → `MemberSummary` (`src/Application/Members/MemberAdminEndpoints.cs:37-43`),
  implemented in `src/Infrastructure/Members/MemberQuery.cs:27-76`.
- `GET /api/admin/members/pending` → `PendingMember` (`MemberAdminEndpoints.cs:13`),
  `src/Infrastructure/Members/PendingMemberQuery.cs:18-29`.
- `TrainerSummary` (`src/Infrastructure/Members/TrainerQuery.cs:15-39`) is `Id`/`DisplayName` only —
  unaffected.

**There is no profile-edit endpoint today.** `ApplicationUser.cs:19` even comments that `DisplayName`
is "Editable by the member (S-09, FR-006)" — the intent was recorded, the endpoint never shipped.

### 5. Identity, cookies, and what password flows can rely on

`src/Program.cs:66-91` — verified directly in the code, not inferred from the archived plan:

```csharp
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();

builder.Services.AddIdentityCore<ApplicationUser>(options => { /* RequiredLength = 8, all
        character-class rules false, RequiredUniqueChars = 1, RequireConfirmedAccount = false,
        RequireUniqueEmail = true */ })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>()
    .AddDefaultTokenProviders();          // <-- src/Program.cs:91
```

- **`AddDefaultTokenProviders()` is present.** `DataProtectorTokenProvider` is registered; reset
  tokens work out of the box. Default `TokenLifespan` is **1 day** — not overridden anywhere. No
  `options.Tokens.PasswordResetTokenProvider` customisation exists.
- **Password policy: length ≥ 8, nothing else.** Rationale recorded in
  `context/archive/2026-08-31-auth-identity-foundation/plan-brief.md:37`. The Angular side mirrors it
  as a module constant (`register.ts:14-15`, `MIN_PASSWORD_LENGTH = 8`) — a duplicated contract that
  a change-password and a reset-password form must both honour.
- **Lockout options are never configured** — Identity defaults apply (5 attempts / 5 minutes), and
  login opts in with `CheckPasswordSignInAsync(..., lockoutOnFailure: true)` (`AuthEndpoints.cs:86`).
- Cookie config (`src/Program.cs:93-120`): 30-day sliding, `HttpOnly`, `Secure=Always`,
  `SameSite=Lax` (Lax deliberately — cancellation-email links must carry the cookie back in;
  `:101-104`). `OnRedirectToLogin` → 401, `OnRedirectToAccessDenied` → 403, because this is an API.
  **A reset link arriving from an email inherits this same Lax reasoning.**
- `SecurityStampValidatorOptions.ValidationInterval = 2 minutes` (`src/Program.cs:133-134`, lowered
  from 30 by S-02). Both `ChangePasswordAsync` and `ResetPasswordAsync` rotate the security stamp, so
  **other sessions die within ~2 minutes** — a free win, but it also means the *acting* session is
  invalidated too unless the handler calls `SignInManager.RefreshSignInAsync(user)` afterwards. That
  method is already used by `RefreshAsync` (`AuthEndpoints.cs:236-242`).
- Authorization policies (`src/Infrastructure/Authorization/AuthorizationPolicies.cs:35-60`):
  `ActiveMember`, `Admin`, `TrainerOrAdmin` — each is `RequireAuthenticatedUser()` +
  `RequireClaim(Status, "Active")` + a role check. For **change password**, bare
  `RequireAuthorization()` (as `/me`, `/refresh`, `/logout` use) is the better fit: a *Pending*
  member should still be able to change their own password. For **forgot/reset**, `AllowAnonymous()`.

**Absent entirely**: `change-password`, `forgot-password`, `reset-password` endpoints. A repo-wide
grep finds zero calls to `ChangePasswordAsync`, `GeneratePasswordResetTokenAsync`, or
`ResetPasswordAsync` outside framework assemblies.

### 6. Email delivery — reuse the outbox, do not build a parallel path

- Abstraction: `src/Application/Notifications/IEmailSender.cs:8-12` —
  `Task<DeliveryResult> SendAsync(string to, string subject, string body, CancellationToken ct)`.
  **Plain text only**; the ACS call hardcodes `htmlContent: null`.
- Sender: `src/Infrastructure/Notifications/AcsEmailSender.cs:44-101` (Azure Communication Services,
  `WaitUntil.Started`). If unconfigured it logs a warning and returns
  `DeliveryResult.Permanent("acs_not_configured")` (`:55-61`).
- Queueing: `IOutboxEnqueuer.Enqueue(channel, recipient, subject, body)`
  (`src/Application/Notifications/OutboxEnqueuer.cs:13-48`) writes an **already-rendered** row and
  does *not* save — the caller's unit of work commits it atomically with the domain change.
- Delivery: `src/Infrastructure/Notifications/OutboxDeliveryWorker.cs:18-320`, registered at
  `src/Program.cs:215`. Claim-with-lease, retry/backoff, stale-lease reclaim, pruning.
  **At-least-once by design** (`context/archive/2026-08-31-notification-delivery-foundation/plan-brief.md:35`).
- Template to copy: `src/Application/Notifications/AccountApprovedNotification.cs:19-58` — body built
  with plain C# string interpolation at enqueue time, called alongside `SaveChangesAsync`.
- Config keys (`src/appsettings.json:13-17`): `Acs:ConnectionString`, `Acs:SenderAddress` (empty
  placeholders; production supplies `Acs__*` App Service settings). Also `VapidKeys:*`,
  `AdminSeed:*`, `ConnectionStrings:Default`.

**Two consequences for a reset email.** (a) At-least-once delivery means the *token*, not the send,
must carry the single-use guarantee — Identity's stamp rotation gives this, since a used token stops
validating. (b) There is **no dev/console email fallback**: locally, without ACS credentials, a reset
mail is silently marked permanently failed in the outbox and the link never reaches anyone. Manual
verification of this flow needs either real ACS credentials or a new dev sender.

### 7. Frontend — Angular 22, hand-rolled, Polish copy

- **Routing** (`src/app/src/app/app.routes.ts`): `login` (`:24-25`) and `register` (`:26`) are the
  only guard-free routes. `pending` (`:27`) has `authGuard` only. `''` → `Home` with
  `[authGuard, activeMemberGuard]` (`:28`). `**` → `''` (`:120`). Anything not needed on first paint
  is `loadComponent`-lazy, explicitly to stay under the 500 kB budget in `angular.json` (`:31-35`).
- **Session state** (`core/auth/auth.service.ts`): a private `signal<CurrentUser|null>` (`:19`)
  exposed via `.asReadonly()` (`:22`), with `computed()` flags `isAuthenticated`/`isActive`/
  `isAdmin`/`isTrainer` (`:24-36`) and a `resolved` signal (`:42-43`). **No tokens anywhere** — the
  SPA is same-origin and rides the HttpOnly cookie (`:10-13`). Calls use
  `firstValueFrom(http.post/get(...))` and deliberately do **not** catch, so components can map
  `HttpErrorResponse` bodies onto controls.
- **Interceptor** (`core/auth/auth.interceptor.ts`): global 401 → `auth.clear()` + navigate to
  `/login`, except for `EXPECTED_401_PATHS = ['/api/auth/login', '/api/auth/me']` (`:14`, `:30-33`).
  **A new endpoint that legitimately answers 401 would need adding to that list** — though 400/404
  are more idiomatic for password flows here.
- **Form pattern** (`features/auth/register/register.ts`): `inject(FormBuilder).nonNullable.group`
  (`:33-37`), `submit()` guards `form.invalid` → `markAllAsTouched()` (`:43-46`), `submitting` and
  `error` signals, `try/catch/finally`. `applyFailure()` (`:68-91`) switches on
  `error?.error?.reason` and calls `control.setErrors(...)` + `markAsTouched()` via a `reject()`
  helper (`:98-101`), with a banner fallback for unrecognised reasons. Login deliberately uses a
  **banner only, never per-field**, so it does not disclose which field was wrong (`login.ts:62-63`).
- **Templates**: `<div class="field">` + `<label for>` + `[attr.aria-invalid]` + `@if` blocks for
  `.field-error`; `.alert[role=alert]` banners; submit disabled while `submitting()`.
- **Styling**: hand-rolled SCSS design system, explicitly not Tailwind/Material
  (`src/app/src/styles.scss:1-11`, decision D7). Global `.panel`, `.card`, `.field`, `.field-error`,
  `.hint`, `.button*`, `.alert` (`:194-375`); component `.scss` files hold layout only.
- **i18n**: none. Polish copy is hardcoded in templates. `LOCALE_ID` is `'pl'` and
  `@angular/common/locales/pl` is registered for `DatePipe` **only** — "Locale DATA, not i18n. D9
  rules out translation machinery" (`app.config.ts:16-23`).
- **Tests** (`register.spec.ts`): `TestBed` + `provideHttpClientTesting()` + `provideRouter([])`
  (`:27-30`), DOM-driven `fill()`/`submit()` helpers dispatching real `input`/`submit` events
  (`:41-63`), `HttpTestingController.expectOne('/api/auth/register')` inside `vi.waitFor`, assertions
  on both `.field-error` text and `Router.navigate` spies, `afterEach(() => controller.verify())`.
- **No profile/account/settings screen exists.** A broad grep across `src/app` for
  `profile|account|settings|konto|profil|change.?password|forgot|reset.?password` found no route,
  directory, or component. `app.html:6-45` has a logout button (`:43`) and no account link — a new
  nav entry is needed.
- **Admin member list** (`core/admin/member-admin.models.ts:32-47`) is
  `{ id, email, displayName, status, roles[], createdAt }`, rendered at `members.html:82-104`. No
  phone/address anywhere; row actions are approve/block/unblock/trainer only (`members.ts:134-229`).

### 8. Where the "forgot password" link actually goes

Because `/` is guarded, **there is no logged-out landing page** — `authGuard` sends anonymous
visitors to `/login` (`auth.guard.ts:37`), making the login screen the de-facto front page. The
natural placement mirrors the existing footer link in `login.html:45`
(`Nie masz jeszcze konta? <a routerLink="/register">Zarejestruj się</a>`): a sibling
`<a routerLink="/forgot-password">Nie pamiętasz hasła?</a>`, or a link directly under the password
field. New `forgot-password` / `reset-password` routes slot in beside `login`/`register` as
guard-free entries in `app.routes.ts:24-26`.

## Code References

Permalink base: `https://github.com/rumek/po-prostu-silka/blob/2ae79df6c7578ef08b5f3d56307afac0e7d4dcd0/`

**Backend**

- `src/Domain/ApplicationUser.cs:17-31` — the member model; where new profile properties go.
- `src/Application/Auth/AuthEndpoints.cs:10` — `RegisterRequest`, the DTO to extend.
- `src/Application/Auth/AuthEndpoints.cs:26` — `CurrentUser`, the `/me` contract mirrored by the SPA.
- `src/Application/Auth/AuthEndpoints.cs:38-56` — endpoint group; where new routes are mapped.
- `src/Application/Auth/AuthEndpoints.cs:123-213` — `RegisterAsync`; validation and write path.
- `src/Application/Auth/AuthEndpoints.cs:163-188` — Identity errors mapped to app reason codes.
- `src/Application/Auth/AuthEndpoints.cs:231-244` — `RefreshSignInAsync` usage to copy after a password change.
- `src/Program.cs:66-91` — Identity registration; `AddDefaultTokenProviders()` at `:91`.
- `src/Program.cs:93-120` — cookie configuration, 401/403 events.
- `src/Program.cs:133-134` — `SecurityStampValidatorOptions.ValidationInterval = 2 min`.
- `src/Infrastructure/Authorization/AuthorizationPolicies.cs:35-60` — the three policies.
- `src/Infrastructure/Persistence/AppDbContext.cs:19-46` — `IdentityDbContext<ApplicationUser>`, auto-discovery.
- `src/Infrastructure/Persistence/Configurations/ApplicationUserConfiguration.cs:12-32` — config style to mirror.
- `src/Infrastructure/Persistence/Migrations/20260831183430_AddIdentitySchema.cs:40-66,220-245` — `Up`/`Down` shape.
- `src/Application/Members/MemberAdminEndpoints.cs:37-43` — `MemberSummary` projection.
- `src/Infrastructure/Members/MemberQuery.cs:27-76` — the admin list query.
- `src/Application/Notifications/IEmailSender.cs:8-12` — the email seam.
- `src/Application/Notifications/OutboxEnqueuer.cs:13-48` — how a message is queued.
- `src/Application/Notifications/AccountApprovedNotification.cs:19-58` — transactional-email template to copy.
- `src/Infrastructure/Notifications/AcsEmailSender.cs:44-101` — ACS sender; unconfigured behaviour at `:55-61`.
- `src/Infrastructure/Notifications/OutboxDeliveryWorker.cs:18-320` — delivery loop.
- `src/appsettings.json:9-28` — every config key that exists today.

**Frontend**

- `src/app/src/app/app.routes.ts:24-28` — public vs guarded routes; insertion point.
- `src/app/src/app/core/auth/auth.service.ts:19-111` — session signals and API calls.
- `src/app/src/app/core/auth/auth.models.ts:5-41` — DTO mirrors of the backend contracts.
- `src/app/src/app/core/auth/auth.interceptor.ts:14,30-33` — global 401 handling and its allow-list.
- `src/app/src/app/features/auth/register/register.ts:14-15,33-101` — the form pattern to copy.
- `src/app/src/app/features/auth/register/register.spec.ts:27-136` — the spec pattern to copy.
- `src/app/src/app/features/auth/login/login.html:45` — where the "forgot password" link belongs.
- `src/app/src/app/core/admin/member-admin.models.ts:32-47` — admin `Member` model.
- `src/app/src/styles.scss:194-375` — shared `.panel/.field/.button/.alert` classes.
- `src/app/src/app/app.html:6-45` — nav; needs a profile entry.

**External documentation (Context7, `/dotnet/aspnetcore.docs`)**

- Reset-token lifespan is customised by adding a `DataProtectorTokenProvider` subclass to
  `options.Tokens.ProviderMap` and pointing `options.Tokens.PasswordResetTokenProvider` at it —
  relevant because the default lifespan is 1 day, which is long for a reset link.
- Microsoft's own `IEmailSender<TUser>` splits `SendPasswordResetLinkAsync` from
  `SendPasswordResetCodeAsync` — link-in-URL vs short-code are the two shapes, and the choice drives
  whether a frontend base URL is needed at all.
- The built-in Identity API endpoints use `{ email, resetCode, newPassword }` as the reset payload —
  a reasonable contract shape to mirror even though this app hand-rolls its endpoints.
- Reset tokens contain characters unsafe in a URL; they must be carried as a **query-string value**,
  never a path segment, and decoded before `ResetPasswordAsync`.

## Architecture Insights

- **Contract mirroring is manual and load-bearing.** Backend `record`s and Angular `interface`s are
  kept in step by hand and by comment ("a CONTRACT the SPA mirrors",
  `MemberAdminEndpoints.cs:11-12`). `MIN_PASSWORD_LENGTH = 8` in `register.ts:14` duplicates
  `options.Password.RequiredLength` in `Program.cs:75`. Every new field multiplies this by one.
- **Errors are a closed vocabulary, not free text.** `record XxxFailure(string Reason)` with stable
  snake_case codes, switched on in the SPA and mapped onto individual form controls. No
  ProblemDetails, no exception-handler middleware — `Results.Json(..., statusCode: N)` throughout.
- **Read models are explicit projections.** `AsNoTracking()` + `.Select(...)` everywhere; nothing
  leaks by adding an entity property, which is safe but means every surface is an edit.
- **The query seam keeps Application EF-free.** `I<Name>Query` declared in `Application`, implemented
  in `Infrastructure` against `AppDbContext`. Any new read of profile fields follows the same split.
- **Enumeration resistance is an established habit.** Login returns a banner rather than per-field
  errors precisely so it does not disclose which field was wrong (`login.ts:62-63`,
  `auth-identity-foundation/plan.md:362-364`), and the F-02 impl-review flagged a *timing* oracle in
  the same endpoint (`reviews/impl-review.md:93-101`). Registration, by contrast, deliberately does
  disclose `email_taken`. A forgot-password endpoint sits on the login side of that line.
- **Notifications are rendered at enqueue, delivered as bytes.** No re-rendering on retry, so attempt
  3 says what attempt 1 said. Any reset email must be composed before the outbox row is written.

## Historical Context (from prior changes)

- `context/foundation/roadmap.md:281-291`, `:61`, `:312` — **S-13 `member-profile-edit`, status
  `ready`**, outcome *"user can edit their display name and change their password"*, prerequisites
  F-02 + S-01, PRD ref FR-006. The change ID matches this folder exactly.
- `context/foundation/prd.md:82-83` — **FR-006** (must-have): *"Member can edit their display name and
  change their password."* With the recorded resolution: *"REVISED — profile management trimmed to
  name + password for MVP."* No PRD requirement mentions phone, address, postal code, city, or
  password reset.
- `context/foundation/prd.md:132` — *"Personal data privacy: member data (names, emails, plans) is
  visible only to the admin and the member themselves; GDPR-baseline handling."*
- `context/foundation/prd.md:152` — *"Unauthenticated access: login/registration only."* A
  forgot/reset screen extends that public surface; it does not contradict it.
- `context/archive/2026-08-31-auth-identity-foundation/plan.md:85` — *"No password change or profile
  edit. S-09 (`member-profile-edit`, FR-006) owns those."*
- Same file, `:86-88` — *"No password reset, email confirmation, 2FA, external/social login, or
  account lockout tuning."* A **deferral**, not a rejection; `roadmap.md:114` says the same
  (*"Password change is its own slice (S-13), keeping this foundation minimal"*).
- `context/archive/2026-09-01-registration-and-approval/plan.md:245-252` — registration has always
  been `(Email, Password, DisplayName)`. A `context/`-wide search for `FirstName` / `LastName` /
  `imię` / `nazwisko` matches **only this slice's own `change.md`** — no prior decision ever split
  the name.
- `context/archive/2026-09-01-member-management/plan-brief.md:50-53` — S-02 explicitly excluded *"any
  schema change or migration"*; the admin surface has never carried profile fields.
- `context/archive/2026-08-31-notification-delivery-foundation/plan-brief.md:30-41` — `IEmailSender`
  behind ACS keeps an SMTP fallback a one-class swap; each new email type rides the proven outbox
  (*"S-05 then adds cancellations to a proven pipeline"*); CI uses fake channels with one manual real
  send. ACS managed-domain deliverability risk is on record at `:86`.
- `context/archive/2026-08-31-persistence-foundation/plan-brief.md:102` — a reversible `Down` is a
  merge requirement, because rollback redeploys the artifact without rolling back schema.
- `context/foundation/lessons.md:5-29` — record necessary adaptations **in `plan.md`**, not only in a
  deploy log. This research turned up a live instance of exactly that failure: F-02's plan omits
  `AddDefaultTokenProviders()` while the shipped code has it, which nearly produced a false "token
  providers are missing" finding here.
- `context/archive/2026-08-31-auth-identity-foundation/reviews/impl-review.md:141-158` — the
  `PasswordSignInAsync` → `CheckPasswordSignInAsync` adaptation: check status *before* anything
  grants a session effect. Directly applicable to reset and change-password ordering.

## Related Research

No prior `research.md` exists for any auth or member slice — the archived auth and registration
changes contain `plan-brief.md` / `plan.md` / `reviews/` only. This is the first research artifact on
this surface.

## Open Questions

Ordered by how much they change the plan.

1. **What are "first name" and "last name"?** The model has a single `DisplayName`; nothing in the
   repo has ever split it. Three ways out: (a) keep `DisplayName` and read the requirement as "the
   name is not editable"; (b) add `FirstName`/`LastName` now and derive or retire `DisplayName`,
   which is a data migration over existing rows and touches every read projection and template;
   (c) add the new fields and leave the name question to a later slice. **This is the single biggest
   fork in the plan and needs an answer before `/10x-plan`.**

2. **"Required in the form, nullable in the DB" — what does the API enforce?** If `POST /register`
   rejects a blank postal code, then every *existing* member (including the seeded admin) has NULLs,
   and the profile-edit endpoint must still accept them or those accounts can never save a profile
   again. Concretely: does the *edit* endpoint require the fields, or only the *registration*
   endpoint? A defensible default to plan against: enforce on registration, accept-and-prompt on
   edit, and treat DB nullability purely as backfill tolerance.

3. **"On the home page before login" — confirm this means the login screen.** `/` is guarded and
   redirects anonymous visitors to `/login`, so there is no pre-login home page to put a link on. If
   a genuine public landing page is wanted, that is separate scope.

4. **How is the reset link's absolute URL built?** No `BaseUrl` / `AppUrl` / `FrontendUrl` key exists
   in any `appsettings`. Two options: derive it from the incoming `HttpRequest` (zero config, but
   wrong behind a proxy and host-header dependent) or add a config key (explicit, needs an App
   Service setting in production). A third option removes the question entirely: **email a short
   reset code instead of a link**, which the member pastes into a form — Microsoft's own
   `IEmailSender<TUser>` offers exactly this split.

5. **How is the reset flow verified locally?** With ACS unconfigured, `AcsEmailSender` returns
   `Permanent("acs_not_configured")` and logs a warning — the mail goes nowhere and no console
   fallback exists. Either add a dev `IEmailSender` that writes the link to the log, or accept that
   this flow is only verifiable against real ACS credentials.

6. **Anti-abuse for `POST /forgot-password`.** The app has no rate limiter at all, and registration's
   own doc comment concedes *"There is no rate limiting and no CAPTCHA either"* (`AuthEndpoints.cs:120-121`)
   with admin approval as the sole mitigation — a mitigation that does not exist for forgot-password.
   Decide: add `AddRateLimiter` scoped to this endpoint, or accept the risk explicitly and record it.
   Related and cheaper: make the response identical (and ideally equal-cost) for known and unknown
   emails, per the enumeration discipline already established for login.

7. **Reset-token lifespan.** The default is 1 day. Shortening it means a custom
   `DataProtectorTokenProvider` in `options.Tokens.ProviderMap`. Is 1 day acceptable for MVP?

8. **Does this scope belong to S-13, and does the PRD need amending?** FR-006 was *deliberately
   trimmed* to name + password; address fields and password reset are outside both it and the
   roadmap's S-13 outcome. Either the PRD and roadmap are updated to record the expanded scope, or
   the address fields split into a separate slice. Leaving the PRD stating a narrower scope than what
   ships is exactly the drift `lessons.md` warns about.

9. **Phone and address validation.** Is `IdentityUser.PhoneNumber` reused or is a new property added?
   Are Polish formats enforced (postal code `NN-NNN`, phone digits / `+48`), and are they enforced on
   the server or only in the Angular form? What max lengths — the existing precedent is an explicit
   `HasMaxLength` on every string column.

10. **Personal-data footprint.** `prd.md:132` promises member data is visible only to the admin and
    the member, GDPR-baseline. Home addresses are a heavier category than names and emails. Does the
    admin member list gain these fields (it currently shows none), and does anything need saying
    about retention or the registration consent copy?
