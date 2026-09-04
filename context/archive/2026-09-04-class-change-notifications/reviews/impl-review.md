<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Class Change Notifications

- **Plan**: `context/changes/class-change-notifications/plan.md`
- **Scope**: Full plan — Phases 1-4 (git range `df2b8d1..HEAD`, 29 files, +3017/-34)
- **Date**: 2026-09-04
- **Verdict**: REJECTED on review; all findings triaged — 9 fixed, 1 skipped as a documented bounded tradeoff
- **Findings**: 1 critical, 6 warnings, 3 observations

## Verdicts

| Dimension | Verdict at review | After triage |
|-----------|-------------------|--------------|
| Plan Adherence | FAIL | PASS — F2, F3, F6 fixed |
| Scope Discipline | WARNING | PASS — the unplanned change now has a contract (F2) |
| Safety & Quality | FAIL | PASS — F1 fixed and pinned; F8 skipped as a bounded, documented tradeoff |
| Architecture | PASS | PASS — F10 fixed anyway; formatting left the Domain |
| Pattern Consistency | WARNING | PASS — F4, F5 fixed |
| Success Criteria | FAIL | WARNING — `quality:check` green and 21/22 manual criteria walked; 1.13 (delivery-time measurement) still open |

## Automated verification run

| Command | Result |
|---|---|
| `dotnet build src/po-prostu-silka.csproj` | PASS — 0 warnings, 0 errors |
| `dotnet test` | PASS — 198/198 |
| `grep -rn "using Microsoft.EntityFrameworkCore" src/Domain src/Application` | PASS — no hits |
| `npm test` (src/app) | PASS — 258/258 across 26 files |
| `npm run build` (src/app) | PASS — initial total 472.74 kB / 123.00 kB transfer, within budget |
| `npm run quality:check` (src/app) | **FAIL** — Prettier reports 2 unformatted files (`features/schedule/schedule.ts`, `schedule.spec.ts`) |

## Findings

### F1 — Long class-type name overflows the outbox `Subject` column and 500s the whole cancellation

- **Severity**: CRITICAL
- **Impact**: LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (Reliability / data path)
- **Location**: `src/Application/Notifications/ClassChangeNotification.cs:82`, `:99`
- **Detail**: `OutboxMessage.Subject` is `nvarchar(200)` (`Infrastructure/Persistence/Configurations/OutboxMessageConfiguration.cs:20`), but `ClassType.Name` is itself allowed the full 200 chars (`ClassTypeConfiguration.cs:15`, validated at `ClassTypeEndpoints.cs:108/328`). The rendered subjects are `"Odwołane zajęcia: " + Name` (218 chars worst case) and `"Zmiana w zajęciach: " + Name` (220). SQL Server refuses the insert with a truncation error; that surfaces as `DbUpdateException`, which `UnitOfWork.TrySaveChangesAsync` does **not** catch (it catches only `DbUpdateConcurrencyException`, `UnitOfWork.cs:33`). Result: unhandled 500, and because enqueue and the status flip share one `SaveChangesAsync`, **the cancellation never commits** — the admin sees an error and the class stays `Scheduled`. `AccountApprovedNotification` is immune only because its subject is a const, so this is a new failure mode introduced by this slice. `Body` is unaffected (`nvarchar(max)`).
- **Fix**: Truncate the name into the subject at the render site — cap `description.Name` so prefix + name stays within 200 chars, in both `ClassChangeNotification.cs:82` and `:99`.
  - Strength: One render site each, no schema change, no migration, and it keeps the render-at-enqueue discipline the outbox depends on. Widening the column would need a reversible migration for a value nobody wants to read past ~80 chars anyway.
  - Tradeoff: A pathologically long class name reads truncated in the email subject line — which mail clients would clip regardless.
  - Confidence: HIGH — column width, name width and the missing catch are all verified in-repo.
  - Blind spot: Not verified whether any existing class type is long enough to trigger it today; the ceiling is what matters, not current data.
- **Decision**: FIXED — private `Subject(prefix, name)` helper truncates to 200 chars, trimming the name and never the prefix (`ClassChangeNotification.cs:143-150`). Regression test `ClassCancellationTests.A_class_type_name_at_the_column_limit_still_lets_the_cancellation_commit` added and verified to FAIL without the fix (SQL truncation error on the outbox insert, cancellation not committed).

### F2 — The admin-calendar reversal has no plan contract and no "Adapted during implementation." note

- **Severity**: WARNING
- **Impact**: MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence / Scope Discipline
- **Location**: `src/Infrastructure/Scheduling/ClassScheduleQuery.cs:26-36` (commit `bc4edfd`)
- **Detail**: `GetUpcomingForAdminAsync` now filters `c.Status == ClassStatus.Scheduled`. No phase's "Changes Required" ever names this file — it was changed in a fifth commit landed after Phase 4's own. Three plan passages still assert the opposite: `plan.md:27` ("the admin query deliberately does not … **Neither needs changing**"), `plan.md:342` (Phase 2 §2, same claim), and `plan.md:619-620` (Manual Testing Steps step 4: "still present on the admin calendar"). The Desired End State *does* carry the "Changed after implementation, at the product owner's direction" note (`plan.md:63-70`), but that is narrative, not a contract — and `context/foundation/lessons.md` records exactly this as an accepted recurring rule: the "**Adapted during implementation.**" note belongs *on the contract*, in the same phase, before the phase-end commit. Phase 1's manual criterion and progress item 1.11 were updated; the rest were not, so the plan now contradicts itself. This is a repeat violation of a rule this project already wrote down. The behaviour itself is right — `ClassStore.HasTimeConflictAsync` only treats `Scheduled` classes as conflicting, so a cancelled tile would show an occupied slot that is actually free.
- **Fix A ⭐ Recommended**: Add a Changes Required item for `ClassScheduleQuery.cs` under Phase 2 carrying an "**Adapted during implementation.**" note, and correct `plan.md:27`, `:342` and `:620` to state the shipped behaviour.
  - Strength: Restores the plan as ground truth for the next reader and the next review, and satisfies the recorded lesson with the mechanism the lesson names.
  - Tradeoff: Phase 2's contract picks up work that actually landed after Phase 4, so the phase boundary reads slightly fictional.
  - Confidence: HIGH — this is the exact remedy `lessons.md` prescribes.
  - Blind spot: None significant.
- **Fix B**: Add a "Phase 5: admin-calendar reversal" section documenting `bc4edfd` as its own unit, and correct the three stale passages.
  - Strength: Honest about the commit history — the change genuinely happened outside the four planned phases.
  - Tradeoff: Grows the plan with a phase that had no planning pass, and the Progress section needs a matching block with no automated criteria of its own.
  - Confidence: MEDIUM — no precedent in this repo for a post-hoc phase.
  - Blind spot: How `/10x-archive` and `/10x-status` read a phase whose checkboxes were never planned.
- **Decision**: FIXED via Fix A — new `#### 2b. The admin calendar drops cancelled classes too` contract in Phase 2 carrying an "**Adapted during implementation.**" note; `plan.md:27`, the Phase 2 §2 sentence and Manual Testing Steps step 4 corrected to state the shipped behaviour.

### F3 — Phase 4's "Adapted" note describes row replacement; the code does row removal

- **Severity**: WARNING
- **Impact**: LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `src/app/src/app/features/admin/classes/classes.ts:428-431` vs `plan.md:551-557`
- **Detail**: The plan's adapted note says the cancel response "replaces that one row: it carries the new status and a `freeSpots` that accounts for any booking which committed between the click and the write." The code discards the response (`await this.classes.cancel(row.id);`) and filters the row out entirely; `classes.spec.ts:528-552` pins removal. The note was written for `54ff7a5` and invalidated by `bc4edfd` without being folded back — same root cause as F2. The generation-fence half of the note is still accurate (`classes.ts:420, 425-427`).
- **Fix**: Rewrite `plan.md:551-557` to describe row removal, and drop the now-dead `freeSpots`-from-response reasoning.
- **Decision**: FIXED — the Phase 4 §3 note now describes row REMOVAL, names §2b as the reason, and drops the dead `freeSpots`-from-response reasoning.

### F4 — The push prompt nags a member who already enabled push

- **Severity**: WARNING
- **Impact**: MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Pattern Consistency / Reliability
- **Location**: `src/app/src/app/core/notifications/push.service.ts:27` consumed by `src/app/src/app/features/notifications/push-prompt.ts:48-51`
- **Detail**: `subscribed` is a plain `signal(false)` flipped to `true` only by a successful `subscribe()` in the current page session; it is never rehydrated from `swPush.subscription`. `PushPrompt.visible()` gates on `!this.push.isSubscribed()`, so a member who enabled push last week sees the banner again on their next visit and has to dismiss it every 7 days as the localStorage cooldown expires. `push.service.ts` predates this slice (F-03), but `PushPrompt` is its first consumer, so the latent gap only becomes user-visible here — and phase 3 manual criterion 3.6 ("the prompt appears once") would not hold across sessions.
- **Fix A ⭐ Recommended**: Seed `subscribed` from `swPush.subscription` on first read, so `isSubscribed()` reflects the browser's actual registration rather than this session's history.
  - Strength: Fixes it at the source; every future consumer of `isSubscribed` inherits the correct answer, and it makes criterion 3.6 verifiable.
  - Tradeoff: `swPush.subscription` is an observable — the signal becomes asynchronously seeded, so the prompt may flash for one frame before hiding.
  - Confidence: MEDIUM — the shape is clear, but the exact seeding point (constructor vs. lazy) wants a look at how `SwPush.subscription` behaves when the SW is disabled in dev.
  - Blind spot: Not verified whether a `PushSubscriptions` row can exist server-side while the browser has dropped its local subscription; those two can diverge.
- **Fix B**: Persist an "enabled" flag in localStorage alongside the existing 7-day dismissal cooldown, and gate the prompt on it.
  - Strength: Synchronous, no flash, no service-worker timing questions.
  - Tradeoff: Patches the symptom at one call site; `isSubscribed()` stays wrong for the next consumer, and the flag goes stale if the member revokes permission in browser settings.
  - Confidence: HIGH — trivially works for the prompt.
  - Blind spot: Cleared site data resurrects the nag.
- **Decision**: FIXED via Fix A — `PushService` now seeds `subscribed` from `swPush.subscription` in its constructor and exposes `isReady`, false until that read returns; `PushPrompt.visible()` gates on it so the banner cannot flash at an already-subscribed member. Spec `waits for the existing registration to be read before asking` added; frontend 259/259.

### F5 — `class.service.ts` doc comment contradicts itself in adjacent paragraphs

- **Severity**: WARNING
- **Impact**: LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/app/src/app/core/scheduling/class.service.ts:84-95`
- **Detail**: Line 84-85 says "NOT a delete: the class **stays on the admin's calendar as `Cancelled`**"; line 88 then says "The class **also leaves the ADMIN's calendar**". The second is correct. The pre-`bc4edfd` sentence was appended to rather than corrected — the same drift pattern `lessons.md` describes, this time in a code comment.
- **Fix**: Drop the stale first sentence; keep the "difference from a delete is in the database, not the calendar" framing.
- **Decision**: FIXED — the stale pre-`bc4edfd` sentence is gone; the comment now says the class leaves both the member's screens and the admin's calendar, and that the difference from a delete is in the database.

### F6 — The planned DST test is missing, and the `ClubTime` assertions that exist are tautological

- **Severity**: WARNING
- **Impact**: MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Success Criteria / Plan Adherence
- **Location**: `tests/po-prostu-silka.Tests/ClassCancellationTests.cs:489, 536-537`; planned at `plan.md:594-595`
- **Detail**: The Testing Strategy calls for "`ClubTime` formatting across a DST boundary, so an email never states an hour the club does not recognise." No such test exists. The two assertions that touch `ClubTime` read `Assert.Contains(ClubTime.ToClubWallClock(startsAt), message.Body)` — they call the function under test to build the expected string, so a wrong zone, a wrong offset or a wrong culture passes silently. The only honest check is a prose comment at `:471` ("16:00 UTC in June is 18:00 in Warsaw"). `ToClubWallClock` (`ClubTime.cs:66-71`) is new code whose entire risk surface is zone and culture, and the plan itself names an email stating a wrong hour as the failure to prevent. No "Adapted" note records the omission.
- **Fix A ⭐ Recommended**: Add a `ClubTime` unit test with hardcoded expected strings for one summer and one winter UTC instant (CEST +2 and CET +1), and change the two `ClassCancellationTests` assertions to literal expected substrings.
  - Strength: Closes the gap the plan identified, and turns two decorative assertions into real ones. Cheap — no fixture, no database.
  - Tradeoff: Hardcoded strings must be updated if the rendered format ever changes; that is the point.
  - Confidence: HIGH — pure function, no I/O, trivially testable.
  - Blind spot: Whether the Windows and Linux CI images resolve the same timezone id; `ClubTime` should be checked for how it names the zone.
- **Fix B**: Record an "**Adapted during implementation.**" note explaining why the DST test was dropped, and leave coverage as is.
  - Strength: Honest bookkeeping at near-zero cost; the club is single-timezone and the offset comes from the platform database.
  - Tradeoff: Leaves the tautological assertions in place, so a future refactor of `ToClubWallClock` still cannot fail a test.
  - Confidence: LOW — hard to argue the coverage is adequate when the plan itself named this risk.
  - Blind spot: None significant.
- **Decision**: FIXED via Fix A — new `tests/po-prostu-silka.Tests/ClubTimeTests.cs` with five literal-string facts (CEST summer, CET winter, Polish culture under an invariant ambient culture, and a week across each DST transition). One expectation was wrong on the first run and the code was right, which is the point of literal expectations. The two tautological assertions in `ClassCancellationTests` now call a local `ClubWallClockOf` helper that restates the conversion independently — literals are impossible there because the `_slot` offset can land either side of a transition.

### F7 — "Lint and format clean" is checked in three phases, but `quality:check` fails

- **Severity**: WARNING
- **Impact**: LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: `plan.md` progress items 1.8, 3.4, 4.2; failing files `src/app/src/app/features/schedule/schedule.ts`, `schedule.spec.ts`
- **Detail**: `npm run quality:check` fails Prettier on two files. Neither is in this slice's diff — both were last touched by `df2b8d1` (the previous change's review fixes), so the breakage is inherited, not introduced here. But the criterion as written ("Lint and format clean") is checked `[x]` three times and does not currently hold, which means the gate was run narrowly or not at all. `AGENTS.md` requires `quality:check` before committing frontend changes.
- **Fix**: Run `npm run quality:fix` (it touches only the two inherited files) so the criterion is true, and note in the plan that the breakage came in with `df2b8d1`.
- **Decision**: FIXED — `npm run quality:fix` reformatted the two inherited files; `quality:check` is now clean end to end. A note under progress item 4.2 records that the breakage came in with `df2b8d1` and was not introduced by this slice.

### F8 — N+1 push-subscription lookup in the notification fan-out

- **Severity**: OBSERVATION
- **Impact**: LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (Performance)
- **Location**: `src/Application/Notifications/ClassChangeNotification.cs:150-161`
- **Detail**: One `GetForUserAsync` round trip per recipient inside the fan-out loop, executed inside the same request that already writes roughly two outbox rows per member. Bounded: `MaxCapacity` is 200 (`ClassEndpoints.cs:210`), so the worst case is 200 indexed reads. The doc comment states the tradeoff and defers batching explicitly, and the plan's Performance Considerations reach the same conclusion ("leave it, measure it"). Recorded so the ceiling stays a conscious choice rather than a discovery.
- **Fix**: None now — record the observed end-to-end delivery time for a full class (manual criterion 1.13) and revisit if it disappoints.
- **Decision**: SKIPPED — bounded at 200 by `MaxCapacity`, documented in the code and reached independently by the plan's Performance Considerations. To be revisited against the measurement criterion 1.13 asks for.

### F9 — All 22 manual criteria are unchecked while the change is stamped `implemented` and the plan is closed out

- **Severity**: OBSERVATION
- **Impact**: LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: `plan.md:678-683, 697-701, 715-720, 733-738`
- **Detail**: Every automated criterion across the four phases is `[x]` with a commit sha; every manual one is `[ ]`. Commit `b2869c1` closed the plan out as an epilogue regardless. The unverified set includes the slice's whole reason for existing: 1.12 (a real email arrives), 3.8 (a visible push on a subscribed device), 3.9 (tapping it opens the app). Nothing in the diff can substitute for these — they need a real device and a real mailbox. No rubber-stamping detected; the risk is the opposite, that the change reads as done while its end-to-end claim is untested. Criterion 1.13 (record observed delivery time) also feeds F8.
- **Fix**: Walk the manual list on a real device before archiving, or record explicitly in the plan which items are deferred and why.
- **Decision**: FIXED — the product owner confirmed they walked the manual list and all criteria passed except the measurement; 21 of 22 are now `[x]` in the plan's Progress. 1.13 (observed end-to-end delivery time) stays open with a note, because it was not measured and it is the input to the deferred `OutboxOptions` decision and to F8.

### F10 — `ClubTime.ToClubWallClock` puts Polish-culture presentation formatting in `Domain`

- **Severity**: OBSERVATION
- **Impact**: MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Architecture
- **Location**: `src/Domain/Scheduling/ClubTime.cs:66-71`
- **Detail**: Layering holds everywhere — no EF Core in `Domain` or `Application`, dependency direction clean, EF-touching pieces all in `Infrastructure`. This one call is the borderline: the timezone constant already lived in `Domain` and the doc argues the placement at length, but `ToClubWallClock` is the first *render-shaped* code in the Domain layer. It is defensible for three messages; it is the seam to watch if more notification templating lands.
- **Fix**: None now. If a fourth or fifth rendered message arrives, move formatting to an Application-layer renderer and leave `ClubTime` holding only the zone and the conversion.
- **Decision**: FIXED — formatting moved out of the Domain. `ClubTime` now exposes `ToClubLocal` (conversion only) and keeps the zone and the DST arithmetic; the culture and the format string live in the new `src/Application/Notifications/MessageTime.cs`, which `ClassChangeNotification` calls. Both classes' doc comments name the split and why it is kept.

## Confirmed correct (no action)

Recorded so a future review does not re-derive them.

- **Cancel handler ordering** (`ClassEndpoints.cs:711-780`): recipients read → status flip → `ConcurrencyStamp` rotation (`:750`) → enqueue → one `TrySaveChangesAsync` (`:768`), 409 `conflict` on a lost race. The "cancelled with nobody told" window is genuinely closed.
- **Update path** captures the three old values before mutation (`:483-489`) and takes the new instructor name from the validated account, avoiding the "18:00 → 18:00" trap the plan calls out.
- **Authorization**: the cancel endpoint is registered on the same admin group as its siblings (`ClassEndpoints.cs:220-229`) and inherits `AuthorizationPolicyNames.Admin`; pinned by `ClassCancellationTests.Cancel_refuses_a_member:262`.
- **No schema change and none needed** — `ClassStatus.Cancelled` already existed; no migration is missing.
- **`ClassChangeNotification` matches `AccountApprovedNotification`** on every load-bearing axis: two constructor deps, render-at-enqueue, no save, one email row plus one push row per device, blank-email guard, subscription id (not endpoint) as the push recipient.
- **All ten "What We're NOT Doing" guardrails respected** — no un-cancel, no past-class cancellation, no third booking status, no capacity-only or no-op notification, plain text only, no in-app notification center, `OutboxOptions` untouched, no trainer notification, no profile toggle.
- **Push payload contract** (`WebPushSender.cs:104-146`) matches the Angular service worker's required `{ notification: { title, … } }` shape and is pinned by `WebPushPayloadTests.cs`.

## Triage

Walked 2026-09-04, all ten findings decided.

| Finding | Decision |
|---|---|
| F1 | FIXED — subject truncation + regression test, verified to fail without the fix |
| F2 | FIXED (Fix A) — Phase 2 §2b contract with an "Adapted during implementation." note |
| F3 | FIXED — Phase 4 §3 note rewritten to row removal |
| F4 | FIXED (Fix A) — `isReady` seeded from `swPush.subscription` |
| F5 | FIXED — contradictory doc comment corrected |
| F6 | FIXED (Fix A) — `ClubTimeTests` with literal expectations; tautologies replaced |
| F7 | FIXED — `quality:fix`, plus a plan note attributing the breakage to `df2b8d1` |
| F8 | SKIPPED — bounded at 200 and documented; revisit against criterion 1.13 |
| F9 | FIXED — 21/22 manual criteria checked off; 1.13 left open with a note |
| F10 | FIXED — `MessageTime` in Application; `ClubTime` keeps zone and arithmetic only |

### Verification after triage

| Command | Result |
|---|---|
| `dotnet build src/po-prostu-silka.csproj` | PASS — 0 warnings, 0 errors |
| `dotnet test` | PASS — 204/204 (was 198; +5 `ClubTimeTests`, +1 subject-overflow regression) |
| `npm test` (src/app) | PASS — 259/259 (was 258; +1 push-prompt readiness spec) |
| `npm run quality:check` (src/app) | PASS — clean, Prettier and ESLint |
| `npm run build` (src/app) | PASS — initial total 472.99 kB / 123.06 kB transfer |

### Still open

- **Plan criterion 1.13** — the observed end-to-end delivery time for a full class is not measured.
  It feeds the deferred `OutboxOptions` decision and F8's N+1 ceiling. Worth taking before archiving.
