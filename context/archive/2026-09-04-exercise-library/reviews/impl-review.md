<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Exercise Library

- **Plan**: `context/changes/exercise-library/plan.md`
- **Scope**: Phases 1-4 of 4 (full plan)
- **Date**: 2026-09-04
- **Commits**: `af62c5f` (p1), `ccade3a` (p2), `a36378e` (p3), `0b15c0f` (p4), `a6ba6a6` (progress), `ffffc3c` (lazy-loading fix)
- **Verdict**: NEEDS ATTENTION → all findings triaged (4 fixed, 1 accepted)
- **Findings**: 0 critical, 2 warnings, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

### Automated success criteria, re-run at review time

| Check | Result |
|---|---|
| `dotnet build` (from `src/`) | PASS — 0 warnings, 0 errors |
| `dotnet test` (repo root) | PASS — 296 passed at review time; 297 after triage fixes |
| `dotnet ef migrations script AddBookings AddExercises` | PASS — generates cleanly |
| `npm test` (from `src/app/`) | PASS — 286 passed at review time; 291 after triage fixes |
| `npm run quality:check` | PASS — Prettier and ESLint clean |
| `npm run build` | PASS — initial bundle 475.01 kB, no budget warning |
| Layering (`grep 'using Microsoft.EntityFrameworkCore' src/Domain src/Application`) | PASS — no hits |

All 14 automated Progress rows carry commit SHAs. The 19 manual rows remain `- [ ]` by explicit
arrangement: the user is verifying on the deployed environment after the final phase. This is
pending, not rubber-stamped — no manual row is checked without evidence.

## Findings

### F1 — A lost name race returns 500 where the codebase already has the primitive for a clean 409

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `src/Application/Training/ExerciseEndpoints.cs:191` (create), `:236` (update), `:301` (activate)
- **Detail**: All three name-guarded writes call `unitOfWork.SaveChangesAsync(...)`, the throwing
  overload, after a read-then-write `IsNameTakenAsync` pre-check against the filtered unique index
  `IX_Exercises_Name_Active`. When two requests race, the loser's write violates the index and
  `SaveChangesAsync` throws an unhandled `DbUpdateException` — a 500 for what is a clean
  `name_taken` 409 in the ordinary path. The plan accepted this explicitly, reasoning it was
  consistent with `ClassTypeEndpoints`. That reasoning is now out of date: `IUnitOfWork.TrySaveAsync`
  returning `SaveOutcome.UniqueViolation`
  (`src/Application/Persistence/IUnitOfWork.cs:70-77`, `src/Infrastructure/Persistence/UnitOfWork.cs:42-61`)
  was added in S-08 for exactly this shape, and its own doc comment names the gap as "the recurring
  hole in this codebase: three separate implementation reviews found a pre-check followed by a write
  where the losing race surfaced as an unhandled DbUpdateException … and each time it was accepted as
  risk because catching it needed EF Core types in Application. It does not any more." Neither
  `research.md` nor `plan.md` found that primitive, so this slice reproduced the hole into a fourth
  surface rather than closing it.
- **Fix A ⭐ Recommended**: Switch the three `ExerciseEndpoints` writes to `TrySaveAsync`, mapping
  `SaveOutcome.UniqueViolation` to the existing `409 name_taken`.
  - Strength: Closes the gap on the surface this change owns, using the primitive built for it; the
    endpoint already returns `name_taken` from its pre-check, so the refusal path and its test exist.
  - Tradeoff: Leaves `ClassTypeEndpoints` with the same hole, so the codebase carries two behaviours
    until someone converts it.
  - Confidence: HIGH — `BookingEndpoints` already consumes `TrySaveAsync` this way; the mapping is
    mechanical and covered by a test that asserts 409 on a duplicate active name.
  - Blind spot: A race test would need two concurrent requests against the real engine; the existing
    suite proves the refusal path, not the race itself.
- **Fix B**: Convert both `ExerciseEndpoints` and `ClassTypeEndpoints` in one pass.
  - Strength: Removes the last two instances of a documented recurring defect; the codebase ends with
    one behaviour instead of two.
  - Tradeoff: Touches a shipped, archived slice that this change was not scoped to modify, and widens
    the diff past what was planned and reviewed.
  - Confidence: MEDIUM — the ClassType conversion is equally mechanical, but its own tests would need
    re-running and its plan is archived and must not be edited.
  - Blind spot: Whether other pre-check-then-write sites (member approval, trainer role) share the
    shape has not been audited here.
- **Decision**: FIXED via Fix A — the three name-guarded writes in `ExerciseEndpoints` now call
  `TrySaveAsync`, with a shared private `NameTaken(unitOfWork)` mapping any non-`Saved` outcome to
  the existing `409 name_taken` after `DiscardChanges()`. `DeactivateAsync` deliberately keeps
  `SaveChangesAsync`: deactivating only ever releases a name, so it cannot violate the index.
  `ClassTypeEndpoints` was left alone, per Fix A's stated tradeoff — it is a shipped, archived slice
  and converting it is its own change.

### F2 — `VideoUrl` is the one input with no length guard before parsing

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `src/Application/Training/ExerciseEndpoints.cs:344-347`
- **Detail**: Every other optional field passes through `TooLong(...)` with a ceiling matching its
  column, and the file's own comment states the rule as "Every column with a HasMaxLength has a guard
  here. NOT optional to check." `VideoUrl` is exempt because it is not itself a column — only the
  parsed 11-character id is stored — so an arbitrarily long string is handed to `Uri.TryCreate` and
  the anchored regex on every request. The practical risk is low (admin-only endpoint, the regex is
  anchored with no backtracking, and the client input has no `maxlength` only because none was
  specified), but it is the one hole in a rule the file states absolutely.
- **Fix**: Guard `VideoUrl` at ~2048 characters before parsing, refusing with the existing
  `invalid_video_url` reason, and mirror it as `[attr.maxlength]` on the form's video input.
- **Decision**: FIXED — `MaxVideoUrlLength = 2048` added to `ExerciseEndpoints`, checked before the
  parser runs; mirrored as `MAX_VIDEO_URL` with a `Validators.maxLength` and `[attr.maxlength]` on
  the form. A new `InlineData` case in `An_unusable_video_link_is_refused` pins it — the padded URL
  still parses on its `v=` parameter, so the test fails if the length guard is removed.

### F3 — Two additions beyond the literal plan contract, both benign

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: `src/app/src/app/features/admin/exercises/exercise-detail.html:50`, `src/Domain/Training/YouTubeVideoId.cs:44-56`
- **Detail**: (a) The iframe carries `allow="accelerometer; clipboard-write; encrypted-media;
  gyroscope; picture-in-picture"`, which the Phase 4 contract did not list — it named `title`,
  `loading`, `allowfullscreen` and `referrerpolicy`. Standard YouTube embed practice and additive
  only. (b) The parser accepts `music.youtube.com`, `youtube-nocookie.com` and the legacy `/v/` form
  beyond the shapes the plan enumerated — a superset, each covered by its own test case. Neither
  touches the "What We're NOT Doing" list, which was verified clean on all nine items, including a
  confirmed-empty diff for `app.html`.
- **Fix**: Note both in the plan's Phase 4 and Phase 1 contracts so the next reader does not read
  them as unexplained drift.
- **Decision**: FIXED — both recorded as "Adapted during implementation" notes on the Phase 1 parser
  contract and the Phase 4 detail-screen contract, per the `lessons.md` rule.

### F4 — No regression test pins the client-side video-id guard

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: `src/app/src/app/features/admin/exercises/exercise-detail.spec.ts`
- **Detail**: `exercise-detail.ts:49-55` re-checks `isVideoId` immediately before
  `bypassSecurityTrustResourceUrl` — the only such call in the application. The review traced the
  path and found the guard genuinely effective rather than decorative: the id can only reach the
  client having already passed the identical anchored pattern server-side, and the 11-character
  `[A-Za-z0-9_-]` class contains no `:`, `/`, `.` or `@`, so no scheme, host or path injection is
  reachable through it even if upstream parsing were fooled. What is missing is a test that pins the
  defence: no spec flushes a malformed `videoId` (a 12-character string, or `javascript:alert(1)`)
  directly in the response body and asserts that no iframe renders.
- **Fix**: Add one spec case to `exercise-detail.spec.ts` flushing a malformed `videoId` and
  asserting `iframe()` is null.
- **Decision**: FIXED — an `it.each` case covers five malformed values (`javascript:alert(1)`, a
  foreign URL, 12 and 10 characters, and one containing a space), each asserting no iframe renders.
  The comment states why it exists: the server guarantee makes these unreachable in practice, which
  is exactly what makes the guard easy to delete as redundant in a later refactor.

### F5 — The form re-fetches the whole library to build two suggestion lists

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `src/app/src/app/features/admin/exercises/exercise-form.ts:132-141`
- **Detail**: `loadSuggestions()` calls the same unfiltered `getAll()` the list uses, purely to derive
  two short distinct arrays. Each row can carry ~9,600 characters of free text, all shipped and
  discarded except two short strings. This was a deliberate decision, recorded in the plan's
  Performance Considerations and in `ExerciseEndpoints.cs:143-146`, and is correct at the stated
  scale of dozens of rows with one admin. The review confirms the reasoning and sharpens the
  threshold: it stops being acceptable once the prose fields are populated in bulk across hundreds of
  rows, which is what S-11 would drive. No caching exists in `ExerciseService`, so moving between the
  list and the form re-fetches every time.
- **Fix**: None now. Revisit with S-11 — either a dedicated suggestions endpoint or a trimmed list
  DTO, as the plan's Performance Considerations already anticipates.
- **Decision**: ACCEPTED — deliberate at the current scale, already documented in the plan and in
  `ExerciseEndpoints.cs:143-146`. The threshold to revisit is S-11 populating the prose fields in
  bulk across hundreds of rows.

## What the review confirmed clean

- **Plan adherence**: every planned file exists and matches its Intent and Contract; no MISSING and
  no DRIFT items across all four phases.
- **The three-way bound table**: all eight free-text columns have EF length, server guard and Angular
  validator agreeing exactly (200/1000/100/50/200/2000/2000/4000). This is the defect class that
  produced the `class-type-definitions` review's CRITICAL finding, and it is closed here.
- **Failure mapping**: all ten named reasons reach the control that owns them; only `missing_field`
  falls back to the banner, exactly as the plan specifies.
- **The recorded adaptation**: the lazy-vs-eager note exists in the plan at both route contracts and
  the code matches what the note says — the `lessons.md` rule about writing adaptations back into the
  plan was followed.
- **Layering**: no EF Core reference in `Domain` or `Application`, including the new seam interfaces.
- **Pattern consistency**: near-exact structural mirrors of the `ClassType` vertical at every layer,
  with only content-appropriate deltas.
- **Data safety**: the migration is purely additive with a true inverse `Down`.
- **Scope**: all nine "What We're NOT Doing" items confirmed absent, `app.html` untouched.
