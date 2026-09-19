<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Compiler-enforced layer boundaries and thin endpoint classes

- **Plan**: `context/changes/backend-layer-boundaries/plan.md`
- **Scope**: Full plan — Phases 1–7 of 7 (all Progress checkboxes `[x]`)
- **Date**: 2026-09-19
- **Verdict**: NEEDS ATTENTION → **APPROVED** after triage (6 of 7 findings fixed, 1 accepted)
- **Findings**: 0 critical, 4 warnings, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING → PASS (F1, F3, F4, F6 fixed) |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | WARNING → PASS (F2, F7 fixed) |
| Success Criteria | PASS |

## Gates re-run during this review (not taken on trust)

| Gate | Result |
|---|---|
| `dotnet build po-prostu-silka.slnx -c Release` | 0 warnings, 0 errors; `Api` emits `po-prostu-silka.dll` |
| `dotnet test po-prostu-silka.slnx` | **576 passed, 0 failed** (5 m 58 s, real SQL Server via Testcontainers) |
| CS-01 proof | `using Microsoft.EntityFrameworkCore;` in `src/Application/Auth/Login.cs` → **CS0234**; probe reverted, tree clean |
| Layering greps | EF Core / `po_prostu_silka.Infrastructure` / `po_prostu_silka.Api` in Domain+Application → none (2 hits are prose in doc comments) |
| Route literals | 18 lines, `diff` vs `f5b58f0^` **empty** |
| Migration script | `--idempotent` before vs after **byte-identical**, MD5 `0b5cd9f2c49f51e59416fc2e201f790e` both; `migrations list` = 22 |
| `reason` codes | 42 unique, sets **identical** before/after |
| Status codes | 87 sites (400×39, 401×4, 409×40, 500×3, 503×1) **identical** |
| HTTP verbs | 5 DELETE / 22 GET / 26 POST / 7 PUT **identical** |
| `MapGroup` prefixes | 16, **identical** (both duplicate-prefix pairs intact) |
| Policy bindings | `ActiveMember`×4, `Admin`×6, `TrainerOrAdmin`×3, bare×6 **identical** |
| Rate-limit bindings | `Register`, `ForgotPassword` **identical** |
| `MayActOn` ownership check | present in all 3 staff handlers (`BookForMember`, `GetClassBookings`, `ReleaseBooking`) |
| `TryBookAsync` body | byte-identical apart from `private`→`public` and one qualifier; retry bound still 10 |
| `Program.cs` diff | exactly **7 added `using` lines**, nothing else |
| `AddHostedService<OutboxDeliveryWorker>()` | still generic-typed |
| Test files | no `.cs` edited; only the required csproj `ProjectReference` |
| `git log --follow src/Api/Program.cs` | reaches `c5dd4ca Bootstrap project` |
| `context/archive/` | untouched |
| Brace-balance sweep | all 167 files under `Application`+`Api/Endpoints` balanced (no truncation) |

Endpoint classes are now registration-only — all thirteen hold exactly one method.
`ClassEndpoints` 1082→66 lines, `AuthEndpoints` 812→65, `MemberAdminEndpoints` 928→82.

## Findings

### F1 — Two dead parameters the plan explicitly ordered dropped were kept

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `src/Application/Scheduling/GetMyBookings.cs:27`, `src/Application/Training/CreateTrainingPlan.cs:35`
- **Detail**: `plan.md:615-616` — "`BookingEndpoints.GetMineAsync:468` takes a `UserManager<ApplicationUser>` that is bound per request and never used. **Drop it** as the handler moves." `plan.md:682` — "`TrainingPlanEndpoints.CreateAsync:324` takes an `IMemberStore` that is never used. **Drop it.**" Both survive verbatim. `grep` confirms each identifier occurs exactly once in its file — the declaration. No "Adapted during implementation" note covers either. The two the plan did *not* name (`MyPlan.GetMineAsync`, `GetMyExerciseAsync`) *were* correctly dropped and carry a doc comment saying so, so this is a missed instruction, not a policy reversal. Cost: a `UserManager<ApplicationUser>` resolved from DI on every `GET /api/bookings/mine`.
- **Fix**: Delete the two parameters and add the same `DROPPED AN INJECTED …  IN S-18` doc-comment note the `MyPlan` handlers carry. The compiler verifies the call sites (method-group binding, no explicit arguments).
  - Strength: Restores the plan's stated intent; removes a per-request DI resolution; makes the four drops consistent and self-documenting.
  - Tradeoff: Touches two files after the plan was closed out; needs a build + test re-run.
  - Confidence: HIGH — verified both are single-occurrence and unused; identical edit already landed cleanly twice in phase 3.
  - Blind spot: None significant — method-group binding means no caller passes them explicitly.
- **Decision**: Fixed via Fix now — both parameters dropped, S-18 doc-comment notes added; build 0/0, 576/576 tests green

### F2 — Eleven extracted helper classes are `public` where `internal` suffices

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/Application/{Members,Scheduling,Training}/` — `MemberRequestReader.cs:19`, `MembershipPassProjection.cs:18`, `BookingAuthorization.cs:31`, `ClassRangeResolver.cs:20`, `ClassRequestValidator.cs:22`, `ClassTypeProjection.cs:10`, `ClassTypeValidator.cs:17`, `ExerciseProjection.cs:10`, `ExerciseValidator.cs:15`, `TrainingPlanItemBuilder.cs:14`, `TrainingPlanValidator.cs:20`
- **Detail**: Each was a set of `private static` members of an endpoint class before the split; extraction only needs assembly-internal visibility. I verified all eleven have **zero** references outside `src/Application` (`grep -rl` over `src/Api`, `src/Infrastructure`, `tests` → 0 for every one). Two direct peers were correctly made `internal` — `Auth/CurrentUserBuilder.cs:20` and `Scheduling/ClassDtoMapping.cs:20` — which shows the narrower option was available and makes the inconsistency visible. This also contradicts the plan's own stated principle at `plan.md:631-632`: "the split is an opportunity to narrow them, not a reason to widen everything" — applied to the constants, but not to the types carrying them. It widens Application's public surface by 11 types, including the resource-authorization helper `BookingAuthorization`.
- **Fix**: Change all eleven to `internal static class`.
  - Strength: Mechanical and compiler-verified; matches the two peers already done correctly; leaves only the ~60 `HandleAsync` entry points public, which is what "one file per use case" is meant to expose.
  - Tradeoff: Eleven one-word edits plus a build + test re-run; `BookingProtocol` must stay `public` (bound from `Api`).
  - Confidence: HIGH — external-reference count verified as 0 for each; the compiler fails loudly if any were wrong.
  - Blind spot: None significant — no reflection over these types (`EndpointAuthorizationTests` reads endpoint metadata, not Application types).
- **Decision**: Fixed via Fix now — all eleven narrowed to `internal static class`; compiler confirms zero external references; build 0/0, 576/576 tests green

### F3 — `roadmap.md` still marks S-18 `in-progress`

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `context/foundation/roadmap.md:147` (slice table) and `:532` (**Status:** line)
- **Detail**: All seven phases plus the epilogue (`b3fe866`, "close out plan") are complete and `change.md` reads `status: implemented`, but the roadmap still says `in-progress` in both places. Every other finished slice (S-11 … S-17) reads `done`. The plan's phase-7 adaptation declines editing roadmap's **Risk** block only — it says nothing about the status. `/10x-status` and the next planning session read this file as ground truth.
- **Fix**: Set both to `done`, matching S-17's formatting.
- **Decision**: Fixed via Fix now — roadmap.md:147 and :532 set to `done`

### F4 — `README.md` still says Domain "references nothing"

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `README.md:54`
- **Detail**: `Domain/   entities and rules — references nothing`. This is the exact claim the plan singled out for correction (change #7, `plan.md:298-301`: record that **"Domain references nothing" stops being true**), and `README.md:54-61` is one of the line ranges that item names. `AGENTS.md:33`, `CLAUDE.md` and `src/Application/README.md` all carry the correction; `README.md` was missed. In an agent-driven repo a stale rule is a defect — it is precisely the claim a future agent would "enforce" by moving `ApplicationUser`, which `AGENTS.md` forbids.
- **Fix**: Change to `entities and rules — references only Microsoft.Extensions.Identity.Stores (for ApplicationUser)`, mirroring `AGENTS.md:33`.
- **Decision**: Fixed via Fix now — README.md:54 now mirrors AGENTS.md:33

### F5 — AD0001 suppression is blanket, not analyzer-scoped

- **Severity**: 💡 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `src/Api/po-prostu-silka.Api.csproj:43`
- **Detail**: `<NoWarn>$(NoWarn);AD0001</NoWarn>` silences *every* analyzer crash in the host project, not just `RouteHandlerAnalyzer`'s — and with it that analyzer's real diagnostics (bad parameter binding, ambiguous route handlers) in the one project that registers all 60 routes. The csproj documents the trigger, the exception, the SDK version, the measurement (120 → 122 → +0) and a re-test instruction inline, which is about as well handled as a blanket suppression can be, and the crash is genuinely in the analyzer rather than the code. Flagged so it does not silently become permanent across SDK bumps.
- **Fix**: Keep as-is; re-test the suppression at the next SDK bump per the inline instruction, and narrow to the specific analyzer if the SDK ever makes that expressible.
- **Decision**: SKIPPED — accepted as documented; the inline re-test instruction at the next SDK bump is the mitigation

### F6 — Two inaccurate counts in the plan's own epilogue notes

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `plan.md:629` and `plan.md:747`
- **Detail**: (a) `plan.md:629` — "Only three of the fourteen extracted constants became `public`". There are four: `BookingProtocol.MaxAttempts:43`, `ClassRangeResolver.ScheduleWindowDays:33`, `ClassRangeResolver.MaxRangeDays:46`, and `TrainingPlanItemBuilder.MaxAttempts:33`. The fourth is genuinely cross-file (read by `CreateTrainingPlan.cs:69`), so `public` is justified — the record is just wrong. (b) `plan.md:747` says the logger-category pin has "Six call sites"; there are four (`Register.cs:180`, `Register.cs:243`, `ForgotPassword.cs:109`, `UpdateProfile.cs:78`), matching the four pre-move `CreateLogger(typeof(...))` sites. Both are prose-count errors, not code errors — but `lessons.md` already records that a plan left asserting something untrue costs the next reader and re-flags at every future review.
- **Fix**: Correct both counts in `plan.md`.
- **Decision**: Fixed via Fix now — both counts corrected in plan.md (four constants, four call sites), with the F2 narrowing recorded alongside

### F7 — Stale comment in the Api csproj

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/Api/po-prostu-silka.Api.csproj:47-49`
- **Detail**: "BOTH are needed, not just Infrastructure: the 13 `Map*Endpoints()` extension methods live in Application **until phase 7**…". Phase 7 has landed; they live in `src/Api/Endpoints/`. The `ProjectReference` to `Application` is still correct and still needed — the ~60 handler classes are bound by method group across the boundary — so only the stated reason is stale.
- **Fix**: Reword to name the real current reason (handlers bound by method group from `Application`, plus the `Testing`-only `AuthorizationPolicies` probes from `Infrastructure`).
- **Decision**: Fixed via Fix now — comment reworded to the real current reason (method-group binding + Testing-only probes)

## Notes on what was checked and found clean

- **Behaviour preservation.** Beyond the gate table above, every extracted file's non-comment code-line sequence was checked as an exact in-order subsequence of its pre-split source across all 13 files / 152 extracted files. The ~243 line deltas are fully accounted for by method-signature renames (`GetMineAsync` → `HandleAsync`), accessibility changes, class-name qualifiers, and route-registration handler names. No statement, guard, `return`, `await` or status code was lost.
- **The truncation risk the plan itself records** (a splitter script that cut `ExerciseValidator.TooLong` short in phase 6, caught and redone) left no residue: that method is complete, all 7 call sites and 9 length constants are intact, and the repo-wide brace-balance sweep is clean.
- **Scope discipline.** No MediatR / FluentValidation / AutoMapper / repository pattern / `NetArchTest` / `AddProblemDetails` / Central Package Management. `IResult` retained. `src/app/` limited to the two planned lines. The only unplanned edit is a one-line doc-comment fix in `ClassChangeNotification.cs` (`ClassEndpoints.ToDto` → `ClassDtoMapping.ToDto`), a necessary consequence of phase 2.
- **The production risk is correctly handled.** `deploy.yml`'s five path changes all landed in the same commit as the split (`f5b58f0`), the `env:` block was untouched, and `<AssemblyName>po-prostu-silka</AssemblyName>` keeps both the publish artifact name and the `WebApplicationFactoryContentRootAttribute` key stable.
- **Environmental, not a code issue:** a stray `po-prostu-silka.exe` (PID 27180) and a `testhost` process on this machine hold the Debug output directory, which makes `dotnet ef` fail its implicit Debug build. Working around it with `--no-build --configuration Release` succeeded. Worth killing those processes locally; nothing to fix in the repo.
