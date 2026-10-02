---
date: 2026-10-02T00:00:00+02:00
researcher: Claude (Opus 5.5) with Karol Rumianowski
git_commit: 19637084cef2cd1054070555c3f7b6c25ce28e84
branch: main
repository: rumek/po-prostu-silka
topic: "What a paid/unpaid flag on the karnet touches, and how a trainer can be allowed to set it"
tags: [research, codebase, membership-pass, karnet, personas, member-list, dashboard, migrations]
status: complete
last_updated: 2026-10-02
last_updated_by: Claude (Opus 5.5)
last_updated_note: "Recorded the user's answers to open questions 1, 2, 4, 5"
---

# Research: What a paid/unpaid flag on the karnet touches

**Date**: 2026-10-02
**Researcher**: Claude (Opus 5.5) with Karol Rumianowski
**Git Commit**: 19637084cef2cd1054070555c3f7b6c25ce28e84 (pushed; permalink base
`https://github.com/rumek/po-prostu-silka/blob/19637084cef2cd1054070555c3f7b6c25ce28e84/`)
**Branch**: main
**Repository**: rumek/po-prostu-silka

## Research Question

For the change `pass-paid-flag` (see `change.md`): map everything a nullable `PaidAt` on
`MembershipPass` touches — entity, migration with backfill, issue/edit request, the views the admin,
the member and the list read, the member-list filter, the seeder, the tests — and establish how a
**trainer** can be allowed to mark a karnet paid/unpaid when today every pass route is admin-only.

Decisions already made with the user (recorded in `change.md`): checkbox unchecked by default; admin
**and trainer** may mark paid/unpaid; the member sees the status of their own karnet; unpaid does not
block booking; existing passes are backfilled as paid.

## Summary

- **The admin half is small and well-trodden.** One nullable `date` column, one optional field on
  `IssuePassRequest`, one field on `MembershipPassView` (built in three places), one more correlated
  subquery on the member list, one new filter. The member's dashboard gets the status for free
  because `/api/passes/mine` returns the same `MembershipPassView`.
- **The trainer half is the real design work.** A trainer has **no pass access today**: every pass
  route is under the `Admin` policy, the `admin/members/:id/passes` screen is `adminGuard`, and the
  trainer's only member surface (`/api/trainer/members`, `/trainer/members`) carries no pass data.
  Letting a trainer mark "paid" means a new `TrainerOrAdmin` route **and** a trainer-side surface
  that shows the karnet — with menu, guard and policy identical (S-25).
- **There is no trainer-to-member relation to scope by.** `BookingAuthorization.MayActOn` is
  per-class; `/api/trainer/members` already lists **every** non-staff member, unscoped. So "a trainer
  may mark any non-staff member's karnet" matches what a trainer can already see and book; "only
  members of my classes" would be a new relation derived from bookings.
- **Three things the decisions so far do not settle** (see Open Questions): whether the
  "unpaid" marker/filter means *today's* karnet or *any* unpaid karnet (debt outlives validity);
  whether to record *who* marked it paid (an audit precedent exists); and the visual token, since
  the style guide reserves `--danger` for errors and there is no money glyph.

## Detailed Findings

### Entity and persistence

- `MembershipPass` (`src/Domain/Members/MembershipPass.cs:47-102`): `ValidFrom`/`ValidTo` are
  `DateOnly` (inclusive), `EntryCount` is the number issued (entries left are derived, never stored),
  `IssuedAt` is `DateTimeOffset` (UTC), `ConcurrencyStamp` guards the **entry pool**.
- EF config `src/Infrastructure/Persistence/Configurations/MembershipPassConfiguration.cs`: DateOnly
  is mapped explicitly — `builder.Property(x => x.ValidFrom).IsRequired().HasColumnType("date");`
  (:31-32), commented as a deliberate convention. `PaidAt` should follow: `HasColumnType("date")`,
  not required.
- Latest migration: `20261001070637_RenameClassTypesToClassGroups`. Plain nullable column precedent:
  `20260923133910_AddBookingAttendance.cs:14-47` (`AddColumn` / `DropColumn`). Backfill precedent:
  `20260908072222_AddMemberForeignKeys.cs:60-90` (`migrationBuilder.Sql("UPDATE ... WHERE ... IS NULL")`
  after the nullable `AddColumn`).
- **Backfill date:** `IssuedAt` is UTC; the club date is `Europe/Warsaw` (`ClubTime.TimeZoneId`,
  `src/Domain/Scheduling/ClubTime.cs:49`). A club-local backfill in T-SQL is
  `CAST([IssuedAt] AT TIME ZONE 'Central European Standard Time' AS date)` (SQL Server takes Windows
  zone names; works on Azure SQL). `CAST([IssuedAt] AS date)` would be off by one day for passes
  issued between 00:00 and 01:00/02:00 local. Down = `DropColumn` — reversible as required.

### Application — issue, edit, view

- `IssuePassRequest` (`src/Application/Members/IssuePassRequest.cs:17-21`) —
  `(string TypeName, DateOnly ValidFrom, DateOnly ValidTo, int EntryCount)`, shared by POST and PUT.
  Adding the paid field as **optional** (`DateOnly? PaidAt = null`) keeps every existing caller
  valid — the e2e helper `src/app/e2e/support/club.ts:99-126` and `MembershipPassEndpointTests.Request`
  (:28-29) post the four fields only — and "absent = unpaid" matches the unchecked default.
- Validation lives in `MembershipPassProjection.TryRead` (`MembershipPassProjection.cs:29-66`),
  limits in `MembershipPassRules.cs`. A `PaidAt` in the future (club-local) or absurdly old would need
  a new reason, e.g. `invalid_paid_at`; `MembershipPassFailure` is a complete Record in the SPA
  (`core/admin/membership-pass-failure.ts:16-34`, `MEMBERSHIP_PASS_FAILURE_REASONS` in
  `core/admin/member-admin.models.ts:315-328`), so a new reason fails the SPA build until it has a
  sentence — by design (`core/http/failure-contract.spec.ts`).
- `IssuePass.cs` builds the entity at :70-79 (`IssuedAt = timeProvider.GetUtcNow()`); `UpdatePass.cs`
  assigns fields at :74-77 and rotates both stamps (:82-83). Concurrency is **not** client-supplied
  (no If-Match); a lost race is a 409 `conflict` from `TrySaveChangesAsync`.
- `MembershipPassView` (`MembershipPassView.cs:27-36`) is the wire contract; it is constructed in
  **three places** that must all gain `PaidAt`: `MembershipPassProjection.ViewOfAsync` (:87-107),
  `Infrastructure/Members/MembershipPassQuery.cs` `GetForMemberAsync` (:26-50) and
  `FindCoveringAsync` (:70-98).
- `GetMyPass.cs:46-48` returns `FindCoveringAsync`'s view — **the member sees `PaidAt` with no
  further backend work.**

### Endpoints and policies

- `src/Api/Endpoints/Members/MembershipPassEndpoints.cs:43-45` — group
  `/api/admin/members/{memberId:guid}/passes`, `.RequireAuthorization(AuthorizationPolicyNames.Admin)`;
  GET, POST, PUT `/{passId}`, DELETE `/{passId}`.
- `MyPassEndpoints.cs:34-36` — `/api/passes/mine` under `MemberOnly`.
- Policies in `src/Infrastructure/Authorization/AuthorizationPolicies.cs`: `MemberOnly` (:69-74),
  `Admin` (:75-79), `TrainerOrAdmin` (:88-92, `RequireRole(Trainer, Admin)` = OR).
- **A trainer cannot reach the admin pass routes, and must not get them wholesale**: the PUT edits
  dates and entry count, which S-16 (MP-04..06) keeps admin-only. A trainer needs a **narrow** write
  — "set paid / unpaid" — not the full edit.

### The trainer's surface today

- API: `/api/trainer/members` (`src/Api/Endpoints/.../TrainerMemberEndpoints.cs:35-40`,
  `TrainerOrAdmin`) lists every non-staff member, name-only search (no e-mail oracle, :79-83),
  returning `TrainerMemberSummary(Id, DisplayName, HasAccount, PlanName)`
  (`src/Application/Training/TrainerMemberSummary.cs:18`; query
  `src/Infrastructure/Training/TrainingPlanQuery.cs:60-93`). **Not narrowed** to the trainer's classes.
- SPA: `/trainer/members` (`app.routes.ts:236-241`, `trainerGuard`;
  `features/trainer/members/trainer-members.html:42-60`); `TrainerMember` type
  `core/training/training-plan.models.ts:87-94`. The plan builder is mounted per member at
  `/trainer/members/:id/plan` (`app.routes.ts:247`) — a precedent for a per-member trainer sub-screen.
- Navigation: `core/layout/navigation.ts:92-95` gives a trainer `[START, SCHEDULE, TRAINER_MEMBERS(, MORE)]`.
- Booking roster (`ClassBooking`, `src/Application/Scheduling/ClassBooking.cs:40-47`) carries no
  pass data; the trainer's booking candidate search
  (`core/scheduling/booking-candidates.ts:61-77`) reuses `/api/trainer/members`.
- `BookingAuthorization.MayActOn` (`src/Application/Scheduling/BookingAuthorization.cs:49-51`):
  `IsInRole(Admin) || principal.GetMemberId() == entity.InstructorMemberId` — per **class**. Passes
  have no class link, so it cannot be reused as-is to scope "which karnets".

**Two viable shapes for the trainer write** (for `/10x-plan` to choose):

1. **Trainer member list carries today's karnet + a toggle.** Extend `TrainerMemberSummary` with the
   covering pass's id, validity and `PaidAt` (same correlated-subquery pattern as `MemberQuery`), show
   a fact/badge on the `/trainer/members` row, and a row action "Oznacz jako opłacony" hitting a
   `TrainerOrAdmin` route such as `PUT /api/passes/{passId}/paid` with `{ paidAt: date | null }`. No
   new screen, no new nav entry.
2. **A trainer karnet sub-screen** `/trainer/members/:id/passes` (read-only list + paid toggle), mirroring
   the plan-builder mount. More surface, but it shows unpaid *past* karnets too (see Open Question 1).

Either way the write should take an **explicit value** (`paidAt` or `null`), never "toggle", so two
staff acting at once converge instead of flipping each other; and it need not rotate the pass stamp,
because `PaidAt` does not touch the entry pool the stamp guards. It must still refuse a pass whose
member is staff/blocked consistently with `IssuePass` (`member_is_staff`, `member_blocked`) — or
deliberately not; payment of a blocked member's karnet is plausible. Plan decides.

### Member list (admin)

- `MemberSummary` (`src/Application/Members/MemberSummary.cs:39-50`) already carries
  `PassValidTo` / `PassEntriesLeft` — **today's** covering karnet, not the latest (doc :32-37).
- `MemberQuery.GetMembersAsync` (`src/Infrastructure/Members/MemberQuery.cs:55-144`):
  `Searched(WithRole(Filtered(...)))` → count → order → Skip/Take; pass fields are correlated scalar
  subqueries (:98-109). A paid field is one more.
- Filter: `MemberListFilter` enum is single-select and pinned (`MemberListFilter.cs:23-42`: Pending=0
  retired, Active=1, Blocked=2, WithoutAccount=3), applied in `MemberQuery.Filtered` (:227-253). An
  "unpaid" filter is better as a **separate, orthogonal** `unpaid=true` query param on
  `GetMembers.HandleAsync` (`GetMembers.cs:42-59`) than as enum value 4 — "Aktywni + nieopłaceni" is
  the useful combination. S-21 noted "four chips fit; changing them is a redesign (UX-09)"; the SPA
  controls today are two `app-select`s (Status, Rola), so the unpaid filter fits as an
  `app-checkbox` toggle — its existing use (`class-groups.html:12`, `exercises.html:28`).
- SPA: `features/admin/members/members.ts` — URL state `parse()` (:64-84) with whitelists (:50-52),
  `setFilter`/`refilter` (:317-398), `narrowed()` (:343), `clearFilters` (:373); service params in
  `core/admin/member-admin.service.ts:48-73`; `MemberQuery` model `member-admin.models.ts:105-111`.
  Rows: `passKind()` (`members.ts:545-555`); phone cell `members.html:144-171` (`fact-line` with
  `ticket`), wide cell `:183-198` (`badge badge-active` "Ważny" etc.).

### Admin karnet screen

- `features/admin/members/member-passes.{ts,html,scss}`, route `admin/members/:id/passes`
  (`app.routes.ts:101-106`, `[authGuard, adminGuard]`). There is **no pass info on `member-form`**
  (`/admin/members/:id`); "member card" in `change.md` means this screen.
- Reactive non-nullable form (`member-passes.ts:104-112`): `typeName`, `validFrom`, `validTo`,
  `entryCount`. **`form.setValue({...})` on edit (:174-179) and `form.reset({...})` on cancel (:192)
  must both include the new control**, or `setValue` throws. Submit sends `getRawValue()` as
  `IssuePassRequest` (:224) — a boolean `paid` control needs explicit mapping to `paidAt`.
- Active-pass card (`member-passes.html:291-328`) and history rows (`:430-479`, `badge passes-badge`
  "Aktywny" at :439) are where "Nieopłacony" shows.
- `app-checkbox` (`shared/forms/checkbox/checkbox.ts:28-52`): `checked = input.required<boolean>()`,
  `checkedChange = output<void>()`, **no ControlValueAccessor** (doc :7-12 calls a CVA "an additive
  change" when a form needs one). Options: add the CVA in `shared/` (with its own spec), or bind
  `[checked]`/`(checkedChange)` to the form control by hand. A raw `<input type="checkbox">` in
  `features/**` fails lint (`no-hand-rolled-presentational`).
- Form errors → banner `<p class="alert" role="alert">` (`member-passes.html:336-338`); wording via
  `messageFor()` (`member-passes.ts:276-280`).

### Member's own view

- Only consumer of `getMyPass()` is the dashboard "Twój karnet" card
  (`features/dashboard/dashboard.html:70-128`, `dashboard.ts:110-206`). Moje konto shows no karnet.
  The status goes on this card; the style guide's karnet card pattern is "Figure · rule · text"
  (`context/foundation/ui-style-guide.md:83`).

### Visual language

- Colour is never the only channel — a state is always also a word (`ui-style-guide.md:14-16`).
- `--danger`/`--danger-bg` are "Errors and 'Brak miejsc' only"; `--success`/`--success-bg`
  "Confirmation toasts only" (`ui-style-guide.md:42-43`). **Neither is licensed for "Nieopłacony"**
  as written — using one is a style-guide amendment; a quiet outlined `.badge` with the word, in a
  component-local modifier (precedent: `members.scss:262-285`), needs none.
- `.badge` = a state (outlined, word); `.chip` = a quiet fact (`ui-style-guide.md:87`).
- Icons: no money/payment glyph in `shared/icons/icon.ts:4-43`. "One meaning, one glyph". A new glyph
  goes into the eager `@switch` (~0.85 kB per S-33's measurement); the eager bundle is at 576.29 kB,
  24 kB under the warning (AGENTS.md). A word-only badge avoids both.

### Test data seeder

- Passes are built in `src/Infrastructure/TestData/TestDataGenerator.cs:330-345` (`AddPass`), called at
  :291/307/317/320/325; `TestDataSeeder.cs:173` adds them. Set `PaidAt` deterministically from the
  generator's `_random` (mostly paid, a few current passes unpaid) so staging shows the marker and the
  filter has hits. `TestDataGeneratorTests.cs:29` is where a PaidAt invariant would sit.

### Tests

- `tests/po-prostu-silka.Tests/MembershipPassEndpointTests.cs`: `Request(...)` helper (:28-29);
  `Issuing_a_pass_returns_every_field` (:46), `Editing_a_pass_changes_its_fields` (:505); auth theories
  over `EveryRoute` (:581-587) — `..._refuses_a_trainer` (:612) **stays true for the admin routes**,
  and the new trainer route needs its own allow/deny cases; own karnet
  `A_member_reads_their_own_karnet` (:626).
- `IntegrationTestFixture.IssuePassAsync(memberId, entryCount, validFrom?, validTo?)` (:275-299)
  inserts via `AppDbContext` — add an optional `paidAt`.
- List: `MemberEndpointTests.cs` local `MemberSummaryBody` (:25-36),
  `The_list_carries_the_pass_covering_today` (:579-601); filter style
  `MemberAdminEndpointTests.cs:429, :549` (`PageAsync(admin, "filter=Blocked&search=...")`).
- Route inventories to extend for a new route: `PersonaAccessTests.cs:22,65`,
  `EndpointAuthorizationTests.cs:91`.
- SPA specs with `Member` / `MembershipPassView` fixtures that need the field:
  `member-passes.spec.ts`, `members.spec.ts`, `member-admin.service.spec.ts`,
  `membership-pass-failure.spec.ts`, `dashboard.spec.ts` (:37), `class-bookings-overlay.spec.ts` (:54),
  `trainer-members.spec.ts`, `checkbox.spec.ts` (if a CVA is added).
- E2E (`src/app/e2e/`): no spec drives the karnet form; `support/club.ts` `issuePass()` keeps working
  if the field is optional.
- Manual plan: `context/testing/03-members-and-passes.md` PASS-01..08 (:93-147) — new cases PASS-09+;
  DASH-01/02 (`02-member-screens.md`) for the member's card; **NAV-04** ("trainer is barred from member
  and admin screens", `01-account-and-access.md:232`) needs re-reading against whatever trainer
  surface is added.

### Audit trail precedent

- `Booking.AttendanceRecordedAt` / `AttendanceRecordedBy` (`src/Domain/Scheduling/Booking.cs:113-119`),
  set in `RecordAttendance.cs:137-138` from `ClaimTypes.NameIdentifier`, `nvarchar(450)` with **no FK**
  ("staff may have no Member row, and an audit field must survive the account it names";
  `BookingConfiguration.cs:30-33`). A `PaidRecordedBy` would copy it. Pass handlers take no
  `ClaimsPrincipal` today, so issue/update/the new route would gain one.

### Notifications

- Nothing notifies on pass issue/change; S-16 excluded "any notification about passes". Out of scope
  here too.

## Code References

- `src/Domain/Members/MembershipPass.cs:47-102` — entity; add `DateOnly? PaidAt`.
- `src/Infrastructure/Persistence/Configurations/MembershipPassConfiguration.cs:31-43` — explicit `date` mapping, stamp.
- `src/Infrastructure/Persistence/Migrations/20260908072222_AddMemberForeignKeys.cs:60-90` — backfill pattern.
- `src/Application/Members/IssuePassRequest.cs:17-21` — request (POST + PUT).
- `src/Application/Members/MembershipPassProjection.cs:29-66, 87-107` — validation; view builder #1.
- `src/Infrastructure/Members/MembershipPassQuery.cs:26-50, 70-98` — view builders #2, #3.
- `src/Application/Members/IssuePass.cs:70-79`, `UpdatePass.cs:74-83` — where the field is assigned.
- `src/Api/Endpoints/Members/MembershipPassEndpoints.cs:43-45` — admin-only pass group.
- `src/Infrastructure/Authorization/AuthorizationPolicies.cs:88-92` — `TrainerOrAdmin`.
- `src/Application/Scheduling/BookingAuthorization.cs:49-51` — per-class trainer scope.
- `src/Application/Training/TrainerMemberSummary.cs:18`, `src/Infrastructure/Training/TrainingPlanQuery.cs:60-93` — trainer member list.
- `src/Infrastructure/Members/MemberQuery.cs:55-144, 227-253` — admin list, pass subqueries, filters.
- `src/Application/Members/MemberListFilter.cs:23-42`, `GetMembers.cs:42-59` — filter binding.
- `src/Infrastructure/TestData/TestDataGenerator.cs:330-345` — seeded passes.
- `src/app/src/app/features/admin/members/member-passes.ts:104-112, 174-192, 224` — the form.
- `src/app/src/app/features/admin/members/members.ts:50-84, 317-398, 545-555` — list filters, pass cell.
- `src/app/src/app/features/dashboard/dashboard.html:70-128` — member's karnet card.
- `src/app/src/app/features/trainer/members/trainer-members.html:42-60` — trainer member list.
- `src/app/src/app/shared/forms/checkbox/checkbox.ts:28-52` — checkbox API (no CVA).
- `src/app/src/app/core/admin/member-admin.models.ts:256-328` — `MembershipPassView`, request, failure union.

## Architecture Insights

- **Derived over stored, except where a fact is a human assertion.** Entries left are derived;
  "paid" cannot be — it is a statement staff make about money handed over outside the app. That is
  why it is a stored `PaidAt`, and why it must not join the pass's concurrency stamp: it guards
  nothing the stamp guards.
- **Persona symmetry (S-25)** is the constraint that shapes the trainer half: whatever a trainer can
  write, the menu, the SPA guard and the API policy must agree on, and the trainer must not inherit
  the admin's full pass edit.
- **One view, three builders.** `MembershipPassView` is assembled in three places; missing one would
  ship a karnet that reads "unpaid" on one screen and "paid" on another — PASS-08's consistency case
  is the manual guard for exactly this.
- **Optional on the wire keeps the e2e and test helpers stable** and matches the product default
  (absent = unpaid).

## Historical Context (from prior changes)

- `context/archive/2026-09-09-membership-pass-and-staff-booking/plan.md:99-100` — "**Money.** A pass is
  issued, never sold. No price, no payment, no invoice — the M-4 carve-out stands." Out of scope there:
  "money in any form; … a trainer-to-member relationship; … any notification about passes"
  (plan-brief.md:225-227). Issue/edit/revoke admin-only (MP-04..06); trainer scoping "class-scoped only"
  (plan.md:106-107). **This change narrows the first decision and widens the trainer's reach — both
  need recording.**
- `context/archive/2026-09-22-role-based-visibility/` — staff never hold member data; the trainer list
  is non-staff members only and is reused by the booking picker; name-only search is an accepted risk.
- `context/archive/2026-09-23-class-attendance/plan.md:158-159` — the audit-field-without-FK decision.
- `context/archive/2026-09-21-member-list-at-scale/plan-brief.md:39` — filter order and the
  "four chips" constraint; `MemberListFilter` values are pinned.
- PRD: `context/foundation/prd.md:202` — "No pass/membership sales, payments, subscriptions, or
  invoices — the app manages participation, not money." Roadmap parked item
  `context/foundation/roadmap.md:1087` — "… the money half stands: a karnet is issued by an admin, never
  bought, and carries no price." Amendment precedents: inline `> **Superseded by M-4 (roadmap S-16),
  2026-09-09.**` under the item (`prd.md:63`); "**Unparked 2026-09-23 → S-27**" appended to a parked
  bullet (`roadmap.md:1080`). This change likely needs `> **Narrowed by pass-paid-flag (roadmap S-NN),
  2026-10-xx.** The app records that a karnet was paid; it never takes a payment or stores an amount.`
  plus a matching note on `roadmap.md:1087`, and a roadmap slice row.

## Related Research

- `context/archive/2026-09-09-membership-pass-and-staff-booking/research.md`
- `context/archive/2026-09-22-role-based-visibility/research.md`
- `context/archive/2026-09-23-class-attendance/research.md`

## Open Questions

> **Resolved by the user on 2026-10-02:** Q1 → any unpaid karnet; Q2 → any non-staff member;
> Q4 → yes, record `PaidRecordedBy`; Q5 → editable, defaults to today, never in the future. Since
> Q1 is "any karnet", Q3 leans to shape 2: an old unpaid karnet is not today's, so a row action on
> today's karnet alone cannot clear it. Q6–Q8 remain for `/10x-plan`.

1. **What does "Nieopłacony" mean on the list — today's karnet or any karnet?** The list's pass
   columns describe the karnet covering *today*. A member whose last month expired unpaid, or who was
   issued next month's karnet in advance, would show nothing. The spreadsheet's "BRAK PŁATNOŚCI" is
   per row regardless of dates — debt outlives validity. Recommendation: the marker and the filter
   mean "has any unpaid karnet" (an `EXISTS` subquery), while the pass cell keeps showing today's.
   *Owner: user.*
2. **Which karnets may a trainer mark?** Any non-staff member's (matches `/api/trainer/members`,
   which is already unscoped) vs. only members booked into the trainer's classes (a new relation).
   Recommendation: any non-staff member. *Owner: user.*
3. **Where does the trainer do it?** Row action on `/trainer/members` (shape 1) vs. a
   `/trainer/members/:id/passes` sub-screen (shape 2). Shape 2 is needed if Q1 is "any karnet",
   since an old unpaid karnet is not today's. *Owner: `/10x-plan`.*
4. **Record who marked it paid?** `PaidRecordedBy` per the attendance precedent — cheap, and useful
   once a trainer takes cash. *Owner: user / `/10x-plan`.*
5. **Payment date: always "today", or editable?** The checkbox stamps today; editing lets the admin
   record a transfer that arrived last week. Validation then needs a rule (not in the future).
   *Owner: `/10x-plan`.*
6. **May a blocked member's karnet be marked paid?** Issue refuses blocked members; paying off a
   debt of a blocked member is plausible. *Owner: `/10x-plan`.*
7. **Visual token.** Word-only outlined badge (no style-guide change, no bundle cost) vs. a coloured
   or iconned one (style-guide amendment; ~0.85 kB eager glyph). Recommendation: word-only.
   *Owner: `/10x-plan`.*
8. **`app-checkbox` in a reactive form** — add a CVA in `shared/` or bind by hand. *Owner: `/10x-plan`.*
