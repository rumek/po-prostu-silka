# Expiring Karnets Dashboard Card Implementation Plan

## Overview

The club's spreadsheet (`xlsx/twt.xlsx`, sheet "Klienci") marks a client "KOŃCZY SIĘ" from five days
before their karnet's end date, so that nobody misses a renewal. This change brings the same signal into
the app as an admin-only card on Start, "Kończą się karnety". The card lists up to five members, nearest
end first, and each row leads to that member's karnet screen. A "Zobacz wszystkich (N)" link opens the
member list narrowed by a new `expiring` filter. One Infrastructure predicate drives both, so the N on
the card is always the list's total.

## Current State Analysis

- **No notion of "ending" exists.** `MemberSummary` carries the karnet covering today (`PassValidTo`,
  `PassEntriesLeft`), derived in `MemberQuery.GetMembersAsync`
  (`src/Infrastructure/Members/MemberQuery.cs:427-438`). Nothing compares that end date against today.
- **The filter mechanism to reuse is pass-paid-flag's `unpaid`.** On the server it is a boolean
  orthogonal to `MemberListFilter`, applied before the count (`MemberQuery.cs:381-392`) and bound in
  `GetMembers.HandleAsync` as `bool? unpaid` (`src/Application/Members/GetMembers.cs:863-881`). In the
  SPA it is `unpaid=1` in the URL, parsed by `members.ts:67-91` and written by `navigate()`
  (`members.ts:499`). The control is an `app-checkbox` inside an `app-field` (`members.html:53-57`).
  `MemberAdminService.getMembers` sends it only when set (`member-admin.service.ts:64-67`).
- **Shared predicates already exist.** `StaffPredicate` (`src/Infrastructure/Members/StaffPredicate.cs`)
  is an `Expression<Func<Member, bool>>` built against `AppDbContext`, and the write refusals and the
  lists both read it. The new predicate follows the same shape.
- **Club-local today** is computed as `DateOnly.FromDateTime(ClubTime.ToClubLocal(timeProvider.GetUtcNow()).DateTime)`
  (`MemberQuery.cs:396`), with `TimeProvider.System` registered in `src/Api/Program.cs:239`.
- **The dashboard branches on persona** (`features/dashboard/dashboard.ts`). The staff half
  ("Twoje zajęcia") is shared by trainers and admins, and nothing on it is admin-only yet. Each card
  has its own `createLoadFence()` and a loading/failed/value trio of signals, and requests the staff
  persona's API won't serve are pinned with `expectNone` in `dashboard.spec.ts`.
- **The integration tests share one database.** `IntegrationTestFixture` is an `ICollectionFixture`
  (`tests/po-prostu-silka.Tests/IntegrationTestFixture.cs:492-493`). The list tests isolate themselves
  with a unique `search`, but a club-wide "top 5" endpoint has no search to hide behind.
  `TestDataSeederCollection` (`TestDataSeederTests.cs:24-25`) is the precedent for a collection with
  its own container.
- **Seed data already covers the card.** `TestDataGenerator` issues current karnets ending at assorted
  dates and renews 30% of them back-to-back (`src/Infrastructure/TestData/TestDataGenerator.cs:320-334`),
  so staging shows both listed and renewed members with no seed change.
- **Bundle:** 580.70 kB after pass-paid-flag phase 4, against a 600 kB warning (AGENTS.md). Both the
  dashboard and `/admin/members` are eager.

## Desired End State

- An admin landing on `/` sees a "Klub" section **above** "Twoje zajęcia" with the card "Kończą się
  karnety". It lists up to 5 members whose karnet is ending, nearest end first, ties by name. Each row
  shows the name, "dziś" / "jutro" / "za N dni" and the end date, and links to `/admin/members/:id/passes`.
- With more than 5 qualifying, a "Zobacz wszystkich (N)" link opens `/admin/members?expiring=1`. That
  list's total equals N.
- With none, the card stays and shows a compact `app-empty`.
- A trainer's Start is unchanged and never requests the expiring endpoint.
- The member list has a "Tylko kończące się" checkbox. It combines with the status, role, unpaid and
  search filters, lives in the URL as `expiring=1`, and is cleared by "Wyczyść filtry".

**"Ending" (the one predicate):** the member's membership is `Active`, the member is not staff
(`StaffPredicate`), and they hold a karnet with `ValidFrom ≤ today ≤ ValidTo` and `ValidTo ≤ today + 5`,
**and** no karnet of theirs has `ValidFrom > that ValidTo`. "today" is club-local. The window is
inclusive at both ends (0…5 days), matching the spreadsheet's `G>=TODAY() AND G<=TODAY()+5`.
Paid/unpaid and entries left play no part.

### Key Discoveries:

- Spreadsheet status formula: `IF(H<>"TAK","BRAK PŁATNOŚCI",…IF(G<TODAY(),"WYGASŁ",IF(G<=TODAY()+5,"KOŃCZY SIĘ","AKTYWNY")))`.
  The sheet lets "unpaid" mask "ending". The app deliberately does not (decision below).
- Passes may not overlap (`MembershipPass` remarks), so "a karnet starting after the current one's
  `ValidTo`" is exactly "a later karnet". A gap between the two still counts as renewed.
- `MemberQuery.Filtered`: membership status alone answers "blocked", because a block sets both
  statuses (`MemberQuery.cs:576-579`).
- `/admin/members/:id/passes` is declared at `app.routes.ts:101` and is lazy.

## What We're NOT Doing

- **No configurable threshold.** It is a fixed 5 days, a constant beside the predicate.
- **No "WYGASŁ" line.** Already-expired karnets are not shown anywhere new.
- **No entry-based "running low" signal.** A karnet with 0 entries left but 10 days to go is not ending,
  and one with entries left ending in 3 days is.
- **No unpaid on the card.** Unpaid stays with pass-paid-flag's marker and filter. An unpaid karnet that
  is ending is listed like any other.
- **No row marker on the member list.** Only the filter. The row already shows "do <data>", and both
  screens are eager.
- **No re-sorting of the member list** under the filter. It stays alphabetical with the id tiebreak.
- **Not for trainers.** They have no member list with karnets to link to.
- **A karnet that has not started is never "ending".** A member with nothing covering today and a
  karnet running from tomorrow for three days is not listed. There is no current karnet to renew, and
  the club issued that one days ago.
- **No notifications.** No e-mail or push about an ending karnet.
- **No roadmap edit.** No roadmap item carries this Change ID.

## Implementation Approach

Build the predicate first and pin every counterexample against it through the existing list endpoint,
which supports `search` isolation. Then add the card endpoint on the same predicate, in its own
test collection. Then the SPA follows, list before card, because the card links into the list's new
URL state. E2E comes last, once both screens exist.

## Critical Implementation Details

- **One `today` per request.** The card endpoint computes club-local today once and uses it for both the
  predicate and `daysLeft`. The SPA never recomputes days from the browser clock: around midnight the
  browser's date and the club's can differ, and "dziś" next to a list that disagrees is the bug to avoid.
- **The card's N and the list's total must be the same SQL predicate**, not two equivalent ones. The list
  applies the predicate before `CountAsync`, as `unpaidOnly` does. The card counts the same
  `IQueryable` it takes its 5 rows from.
- **Bundle.** Measure `ng build` before phase 2 and after each of phases 2 and 3, and record both
  numbers in AGENTS.md's budget paragraph in the same phase. That paragraph exists because unrecorded
  growth happened once already.

## Phase 1: Predicate, list filter and card endpoint

### Overview

The server side, complete: the one definition of "ending", the `expiring` flag on the member list, the
card's read endpoint, and integration tests covering every edge case.

### Changes Required:

#### 1. The predicate

**File**: `src/Infrastructure/Members/ExpiringPassPredicate.cs` (new)

**Intent**: The one Infrastructure definition of "this member's karnet is ending", read by both the list
filter and the card. Its doc comment explains why this exists, in the voice of `StaffPredicate`: the
spreadsheet signal, why a renewal excludes, and why paid and entries don't count.

**Contract**: `public static class ExpiringPassPredicate` with `public const int WindowDays = 5;`,
`Expression<Func<Member, bool>> IsExpiring(AppDbContext db, DateOnly today)` (status Active, not staff
via `StaffPredicate.IsNotStaff`, current karnet ending within `today..today+WindowDays` inclusive, no
later karnet), and an expression or helper the card uses to project that current karnet's `ValidTo`.
Built as one expression tree so EF translates it as correlated `EXISTS` subqueries.

#### 2. The list filter

**Files**: `src/Infrastructure/Members/MemberQuery.cs`, `src/Application/Members/IMemberQuery.cs`,
`src/Application/Members/GetMembers.cs`

**Intent**: `expiring=true` narrows the admin's member list to the predicate. It is orthogonal to the
status, role and unpaid filters and applied before the count, exactly like `unpaidOnly`.

**Contract**: `GetMembersAsync(…, bool unpaidOnly, bool expiringOnly, int page, …)`.
`GetMembers.HandleAsync` binds `bool? expiring`. The `today` already computed in `GetMembersAsync` moves
above the filters so the predicate and the `PassValidTo` projection use one value.

#### 3. The card endpoint

**Files**: `src/Application/Members/GetExpiringPasses.cs` (new), `src/Application/Members/ExpiringPasses.cs`
(new, the response records), `IMemberQuery.cs`, `MemberQuery.cs`, `src/Api/Endpoints/Members/MemberAdminEndpoints.cs`

**Intent**: The card's read. The nearest few ends plus the total, from the predicate, in one place.
Admin-only by living in the `/api/admin/members` group (policy `Admin`), the same persona as the card.

**Contract**: `GET /api/admin/members/expiring-passes` → `200 { items: ExpiringPass[], total: int }`,
where `ExpiringPass(Guid MemberId, string DisplayName, DateOnly ValidTo, int DaysLeft)`. There are at
most `GetExpiringPasses.CardSize = 5` items, ordered `ValidTo`, then `DisplayName`, then `Id`.
`IMemberQuery.GetExpiringPassesAsync(int take, CancellationToken)`. No query parameters. The route is
mapped before `/{memberId:guid}`, although the guid constraint already keeps them apart.

#### 4. Tests

**File**: `tests/po-prostu-silka.Tests/MemberEndpointTests.cs` (predicate through the list, shared
fixture, unique `search` per test)

**Intent**: Pin every counterexample decided in planning, so a later "simplification" of the predicate
fails loudly.

**Contract**: one test each:
- ends today → listed
- ends today+5 → listed
- ends today+6 → not listed
- ended yesterday → not listed
- renewed back-to-back → not listed
- renewed after a gap → not listed
- unpaid and ending → listed
- 0 entries left and ending → listed
- blocked → not listed
- promoted to Trainer while holding an ending karnet → not listed
- only a future karnet ending within the window → not listed
- `expiring` combines with `unpaid` and `filter=Active`

Dates are relative to club-local today, computed in the test the way `MemberQuery` does.

**File**: `tests/po-prostu-silka.Tests/ExpiringPassesEndpointTests.cs` (new, own
`[CollectionDefinition]` with its own `IntegrationTestFixture`, as `TestDataSeederTests.cs:24-25` does)

**Intent**: The card's club-wide behaviour, which a shared database would pollute.

**Contract**:
- empty club → `total 0`, no items
- 6 ending → 5 items and `total 6`
- the 5th and 6th share a `ValidTo` → the alphabetically first is on the card
- `daysLeft` is 0 for today and 5 for today+5
- `total` equals `GET /api/admin/members?expiring=true` total
- a trainer gets 403, and an anonymous caller gets 401

### Success Criteria:

#### Automated Verification:

- Build passes with no new warnings: `dotnet build po-prostu-silka.slnx`
- All backend tests pass, the new ones included: `dotnet test po-prostu-silka.slnx`

#### Manual Verification:

- `GET /api/admin/members/expiring-passes` against the local DB with seeded test data returns
  plausible rows, and none of them has a later karnet on their passes screen

**Implementation Note**: After completing this phase and all automated verification passes, pause here
for manual confirmation before proceeding to the next phase.

---

## Phase 2: "Tylko kończące się" on the member list

### Overview

The list side of the shared predicate. The card's "Zobacz wszystkich" lands here.

### Changes Required:

#### 1. Service and models

**Files**: `src/app/src/app/core/admin/member-admin.models.ts`, `src/app/src/app/core/admin/member-admin.service.ts`

**Intent**: `MemberQuery.expiring`, sent only when set, like `unpaid`. Plus the card's read and its
types, so phase 3 only consumes them.

**Contract**: `MemberQuery.expiring?: boolean` → `expiring=true`. Interfaces `ExpiringPass { memberId,
displayName, validTo, daysLeft }` and `ExpiringPasses { items, total }`. `getExpiringPasses(): Promise<ExpiringPasses>`
→ `GET /api/admin/members/expiring-passes`.

#### 2. URL state and control

**Files**: `src/app/src/app/features/admin/members/members.ts`, `members.html`

**Intent**: `expiring=1` works the same way as `unpaid=1`: parsed, canonicalised, written, counted by
`narrowed()`, reset by `clearFilters()`, and toggled by an `app-checkbox` "Tylko kończące się" in its
own `app-field` labelled "Ważność", the next grid cell after "Płatność".

**Contract**: `ListState.expiring: boolean`. The URL value is `1` or absent, and any other value is
dropped as non-canonical (`replaceUrl`). The class doc's "The URL is the list's state" section names
the new parameter.

#### 3. Specs

**Files**: `members.spec.ts`, `member-admin.service.spec.ts`

**Intent**: The URL round-trip (`?expiring=1` → request carries `expiring=true` and the box is checked),
junk canonicalisation (`expiring=yes` → dropped), combination with `unpaid=1`, "Wyczyść filtry" clears
it, and the service omits the parameter when false.

### Success Criteria:

#### Automated Verification:

- SPA specs pass: `npm test` (from `src/app/`)
- Lint and format pass: `npm run quality:check` (from `src/app/`)
- Production build succeeds with the initial bundle measured and recorded in AGENTS.md: `npx ng build`
  (from `src/app/`)

#### Manual Verification:

- On `/admin/members`, ticking "Tylko kończące się" narrows the list, survives a reload, combines with
  "Tylko nieopłacone", and "Wyczyść filtry" clears it
- The controls card still lays out cleanly at phone, tablet and desktop widths

**Implementation Note**: After completing this phase and all automated verification passes, pause here
for manual confirmation before proceeding to the next phase.

---

## Phase 3: "Kończą się karnety" on Start

### Overview

The admin's card, the user-visible point of the change.

### Changes Required:

#### 1. The card

**Files**: `src/app/src/app/features/dashboard/dashboard.ts`, `dashboard.html`, `dashboard.scss`

**Intent**: For persona `admin` only, a section "Klub" rendered **above** the staff section, holding one
block "Kończą się karnety". It is loaded in parallel with `loadClasses()` with its own fence, and has
the dashboard's loading / failed-with-retry / rows / `app-empty compact` states. Rows are
`app-list` / `li[appRow]` (name in `.row-name`, "dziś" / "jutro" / "za N dni" and the date in
`.row-meta`), each one a link to `/admin/members/:memberId/passes`. When `total > items.length`,
"Zobacz wszystkich (N)" goes to `/admin/members` with `queryParams { expiring: 1 }`. Its placement
follows the "Najbliższe zajęcia" pattern: the header action on a phone and the bottom edge on wider
screens. Icons come from `shared/icons` (`ticket` / `calendar`). Styling follows
`context/foundation/ui-style-guide.md`.

**Contract**: `isAdmin = computed(() => persona() === 'admin')`. The days label comes from the server's
`daysLeft` (0 → "dziś", 1 → "jutro", n → "za n dni"; every n in 2…5 takes "dni"). Never computed from
the browser date. The class comment's "ONE ROUTE, TWO AUDIENCES" list gains the admin's extra block.

#### 2. Spec

**File**: `src/app/src/app/features/dashboard/dashboard.spec.ts`

**Intent**: Persona gating, which is the S-25 rule this card could break.

**Contract**:
- An admin requests the expiring endpoint once; a trainer and a member never do (`expectNone`).
- The rows render the server's order and labels for daysLeft 0, 1 and 4.
- Each row links to the member's passes route.
- "Zobacz wszystkich (7)" appears with total 7 and 5 items, and is absent with total 5 and 5 items.
- An empty response renders `app-empty`.
- A failure renders the alert and a retry reloads it.

#### 3. Bundle record

**File**: `AGENTS.md` (the initial-bundle paragraph)

**Intent**: Record the measured figure after this phase and the delta per phase, as every slice since
S-12 has.

### Success Criteria:

#### Automated Verification:

- SPA specs pass: `npm test`
- Lint and format pass: `npm run quality:check`
- Production build succeeds under the 600 kB warning, with the figure recorded in AGENTS.md:
  `npx ng build`
- Backend tests still pass: `dotnet test po-prostu-silka.slnx`

#### Manual Verification:

- As `admin1@example.test` on seeded data: the card shows up to 5 rows nearest-first, a row opens that
  member's karnet screen, the bar's back arrow returns to Start, and "Zobacz wszystkich (N)" opens a
  list whose count reads N
- As a trainer: Start shows no "Klub" section, and the network log has no `expiring-passes` request
- Phone (≤30rem), tablet and desktop: the card reads cleanly, and on a phone "Zobacz wszystkich" sits
  in the card header

**Implementation Note**: After completing this phase and all automated verification passes, pause here
for manual confirmation before proceeding to the next phase.

---

## Phase 4: E2E: renewing removes a member from the card

### Overview

The one risk that only a browser sees end to end. Issuing the next karnet on the passes screen must take
the member off the card that sent the admin there. This is driven with `/10x-e2e`, local only, under
the rules in `src/app/e2e/CLAUDE.md`.

### Changes Required:

#### 1. The spec

**File**: `src/app/e2e/renewal-clears-expiring-card.spec.ts` (new)

**Intent**: Set up a unique `E2E …` member through the API with a karnet ending in 2 days. On Start the
card shows them, and "za 2 dni" is visible when they are within the first 5. They are always in the
list behind `?expiring=1`, which is searched by name. Open their row (or their list entry), issue a
karnet starting the day after the current one ends, and return to Start: the member is gone from the
card and from the filtered list. The spec cleans up after itself.

**Contract**: Locators are `getByRole` / `getByLabel` / `getByText`, with no `waitForTimeout`. The
assertion never depends on how many other members happen to be ending in the local DB. It searches
`/admin/members?expiring=1` for the unique name rather than counting the card.

### Success Criteria:

#### Automated Verification:

- The new spec passes locally: `npx playwright test e2e/renewal-clears-expiring-card.spec.ts` (from
  `src/app/`, with `docker compose up -d`)
- The full E2E suite passes: `npm run e2e`

#### Manual Verification:

- The spec was reviewed against the `/10x-e2e` five anti-patterns

---

## Testing Strategy

### Unit Tests:

- `dashboard.spec.ts`: persona gating, order, labels, the "see all" threshold, empty and failure states
- `members.spec.ts`: the `expiring` URL state round-trip, canonicalisation, combination and clearing
- `member-admin.service.spec.ts`: the parameter is omitted when false, and the card's GET

### Integration Tests:

- `MemberEndpointTests`: the predicate's twelve cases through `?expiring=true&search=…`
- `ExpiringPassesEndpointTests` (own container): top 5, total, tiebreak by name, `daysLeft`, total
  equal to the list's total, and the 401/403 authorization

### Manual Testing Steps:

1. `docker compose up -d`, then run the API with `TestDataSeed:Enabled=true` and sign in as `admin1@example.test`.
2. Start: "Klub → Kończą się karnety" sits above "Twoje zajęcia", and rows are nearest-first.
3. Open a row, then issue the next karnet. Back on Start the member is gone and N dropped by one.
4. "Zobacz wszystkich (N)" → `/admin/members?expiring=1` shows N and the checkbox is ticked.
5. Sign in as a trainer: no card, and no `expiring-passes` request.

## Performance Considerations

The predicate is two correlated `EXISTS` subqueries per member over `MembershipPasses`, which is
indexed by member through its foreign key. At one club's scale that costs nothing. The card makes one
extra request on an admin's Start, in parallel with the classes feed, so it adds no serial latency.

## Migration Notes

None. There is no schema change: "ending" is derived from existing columns.

## References

- Change identity and the decisions made before planning: `context/changes/expiring-passes-dashboard/change.md`
- Mechanism reused: `context/changes/pass-paid-flag/plan.md`, `src/Infrastructure/Members/MemberQuery.cs:381-392`
- Predicate pattern: `src/Infrastructure/Members/StaffPredicate.cs`
- Spreadsheet rule: `xlsx/twt.xlsx`, sheet "Klienci", column K

**Prerequisite:** `pass-paid-flag` is merged into `main` and this change branches from there.

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Predicate, list filter and card endpoint

#### Automated

- [x] 1.1 Build passes with no new warnings — 255f4e4
- [x] 1.2 All backend tests pass, the new ones included — 255f4e4

#### Manual

- [ ] 1.3 Expiring endpoint returns plausible rows on seeded data, none renewed

### Phase 2: "Tylko kończące się" on the member list

#### Automated

- [x] 2.1 SPA specs pass
- [x] 2.2 Lint and format pass
- [x] 2.3 Production build succeeds, bundle measured and recorded

#### Manual

- [ ] 2.4 Checkbox narrows, survives reload, combines with unpaid, clears
- [ ] 2.5 Controls card lays out at phone, tablet and desktop widths

### Phase 3: "Kończą się karnety" on Start

#### Automated

- [ ] 3.1 SPA specs pass
- [ ] 3.2 Lint and format pass
- [ ] 3.3 Production build under the warning, figure recorded in AGENTS.md
- [ ] 3.4 Backend tests still pass

#### Manual

- [ ] 3.5 Admin card: rows, link to passes, back, "Zobacz wszystkich (N)" matches the list
- [ ] 3.6 Trainer: no card, no expiring-passes request
- [ ] 3.7 Card reads cleanly at phone, tablet and desktop

### Phase 4: E2E: renewing removes a member from the card

#### Automated

- [ ] 4.1 New spec passes locally
- [ ] 4.2 Full E2E suite passes

#### Manual

- [ ] 4.3 Spec reviewed against the five anti-patterns
