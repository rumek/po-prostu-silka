<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Mark a Karnet as Paid or Unpaid

- **Plan**: context/changes/pass-paid-flag/plan.md
- **Scope**: Phases 1–5 of 5 (full plan; automated items done, manual items pending)
- **Date**: 2026-10-02
- **Verdict**: NEEDS ATTENTION → triaged 2026-10-02: F1–F8 fixed, F9 partly (E2E + manual pending)
- **Findings**: 0 critical, 3 warnings, 6 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | WARNING |

Automated checks run in this review: `dotnet build` (0 warnings), `dotnet test` (793/793),
`npm run quality:check` (clean), `npm test` (952/952), `npm run build` (initial 580.70 kB, matches
the AGENTS.md record). The E2E suite was not re-run in this review; it needs the local SQL Server
from `docker compose`.

## Findings

### F1 — Payment write 409s against a concurrent booking; doc comment claims otherwise

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/Application/Members/SetPassPaid.cs:25-27, :61-67
- **Detail**: `MembershipPass.ConcurrencyStamp` is an `IsConcurrencyToken()`
  (`MembershipPassConfiguration.cs:47-50`). EF therefore adds `WHERE ConcurrencyStamp = @original` to
  *every* UPDATE of a tracked pass, including this one. Booking (`BookingProtocol.cs:161,225`) and
  attendance (`RecordAttendance.cs:132`) rotate that stamp. A booking that lands between `FindAsync`
  and `SaveChanges` makes the payment write return 409 `conflict`, with the message "Ktoś właśnie
  zmienił dane…". The doc comment says the payment write cannot race a booking and that concurrent
  writes are last-writer-wins. Both statements are false. The booking side is unaffected: payment
  never changes the stamp, so a booking never loses. Impact is low, because a retry succeeds.
- **Fix A ⭐ Recommended**: Write the payment through a store method (`IMembershipPassStore.SetPaidAsync`) that issues `ExecuteUpdateAsync` on `PaidAt`/`PaidRecordedBy` only.
  - Strength: Gives the documented last-writer-wins behaviour, keeps EF in Infrastructure, and stays out of the stamp entirely.
  - Tradeoff: One more store method. The entity is not tracked for the response, so the view must be built from a re-read.
  - Confidence: MED — `ExecuteUpdateAsync` is not used elsewhere in this repo yet; worth checking how the stores are shaped.
  - Blind spot: The test fixture may assert on tracked-entity state.
- **Fix B**: Keep the tracked update and correct the comment. Accept a rare 409 here, or add one automatic retry in the handler.
  - Strength: Minimal change. The 409 path already exists and is surfaced as a banner.
  - Tradeoff: The user may see a misleading "someone changed this person's data" message for a race they could not see.
  - Confidence: HIGH — the behaviour is already tested apart from the race.
  - Blind spot: None significant.
- **Decision**: FIXED via Fix A — `IMembershipPassStore.SetPaymentAsync` (ExecuteUpdateAsync on PaidAt/PaidRecordedBy), `SetPassPaid` no longer saves the unit of work; 404 if revoked mid-write. Build clean; 93 pass/payment tests green.

### F2 — A member promoted to staff keeps an uncleareable "Nieopłacony" marker

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/Infrastructure/Members/MemberQuery.cs:68, :120; src/Application/Members/SetPassPaid.cs:54-57
- **Detail**: S-25 allows granting Trainer to a member who already holds a karnet. If one of their
  karnets is unpaid, three things follow:
  - The admin list still computes `HasUnpaidPass` for that staff row.
  - The `unpaid` filter still includes the row.
  - `SetPassPaid` refuses the holder with `member_is_staff`, and the "Karnety" link is hidden on staff rows.

  The account then shows as a debtor in "Tylko nieopłacone" for good, with no way to settle it. The
  trainer list is unaffected, because it excludes staff.
- **Fix A ⭐ Recommended**: Exclude staff from the marker and the `unpaid` filter in `MemberQuery`, using the same `StaffPredicate`, and pin it with a test.
  - Strength: Consistent with S-25: the data becomes invisible and is never acted on, and the persona rule stays absolute.
  - Tradeoff: A real historical debt disappears from the debtor list once someone becomes staff.
  - Confidence: HIGH — `StaffPredicate` is the one Infrastructure definition of staff.
  - Blind spot: Whether the client cares about a trainer's old karnet debt.
- **Fix B**: Let `SetPassPaid` accept a staff holder, since recording money is not giving staff member data.
  - Strength: Keeps the debt visible and settleable.
  - Tradeoff: The first carve-out from "staff never hold member data". It also has to reach the UI, because the Karnety link is hidden on staff rows.
  - Confidence: MED — touches the persona rule, the tests and the admin screen.
  - Blind spot: The persona tests that pin `member_is_staff`.
- **Decision**: FIXED via Fix A — `MemberQuery` excludes staff (StaffPredicate) from `HasUnpaidPass` and the `unpaid` filter; pinned by `A_member_promoted_to_staff_is_neither_marked_nor_filtered_as_unpaid`.

### F3 — Plan deviations not recorded as "Adapted during implementation."

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence / Scope Discipline
- **Location**: context/changes/pass-paid-flag/plan.md
- **Detail**: This breaks the lessons.md rule. Each deviation is benign but none is noted in the plan:
  - `PersonaAccessTests` was not extended. Plan 2.5 said it would be; its MemberOnly shape does not fit TrainerOrAdmin routes, and the authorization is covered in `EndpointAuthorizationTests` and the route tests.
  - `app-checkbox` gained an `inputId` input. This is a shared-kit API change, and it is still not a `ControlValueAccessor`.
  - `core/passes/club-today.ts` was added.
  - The eager bundle grew by +4.41 kB, against the plan's "near-zero eager delta". The figure is recorded in AGENTS.md.
  - Test data uses `_paymentRandom` instead of `_random`.
  - The paid date renders as `d MMM y`, against the plan's `dd.MM.yyyy`.
  - The issue form has a client-side date check.
- **Fix**: Add "Adapted during implementation." notes to the matching contracts in plan.md (2.5, 3.1/3.2/3.3, 3 success criteria, 1.6).
- **Decision**: FIXED — eight "Adapted during implementation." notes added to plan.md (test-data RNG, F1 SetPaymentAsync, F2 staff exclusion, PersonaAccessTests, date format + club-today.ts, issue-form check, checkbox inputId, bundle delta).

### F4 — "Nieopłacony" badge styled in three places

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/app/src/app/shared/passes/pass-payment-status.scss; features/admin/members/members.scss; features/trainer/members/trainer-members.scss
- **Detail**: `.pass-unpaid`, `.badge-unpaid` and `.trainer-members-unpaid` all style the same marker. Neither list uses the shared `app-pass-payment-status` component. This is the third-copy pattern that S-19 calls a review finding.
- **Fix**: Render the marker in both lists through `app-pass-payment-status`, or a shared unpaid-only variant, and delete the two local classes.
- **Decision**: FIXED — both lists render `<app-pass-payment-status [paidAt]="null" />`; `.badge-unpaid` removed, `.trainer-members-unpaid` keeps only its margin. List specs green (70), lint clean, initial bundle 580.72 kB (+0.02).

### F5 — Trainer screen duplicates the payment controller from the admin karnet screen

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/app/src/app/features/trainer/passes/trainer-member-passes.ts; src/app/src/app/features/admin/members/member-passes.ts
- **Detail**: `openPayment` / `markPaid` / `clearPaid` / `replace` are copied between the two screens. The plan asked for identical behaviour, not a shared helper, so this is not drift. A third caller would make it the copy S-19 warns about.
- **Fix**: Extract a `shared/passes/pass-payment-actions.ts` function, held in a field like `form-state`, the next time this code is touched. No action now.
- **Decision**: FIXED — `shared/passes/pass-payment-actions.ts` (`createPassPaymentActions(replace)`, a function held in a field); both karnet screens bind `payment.*`. Screen specs green (17), lint clean, bundle unchanged.

### F6 — Re-running Up after Down marks every karnet paid

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Infrastructure/Persistence/Migrations/20261002095507_AddMembershipPassPayment.cs
- **Detail**: `Down` drops both columns, and that part works. A later `Up` re-runs the backfill, so karnets that were really unpaid become "paid on issue day". The SQL itself is correct: `AT TIME ZONE` on a `datetimeoffset`, then `CAST` to `date`.
- **Fix**: Note in the rollback section of `context/deployment/deploy-plan.md` that a Down→Up cycle loses the unpaid state.
- **Decision**: FIXED — "Lossy Down, pass-paid-flag" paragraph added to `context/deployment/deploy-plan.md` "Rollback note": leave the migration applied; export/restore PaidAt if Down must run.

### F7 — "Cofnij płatność" clears without confirmation or undo

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: features/admin/members/member-passes.ts:370; features/trainer/passes/trainer-member-passes.html
- **Detail**: One tap clears `PaidAt`, and the original payment date is lost. `PaidRecordedBy` keeps only the name of whoever cleared it. Re-marking is easy, but the date has to be re-entered from memory.
- **Fix**: Accept, or give the success toast an undo action that re-sends the previous `paidAt`.
- **Decision**: FIXED differently — ToastService has no actions, so instead of an undo the success toast names the cleared day ("Cofnięto płatność z 1 paź 2026."), so it can be re-entered. Pinned in trainer-member-passes.spec.ts.

### F8 — Trainer can tell a staff-held pass from a missing one

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Application/Members/SetPassPaid.cs:43-57
- **Detail**: An unknown pass returns 404, but a staff-held pass returns 409 `member_is_staff`. The trainer read returns 404 for staff ("staff do not exist from a trainer screen"). Exploiting this needs a known pass GUID, and nothing else leaks.
- **Fix**: Accept. Optionally return 404 instead of 409 for staff holders.
- **Decision**: FIXED — `SetPassPaid` returns 404 for a staff holder (test renamed `A_staff_holders_karnet_is_not_found`); `member_is_staff` copy reverted to its issue-only wording; plan.md adaptation note added.

### F9 — Every manual verification item is still open

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: context/changes/pass-paid-flag/plan.md, Progress 1.5–1.6, 2.3–2.4, 3.5–3.9, 4.4–4.6, 5.3
- **Detail**: All automated items are checked and were re-verified here, except E2E. None of the manual items are checked, so nothing is rubber-stamped. They are pending, including the phone/tablet/desktop pass from the lessons' mobile-first rule.
- **Fix**: Run the manual items (PASS-09..14, DASH, NAV-04) before the PR into `main`.
- **Decision**: PARTLY FIXED — after the triage fixes the full suites were re-run: `dotnet test` 794/794, `npm test` 952/952, `quality:check` clean. Still open, for the user: E2E (needs `docker compose up -d`) and the manual items (PASS-09..14, DASH, NAV-04, three widths) before the PR into main.
