<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Class Schedule and Admin Class Management (S-03)

- **Plan**: `context/changes/class-schedule-and-admin/plan.md`
- **Scope**: Full plan — Phases 1–3 of 3 (31/31 Progress rows complete)
- **Date**: 2026-09-01
- **Verdict**: NEEDS ATTENTION → all findings resolved during triage (2026-09-01)
- **Findings**: 0 critical, 4 warnings, 1 observation
- **Commits reviewed**: `d457ebc`, `60184a6`, `110ddc8`, `5950928`

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

**Scope Discipline** — every item on the "What We're NOT Doing" list verified absent: no booking
logic (`Booking` appears only in comments referencing S-04), `ClassStatus.Cancelled` never assigned
anywhere in `src/`, no notification code, no pagination, no calendar grid, no instructor entity. The
one extra failure reason (`missing_field`) implements a rule the plan already required.

**Architecture** — layering holds: `Domain` references nothing, `Application` declares the seams and
never touches EF Core, `Infrastructure` implements them. The read/write seam split follows
`IMemberQuery` / `IPushSubscriptionStore`, and no repository pattern was introduced.

**Success Criteria** — re-verified independently during this review: `npm test` 105 passing across 16
files, `quality:check` clean, `dotnet build` 0/0, schedule window returns only in-range classes, and
`freeSpots` equals `capacity` on all 10 rows as designed.

## Findings

### F1 — Ambiguous-hour duplicate picks the wrong occurrence

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `src/Domain/Scheduling/ClubTime.cs:67`
- **Detail**: `zone.GetAmbiguousTimeOffsets(shifted)[0]` is used with the comment *"take the FIRST
  occurrence (still on the pre-transition offset)"*. Two problems, one theoretical and one measured.

  The API contract does not define the array's order — Microsoft's documentation states the ordering
  is undefined and must not be relied on. That alone makes the index a latent bug.

  Measured on this platform, it is already wrong. Probing `Europe/Warsaw` at `2026-10-25 02:30`
  (Poland's repeated hour):

  ```
  offsets[0] = 01:00  ← CET,  the SECOND (post-transition) occurrence
  offsets[1] = 02:00  ← CEST, the FIRST  (pre-transition) occurrence
  ```

  So `[0]` returns the post-transition offset — the opposite of what the comment claims and of what
  the code intends. A class duplicated into that repeated hour lands one hour later in real time than
  the admin scheduled. The window is narrow (one hour, once a year, only for classes duplicated into
  it), but the failure is silent — the same class of bug the Phase 2 DST work existed to eliminate.
- **Fix**: Replace `[0]` with `offsets.Max()`, which deterministically selects the larger (DST,
  pre-transition) offset and therefore the earlier instant — the actual first occurrence. Correct the
  comment to say the choice is made by offset magnitude, not array position, and note that the array
  order is undefined by contract.
  - Strength: One-line change that makes the behaviour match the stated intent and stops depending on
    an undefined API ordering; verified against the measured offsets above.
  - Tradeoff: None significant — `Max()` is defined for the two-element ambiguous case.
  - Confidence: HIGH — the offsets were probed directly on this runtime, not inferred from docs.
  - Blind spot: Not verified on Linux App Service (different tzdata source), though `Max()` is
    correct regardless of ordering, which is the point of the change.
- **Decision**: FIXED differently — resolved via a `BaseUtcOffset` comparison (`DaylightOffsetFor`)
  rather than `Max()`, which states the intent semantically ("the daylight occurrence") and stays
  correct for zones with a negative daylight offset. Verified on this runtime: the shipped code
  resolved the repeated hour to 01:30 UTC, the fix resolves it to 00:30 UTC — exactly the one-hour
  error, now gone.

### F2 — Two plan deviations shipped undocumented

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `src/Application/Scheduling/ClassEndpoints.cs:367-376`,
  `src/app/src/app/core/scheduling/local-datetime.ts`
- **Detail**: The plan carries exactly two "**Adapted during implementation.**" notes (the
  `(Status, StartsAt)` index and the `ClubTime` DST fix). Two further deviations shipped without one:

  1. **`IClassAdminQuery` was never created.** Phase 2 §1 specifies two interfaces — `IClassScheduleQuery`
     for the member window and `IClassAdminQuery` for the admin list — and the DI section specifies
     three seams. The implementation has one interface with two methods (`GetScheduleAsync`,
     `GetUpcomingForAdminAsync`) and registers two seams. Grep confirms `IClassAdminQuery` exists
     nowhere in `src/`.
  2. **The `datetime-local` conversion was extracted to its own module.** Phase 3 §4 describes it as
     logic inside the form component; it lives in `core/scheduling/local-datetime.ts` with its own
     spec. Phase 3 has no adaptation notes at all.

  Both changes are improvements — one interface for one implementing class is simpler, and isolating
  the silent-failure conversion earned it a dedicated test file. Neither is a defect. The problem is
  that `context/foundation/lessons.md` records this exact failure mode as a project rule: *"the
  implementation adapts correctly but the plan text is left stating the original, wrong contract...
  the cost lands on the next reader — and on every future review, which re-flags the same non-issue."*
  This review is that future review, and it re-flagged them.
- **Fix**: Add an "**Adapted during implementation.**" note to Phase 2 §1 recording the interface
  merge (and the corresponding two-seam DI registration), and one to Phase 3 §4 recording the
  extraction to `local-datetime.ts` and why it was worth isolating.
- **Decision**: FIXED — adaptation notes added to Phase 2 §1 (the `IClassAdminQuery` merge and the
  two-seam DI registration, plus the bare-`Capacity` projection from F5) and to Phase 3 §4 (the
  extraction to `local-datetime.ts` and why isolating it earned its own spec).

### F3 — `Class` has no concurrency token; edit and delete last-write-wins

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Pattern Consistency
- **Location**: `src/Application/Scheduling/ClassEndpoints.cs:219,244`, `src/Domain/Scheduling/Class.cs`
- **Detail**: `UpdateAsync` and `DeleteAsync` call `SaveChangesAsync`, not `TrySaveChangesAsync`, and
  `Class` carries no `ConcurrencyStamp` or rowversion — so there is no token to rotate and no lost
  update to detect. This diverges from the established pattern in `MemberAdminEndpoints`
  (`ApproveAsync`, `BlockAsync`, `UnblockAsync`), where the stamp rotation is what makes the
  read-check-write atomic, and where a lost race returns 409 rather than a false success.

  Concretely: two admin tabs editing the same class silently last-write-wins, and an edit racing a
  delete surfaces an uncaught `DbUpdateConcurrencyException` as a 500 rather than a handled conflict.

  The mitigating argument is the same one `ClassStore.HasRoomConflictAsync` already documents —
  exactly one admin account is ever seeded, so concurrent admin writes are not a real scenario. That
  reasoning is sound but it lives only on the overlap check, not on these two handlers, so a reader
  of `UpdateAsync` sees an unexplained departure from the codebase's concurrency convention.
- **Fix A ⭐ Recommended**: Extend the existing single-admin justification with a short comment on
  `UpdateAsync` and `DeleteAsync` pointing at `ClassStore`'s note, so the omission reads as decided
  rather than overlooked.
  - Strength: Costs nothing, matches how the same accepted risk is already handled one file over, and
    leaves the trigger for revisiting (a second admin) recorded in one place.
  - Tradeoff: The gap remains real if a second admin is ever seeded — it is documented, not closed.
  - Confidence: HIGH — mirrors the treatment the same risk already received in this slice.
  - Blind spot: None significant.
- **Fix B**: Add a `ConcurrencyStamp` to `Class` and switch both handlers to `TrySaveChangesAsync`,
  returning 409 on a lost race.
  - Strength: Actually closes the gap and matches `MemberAdminEndpoints` exactly.
  - Tradeoff: A new column and a migration for a race that cannot occur with one admin account; the
    frontend would need a 409 path on edit and delete that nothing currently exercises.
  - Confidence: MEDIUM — correct, but it is scope the plan did not call for and S-04 may reshape.
  - Blind spot: Whether S-04's booking work will add its own concurrency token to `Class` anyway.
- **Decision**: FIXED via Fix A — `UpdateAsync` and `DeleteAsync` now carry comments extending
  `ClassStore`'s single-admin justification, naming last-write-wins and the delete-while-editing 500
  as the specific gaps, and a second admin as the trigger to add a `ConcurrencyStamp`.

### F4 — Loading one class for edit fetches the entire unbounded admin list

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `src/app/src/app/core/scheduling/class.service.ts:30-40`
- **Detail**: `getById` calls `getAdminClasses()` and filters client-side, because no
  `GET /api/admin/classes/{id}` exists. Its comment says the list is *"already loaded on the way
  here"*, which is true when the admin clicks Edit from the list — and false on a bookmark, a refresh,
  or a deep link, each of which triggers a full cold fetch to render one row.

  The admin query is deliberately unbounded (`GetUpcomingForAdminAsync` has no upper time limit), so
  this grows with every class ever scheduled. Today that is ten rows; it has no natural ceiling.
- **Fix**: Add `GET /api/admin/classes/{id}` (a `store.FindAsync` plus the existing `ToDto`) and point
  `getById` at it. Correct the comment either way — the "already loaded" claim is wrong for the
  deep-link path.
- **Decision**: FIXED — added `GET /api/admin/classes/{id}` (`GetByIdAsync`, `store.FindAsync` +
  `ToDto`, 404 on unknown) and repointed `class.service.getById` at it. The misleading "already
  loaded" comment is gone; `class-form.spec.ts` now expects the single-item endpoint.

### F5 — `freeSpots` projected as bare `Capacity`, not the planned `Capacity - 0`

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `src/Infrastructure/Scheduling/ClassScheduleQuery.cs:58-62`,
  `src/Application/Scheduling/ClassEndpoints.cs:358-359`
- **Detail**: The plan specifies projecting `Capacity - 0` so S-04 has a literal expression to edit.
  Both call sites project plain `Capacity` instead, each with a comment naming S-04 as the real
  source. Verified that the two agree, so there is no list-vs-create response mismatch — the failure
  this was worth checking for is absent. `- 0` would have been dead arithmetic; the comments carry
  the intent adequately.
- **Fix**: None needed. Recorded so a future reader does not re-flag the difference against the plan
  text.
- **Decision**: CLOSED — already covered by F2's Phase 2 §1 adaptation note.
