<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Auth & Identity Foundation (F-02)

- **Plan**: context/changes/auth-identity-foundation/plan.md
- **Scope**: Full plan — Phases 1–4 (all four implemented; 33/47 Progress rows verified)
- **Date**: 2026-08-31
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 4 warnings, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | WARNING |

## Evidence gathered

Automated criteria re-run during this review:

| Check | Result |
|---|---|
| `dotnet build po-prostu-silka.slnx -c Release` | 0 warnings, 0 errors |
| `dotnet list package --vulnerable --include-transitive` | clean |
| `dotnet test po-prostu-silka.slnx` | 12/12 pass |
| `npm test` (Vitest) | 12/12 pass across 3 files |
| `npm run quality:check` | Prettier and ESLint both clean |
| Layering: `grep "using Microsoft.EntityFrameworkCore" src/Domain src/Application` | no matches — EF Core boundary intact |
| Migration `SchemaMarkers` check | no `DropTable`/`CreateTable` operand; only explanatory comments |

Scope discipline was checked against every item in "What We're NOT Doing" — no registration
endpoint, no login/register UI (the two route components are labelled stubs), no block/unblock
action, no password change/reset/confirmation/2FA/social login, no `SchemaMarkers` drop, no `.csproj`
split, no PWA/service worker. **No violations found.**

## Findings

### F1 — Admin seeder crashes the app on an unreachable or unmigrated database

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/Program.cs:117-124
- **Detail**: The seeder runs before `app.Run()` with no `try`/`catch`. `EnableRetryOnFailure` covers
  transient SQL faults but *not* an unmigrated schema (`RoleExistsAsync` throws "invalid object
  name", which is not transient and is never retried) nor a retry-exhausted outage. Either throws
  out of `SeedAsync`, goes unhandled, and the process dies **before** `UseAuthentication` runs or
  `/health` is mapped — so App Service sees a crash-loop rather than a live app reporting unhealthy.
  The missing-config path was handled deliberately and gracefully (`AdminSeeder.cs:35-44` logs and
  returns; the Program.cs comment says "an unseeded production app is a log line, not an outage"),
  but the more severe DB path got no equivalent treatment. The deploy pipeline migrates before
  deploying, which covers the ordinary deploy — it does not cover a transient DB outage during an
  App Service recycle, which happens without warning on this tier.
- **Fix**: Wrap the seeder call in `try`/`catch`, log the exception, and let startup continue so
  `/health` can report the outage instead of the process disappearing.
  - Strength: Makes the failure observable through the probe F-01 built for exactly this purpose,
    and matches the graceful posture already chosen one function down for missing config.
  - Tradeoff: A silently-unseeded app starts "successfully" — mitigated because `/health` still
    reports the DB failure, and the log carries the exception.
  - Confidence: HIGH — the control flow is unambiguous; nothing catches between `SeedAsync` and the
    host builder.
  - Blind spot: Not verified against a real App Service recycle mid-outage; reasoning is from code.
- **Decision**: FIXED — seeder call wrapped in try/catch in Program.cs; logs the exception and continues startup so /health can report the outage.

### F2 — Role-seeding failures are logged as success

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Infrastructure/Identity/AdminSeeder.cs:23-30 (and :78)
- **Detail**: `await roleManager.CreateAsync(new IdentityRole(role));` discards the returned
  `IdentityResult` and unconditionally logs `"Seeded missing role {Role}."`. A failure — a
  unique-constraint race on `RoleNameIndex` between overlapping startups, or any store error —
  is reported as success while the role does not exist. `RequireRole` in both authorization
  policies would then fail closed for every user, with the log actively pointing away from the
  cause. `AddToRoleAsync` on line 78 discards its result the same way. The admin-creation branch
  immediately below (`:67-76`) does this correctly, so the file is inconsistent with itself.
  **This is the same failure class as F1 in the persistence-foundation review** (the firewall
  cleanup's `|| echo` swallowing every delete error) — a recurring pattern, not a one-off.
- **Fix**: Check `.Succeeded` and log `.Errors` for both `CreateAsync` and `AddToRoleAsync`, mirroring
  the admin branch already in this file.
- **Decision**: SKIPPED — reviewer accepted the risk of a silently-failed role creation.

### F3 — Login timing reveals whether an email is registered

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/Application/Auth/AuthEndpoints.cs:47-62
- **Detail**: The response bodies are correctly identical for "unknown email" and "wrong password"
  — there is a test asserting exactly that. But the code paths are not: an unknown email returns
  immediately, while a known email always pays for `CheckPasswordSignInAsync`'s PBKDF2 verification.
  The measurable latency difference is a classic enumeration oracle, so the equal payloads buy less
  than they appear to. For this app the leak is "is this address a member of this gym", which is
  personal data under the PRD's privacy NFR, though the population is small and the attacker payoff
  is low.
- **Fix**: Equalise the work — verify the supplied password against a constant dummy hash when the
  user is null, so both branches perform one hash verification.
  - Strength: Closes the oracle in a few lines, entirely inside the null branch, with no change to
    the response contract S-01 consumes.
  - Tradeoff: Adds a deliberate ~100ms of work to a request that currently short-circuits, and the
    dummy hash needs a comment or a future reader will "optimise" it away.
  - Confidence: MEDIUM — the asymmetry is certain; whether it is worth closing at this app's threat
    level is a judgment call, not a fact.
  - Blind spot: Have not measured the actual delta on this hardware; Azure SQL latency variance may
    partly mask it in practice.
- **Decision**: SKIPPED — leak accepted: small member population, low attacker payoff.

### F4 — `/api/auth/me` makes two database round-trips the cookie already covers

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/Application/Auth/AuthEndpoints.cs:83-96, src/Infrastructure/Identity/AppUserClaimsPrincipalFactory.cs:23-31
- **Detail**: `GetCurrentUser` calls `GetUserAsync` (one query) and then `GetRolesAsync` (a join) on
  every call. But the claims factory already mints everything `CurrentUser` needs into the
  principal at sign-in and on every cookie refresh: id, email, roles, the status claim, and
  DisplayName. The SPA guard calls `/me` on every cold load, and the database is Azure SQL Basic
  (5 DTU) — the tightest resource in the stack, and the one the whole claims-over-query design was
  chosen to protect. The redundancy is at odds with the plan's own stated rationale.
- **Fix A ⭐ Recommended**: Drop the redundant `GetRolesAsync` and read roles from the principal's
  claims, keeping `GetUserAsync`.
  - Strength: Halves the cost while preserving the deleted-user-with-live-cookie check that
    `GetUserAsync` incidentally provides.
  - Tradeoff: Still one query per `/me`; does not reach the zero-query ideal.
  - Confidence: HIGH — the base `UserClaimsPrincipalFactory` adds a role claim per role; nothing
    else depends on the roles round-trip.
  - Blind spot: None significant.
- **Fix B**: Answer `/me` entirely from claims, with no database access at all.
  - Strength: Zero queries on the hottest auth path.
  - Tradeoff: Loses the deleted-user check until the 30-minute security-stamp revalidation catches
    it — a deliberate security/performance trade that should be made consciously, not by accident.
  - Confidence: MEDIUM — correct, but it widens a real (if narrow) window.
  - Blind spot: Have not confirmed how S-01/S-02 will expect `/me` to behave for a just-blocked user.
- **Decision**: FIXED via Fix A — /me now reads roles from the principal's claims; GetUserAsync kept as the deleted-user check. Login path unchanged (no principal available there).

### F5 — Plan specifies `PasswordSignInAsync`; implementation uses `CheckPasswordSignInAsync`

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: src/Application/Auth/AuthEndpoints.cs:56-72 (plan contract at plan.md:362)
- **Detail**: The plan's Phase 2 contract says login "uses `SignInManager.PasswordSignInAsync`". The
  code uses `CheckPasswordSignInAsync(..., lockoutOnFailure: true)` followed by a conditional
  `SignInAsync(user, isPersistent: true)`. The substitution is *necessary*, not sloppy:
  `PasswordSignInAsync` authenticates and issues the cookie atomically, so a pending or blocked
  account would receive a valid session cookie before the status check could refuse it — defeating
  the central access rule this change exists to enforce. Every stated success criterion is met and
  covered by passing tests. The defect is that the plan text is now wrong, and the three other
  in-flight adaptations were each documented with an "Adapted during implementation" note while this
  one was not.
- **Fix**: Add a one-line "Adapted" note to the plan's Phase 2 §4 contract recording why
  `PasswordSignInAsync` cannot be used.
- **Decision**: SKIPPED — plan text left stale; the code is correct.

### F6 — Auth guard has no in-flight de-duplication

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/app/src/app/core/auth/auth.guard.ts:29-31
- **Detail**: If two guarded navigations race before the first `/api/auth/me` resolves, both observe
  `sessionResolved() === false` and each fire their own request. Harmless today — the GET is
  idempotent and both converge — and there is only one guarded route. It becomes worth fixing once
  child routes land in S-01 and guards evaluate together.
- **Fix**: Cache the in-flight promise on `AuthService` so concurrent callers share one request.
- **Decision**: SKIPPED — harmless with one guarded route; revisit when S-01 adds child routes.

### F7 — A third of the success criteria are unverified, and one is a deploy blocker

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Success Criteria
- **Location**: N/A (plan.md `## Progress`)
- **Detail**: 33 of 47 rows are checked. Every runnable automated criterion passes. The 14 open rows
  are: nine that require a deploy (1.9, 1.11, 1.13, 2.9, 2.10, 3.5, 3.6, 3.7, 4.6); four deferred by
  explicit reviewer choice during Phase 2 despite evidence having been gathered (2.11, 2.12, 2.14,
  2.15); and **2.13, setting `AdminSeed__Email` / `AdminSeed__Password` in App Service**. 2.13 is not
  a bookkeeping item: without it the seeder logs an error and skips, leaving production with Identity
  tables, no admin account, and a `/health` that still reports `Healthy` — the failure is invisible
  to the probe. Nothing has been pushed, so production is still running the pre-Identity build.
- **Fix**: Set the two App Service settings, then push; the deploy closes nine rows in one pass and
  the four deferred rows can be marked from the evidence already recorded.
- **Decision**: PARTIALLY ADDRESSED — 2.11, 2.12, 2.14, 2.15 marked verified from Phase 2 evidence (37/47). Ten rows remain: nine deploy-dependent plus 2.13, which must be set before pushing.
