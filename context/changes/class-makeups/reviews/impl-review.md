<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Class Makeups Implementation Plan

- **Plan**: context/changes/class-makeups/plan.md
- **Scope**: All phases (1–5 of 5)
- **Date**: 2026-10-02
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 4 warnings, 6 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | WARNING |

Automated checks run during the review:

- `dotnet build po-prostu-silka.slnx`: 0 warnings, 0 errors.
- `dotnet test`: 836 of 836 passed.
- `npm test`: 990 of 990 passed.
- `npm run quality:check`: Prettier and ESLint clean.
- Playwright was not re-run; it is gated by pre-push.

## Findings

### F1 — Makeup booking does not re-check the absence inside the race loop

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Safety & Quality
- **Location**: src/Application/Scheduling/BookingProtocol.cs:128-139, src/Application/Scheduling/BookMakeup.cs:71, src/Application/Scheduling/CloseMakeup.cs, src/Application/Scheduling/RecordAttendance.cs:133
- **Detail**: The plan says: "Inside the loop, before adding, re-read the absence and check that its status is `open`, using the predicate's rules in memory on tracked rows." What exists is different:
  - `BookMakeup` checks `item.Status == "open"` once, before the loop.
  - The loop re-checks only the deadline and `HasLiveMakeupAsync`.
  - Nothing serializes the three writers against each other, because they touch three different rows:
    - the makeup save rotates the makeup class's stamp, and skips the pass stamp on purpose;
    - `RecordAttendance` rotates the absence's pass stamp;
    - `CloseMakeup` writes only the absence row, which has no concurrency token.

  This allows two outcomes:
  - **Re-mark racing a booking.** Staff re-mark the absence Makeup → Present while another member of staff books its makeup. Both succeed. The result is a free, non-consuming booking linked to an absence that is no longer "odrobi", and it is invisible in Odrabianie.
  - **Close racing a booking.** Staff close the item while another member of staff books it. Both succeed. The item reads `not_made_up`, but a live free makeup remains.

  The `CloseMakeup` comment "the only concurrent writer of the row - a re-mark" is also wrong, because `BookMakeup` races it too. The likelihood is low, but the cost is a free class and a karnet ledger that no longer matches.
- **Fix A ⭐ Recommended**: In the makeup branch of `TryBookAsync`, re-read the tracked absence each attempt (still Active, `Attendance == Makeup`, `MakeupClosedAt == null`) and rotate the ABSENCE's pass stamp in the same save; have `CloseMakeup` rotate that same pass stamp too.
  - Strength: Reuses the token `RecordAttendance` already rotates (RecordAttendance.cs:158), so all three writers serialize on one row without a migration; matches the plan's "re-read on tracked rows" contract.
  - Tradeoff: A makeup booking now contends with ordinary bookings on the absence's pass (rare, retried by the loop).
  - Confidence: MED — the stamp pattern is proven here, but absences with a null `MembershipPassId` (pre-S-16 rows) have no pass to stamp.
  - Blind spot: Haven't checked how many `Makeup` absences can lack a pass id; they would need a fallback (e.g. the absence class's stamp).
- **Fix B**: Add a `ConcurrencyStamp` to `Booking` (migration) and rotate the absence's own stamp in BookMakeup, CloseMakeup and RecordAttendance.
  - Strength: The token sits on the row that actually carries the state, so there is no gap for pass-less rows.
  - Tradeoff: A new column and migration, and every booking write path must decide whether to rotate it.
  - Confidence: MED — straightforward, but a wider change late in the slice.
  - Blind spot: Other booking writers (release, cancel class) would not rotate it unless audited.
- **Decision**: FIXED (Fix A) — `MakeupClaim.RotateAsync` (BookMakeup.cs) rotates the absence's pass stamp, falling back to its class stamp; called in the makeup branch of TryBookAsync (which now re-reads the absence: Active, Makeup, not closed) and in Close/Reopen; RecordAttendance rotates the class stamp for pass-less bookings.

### F2 — "Wszyscy obecni" overwrites Odrobi and Przepada marks

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/app/src/app/features/class-bookings/class-bookings-overlay.ts:418-421
- **Detail**: `markAllPresent` takes every row whose attendance is not `present`, which now includes `makeup` and `forfeited`.
  - A trainer who marks two members "Odrobi" and then taps "Wszyscy obecni" for the rest silently erases the makeup items they just granted. `RecordAttendance` also clears any hand close.
  - Rows with a booked makeup are refused `makeup_booked`, but `failedId` holds only one refusal, so only the last one shows.
  - Before S-36, flipping absent to present was a correction. "Odrobi" is now a deliberate grant.
- **Fix**: Limit the bulk action to unmarked rows (`attendance === null`; optionally also legacy `absent`), and update the doc comment and spec.
- **Decision**: FIXED — bulk action limited to `bulkMarkable` (unmarked + legacy absent); button shows while any such row exists; spec rewritten to assert Odrobi/Przepada rows are not sent.

### F3 — Deviations from the plan without an "Adapted during implementation" note

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: context/changes/class-makeups/plan.md (Phases 2–5)
- **Detail**: Several deviations are not recorded in the plan, which breaks the lessons.md rule "Record necessary adaptations in the plan". Most are reasonable; they are only undocumented.
  - The status predicate is `Domain/Scheduling/MakeupRules.StateOf`, applied in memory by `MakeupQuery`, not a new `Infrastructure/Scheduling/MakeupStatus.cs`.
  - The eligible-classes endpoint is in `GetMakeups.cs`, not `GetMakeupClasses.cs`.
  - `MakeupFailure` is in `MakeupItem.cs`, not its own file.
  - `GetOpenForMemberAsync` is named `GetForMemberAsync`.
  - The superseded makeup is cancelled in a SEPARATE save before the loop, not "in the same save" (the reason is only in a code comment).
  - The picker is a flat list, not grouped by day, and its empty text differs.
  - The list shows no "za N dni" variant.
  - The Moje zajęcia hint reads "Najbliższy termin: {date}" rather than "· do {date}".
  - In the E2E spec, the makeup class belongs to the same trainer, where the plan said another trainer.
- **Fix**: Add "Adapted during implementation." notes for each of these to the matching contracts in plan.md.
- **Decision**: FIXED — "Adapted during implementation." notes added to plan.md (Phase 2 §1, §2, §3 incl. MakeupClaim from F1, §5; Phase 3 §3; Phase 4 §2, §3; Phase 5 §1).

### F4 — Planned automated test cases missing

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: src/app/src/app/features/my-classes/my-classes.spec.ts, src/app/src/app/features/makeups/makeups.spec.ts, tests/po-prostu-silka.Tests/AttendanceEndpointTests.cs
- **Detail**: Some cases the success criteria list are absent:
  - **`my-classes`:** no spec that the hint is skipped when `/api/makeups/mine` fails, and none for "no request from staff". The route's `memberGuard` covers the latter structurally.
  - **`makeups`:** no closed-view empty-state case.
  - **`AttendanceEndpointTests`:** "makeup/forfeited keep the entry spent on all four read sites" asserts only three. The `MemberQuery` member list is not asserted.
  - **`MakeupEndpointTests`:** the `mine` test excludes only a planned item, not a closed or past-deadline one.
- **Fix**: Add the missing spec cases and the member-list assertion.
- **Decision**: FIXED — added: my-classes "hint skipped on failure"; makeups closed-view empty state; member-list (MemberQuery) assertion in `A_new_absence_keeps_its_entry_spent`; closed and past-deadline exclusions in the `mine` test. ("No request from staff" stays covered structurally by `memberGuard`.)

### F5 — Makeup list is derived in memory from the whole history

- **Severity**: 💡 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/Infrastructure/Scheduling/MakeupQuery.cs:40-56, 139-148
- **Detail**: `GetItemsAsync` loads every "odrobi" absence the club ever recorded, each with a correlated makeup subquery. It then derives the status and pages in memory. The doc comment says "a handful a week", but the set only grows, because closed items never leave it. The plan's performance note anticipated measuring first, so nothing is broken today.
- **Fix**: Pre-filter the default (open-only) view in SQL before materializing:
  - keep `MakeupClosedAt IS NULL`;
  - keep an absence start ≥ `StartOfLocalDay(today − 30)`, or a live, unmarked makeup.

  Keep `StateOf` as the final decision.
  - Strength: Bounds the default view by time, which is the one staff open every day.
  - Tradeoff: The SQL filter partly restates the predicate, which is a second place to keep in sync.
  - Confidence: MED — growth is real but slow for a single club.
  - Blind spot: No query plan measured on realistic volumes.
- **Decision**: FIXED — `MakeupQuery.PossiblyOpen` pre-filters the default view in SQL (not closed by hand; absence start ≥ StartOfLocalDay(today − 30) or a live makeup not marked present/forfeited); StateOf still decides. MakeupEndpointTests 20/20 incl. day 30/31 and planned-past-deadline.

### F6 — Superseded-makeup save failure ends as a misleading `conflict`

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Application/Scheduling/BookMakeup.cs:80-87
- **Detail**: If saving the cancellation of a makeup on a cancelled class fails, the change is discarded and the loop runs anyway. Every insert then hits `IX_Bookings_MakeupForBookingId_Active` until the retries run out, and the result is a generic `conflict`.
- **Fix**: Return `conflict` immediately when that save does not succeed.
- **Decision**: FIXED — returns `conflict` immediately when the superseded-makeup save fails.

### F7 — Makeup bookings inflate a karnet's attendance summary

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Infrastructure/Scheduling/BookingQuery.cs:126-148
- **Detail**: `CountAttendanceForPassAsync` does not exclude makeup bookings, which carry the pass id. An 8-entry karnet can therefore show Present + Absent = 9, against `MyAttendanceSummary`'s "one mark per entry". The SPA does not render this summary today, so the issue is latent.
- **Fix**: Exclude `MakeupForBookingId != null` from that count, or document makeups as extras.
- **Decision**: FIXED — `CountAttendanceForPassAsync` excludes `MakeupForBookingId != null`.

### F8 — Picker refusal leaves the parent list stale

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/app/src/app/features/makeups/makeup-class-picker.ts:112
- **Detail**: On a refusal, the picker reloads only its own classes.
  - On `makeup_not_open`, that reload also returns 409, so the overlay shows two alerts.
  - The Odrabianie list behind it still shows the item as open.
- **Fix**: On `makeup_not_open`, emit to the parent to reload the list and close the picker (or skip the self-reload).
- **Decision**: FIXED — on `makeup_not_open` the picker emits `stale` (no banner, no self-reload); the list closes it, shows an `info` toast and reloads. Spec added.

### F9 — Odrabianie screen wording and the UI lesson

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/app/src/app/features/makeups/makeups.html:4-7, 72-133
- **Detail**: The lessons.md rule asks for a minimalist screen, with icons over words.
  - The row actions are text buttons, while the roster's toggle is icon-led.
  - "Zamknij" collides with the overlay X's meaning; "Nie odrobi" would be clearer.
  - The intro hint says "z ostatnich 30 dni", but planned items outlive the deadline.

  The screen was not checked at tablet or desktop widths during this review.
- **Fix**: Rename "Zamknij" (e.g. "Nie odrobi"), give the actions icons from `shared/icons`, and correct the hint sentence.
- **Decision**: FIXED — row actions now icon + short word (booking "Zapisz", absent "Nie odrobi", cancelled "Zwolnij", repeat "Otwórz ponownie"); hint reads "30 dni na odrobienie od dnia zajęć"; specs, E2E locator and manual cases MAKEUP-01/04 updated. Not yet eyeballed at tablet/desktop widths.

### F10 — All manual verification items are still open

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: context/changes/class-makeups/plan.md:676-725
- **Detail**: Every manual item is unchecked, while change.md said `implemented`. None is rubber-stamped; they are simply pending. Items 3.3 and 4.6 (layout at phone, tablet and desktop) also carry the UI lesson.
  - **Phase 1:** 1.4.
  - **Phase 2:** 2.3.
  - **Phase 3:** 3.3, 3.4.
  - **Phase 4:** 4.4–4.6.
  - **Phase 5:** 5.3, 5.4.
- **Fix**: Run the manual cases (MAKEUP-01… in context/testing/04-schedule-bookings-attendance.md) and tick them before merging.
- **Decision**: ACCEPTED — the user runs the manual cases (MAKEUP-*, 3.3/4.6 layouts incl. the F9 buttons) before merge.
