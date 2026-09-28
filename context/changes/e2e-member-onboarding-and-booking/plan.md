# E2E: Invitation Claim and Staff Booking Implementation Plan

## Overview

Add three Playwright specs to `src/app/e2e/` covering manual cases `REG-01`, `BOOK-01` and `BOOK-03` end to end:
an invited person lands on the club's record carrying its karnet and booking, a trainer's booking reaches the
member's Start screen, and a booking without a valid karnet is refused in the overlay with the words the real
API reason maps to. The specs arrange their own data through the admin API and clean it up. That shared
support layer is the pattern every later spec copies, S-32 first. No production code changes.

## Current State Analysis

- Four specs exist (`seed`, `guarded-route-redirects-to-login`, `tab-switches-do-not-grow-history`,
  `back-closes-open-overlay`). All run as the seeded admin through the `setup` project's saved cookie
  (`e2e/auth.setup.ts`, `playwright/.auth/admin.json`). No spec yet acts as a member or a trainer, or
  registers an account.
- `back-closes-open-overlay.spec.ts` carries the only data-arranging code. Its `createClass`/`afterEach` are
  inline. It takes **the first trainer** from `/api/admin/trainers` and fails on a database without one, which
  is exactly what a fresh CI database (S-30) is. It also books a **fixed slot**, "an hour from now". The
  club-wide overlap rule (`ClassStore.HasTimeConflictAsync`, `src/Infrastructure/Scheduling/ClassStore.cs:60`,
  half-open, `Scheduled` classes only) makes that collide with any parallel spec that picks the same hour.
- **Registration is rate-limited**: 3 per 5 minutes per partition (`src/Api/Program.cs:207`). The partition is
  the **last** `X-Forwarded-For` segment, falling back to the socket address
  (`src/Api/Auth/RateLimitPolicies.cs`, `PartitionKey`). Every run of these specs registers at least one
  account, so a second local run within 5 minutes, or a CI retry, would get 429 without a workaround.
- **Nothing a member-shaped spec creates can be fully deleted.** Members and accounts have no delete endpoint.
  The removal order for what can go is fixed by the API's refusals. Release bookings first
  (`DELETE /api/admin/classes/{id}/bookings/{bookingId}`), then delete the class, which is refused with
  `has_bookings` otherwise (`DeleteClass.cs:49`). Then revoke the karnet, refused with `has_active_bookings`
  otherwise (`RevokePass.cs:51`). Last, deactivate the class type (types cannot be deleted).
- The invitation link is built by `members.ts:742` as `${origin}/register?invitationCode=<code>` and written
  with `navigator.clipboard.writeText`. The register screen reads the code from the query and submits
  `{ email, password, memberCode }` (`RegisterRequest.cs`). On success it navigates to `/`.
- A trainer's candidate search in the bookings overlay is name-only and members-only
  (`core/scheduling/booking-candidates.ts`, `/api/trainer/members`). A refusal is shown in the overlay as
  `<p class="field-error" role="alert">` from `bookingFailureMessage` (`class-bookings-overlay.html`, the
  `addFailure()` block). `no_valid_pass` maps to "Ta osoba nie ma karnetu ważnego w dniu tych zajęć."
  (`core/scheduling/booking-failure.ts:32`).
- The member's Start shows one "Najbliższe zajęcia" card, the next booking only, and a "Twój karnet" card with
  `entriesLeft` over `z {{ entryCount }}` (`features/dashboard/dashboard.html`). Entries left are derived from
  active bookings, so a booked future class already shows one entry fewer.

## Desired End State

From `src/app`, with Docker up and migrations applied, `npm run e2e` runs seven tests green: the setup
projects plus four existing specs plus three new ones. It does so on the developer database and on a
database holding only the seeded admin. Running it twice back to back is also green: no 429, no slot
collision, no "already exists". After a run, the only rows left behind are `E2E …`-named members and
accounts with `@example.test` addresses, plus the one E2E trainer. Each new spec was shown to fail when the
behaviour it protects is broken.

### Key Discoveries:

- Rate-limit partition is the last XFF segment: `src/Api/Auth/RateLimitPolicies.cs` (`PartitionKey`).
- Overlap rule is club-wide and ignores cancelled classes: `src/Infrastructure/Scheduling/ClassStore.cs:60-85`.
- Cleanup refusals: `DeleteClass.cs:49,60` (`has_bookings`), `RevokePass.cs:51` (`has_active_bookings`).
- Member creation carries no e-mail (`MemberRequest.cs:28`): the address arrives with registration, and
  `Members.Email` is unique (`MemberConfiguration.cs:86`).
- A trainer role needs an account (`ChangeTrainerRole.cs:82`, `no_account`), so the E2E trainer is created
  through the same claim path the first spec tests.
- The playwright `setup` project already matches `/.*\.setup\.ts/` (`playwright.config.ts`), so a second
  setup file needs no config change to run. It does need to run **independently** of `auth.setup.ts`, since
  `fullyParallel: true` gives no ordering between them.

## What We're NOT Doing

- **Not testing the registration rate limit.** Specs step around it (decision: edge case, integration-owned).
- No production code, no test-only endpoints, no delete-member capability (S-29 owns erasure).
- Not driving the "Dodaj członka" or karnet forms through the UI. Those are arranged through the API, and
  their forms have SPA specs and integration tests.
- Not asserting the admin member list's "Bez konta" disappearing after the claim (REG-01's third bullet). It
  is a separate screen and a separate risk.
- Not covering the other `BOOK-03` refusals (no entries left, full class, already booked, blocked, staff).
  They share the same outlet, and their reason→sentence mapping is pinned by SPA specs and the contract tests
  (test-plan §6.4). One refusal proves the outlet end to end.
- Not covering the trainer's narrowing to their own classes (`MayActOn`), which is integration-owned (risk #2).
- Not wiring CI (S-30), and not adding a separate phone-width variant.

## Implementation Approach

Build a small `e2e/support/` layer first, then write each spec as arrange (API) → act (UI) → assert (UI) on
top of it. Cleanup is registered **at creation time** into a per-test registry that a fixture tears down in
reverse order. A failing assertion therefore still cleans up, and the removal order falls out of creation
order. The E2E trainer is the one
deliberately shared object. It is get-or-created by a setup file that runs once per run before any spec, so
two workers never race to create it.

## Critical Implementation Details

- **Cleanup order vs creation order.** The class's cleanup releases **every** booking on it, including one
  made through the UI that the registry never saw, and then deletes the class. The karnet can be revoked only
  after that (`has_active_bookings`). With a reversed registry, that means every spec arranges the karnet
  **before** the class. `book()` registers no cleanup of its own, because the class's cleanup covers it.
- **`trainer.setup.ts` cannot rely on `admin.json`.** Setup files run in parallel, so it signs in as the admin
  itself through its own request context.
- **Dates render in the browser's zone.** Keep the existing spec's `test.use({ timezoneId, locale: 'pl-PL' })`
  so the day and week the helper computes are the ones the calendar shows.

## Phase 1: Shared E2E support

### Overview

Everything the three specs, and S-32, arrange with: an admin API context, data builders with registered
cleanup, a collision-free class slot, anonymous and signed-in browser contexts, registration that never hits
the rate limit, and the one fixed E2E trainer. The existing overlay spec moves onto it.

### Changes Required:

#### 1. Credentials

**File**: `src/app/e2e/credentials.ts`

**Intent**: Add the E2E trainer's fixed identity and its session file, beside the admin's.

**Contract**: `trainerCredentials` (`email: 'e2e-trainer@example.test'`, a development-only password,
env-overridable like the admin's), `trainerDisplayName: 'E2E Trener'`, and
`trainerAuthFile = 'playwright/.auth/trainer.json'` (already gitignored by `**/playwright/.auth/`).

#### 2. Data builders and cleanup fixture

**File**: `src/app/e2e/support/fixtures.ts`, `src/app/e2e/support/club.ts` (names indicative)

**Intent**: One `test` export extending Playwright's with an `adminApi` fixture (an `APIRequestContext` on
the admin's `storageState`) and a `cleanup` registry torn down in reverse order after each test, tolerating
404. Builders return ids and register their own cleanup.

**Contract**:

- `createMember(displayName)` → `POST /api/admin/members`. No cleanup (members cannot be deleted); names
  must be `E2E <purpose> <ts>`.
- `issuePass(memberId, { entries, validFrom, validTo })` → `POST /api/admin/members/{id}/passes`
  (`typeName`, `validFrom`, `validTo`, `entryCount`). Cleanup: `DELETE …/passes/{passId}`.
- `createClass({ name, instructorMemberId })` → class type plus class in a free slot (item 3). Cleanup: release
  every booking listed by `GET /api/admin/classes/{id}/bookings`, delete the class, deactivate the type.
- `book(classId, memberId)` → `POST /api/admin/classes/{id}/bookings` `{ memberId }`.
- `issueInvitation(memberId)` → `POST /api/admin/members/{id}/access-code` → `{ code, expiresAt }`.
- `registerViaApi({ email, password, code })` → `POST /api/auth/register` with a unique XFF (item 4).
- Every spec imports `test`/`expect` from here, not from `@playwright/test`.

#### 3. Collision-free class slot

**File**: `src/app/e2e/support/slots.ts`

**Intent**: Pick a start that no parallel spec or re-run will share, and survive the rare collision instead
of failing on it.

**Contract**: a random day from tomorrow to +6 days and a random 15-minute start between 07:00 and 21:00,
local time, for a 30-minute class. On `409 time_conflict`, draw again, up to a bounded number of attempts,
then fail with a message naming the cause. Also export the helper that brings a start into view in the
desktop week calendar: one "Następny tydzień" click when the start falls in the next Monday-based week.
Tomorrow-or-later means the class never starts mid-test, which would close bookings (`class_started`).

#### 4. Browser contexts and the rate limit

**File**: `src/app/e2e/support/sessions.ts`

**Intent**: Give specs the two extra personas without ever logging in through the form, and keep
registrations out of each other's rate-limit partition.

**Contract**:

- `uniqueClientAddress()` → a fake address unique per call (e.g. `10.x.y.z` from time plus a counter), sent as
  `X-Forwarded-For`. The limiter keys on the last segment. A comment says this sidesteps a courtesy cap that
  is deliberately not under test here.
- `anonymousContext(browser)` → a new context with an empty `storageState` and that header in
  `extraHTTPHeaders`, so the SPA's own register call carries it.
- `signedInContext(browser, credentials)` → a new context signed in through `POST /api/auth/login` on its own
  `request`.

#### 5. The E2E trainer

**File**: `src/app/e2e/trainer.setup.ts`

**Intent**: Ensure exactly one E2E trainer exists and save its session, once per run, on any database,
including one with only the seeded admin.

**Contract**: signs in as the admin on its own request context. Finds the member by
`GET /api/admin/members?search=<trainer email>`. Repairs whichever step is missing: create the member, issue
the code, register (unique XFF), then `POST /api/admin/members/{id}/roles/trainer`. Then signs in as the
trainer and saves `trainerAuthFile`. Idempotent: a second run only signs in. A wrong password on an existing
account fails loudly and names the account.

#### 6. Move the overlay spec onto the support layer

**File**: `src/app/e2e/back-closes-open-overlay.spec.ts`

**Intent**: Remove the fixed "an hour from now" slot and the first-trainer assumption. Both break the new
specs, and fresh databases.

**Contract**: uses `createClass` with the E2E trainer and the slot helper. Its inline `nextStart`,
`createClass` and `afterEach` go away. The day-strip navigation it does at phone width stays its own. The
test name and the risk header are unchanged.

#### 7. Rules

**File**: `src/app/e2e/CLAUDE.md`

**Intent**: Make the new pattern the rule a generated spec follows.

**Contract**: add rules for importing `test` from `support/fixtures`, arranging through the builders (never
`page.request` ad hoc, never production code), registering cleanup at creation, class slots only from the
slot helper, registration only through the support layer, and the "members stay behind, named E2E" note.

#### 8. Adapted during implementation

- **`guarded-route-redirects-to-login.spec.ts` was already red on `main`,** so the suite could not be
  green without touching it. Commit `77f4665` (sign-in restyle) renamed the login `h1` from "Cześć!" to
  "Zaloguj się". It also added a "Pokaż hasło" toggle, which makes `getByLabel('Hasło')` ambiguous. Both
  locators were updated: the heading now reads "Zaloguj się", and the password field uses
  `getByLabel('Hasło', { exact: true })`. The spec's risk and assertions are otherwise unchanged. The
  register screen probably carries the same toggle, so Phase 2 must use `exact: true` too.
- **The cleanup registry got its own file,** `support/cleanup.ts`, beside `fixtures.ts` and `club.ts`. The
  builders live on a `Club` class, exposed as a `club` fixture. `credentials.ts` also gained
  `memberPassword` for the accounts specs register.
- **Local runs on this machine:** port 5264 falls in a Windows-reserved TCP range (5241–5340), so
  `webServer` cannot bind it. Verification ran the API with `--urls http://localhost:5480` and
  `E2E_BASE_URL=http://localhost:5480`, which the config already supports. No config change was made.

### Success Criteria:

#### Automated Verification:

- Specs compile and are discovered: `npx playwright test --list` (from `src/app`)
- Formatting passes: `npm run quality:check` (from `src/app`)
- Suite green on the dev database: `npm run e2e`
- Suite green twice in a row within 5 minutes (no 429, no `time_conflict` failure): `npm run e2e && npm run e2e`

#### Manual Verification:

- On a database holding only the seeded admin (e.g. `TestDataSeed:Reset`, or a fresh container),
  `npm run e2e` creates the E2E trainer and passes. A second run creates no second trainer.
- After a run, the admin calendar shows no leftover `E2E` classes and the class-type list shows only
  deactivated `E2E` types.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for
manual confirmation from the human that the manual testing was successful before proceeding to the next
phase.

---

## Phase 2: The invitation claim carries the karnet and the booking

### Overview

`REG-01` end to end. Arrange a recorded member with a karnet and a booking. Then drive the UI where the risk
lives: the admin issues the code and copies the link from the member list, an anonymous visitor registers
from that link, and Start shows the record's karnet and booking.

### Changes Required:

#### 1. Spec

**File**: `src/app/e2e/invitation-claim-carries-karnet-and-booking.spec.ts`

**Intent**: Prove that the link the admin copies claims *that* record. Registration must not produce a
fresh, empty member, and the new session must land on Start showing what the club recorded.

**Contract**:

- Provenance header: the risk (REG-01; test-plan risk #3's claim-only registration door), the seed, what it
  creates and what stays behind.
- Arrange (API): member `E2E zaproszenie <ts>`, a karnet of 5 entries valid today to +30 days, and a class
  (instructor: E2E trainer) with that member booked.
- Act (UI, admin page with `clipboard-read`/`clipboard-write` granted): Członkowie → search the name → row
  menu "Akcje — <name>" → "Wygeneruj kod klubowicza" → "Kopiuj link". Read the link from the clipboard.
  Open it in `anonymousContext`. Fill "Adres e-mail" (`e2e-<ts>@example.test`) and "Hasło", then submit
  "Załóż konto".
- Assert (anonymous page): URL `/`; the greeting h1 names the member; "Najbliższe zajęcia" shows the class
  name; "Twój karnet" shows 4 left of 5, because the arranged booking already holds one entry.
- Do not assert the link's shape (`invitationCode=`). Opening it and landing on the record is the assertion.

#### 2. Adapted during implementation

- **No greeting assertion.** Start greets by the first word of the name ("Cześć, E2E!"), so it identifies
  nobody. The unique class name and `4 z 5` are what prove the record was claimed. A fresh, empty member
  would show both cards' empty states. The spec asserts the "Załóż konto" heading straight after opening
  the link, so the wrong-parameter break fails there by name rather than on the test timeout.
- **Phase 1's cleanup was wrong for any booked class, and it went unnoticed.** A release only marks a
  booking `Cancelled`, and the row keeps its class and karnet. `DeleteClass` refuses any booking, cancelled
  ones included (by design: history), and `removeClass` ignored that 409. `RevokePass` checks *active*
  bookings only, so on a karnet with a released booking it hits the restrict FK and answers **500**. That
  is a production defect, recorded here for a separate change. The user's decision (2026-09-28):
  `removeClass` deletes the class when nobody was ever booked, and otherwise **cancels** it (freeing the
  slot). A karnet whose member held a booking on the class is left behind rather than revoked. So
  **cancelled `E2E` classes and karnets on `E2E` members now stay behind too**, which amends the Desired End
  State and manual check 1.6.
- **E2E runs locally only.** The user's decision (2026-09-28): never against the staging database, and not
  in the deploy pipeline, because of what runs leave behind. That conflicts with S-30 (CI wiring), which
  needs re-scoping. Instead, `.githooks/pre-push` (enabled with `git config core.hooksPath .githooks`)
  rebuilds the SPA and runs the suite before every push. It borrows the newest nvm Node when the default
  is below 22, and `git push --no-verify` or `SKIP_E2E=1` skips it. `playwright.config.ts`'s `webServer`
  now passes `--urls ${baseURL}`, so `E2E_BASE_URL` can move the API off the reserved port 5264.

### Success Criteria:

#### Automated Verification:

- The spec passes: `npx playwright test e2e/invitation-claim-carries-karnet-and-booking.spec.ts`
- Full suite still green: `npm run e2e`
- Formatting passes: `npm run quality:check`

#### Manual Verification:

- Break-verify: change the query parameter `copyInvitationLink` writes (e.g. `memberCode=`). The spec fails on
  the register screen, not later. Revert, then re-run so `wwwroot` is rebuilt from clean code.
- Break-verify: make the dashboard's pass card read nothing (e.g. make `loadPass` resolve `null`). The spec
  fails on the karnet assertion. Revert and re-run.

**Implementation Note**: pause for manual confirmation before Phase 3.

---

## Phase 3: A trainer's booking reaches the member; a booking without a karnet is refused

### Overview

`BOOK-01` and `BOOK-03` as two spec files, both acting as the E2E trainer on a class they instruct.

### Changes Required:

#### 1. Booking reaches the member

**File**: `src/app/e2e/staff-booking-reaches-member.spec.ts`

**Intent**: Prove that a trainer's booking made in the overlay is what the member then sees: the class on
Start, and one entry fewer on the karnet.

**Contract**:

- Arrange (API): member `E2E zapis <ts>` with an account (code plus `registerViaApi`, address
  `e2e-<ts>@example.test`), a karnet of 5 entries, and a class instructed by the E2E trainer with no bookings.
- Act (UI, `signedInContext` as the trainer): Grafik → bring the class's week into view → open the class →
  dialog `Zapisani na „<name>”` → "Dopisz członka": type the name → choose it in "Osoba do dopisania" →
  "Zapisz".
- Assert (trainer): the member's name is listed in the dialog.
- Assert (`signedInContext` as the member): Start shows the class under "Najbliższe zajęcia", and "Twój
  karnet" shows 4 of 5.

#### 2. Refusal without a karnet

**File**: `src/app/e2e/booking-without-karnet-is-refused.spec.ts`

**Intent**: Prove that the real API's refusal reaches the overlay as its sentence, and that nothing was
booked.

**Contract**:

- Arrange (API): member `E2E bez karnetu <ts>`, with no account and no karnet, and a class instructed by the
  E2E trainer.
- Act: as in item 1, choosing this member (listed "— bez konta").
- Assert: an alert in the dialog reads "Ta osoba nie ma karnetu ważnego w dniu tych zajęć.". The roster still
  reads "Nikt nie jest jeszcze zapisany na te zajęcia." and the count "0 / 5 miejsc zajętych" is unchanged.

#### 3. Test plan

**File**: `context/foundation/test-plan.md`

**Intent**: Record what the e2e layer now covers and the pattern it uses.

**Contract**:

- §4 e2e row and §5 e2e gate: add the three journeys.
- §6.6: point to `e2e/support/` (builders, cleanup registry, slot helper, XFF note, E2E trainer) and the
  break-verify for each new spec.
- §6.7: a note for this slice: what stays behind, and why the rate limit is stepped around rather than
  tested.
- Update the freshness ledger if §8 asks for it.

### Success Criteria:

#### Automated Verification:

- Both specs pass: `npx playwright test e2e/staff-booking-reaches-member.spec.ts e2e/booking-without-karnet-is-refused.spec.ts`
- Full suite green twice in a row: `npm run e2e && npm run e2e`
- Formatting passes: `npm run quality:check`

#### Manual Verification:

- Break-verify the booking spec: make `GET /api/bookings/mine` omit the new booking (e.g. filter it out in the
  SPA's bookings service). The spec fails on the member's Start. Revert and re-run.
- Break-verify the refusal spec: change the `no_valid_pass` sentence in `booking-failure.ts`. The spec fails
  on the alert. Revert and re-run.
- `test-plan.md` reads correctly for someone adding the next spec (S-32).

**Implementation Note**: pause for manual confirmation before closing the slice.

---

## Testing Strategy

### Unit Tests:

- None. The support layer is exercised by the specs themselves, and a helper bug shows as a red spec.

### Integration Tests:

- None added. The rules these journeys pass through (claim, derivation of entries, refusals, rate limit) keep
  their integration coverage (test-plan risks #2, #3, #6).

### Manual Testing Steps:

1. Run `npm run e2e` twice back to back and confirm it is green both times.
2. Run it against a database with only the seeded admin, and confirm the E2E trainer is created once.
3. Perform each break-verify above and confirm the named spec fails at the named assertion.
4. Inspect the admin calendar and class types after a run for leftovers.

## Performance Considerations

Each new spec does a handful of API calls plus one or two extra browser contexts. The suite stays well under
a minute on top of the `e2e:stage` build. Parallel workers are safe by construction: unique names and
addresses, a random slot with retry, a per-test XFF, and a shared trainer created before any spec.

## Migration Notes

None. No schema or production change. Leftover `E2E` members and accounts accumulate in local and staging-like
databases by decision. `TestDataSeed:Reset=true` clears them locally.

## References

- Roadmap: `context/foundation/roadmap.md` S-31 (and S-30, S-32)
- Manual cases: `context/testing/01-account-and-access.md` REG-01,
  `context/testing/04-schedule-bookings-attendance.md` BOOK-01, BOOK-03
- Test plan: `context/foundation/test-plan.md` §2 risks #2, #3, #6; §6.6
- Seed and rules: `src/app/e2e/seed.spec.ts`, `src/app/e2e/CLAUDE.md`
- Existing data-arranging spec: `src/app/e2e/back-closes-open-overlay.spec.ts`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Shared E2E support

#### Automated

- [x] 1.1 Specs compile and are discovered: `npx playwright test --list` — a3defb7
- [x] 1.2 Formatting passes: `npm run quality:check` — a3defb7
- [x] 1.3 Suite green on the dev database: `npm run e2e` — a3defb7
- [x] 1.4 Suite green twice in a row within 5 minutes: `npm run e2e && npm run e2e` — a3defb7

#### Manual

- [ ] 1.5 On an admin-only database the E2E trainer is created once and the suite passes
- [ ] 1.6 No leftover E2E classes; only deactivated E2E class types remain

### Phase 2: The invitation claim carries the karnet and the booking

#### Automated

- [x] 2.1 The spec passes: `npx playwright test e2e/invitation-claim-carries-karnet-and-booking.spec.ts`
- [x] 2.2 Full suite still green: `npm run e2e`
- [x] 2.3 Formatting passes: `npm run quality:check`

#### Manual

- [x] 2.4 Break-verify: wrong query parameter in the copied link fails the spec on the register screen
- [x] 2.5 Break-verify: an empty pass card fails the spec on the karnet assertion

### Phase 3: A trainer's booking reaches the member; a booking without a karnet is refused

#### Automated

- [ ] 3.1 Both specs pass: `npx playwright test e2e/staff-booking-reaches-member.spec.ts e2e/booking-without-karnet-is-refused.spec.ts`
- [ ] 3.2 Full suite green twice in a row: `npm run e2e && npm run e2e`
- [ ] 3.3 Formatting passes: `npm run quality:check`

#### Manual

- [ ] 3.4 Break-verify: the member's bookings omitting the new booking fails the booking spec
- [ ] 3.5 Break-verify: a changed `no_valid_pass` sentence fails the refusal spec
- [ ] 3.6 `test-plan.md` reads correctly for the next spec author
