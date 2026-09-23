<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: S-27 Class Attendance

- **Plan**: context/changes/class-attendance/plan.md
- **Scope**: Phases 1–5 of 5 (full plan)
- **Date**: 2026-09-23
- **Verdict**: APPROVED
- **Findings**: 0 critical, 2 warnings, 1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | WARNING |

## Evidence

- `dotnet build po-prostu-silka.slnx`: 0 warnings, 0 errors.
- `dotnet test po-prostu-silka.slnx`: 699 passed, 0 failed.
- `npm test` (Node 24.15.0): 72 files, 882 tests passed.
- `npm run quality:check`: Prettier and ESLint clean.
- `npm run build`: initial total 536.03 kB, under the 600 kB warning. AGENTS.md records 535.96 kB after
  phase 4, so phase 5 added 0.07 kB.
- The deviations from the plan (all Phase 1 tests in `AttendanceEndpointTests.cs`, `EntryCount` on
  the summary, `ClubTime.StartOfLocalDay`, a 400 on a bad `before`, no dark-mode chips, the tabs
  spec, the lazy latch, `Intl` in place of `DatePipe`) are each recorded under "Adapted during
  implementation", as lessons.md requires.
- The core rules hold:
  - `EntryConsumption.ConsumesAnEntry` is the one predicate at all three sites.
  - `RevokePass` switched to `AnyActiveForPassAsync`.
  - `CancelClass` rotates each pass stamp before its single save.
  - `ReleaseBooking` refuses `class_started`.
  - `RecordAttendance` runs the planned check order inside the retry loop and does not rotate the
    class stamp.
  - The history endpoint takes the member from the principal, and its window arithmetic goes through
    `ClubTime`.
  - The migration's `Down` drops all three columns.
- Process note: the two review sub-agents stopped on a rate limit, so the drift and pattern review
  was done inline. Pattern comparison covered the handlers, the overlay and the history component
  against their siblings, at less depth than a full sub-agent sweep.

## Findings

### F1 — Karnet dot strip gives absent bookings a slot

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/app/src/app/features/my-classes/attendance-history.ts (`dots` computed)
- **Detail**: The strip fills `entryCount` slots with present, then unrecorded, then **absent** dots,
  and draws the rest as `free`. Under AT-03, an absence *returns* its entry, so it should not take a
  slot. Example: an 8-entry karnet with 7 present and 2 absent draws 7 green and 1 red dot, with no
  free dot. The karnet still has 1 entry left, and the dashboard's "Zostało 1 wejście" says so. The
  strip is `aria-hidden`, but it is the one thing a sighted member reads at a glance, and here it
  contradicts the balance. The spec (`attendance-history.spec.ts:150`) pins only the count of free
  dots, which is why this was not caught.
- **Fix**: Fill slots with present and unrecorded dots only, the ones that spend an entry. Absent
  stays in the counts row, not in the strip. Add a spec case where an absent booking leaves a free
  dot.
- **Decision**: FIXED — absent no longer takes a slot; `.history-dot--absent` removed; spec updated (3 free) and a new case pins 7 present + 2 absent → 1 free.

### F2 — Every manual verification step is still pending

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: context/changes/class-attendance/plan.md (Progress: 1.4, 1.5, 2.2, 3.2, 4.4–4.7, 5.5–5.8)
- **Detail**: All 12 manual items are `[ ]`. Nothing is rubber-stamped, but no human has yet checked
  the behaviour the tests cannot see: phone layout, the screen reader, system back on the tabs,
  admin past-week tiles at the desk. Item 5.5 still reads "light and dark", while the Phase 5
  adaptation says the check is light only because the app has no dark theme.
- **Fix**: Run the manual steps and tick them with evidence. Reword 5.5 to "light only (no dark
  theme exists)" so the Progress entry matches the adaptation.
- **Decision**: FIXED — Phase 5 Manual Verification now says light only (Progress 5.5 title left as is, per the convention against renaming step titles). The 12 manual steps remain for the user to run.

### F3 — "Pokaż wcześniejsze" can load an empty page across a gap

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Application/Scheduling/GetMyAttendanceHistory.cs:79-81
- **Detail**: `EarlierBefore` is always the first month of the current window whenever *any* older
  active booking exists. A member who paused for six months therefore clicks the button, sees
  nothing appended, and has to click again, up to once per empty three-month window. This is correct
  but reads as broken. It matches the plan's contract, so it is a plan gap rather than drift.
- **Fix**: Make `EarlierBefore` the first day of the month after the newest older booking's
  club-local month, so the next page always starts with something. Replace `HasHistoryBeforeAsync`
  with a max-`StartsAt` lookup. Then pin it with a gap test.
- **Decision**: FIXED — `HasHistoryBeforeAsync` replaced by `LatestHistoryStartBeforeAsync`; `EarlierBefore` = first of the month after the newest older class (club-local). New test `EarlierBefore_skips_the_empty_months_of_a_break`. Build clean; AttendanceHistoryTests 10/10 pass.
