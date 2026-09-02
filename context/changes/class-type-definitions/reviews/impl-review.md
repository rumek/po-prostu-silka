<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Class Type Definitions

- **Plan**: `context/changes/class-type-definitions/plan.md`
- **Scope**: Phases 1–3 of 3 (full plan)
- **Date**: 2026-09-02
- **Verdict**: REJECTED at review; both CRITICAL findings FIXED during triage
- **Findings**: 2 critical, 4 warnings, 4 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | FAIL |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | FAIL |

## Success criteria re-run (all six pass)

| Criterion | Result |
|---|---|
| 1.1 / 2.1 `dotnet build` | PASS — 0 warnings, 0 errors |
| 1.2 / 1.3 migration apply + reverse + re-apply | PASS — round-tripped against Docker SQL Server |
| 1.4 model snapshot committed with the migration | PASS — both in `db80aee` |
| 2.2 no EF Core `using` in Application/Domain | PASS — none |
| 2.3 API starts, six routes in OpenAPI | PASS |
| 3.1 `npm test` | PASS — 141 tests, 18 files |
| 3.2 `npm run quality:check` | PASS — Prettier + ESLint clean |
| 3.3 `npm run build` | PASS — 429.87 kB bundle |

Manual items (1.5–1.9, 2.4–2.11, 3.4–3.11) were confirmed by the human. 1.5–1.9 and 2.4–2.11
additionally have observable evidence: they were exercised programmatically during implementation
(HTTP probes and direct SQL). 3.4–3.11 rest on the human's browser pass plus 22 unit specs — no
independent artifact. Not flagged as rubber-stamping, but noted.

## Findings

### F1 — Plan's "no backend test project exists" premise is false; the slice ships zero server-side tests

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Success Criteria
- **Location**: `context/changes/class-type-definitions/plan.md` (Current State Analysis, Testing Strategy); `tests/po-prostu-silka.Tests/`
- **Detail**: The plan asserts "No backend test project exists (AGENTS.md)" and builds its whole
  verification strategy on that. It is false. `tests/po-prostu-silka.Tests/` holds **69 tests** across
  7 files — including `MemberAdminEndpointTests.cs` with 22 tests, the closest sibling to the surface
  this slice added. `.github/workflows/deploy.yml:47` gates the deploy on `dotnet test`. The claim was
  taken verbatim from a stale line in `AGENTS.md` and never verified against the filesystem.
  This is not merely a documentation slip: it was the stated premise of the planning question about
  backend verification, so the decision to ship without server-side tests was made on bad information.
  The consequences: `name_taken` on all three write paths, the deactivate-then-reuse-name rule, and
  the filtered unique index — the rule the entire design rests on — are exercised only through mocked
  frontend specs.
- **Fix A ⭐ Recommended**: Add an integration test class for `ClassTypeEndpoints` following
  `MemberAdminEndpointTests.cs`, covering at minimum: create → deactivate → re-create same name →
  activate original expects 409; the `excludingId` edit path; the four validation bounds; 403 for a
  non-admin.
  - Strength: The fixture (`IntegrationTestFixture.cs`) and the pattern already exist, so this is
    additive, not infrastructural. It pins the one rule the whole slice rests on, against the real
    SQL Server semantics the filtered index depends on — which no mocked spec can reach.
  - Tradeoff: Real work, ~an afternoon; belongs in a follow-up rather than a hotfix.
  - Confidence: HIGH — 22 sibling tests demonstrate the exact shape needed.
  - Blind spot: RESOLVED during triage — `IntegrationTestFixture.cs` boots a real SQL Server via
    Testcontainers, and its own doc comment cites "actual filtered unique indexes" as the reason it
    does so. The fixture was exactly right for this.
- **Fix B**: Correct `AGENTS.md` and the plan only, and record the untested surface as accepted risk.
  - Strength: Cheap and honest; stops the false premise propagating into S-06's plan.
  - Tradeoff: Leaves the load-bearing uniqueness rule unverified server-side while CI runs tests.
  - Confidence: HIGH — the edit is trivial.
  - Blind spot: None significant.
- **Decision**: FIXED via Fix A — added `tests/po-prostu-silka.Tests/ClassTypeEndpointTests.cs`,
  covering the deactivate → reuse → reactivate-collision cycle, the `excludingId` edit path, every
  validation bound and its boundary value, case-insensitive matching, idempotent activation, list
  ordering, 404s, and 401/403 across all six routes. Backend suite: 110 passing (was 72).

### F2 — A name longer than 200 characters is a deterministic 500

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `src/Application/Scheduling/ClassTypeEndpoints.cs:303-330`; `src/app/src/app/features/admin/class-types/class-type-form.html:18-23`
- **Detail**: `ClassTypeConfiguration.cs:15` maps `Name` to `nvarchar(200)`, but `Validate()` has no
  length check for it — while it *does* check `description_too_long` against 1000. The form's name
  control carries only `Validators.required` and no `maxlength` attribute, unlike the description
  textarea which has both. **Confirmed empirically**: a 201-character name returns
  `HTTP 500` with an unhandled `DbUpdateException` → `SqlException`
  "String or binary data would be truncated in table 'ClassTypes', column 'Name'". A 200-character
  name returns 200. This is one ordinary request, not a race. In Development the response body is a
  full stack trace; in Production (no `UseExceptionHandler` in `Program.cs`) it is an empty 500.
- **Fix**: Add `MaxNameLength = 200` and a `name_too_long` reason to `Validate()`, plus
  `Validators.maxLength(200)` and a `maxlength` attribute on the name input — mirroring exactly what
  description already does at both ends.
- **Decision**: FIXED — server constant + `name_too_long` reason, SPA failure union, form validator,
  `maxlength` attribute, error-to-control mapping and message. Covered by the new integration theory
  and two new frontend specs.

### F3 — The unique-index race can still surface as a 500 on three write paths

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `src/Application/Scheduling/ClassTypeEndpoints.cs:176`, `:227`, `:293`
- **Detail**: `IsNameTakenAsync` is a pre-check; the write follows. Between them another request can
  claim the name, and `IX_ClassTypes_Name_Active` then rejects the save with a `DbUpdateException`
  that nothing catches — a 500 where the caller should see the `409 name_taken` the same handler
  already returns on the pre-check path. Affects `CreateAsync`, `UpdateAsync` and `ActivateAsync`;
  `DeactivateAsync` is safe, because clearing `IsActive` only ever removes a row from the filtered
  index. The plan and `ClassTypeStore.cs:31-36` both name this race and call the index its backstop —
  correctly — but neither notices that the backstop's failure mode is an unhandled exception.
  **Verified**: `UnitOfWork.TrySaveChangesAsync` (`UnitOfWork.cs:23`) catches only
  `DbUpdateConcurrencyException`, so switching to it would *not* help — this is a different exception
  type from the optimistic-concurrency one the handler comment discusses.
- **Fix A ⭐ Recommended**: Accept and document at the handlers. Add one comment at each of the three
  save sites noting that a lost race surfaces as a 500 rather than a 409, and that this is tolerated
  on the same single-seeded-admin grounds `ClassStore.HasRoomConflictAsync` already records, with a
  second admin as the trigger to revisit.
  - Strength: Matches the reasoning the repo has already accepted twice for this exact class of race,
    and costs nothing. The club has one seeded admin, so the window is not reachable in practice.
  - Tradeoff: A real 500 remains possible if a second admin is ever added — and the trigger to revisit
    lives in a comment, not a test.
  - Confidence: HIGH — `ClassStore.cs:26-35` is the established precedent, verbatim.
  - Blind spot: None significant.
- **Fix B**: Add a `TrySaveUniqueAsync` seam on `IUnitOfWork` that catches `DbUpdateException` with
  SQL error 2601/2627 and returns false, letting all three handlers return `name_taken` uniformly.
  - Strength: Actually closes the race and makes the three paths uniform; keeps EF Core in
    Infrastructure, as `TrySaveChangesAsync` already does.
  - Tradeoff: ~15 lines plus a new Application-layer contract, for a window one seeded admin cannot
    reach. Sniffing vendor error numbers is its own small coupling.
  - Confidence: MEDIUM — the shape is clear, but nothing in the repo does error-number matching yet.
  - Blind spot: Haven't checked whether Azure SQL returns the same error numbers under retry.
- **Decision**: SKIPPED — left as-is by the reviewer's call. The window needs two concurrent admin
  writers and the club seeds exactly one admin, so it is not reachable today. Revisit alongside the
  same trigger `ClassStore.HasRoomConflictAsync` already records: the day a second admin exists.

### F4 — The migration's wipe re-fires if the migration is ever rolled back and re-applied

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `src/Infrastructure/Persistence/Migrations/20260902111715_AddClassTypes.cs:29`
- **Detail**: `DELETE FROM [Classes]` is unconditional. `Down` reverses the schema fully and correctly,
  so the AGENTS.md reversibility rule is satisfied in the letter — but a `Down` followed by a re-`Up`
  runs the delete a second time, destroying any rows created in between. Separately,
  `.github/workflows/deploy.yml:100-104` runs `dotnet ef database update` automatically on merge with
  no backup and no manual gate, so the "development-only data" premise is load-bearing and asserted
  only in a code comment. Blast radius itself is verified narrow: nothing references `Classes`, no
  `Bookings` table exists, and Identity/outbox/push tables are untouched.
- **Fix**: Narrow the statement to `DELETE FROM [Classes] WHERE [ClassTypeId] IS NULL;` — identical
  effect today (every row has a NULL `ClassTypeId`), but a no-op once S-06 tightens the column, which
  removes the re-apply hazard at zero cost.
- **Decision**: FIXED — with one correction the fix as written could not have taken: the predicate
  tests a column the original ordering had not created yet, so the wipe moved from *first* to
  immediately after `AddColumn`, still ahead of the foreign key. Migration round-tripped
  (`Down` → re-`Up`) clean afterwards. Plan's Critical Implementation Details carries an
  "Adapted during implementation review (F4)" note recording the ordering change.
  **Caveat**: `db80aee` is already on `origin/main` and `deploy.yml` applies migrations on push, so
  any environment that already ran this migration keeps the unconditional version's result — EF keys
  `__EFMigrationsHistory` on the migration ID, not its body. The narrowing protects fresh databases
  and rollback cycles only.

### F5 — The "Nieaktywny" badge has no base styling, and a comment claims otherwise

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/app/src/app/features/admin/class-types/class-types.scss:1-2`, `:76-79`; `class-types.html:46`
- **Detail**: The template renders `<span class="badge badge-inactive">`, and the plan says the badge is
  "styled after the members screen's status badges". **Verified**: `.badge` is defined only in
  `features/admin/members/members.scss:91`. That component uses Angular's default `Emulated`
  encapsulation, so the rule is scoped to `app-members` and never reaches `app-class-types`.
  `styles.scss` contains no `.badge` at all. The class-types stylesheet defines only `.badge-inactive`
  (colour + weight), so the badge renders as bare inline text — no padding, border, radius or size.
  Compounding it, the file's own header comment asserts `.badge` "come[s] from src/styles.scss", which
  is false and is exactly what would stop the next reader noticing.
- **Fix**: Promote the `.badge` base rule from `members.scss` into `src/styles.scss` (where the comment
  already claims it lives), leaving the per-status modifiers where they are; then correct the header
  comment in both stylesheets.
- **Decision**: FIXED — `.badge` moved to `src/styles.scss:254` with a comment recording why it is
  global (two screens need it; Emulated encapsulation hid it). Modifiers stay with the components
  that own their meaning. Header comments corrected in `members.scss` and `class-types.scss`.

### F6 — A whitespace-only name lands in the form-level banner, which the plan forbids

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `src/app/src/app/features/admin/class-types/class-type-form.ts:54`, `:160-162`
- **Detail**: Angular's `Validators.required` treats `"   "` as present, so a whitespace-only name
  passes client validation and reaches the server, which refuses it with `missing_field`
  (**confirmed**: `HTTP 400 {"reason":"missing_field"}`). `applyFailure` maps `missing_field` to the
  form-level `error` banner — the one destination the plan explicitly rules out ("lands a server
  refusal on the **control** responsible for it rather than in a banner"). The admin gets
  "Uzupełnij wszystkie wymagane pola" with no field marked.
- **Fix**: Add a whitespace-rejecting validator to the name control (a `Validators.pattern(/\S/)` or a
  small `notBlank` validator) so the client refuses it before the request, matching the server rule.
- **Decision**: SKIPPED — left as-is by the reviewer's call. The server still refuses the input
  correctly; only the message placement is off, and it costs the admin one extra round trip on an
  input nobody types deliberately.

### F7 — `load()` leaves a stale notice and row-failure on retry

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/app/src/app/features/admin/class-types/class-types.ts:63-85`
- **Detail**: `classes.ts` clears `notice` and `failedId` at the start of every action, and
  `toggleInactive` here does the same — but `load()` does not. Retrying after a failed activation
  leaves the previous `name_taken` message on screen above a freshly loaded list.
- **Fix**: Clear `notice` and `failedId` at the top of `load()`.
- **Decision**: FIXED — both cleared at the top of `load()`.

### F8 — Deactivation gives no confirmation; the row silently vanishes

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/app/src/app/features/admin/class-types/class-types.ts:106-136`
- **Detail**: With "pokaż nieaktywne" off — the default — deactivating a row removes it from view with
  no message. The sibling screen always leaves feedback: a `notice` after a duplicate, or a list
  shortened by a deletion the admin explicitly confirmed. Here the only signal is absence. The spec at
  `class-types.spec.ts:108-128` asserts the disappearance, so it is intentional, but it sits below the
  feedback level of the screen it was modelled on.
- **Fix**: Set a one-line `notice` after a successful deactivation naming the type.
- **Decision**: FIXED — both verbs now confirm in words, naming the type; deactivation also points at
  the "Pokaż nieaktywne" toggle. Covered by a new spec.

### F9 — The rename pre-check is stricter than the index for inactive types, undocumented

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `src/Application/Scheduling/ClassTypeEndpoints.cs:212`
- **Detail**: `IsNameTakenAsync` refuses renaming an *inactive* type to a name an active type holds,
  even though the filtered index does not constrain inactive rows and the write would succeed.
  Defensible — it prevents a surprise `name_taken` later at activate time, which the code goes out of
  its way to handle — but it is an undocumented divergence between the endpoint rule and the database
  rule, and the next reader will not be able to tell it was deliberate.
- **Fix**: Add one comment line at the check stating the endpoint is deliberately stricter than the
  index, and why.
- **Decision**: SKIPPED — left as-is by the reviewer's call. The behaviour is correct; only the
  rationale is unwritten.

### F10 — Maximum-bound violations map to a `{ min: true }` error key

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/app/src/app/features/admin/class-types/class-type-form.ts:152`, `:155`
- **Detail**: `invalid_duration` and `invalid_capacity` both set `{ min: true }` even when the server
  refused a value for exceeding the *maximum*. Harmless today because the template renders a single
  range message covering both bounds, but the error key misstates which bound failed. The idiom was
  copied from `class-form.ts`, where the server has floors only and it happens to be accurate.
- **Fix**: Set `{ max: true }` when the submitted value exceeds the ceiling, or rename the key to
  something bound-neutral such as `{ outOfRange: true }`.
- **Decision**: SKIPPED — left as-is by the reviewer's call. The rendered message already covers both
  bounds, so nothing user-visible is wrong.


## Triage outcome (2026-09-02)

| Decision | Findings |
|---|---|
| Fixed | F1, F2, F4, F5, F7, F8 (6) |
| Skipped | F3, F6, F9, F10 (4) |

Both CRITICAL findings were fixed. Verification after triage:

| Check | Result |
|---|---|
| `dotnet build` | 0 warnings, 0 errors |
| `dotnet test` | **110 passing** (was 72 — F1 added 38) |
| migration `Down` → re-`Up` | clean, with the narrowed wipe |
| `npm test` | **144 passing** (was 141) |
| `npm run quality:check` | Prettier + ESLint clean |
| `npm run build` | 430.42 kB |

Also corrected outside the findings, because leaving it would carry the same false premise into
S-06's planning: `AGENTS.md`'s "No test project exists yet" now describes the real test project and
how to run it, and the plan's Current State Analysis and Testing Strategy are marked corrected.

The four skipped findings are deliberate calls, not oversights. F3 (index race) needs two concurrent
admin writers and the club seeds one; F6, F9 and F10 are cosmetic or documentation-only.
