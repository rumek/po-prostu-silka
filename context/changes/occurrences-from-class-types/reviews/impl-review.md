<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Occurrences From Class Types

- **Plan**: `context/changes/occurrences-from-class-types/plan.md`
- **Scope**: Full plan (Phase 1 and Phase 2, both complete)
- **Date**: 2026-09-02
- **Verdict**: NEEDS ATTENTION (all 10 findings triaged and resolved — see Decisions)
- **Findings**: 0 critical, 5 warnings, 5 observations
- **Commits reviewed**: `67679b7`, `41ec4a3`, `1a34ae7`, `ca9a5b0`, `cc3930d`, `955f58a`, `790f79a`

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

### What passed cleanly

- **The FR-007 asymmetry holds on every write path.** `CreateAsync` copies `DurationMinutes`/`Capacity` from the request (`ClassEndpoints.cs:284-285`); `UpdateAsync` and `DuplicateAsync` never load the type's defaults at all. At review time the only reads of the new `Class.ClassType` navigation were `ToDto` and the read projection; after F2's fix `ToDto` takes the type as a parameter, so the projection and `GetByIdAsync` are the only readers. The three copy-semantics tests that replace the removed compile-time barrier exist and point at the right thing.
- **Both migrations reverse correctly**, verified by running `Up` → `Down` → `Up` against real SQL Server. `DropDeadClassColumns.Down` restores the three columns as *nullable* — the state `AddOccurrenceBinding` left them in, not the pre-S-06 `NOT NULL`.
- **No dangling references to the dropped columns** anywhere outside historical migrations and their snapshots.
- **Layering holds** — no EF Core in `Domain` or `Application`; the seams are declared in `Application`, implemented in `Infrastructure`.
- **`HasTimeConflictAsync` keeps both halves** (database query + `db.Classes.Local` / `EntityState.Added` pass), with half-open interval logic intact.
- **`ClassScheduleQuery` stays one SQL statement** — navigations inside `Select`, no `Include`.
- **Authorization is applied at every new group**, and `GET /api/admin/trainers` is pinned by the member-refusal theory test.
- **All 10 planned integration tests exist**, plus six beyond the minimum.
- **Select styling keeps focus visibility and keyboard operability** — `appearance: none` removes only the painted chevron; the global `:focus-visible` outline is inherited. `:has()` is within the project's default browser targets.

## Findings

### F1 — `noTrainers` empty state is not scoped to the create route

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (Correctness)
- **Location**: `src/app/src/app/features/admin/classes/class-form.ts:122-131`
- **Detail**: `noTrainers` is set unconditionally, before the `if (id)` branch, while `noClassTypes` is correctly set only inside the create (`else`) branch. If every Trainer role is revoked or every trainer is blocked, opening `/admin/classes/:id` replaces the whole form with the "nobody holds the trainer role" signpost — so the admin cannot edit the start time or capacity of an existing class, or even see it. The class still has a valid stored instructor; an empty trainer list is a *create* precondition, not an *edit* one. The asymmetry with `noClassTypes` two lines below indicates oversight rather than intent. The existing spec only covers the create case, which is why it passes.
- **Fix**: Gate `noTrainers` on `!id`, mirroring `noClassTypes`, and add an edit-path spec asserting the form still renders with an empty trainer list.
- **Decision**: FIXED — both signals moved into the create branch with a comment stating they are create-only preconditions; two edit-path specs added (empty trainer list, every type retired). 158/158 frontend tests pass. Only the trainer spec fails against the previous code — `noClassTypes` was already correctly scoped, so the retired-type spec is a regression guard for the symmetry rather than proof of the bug.

### F2 — A successful write can return 404, telling the admin it failed

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality (Reliability)
- **Location**: `src/Application/Scheduling/ClassEndpoints.cs:298-300`, `:368-370`
- **Detail**: Both write paths re-read the entity *after* `SaveChangesAsync` succeeds, to resolve the name and display name for the response, and return `Results.NotFound()` if that re-read comes back null. The row is already committed at that point, so null can only mean a concurrent delete — yet the client is told 404. The SPA's `applyFailure` default branch then renders "Nie udało się zapisać zajęć. Spróbuj ponownie za chwilę." (`class-form.ts:280`). On create, the admin retries and either creates a **second** class or is refused with `time_conflict` for a class they believe was never created. The `is null → NotFound()` idiom is correct on the four *pre-commit* lookups in this file; it was copied to the two post-commit re-reads where it is not.
- **Fix A ⭐ Recommended**: Build the response DTO from the already-loaded entity plus the `classType` the handler already fetched, dropping the post-commit re-read entirely on `CreateAsync`; keep the re-read on `UpdateAsync` (it genuinely needs fresh instructor fixup) but treat null as `Results.Problem`, not 404.
  - Strength: Removes a database round-trip from the create path and eliminates the false-failure class at its source.
  - Tradeoff: `CreateAsync` and `UpdateAsync` stop sharing one response shape, so the DTO construction appears twice.
  - Confidence: HIGH — the handler already holds the validated `classType`; only the instructor display name needs sourcing.
  - Blind spot: The instructor's `DisplayName` is not currently loaded in `CreateAsync`; it would need fetching from the `ApplicationUser` the validation already retrieved.
- **Fix B**: Leave both re-reads, change only `NotFound()` → `Results.Problem` on the two post-commit sites.
  - Strength: Two-line change, no restructuring; the client stops seeing a 404 that means "it worked".
  - Tradeoff: Keeps an avoidable round-trip on every create, and a 500 is still a lie in the sense that the write succeeded.
  - Confidence: HIGH — trivially safe.
  - Blind spot: The SPA still shows a generic failure message for a write that succeeded; only the status code improves.
- **Decision**: FIXED via Fix A, extended — BOTH post-commit re-reads removed, not just the create one. `ValidateInstructorAsync` now returns `(IResult? Failure, ApplicationUser? Instructor)`, and `ToDto` takes the resolved `ClassType` and `ApplicationUser` as parameters instead of reading them off navigations. `UpdateAsync` projects from `existing.ClassType` (the type is immutable, so it is still correct) plus the newly validated instructor. `GetByIdAsync` remains the only caller reading from a fully-loaded entity, and the only place a null genuinely means 404. Removes one round-trip from each write path. 140/140 backend tests pass; `Edit_can_reassign_the_instructor` extended to assert the response carries the NEW trainer's display name, with `IntegrationTestFixture.CreateUserAsync` gaining an optional `displayName` so two trainers can be told apart (every account was previously called "Test Trainer", which would have made that assertion vacuous).

### F3 — Class-type failures route to a banner, not the control the plan specifies

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `src/app/src/app/features/admin/classes/class-form.ts:264-271`
- **Detail**: The plan's Phase 2 `applyFailure` contract maps `unknown_class_type` / `inactive_class_type` / `class_type_immutable` to the `classTypeId` control. The code routes all three to the form-level banner instead. The implementation reasoning is sound and documented in a code comment — the control is disabled in edit mode, so `setErrors` would render nothing visible — and the spec names the actual behaviour. But `plan.md` still states the control mapping with no "Adapted during implementation" note, so plan and code disagree in writing. This is exactly the failure mode `context/foundation/lessons.md` was written to prevent.
- **Fix**: Add an "**Adapted during implementation.**" note to the Phase 2 `applyFailure` contract in `plan.md` recording the banner routing and why.
- **Decision**: FIXED — note added beneath the `applyFailure` contract, stating that a disabled control renders nothing from `setErrors` and that all three reasons carry the same instruction to the admin.

### F4 — `AddOccurrenceBinding`'s header still documents the reversed deferral decision

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `src/Infrastructure/Persistence/Migrations/20260902162435_AddOccurrenceBinding.cs:20-25`
- **Detail**: The migration header states "THREE COLUMNS ARE DELIBERATELY NOT DROPPED … Dropping them belongs to a follow-up change, one release later." That was true when the migration was written, but `ca9a5b0` reversed it — `DropDeadClassColumns` ships in the same release and sits directly beside it. The two migration headers now contradict each other, and the stale one is the first thing a reader hits.
- **Fix**: Rewrite that paragraph to say the columns are relaxed here and dropped by `DropDeadClassColumns` in the same release, pointing at that migration's header for the accepted rollback cost.
- **Decision**: FIXED — the paragraph now reads "RELAXED HERE, NOT DROPPED", names `DropDeadClassColumns` as shipping in the same release, and points at its header for the rollback cost.

### F5 — The plan claims `InstructorName` "never shipped"; it did

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `context/changes/occurrences-from-class-types/plan.md:142-144`
- **Detail**: The second "Adapted during implementation" note says "The interim `InstructorName` property that carried the renamed column therefore never shipped." It did ship: `67679b7` defines `public string? InstructorName` in `Class.cs:92` and maps it with `HasColumnName("Instructor")`. It was removed two commits later in `ca9a5b0`. The claim is true of HEAD but false as a description of what happened — which is what an adaptation note purports to be. Verified directly with `git show 67679b7`.
- **Fix**: Reword to say the property shipped in `67679b7` and was removed in `ca9a5b0` when the drop decision was reversed.
- **Decision**: FIXED — the note now states the property shipped in `67679b7` and was removed in `ca9a5b0`, and that only HEAD is free of it.

### F6 — Select styling is unrecorded scope

- **Severity**: 📄 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: `src/app/src/styles.scss:252-289`, `class-form.html:33`
- **Detail**: `955f58a` added ~59 lines of global select styling plus a `.select` wrapper — user-requested after manual verification, so not unauthorised scope creep, but it appears nowhere in `plan.md`: not in Changes Required, not in Progress. A later reader diffing plan against code sees an unexplained global stylesheet change.
- **Fix**: Add it to the Phase 2 Changes Required as a sixth entry, with a Progress row.
- **Decision**: FIXED — added as Phase 2 change #6 with an "Added during implementation" note recording that it was requested after manual verification, and documenting the load-bearing wrapper. No Progress row added: the existing 2.1–2.3 rows already cover its verification, and the phase is closed with SHAs.

### F7 — Edit form shows a blank required instructor select when the trainer was de-roled

- **Severity**: 📄 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality (Correctness)
- **Location**: `src/app/src/app/features/admin/classes/class-form.ts:146-180`
- **Detail**: `loadExisting` builds a fallback option for the *class type* when the list does not contain it, but does nothing equivalent for the instructor — `trainers()` comes from `/api/admin/trainers`, which is active-Trainer-only by design. `setValue` puts the stored `instructorUserId` into a select with no matching `<option>`, so the browser renders it blank while the control keeps its value and passes `required`. The admin sees an unfilled mandatory field with no error, submits, and only then gets `unknown_instructor`. This is the read-side consequence of the accepted "trainer revoked, no warning" risk; the server behaviour is intended, but the form should surface it on load rather than on submit.
- **Fix**: Apply the same fallback-option trick already used for the class type, with a disabled or annotated label, so the stale instructor is visible.
- **Decision**: FIXED — `loadExisting` prepends an option for the stored instructor labelled "(nieaktywny)" when they are absent from the active-trainer list, so the select shows who is assigned instead of rendering blank. Spec added; 159/159 frontend tests pass.

### F8 — The member schedule ships trainers' Identity account ids

- **Severity**: 📄 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (Security)
- **Location**: `src/Application/Scheduling/ClassEndpoints.cs:44`, `src/Infrastructure/Scheduling/ClassScheduleQuery.cs`
- **Detail**: `ScheduledClass` carries `InstructorUserId`, and `ProjectAsync` is shared by both the admin list and the member-facing `GET /api/classes`. Every active member therefore receives the raw Identity primary key of every trainer on the schedule. The SPA never uses it on that screen. Not exploitable on its own — every admin surface is policy-gated — but it is an internal identifier crossing a trust boundary with no consumer, and it sits oddly beside the deliberate restraint of `TrainerSummary`, which was kept to two fields precisely to avoid shipping account detail.
- **Fix**: Either split the member projection to omit `instructorUserId`, or document in the `ScheduledClass` doc comment that exposing it to members is considered and accepted.
- **Decision**: ACCEPTED — documented rather than changed. `ScheduledClass` now carries a paragraph stating the exposure is deliberate, that the member SPA never reads the field, that it grants nothing on its own, and that splitting the projection was weighed and declined to avoid handing S-07/S-08 a branch. Names the condition to revisit under: a member-facing endpoint that accepts a user id.

### F9 — Dead `excludingId` term in the change-tracker branch

- **Severity**: 📄 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/Infrastructure/Scheduling/ClassStore.cs:91`
- **Detail**: The `db.Classes.Local` branch filters on `excludingId == null || c.Id != excludingId`, but entities in that branch are always `EntityState.Added` — an added entity never carries a caller-supplied id to exclude. The term is unreachable. Harmless, and it does make the two halves read symmetrically, which may have been the point.
- **Fix**: Either drop the term or add a one-line comment saying it exists for symmetry with the database predicate.
- **Decision**: ACCEPTED — kept, with a comment stating it is unreachable and retained for symmetry so the two halves of the check read identically.

### F10 — `ValidateInstructorAsync` does not thread the cancellation token

- **Severity**: 📄 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/Application/Scheduling/ClassEndpoints.cs:532-548`
- **Detail**: Every other await on both write paths threads `cancellationToken`; the two `UserManager` calls here do not, so an aborted request still pays for both round-trips. `UserManager.FindByIdAsync` and `IsInRoleAsync` have no token overloads, so this is a framework limitation rather than an omission — but it is currently silent.
- **Fix**: Add a one-line comment noting `UserManager` exposes no token overloads for these two calls.
- **Decision**: FIXED — noted in `ValidateInstructorAsync`'s doc comment as a deliberate gap, added while restructuring the method for F2.

## Success criteria verification

All nine automated criteria re-run independently during this review:

| Criterion | Result |
|---|---|
| 1.1 `dotnet build` warning-free | PASS — 0 warnings, 0 errors |
| 1.2 `AddOccurrenceBinding` applies and reverses | PASS — down to `AddClassTypes` and forward again |
| 1.3 `dotnet test` | PASS — 140/140 |
| 1.4 No EF Core in `Domain`/`Application` | PASS — no `using` hits |
| 1.9 `DropDeadClassColumns` applies and reverses | PASS — both migrations round-tripped |
| 2.1 `npm test` | PASS — 156/156 |
| 2.2 `npm run quality:check` | PASS — Prettier + ESLint clean |
| 2.3 `npm run build` | PASS — 438.94 kB initial |
| 2.4 `dotnet test` (re-run) | PASS — 140/140 |

The 11 manual criteria were confirmed by the product owner before the epilogue commit. Their observable evidence is present in the diff (empty-state markup, disabled type select, prefill wiring, `time_conflict` mapping, room removed from both templates), so none reads as rubber-stamped.
