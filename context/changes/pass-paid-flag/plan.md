# Mark a Karnet as Paid or Unpaid — Implementation Plan

## Overview

The club's spreadsheet carries a "Zapłacono?" column and a derived "BRAK PŁATNOŚCI" status; the app
has nothing equivalent. This change adds one human assertion to `MembershipPass` — **when it was
paid** (`PaidAt`, a club-local day, null = unpaid) and **who said so** (`PaidRecordedBy`) — lets an
admin set it when issuing a karnet, lets an admin **or a trainer** mark any non-staff member's karnet
paid / unpaid afterwards through one narrow route, shows staff a "Nieopłacony" marker with a filter,
and shows the member the status of the karnet on their dashboard. Paying is **not** a gate: an
unpaid karnet books exactly like a paid one.

## Current State Analysis

Grounded in `research.md` (same folder), spot-checked against the code on 2026-10-02.

- `MembershipPass` (`src/Domain/Members/MembershipPass.cs`) has no payment field. `IssuedAt` is a UTC
  `DateTimeOffset` stored as `datetimeoffset`; `ValidFrom` / `ValidTo` are `DateOnly` mapped
  explicitly to `date` (`MembershipPassConfiguration.cs:31-32`).
- Every pass route is under the `Admin` policy (`MembershipPassEndpoints.cs:43-45`); the PUT is a
  **full edit** sharing `IssuePassRequest` with the POST (`IssuePassRequest.cs:17-21`,
  `UpdatePass.cs:74-77`). A trainer has no pass access at all — neither API nor screen.
- `MembershipPassView` (`MembershipPassView.cs:27-36`) is built in three places:
  `MembershipPassProjection.ViewOfAsync`, `MembershipPassQuery.GetForMemberAsync` and
  `MembershipPassQuery.FindCoveringAsync`. `/api/passes/mine` returns the third, so the member's card
  gets any new view field for free.
- The trainer's member list (`/api/trainer/members`, `TrainerMemberEndpoints.cs`) lists **active,
  non-staff** members (`TrainingPlanQuery.Assignable`, `:35-41`), unscoped by class, name-only search;
  its row (`trainer-members.html:42-60`) links to the plan builder at `/trainer/members/:id/plan` —
  the precedent for a per-member trainer child screen.
- The admin list's pass columns describe **today's** covering karnet (`MemberSummary`,
  `MemberQuery.cs:98-109`); filters are a pinned single-select enum (`MemberListFilter`) plus a role
  filter.
- `IssuePass` refuses a blocked member (`member_blocked`) and staff (`member_is_staff`,
  `IssuePass.cs:46-55`) via `IMemberStore.IsStaffAsync`.
- Audit precedent: `Booking.AttendanceRecordedBy` — `nvarchar(450)`, **no FK**, set from
  `ClaimTypes.NameIdentifier` (`RecordAttendance.cs:138`, `BookingConfiguration.cs:30-33`).
- `app-checkbox` has no `ControlValueAccessor` (`shared/forms/checkbox/checkbox.ts`), and no form in
  the app has a checkbox yet.
- `context/foundation/roadmap.md` has no item for this change; PRD `prd.md:202` states "No pass/
  membership sales, payments, subscriptions, or invoices".

## Desired End State

- Issuing a karnet shows an unchecked **"Opłacony"** checkbox; ticking it reveals a payment date
  defaulting to today (club-local). The new pass is stored with `PaidAt` (or null) and, if paid,
  `PaidRecordedBy` = the admin's user id.
- `PUT /api/passes/{passId}/paid` with `{ "paidAt": "2026-10-02" }` or `{ "paidAt": null }` is
  reachable by **Admin and Trainer** (policy `TrainerOrAdmin`), refuses a future or absurdly old date
  with `invalid_paid_at`, refuses a staff holder with `member_is_staff`, allows a blocked member, and
  overwrites `PaidRecordedBy` on every write. It never rotates a concurrency stamp.
- Editing a karnet (`PUT /api/admin/members/{id}/passes/{passId}`) **never** changes payment.
- The admin's karnet screen shows "Nieopłacony" (outlined badge) or "Opłacony · dd.MM.yyyy" on the
  active card and every history row, with row actions "Oznacz jako opłacony" (overlay with the date)
  and "Cofnij płatność" (toast).
- `/admin/members` shows a "Nieopłacony" badge on any member holding **any** unpaid karnet, and a
  "Tylko nieopłacone" checkbox filter (`unpaid=true`) combinable with the status/role filters and
  kept in the URL.
- A trainer sees the same badge on `/trainer/members` rows and opens a new child screen
  `/trainer/members/:id/passes` ("Karnety") listing the member's karnets read-only, with the same two
  payment actions.
- The member's "Twój karnet" card on Start says "Opłacony" or "Nieopłacony" for today's karnet.
- Existing passes are paid on their club-local issue day after the migration; `Down` drops both
  columns.

### Key Discoveries:

- `IssuePassRequest` is shared by POST and PUT — an optional `PaidAt` on it would let a four-field
  PUT (the e2e helper `src/app/e2e/support/club.ts:99-126`, `MembershipPassEndpointTests.Request`)
  wipe a payment. Hence: POST reads it, PUT ignores it, a separate route owns changes.
- `IssuedAt` is `datetimeoffset`, so `[IssuedAt] AT TIME ZONE 'Central European Standard Time'`
  converts it correctly in T-SQL; `CAST([IssuedAt] AS date)` would put passes issued 00:00–02:00
  local on the previous day.
- `TrainingPlanQuery.Assignable` excludes blocked members, so a trainer never *finds* a blocked member,
  but the trainer routes here scope by "not staff" only — the decision is "any non-staff member".
- `MembershipPassFailure` is a complete record in the SPA; a new reason fails
  `core/http/failure-contract.spec.ts` until it has a sentence — that is the guard, not an obstacle.
- `member-passes.ts:174-192` uses `form.setValue` / `form.reset` with every control named — a new
  control must be added to both, or `setValue` throws.

## What We're NOT Doing

- No price, amount, payment method, payment history, receipts, or a payments dashboard.
- No booking gate: an unpaid karnet never refuses a booking.
- No notification (email or push) about payment.
- `PaidRecordedBy` is stored, **not displayed** and not on the wire — "who took the money" on screen
  is a later feature.
- No member-side debt indicator for older karnets: the member's card speaks only about today's karnet
  (decided — an older unpaid karnet is visible to staff only).
- No change to the trainer's member list scope (still active, non-staff, unscoped by class), and no
  unpaid filter on the trainer's list.
- No `ControlValueAccessor` on `app-checkbox`; no new icon glyph; no `--danger` colour for the marker;
  no `ui-style-guide.md` amendment.
- Revoking (DELETE) an unpaid karnet keeps its current behaviour — the record and its debt go with it.

## Implementation Approach

Backend first, bottom-up: column + backfill + the issue path (phase 1), then the one write route and
the two read surfaces staff need (phase 2). The SPA follows persona by persona — admin and member
(phase 3), then the trainer's new child screen (phase 4) — so each phase can be verified in the
browser on its own. Documentation, the manual test plan and one E2E spec close it (phase 5).

The payment write takes an **explicit value**, never "toggle", so two staff acting at once converge.
It lives outside the admin pass group because its policy differs; the trainer's read lives in the
existing `/api/trainer/members` group, which already serves a minimized, contact-free projection.

## Critical Implementation Details

- **Ordering in the migration:** `AddColumn` both nullable columns, then the `UPDATE ... SET PaidAt =
  CAST([IssuedAt] AT TIME ZONE 'Central European Standard Time' AS date)` via `migrationBuilder.Sql`,
  following `20260908072222_AddMemberForeignKeys.cs:60-90`. `PaidRecordedBy` stays null for
  backfilled rows — nobody recorded those payments.
- **The stamp is deliberately untouched by the payment route.** `MembershipPass.ConcurrencyStamp`
  guards the entry pool; `PaidAt` is not part of it. Rotating it would make a payment write conflict
  with a concurrent booking for no reason. Say so in a comment where the route saves.

## Phase 1: Data model and the issue path

### Overview

The two columns exist, existing passes are backfilled as paid, issuing a karnet can record a payment,
and every `MembershipPassView` carries `PaidAt`.

### Changes Required:

#### 1. Entity and configuration

**File**: `src/Domain/Members/MembershipPass.cs`, `src/Infrastructure/Persistence/Configurations/MembershipPassConfiguration.cs`

**Intent**: Add the payment fact and its author, with a doc comment explaining that this is a human
assertion (the one stored fact on a pass that cannot be derived) and why it is outside the stamp.

**Contract**: `DateOnly? PaidAt` → `HasColumnType("date")`, not required. `string? PaidRecordedBy` →
`HasMaxLength(450)`, no FK, commented with the attendance precedent (`BookingConfiguration.cs:30-33`).

#### 2. Migration

**File**: `src/Infrastructure/Persistence/Migrations/<timestamp>_AddMembershipPassPayment.cs` (generated with `dotnet ef migrations add AddMembershipPassPayment --project src/Infrastructure/po-prostu-silka.Infrastructure.csproj --startup-project src/Api/po-prostu-silka.Api.csproj`)

**Intent**: Add the columns and backfill `PaidAt` from the club-local issue day so nobody becomes a
debtor on deploy; `Down` drops both columns.

**Contract**: `UPDATE [MembershipPasses] SET [PaidAt] = CAST([IssuedAt] AT TIME ZONE 'Central
European Standard Time' AS date) WHERE [PaidAt] IS NULL;` (verify the table name against the
snapshot). Comment why the Windows zone name and why not a plain `CAST`.

#### 3. Rules, request, validation, failure

**Files**: `src/Application/Members/MembershipPassRules.cs`, `IssuePassRequest.cs`, `MembershipPassProjection.cs`, `MembershipPassFailure.cs` (reason vocabulary comment if it lists them)

**Intent**: One rule for a valid payment date, read by the issue path now and the payment route in
phase 2.

**Contract**:
- `MembershipPassRules.MaxPaidAtAgeDays = 400` — sanity ceiling on how far back a payment may be
  dated, same reasoning as `MaxValidityDays`.
- `IssuePassRequest` gains `DateOnly? PaidAt = null` (optional, last parameter — existing four-field
  callers stay valid). Doc comment: **read by POST only; `UpdatePass` ignores it — payment changes go
  through `SetPassPaid`.**
- A shared check (e.g. `MembershipPassProjection.TryReadPaidAt(DateOnly? paidAt, DateOnly clubToday,
  out IResult failure)`) refusing `paidAt > clubToday` or `paidAt < clubToday − MaxPaidAtAgeDays`
  with 400 `invalid_paid_at`. Club today comes from the same `TimeProvider` + `ClubTime` path
  `CoversToday` already uses.

#### 4. Issue and update handlers

**Files**: `src/Application/Members/IssuePass.cs`, `UpdatePass.cs`

**Intent**: Issue stores `PaidAt` and, when non-null, `PaidRecordedBy` from the caller's
`ClaimTypes.NameIdentifier` (the handler gains a `ClaimsPrincipal` parameter, as `RecordAttendance`
has). Update leaves both fields alone, with a one-line comment pointing at `SetPassPaid`.

**Contract**: `IssuePass.HandleAsync(..., ClaimsPrincipal principal, ...)`. Validation order: after
the existing member checks, alongside `TryRead`.

#### 5. The view, in all three builders

**Files**: `src/Application/Members/MembershipPassView.cs`, `MembershipPassProjection.cs` (`ViewOfAsync`), `src/Infrastructure/Members/MembershipPassQuery.cs` (`GetForMemberAsync`, `FindCoveringAsync`)

**Intent**: Every reader of a karnet sees the same payment state.

**Contract**: `MembershipPassView` gains `DateOnly? PaidAt` as its last positional member. All three
constructions set it — missing one is the PASS-08 inconsistency.

#### 6. Test data and test fixture

**Files**: `src/Infrastructure/TestData/TestDataGenerator.cs`, `tests/po-prostu-silka.Tests/IntegrationTestFixture.cs`, `TestDataGeneratorTests.cs`

**Intent**: Staging shows the marker with hits for the filter; tests can arrange either state.

**Contract**: `AddPass` sets `PaidAt` deterministically from `_random` — most passes paid on their
`ValidFrom`, a few current ones and at least one expired one unpaid. `IssuePassAsync(..., DateOnly?
paidAt = null)` on the fixture. A generator test asserts the seed holds at least one unpaid current
and one unpaid expired pass, and no `PaidAt` in the future.

#### 7. Integration tests

**File**: `tests/po-prostu-silka.Tests/MembershipPassEndpointTests.cs`

**Intent**: Pin the issue path and the "edit never touches payment" rule.

**Contract**: new facts — issuing without `paidAt` returns `paidAt: null`; issuing with today's date
returns it and stores `PaidRecordedBy` = the admin's user id; a future date and a date older than 400
days are 400 `invalid_paid_at`; **editing a paid pass with a four-field body keeps `PaidAt`**;
`A_member_reads_their_own_karnet` asserts `paidAt` is present. `Issuing_a_pass_returns_every_field`
covers the new field.

### Success Criteria:

#### Automated Verification:

- Solution builds without warnings: `dotnet build po-prostu-silka.slnx`
- Migration is generated and its `Down` drops both columns (read the file)
- Backend tests pass: `dotnet test`
- The migration applies against the local DB: `docker compose up -d` then `dotnet run --project src/Api/po-prostu-silka.Api.csproj` and `GET /health` returns healthy

#### Manual Verification:

- In the local DB, a pass issued before the migration has `PaidAt` = its club-local issue day (check one issued just after local midnight if the seed has one)
- Reverting the migration (`dotnet ef database update <previous>` with both project flags) drops the columns cleanly

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Payment route and the staff read surfaces

### Overview

One `TrainerOrAdmin` write for the payment, a trainer read of a member's karnets, and an
"any unpaid karnet" fact on both member lists, with an orthogonal `unpaid` filter on the admin's.

### Changes Required:

#### 1. The payment write

**Files**: `src/Application/Members/SetPassPaid.cs` (new), `src/Application/Members/PassPaidRequest.cs` (new), `src/Api/Endpoints/Members/PassPaymentEndpoints.cs` (new), `Program.cs` (or wherever endpoint maps are registered)

**Intent**: The only way to change payment on an existing karnet, shared by admin and trainer screens.
Doc comment records the decisions: explicit value not toggle; no stamp rotation; blocked members are
allowed (payment is a fact about money, not access — the asymmetry with `IssuePass` is deliberate);
staff holders are refused; last writer is the recorder.

**Contract**: `PUT /api/passes/{passId:guid}/paid`, group policy `TrainerOrAdmin`, body
`PassPaidRequest(DateOnly? PaidAt)`. Unknown pass → 404. Holder is staff (`IMemberStore.IsStaffAsync`)
→ 409 `member_is_staff`. Bad date → 400 `invalid_paid_at` (phase 1 check). Otherwise set `PaidAt`,
set `PaidRecordedBy` to the caller's id (also when clearing, so the last change is attributable), save
— a `TrySaveChangesAsync` failure stays 409 `conflict` — and return 200 with the full
`MembershipPassView` (entries used computed as `UpdatePass` does). Setting the value it already has is
a 200, not an error. The route path does not collide with `/api/passes/mine` (different group,
`{passId:guid}` constraint).

#### 2. The trainer's read of a member's karnets

**Files**: `src/Application/Training/GetTrainerMemberPasses.cs` (new), `src/Application/Training/TrainerMemberPasses.cs` (new), `src/Api/Endpoints/Training/TrainerMemberEndpoints.cs`

**Intent**: The data behind `/trainer/members/:id/passes` — the member's name for the screen title and
the same karnet history the admin reads, nothing else (no contact data, so the group's minimized-
projection rule holds).

**Contract**: `GET /api/trainer/members/{memberId:guid}/passes` in the existing `TrainerOrAdmin`
group, returning `TrainerMemberPasses(Guid MemberId, string DisplayName, IReadOnlyList<MembershipPassView> Passes)`
(newest first, via `IMembershipPassQuery.GetForMemberAsync`). Unknown member **or a staff member** →
404 (staff "do not exist" from a trainer screen, as in `GetMemberPlan`). Blocked non-staff members are
returned.

#### 3. "Has any unpaid karnet" on both lists

**Files**: `src/Application/Members/MemberSummary.cs`, `src/Infrastructure/Members/MemberQuery.cs`, `src/Application/Training/TrainerMemberSummary.cs`, `src/Infrastructure/Training/TrainingPlanQuery.cs`

**Intent**: The marker means "owes for any karnet", not "today's karnet is unpaid" — debt outlives
validity (user decision).

**Contract**: `bool HasUnpaidPass` appended to both summaries, projected as
`db.MembershipPasses.Any(p => p.MemberId == x.Id && p.PaidAt == null)` after paging, like the existing
correlated subqueries.

#### 4. The admin list's unpaid filter

**Files**: `src/Application/Members/GetMembers.cs` (and `MemberListRequest.cs` if the query binds through it), `src/Infrastructure/Members/MemberQuery.cs`, `IMemberQuery.cs`

**Intent**: "Aktywni + nieopłaceni" must be expressible, so the filter is orthogonal to the pinned
`MemberListFilter` enum rather than value 4.

**Contract**: optional query parameter `unpaid` (bool, default false) on `GET /api/admin/members`;
applied as an `EXISTS` predicate before the count, so `total` and paging reflect it.

#### 5. Tests and route inventories

**Files**: `tests/po-prostu-silka.Tests/MembershipPassEndpointTests.cs` (or a new `PassPaymentEndpointTests.cs`), `TrainerMemberEndpointTests.cs`, `MemberEndpointTests.cs` / `MemberAdminEndpointTests.cs`, `PersonaAccessTests.cs`, `EndpointAuthorizationTests.cs`

**Intent**: Pin who may do what, and what the marker means.

**Contract**:
- Payment route: admin sets and clears; **trainer** sets and clears; **member is 403**; anonymous 401;
  `PaidRecordedBy` overwritten by the last caller (also on clear); staff holder → `member_is_staff`;
  blocked member → 200; future / too-old date → `invalid_paid_at`; unknown pass → 404; the pass's
  `ConcurrencyStamp` is unchanged after the write.
- Trainer read: trainer and admin get the history with `paidAt`; member 403; staff member id 404;
  unknown id 404.
- Admin list: `HasUnpaidPass` is true for a member whose only unpaid karnet **expired last month**
  while today's is paid (the counterexample); `unpaid=true` returns exactly those members and combines
  with `filter=Active`; total reflects the filter.
- Trainer list: `HasUnpaidPass` present and correct.
- The admin pass routes' `..._refuses_a_trainer` theory stays green unchanged.
- Both new routes are added to the `PersonaAccessTests` and `EndpointAuthorizationTests` inventories.

### Success Criteria:

#### Automated Verification:

- Solution builds without warnings: `dotnet build po-prostu-silka.slnx`
- Backend tests pass: `dotnet test`

#### Manual Verification:

- Via the running API (Scalar/OpenAPI or curl with a trainer cookie): a trainer marks a member's karnet paid and clears it; the admin's `GET .../passes` shows the change
- `GET /api/admin/members?unpaid=true&filter=Active` returns the expected seeded members

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Admin and member screens

### Overview

The admin issues with a payment, sees and changes payment on the karnet screen, finds debtors on the
member list; the member sees the status on Start.

### Changes Required:

#### 1. SPA models, service, failure words

**Files**: `src/app/src/app/core/admin/member-admin.models.ts`, `member-admin.service.ts`, `membership-pass-failure.ts`, `core/http/failure-messages.ts` (if the table is built there)

**Intent**: Mirror the wire contracts and give `invalid_paid_at` its sentence.

**Contract**: `MembershipPassView.paidAt: string | null`; `IssuePassRequest.paidAt?: string | null`;
`Member.hasUnpaidPass: boolean`; `MemberQuery.unpaid?: boolean` → `unpaid=true` param only when set;
a `setPassPaid(passId, paidAt: string | null)` method on a service both admin and trainer screens can
inject (a small `core/passes/pass-payment.service.ts` is cleaner than putting a `TrainerOrAdmin` call
on the admin service). `invalid_paid_at` added to `MEMBERSHIP_PASS_FAILURE_REASONS` with Polish copy,
e.g. "Data płatności nie może być w przyszłości ani starsza niż 400 dni."

#### 2. Shared payment overlay and status

**Files**: `src/app/src/app/shared/passes/pass-payment-overlay.{ts,html,scss}` (new), `shared/passes/pass-payment-status.{ts,html}` (new, optional if the markup is one line)

**Intent**: One "Oznacz jako opłacony" overlay and one status rendering for the admin karnet screen and
the trainer's — a third copy is the review finding S-19 warns about.

**Contract**: the overlay follows "every overlay has one shape" (`app-overlay-head` with `h2`,
`.overlay-body` with an `app-field` "Data płatności" `type="date"` defaulting to club-local today, `max`
= today, `.overlay-actions` with "Zapisz" first), wires `useOverlayFocus(() => this.close())`, and
emits the chosen date; its caller performs the request and maps a refusal through the failure table
into a form banner inside the overlay. The status renders an outlined `.badge` modifier
"Nieopłacony" or a quiet "Opłacony · dd.MM.yyyy" — word only, no `--danger`, no new glyph.

#### 3. Issue form

**Files**: `src/app/src/app/features/admin/members/member-passes.{ts,html}`

**Intent**: Record a payment at issue time without a second step.

**Contract**: an `app-checkbox` "Opłacony" (unchecked by default) bound by hand to a `paid` boolean
control (`[checked]` / `(checkedChange)`), and an `app-field` date control `paidAt` shown only while
ticked, defaulting to today. Both added to `form.setValue` and `form.reset` (`member-passes.ts:174-192`);
submit maps them to `paidAt` (date or null). **In edit mode both are hidden** and `paidAt` is not sent —
editing never changes payment.

#### 4. Karnet screen — status and actions

**Files**: `src/app/src/app/features/admin/members/member-passes.{ts,html,scss}`

**Intent**: Show and change payment on the active card and every history row.

**Contract**: status beside "Aktywny" on the active card and on each history row; row actions
"Oznacz jako opłacony" (when unpaid — opens the shared overlay) and "Cofnij płatność" (when paid —
immediate `paidAt: null`, success toast "Cofnięto płatność"); failures in row actions are toasts
(outlet 3), the row busy via `busy-set`. Replace the row in place from the returned view.

#### 5. Member list — marker and filter

**Files**: `src/app/src/app/features/admin/members/members.{ts,html,scss}`

**Intent**: Find who owes, alone or combined with the status/role filters.

**Contract**: "Nieopłacony" badge in both the phone cell and the wide cell when `hasUnpaidPass`; an
`app-checkbox` "Tylko nieopłacone" in the controls; `unpaid=1` in the URL state (`parse()` whitelist),
included in `narrowed()` and cleared by `clearFilters`; toggling resets to page 1 like the other
filters.

#### 6. Member's dashboard card

**Files**: `src/app/src/app/features/dashboard/dashboard.html` (and `.ts` only if a helper is needed)

**Intent**: The member sees whether today's karnet is paid.

**Contract**: the shared status inside the "Twój karnet" card, in the text part of the
"Figure · rule · text" pattern. Nothing for older karnets (decided).

#### 7. Specs

**Files**: `member-passes.spec.ts`, `members.spec.ts`, `member-admin.service.spec.ts`, `membership-pass-failure.spec.ts`, `dashboard.spec.ts`, `class-bookings-overlay.spec.ts` (fixture), new `pass-payment-overlay.spec.ts`, `pass-payment.service.spec.ts`

**Intent**: Fixtures gain the new fields; behaviour is pinned.

**Contract**: issuing with the box ticked posts `paidAt` = today, unticked posts `paidAt: null`; edit
posts no `paidAt`; "Cofnij płatność" sends `null` and shows a toast; the overlay defaults to today and
refuses nothing client-side beyond `max`; the list's `unpaid` survives a reload from the URL and
appears in the request; the dashboard renders both states. `npm run quality:check` passes — the kit
rule allows the new markup because only kit components are used in `features/`.

### Success Criteria:

#### Automated Verification:

- SPA unit tests pass: `npm test` (from `src/app/`)
- Lint and format pass: `npm run quality:check`
- Production build stays under the bundle warning: `npm run build` — record the initial-bundle figure (expected near-zero eager delta; the overlay is used only by lazy routes)
- Backend still green: `dotnet test`

#### Manual Verification:

- Issue a karnet ticked and unticked; edit a paid karnet and confirm it stays paid
- Mark an expired karnet paid with last week's date; try tomorrow's date and see the banner
- `/admin/members` with "Tylko nieopłacone" + "Aktywni" narrows as expected; reload keeps it
- As a member, Start shows "Nieopłacony" then "Opłacony" after staff marks it
- Phone (narrow), tablet and desktop widths all read well (lessons: mobile-first)

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 4: Trainer's karnet screen

### Overview

A trainer finds a member who owes and marks the karnet paid, from a child screen mounted beside the
plan builder.

### Changes Required:

#### 1. Model and service

**Files**: `src/app/src/app/core/training/training-plan.models.ts` (or a new `core/passes/` model), `core/training/*.service.ts`

**Intent**: Mirror `TrainerMember.hasUnpaidPass` and `TrainerMemberPasses`, and fetch the latter.

**Contract**: `getTrainerMemberPasses(memberId)` → `GET /api/trainer/members/{id}/passes`.

#### 2. Route

**File**: `src/app/src/app/app.routes.ts`

**Intent**: A trainer child screen, gated identically to the API (S-25: `trainerGuard` ↔
`TrainerOrAdmin`), no new menu entry.

**Contract**: `trainer/members/:id/passes`, `title: 'Karnety'`, `data: { level: 'child', parent:
'/trainer/members' }`, lazy, `canActivate: [authGuard, trainerGuard]`, placed beside
`trainer/members/:id/plan` after the literal route.

#### 3. Screen

**Files**: `src/app/src/app/features/trainer/passes/trainer-member-passes.{ts,html,scss}` (new)

**Intent**: Read-only karnet history with the two payment actions — no issue, edit or revoke.

**Contract**: `h1.screen-title` via `useScreenTitle(() => 'Karnety — ' + name)`, an `[appUp]` link to
`/trainer/members`; `form-state` loading / `loadFailed` / `notFound` (404 → screen state, never a
toast); `app-list` / `li[appRow]` rows (type name, validity, entries left, shared status, actions);
`app-empty icon="ticket"` when the member has no karnet; the shared overlay and toasts exactly as on the
admin screen.

#### 4. Trainer member list row

**Files**: `src/app/src/app/features/trainer/members/trainer-members.{ts,html}`

**Intent**: Surface the debt where the trainer already looks and give a way in.

**Contract**: "Nieopłacony" badge in `.row-meta` (or `.row-identity`) when `hasUnpaidPass`; a second
link "Karnety" to `/trainer/members/:id/passes` in `.row-actions`; the name keeps linking to the plan.
Update the hint sentence to mention karnety.

#### 5. Specs

**Files**: new `trainer-member-passes.spec.ts`, `trainer-members.spec.ts`, `app.routes.spec.ts` (passes automatically if the identity is declared)

**Intent**: Pin the screen's states and actions.

**Contract**: renders history and statuses; marks paid via the overlay and replaces the row; clears
with a toast; a 404 renders the not-found state; the list row shows the badge and the link.

### Success Criteria:

#### Automated Verification:

- SPA unit tests pass: `npm test`
- Lint and format pass: `npm run quality:check`
- Production build passes and bundle figure recorded: `npm run build`

#### Manual Verification:

- As a trainer: badge on `/trainer/members`, open "Karnety", mark an old karnet paid, undo it
- Phone: the app bar shows "Karnety" with the back arrow instead of the bottom bar; back returns to the list
- As a member, `/trainer/members/<id>/passes` is refused by the guard (and the API 403s)

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 5: Documentation, manual test plan and E2E

### Overview

Record the narrowed Non-Goal, give the change a roadmap row, extend the manual plan, and add one
browser-level spec for the risk unit tests cannot see end to end.

### Changes Required:

#### 1. PRD and roadmap

**Files**: `context/foundation/prd.md`, `context/foundation/roadmap.md`

**Intent**: The "no payments" Non-Goal is narrowed, not removed.

**Contract**: under `prd.md:202` add `> **Narrowed by pass-paid-flag (roadmap S-34), 2026-10-xx.** The
app records that a karnet was paid and when; it never takes a payment or stores an amount.`; append a
matching note to the parked item at `roadmap.md:1087`; add an S-34 row (`pass-paid-flag`) to "At a
glance", a `### S-34` body and the change-id index, following the S-33 entries' shape.

#### 2. Manual test plan

**Files**: `context/testing/03-members-and-passes.md`, `02-member-screens.md`, `01-account-and-access.md`

**Intent**: Cover the new behaviours by hand.

**Contract**: PASS-09+ (issue paid/unpaid; edit keeps payment; mark/undo with a past date; future date
refused; list marker for an expired unpaid karnet; filter combined with status; blocked member's
karnet can be marked); a DASH case for the status on Start; NAV-04 re-worded so the trainer's new
`/trainer/members/:id/passes` is allowed while admin karnet screens stay barred.

#### 3. E2E

**File**: `src/app/e2e/<new>.spec.ts` (generated through `/10x-e2e`)

**Intent**: The one cross-persona risk: a trainer marks a karnet paid and the admin's list marker and
the member's card agree (the three view builders and the list subquery in one journey).

**Contract**: seed via `support/club.ts` (issue an unpaid karnet — the existing four-field helper
already produces one), trainer marks it paid on the new screen, admin's "Tylko nieopłacone" no longer
lists the member, member's Start reads "Opłacony". Unique `E2E …` names, role/label locators, no
`waitForTimeout`.

### Success Criteria:

#### Automated Verification:

- E2E suite passes locally: `npx playwright test` (from `src/app/`, with `docker compose up -d`)
- SPA and backend still green: `npm test`, `dotnet test`

#### Manual Verification:

- PRD and roadmap read correctly; PASS-09+ executed once against local data

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful.

---

## Testing Strategy

### Unit Tests:

- SPA: issue-form mapping (ticked/unticked/edit), overlay default date, row actions, list URL state,
  dashboard both states, trainer screen states, failure table completeness for `invalid_paid_at`.

### Integration Tests:

- Payment route authorization matrix (admin, trainer, member, anonymous), date bounds, staff refusal,
  blocked allowed, recorder overwrite, stamp untouched.
- Edit-never-touches-payment with a four-field body.
- `HasUnpaidPass` with an expired unpaid karnet beside a paid current one; `unpaid` filter combined
  with `filter=Active`.
- Trainer read: staff member 404, blocked member returned.

### Manual Testing Steps:

1. Issue a karnet unpaid, check list marker and member's card; mark it paid with yesterday's date.
2. As a trainer, clear and re-mark an expired karnet from `/trainer/members/:id/passes`.
3. Edit a paid karnet's dates and confirm payment is kept.
4. Try a future payment date — banner in the overlay.
5. Check all three widths.

## Performance Considerations

`HasUnpaidPass` is one more correlated `EXISTS` per page row; the filter is one `EXISTS` before the
count. Passes per member are a handful a year; the existing `MemberId` index on `MembershipPasses`
serves both. No new index.

## Migration Notes

Additive, nullable columns with a backfill; reversible via `Down`. Deploying the previous artifact
after rollback leaves the columns in place unused — harmless, since nothing in the old code reads
them.

## References

- Research: `context/changes/pass-paid-flag/research.md`
- Decisions: `context/changes/pass-paid-flag/change.md`
- Audit-field precedent: `src/Application/Scheduling/RecordAttendance.cs:138`
- Backfill precedent: `src/Infrastructure/Persistence/Migrations/20260908072222_AddMemberForeignKeys.cs:60-90`
- Trainer child-screen precedent: `src/app/src/app/app.routes.ts` (`trainer/members/:id/plan`)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Data model and the issue path

#### Automated

- [x] 1.1 Solution builds without warnings — 353681b
- [x] 1.2 Migration is generated and its Down drops both columns — 353681b
- [x] 1.3 Backend tests pass — 353681b
- [x] 1.4 The migration applies against the local DB and /health is healthy — 353681b

#### Manual

- [ ] 1.5 Pre-migration pass has PaidAt = its club-local issue day
- [ ] 1.6 Reverting the migration drops the columns cleanly

### Phase 2: Payment route and the staff read surfaces

#### Automated

- [x] 2.1 Solution builds without warnings — a96bb13
- [x] 2.2 Backend tests pass — a96bb13

#### Manual

- [ ] 2.3 Trainer marks and clears a karnet via the API; admin sees the change
- [ ] 2.4 unpaid=true&filter=Active returns the expected seeded members

### Phase 3: Admin and member screens

#### Automated

- [x] 3.1 SPA unit tests pass — face33b
- [x] 3.2 Lint and format pass — face33b
- [x] 3.3 Production build under the bundle warning, figure recorded — face33b
- [x] 3.4 Backend still green — face33b

#### Manual

- [ ] 3.5 Issue ticked/unticked; edit keeps payment
- [ ] 3.6 Mark an expired karnet paid with a past date; future date shows the banner
- [ ] 3.7 Unpaid filter combined with Aktywni; survives reload
- [ ] 3.8 Member's Start shows both states
- [ ] 3.9 Phone, tablet and desktop widths read well

### Phase 4: Trainer's karnet screen

#### Automated

- [x] 4.1 SPA unit tests pass — 5c6e99c
- [x] 4.2 Lint and format pass — 5c6e99c
- [x] 4.3 Production build passes, bundle figure recorded — 5c6e99c

#### Manual

- [ ] 4.4 Trainer marks and undoes an old karnet from the new screen
- [ ] 4.5 Phone app bar and back behaviour on the child screen
- [ ] 4.6 Member is refused the trainer screen and route

### Phase 5: Documentation, manual test plan and E2E

#### Automated

- [x] 5.1 E2E suite passes locally
- [x] 5.2 SPA and backend still green

#### Manual

- [ ] 5.3 PRD and roadmap read correctly; PASS-09+ executed once
