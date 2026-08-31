# Auth & Identity Foundation (F-02) — Plan Brief

> Full plan: `context/changes/auth-identity-foundation/plan.md`

## What & Why

Give the app an identity system: ASP.NET Core Identity for email + password, the PRD's
`pending / active / blocked` lifecycle as real schema, authorization policies that encode "active
account **and** role", and an admin seeded at setup who is never self-registered. Every authenticated
screen in S-01–S-09 sits on this, and the token strategy chosen here is expensive to reverse later.

## Starting Point

The app is live on Azure SQL with zero auth surface: `Program.cs` has no authentication middleware and
exactly two endpoints (`/health`, SPA fallback), `AppDbContext` derives from plain `DbContext`, and the
Angular app is an untouched `ng new` scaffold with an empty routes array and no HTTP client. There is no
test project anywhere in the repo — F-01 deliberately deferred it here.

## Desired End State

A seeded admin logs in from the browser and receives a long-lived auth cookie; `GET /api/auth/me`
returns their identity; anonymous, pending, and blocked callers are refused with proper status codes
rather than an HTML redirect; and `dotnet test` runs a real integration suite against SQL Server in CI.
S-01 can then build registration screens on top of working plumbing instead of inventing it.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Auth mechanism | Identity cookies, same-origin | The SPA is served from the API's own `wwwroot` (`outputMode: "static"`), so there is no CORS or cross-site cookie problem, and an HttpOnly cookie cannot be exfiltrated by XSS the way a stored JWT can. |
| Session lifetime | 30 days, sliding | An active member effectively never re-logs in, while a lost phone loses access within a month; sliding expiration is what makes 30 generous in practice. |
| Cookie `SameSite` | `Lax`, not `Strict` | F-03/S-05 email cancellation links back into the app — under `Strict` the cookie is withheld on that navigation and members land logged out on the exact link the product exists to deliver. |
| Authorization model | Named policies over role + status | The PRD's real rule is "active **and** role"; a policy states it once so nine slices annotate rather than re-derive it and eventually forget the status half. |
| Account status | Defined in F-02, driven by S-01 | Keeps schema in one migration and lets F-02's login actually enforce the access rule; S-01 adds behaviour, not columns. |
| Endpoints | `login`, `logout`, `me` | Exactly what the foundation needs to prove itself; registration stays with S-01, which owns the approval semantics. |
| User fields | `DisplayName`, `AccountStatus`, `CreatedAt` | One migration covers what S-01, S-02 and S-09 all need — S-02's member list is unusable without a name and a date. |
| Password policy | 8+ chars, no character-class rules | Length beats composition, and composition rules on a phone drive members to `Password1!` and to abandoning signup. |
| Admin seeding | Idempotent seeder at app startup | Works identically in Docker-local and Azure with no CI changes, reading its password from App Service app settings — the pattern `infrastructure.md:84` already establishes. |
| `SchemaMarker` | C# removed now, table dropped by F-03 | Honours the deferred-destructive policy on the first change that tests it; dropping both now would break `/health` on a rollback. |
| Test stack | xUnit + Testcontainers | Real SQL Server locking semantics, which S-04's no-overbooking work needs and which `AGENTS.md` already rejects SQLite for. |
| DB retry | `EnableRetryOnFailure` on, `/health` strict | Honours F-01's explicit handoff at the moment it named; login is the first real query surface on a throttling Basic tier. |

## Scope

**In scope:** Identity packages and schema; `ApplicationUser` + `AccountStatus`; `IdentityDbContext`
conversion and migration; cookie configuration; `login`/`logout`/`me`; `ActiveMember` and `Admin`
policies; password policy; idempotent admin seeder; connection resiliency; the repo's first test project
wired into CI; Angular HTTP client, auth service, interceptor, guard, and route structure.

**Out of scope:** registration endpoint and approval flow (S-01); login/registration UI (S-01);
block/unblock admin action (S-02); password change and profile edit (S-09); password reset, email
confirmation, 2FA, social login; dropping the `SchemaMarkers` table (F-03); PWA/service worker;
`.csproj` splitting.

## Architecture / Approach

Cookie-authenticated same-origin SPA. Angular calls relative `/api/...` paths from `wwwroot`; the browser
carries an HttpOnly `Lax` cookie automatically. Identity's cookie handler is reconfigured to return
`401`/`403` instead of redirecting to a Razor login page — without that override the redirect is followed
to the SPA fallback and the client sees `200 index.html` where it expected `401`. Account status travels
as a claim (populated by a custom claims-principal factory) so the `ActiveMember` policy needs no
per-request database round-trip on a 5-DTU tier, with a 30-minute security-stamp interval bounding
staleness.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Identity schema & DbContext | Identity tables, custom user columns, migration, retry, `SchemaMarker` C# removed | `DbContext` base-class change rewrites the model snapshot — the migration must not touch `SchemaMarkers` |
| 2. Auth, policies & admin seed | Cookie config, `login`/`logout`/`me`, policies, seeder | First phase to change live behaviour; the cookie-redirect override is easy to miss and fails silently |
| 3. Test project | xUnit + Testcontainers suite, wired into CI | Adds ~30-60s and a Docker dependency to every deploy run |
| 4. Angular auth plumbing | HTTP client, auth service, interceptor, guard, routes | Interceptor must not turn an expected `401` from `/login` or `/me` into a redirect loop |

**Prerequisites:** F-01 (done — Azure SQL provisioned, migrations run on deploy). App Service setting
`AdminSeed__Password` must be created before Phase 2 deploys.
**Estimated effort:** ~3-4 sessions across 4 phases; Phase 3 is the largest single lift because it builds
test infrastructure from nothing.

## Open Risks & Assumptions

- **Seeder idempotency has no automated test** — deliberately triaged out. It only fails on a *second*
  cold start, so it is invisible in development; manual verification step 5 is the compensating control.
- **`SchemaMarkers` depends on F-03 remembering to drop it.** Harmless if forgotten, but it becomes
  scaffolding masquerading as schema.
- **Blocking a signed-in user is not fully closed until S-02**, which owns the action that updates the
  security stamp — and S-02 is currently `blocked` on PRD Open Question 1.
- **The `Pending`/`Blocked` login response shape is a contract S-01 consumes**; changing it later means
  changing both.
- **Every push deploys to production** — each phase must leave the live site working, not merely leave
  the repo consistent.

## Success Criteria (Summary)

- The seeded admin logs in from a browser, stays signed in across a page reload, and sees their identity
  at `/api/auth/me`.
- A pending or blocked account is refused at login, and an anonymous request to a protected route gets a
  JSON `401` rather than the Angular shell.
- `dotnet test` passes in CI against a real SQL Server container, before anything touches the production
  database.
