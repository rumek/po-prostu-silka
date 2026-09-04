<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Class Booking and Cancellation

- **Plan**: `context/changes/class-booking-and-cancel/plan.md`
- **Scope**: Phases 1-4 of 4 (full plan)
- **Date**: 2026-09-04
- **Verdict**: REJECTED at review time; **APPROVED after triage** (F1 fixed and pinned by a red-first test; F3 and F6 knowingly skipped)
- **Findings**: 1 critical, 4 warnings, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING (F2 fixed; F3, F6 skipped) |
| Scope Discipline | PASS |
| Safety & Quality | FAIL -> PASS after triage (F1, F4, F8 fixed) |
| Architecture | PASS |
| Pattern Consistency | WARNING -> PASS after triage (F7 fixed) |
| Success Criteria | WARNING -> PASS after triage (17 of 18 manual items confirmed; 3.9 open) |

## Automated verification (run during this review)

| Criterion | Command | Result |
|---|---|---|
| 1.1 / 2.1 Solution builds warning-free | `dotnet build` from `src/` | PASS - 0 warnings, 0 errors |
| 1.2 Migration applies and reverses | `dotnet ef database update` -> `DropDeadClassColumns` -> forward | PASS - full round trip against Docker SQL Server |
| 1.3 / 2.2 / 3.4 All tests pass | `dotnet test` from repo root | PASS - 173/173 |
| 1.4 Concurrency test fails without the stamp rotation | rotation at `BookingEndpoints.cs:239` commented out, test re-run | PASS - `Concurrent_bookings_never_exceed_capacity` fails with "Expected 3, Actual 4"; source restored |
| 1.5 No EF Core in Domain/Application | `grep -rn "Microsoft.EntityFrameworkCore" src/Domain src/Application` | PASS - single hit is a comment saying it is forbidden |
| 2.3 Member schedule = one SQL statement | static verification | PASS - `ClassScheduleQuery.cs:59-61` counts via correlated subquery inside `Select`, no `.Include`, no post-`ToListAsync` loop |
| 3.1 / 4.1 Frontend unit tests pass | `npm test` from `src/app/` | PASS - 241/241 in 25 files |
| 3.2 / 4.2 Lint and format clean | `prettier --check` + `ng lint` | PASS - all files pass both |
| 3.3 / 4.3 Frontend builds within budget | `ng build` | PASS - initial 468.66 kB of 500 kB budget, no warning; `my-classes` is its own 4.41 kB lazy chunk with no calendar |

Note: the `npm` scripts required Node 24 (`C:\nvm\v24.15.0`); the shell default Node 18 is below the Angular CLI minimum. The global nvm symlink was not changed.

Manual verification: **0 of 18 items checked** at review time across all four phases. See F5 - all but 3.9 were confirmed by the user during triage and are now checked off.

## Findings

### F1 - Capacity shrink can overbook: `UpdateAsync` never rotates the concurrency stamp

- **Severity**: CRITICAL
- **Impact**: LOW - quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Application/Scheduling/ClassEndpoints.cs:517-540
- **Detail**: The handler validates `request.Capacity >= bookedCount` and then saves without assigning a new `existing.ConcurrencyStamp`. `ClassConfiguration.cs:34-37` declares `IsConcurrencyToken()` only - EF puts the column in the `WHERE` clause but does **not** generate a new value on update (unlike `IsRowVersion()`). So a capacity edit leaves the stamp unchanged in the database and a concurrent booker's stale token still matches:

  ```
  T1  Member reads class (Capacity=10, stamp=S1); counts 5 active -> ok
  T2  Admin PUT capacity=5 -> UPDATE ... WHERE stamp=S1 -> 1 row; stamp still S1
  T3  Member inserts booking, rotates to S2 -> UPDATE ... WHERE stamp=S1 -> MATCHES -> commit
      => 6 active bookings against capacity 5
  ```

  This is the project's headline hard rule (a class never accepts more bookings than it has spots) broken from the exact direction the code claims to cover - the comment at `:511` is headed "THE NO-OVERBOOKING GUARANTEE, FROM THE OTHER SIDE". The member-facing booking path itself is sound and was verified in depth (see "What was verified clean"); this is the one hole, and it is on the admin side. The window is narrow in practice (one seeded admin account), but "narrow" is the same justification the plan explicitly refused to accept for the booking path.
- **Fix**: Assign `existing.ConcurrencyStamp = Guid.NewGuid().ToString();` alongside the other field assignments at `ClassEndpoints.cs:523-526`, and add an integration test racing a capacity shrink against `POST /api/classes/{id}/bookings`. Widen the doc on `Class.ConcurrencyStamp` (`Class.cs:77-81`) from "every write that changes how many spots are taken" to "...or how many spots exist".
  - Strength: One line plus a test. The `TrySaveChangesAsync` handling at `:539` is already in place, so the lost-race path needs no new code. Restores the invariant the whole design exists to protect.
  - Tradeoff: None meaningful - the stamp is already rotated on all three booking paths, so this only makes the fourth writer follow the same protocol.
  - Confidence: HIGH - `IsConcurrencyToken()` without a generated value is documented EF Core behaviour, and every other writer in this feature rotates explicitly for exactly this reason.
  - Blind spot: Not proven by a running test - no capacity-shrink race test exists to fail first. Writing that test before the fix would confirm it empirically.
- **Decision**: FIXED - rotation added at `ClassEndpoints.cs:540` with the interleaving written out as a comment; `Class.ConcurrencyStamp` doc widened to "every writer that moves either side of the inequality". Two tests added to `BookingEndpointTests.cs`: `Lowering_capacity_rotates_the_class_stamp` (deterministic - confirmed red without the fix, "Strings are equal") and `A_capacity_shrink_racing_a_booking_never_overbooks` (invariant under real contention). `dotnet test` 175/175, build 0 warnings.

### F2 - Four in-code deviations never written back into `plan.md`

- **Severity**: WARNING
- **Impact**: MEDIUM - real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: context/changes/class-booking-and-cancel/plan.md (phases 1, 2, 3, 4)
- **Detail**: Every deviation below is well-reasoned and documented **in code**, and none is a fault on its own. What they share is that `plan.md` still asserts the original contract. `grep "Adapted during implementation" context/changes/class-booking-and-cancel/` returns nothing - which is precisely the failure `context/foundation/lessons.md` records as a standing rule ("recording it in a commit message, deploy log or review report is not a substitute").
  1. **Retry bound 3 -> 10.** Plan Phase 1 section 9 says "at most 3 attempts"; `BookingEndpoints.cs:126` is `MaxAttempts = 10`, with a 20-line rationale at `:106-125` (each committing racer costs every other racer an attempt, so 3 turned "capacity 3, 4 takers" into `conflict` instead of `class_full`).
  2. **Delete guard uses `HasAnyAsync`, not `CountActiveAsync > 0`.** Plan Phase 2 section 3 specified the active count; the code counts rows of **any** status (`BookingEndpoints.cs:503`, `BookingStore.cs:43`) because both FKs are `Restrict` and a class with only cancelled bookings would otherwise pass the guard and then 500 on an FK violation. Correct - and it silently inverts a planned test (see F6).
  3. **`getMine()` on every window change.** Plan Phase 3 section 5 says "once alongside the first schedule load"; `schedule.ts:90-97` fetches it in the same `Promise.all` as every `load(range)`, documented as keeping the set honest across a cancellation made in another tab.
  4. **Phase 4 panel -> overlay.** Plan Phase 4 section 2 contracts a `.card classes-panel` below the calendar closed by "Anuluj", matching the duplicate and delete panels. The code ships `class-bookings-overlay.*` - a separate component with its own 116-line `.scss`, a "Zamknij" button, and its own `ngOnInit` data load. Recorded only in commit `79c9434` and the component doc. The plan also names `classes.scss` as a changed file; it was never touched.
- **Fix A (Recommended)**: Add an "**Adapted during implementation.**" note to each of the four contracts in `plan.md`, and correct Progress checklist item 4.6 ("The panel is usable on a phone") to name the overlay.
  - Strength: Discharges the recorded lesson exactly as written, and stops every future review from re-flagging these four as drift. The reasoning already exists in the code comments and can be lifted verbatim.
  - Tradeoff: Four small edits to a document whose phases are already committed as done.
  - Confidence: HIGH - the lesson names this situation and this remedy.
  - Blind spot: None significant.
- **Fix B**: Record the four deviations in a single "Phase epilogue" section at the end of `plan.md` instead of inline.
  - Strength: One edit rather than four; keeps the phase bodies as the historical contract.
  - Tradeoff: A reader of Phase 4 section 2 still reads a false contract and has to reach the end of a 46 KB document to learn otherwise - which is the specific cost the lesson was written about.
  - Confidence: MEDIUM - satisfies the letter of the lesson, not its stated intent.
  - Blind spot: None significant.
- **Decision**: FIXED via Fix A - four "**Adapted during implementation.**" notes added inline to `plan.md` (Phase 1 §9 retry bound, Phase 2 §3 delete guard, Phase 3 §5 `getMine()`, Phase 4 §2 panel -> overlay). Performance Considerations corrected from "three attempts" to "ten", and Progress item 4.6 now reads "the overlay is usable on a phone".

### F3 - The class detail overlay never returns focus to the tile

- **Severity**: WARNING
- **Impact**: LOW - quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: src/app/src/app/features/schedule/class-details-overlay/class-details-overlay.ts
- **Detail**: Plan Phase 3 section 4 states "Closing on Escape **and returning focus to the tile** are required", and Progress item 3.9 repeats it. Escape is implemented (`class-details-overlay.ts:27`) and pinned by a spec (`class-details-overlay.spec.ts:144`). Focus restoration is not implemented at all: a grep for `focus` across `features/schedule/` and `shared/calendar/` returns only comments - no `.focus()` call, no element reference, no test. Closing the overlay drops focus to `<body>`, so a keyboard member loses their place in the calendar and has to tab from the top of the page. Not documented anywhere as skipped, so it reads as an oversight rather than a decision.
- **Fix**: Capture the activating tile button in `schedule.ts:openDetails` (the calendar already renders a real `<button>` per tile since `schedule-calendar.ts:157`) and call `.focus()` on it in `closeDetails()`; add a spec asserting focus lands back on the tile after Escape.
- **Decision**: SKIPPED - not worth fixing now. Progress item 3.9 therefore cannot be checked off as written; the plan requirement stands unmet and open.

### F4 - A week change during an in-flight booking leaves the overlay permanently busy

- **Severity**: WARNING
- **Impact**: LOW - quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/app/src/app/features/schedule/schedule.ts:220-223
- **Detail**: The `finally` in `act()` runs `this.acting.set(false)` only `if (generation === this.generation)`. If the member navigates to another week while a book or cancel is in flight, `load()` bumps `generation` (`:84`), the fence fails, and `acting` stays `true` for the lifetime of the component. `schedule.html:31` binds `[busy]="acting()"`, so every subsequently opened class overlay shows disabled Book/Cancel buttons in their busy state. Unlike `loading`, which self-heals on the next `load()`, nothing ever resets `acting` - `closeDetails()` clears only `selected` and `actionError` (`:137-140`). The only recovery is a full page reload.
- **Fix**: Reset `acting` unconditionally in the `finally` - the generation fence is needed for applying the *result*, not for clearing the in-flight flag.
- **Decision**: FIXED - the `finally` now clears `acting` unconditionally, with the reason recorded under the method's "generation fence" doc. Regression test `does not stay busy when the week changes mid-booking` added to `schedule.spec.ts` - confirmed red against the guarded version. `npm test` 242/242, prettier and lint clean.

### F5 - Every manual success criterion is unchecked, and the plan's phase gates were passed anyway

- **Severity**: WARNING
- **Impact**: MEDIUM - real tradeoff; pause to reason through it
- **Dimension**: Success Criteria
- **Location**: context/changes/class-booking-and-cancel/plan.md:778-846
- **Detail**: All 15 automated criteria are checked with commit shas, and all 15 were independently re-verified as passing during this review. All **18** manual criteria across the four phases are `- [ ]`. Each phase ends with an explicit "**Implementation Note**: pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase", yet phases 2, 3 and 4 all landed. Several unchecked items are the only coverage for behaviour no automated test reaches - 2.7 (blocking frees future spots and leaves past bookings alone) does have a backing integration test, but 3.6, 3.9, 3.10, 4.5 and 4.6 do not. `change.md` also still read `status: implementing` before this review.
- **Fix A (Recommended)**: Run the Testing Strategy walkthrough against `docker compose up -d` and check the items off; treat 3.9 as blocked by F3 until that is fixed.
  - Strength: The gates exist because several of the phases' guarantees are only observable in a browser. The environment is already up from this review's migration round trip.
  - Tradeoff: Roughly 30-40 minutes of hands-on clicking.
  - Confidence: HIGH - the walkthrough is written out in the plan's Testing Strategy and needs no invention.
  - Blind spot: Whether the seeded local data has a class near capacity for item 4.6; one may need to be created first.
- **Fix B**: Convert the highest-value manual items into integration/component tests and check off only what remains genuinely visual.
  - Strength: Durable - the next slice re-verifies them for free instead of re-clicking.
  - Tradeoff: Real work, and items like 3.10 ("comfortable on a phone") and 4.6 cannot be automated at all, so a manual pass is still needed.
  - Confidence: MEDIUM - several items (3.6, 4.5) are testable in the component harness; the rest are not.
  - Blind spot: None significant.
- **Decision**: FIXED via Fix A - the user confirmed the manual walkthrough during triage. 17 of the 18 items are now checked off in the plan's Progress section. **3.9 stays open**: Escape works, but focus restoration was never implemented and F3 was skipped, so the item is annotated rather than checked.

### F6 - Three plan-named tests are missing, one of them now pinning the opposite behaviour

- **Severity**: OBSERVATION
- **Impact**: LOW - quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: tests/po-prostu-silka.Tests/ClassEndpointTests.cs
- **Detail**:
  1. Plan Phase 2 section 7 lists "a class with only a cancelled booking deletes successfully". It is absent - and given the `HasAnyAsync` change (F2.2) it would now **fail**, since deletion is refused for cancelled rows too. The reversal is recorded only in a store doc comment; no test pins the new behaviour either way.
  2. Plan Phase 2 section 7 lists "a class's free spots recover after that cascade". The cascade test (`BookingEndpointTests.cs:722`) asserts row statuses only; `Free_spots_fall_on_a_booking_and_recover_on_a_cancellation` (`:573`) covers the ordinary cancel path, not the cascade.
  3. Plan Phase 2 section 7 names `MemberAdminEndpointTests.cs` as a changed file; it was not touched. The coverage exists, relocated into `BookingEndpointTests.cs`, so the plan's file list is simply inaccurate.
- **Fix**: Add a test pinning that a class with only cancelled bookings is *refused* deletion (the new intended behaviour), extend the cascade test to assert the class's free-spot count recovers, and correct the plan's Phase 2 section 7 file list.
- **Decision**: SKIPPED - not worth fixing now. The inverted delete behaviour is at least documented in `plan.md` as part of F2, but no test pins it either way.

### F7 - Dead generation fence in `afterRelease`

- **Severity**: OBSERVATION
- **Impact**: LOW - quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/app/src/app/features/admin/classes/classes.ts:245-250
- **Detail**: `const generation = this.generation;` is compared to `this.generation` on the very next line with no `await` between, so the guard can never fail. Its comment presents it as a generation fence, which will lead the next reader to trust a check that does nothing. The genuine fences in `schedule.ts:180` and `my-classes.ts:61` capture the value *before* an `await`, which is what makes them work.
- **Fix**: Delete the dead capture and comparison; the overlay already closes on a window change.
- **Decision**: FIXED - dead capture and comparison removed from `afterRelease`; the doc now states why no fence belongs there (no await to straddle) and names what actually guards the stale-window case (`load` closes the overlay). `npm test` 242/242, prettier and lint clean.

### F8 - The block cascade is the one writer outside the stamp protocol, and two comments overstate exactness

- **Severity**: OBSERVATION
- **Impact**: LOW - quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Application/Members/MemberAdminEndpoints.cs:269-286
- **Detail**: The cascade cancels future bookings without rotating any class stamp. The plan's safety argument holds - cancelling only ever *frees* spots, so a racing booker reading a pre-cascade count is conservative, never permissive. Two second-order effects are undocumented: a concurrent `BookAsync` can return `class_full` for a class that has just had a spot freed (resolved on the member's next tap), and the "EXACT" free-spot claims at `BookingEndpoints.cs:249-251` and `:322-323` are exact only with respect to *stamped* writes. Not a correctness defect - the invariant is not at risk in this direction.
- **Fix**: Tighten the two "EXACT" comments to name the block cascade as the one unstamped writer. No code change.
- **Decision**: FIXED - both "exact" claims (`BookingEndpoints.cs:249-251`, `:322-323`) now qualify themselves against the cascade and state that it can only make them understate the spots available, the direction that cannot overbook; the cascade comment in `MemberAdminEndpoints.cs` names itself as the one writer outside the stamp protocol. Comments only - no behaviour changed. Build 0 warnings, `dotnet test` 175/175.

## What was verified clean

- **The member booking path closes the overbooking race.** `BookingEndpoints.cs:194-241` reads the class (and its stamp) *before* it reads the count - the inverted order would be exploitable and is not used. Rotation happens on book (`:239`), member cancel (`:318`) and admin release (`:426`); the insert and the guarded UPDATE ship in one `SaveChangesAsync`. The retry loop is bounded and calls `DiscardChanges()` -> `ChangeTracker.Clear()` before every re-read, so no attempt re-sends stale tracked state; exhaustion returns 409 `conflict`, never a 500. Empirically confirmed during this review: commenting out the rotation makes the race test overbook 4 into 3.
- **Auth and IDOR.** Every new route is policy-gated at the group (`BookingEndpoints.cs:133-153`): member routes under `ActiveMember`, `/api/admin/classes/*/bookings` under `Admin`. Bookings resolve from `userManager.GetUserId(principal)`, never from the body or route, so cancel and list are self-scoped by construction. `ReleaseAsync:416-421` collapses wrong-class, unknown and already-cancelled into 404, blocking booking-id enumeration. Member emails are reachable only through the Admin group.
- **Layering and architecture.** No EF Core in `Domain` or `Application`; `BookingConfiguration` is a discovered `IEntityTypeConfiguration<T>` with nothing added to `OnModelCreating`; no `Class.Bookings` collection navigation, so no write path can count through the aggregate; DI registration matches the neighbouring scoped store/query pairs.
- **Migration.** `Down` drops exactly what `Up` created; `defaultValueSql: "CONVERT(nvarchar(36), NEWID())"` gives each existing row its own token rather than a shared one; both FKs `Restrict` so no cascade can erase booking history. Round trip run for real against Docker SQL Server.
- **Scope discipline.** No violations of the "What We're NOT Doing" list: no notifications on any booking path, no `ClassStatus.Cancelled` transition, no attendance tracking, no waitlist, no cancellation deadline, no admin-side booking creation, no `bookedByMe` on `ScheduledClass`, `Home` still a placeholder, no unblock restoration. The `features/home/*` and `shared/calendar/*` changes are both explicitly planned (Phase 3 sections 7 and 3), not creep.
- **Frontend idiom.** `class-bookings-overlay.ts` and `my-classes.ts` both reproduce the `busy: ReadonlySet<string>` + `failedId` + `isBusy(id)` idiom from `classes.ts` exactly; the overlays follow `class-create-overlay`'s host-level Escape pattern; `booking-failure.ts` mirrors `class-failure.ts` including the `Object.hasOwn` guard. No `innerHTML`, no `bypassSecurityTrust`, no hardcoded secrets, no CORS change.

## Triage outcome (2026-09-04)

| Finding | Decision |
|---|---|
| F1 Capacity shrink can overbook | FIXED - rotation + two tests, red-first confirmed |
| F2 Deviations not in plan.md | FIXED via Fix A - four inline notes |
| F3 Overlay does not restore focus | SKIPPED - Progress 3.9 stays open |
| F4 Overlay stuck busy after week change | FIXED - unconditional reset + regression test |
| F5 Manual criteria unchecked | FIXED via Fix A - 17 of 18 confirmed and checked off |
| F6 Three plan-named tests missing | SKIPPED |
| F7 Dead generation fence | FIXED |
| F8 Cascade outside the stamp protocol | FIXED - comments only |

Final state: `dotnet build` 0 warnings, `dotnet test` 175/175, `npm test` 242/242, prettier and `ng lint` clean.

Two items remain deliberately open: focus restoration on the class detail overlay (F3, Progress 3.9), and the missing tests around the inverted delete guard and the block cascade's free-spot recovery (F6).
