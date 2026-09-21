# Member List at Scale Implementation Plan

## Overview

S-21 (M-7 UX-05, UX-06): the admin's member list stops fetching every member to hide all but a few.
The API pages, searches and filters; the SPA asks for one page at a time, keeps the list's state in
the URL, and renders it as a table from the `form-columns` breakpoint up and as one compact row per
member below it. The class-bookings picker — the endpoint's second consumer, which also loaded the
whole club — becomes a search on the same endpoint.

v1 FR-005 (the admin browses all members in one searchable list) is unchanged as a requirement; this
slice changes where the search runs, not what it promises.

## Current State Analysis

- `GET /api/admin/members?filter=` returns a bare `MemberSummary[]` of EVERY member matching the
  filter, ordered by `DisplayName` alone (`src/Infrastructure/Members/MemberQuery.cs:33`). The
  handler's own doc says the opposite of this slice: "No pagination… Search is the SPA's job"
  (`src/Application/Members/GetMembers.cs:19-22`).
- `members.ts` loads every row and searches in a `computed()` (`members.ts:110-121`): substring,
  `toLocaleLowerCase`, accent-SENSITIVE ("lukasz" does not find "Łukasz"). No debounce, no request.
- Filter is already server-side and indexed (`IX_Members_Status`); `DisplayName` and `Email` carry no
  search index, and none would help a substring match.
- `class-bookings-overlay.ts:121` is a SECOND consumer: it loads `getMembers('Active')` — the whole
  active club — into an `<app-select>` and filters out already-booked people client-side.
- The SPA and API ship as ONE artifact (`deploy.yml` stages the Angular build into `src/Api/wwwroot`),
  but F-03's service worker can keep an old SPA running in an open tab across a deploy.
- Integration tests share ONE database per collection (`[Collection(nameof(IntegrationCollection))]`),
  and ~24 call sites across 10 test files resolve a member id with
  `GET /api/admin/members` + `.Single(m => m.Email == email)` — which silently depends on the whole
  club fitting in one response.
- S-20 already landed the breakpoint partial (`src/app/src/styles/_breakpoints.scss`) with `narrow`
  (30rem), `form-columns` (40rem) and `desk` (64rem). The list screen uses `app-list` / `li[appRow]`
  from the S-23 kit, with a hand-built row action menu (`members.html:95-212`).

## Desired End State

- `GET /api/admin/members?filter=&search=&page=&pageSize=` returns
  `{ items, total, page, pageSize }`. Search is a substring match on display name OR email,
  case- and accent-insensitive including `ł`; order is `DisplayName, Id` (stable across pages); bad
  paging values are a 400, an out-of-range page is an empty `items` with the true `total`.
- `/admin/members?q=kowal&filter=Active&page=3` is the list's address: back from "Edytuj dane" or a
  reload restores the same page, filter and phrase. Typing searches after a ~300 ms pause, one request
  per pause. A pager shows `1–25 z 312` with Poprzednia / Następna.
- From 40rem up the list is a `<table>` (Imię i nazwisko · Status · E-mail · Od · Akcje); below it the
  same DOM collapses into one compact row per member: name, status/role badges, Akcje. Every action
  the menu offers today remains reachable at both widths.
- The class-bookings picker searches active members on the server instead of loading the club.
- No code path in the SPA fetches the whole member list.

Verify with the automated criteria per phase plus the manual walk in "Manual Testing Steps".

### Key Discoveries:

- `MemberQuery.GetMembersAsync` projects in the DB with a correlated Roles collection
  (`MemberQuery.cs:35-67`); paging slots in as `Skip/Take` before the projection, count as a separate
  `CountAsync` on the same filtered/searched query.
- 400s in this API are `Results.Json(new XFailure("reason"), statusCode: 400)`
  (`src/Application/Scheduling/ClassRangeResolver.cs:48-49`) — follow it.
- `MemberSummary`'s doc warns it is mirrored field-for-field by `member-admin.models.ts`; the page
  envelope is the new contract to mirror, and `MemberSummary` itself stays unchanged.
- Query params are read only through `snapshot.queryParamMap` today (`register.ts:55`,
  `reset-password.ts:67`); `provideRouter(routes)` has no `withComponentInputBinding`
  (`app.config.ts:25`). This screen needs the reactive `queryParamMap`, not the snapshot, because
  navigating to the same route with new params reuses the component.
- `shared/forms/load-fence.ts` already discards out-of-order responses; it is what makes debounced
  search + back/forward navigation safe without cancelling requests.
- The presentational lint rule (`tools/eslint-rules/no-hand-rolled-presentational.js`) checks
  `ul`/`li` classes, `div.field`, bare `select`, checkboxes and loading/empty markup — a `<table>` is
  not in its vocabulary, so the table needs a documented exception, not a disable comment.
- `ł`/`Ł` have no Unicode decomposition, so an accent-insensitive collation does not fold them to
  `l`; every other Polish diacritic (ą ć ę ń ó ś ź ż) does.

## What We're NOT Doing

- **No visual redesign** (M-7 UX-09): spacing, typography, colours and the action menu's look stay;
  the only layout change is table vs. compact row.
- **The filter chips stay chips.** Four chips fit at both widths; turning them into a select is a
  redesign (roadmap unknown resolved here).
- **No sortable columns, no page-size chooser, no numbered pages, no "load more".**
- **No search index / full-text.** A substring match cannot use a B-tree index; at one club's scale
  (hundreds to low thousands) the scan is cheap, and debouncing is what bounds the request rate.
- **No compatibility shim for an old SPA.** The endpoint changes shape in place; an admin tab open
  across the deploy shows the list's load-failure state until reloaded (accepted, see Open Risks in
  the brief).
- **The trainer's member list and member-centric plans** — S-22 (UX-07, UX-08).
- **Paging the exercise, class-type and plan lists** — out of M-7 by the roadmap.
- **A persisted normalised search column / migration.** `ł` is folded in the query instead.

## Implementation Approach

Server first, because it is the contract everything else reads. The API change is a breaking change
to the response shape, so phases 1–3 are one deployable unit: **do not push to `main` between phase
1 and the end of phase 3** (CI deploys on push; phase 1 alone breaks the members screen and the
picker, phase 2 leaves the picker on a temporary 100-row cap). Phase 4 is layout only and is safe to
ship on its own.

## Critical Implementation Details

**Search folding.** The search predicate compares
`COLLATE Latin1_General_100_CI_AI` of the column with `ł`/`Ł` replaced by `l`/`L`, against the term
with the same replacement applied in C#. Reach it with `EF.Functions.Collate(...)` over
`.Replace(...)` and `.Contains(term)`; if EF's translation of that composition turns out not to
compose, fall back to `EF.Functions.Like` with `%`, `_` and `[` escaped by hand. Either way the
Phase 1 tests are the arbiter, and `%` / `_` in a search term must match literally.

**Ordering tiebreak.** `OrderBy(DisplayName).ThenBy(Id)` is load-bearing, not tidiness: two members
with the same name straddling a page boundary would otherwise appear on both pages or on neither.

**URL is the single source of truth for list state.** Chip clicks, pager clicks and (debounced)
typing NAVIGATE; only the `queryParamMap` subscription loads. Search navigations use
`replaceUrl: true` so typing does not fill the back stack; filter and page navigations push. A
filter or search change always drops `page` (back to 1). Unparseable params (`page=abc`,
`filter=Foo`, `page=0`) are normalised with a `replaceUrl` navigation, never sent to the API.

**Out-of-range page.** If a load returns zero items with `page > 1` and `total > 0` (a block, a
deletion or a new filter shrank the list), navigate with `replaceUrl` to the last page rather than
showing "Brak członków".

---

## Phase 1: The API pages, searches and filters

### Overview

The endpoint takes `search`, `page` and `pageSize`, returns a page envelope with a total, and the
backend test suite stops depending on the whole club fitting in one response.

### Changes Required:

#### 1. Page envelope

**File**: `src/Application/Paging/PagedResult.cs` (new)

**Intent**: A generic page envelope, so the next list that needs paging (exercises, class types —
deferred by the roadmap) reuses it rather than inventing a second shape.

**Contract**: `public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);`
serialises as `{ items, total, page, pageSize }`. `Page` is 1-based.

#### 2. Query seam

**File**: `src/Application/Members/IMemberQuery.cs`

**Intent**: The list method takes the search term and a page and returns a page plus total.

**Contract**: `Task<PagedResult<MemberSummary>> GetMembersAsync(MemberListFilter? filter, string? search, int page, int pageSize, CancellationToken ct)`.
Update the interface doc: the list is paged and searched here, not in the SPA.

#### 3. Handler: validation and defaults

**File**: `src/Application/Members/GetMembers.cs`

**Intent**: Bind `search`, `page`, `pageSize` from the query string, validate, and delegate. Replace
the "No pagination… Search is the SPA's job" paragraph with the S-21 reasoning.

**Contract**: `page` default 1, must be ≥ 1; `pageSize` default 25, must be 1–100; `search` trimmed,
empty/whitespace → no search, longer than 100 chars → 400. Any violation →
`Results.Json(new MemberListFailure("invalid_page"), statusCode: 400)` (or `invalid_search` for the
length), following `ClassRangeResolver.InvalidRange`. Constants (`DefaultPageSize = 25`,
`MaxPageSize = 100`, `MaxSearchLength = 100`) live on the handler so tests and docs name them.

#### 4. Query implementation

**File**: `src/Infrastructure/Members/MemberQuery.cs`

**Intent**: Apply filter, then search, count the result, then order and page before projecting.

**Contract**: search predicate as described in Critical Implementation Details (display name OR
email; email may be null). Order `DisplayName` then `Id`. `Total` from `CountAsync` on the
filtered+searched query (two round-trips, accepted). `Skip((page-1)*pageSize).Take(pageSize)` before
the existing projection; the Roles correlated projection and enum→name mapping are unchanged. A page
beyond the end returns empty `Items` with the true `Total`. Update the class doc's cost note.

#### 5. Test helper and call-site migration

**File**: `tests/po-prostu-silka.Tests/IntegrationTestFixture.cs` (+ the 10 test files listed in
Current State)

**Intent**: One helper that resolves a member by email through `?search=<email>`, so no test assumes
the list fits in one page. Every existing `GetFromJsonAsync<List<MemberBody>>("/api/admin/members")`
lookup moves to it; tests that inspect the list itself read `.items`.

**Contract**: e.g. `Task<Guid> FindMemberIdAsync(HttpClient admin, string email)` returning the
single match, failing loudly on zero or many. Test-local `MemberBody` / `MemberSummaryBody` records
gain a wrapping page record where the envelope is read.

**Adapted during implementation.** Three call sites looked a member up by ID, not by e-mail
(`ClassEndpointTests` ×2 for the instructor's display name, `MyPlanEndpointTests.EmailOfAsync`). They
read `GET /api/admin/members/{id}` (the detail route) instead of the search helper — an id is not
searchable, and the detail route answers exactly that question. The envelope record is one generic
`MemberPageBody<T>` in `IntegrationTestFixture.cs` rather than one per file, and test-local row
records that no longer had a reader were deleted.

#### 6. New integration tests

**File**: `tests/po-prostu-silka.Tests/MemberAdminEndpointTests.cs`

**Intent**: Pin the contract against the real engine. Because the database is shared, each test
creates its members under a unique marker (a GUID fragment in display name and email) and searches for
that marker, so totals are exact.

**Contract**: tests named for the behaviour:
- `Member_list_returns_a_page_envelope_with_the_total` — 3 marked members, `pageSize=2` → page 1 has
  2 items, `total` 3; page 2 has 1.
- `Member_list_pages_are_stable_when_names_tie` — 3 members with the SAME display name, `pageSize=1`,
  pages 1–3 → three distinct ids.
- `Member_list_search_matches_email_substring`
- `Member_list_search_ignores_case_and_polish_diacritics` — "Łukasz", "Żaneta", "Gęślicka" found by
  `lukasz`, `ZANETA`, `geslicka`.
- `Member_list_search_treats_wildcards_literally` — `%` and `_` in the term do not match everything.
- `Member_list_search_composes_with_the_filter` — a blocked and an active marked member,
  `filter=Blocked&search=<marker>` → only the blocked one.
- `Member_list_page_past_the_end_is_empty_with_the_total`
- `Member_list_rejects_invalid_paging` (theory: `page=0`, `pageSize=0`, `pageSize=101`, `search` of 101
  chars → 400).
- Existing `Member_list_includes_admins_with_their_roles` /
  `Member_list_reports_a_granted_trainer_role` keep their assertion through search.

### Success Criteria:

#### Automated Verification:

- Solution builds warning-free: `dotnet build po-prostu-silka.slnx`
- All backend tests pass, including the new list tests: `dotnet test`
- No test resolves a member via an unsearched `GET /api/admin/members`: `rg -n '"/api/admin/members"\)' tests` returns only authorization-matrix entries

#### Manual Verification:

- `GET /api/admin/members?search=lukasz&pageSize=5` against the local DB (Swagger/HTTP file) returns the envelope with a Łukasz in it

**Implementation Note**: The SPA is broken against this API until phase 2 lands. Do not push.

---

## Phase 2: The members screen asks the server

### Overview

The SPA mirrors the envelope, the members screen searches and pages on the server with its state in
the URL, and the picker is kept compiling on a temporary cap.

### Changes Required:

#### 1. Models and service

**File**: `src/app/src/app/core/admin/member-admin.models.ts`, `core/admin/member-admin.service.ts`

**Intent**: Mirror `PagedResult<MemberSummary>` and let the caller say which page, which phrase.

**Contract**: `interface MemberPage { items: Member[]; total: number; page: number; pageSize: number }`;
`getMembers(query: { filter?: MemberFilter; search?: string; page?: number; pageSize?: number }): Promise<MemberPage>`.
Every absent/empty field is OMITTED from the params (same reason the existing doc gives for
`filter`). Update the doc comment that points to the wrong file (`MemberAdminEndpoints.cs` →
`src/Application/Members/MemberSummary.cs`). Service spec asserts the params.

#### 2. Members screen: URL-driven load

**File**: `src/app/src/app/features/admin/members/members.ts`

**Intent**: Replace client-side search with server search driven by the URL; add paging.

**Contract**:
- State is read from `ActivatedRoute.queryParamMap` (reactive): `q`, `filter`, `page`. Each emission
  normalises (invalid → `replaceUrl` navigation and stop) and then calls `load()`; `load()` sends
  `{ filter, search: q, page, pageSize: 25 }` under the existing load fence.
- `rows` holds the page's items; `total`, `page`, `pageSize` signals come from the envelope. The
  `visible` computed and its doc go.
- The search box keeps its own `searchInput` signal (initialised from `q`) and navigates after a
  ~300 ms pause with `replaceUrl: true`, dropping `page`. Implement the debounce with a plain timer
  cleared on each keystroke and on destroy (via `DestroyRef`); nothing in the SPA uses RxJS
  `debounceTime` today, and this does not need it.
- `setFilter` navigates (drops `page`) instead of setting a signal and loading; it keeps closing the
  code panel and clearing `failedId`.
- Pager: `previous()` / `next()` navigate to `page ∓ 1`; disabled at the ends.
- Out-of-range page → `replaceUrl` navigation to the last page (see Critical Implementation Details).
- `mutate`'s and `handleCodeFailure`'s refetch-on-409 keep calling `load()` — it now reloads the
  current page, which is what they meant.
- Update the class doc: the S-02 "search filters the loaded rows" split is superseded by S-21.

#### 3. Members screen: template

**File**: `src/app/src/app/features/admin/members/members.html`

**Intent**: Wire the search box to `searchInput`; add the pager and count; keep the list markup as is
(phase 4 changes it).

**Contract**: under the list, a `nav` with `aria-label="Strony listy członków"` holding
`{{ from }}–{{ to }} z {{ total }}` and two `button`s, Poprzednia / Następna. Hidden when
`total <= pageSize`. The count text sits in a `role="status"` element so a screen reader hears the
new range after a page change. Empty-state wording is unchanged (it still distinguishes "search"
from "view").

#### 4. Picker: temporary cap

**File**: `src/app/src/app/features/admin/classes/class-bookings-overlay.ts`

**Intent**: Keep it compiling and working until phase 3: `getMembers({ filter: 'Active', pageSize: 100 })` → `.items`.

**Contract**: a `// TEMPORARY (S-21 phase 2)` comment naming phase 3; the spec's expected URL updates.

#### 5. Specs

**File**: `members.spec.ts`, `member-admin.service.spec.ts`, `class-bookings-overlay.spec.ts`

**Intent**: Replace the client-side search tests with request-shape tests; add paging and URL tests.

**Contract**: tests for — typing sends ONE request after the pause, with `search` and without `page`;
a stale response (older generation) is discarded; a chip click navigates with `filter` and no `page`;
Następna requests `page=2`; the pager is hidden when everything fits; `page=abc` in the URL is
normalised without an API call carrying it; an empty page 3 of a 2-page result lands on page 2;
a 409 on block reloads the CURRENT page. Use Vitest fake timers for the debounce.

**Adapted during implementation.** `load()` leaves `page` off the request on page 1 (the API's
default), so the first page's request is the same however the admin arrived at it; `page=1` in the
URL is normalised away like any other non-canonical value. `classes.spec.ts` also had to move — it
opens the bookings overlay and answered the picker's member request, so its expected URL follows the
phase-2 cap (and goes away in phase 3).

### Success Criteria:

#### Automated Verification:

- SPA unit tests pass: `npm test` (from `src/app/`)
- Lint and format pass: `npm run quality:check` (from `src/app/`)
- Production build succeeds within budget: `npm run build` (from `src/app/`)
- No client-side member search remains: `rg -n "toLocaleLowerCase\(\)\.includes" src/app/src/app/features/admin/members` returns nothing

#### Manual Verification:

- With 60+ members seeded locally, typing "kow" issues one request after the pause and shows matches
- Następna/Poprzednia move through pages; the count reads correctly
- Open "Edytuj dane" from page 3 with a phrase, go back: same page, phrase and filter
- Reloading `/admin/members?q=kow&page=2` restores that view

**Implementation Note**: The picker is on a 100-row cap. Do not push.

---

## Phase 3: The booking picker searches instead of loading the club

### Overview

The "Dopisz członka" picker in the class-bookings overlay becomes a server search over active members.

### Changes Required:

#### 1. Overlay logic

**File**: `src/app/src/app/features/admin/classes/class-bookings-overlay.ts`

**Intent**: Replace `loadCandidates()` with a debounced search: an `addSearch` signal, ~300 ms pause,
`getMembers({ filter: 'Active', search, pageSize: 20 })`, results through a load fence of its own
(an independent load, per AGENTS.md's one-fence-per-load rule). `bookable` still excludes people
already holding a spot. Empty/whitespace phrase → no request, no results. A failed search stays quiet,
as `loadCandidates` did — the panel's job is showing who is coming.

**Contract**: `chosen` resets when results change and the chosen id is no longer among them. After
a successful `add()`, the phrase and results clear. Remove the TEMPORARY comment and the phase-2 cap.

#### 2. Overlay template

**File**: `src/app/src/app/features/admin/classes/class-bookings-overlay.html`

**Intent**: The add block always renders (whether anyone is left can no longer be known without a
search). A search input (`type="search"`, in an `app-field` labelled "Dopisz członka") sits above
the existing `app-select`, which lists the matches.

**Contract**: the select's placeholder option reads "Wpisz imię lub e-mail…" with no phrase,
"Wybierz osobę…" with matches, and a phrase with no bookable match renders an `app-empty` line
("Nikt pasujący nie może zostać dopisany.") instead of an empty select. When `total` exceeds the 20
shown, a hint says to narrow the phrase. Update the block comment ("Hidden entirely when there is
nobody left to add") to the new rule.

#### 3. Spec

**File**: `src/app/src/app/features/admin/classes/class-bookings-overlay.spec.ts`

**Intent**: Opening the overlay no longer requests members; typing does, once, with
`filter=Active&search=…&pageSize=20`; booked people are excluded from the options; the empty-match
line renders; a successful add clears the phrase.

### Success Criteria:

#### Automated Verification:

- SPA unit tests pass: `npm test`
- Lint and format pass: `npm run quality:check`
- Nothing in the SPA requests the member list without a page size or a search: `rg -n "getMembers\(" src/app/src/app --glob '!*.spec.ts'` shows only the members screen and the overlay's search call

#### Manual Verification:

- Open a class's Zapisani overlay, type part of a name without Polish letters, pick the person, Zapisz — they appear in the roster and the phrase clears
- Someone already booked does not appear in the matches

**Implementation Note**: Phases 1–3 are now deployable together. Pushing is safe from here.

---

## Phase 4: A table on the web, one compact row on a phone

### Overview

The list becomes one semantic `<table>` whose rows collapse, in CSS, into compact rows below the
`form-columns` breakpoint. No new breakpoint, no second template.

### Changes Required:

#### 1. Template

**File**: `src/app/src/app/features/admin/members/members.html`

**Intent**: Replace `app-list` / `li[appRow]` with `<table class="members-table">`: a visually hidden
`<caption>` ("Członkowie klubu"), a `thead` with Imię i nazwisko · Status · E-mail · Od · Akcje
(the last as a visually hidden header), and one `tr` per member. The name cell holds the name; the
status cell holds the status badge and role badges; the actions cell holds the unchanged row menu,
failure line and code panel.

**Contract**: all action markup and handlers move verbatim — only their container changes. Add a
comment at the table explaining why this screen is a table and not `app-list` (columns an admin
compares down the page; the kit's lint rule does not cover tables by design).

#### 2. Styles

**File**: `src/app/src/app/features/admin/members/members.scss`

**Intent**: Table layout at `bp.form-columns` and up; below it, `table`, `tbody`, `tr`, `td` become
blocks, `thead` is visually hidden, each `tr` is a compact card-like row carrying name, badges and
Akcje on one line where it fits, and the e-mail and Od cells are hidden.

**Contract**: widths only via `@include bp.form-columns` (and the existing `bp.narrow` block for the
menu); no literal media query (`breakpoints.spec.ts` fails on one). The row menu keeps
`position: relative` on its wrapper so the dropdown still anchors to the button in a cell. The
existing `.row-*` global classes are no longer used here; do not delete them from `styles.scss` —
other screens use them.

#### 3. Breakpoint partial note

**File**: `src/app/src/styles/_breakpoints.scss`

**Intent**: `form-columns` now also decides the member table; say so in its comment so a later change
to "the form breakpoint" knows the table moves with it.

#### 4. Docs

**File**: `AGENTS.md`, `context/foundation/roadmap.md`

**Intent**: In "The presentational kit (S-23)", one sentence: a data table with columns
(`/admin/members`, S-21) is a `<table>`, not `app-list`, and collapses to rows in CSS. In the roadmap,
record the S-21 unknowns as answered (chips stay chips; search debounced ~300 ms, substring, no
index).

#### 5. Specs

**File**: `members.spec.ts`

**Intent**: Queries that found rows as `listitem` now find `row`s in the table; the column headers
exist; every action is still reachable from a row's menu.

### Success Criteria:

#### Automated Verification:

- SPA unit tests pass: `npm test`
- Lint and format pass (including the breakpoint literal check): `npm run quality:check`
- Production build succeeds; initial bundle stays under the 600 kB warning: `npm run build`

#### Manual Verification:

- At ≥ 640px the list is a table with aligned columns; the Akcje menu opens inside its cell and is not clipped
- At 360px each member is one compact row: name, status (and role) badges, Akcje; no horizontal scroll
- Every menu action (Edytuj dane, Karnety, codes, Trener, Zablokuj/Odblokuj) works at both widths
- Screen reader (NVDA or VoiceOver) announces the column header when moving across cells at desktop width

---

## Testing Strategy

### Unit Tests:

- Service: params omitted when absent, present when given; envelope typed.
- Members screen: debounce (one request per pause), load fence on out-of-order responses, URL
  normalisation, filter/search drop `page`, pager ends, out-of-range page redirect, 409 reloads the
  current page.
- Overlay: search-only requests, exclusion of booked members, empty-match line, clear after add.

### Integration Tests:

- The eight `MemberAdminEndpointTests` listed in Phase 1 against real SQL Server (Testcontainers), in
  particular the diacritic-folding and tie-stability tests, which only a real engine can answer.

### Manual Testing Steps:

1. Seed 60+ members locally (including "Łukasz", "Żaneta", two identical names).
2. Search `lukasz`, `zaneta`, part of an e-mail; confirm one request per pause in DevTools.
3. Page through; confirm the two identical names each appear exactly once.
4. Filter Zablokowani, then search; edit a member from page 2 and go back.
5. Block the only member on the last page under the Aktywni filter; reload — lands on the new last page.
6. Book someone into a class via the overlay search.
7. Resize 1280 → 640 → 360 and exercise the Akcje menu at each width.

## Performance Considerations

Two queries per load (count + page), each a scan of `Members` when searching — acceptable at one
club's scale and bounded by the 300 ms debounce. The previous cost (every row, every visit, with a
correlated Roles projection for each) goes away. No index is added: a substring match cannot use one.

## Migration Notes

No schema migration. The response shape of `GET /api/admin/members` changes in place; the SPA that
reads it ships in the same artifact. An admin tab still running the previous SPA through the service
worker shows the list's load-failure state until it reloads.

## References

- Roadmap: `context/foundation/roadmap.md` — M-7 (UX-05, UX-06, UX-09), S-21
- Current list query: `src/Infrastructure/Members/MemberQuery.cs:29-82`
- 400 pattern: `src/Application/Scheduling/ClassRangeResolver.cs:48-49`
- Client search being replaced: `src/app/src/app/features/admin/members/members.ts:106-121`
- Second consumer: `src/app/src/app/features/admin/classes/class-bookings-overlay.ts:86-125`
- Breakpoints: `src/app/src/styles/_breakpoints.scss`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: The API pages, searches and filters

#### Automated

- [x] 1.1 Solution builds warning-free: `dotnet build po-prostu-silka.slnx` — e945a2b
- [x] 1.2 All backend tests pass, including the new list tests: `dotnet test` — e945a2b
- [x] 1.3 No test resolves a member via an unsearched `GET /api/admin/members` — e945a2b

#### Manual

- [ ] 1.4 `GET /api/admin/members?search=lukasz&pageSize=5` returns the envelope with a Łukasz in it

### Phase 2: The members screen asks the server

#### Automated

- [x] 2.1 SPA unit tests pass: `npm test`
- [x] 2.2 Lint and format pass: `npm run quality:check`
- [x] 2.3 Production build succeeds within budget: `npm run build`
- [x] 2.4 No client-side member search remains

#### Manual

- [ ] 2.5 Typing "kow" issues one request after the pause and shows matches
- [ ] 2.6 Następna/Poprzednia move through pages; the count reads correctly
- [ ] 2.7 Back from "Edytuj dane" restores page, phrase and filter
- [ ] 2.8 Reloading `/admin/members?q=kow&page=2` restores that view

### Phase 3: The booking picker searches instead of loading the club

#### Automated

- [ ] 3.1 SPA unit tests pass: `npm test`
- [ ] 3.2 Lint and format pass: `npm run quality:check`
- [ ] 3.3 Nothing in the SPA requests the member list without a page size or a search

#### Manual

- [ ] 3.4 Booking a member through the overlay search works and clears the phrase
- [ ] 3.5 Someone already booked does not appear in the matches

### Phase 4: A table on the web, one compact row on a phone

#### Automated

- [ ] 4.1 SPA unit tests pass: `npm test`
- [ ] 4.2 Lint and format pass (including the breakpoint literal check): `npm run quality:check`
- [ ] 4.3 Production build succeeds under the 600 kB warning: `npm run build`

#### Manual

- [ ] 4.4 At ≥ 640px the list is a table; the Akcje menu opens unclipped
- [ ] 4.5 At 360px each member is one compact row with no horizontal scroll
- [ ] 4.6 Every menu action works at both widths
- [ ] 4.7 Screen reader announces column headers at desktop width
