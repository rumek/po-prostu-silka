<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Member List at Scale

- **Plan**: context/changes/member-list-at-scale/plan.md
- **Scope**: Phases 1–4 of 4 (full plan)
- **Date**: 2026-09-21
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 7 warnings, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

Automated criteria, re-run for this review:
- `dotnet build po-prostu-silka.slnx`: 0 warnings, 0 errors.
- `dotnet test`: 592/592 passed.
- `npm test`: 733/733 passed.
- `npm run quality:check`: clean.
- `npm run build`: initial bundle 519.12 kB, under the 600 kB warning.
- rg checks 1.3, 2.4 and 3.3: as expected.

The SPA checks ran against a working tree that also carries uncommitted `admin-schedule-web-only` edits. On Node 24.15.0: the shell's default Node 18 cannot run the Angular CLI.

Manual items: all 12 are unchecked, so they are honestly pending, not rubber-stamped.

Accepted deviations, recorded in the plan as "Adapted during implementation" and verified against the code:
- ID lookups go through `/api/admin/members/{id}`.
- The test envelope is one generic `MemberPageBody<T>`.
- `page=1` is normalised away.
- The row class is `members-entry`.
- The narrow-width menu rules were dropped.

Benign extras outside the plan:
- `th scope="row"` on the name cell.
- `maxlength="100"` on the search inputs.
- `setFilter` carries a pending phrase.
- The picker is disabled during an add.
- An extra `Michał` diacritic case and an extra `[` wildcard case.

## Findings

### F1 — A page change wipes the pager: focus is lost and the range is not reliably announced

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: src/app/src/app/features/admin/members/members.html:59-66, :289-313
- **Detail**: The table and the `<nav>` pager both sit in the final `@else` of `@if (loading())`, so every page change or search swaps them for `<app-loading />` and back.
  - Focus: the Następna or Poprzednia button a keyboard user just pressed is destroyed, so focus drops to `<body>`.
  - Announcement: the `<p role="status">` range is re-inserted with its text already filled in, and screen readers usually stay silent for a live region created that way.
  - The plan's stated reason for `role="status"` ("so a screen reader hears the new range after a page change") is not met, and no spec covers it.
- **Fix A ⭐ Recommended**: Keep the previous rows and pager rendered during a reload (show `app-loading` only on the first load, when no rows exist yet).
  - Strength: the pager and its status region survive, so the text changes in place (announced) and focus stays on the button; the table also stops flashing on every page.
  - Tradeoff: stale rows are visible for one round-trip, so row actions need disabling (or a busy state) during the reload.
  - Confidence: MED — standard pattern; needs a check that `mutate`'s refetch-on-409 still reads well.
  - Blind spot: actual NVDA/VoiceOver behaviour is untested (manual 4.7 is still pending).
- **Fix B**: Move the status region out of the conditional, next to the list, and restore focus to the pager button after a load.
  - Strength: smaller template change, and the loading UX is unchanged.
  - Tradeoff: needs imperative focus management after the render (`afterNextRender`); the table still flashes.
  - Confidence: MED — reliable announcement; the focus restore is fiddly with `@if`.
  - Blind spot: the case where the button is disabled on arrival (the last page).
- **Decision**: FIXED (Fix A). The spinner now shows only while no rows exist. A reload keeps the table and pager in the DOM: `aria-busy` is set, the rows are dimmed and their Akcje triggers are disabled. A new `shownPage` signal keeps the range describing the rows actually on screen. A failed load drops the rows, so a retry shows the spinner again. New spec: "keeps the pager, its range and the focus in place while the next page loads". members spec 42/42 passed; lint clean. Still open: arriving on the last page disables Następna, which drops focus; that remains for manual check 4.7.

### F2 — The SPA has no mirror of the new `MemberListFailure` union

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Pattern Consistency
- **Location**: src/Application/Members/MemberListFailure.cs:14
- **Detail**: The new reasons `invalid_page` and `invalid_search` have none of the pieces the SPA keeps for every other reason union:
  - no `*_FAILURE_REASONS` in `member-admin.models.ts`
  - no table built with `createFailureMessages`
  - no entry in `core/http/failure-contract.spec.ts`

  The direct precedent is the read-only `invalid_range` from `ClassRangeResolver`. The SPA never renders it either, yet it got `ScheduleReadFailure` and `schedule-read-failure.ts` because "every `*Failure` union has one". The contract spec cannot notice this gap, because it only checks unions that already exist in the SPA.
- **Fix A ⭐ Recommended**: Add a `MemberListFailure` union, reasons and table next to the member-admin models, following `schedule-read-failure.ts`, and register it in the contract spec.
  - Strength: it matches the one existing precedent exactly, and the AGENTS.md S-19 rule holds without an exception.
  - Tradeoff: a table with words for failures the SPA cannot currently cause.
  - Confidence: HIGH — the precedent is identical in kind (read path, 400, never rendered).
  - Blind spot: None significant.
- **Fix B**: Record in the plan (and in the handler's doc) why this read-path union is exempt.
  - Strength: no dead words.
  - Tradeoff: creates a second convention next to `ScheduleReadFailure`; the next union has two precedents to choose from.
  - Confidence: MED.
  - Blind spot: whether the S-19 rule's author intended read paths to be covered.
- **Decision**: FIXED (Fix A). Added `MemberListFailure` and `MEMBER_LIST_FAILURE_REASONS` in `member-admin.models.ts` and a new table in `core/admin/member-list-failure.ts`. The union is registered in `failure-contract.spec.ts`, and the count moved from 17 to 18. Contract spec 145/145 passed; lint clean.

### F3 — Clicking the pager during the search pause discards the typed phrase

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/app/src/app/features/admin/members/members.ts:335-340 (with :210-213)
- **Detail**: `goToPage` navigates with the committed `query()` and leaves the pending timer alone. When the new URL emits, `onParams` sees that the box disagrees with `q`, cancels the timer and overwrites the box with the old phrase. `setFilter` already handles this case by carrying the pending phrase; `goToPage` does not.
- **Fix**: If a search is pending, `goToPage` cancels it and navigates to page 1 with the typed phrase, as `setFilter` does. Otherwise it keeps its current behaviour.
- **Decision**: FIXED. When a debounce is pending and the typed phrase differs from `query()`, `goToPage` now commits the phrase through `commitSearch` (page 1, replace) instead of navigating to the page. Otherwise it cancels the timer and pages as before. New spec: "searches the pending phrase instead of discarding it when the pager is clicked". members spec 43/43 passed; lint clean.

### F4 — The picker says "nobody can be added" when bookable members exist beyond the first 20 matches

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/app/src/app/features/admin/classes/class-bookings-overlay.html:80-81, :111-113
- **Detail**: `searched() && bookable().length === 0` renders "Nikt pasujący nie może zostać dopisany." The "zawęź wyszukiwanie" hint lives only in the `@else`. If all 20 matches shown are already booked and `candidatesTotal() > 20`, the admin is told nobody matching can be added, which is false, and the hint that would help is hidden.
- **Fix**: Render the narrow-the-phrase hint outside the empty/select branch (or show the empty line only when `candidatesTotal() <= candidates().length`), and add a spec for the 20-booked/total-over-20 case.
- **Decision**: FIXED. The empty line now also requires `candidatesTotal() <= candidates().length`. When more matches exist, the controls render with the "zawęź" hint. New spec: "asks for a narrower phrase, not "nobody", when every shown match is signed up". Overlay spec 19/19 passed; lint clean.

### F5 — Integer overflow on `page` becomes a 500

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Application/Members/GetMembers.cs:52; src/Infrastructure/Members/MemberQuery.cs:73
- **Detail**: `page` has no upper bound, and `(page - 1) * pageSize` is unchecked int arithmetic, so `?page=2147483647&pageSize=100` wraps to a negative `Skip`. SQL Server rejects the negative OFFSET and the caller gets a 500 instead of `invalid_page`. The endpoint is admin-only and the SPA caps the page at 6 digits, so exposure is low. Related: a non-numeric `page` or `pageSize` gets the framework's binding 400, not the `invalid_page` body. That is consistent with how `filter` behaves already, and not a finding on its own.
- **Fix**: In the handler, refuse `page > int.MaxValue / pageSize` with `invalid_page`, and add that case to the `Member_list_rejects_invalid_paging` theory.
- **Decision**: FIXED. The handler now refuses `(long)(page - 1) * size > int.MaxValue` with `invalid_page`. The `page=int.MaxValue&pageSize=100` case was added to `InvalidListQueries`. MemberAdminEndpointTests: 68/68 passed.

### F6 — The phone layout's table semantics can be lost in WebKit

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/app/src/app/features/admin/members/members.scss:59-99
- **Detail**: Below `form-columns`, `table`/`tbody`/`tr`/cells become `block`/`grid`/`flex`. WebKit (every iOS browser) drops the implicit table/row/cell roles when a table element's display changes. On exactly the phone layout, VoiceOver then loses the hidden thead and the `th scope="row"`, a regression the plan's one-DOM approach did not account for.
- **Fix**: Add explicit ARIA roles in the template: `role="table"` on the table, `rowgroup` on the row groups, `row` on each row, `columnheader` on the header cells, `rowheader` on the name cell and `cell` on the rest. It is one DOM, with no layout change.
- **Decision**: FIXED. `members.html` now sets explicit `table`/`rowgroup`/`row`/`columnheader`/`rowheader`/`cell` roles, with a comment on why they restate the elements. members spec 43/43 passed; lint clean. The VoiceOver behaviour itself is still unverified (manual 4.7).

### F7 — The roadmap's S-21 answers exist only in the uncommitted working tree

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: context/foundation/roadmap.md (the S-21 section)
- **Detail**: The Phase 4 docs item was to record the S-21 unknowns as answered (chips stay chips; ~300 ms debounce, substring, no index). It is written, but only as uncommitted edits in `roadmap.md`. HEAD has no S-21 entry at all, and the same uncommitted diff carries unrelated M-7/S-20/S-22/S-23 edits. The S-21 status still reads `in-progress`. The plan's Progress also has the phase-4 SHAs appended only in the working tree.
- **Fix**: Commit the roadmap (M-7 block included, or split out) together with the plan.md Progress update, and set S-21's status when the change is closed.
- **Decision**: FIXED. At the user's choice, one commit carries the whole `roadmap.md` (M-7 block included), the plan.md Progress update, change.md, this review and the F1–F6 code fixes. The uncommitted `admin-schedule-web-only` files were left out. S-21's status stays `in-progress` until the change is archived.

### F8 — The overlay's class JSDoc is attached to a constant

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/app/src/app/features/admin/classes/class-bookings-overlay.ts:30-63
- **Detail**: `PICKER_RESULTS` and `PICKER_DEBOUNCE_MS` were inserted between the long class doc (including the new "A SEARCH, NOT THE CLUB (S-21)" paragraph) and `@Component`. The class doc now documents `PICKER_RESULTS`, and two `/** */` blocks stand back to back.
- **Fix**: Move the two constants above the class doc comment.
- **Decision**: FIXED. Both constants now sit above the class doc. Overlay spec 19/19 passed; lint and format clean.

### F9 — Test gaps: generic search terms, and no history or Back sync coverage

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: tests/po-prostu-silka.Tests/MemberEndpointTests.cs (~455, ~486); src/app/src/app/features/admin/members/members.spec.ts
- **Detail**: Two integration tests search generic words (`search=Filtr`, `search=Zablokowany`) instead of the unique name they created. They pass only because everything matching fits in `pageSize=100`, which is the assumption Phase 1 step 5 set out to remove. On the SPA side, the specs check `router.url` but never that search replaces the history entry while filter and page push. Nothing asserts that the search box follows a Back/Forward navigation (the code does it at `members.ts:210-213`).
- **Fix**: Search the full unique name in both integration tests. Add two specs: replaceUrl-vs-push on search vs page, and the box following a URL change.
- **Decision**: FIXED.
  - Integration: both filter tests now search the unique name, plus an explicit `Assert.Empty` that the other status is excluded. MemberEndpointTests: 22/22 passed.
  - SPA: two new specs, "replaces the history entry for a search, and pushes one for a filter or a page" and "puts the phrase from the URL into the search box on Back or Forward". members spec: 45/45 passed.

### F10 — An emptied list on page > 1 keeps a stale `?page=N`

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/app/src/app/features/admin/members/members.ts:245-249
- **Detail**: The out-of-range redirect fires only when `total > 0`. If the list drops to zero while on page 3 (for example, the last blocked member is unblocked under the Zablokowani filter), the screen shows the empty state with `?page=3` still in the URL. It is harmless but not canonical, and a later member appearing would land on an out-of-range page and redirect again.
- **Fix**: Also redirect to page 1 (with `replaceUrl`) when `total === 0 && page > 1`.
- **Decision**: FIXED. An empty page > 1 now redirects to `max(1, ceil(total / pageSize))`, and only backwards (`last < page`). That also closes the count/page race, where the redirect would have targeted the same URL and left stale rows up. New spec: "lands on the first page when the list emptied entirely". Full SPA suite 747/747 passed; lint clean.

## Triage summary

- Fixed: F1 (Fix A), F2 (Fix A), F3, F4, F5, F6, F7, F8, F9, F10.
- Skipped, accepted or recorded as a rule: none.
- F1–F7 are in commit 77536fc. F8–F10 and this report's final decisions are in the commit that follows it.
