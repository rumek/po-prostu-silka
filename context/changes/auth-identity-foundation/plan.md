# Auth & Identity Foundation (F-02) Implementation Plan

## Overview

Give the app an identity system. Wire ASP.NET Core Identity for email + password against the database
F-01 provisioned, model the PRD's `pending / active / blocked` account lifecycle as first-class schema,
express authorization as named policies that combine role *and* account status, seed the admin account
that is never self-registered, and stand up the repo's first test project.

This is roadmap item **F-02**, the second head of the dependency graph for milestone `first-usable-mvp`.
It ships the **authentication foundation, not the registration flow**: S-01 (`registration-and-approval`)
owns the register endpoint, the approval screen, and the admin's approve action. F-02 makes those
possible and proves the access rules hold.

## Current State Analysis

The app is live at `https://po-prostu-silka.azurewebsites.net`, backed by Azure SQL, with no identity
surface whatsoever.

- **`src/Program.cs`** (42 lines) registers OpenAPI, `AppDbContext`, and a DB-backed health check. There
  is **no `AddAuthentication`, no `AddIdentity`, no `UseAuthentication`/`UseAuthorization`**, and exactly
  two endpoints: `MapHealthChecks("/health")` and `MapFallbackToFile("index.html")`.
- **`src/po-prostu-silka.csproj`** carries four packages, all pinned `10.0.11`. No Identity package.
- **`src/Infrastructure/Persistence/AppDbContext.cs`** derives from plain `DbContext` and exposes a single
  `DbSet<SchemaMarker>`. Entity config is discovered via `ApplyConfigurationsFromAssembly` — the
  convention this change must follow rather than replace.
- **`src/Domain/`** contains only `SchemaMarker.cs`; **`src/Application/`** contains only `README.md`.
- **No test project exists anywhere** — no `*.Tests.csproj`, no `.sln`, no xunit/nunit/mstest reference.
- **The Angular app is an untouched `ng new` scaffold**: `app.routes.ts` is `export const routes: Routes = [];`,
  `app.config.ts` provides only `provideBrowserGlobalErrorListeners`, `provideRouter`, and
  `provideClientHydration`. No `provideHttpClient`, no interceptors, no guards, no environment files.
- **`.github/workflows/deploy.yml`** applies migrations before deploying and needs **no changes** for a new
  migration — `dotnet ef database update` operates on the whole model. It has **no test step**.

### Key Discoveries:

- **The SPA is same-origin with the API, and SSR is vestigial.** `src/app/angular.json` sets
  `"outputMode": "static"`, `app.routes.server.ts` declares `{ path: '**', renderMode: RenderMode.Prerender }`,
  and `deploy.yml` copies only `dist/app/browser/` into `wwwroot`. There is no Node SSR server at runtime.
  This is what makes cookie auth cheap: no CORS, no cross-site cookie flags, no token in JS-reachable
  storage, and no server-side request context to thread auth through.
- **No prior JWT-vs-cookie decision exists.** A sweep of `context/foundation/`, `context/changes/`, and
  `context/archive/` for "JWT" and "cookie" returned zero hits. `tech-stack.md:29` pins *Identity*, not a
  token strategy. Session lifetime was an explicit open Unknown at `roadmap.md:107` and is settled here.
- **`SchemaMarker` asks to be deleted by this change.** Its own doc comment: *"F-02 introduces the first
  real entities; when it does, this type and its migration should be removed rather than built upon."*
  But `AGENTS.md` and `infrastructure.md:85` require destructive changes to lag one release. Both cannot
  hold — resolved below by splitting the removal across two releases.
- **The test project is a documented inherited cost.** `context/archive/2026-08-31-persistence-foundation/plan.md:513-519`:
  *"Whichever plan stands the test project up should budget for it explicitly rather than discovering it
  mid-phase."* S-04's no-overbooking concurrency tests will need real SQL Server locking semantics —
  `AGENTS.md` already rejects SQLite for exactly this reason.
- **F-01 deferred `EnableRetryOnFailure` to this change by name** (`archived plan.md:550`): *"when the first
  real query surface lands in F-02"*. Login is that surface.
- **Identity packages exist at `10.0.11`**, matching the project's pinning convention — verified against
  `api.nuget.org` flat-container indexes for `Microsoft.AspNetCore.Identity.EntityFrameworkCore`.
- **Every push to `main` deploys to production.** `deploy.yml` triggers on push with no staging step, so
  each phase must leave the live site working — not merely leave the repo consistent.

## Desired End State

The database holds ASP.NET Core Identity tables plus an account-status column; the deployed app has a
seeded admin who can log in and receive an auth cookie; a protected endpoint rejects anonymous, pending,
and blocked callers; the Angular app carries the HTTP and routing plumbing that S-01's screens will use;
and `dotnet test` runs a real integration suite in CI.

**Verified by:**

1. `curl -c` against `POST /api/auth/login` with the seeded admin returns `200` and sets an auth cookie;
   `GET /api/auth/me` with that cookie returns the admin's email, display name, status, and roles.
2. `GET /api/auth/me` without a cookie returns `401` **as JSON**, not a 302 to a login page.
3. The integration suite passes: active logs in; pending and blocked are rejected; a policy-protected
   route returns 401 anonymous and 403 for the wrong role.
4. `curl https://po-prostu-silka.azurewebsites.net/health` still returns `200 Healthy` after deploy.
5. `dotnet ef migrations script <initial> <identity>` and its inverse both generate without error.

## What We're NOT Doing

- **No registration endpoint or approval flow.** `POST /api/auth/register`, the pending-approval screen,
  the admin's pending list, and the approve action are S-01's stated outcome (FR-001, FR-003).
- **No login or registration UI.** Phase 4 ships plumbing (interceptor, guard, service, route structure)
  with no screens. Screens are S-01.
- **No block/unblock admin action.** The `AccountStatus` column and its enforcement land here; the admin
  UI and the transition rules are S-02 (FR-004, FR-005) — which is `blocked` on PRD Open Question 1.
- **No password change or profile edit.** S-09 (`member-profile-edit`, FR-006) owns those.
- **No password reset, email confirmation, 2FA, external/social login, or account lockout tuning.**
  `roadmap.md:107` scopes F-02 to "email+password, roles, and the admin seed — no reset/confirmation
  flows beyond what registration needs."
- **No dropping of the `SchemaMarkers` table.** The C# type goes; the table is dropped by F-03 — see
  "Migration Notes".
- **No `.csproj` splitting of Domain/Application/Infrastructure.** Still folders, per F-01's decision.
- **No PWA / service worker / manifest.** The app is not installable yet; that belongs with F-03's push
  work, which needs the service worker for its own reasons.
- **No seeder-idempotency test.** Deliberately excluded during triage — recorded in "Open Risks" instead.
- **No migration reversibility test.** CI and manual verification already cover it.

## Implementation Approach

Four phases, ordered so each one deploys safely to a live site on its own.

**Phase 1 adds schema only.** Identity tables and the status column appear in the database; no endpoint,
middleware, or behaviour changes. If it deploys and something is wrong, the site behaves exactly as
before. This is also where `SchemaMarker`'s C# leaves and connection resiliency arrives.

**Phase 2 turns on authentication.** Cookie configuration, the three endpoints, the policies, and the
seeder. This is the phase that changes live behaviour, and it deploys into a database that already has
the schema waiting.

**Phase 3 stands up the test project** and proves Phase 2's rules mechanically, then wires `dotnet test`
into CI so later slices inherit a gate rather than build one.

**Phase 4 wires the Angular side** — the last phase because it consumes endpoints that must already exist.

**Why schema precedes behaviour.** Migrations run before `webapps-deploy` in the same job, so schema is
always ahead of or equal to code. Splitting schema (P1) from behaviour (P2) means a failure in either
phase leaves a coherent system rather than code pointing at absent tables.

## Critical Implementation Details

**Identity's cookie handler redirects to `/Account/Login` by default — it must return status codes
instead.** `AddIdentityCookies`/`AddCookie` sets `LoginPath` and issues a `302` on an unauthenticated
request. For an API consumed by fetch, that 302 is then followed to the SPA fallback, and the client
receives `200` with `index.html` instead of `401` — a failure that looks like a working request. Override
`options.Events.OnRedirectToLogin` and `OnRedirectToAccessDenied` to set `401`/`403` on
`context.Response` and return `Task.CompletedTask`.

**`SameSite=Lax`, not `Strict`.** Strict is the reflexive choice for a same-origin app, but F-03/S-05
send cancellation emails containing links back into the app; under `Strict` the auth cookie is withheld
on that cross-site top-level navigation and the member lands logged out on the exact link the product
exists to deliver. `Lax` sends the cookie on top-level GET navigation while still withholding it on
cross-site POST, which is the CSRF vector that matters. Pair it with `HttpOnly = true` and
`SecurePolicy = Always`.

**The status check must live inside the authorization policy, not only at login.** Checking status only
at login means an account blocked *after* signing in keeps a valid cookie for up to 30 days. The
`ActiveMember` policy re-evaluates the status claim on every request; whichever slice implements
block (S-02) must also invalidate the cookie via Identity's security stamp so the claim cannot go stale.
Set `SecurityStampValidationInterval` to a short window (30 minutes) so a blocked user is ejected
promptly rather than at the end of the sliding window.

**Middleware order in `Program.cs` is load-bearing.** `UseAuthentication()` must precede
`UseAuthorization()`, and both must precede `MapFallbackToFile("index.html")` — which F-01's comment
already marks as "must stay last". Auth middleware placed after the static-file middleware will not run
for API routes that the fallback claims first.

**`AppDbContext` changes base class, which rewrites the model snapshot.** Moving from `DbContext` to
`IdentityDbContext<ApplicationUser>` introduces seven Identity entities at once. Generate the migration
and read it before committing: it must contain only Identity tables plus the `AspNetUsers` custom
columns, and must not touch `SchemaMarkers`.

**The seeder runs on every cold start and must be genuinely idempotent.** App Service recycles without
warning, and Always On means the app restarts on its own schedule. Guard on "does a user with this email
already exist" — never on "is the table empty" — and never update an existing admin's password, or a
rotated password would be silently reverted on the next recycle.

## Phase 1: Identity Schema & DbContext

### Overview

Add the Identity packages, define `ApplicationUser` and the status enum, convert `AppDbContext` to
`IdentityDbContext`, generate the migration, enable connection resiliency, and retire `SchemaMarker`'s
C#. Schema and wiring only — no endpoints, no middleware, no behaviour change.

### Changes Required:

#### 1. Identity package reference

**File**: `src/po-prostu-silka.csproj`

**Intent**: Add the EF Core-backed Identity store. Only one package is needed — the core Identity
services and the cookie handler come with the ASP.NET Core shared framework.

**Contract**: One `PackageReference` for `Microsoft.AspNetCore.Identity.EntityFrameworkCore` pinned to
`10.0.11`, matching every existing pin. `Microsoft.AspNetCore.Identity.UI` is deliberately **not**
included — it ships Razor Pages scaffolding for a server-rendered login, which this SPA does not use.
Leave the `Remove="app\**"` item group untouched.

#### 2. Account status enum

**File**: `src/Domain/AccountStatus.cs` (new)

**Intent**: Model the PRD's locked three-state lifecycle as a domain type, so later slices reference a
type rather than magic strings.

**Contract**: `public enum AccountStatus { Pending = 0, Active = 1, Blocked = 2 }`, in
`po_prostu_silka.Domain`. Explicit numeric values — the column is persisted as `int`, so reordering the
members later would silently reinterpret existing rows. `Pending` is `0` so it is the default for any
row inserted without an explicit value.

#### 3. The application user

**File**: `src/Domain/ApplicationUser.cs` (new)

**Intent**: Extend Identity's user with the three fields the milestone needs, so S-01, S-02 and S-09 do
not each add an Identity migration.

**Contract**: `ApplicationUser : IdentityUser` (default `string` keys — conventional, best-documented,
and what every Identity sample assumes) with `DisplayName` (`string`, required), `AccountStatus Status`,
and `DateTimeOffset CreatedAt`.

**Layering note**: this type inherits from `IdentityUser`, which lives in
`Microsoft.AspNetCore.Identity` — *not* in `Microsoft.EntityFrameworkCore`. The AGENTS.md rule forbids
EF Core in `Domain`, and this does not violate it. Do not "fix" it by moving the type to
`Infrastructure`; the layering rule names EF Core specifically.

#### 4. Entity configuration

**File**: `src/Infrastructure/Persistence/Configurations/ApplicationUserConfiguration.cs` (new)

**Intent**: Configure the custom columns in the established per-entity configuration class, so
`ApplyConfigurationsFromAssembly` picks it up with no edit to `OnModelCreating`.

**Contract**: `IEntityTypeConfiguration<ApplicationUser>` setting `DisplayName` required with a
`MaxLength(100)`, `Status` required and stored as `int` with a default of `AccountStatus.Pending`, and
`CreatedAt` required. Add a non-unique index on `Status` — S-02's member list filters on it (FR-005).
Do not re-declare Identity's own keys, indexes, or table names.

#### 5. DbContext conversion

**File**: `src/Infrastructure/Persistence/AppDbContext.cs`

**Intent**: Give the context the Identity model while preserving the auto-discovery convention.

**Contract**: Change the base from `DbContext` to `IdentityDbContext<ApplicationUser>`. `OnModelCreating`
must call `base.OnModelCreating(modelBuilder)` **before** `ApplyConfigurationsFromAssembly` — the base
call is what builds the Identity model, and inverting the order drops it. Remove the
`DbSet<SchemaMarker>` property. Update the class doc comment, which currently promises the
auto-discovery convention, to note the Identity base.

#### 6. Retire the SchemaMarker C#

**Files**: `src/Domain/SchemaMarker.cs` (delete),
`src/Infrastructure/Persistence/Configurations/SchemaMarkerConfiguration.cs` (delete)

**Intent**: Remove the F-01 scaffolding now that real entities exist, as its own doc comment instructs —
while leaving the table in place so a rollback of this release still finds it.

**Contract**: Delete both files. Do **not** generate a `DropTable` for `SchemaMarkers`; the table is
dropped by F-03. The migration in step 8 must therefore contain no reference to it — if EF generates one,
the model snapshot is being regenerated from a stale state; investigate rather than hand-editing.

#### 7. Connection resiliency

**File**: `src/Program.cs`

**Intent**: Honour F-01's explicit handoff — retry transient Azure SQL failures now that a real query
surface (login) exists on a Basic-tier database that throttles under DTU pressure.

**Contract**: Add `sqlOptions => sqlOptions.EnableRetryOnFailure()` to the existing `UseSqlServer` call.
Leave the health check strict — it must still report `Unhealthy` on a genuine outage. Note in a comment
that the resulting execution strategy makes manually-managed transactions require
`CreateExecutionStrategy().Execute(...)`; S-04's booking transaction will need this.

#### 8. Identity migration

**Files**: `src/Infrastructure/Persistence/Migrations/*` (generated)

**Intent**: Create the Identity tables and the custom user columns in one migration.

**Contract**: `dotnet ef migrations add AddIdentitySchema -p src/po-prostu-silka.csproj -o Infrastructure/Persistence/Migrations`.
Read the generated file before committing: it must create the seven Identity tables (`AspNetUsers`,
`AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetRoleClaims`, `AspNetUserLogins`,
`AspNetUserTokens`) plus the three custom `AspNetUsers` columns, and must **not** drop `SchemaMarkers`.
Both `Up` and `Down` must be non-empty — a migration whose `Down` is empty violates the reversibility
policy (`infrastructure.md:101`) and must be regenerated, not hand-patched.

**Adapted during implementation.** EF *will* scaffold `DropTable("SchemaMarkers")` in `Up` (and a
matching `CreateTable` in `Down`) and warn about possible data loss. This is correct EF behaviour, not
a stale snapshot: removing the entity from the model is exactly what makes EF emit the drop, and there
is no way to regenerate it differently while the entity is gone. Delete both calls by hand and leave a
comment saying why. The `.Designer.cs` and model snapshot describe the *model*, not the operations, so
they remain valid and self-consistent — the database simply retains a table EF no longer tracks, which
is the intended end state.

### Success Criteria:

#### Automated Verification:

- Build passes with no new warnings: `dotnet build src/po-prostu-silka.csproj -c Release`
- No vulnerable packages: `dotnet list src/po-prostu-silka.csproj package --vulnerable --include-transitive`
- Restore still resolves only from nuget.org: `dotnet nuget list source` shows nuget.org alone
- Migration applies to a clean local container: `docker compose down -v && docker compose up -d` then `dotnet ef database update -p src/po-prostu-silka.csproj --connection "<dev connection string>"`. The `--connection` argument is **required**: `AppDbContextFactory` takes precedence over the host for every design-time command, so without it EF connects to the factory's LocalDB placeholder and fails. This is the same reason CI passes `--connection`.
- Migration is reversible: `dotnet ef migrations script AddIdentitySchema InitialSchemaMarker -p src/po-prostu-silka.csproj` generates without error
- Design-time factory still works without runtime config: `ASPNETCORE_ENVIRONMENT=Production dotnet ef migrations script --idempotent -p src/po-prostu-silka.csproj` succeeds
- The migration performs no DropTable on SchemaMarkers: `grep -n 'DropTable' -A1 src/Infrastructure/Persistence/Migrations/*_AddIdentitySchema.cs` shows no `SchemaMarkers` operand in `Up`, and `Down` contains no `CreateTable` for it (the name may still appear in explanatory comments)
- App still starts and `/health` returns 200 `Healthy` locally
- Deployed app still healthy: `curl https://po-prostu-silka.azurewebsites.net/health` returns 200 `Healthy`

#### Manual Verification:

- The seven Identity tables and the three custom `AspNetUsers` columns are visible in the local database via a SQL client
- The same tables are visible in Azure SQL after deploy
- The generated migration was read end to end before commit, and its `Down` genuinely reverses its `Up`
- `SchemaMarkers` still exists in both databases (it is dropped by F-03, not here)

**Implementation Note**: After completing this phase and all automated verification passes, pause here
for manual confirmation from the human that the manual testing was successful before proceeding to the
next phase.

---

## Phase 2: Authentication, Policies & Admin Seed

### Overview

Turn on authentication: cookie configuration tuned for long-lived mobile sessions and API semantics, the
three auth endpoints, the named authorization policies, the password policy, and the idempotent admin
seeder. This is the phase that changes live behaviour.

### Changes Required:

#### 1. Identity service registration and password policy

**File**: `src/Program.cs`

**Intent**: Register Identity against `AppDbContext` with a password policy tuned for members typing on
phones, and no confirmation requirement.

**Contract**: `AddIdentityCore<ApplicationUser>` (or `AddIdentity`) with `AddRoles<IdentityRole>()`,
`AddEntityFrameworkStores<AppDbContext>()`, and `AddSignInManager()`. Password options: `RequiredLength = 8`,
`RequireDigit`, `RequireLowercase`, `RequireUppercase`, `RequireNonAlphanumeric` all `false`,
`RequiredUniqueChars = 1`. `SignIn.RequireConfirmedAccount = false` — `roadmap.md:107` scopes out
confirmation flows and the admin-approval gate is the vetting mechanism. `User.RequireUniqueEmail = true`.

#### 2. Cookie configuration

**File**: `src/Program.cs`

**Intent**: Make the auth cookie long-lived for mobile, and make it behave like an API rather than a
server-rendered login.

**Contract**: `ExpireTimeSpan = TimeSpan.FromDays(30)`, `SlidingExpiration = true`,
`Cookie.HttpOnly = true`, `Cookie.SameSite = SameSiteMode.Lax`,
`Cookie.SecurePolicy = CookieSecurePolicy.Always`. Override `Events.OnRedirectToLogin` and
`Events.OnRedirectToAccessDenied` to set `401` / `403` respectively instead of redirecting — see
"Critical Implementation Details" for why this is load-bearing rather than cosmetic. Set
`SecurityStampValidationInterval` to 30 minutes so a status change takes effect without waiting out the
sliding window.

#### 3. Authorization policies

**File**: `src/Infrastructure/Authorization/AuthorizationPolicies.cs` (new) — registered from `Program.cs`

**Intent**: Encode the PRD's real access rule — *active account* **and** role — in one place, so nine
downstream slices annotate endpoints rather than re-deriving the rule and eventually forgetting the
status half.

**Contract**: Two named policies exposed as `const string` names so callers cannot typo them:
`ActiveMember` (authenticated, status claim `Active`, role `User` or `Admin`) and `Admin` (all of that
plus role `Admin`). Status is carried as a claim populated by a custom `IUserClaimsPrincipalFactory`
so the policy needs no database round-trip per request. Register the policies via
`AddAuthorizationBuilder`. These names are a contract later slices depend on — do not rename them.

#### 4. Auth endpoints

**File**: `src/Application/Auth/AuthEndpoints.cs` (new)

**Intent**: The minimum surface the SPA needs to establish, inspect, and end a session. Registration is
deliberately absent — S-01 owns it.

**Contract**: A minimal-API endpoint group mapped at `/api/auth`:
- `POST /login` — takes email + password; uses `SignInManager.PasswordSignInAsync`; returns `401` for
  bad credentials **and** for `Pending` / `Blocked` accounts, with a body distinguishing the two cases so
  S-01 can render the awaiting-approval screen. Do not reveal whether the email exists.
- `POST /logout` — requires authentication; signs out; returns `204`.
- `GET /me` — requires authentication; returns email, display name, status, and roles.

This file lives in `Application` and must not reference EF Core (it uses Identity's managers, which is
permitted — see the layering note in Phase 1).

#### 5. Middleware wiring

**File**: `src/Program.cs`

**Intent**: Put authentication and authorization into the pipeline in the only order that works.

**Contract**: `app.UseAuthentication()` then `app.UseAuthorization()`, both **after** `UseStaticFiles()`
and **before** `MapHealthChecks` / the auth endpoint group / `MapFallbackToFile("index.html")`. The
existing comment marking `MapFallbackToFile` as "must stay last" still holds. `/health` stays anonymous.

#### 6. Admin seeder

**File**: `src/Infrastructure/Identity/AdminSeeder.cs` (new), invoked from `src/Program.cs`

**Intent**: Guarantee an admin exists in every environment without ever self-registering one, reading
its credentials from configuration exactly as `infrastructure.md:84` prescribes for secrets.

**Contract**: On startup, in a scoped service: ensure the `User` and `Admin` roles exist; then, if no
user with the configured admin email exists, create one with `Status = Active`, `EmailConfirmed = true`,
and the `Admin` role. If the user already exists, **do nothing** — never update the password, or a
rotated credential is silently reverted on the next App Service recycle. Configuration keys
`AdminSeed:Email` and `AdminSeed:Password`, supplied in production as App Service app settings
(`AdminSeed__Email`, `AdminSeed__Password`) and locally via `appsettings.Development.json`. If the
password key is absent in Production, log an error and skip seeding rather than throwing — a missing
setting must not take the site down. Log whether it created or found the admin, but **never log the
password**.

#### 7. Local development configuration

**Files**: `src/appsettings.json`, `src/appsettings.Development.json`

**Intent**: Document the configuration keys without committing a production credential.

**Contract**: `appsettings.json` gains an `AdminSeed` section with an empty `Email`/`Password` and a
`//AdminSeed` comment-key explaining that production supplies these as App Service app settings —
mirroring the existing `//Default` connection-string pattern. `appsettings.Development.json` gains a
development-only admin email and password, clearly commented as local-only, following the precedent set
by the committed docker-compose SA password.

### Success Criteria:

#### Automated Verification:

- Build passes with no new warnings: `dotnet build src/po-prostu-silka.csproj -c Release`
- App starts locally and `/health` still returns 200 `Healthy`
- Seeded admin can log in: `POST /api/auth/login` with the dev admin credentials returns 200 and sets a cookie
- Session round-trips: `GET /api/auth/me` with that cookie returns the admin's email, `Active` status, and `Admin` role
- Anonymous access is rejected as JSON, not a redirect: `GET /api/auth/me` without a cookie returns `401` and a `content-type` that is not `text/html`
- Logout works: `POST /api/auth/logout` returns 204, and `/me` with the same cookie then returns 401
- Bad credentials are rejected: login with a wrong password returns 401
- SPA fallback still works: an arbitrary unmapped route returns 200 with the Angular shell
- Deployed app healthy after deploy: `curl https://po-prostu-silka.azurewebsites.net/health` returns 200 `Healthy`
- Deployed login works against Azure SQL with the App Service-supplied admin credentials

#### Manual Verification:

- The auth cookie in browser devtools shows `HttpOnly`, `Secure`, `SameSite=Lax`, and a ~30-day expiry
- Restarting the app twice does not create a second admin row (`SELECT COUNT(*) FROM AspNetUsers`)
- The `AdminSeed__Password` App Service setting is set, is not in the repo or shell history, and is stored in a password manager
- No password value appears in application logs or the deploy run log
- A user row manually set to `Pending` or `Blocked` in the database is refused at login with the distinguishing response

**Implementation Note**: After completing this phase and all automated verification passes, pause here
for manual confirmation from the human that the manual testing was successful before proceeding to the
next phase.

---

## Phase 3: Test Project

### Overview

Stand up the repo's first test project — xUnit with Testcontainers and `WebApplicationFactory` — and use
it to assert Phase 2's access rules mechanically. Wire `dotnet test` into CI so later slices inherit a
gate rather than build one. This is the inherited cost F-01 explicitly handed here.

### Changes Required:

#### 1. Test project

**Files**: `tests/po-prostu-silka.Tests/po-prostu-silka.Tests.csproj` (new), `po-prostu-silka.sln` (new, repo root)

**Intent**: Create the test project the whole milestone will grow into, and a solution file so
`dotnet test` and `dotnet build` at the root resolve both projects.

**Contract**: xUnit test project targeting `net10.0`, referencing the web project. Packages:
`xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`,
`Microsoft.AspNetCore.Mvc.Testing` (pinned `10.0.11`), and `Testcontainers.MsSql`. Placed in a top-level
`tests/` folder — the web project's `Remove="app\**"` item group only excludes the Angular tree, so a
sibling `tests/` directory needs no csproj change. Add a `.sln` referencing both projects; there is none
today. Pin every package version explicitly, matching repo convention.

#### 2. Integration test fixture

**File**: `tests/po-prostu-silka.Tests/IntegrationTestFixture.cs` (new)

**Intent**: Give tests a real SQL Server and a real app host, because the rules under test are about
Identity's behaviour against an actual schema — not something fakes can assert.

**Contract**: An xUnit collection fixture that starts an `MsSqlContainer` once per run, applies
migrations to it, and exposes a `WebApplicationFactory<Program>` overriding the `Default` connection
string to point at the container. Real SQL Server rather than SQLite or the in-memory provider — the
same reasoning `AGENTS.md` records, and what S-04's concurrency tests will need. `Program.cs` needs a
`public partial class Program { }` declaration at its end so `WebApplicationFactory<Program>` can
reference the implicit entry-point class.

#### 3. Auth behaviour tests

**File**: `tests/po-prostu-silka.Tests/AuthEndpointTests.cs` (new)

**Intent**: Assert the PRD's Access Control rules directly — the invariants whose silent breakage would
compromise every later slice.

**Contract**: Tests covering: an `Active` user logs in successfully; a `Pending` user is refused; a
`Blocked` user is refused; `GET /api/auth/me` returns `401` when anonymous; an `Admin`-policy endpoint
returns `403` for a plain `User`; and `/me` returns the expected claims after login. Seed each test's
users through Identity's `UserManager` rather than raw SQL, so password hashing matches production.

#### 4. CI test step

**File**: `.github/workflows/deploy.yml`

**Intent**: Make the suite a gate rather than a local habit, and fail the pipeline before anything
touches the production database.

**Contract**: A `dotnet test` step inserted after `Setup .NET` and **before** `Publish API` — so a
failing test aborts the run before migrations are applied and before deploy. `ubuntu-latest` ships a
running Docker daemon, so Testcontainers works with no extra setup. Note the added run time (~30-60s for
container startup) in the step name so it is not mistaken for a hang.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build po-prostu-silka.sln -c Release`
- Test suite passes locally: `dotnet test po-prostu-silka.sln`
- Tests genuinely exercise a container: the run logs show an `mssql` container starting and stopping
- The suite fails when it should: temporarily flipping the seeded test user to `Blocked` makes the active-login test fail (revert after confirming)
- CI runs the tests: the Actions run log shows the test step passing before `Publish API`
- A failing test aborts before migrations: confirmed by the step ordering in the run log
- Deployed app still healthy after the CI change: `curl https://po-prostu-silka.azurewebsites.net/health` returns 200 `Healthy`

#### Manual Verification:

- The test project's package versions are pinned, matching repo convention
- Test run time is acceptable for the auto-deploy-on-merge flow (note the actual duration)
- No test writes to the developer's docker-compose database — the container is isolated

**Implementation Note**: After completing this phase and all automated verification passes, pause here
for manual confirmation from the human that the manual testing was successful before proceeding to the
next phase.

---

## Phase 4: Angular Auth Plumbing

### Overview

Give the SPA the HTTP and routing infrastructure S-01's screens will consume: an HTTP client that sends
credentials, an interceptor that reacts to 401, an auth service holding session state, a route guard, and
a real route structure. No login or registration UI — those are S-01's.

### Changes Required:

#### 1. HTTP client and app structure

**Files**: `src/app/src/app/app.config.ts`, `src/app/src/app/app.routes.ts`

**Intent**: Provide the HTTP client with cookie credentials enabled and replace the empty routes array
with a structure S-01 can slot screens into.

**Contract**: `app.config.ts` adds `provideHttpClient(withFetch(), withInterceptors([authInterceptor]))`.
Because the SPA is served same-origin from `wwwroot`, no API base URL and no `withCredentials` cross-site
configuration is needed — relative `/api/...` paths carry the cookie automatically. `app.routes.ts` gains
a placeholder authenticated route protected by the guard, so the guard is exercised; S-01 replaces the
placeholder with real screens.

#### 2. Auth service

**File**: `src/app/src/app/core/auth/auth.service.ts` (new)

**Intent**: Own session state for the SPA — who is signed in, with what status and roles — so guards and
future screens read one source rather than each calling `/me`.

**Contract**: An injectable service exposing the current user as a signal (matching the codebase's
existing signal usage in `app.ts`), with `login()`, `logout()`, and a `loadCurrentUser()` that calls
`GET /api/auth/me` and tolerates a `401` by resolving to "not signed in". Establishes the `core/`
folder convention — the flat scaffold has no structure today.

#### 3. Auth interceptor

**File**: `src/app/src/app/core/auth/auth.interceptor.ts` (new)

**Intent**: React to session expiry in one place instead of in every future screen.

**Contract**: A functional `HttpInterceptorFn` that passes requests through and, on a `401` response,
clears the auth service's user state and redirects to the login route. It must **not** intercept the
`/api/auth/login` and `/api/auth/me` calls into a redirect loop — a 401 from those is an expected answer,
not a session expiry.

#### 4. Route guard

**File**: `src/app/src/app/core/auth/auth.guard.ts` (new)

**Intent**: Keep unauthenticated users out of app routes, satisfying the PRD's "all app content requires
an active account".

**Contract**: A functional `CanActivateFn` that resolves the current user (awaiting `loadCurrentUser()`
on first navigation so a page reload with a valid cookie does not bounce to login) and redirects to the
login route otherwise. Status-specific routing — sending a `Pending` user to the awaiting-approval
screen — is S-01's; the guard only needs to expose the status for S-01 to branch on.

#### 5. Vitest specs

**File**: `src/app/src/app/core/auth/auth.interceptor.spec.ts`, `auth.guard.spec.ts` (new)

**Intent**: Lock in the redirect-on-401 and guard-redirect behaviour before screens depend on them.

**Contract**: Specs using the existing `@angular/build:unit-test` (Vitest) setup — no new test tooling.
Cover: the interceptor redirects on a 401 from a normal request; it does *not* redirect on a 401 from
`/api/auth/login` or `/api/auth/me`; the guard allows an authenticated user and redirects an anonymous one.

### Success Criteria:

#### Automated Verification:

- Frontend quality gate passes: `npm run quality:check` from `src/app/`
- Angular builds: `npm run build` from `src/app/`
- Vitest suite passes: `npm test` from `src/app/`
- The stock scaffold spec still passes (it is not broken by the routing change)
- Full stack runs locally: app serves, and an authenticated fetch from the browser to `/api/auth/me` returns the seeded admin
- Deployed app healthy and SPA serving: `curl https://po-prostu-silka.azurewebsites.net/health` returns 200 `Healthy` and `/` returns the Angular shell

#### Manual Verification:

- Logging in via devtools/fetch, then reloading the page, keeps the session — the guard does not bounce a valid cookie to login
- A manually expired or deleted cookie triggers the interceptor's redirect exactly once, with no redirect loop
- The `core/auth/` folder structure is a sensible base for S-01 to extend

**Implementation Note**: This is the final phase. After it passes, F-02 is done and S-01
(`registration-and-approval`) and S-09 (`member-profile-edit`) are unblocked.

---

## Testing Strategy

This is the change that stands up testing for the whole project, per F-01's handoff. The strategy is
deliberately narrow: assert the access rules, not the framework.

### Integration Tests (Phase 3, xUnit + Testcontainers):

- `Active` user logs in successfully and receives an auth cookie
- `Pending` user is refused at login, with a response distinguishable from bad credentials
- `Blocked` user is refused at login
- `GET /api/auth/me` returns `401` when anonymous — and returns JSON, not the SPA shell
- An `Admin`-policy endpoint returns `403` for a plain `User` and `200` for an `Admin`
- `/me` returns the expected email, display name, status, and roles after login

### Unit Tests (Phase 4, Vitest):

- The interceptor redirects on `401` from a normal request
- The interceptor does *not* redirect on `401` from `/api/auth/login` or `/api/auth/me`
- The guard admits an authenticated user and redirects an anonymous one

### Manual Testing Steps:

1. `docker compose down -v && docker compose up -d` for a genuinely empty database
2. `dotnet ef database update` — confirm the Identity tables appear and `SchemaMarkers` survives
3. `dotnet run`, then `POST /api/auth/login` with the dev admin — expect 200 and a cookie
4. `GET /api/auth/me` with the cookie — expect the admin's claims; without it — expect a JSON 401
5. Restart the app twice; `SELECT COUNT(*) FROM AspNetUsers` must remain 1
6. Manually set that user's `Status` to `2` (Blocked) and retry login — expect refusal
7. Inspect the cookie in devtools: `HttpOnly`, `Secure`, `SameSite=Lax`, ~30-day expiry
8. After each phase deploys, repeat steps 3-4 against the live URL

**What this does not cover**: seeder idempotency is verified manually (step 5) rather than by an
automated test — a deliberate triage decision recorded in "Open Risks" below.

## Performance Considerations

- **Claims-based status checks avoid a per-request database round-trip.** The `ActiveMember` policy reads
  a claim rather than querying `AspNetUsers`, which matters on a 5-DTU Basic tier where every avoidable
  query is real headroom. The cost is staleness, bounded by the 30-minute security-stamp validation
  interval.
- **The auth cookie adds bytes to every request.** Identity's cookie with a few claims is well under the
  practical limit; if later slices add many claims, watch for the cookie approaching 4KB, at which point
  the handler silently chunks it across multiple cookies.
- **Testcontainers adds ~30-60s to every CI run.** Acceptable for auto-deploy-on-merge at this scale, but
  it is the first real fixed cost added to the pipeline — note the actual duration in Phase 3 so a future
  change can judge whether it has grown.

## Migration Notes

- **The `SchemaMarkers` table is deliberately left behind by this change.** The C# type and its
  configuration are deleted in Phase 1; the table is dropped by **F-03**
  (`notification-delivery-foundation`), one release later. This satisfies `infrastructure.md:85` and
  `AGENTS.md`: rollback redeploys the previous artifact but does not roll back schema, so a destructive
  change must lag one release behind the code that stops needing it. **F-03's plan must include
  `migrationBuilder.DropTable("SchemaMarkers")` with a matching `CreateTable` in its `Down`** — this is a
  handoff, and the only thing that closes it is F-03 remembering. **EF will not generate that drop
  for F-03**: the entity left the model in F-02, so from EF's perspective the table is already gone.
  F-03 must add an empty migration and hand-write both directions.
- The Identity migration is additive only — no existing data, no existing schema to reshape.
- Every migration must have a working `Down`; reversibility is a merge requirement (`infrastructure.md:101`).

## Open Risks & Assumptions

- **Seeder idempotency is not covered by an automated test.** Triage deliberately scoped it out. The
  failure mode is invisible in development because it only manifests on a second cold start — if a
  duplicate-admin row ever appears in production, this is the first place to look. Manual step 5 is the
  compensating control.
- **`SchemaMarkers` depends on F-03 remembering to drop it.** If F-03 is re-planned or reordered, the
  orphan table persists silently. It is harmless, but it is scaffolding masquerading as schema.
- **Blocking a signed-in user is not fully closed until S-02.** The `ActiveMember` policy re-checks the
  status claim, and the 30-minute security-stamp interval bounds staleness — but the block *action* that
  updates the security stamp is S-02's, which is currently `blocked` on PRD Open Question 1.
- **`AddIdentityCore` vs `AddIdentity` affects which cookie schemes are registered.** If the endpoints
  behave but the cookie never appears, this is the likely cause — `AddIdentityCore` does not register the
  cookie handler by itself.
- **The `Pending`/`Blocked` login response shape is a contract S-01 depends on.** It is defined here and
  consumed there; changing it later means changing both.

## References

- Roadmap item: `context/foundation/roadmap.md:97-109` — F-02, milestone `first-usable-mvp`
- Access control rules: `context/foundation/prd.md:144-152`; FR-001/FR-002 at `prd.md:72-75`
- Platform, secrets, and rollback policy: `context/foundation/infrastructure.md:84-86`
- Predecessor and its handoffs: `context/archive/2026-08-31-persistence-foundation/plan.md:513-519,550`
- Current entry point: `src/Program.cs`
- Current context: `src/Infrastructure/Persistence/AppDbContext.cs`
- Existing pipeline: `.github/workflows/deploy.yml`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Identity Schema & DbContext

#### Automated

- [x] 1.1 Build passes in Release with no new warnings
- [x] 1.2 No vulnerable packages
- [x] 1.3 Restore resolves only from nuget.org
- [x] 1.4 Migration applies to a clean local container
- [x] 1.5 Migration is reversible
- [x] 1.6 Design-time factory works without runtime config
- [x] 1.7 Migration does not drop SchemaMarkers
- [x] 1.8 App starts and local /health returns 200 Healthy
- [ ] 1.9 Deployed /health returns 200 Healthy

#### Manual

- [x] 1.10 Identity tables and custom columns visible in local database
- [ ] 1.11 Identity tables visible in Azure SQL after deploy
- [x] 1.12 Generated migration read end to end; Down reverses Up
- [ ] 1.13 SchemaMarkers table still present in both databases

### Phase 2: Authentication, Policies & Admin Seed

#### Automated

- [ ] 2.1 Build passes in Release with no new warnings
- [ ] 2.2 App starts locally and /health returns 200 Healthy
- [ ] 2.3 Seeded admin can log in and receives a cookie
- [ ] 2.4 GET /api/auth/me round-trips the session
- [ ] 2.5 Anonymous /me returns JSON 401, not an HTML redirect
- [ ] 2.6 Logout returns 204 and invalidates the cookie
- [ ] 2.7 Bad credentials return 401
- [ ] 2.8 SPA fallback still works
- [ ] 2.9 Deployed /health returns 200 Healthy
- [ ] 2.10 Deployed login works against Azure SQL

#### Manual

- [ ] 2.11 Cookie shows HttpOnly, Secure, SameSite=Lax, ~30-day expiry
- [ ] 2.12 Two restarts do not create a second admin row
- [ ] 2.13 AdminSeed__Password set in App Service, stored durably, absent from repo
- [ ] 2.14 No password value in application or deploy logs
- [ ] 2.15 Pending and Blocked users refused at login with the distinguishing response

### Phase 3: Test Project

#### Automated

- [ ] 3.1 Solution builds in Release
- [ ] 3.2 Test suite passes locally
- [ ] 3.3 Run logs show an mssql container starting and stopping
- [ ] 3.4 Suite fails when it should (deliberate break, then revert)
- [ ] 3.5 CI runs the tests before Publish API
- [ ] 3.6 A failing test aborts before migrations are applied
- [ ] 3.7 Deployed /health returns 200 Healthy

#### Manual

- [ ] 3.8 Test project package versions pinned per repo convention
- [ ] 3.9 Test run duration acceptable for auto-deploy-on-merge, and recorded
- [ ] 3.10 Tests do not write to the developer's docker-compose database

### Phase 4: Angular Auth Plumbing

#### Automated

- [ ] 4.1 npm run quality:check passes
- [ ] 4.2 Angular builds
- [ ] 4.3 Vitest suite passes
- [ ] 4.4 Stock scaffold spec still passes
- [ ] 4.5 Authenticated browser fetch to /api/auth/me returns the seeded admin
- [ ] 4.6 Deployed /health returns 200 Healthy and / returns the Angular shell

#### Manual

- [ ] 4.7 Page reload with a valid cookie keeps the session
- [ ] 4.8 Expired cookie triggers exactly one redirect, no loop
- [ ] 4.9 core/auth/ structure is a sensible base for S-01
