<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Registration and approval (S-01)

- **Plan**: `context/changes/registration-and-approval/plan.md`
- **Scope**: Full plan — Phases 1–3 of 3
- **Date**: 2026-09-01
- **Verdict**: NEEDS ATTENTION → RESOLVED (7 fixed, 1 accepted as risk, 1 skipped)
- **Findings**: 0 critical, 4 warnings, 5 observations (F9 added during triage)

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | WARNING |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

## Success criteria — verified

All six automated checks were re-run during this review:

| Check | Result |
|---|---|
| `dotnet build` (src/) | 0 warnings, 0 errors |
| `dotnet test` | 54/54 passed |
| Double-approve writes exactly one outbox row | `MemberAdminEndpointTests.Approving_twice_queues_exactly_one_email` passes (serialised case only — see F1) |
| `npm run quality:check` | Prettier + ESLint clean |
| `npm test` | 56/56 across 11 files |
| `npm run build` | clean |

Manual criteria 1.13–1.15, 2.10–2.13 and 3.6–3.11 were confirmed by the user, including a real approval email delivered to a real inbox on the deployed site. `origin/main` is at `96210d6`, consistent with the claimed deploy. No rubber-stamping detected.

## Plan adherence — no drift

Every "Changes required" bullet across all three phases implements as written. All five "**Adapted during implementation.**" notes accurately describe the code they annotate. Decisions D1–D12 are all honoured in code, including the three most load-bearing:

- **D1/D2** — `AuthEndpoints.cs:88` refuses only `Blocked`; Pending falls through to `SignInAsync`.
- **D6** — `MemberAdminEndpoints.cs:90-102` flips status, calls `NotifyAsync`, then a single `SaveChangesAsync`. No `BeginTransaction`, no `CreateExecutionStrategy`. The tracked-entity assumption was traced end to end and holds: Identity's `UserStore` and `UnitOfWork` resolve the same scoped `AppDbContext`, `OutboxWriter.Add` does not save, and `AccountApprovedNotification` only reads subscriptions.
- **D11** — `pending.ts:50` calls `auth.refresh()`; `pending.spec.ts:62` asserts `/api/auth/me` is never called.
- **D12** — `git diff 3430f22..HEAD -- auth.guard.ts` is empty. Untouched as required.

The "What we're NOT doing" list is respected: no reject, no block/unblock, no member list, no rate limiting, no i18n machinery, no CSS framework, no in-app notification centre.

## Findings

### F1 — Concurrent double-approve sends two emails; the idempotency guard is not transactional

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/Application/Members/MemberAdminEndpoints.cs:78-102
- **Detail**: The already-Active early return is a read-then-check across two separate scoped `DbContext`s, not an atomic guard. Two admins approving the same row simultaneously both read `Pending`, both pass the guard, both enqueue an email, both save.

  `ApplicationUser.ConcurrencyStamp` **is** a concurrency token (ModelSnapshot:30-31), so EF emits `WHERE Id=@id AND ConcurrencyStamp=@original`. But the flip bypasses `UserManager.UpdateAsync`, which is what rotates the stamp — so the first write leaves it unchanged and the second write's predicate still matches. Neither call fails. Result: two `OutboxMessages` Email rows, and the member receives the approval email twice.

  This is the plan's own guardrail ("Approving twice must send one email, not two", plan-brief "Watch for"). `MemberAdminEndpointTests.Approving_twice_queues_exactly_one_email` passes because it serialises the two calls; it cannot catch this.
- **Fix A ⭐ Recommended**: Rotate the concurrency stamp as part of the flip, and treat the resulting `DbUpdateConcurrencyException` as "already approved" → 200.
  - Strength: Makes the existing guard actually atomic while keeping the single `SaveChangesAsync` that D6 requires — the outbox row and the status flip stay in one transaction. Uses the concurrency token already configured on the entity rather than adding a mechanism.
  - Tradeoff: Adds a try/catch around the save; the 200-on-conflict path needs its own test to avoid becoming untested branch.
  - Confidence: HIGH — the stamp is already a token; only the rotation is missing.
  - Blind spot: Not verified against a real concurrent test; the reasoning is from the EF model and Identity's `UserStore` source, not an executed race.
- **Fix B**: Accept and document as a known risk, matching how D4 and the role-less-user risk were handled.
  - Strength: Zero code change. The window is two admins clicking the same row within one round-trip, on a single gym's queue; the consequence is a duplicate email, not data corruption.
  - Tradeoff: Leaves a documented guardrail unmet, and the plan explicitly called this one out rather than accepting it.
  - Confidence: MEDIUM — depends on how literally the "one email, not two" guardrail is meant.
  - Blind spot: S-05 will copy this approve handler as its reference implementation, so the pattern propagates.
- **Decision**: FIXED via Fix A — concurrency stamp rotated in `MemberAdminEndpoints.ApproveAsync`; the catch lives behind a new `IUnitOfWork.TrySaveChangesAsync` so no EF Core type enters Application. New test `Concurrent_approves_still_queue_exactly_one_email` was verified to FAIL (Expected 1, Actual 2) with the rotation removed, and pass with it.

### F2 — Registration's rollback path cannot fire for its own stated failure mode

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/Application/Auth/AuthEndpoints.cs:158-173
- **Detail**: The guard is `if (!roleAssigned.Succeeded)` — but `UserManager.AddToRoleAsync` returns a failed `IdentityResult` essentially only for `UserAlreadyInRole`, impossible for a user created two lines earlier. The realistic failure — the `User` role row not existing — is **thrown**, not returned: `UserStore.AddToRoleAsync` does `if (roleEntity == null) throw new InvalidOperationException(RoleNotFound)`.

  That state is reachable in this codebase. `Program.cs:170-183` wraps `AdminSeeder.SeedAsync` (which creates missing roles) in a catch that deliberately logs and continues — "Never take the site down over seeding." On a boot where seeding failed, `POST /api/auth/register` creates the user, throws out of `AddToRoleAsync`, never reaches `DeleteAsync`, and returns an unhandled 500.

  The outcome is exactly the orphan the rollback exists to prevent — a Pending account with no role, which passes `ActiveMember`'s status check, fails its `RequireRole`, and has no admin surface to repair — plus a now-taken email the member can never re-register. The plan's Open Risks accepted the role-less-user risk, but that acceptance assumed the delete fallback runs. It does not.
- **Fix**: Wrap the `AddToRoleAsync` call and its compensating delete in a `try/catch (Exception)`, not only the `Succeeded` check, and add a `RegisterEndpointTests` case for the missing-role branch.
  - Strength: Closes the gap the existing code already intends to close; small and local.
  - Tradeoff: Needs a way to simulate a missing role in the fixture.
  - Confidence: HIGH — Identity's throw-on-missing-role behaviour is well established, and the seeder's catch-and-continue is quoted directly from `Program.cs`.
  - Blind spot: None significant.
- **Decision**: SKIPPED — the risk stays as the plan already records it.

### F3 — Out-of-plan csproj commit whose stated justification does not hold

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: src/po-prostu-silka.csproj (commit 8ac8c6c)
- **Detail**: Commit `8ac8c6c` landed after the plan's epilogue (`f29e91b`), is in no phase's "Changes required", and has no Progress entry. It replaced a pre-existing and working `<ItemGroup>` of `<Compile/Content/EmbeddedResource/None Remove="app\**" />` with `DefaultItemExcludes` + `TypeScriptCompileBlocked`.

  The commit message asserts the Angular workspace was being swept into the .NET project and that `dotnet publish` was copying `app/dist/**.json`. A controlled A/B of both csproj versions with node reuse disabled shows **identical** item sets:

  ```
  old csproj:  Content total: 16 | under app/: 0
  new csproj:  Content total: 16 | under app/: 0
  ```

  Compile, None and EmbeddedResource are likewise 0 under `app/` in both. The single measurement the diagnosis was built on ("25 items, 9 under app/") is not reproducible and was most likely a stale MSBuild node serving a cached evaluation. The change is behaviour-neutral, its commit message is inaccurate, and the Visual Studio problem it claimed to fix remains undiagnosed — the actual error text was never captured.

  Not pushed: `origin/main` is 2 commits behind.
- **Fix**: Revert `8ac8c6c`, restore the original `Remove="app\**"` ItemGroup, and diagnose the Visual Studio failure from its actual error text before changing build configuration.
  - Strength: Removes an unjustified change from a slice it does not belong to, and stops an inaccurate commit message entering shared history while it is still unpushed.
  - Tradeoff: `TypeScriptCompileBlocked` is lost — it may genuinely help the VS symptom, but that is unverified either way.
  - Confidence: HIGH — the A/B measurement is direct and reproducible.
  - Blind spot: Whether VS behaves differently from `dotnet msbuild` on the same project has not been tested.
- **Decision**: FIXED — commit 8ac8c6c dropped from history (it was the unpushed tip); `src/po-prostu-silka.csproj` restored byte-identical to its pre-change state. The Visual Studio problem remains undiagnosed and needs its real error text.

### F4 — `ApproveFailure` is declared but never read, so 409 `not_pending` renders a retry-forever message

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/app/src/app/core/admin/member-admin.models.ts:18-20
- **Detail**: `auth.models.ts`'s failure types are all consumed — `login.ts:messageFor` and `register.ts:applyFailure` both switch on `reason`. `ApproveFailure` breaks that pattern: `approvals.ts:60-63` has a bare `catch` that never reads the body, so an admin hitting the documented 409 (row already approved in another tab, or a Blocked member) sees the generic "Nie udało się zatwierdzić. Spróbuj ponownie." and can retry indefinitely. Both specs flush `{ reason: 'not_pending' }` yet neither asserts a distinct message.
- **Fix**: Either branch on `not_pending` to show "Ten członek nie oczekuje już na zatwierdzenie" and drop the row, or delete the unused type.
- **Decision**: FIXED — `approvals.ts` now branches on `not_pending`: the stale row is dropped and the admin is told why, instead of a generic retry-forever message. Spec added; the existing "keeps the row" spec now uses a 500 so the two paths stay distinct.

### F5 — Header and guard disagree on what "admin" means

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/app/src/app/app.html:11
- **Detail**: The shell shows the link on `auth.isAdmin()` alone; `admin.guard.ts:29` requires `auth.isAdmin() && auth.isActive()`. A pending admin therefore sees "Zgłoszenia", clicks it, and is bounced `/admin/approvals` → `/` → `/pending` — exactly the "wonders why it refuses them" the comment on that line says it is avoiding.
- **Fix**: Change the template condition to `auth.isAdmin() && auth.isActive()` so it matches the guard.
- **Decision**: FIXED — `app.html` condition is now `auth.isAdmin() && auth.isActive()`, matching `adminGuard` and the backend Admin policy. Spec added for the pending-admin case.

### F6 — Concurrent duplicate registration reports the wrong reason

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Application/Auth/AuthEndpoints.cs:127, 146-155
- **Detail**: The `FindByEmailAsync` pre-check returns 409 `email_taken`, but two simultaneous registrations of the same address race past it. The loser's `CreateAsync` fails with `DuplicateEmail`/`DuplicateUserName`, and the mapper's `codes.Any(c => c.Contains("Email"))` branch turns that into 400 `invalid_email` — so the SPA renders "Podaj poprawny adres e-mail." on a perfectly valid address, without the "Zaloguj się" link the real `email_taken` path offers.
- **Fix**: Test for the `Duplicate*` codes and return `email_taken` before the `Contains("Email")` branch.
- **Decision**: FIXED — Identity `Duplicate*` codes now map to `409 email_taken` before the `Contains("Email")` branch. Note: this covers the validator-caught case; the genuine INSERT race is a separate finding (F9).

### F7 — A null JSON field on `/register` is an unhandled 500

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Application/Auth/AuthEndpoints.cs:127
- **Detail**: `RegisterRequest` is a positional record of non-nullable strings, but `{"email": null}` deserialises fine and `FindByEmailAsync` does `ArgumentNullException.ThrowIfNull` → unhandled 500 on an anonymous endpoint. The author guarded `DisplayName` (`request.DisplayName?.Trim() ?? string.Empty`, line 118) but not `Email` or `Password`. **Pre-existing pattern**: `LoginAsync:63` has the identical shape, so this is a consistency gap rather than a regression introduced here.
- **Fix**: Null/whitespace-guard `Email` and `Password` alongside the existing `DisplayName` check, returning `invalid_email` / `invalid_password`.
- **Decision**: FIXED on both endpoints — `RegisterAsync` returns `invalid_email`/`invalid_password` and `LoginAsync` returns its non-disclosing `invalid_credentials` for null/blank fields. Tests added for both.

### F8 — Application → Infrastructure reference; the stated escalation trigger is arguably met

- **Severity**: 📋 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Architecture
- **Location**: src/Application/Members/MemberAdminEndpoints.cs:5
- **Detail**: `using po_prostu_silka.Infrastructure.Authorization;` is a genuine Application→Infrastructure dependency, which AGENTS.md's layering table does not permit. It is documented at lines 27-33 with a stated escalation: "if Application ever grows a second such reference, move the name constants down into `Domain`."

  AGENTS.md's **hard** rule is untouched — `using Microsoft.EntityFrameworkCore` appears in 21 files, every one under `src/Infrastructure/` or `Program.cs`; Domain and Application are clean, and both new seams follow the established `IOutboxWriter`/`IPushSubscriptionStore` shape exactly.

  The judgement call is whether to act now. The alternative to the constant is a bare `"Admin"` string literal, which is the exact failure `AuthorizationPolicies` exists to prevent, so the current code is the lesser evil. But `ApplicationRoles` — which the policy builder itself references — already lives in `Domain`, so moving `ActiveMember`/`Admin`/`StatusClaimType` beside it would remove the exception entirely for a few lines of churn, rather than leaving a comment that functions as a permanent waiver.
- **Fix A ⭐ Recommended**: Move the three policy-name constants into `Domain` beside `ApplicationRoles`, leaving `AddApplicationPolicies` in Infrastructure.
  - Strength: Removes the only layering exception in the codebase for a few lines of churn, and puts the names next to `ApplicationRoles`, which the policy builder already reads from `Domain`.
  - Tradeoff: Touches an F-02 artifact whose comment says "THESE NAMES ARE A CONTRACT... Do not rename them" — moving is not renaming, but it edits a file S-01 otherwise leaves alone.
  - Confidence: HIGH — `ApplicationRoles` proves the pattern works from `Domain`.
  - Blind spot: Not checked whether any archived plan documents the current location as deliberate.
- **Fix B**: Leave as documented, and act only if a third consumer appears.
  - Strength: Honours the escalation the code itself wrote down; no churn in a slice that is otherwise closed.
  - Tradeoff: Documented exceptions tend to become permanent, and S-02/S-03 will each add admin endpoints that need the same constant.
  - Confidence: MEDIUM — depends on whether `Program.cs` counts as a prior consumer (it is the composition root, so arguably not).
  - Blind spot: None significant.
- **Decision**: FIXED via Fix A — policy names moved to `src/Domain/AuthorizationPolicyNames.cs`; `AuthorizationPolicies` keeps the builder plus aliases for existing call sites. Application now references Domain only; verified no `using po_prostu_silka.Infrastructure` in Application or Domain.

### F9 — Simultaneous registration of the same email returns an unhandled 500

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/Application/Auth/AuthEndpoints.cs (the `CreateAsync` call)
- **Detail**: Found while fixing F6, and proven with a throwaway concurrent test. Two simultaneous
  registrations of one address pass both the `FindByEmailAsync` pre-check AND Identity's
  `UserValidator`; the INSERT then violates the unique index —
  `Cannot insert duplicate key row in object 'dbo.AspNetUsers' with unique index 'UserNameIndex'` —
  surfacing as an unhandled `DbUpdateException` (500) on an anonymous endpoint. F6 predicted a
  mis-mapped 400; the reality is worse.
- **Fix**: Not applied. Catching it needs EF Core types in Application (AGENTS.md's hard rule), and
  `UserManager.CreateAsync` owns its own save, so the `IUnitOfWork` seam cannot intercept it; a new
  `IUserRegistrar` seam is a large abstraction for a millisecond-wide race whose victim can simply
  retry onto the clean `409 email_taken` path.
- **Decision**: ACCEPTED AS RISK — recorded in the plan's Open risks section.

## Claims checked and dismissed

Recorded so a future review does not re-raise them:

- **"`Approvals.ngOnInit` fetches during prerender, baking the error alert into the prerendered HTML."** Dismissed. `dist/app/browser/index.html` contains a bare `<app-root></app-root>` and no component markup; `prerendered-routes.json` is `{"routes": {}}`. No route is prerendered, so no component runs at build time. Latent only if SSR is ever wired up — which the plan's own Phase 2 adaptation note already records.
- **"Concurrent double-approve throws `DbUpdateConcurrencyException` → 500."** Wrong mechanism. The stamp is a token but is never rotated, so both writes' predicates match and neither throws. The real outcome is two emails — see F1.
- **"The new `DefaultItemExcludes` covers an `app/dist/**` case the old `Remove` list missed."** Dismissed by direct A/B measurement — see F3.
