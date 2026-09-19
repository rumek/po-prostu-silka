# Test Plan

> Phased test rollout for this project. Strategy is frozen at the top
> (§1–§5); cookbook patterns at the bottom (§6) fill in as phases ship.
> Read before writing any new test.
>
> Refresh: re-run `/10x-test-plan --refresh` when stale (see §8).
>
> Last updated: 2026-09-14

## 1. Strategy

Tests follow three non-negotiable principles for this project:

1. **Cost × signal.** The cheapest test that gives a real signal for the
   risk wins. Do not promote to e2e because e2e "feels safer." Do not put a
   vision model on top of a deterministic visual diff that already catches
   the regression.
2. **User concerns are first-class evidence.** Risks anchored in "the team
   is worried about X, and the failure would surface somewhere in <area>"
   carry the same weight as PRD lines or hot-spot data.
3. **Risks are scenarios, not code locations.** This plan documents *what
   could fail* and *why we believe it's likely* — drawn from documents,
   interview, and codebase *signal* (churn, structure, test base). It does
   NOT claim to know which line owns the failure. That knowledge is
   produced by `/10x-research` during each rollout phase. If the plan and
   research disagree about where the failure lives, research is the
   ground truth.

Hot-spot scope used for likelihood weighting: `src/` (Domain, Application,
Infrastructure), `src/app/src/app/`, `tests/` — excluding migrations,
`*.Designer.cs`, lockfiles, `context/` and build output. 118 commits in the
30 days to 2026-09-11.

Timing constraint: roadmap S-18 (backend split into projects, no behaviour
change) and S-19 (SPA error-handling refactor) are `ready`. Phases 1–3 are
the safety net those slices lean on; land them first where possible.

## 2. Risk Map

The top failure scenarios this project must protect against, ordered by
risk = impact × likelihood. Risks are failure scenarios in user / business
terms, not test names. The Source column cites the *evidence that surfaced
this risk* — never a specific file as "where the failure lives" (that is
research's job, see §1 principle #3).

| # | Risk (failure scenario) | Impact | Likelihood | Source (evidence — not anchor) |
|---|-------------------------|--------|------------|--------------------------------|
| 1 | Two staff bookings race for the last spot in a class (or the last entry on a karnet) and both succeed — the class runs over capacity, or the pass over its entry pool | High | High | PRD §Guardrails "no overbooking"; PRD FR-008 Socrates note (simultaneous booking); roadmap S-16 risk (second concurrency invariant); interview Q1; hot-spot dir `src/Application/Scheduling` (31 file-changes/30d) |
| 2 | A booking is admitted or refused wrongly — no karnet valid on the class's club-local date, no entry left, entries miscounted after a booking is released or its member is blocked, a trainer booking into a class they do not instruct | High | High | AGENTS.md hard rule (S-16); roadmap S-16 outcome; interview Q3; hot-spot dirs `src/Infrastructure/Scheduling` (27), `src/Domain/Scheduling` (23). Entries held by a club-cancelled class stay consumed by design (S-16) — an open product decision, roadmap Open Question 7, not a defect this risk covers |
| 3 | A retired or closed door still opens — a member books or cancels their own spot, registration succeeds without an invitation code, or a burst of registrations from one client is not capped (the per-address cap is a courtesy limit by design, not an authorization control) | High | High | interview Q2; roadmap S-16 risk ("the larger risk is subtraction"); roadmap S-17 risk (guard, link and API refusal must land together); roadmap S-18 relocates every endpoint |
| 4 | A signed-in user reaches data or actions that are not theirs — a member reads another member's plan, bookings or contact details; the password reset reveals which addresses are registered | High | Medium | PRD NFR "personal data privacy"; PRD FR-026; roadmap S-13 risk (enumeration oracle); roadmap S-14 risk (authorization claims); hot-spot dirs `src/Application/Members` (34), `src/Application/Auth` (24); stronger likelihood signal from Phase 2 research: authorization is declared per route group with no fallback policy, and roadmap S-18 re-creates every group |
| 5 | A class is cancelled or changed and a booked member receives no email or push — or someone not booked receives one | High | Medium | PRD §Guardrails "no missed cancellations"; PRD US-02; roadmap S-09 (M-1 north star) |
| 6 | A structural refactor silently changes an API contract the SPA depends on (route, status, `reason` code), so the user sees the wrong message or a refused action looks successful | Medium | High | roadmap M-6 CS-03 and CS-07 (reason codes mirrored field-for-field by the SPA); S-18 and S-19 both `ready`; hot-spot dir `src/app/src/app/features` (345) |
| 7 | An SPA regression (route guard, error display, form state) reaches production because no gate runs the frontend specs or lint | Medium | High | interview Q4; deploy workflow gates on the backend test run only; hot-spot dirs `src/app/src/app/core` (112), `src/app/src/app/shared` (77) |

Considered and left out: a migration that fails on Azure SQL — not raised in
the interview, and already covered by the reversible-migration rule and the
idempotent migration script CI generates. Belongs to deploy verification,
not to this test rollout.

### Risk Response Guidance

| Risk | What would prove protection | Must challenge | Context `/10x-research` must ground | Likely cheapest layer | Anti-pattern to avoid |
|------|-----------------------------|----------------|--------------------------------------|-----------------------|-----------------------|
| #1 | N truly simultaneous booking requests against a class with C free spots (and a karnet with E entries left) admit exactly min(N, C, E); every other request gets the documented refusal, and the persisted counts agree | "A sequential test that fills the class proves concurrency safety" | The concurrency protocol (transaction, concurrency stamps), how the test fixture can issue parallel requests against the real engine, what a losing request sees | integration (real SQL Server, parallel requests) | sequential happy path; expected count computed by calling production code |
| #2 | Boundary cases follow the S-16 rules: first and last validity day in club time, a class near midnight where UTC and club-local dates differ, the last entry, an entry returned after a cancellation | "Entries left is a stored number to assert against" — it is derived from active bookings | The club-local date rule, how entries are derived, which roles may book into which class | integration; unit only for pure date logic | assertion copied from the derivation code; skipping time-zone edges |
| #3 | Every retired capability is refused for every role, and its SPA route is gone or redirects | "Removing the button removed the capability" | The route inventory against the S-16/S-17 retired list; rate-limit configuration and how tests can exercise it | integration (route × role matrix) + SPA guard spec | testing only the UI; a matrix generated from today's routes, which would bless the leftovers |
| #4 | User A is refused user B's resources by id; the reset endpoint answers identically (status, body, timing class) for registered and unknown addresses | "Authenticated means authorized" | Ownership checks per resource; the member/login split from S-14; the reset response shape | integration | testing only the own-resource happy path |
| #5 | Cancelling or changing a class queues exactly one message per channel for each actively booked member with an address or device — addressed to THAT member — and none for anyone else; a failed send is retried on the backoff schedule and, if it still fails, dead-lettered and surfaced by `/health` rather than silently dropped (corrected by Phase 4 research: a permanent failure is dead-lettered on the first attempt by design, so "retried rather than lost" overstated it) | "An outbox row written means the member was notified"; "a `Pending` row observed in the integration host is a stable observation" (Phase 4 research: the test host runs the real delivery worker) | The fan-out rule, including the member/login identity split since S-14 (email from the member record, push only through a login); the existing fake channels; retry semantics; whether the test host runs the delivery worker | integration with fake channels | asserting only on an outbox count (a wrong-but-same-size recipient list passes); mocking the dispatcher itself |
| #6 | Each failure the SPA handles still returns the same status and `reason` from the API, and the SPA maps each known reason to its intended display | "The suite is green after the move, so nothing changed" | The reason-code catalogue on both sides; which endpoints the SPA calls | integration assertions on `reason` + SPA service specs | snapshotting whole response bodies |
| #7 | A failing spec or a lint error blocks the deploy | "44 specs exist, so the SPA is covered" | Whether the specs pass today; how the current workflow is structured | CI gate | switching the gate on while specs are red, then muting them |

## 3. Phased Rollout

Each row is a discrete rollout phase that will open its own change folder
via `/10x-new`. Status moves left-to-right through the values below; the
orchestrator updates Status as artifacts appear on disk.

| # | Phase name | Goal (one line) | Risks covered | Test types | Status | Change folder |
|---|------------|-----------------|---------------|------------|--------|---------------|
| 1 | Booking invariants | Prove class capacity and the karnet entry pool hold under parallel requests and at date/entry boundaries | #1, #2 | integration (+ unit for pure date logic) | complete | context/changes/testing-booking-invariants/ |
| 2 | Access surface | Prove retired doors stay shut and ownership is checked, per route and per role | #3, #4 | integration (route × role matrix) + SPA guard specs | complete | context/changes/testing-access-surface/ |
| 3 | Frontend gate and API contract | Make SPA specs and lint block the deploy, and pin `reason` codes on both sides of the API | #6, #7 | CI gate + integration + SPA specs; post-edit hook + pre-commit (recommended local) | complete | context/changes/testing-frontend-gate-and-contract/ |
| 4 | Class-change notification fan-out | Prove every booked member, and only they, is notified on cancel or change | #5 | integration with fake channels | complete | context/changes/testing-notification-fan-out/ |

Order rationale: Phase 1 carries the top risk and the interview's Q1/Q3.
Phase 2 must exist before S-18 relocates every endpoint. Phase 3 lands before
or alongside S-19. Phase 4 is high-impact; its delivery machinery has been stable
since S-09, but S-14 changed the fan-out's inputs — email comes from the member
record and a member may have no account — without the notification tests growing
those cases (corrected by Phase 4 research).

## 4. Stack

Test profile: **meaningful** — 23 backend integration test files and 44
colocated SPA spec files; one browser-level (e2e) spec plus its seed, local only.

| Layer | Tool | Version | Notes |
|-------|------|---------|-------|
| backend integration | xUnit + `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory<Program>`) | 2.9.3 / 10.0.11 | Boots the real app over HTTP; the default layer for every backend risk |
| database for tests | Testcontainers.MsSql | 4.14.0 | Real SQL Server, so locking and filtered indexes behave as on Azure SQL; never swap for SQLite or in-memory |
| external edges | hand-written fake email/push channels in the test project | n/a | Mock only at the delivery edge |
| SPA unit | Vitest (via `ng test`) + jsdom | 4.x / 28.x | Colocated `*.spec.ts`; not yet run in CI — see Phase 3 |
| SPA lint/format | ESLint + Prettier (`npm run quality:check`) | 10.x / 3.x | Not yet run in CI — see Phase 3 |
| e2e | Playwright (`@playwright/test`, Chromium) | 1.63.0 | Added 2026-09-14 as a deliberately small layer, not a promotion of any risk above: §1 principle #1 still stands, and every risk in §2 keeps its cheaper primary layer. One spec (anonymous visitor on a guarded route → login → dashboard) complements #3's SPA guard specs by proving guard, auth API, cookie and database together. Runs against the shipped shape — the API serving the built SPA from `wwwroot` — never `ng serve`, which has no `/api` proxy. Local only; not in CI. See §6.6 |
| AI-native (local) | post-edit hook (`.claude/hooks/post-edit-spa.sh`) running Prettier, ESLint and the edited file's colocated spec — checked: 2026-09-13 | n/a | Corrected in Phase 3: this row used to promise "the targeted backend tests". Backend tests are deliberately NOT hooked — Testcontainers starts a SQL Server (30-60s), which breaks "per-edit stays fast". The spec half is scoped to `core/` and `shared/`. When NOT to use: as a substitute for the CI gate |

**Stack grounding tools (current session):**
- Docs: Context7 — available; queried for Playwright's `webServer` and setup-project/`storageState` configuration when the e2e layer was added; checked: 2026-09-14
- Search: Exa — available; not used, Context7 covered the one new tool; checked: 2026-09-11
- Runtime/browser: Playwright CLI (`npx playwright test`) — installed in `src/app`, used to run and break-verify the e2e spec; Claude in Chrome — available, not used; Playwright MCP not available in current session; checked: 2026-09-14
- Provider/platform: Linear — available; no quality-gate relevance; GitHub MCP not available in current session; checked: 2026-09-11

## 5. Quality Gates

| Gate | Where | Required? | Catches |
|------|-------|-----------|---------|
| backend build + typecheck | local + CI | required (wired) | compile and nullability drift |
| backend integration tests (`dotnet test`) | local + CI, gates deploy | required (wired) | logic, authorization and concurrency regressions |
| SPA typecheck via production build | CI | required (wired) | template and type drift |
| SPA specs (`npm test`) | local + CI, gates deploy | required (wired) | guard, error-display and form regressions |
| SPA lint + format (`npm run quality:check`) | local + CI, gates deploy | required (wired) | style and lint drift |
| post-edit hook | local (agent loop) | recommended (wired) | SPA format, lint and colocated-spec regressions at edit time |
| pre-commit (lefthook) | local (git) | recommended (wired) | the same format and lint checks on staged SPA files, including edits that never passed through the agent |
| e2e (`npm run e2e` from `src/app`) | local only | optional (not wired to CI) | a guarded route rendering for an anonymous visitor, or a valid sign-in not opening the app, across guard + auth API + cookie + database |

## 6. Cookbook Patterns

How to add new tests in this project. Each sub-section is filled in once
the relevant rollout phase ships; before that, the sub-section reads
"TBD — see §3 Phase <N>."

### 6.1 Adding a concurrency or invariant test for a booking rule

- **Location**: `tests/po-prostu-silka.Tests/`, in the suite that owns the route under test —
  `AdminBookingEndpointTests.cs` for staff booking and the karnet gate, `BookingEndpointTests.cs`
  for capacity and release, `MembershipPassEndpointTests.cs` for pass edits.
- **Pattern**: arrange through the fixture (`IssuePassAsync`, `CreateMemberAsync`), never through
  the API under test; act through HTTP; assert on the **database**, not only on status codes. A race
  uses one client per racer and `Task.WhenAll`, then counts active rows. Where the losing
  interleaving cannot be forced from the API, assert the concurrency-stamp rotation directly instead
  of writing a race that passes either way.
- **Oracle rule**: dates and counts are literals written from the rule ("a 00:30 Warsaw class on the
  15th is covered by a pass valid on the 15th"), with the UTC/Warsaw arithmetic in a comment. Never
  compute an expectation with `ClubTime` or a production count.
- **Slots**: fixed-instant classes use a year and an hour no other suite slides through (other
  suites use 10:00Z/16:00Z every 60 days); one day per test — the overlap rule is club-wide.
- **Reference tests**: `Concurrent_bookings_never_exceed_the_pass_entry_count` (race),
  `A_summer_class_just_after_midnight_is_not_covered_by_a_karnet_for_the_utc_date` (literal-date
  boundary), `Lowering_the_entry_count_rotates_the_pass_stamp` (deterministic stamp).
- **Prove it bites**: break the guarded line in `src/`, see the new test go red, revert.
- **Run locally**: Docker running, then `dotnet test --filter FullyQualifiedName~AdminBookingEndpointTests` from the repo root.

### 6.2 Adding an authorization test for a new or retired endpoint

- **Two layers, and a new route usually needs only the first.** `EndpointAuthorizationTests.cs`
  reads every endpoint's authorization metadata: nothing under `/api` is anonymous except the four
  auth routes, `/api/admin/*` is Admin except a named list of trainer routes, `/api/trainer/*` is
  TrainerOrAdmin. A new route in an existing group is covered automatically. Touch the test only when
  the PRODUCT changes who may reach something — then add the route to the allowlist or the trainer
  list, citing the decision in a comment.
- **HTTP refusal theories** (`EveryAdminRoute` / `EveryRoute`, anonymous 401 · member 403 · trainer
  403) prove the policy actually refuses real users — metadata cannot see a policy redefined to admit
  trainers. Add the route to its suite's theory data when it grants or reveals something (roles,
  codes, passes, blocking). Reference: `MemberAdminEndpointTests.EveryAdminRoute`.
- **Ownership** (a resource that belongs to someone) is never a policy question: test it over HTTP
  with two real users, asserting the other's id is refused. Reference:
  `MyPlanEndpointTests.A_member_cannot_see_another_members_plan`, the trainer tests in
  `AdminBookingEndpointTests`.
- **Retired routes**: assert with the caller who USED to be allowed; writes must be neither a success
  nor a 409; GETs must not be JSON — an unmatched GET falls to the SPA fallback and can answer 200.
  Never pin 405 vs 404.
- **Oracle rule**: allowlists and exceptions come from the PRD or a slice decision and are asserted
  to exist; never derive them from the endpoint data source.
- **Run locally**: Docker running, then `dotnet test --filter FullyQualifiedName~EndpointAuthorizationTests` (no HTTP, milliseconds).

### 6.3 Adding an SPA spec that the deploy gate enforces

- **Location**: colocated with the file under test — `foo.ts` beside `foo.spec.ts`, under
  `src/app/src/app/`. There is no separate test tree.
- **The gate**: `npm run quality:check` and `npm test` run in `.github/workflows/deploy.yml` BEFORE
  `dotnet test`, the publish and the migrations, so a red spec or a lint error aborts the run while
  the previous artifact is still serving.
- **A failure table** is a `Record<Union['reason'], string>` keyed by the union itself, so a new code
  fails the BUILD rather than falling through to the fallback message. Its spec iterates the exported
  `*_FAILURE_REASONS` list (§6.4) — never a hand-copied array with a count assertion, which is a
  second oracle that goes stale silently.
- **What a table's spec proves**: a message for every reason; the fallback for an unknown, `undefined`
  or `null` reason; and that refusals leading to DIFFERENT actions do not read alike.
- **Run one file**: `npx ng test --include src/app/core/<area>/<file>.spec.ts` from `src/app`
  (`--watch` is false outside a TTY). This is exactly what the post-edit hook runs.
- **Reference specs**: `core/admin/membership-pass-failure.spec.ts` (list-driven),
  `core/scheduling/booking-failure.spec.ts`, `core/scheduling/class-failure.spec.ts`.
- **Prove it bites**: delete one entry from the table's `MESSAGES` — the build fails; make it fall
  through instead (cast the `Record` to `Partial`) — the spec fails. Revert both.

### 6.4 Pinning an API failure contract

- **Where**: the suite that owns the ROUTE, never a separate contract suite — `ProfileEndpointTests`
  for profile refusals, `MembershipPassEndpointTests` for karnet ones. Where the arrangement lives
  elsewhere (a karnet refusal needs a class and a booking), bring the arrangement to the route's
  suite rather than the assertion to the arrangement's.
- **What to assert**: the status and the `reason` TOGETHER, over HTTP, through the suite's own
  `private sealed record FailureBody(string Reason)` — each file declares its own by convention.
- **Which codes are worth it**: the ones the SPA maps. Every union under `src/app/src/app/core/**`
  mirrors a `*Failure` record field-for-field and the code is what the screens switch on, so a
  refusal whose status is pinned but whose code is not can be renamed in silence. That was exactly
  the gap in the training-plan bounds: three tests asserting `BadRequest` and no code.
- **Which to skip**: codes reachable only by losing an optimistic race (`conflict`, `member_changed`)
  — §6.1's rule against races that pass either way applies — and `invalid_registration`, which is
  Identity's unrecognised-error fallback and cannot be provoked deterministically.
- **Oracle rule**: the expected code is a literal written from the rule and from the TS union it
  mirrors, never read back from the production constant under test. Name the mirrored file in a
  comment on the test.
- **Both halves, no bridge**: the backend test proves the API still answers the code; the exported
  reason list plus its spec (§6.3) proves the SPA still handles it. Nothing parses TypeScript from
  C#, and nothing should — a generated bridge would fail the moment either side moved.
- **Run locally**: Docker running, then `dotnet test --filter FullyQualifiedName~<Suite>`.

### 6.5 Adding a notification fan-out test

- **Location**: `tests/po-prostu-silka.Tests/ClassCancellationTests.cs` owns both triggers — a cancel,
  and an edit that moves the start time, duration or instructor of a booked, scheduled class. Capacity
  edits, no-op edits and edits to a cancelled class are silent by rule and already pinned.
- **Identity, never counts.** Assert WHO each row is addressed to: the email `Recipient` is the member's
  address, the push `Recipient` is a subscription id. Take the expected values from the arrangement — the
  address the test typed, the `PushSubscription` rows it inserted (`DevicesOfAsync`) — never from
  `BookingQuery`, `ClassChangeNotification` or the subscription store. A count of "three emails" passes for
  any wrong list of the right size.
- **The member shapes to cover.** Since S-14 email comes from the member RECORD and push only through a
  login. An account with devices, an account without, a member with no account but an address (email only),
  a member with neither (skipped — book a reachable member alongside as a control), and a claimed member
  whose login differs from the record (told at the record's address). Book accountless members through
  `BookMemberAsync`; `BookAsync` needs a signed-in client.
- **Finding your rows**: every class type carries a GUID in its name and the subject carries the name, so
  `MessagesAboutAsync(type.Name)` returns exactly this test's rows in the shared table.
- **Rows stay as written.** The integration host does NOT run `OutboxDeliveryWorker` (removed in
  `TestAppFactory`), so a `Pending` row is a stable observation. If a test ever needs delivery, it builds
  its own worker.
- **Proving delivery** (the only way to challenge "a row written means the member was notified"): cancel
  through the API, then build a standalone `OutboxDeliveryWorker` over the test database with
  `FakeEmailSender`/`FakePushSender`, as `OutboxDeliveryTests.InitializeAsync` does, and call `RunPassAsync`.
  Two settings are load-bearing, and each one gets wrong silently: a clock AT OR AFTER real time (the host
  stamped the rows with real `NextAttemptAt`, so an old test clock claims nothing), and a batch large
  enough to drain rows other tests left `Pending`. Filter the channels' `Sent` lists by the subject, and
  assert this test's rows ended `Sent` in the database so an unclaimed row fails instead of passing.
  Reference: `A_cancellation_is_delivered_to_exactly_the_members_owed_it`.
- **Prove it bites**: read the account's email instead of the member's in `BookingQuery.GetForClassAsync`;
  remove the blank-email guard in `ClassChangeNotification.FanOutAsync`; enqueue push rows with the
  endpoint instead of the subscription id. Each should turn a named test red.
- **Retry, backoff and dead-lettering** belong to `OutboxDeliveryTests` with synthetic rows — do not
  re-test them through the fan-out.
- **Run locally**: Docker running, then `dotnet test --filter FullyQualifiedName~ClassCancellationTests`.

### 6.6 Adding a browser-level (e2e) spec

- **First ask whether it belongs here.** A risk earns an e2e spec only when it crosses several
  boundaries (routing, auth, API, database) or exists only in the rendered UI. If an integration test
  or an SPA spec can prove it, write that instead — §1 principle #1.
- **Location**: `src/app/e2e/`, one test per file, named after the scenario. Model it on
  `e2e/seed.spec.ts`; the rules are in `e2e/CLAUDE.md` (role-based locators, wait for state never
  time, unique `Date.now()`-suffixed data, cleanup in the same test).
- **Auth**: specs start signed in as the seeded admin through the `setup` project, which logs in via
  `POST /api/auth/login` and saves `playwright/.auth/admin.json` (gitignored). Only a spec whose risk
  IS the login form opts out with `test.use({ storageState: { cookies: [], origins: [] } })`.
- **The app under test**: `playwright.config.ts` runs `npm run e2e:stage` (build the SPA, copy it into
  `src/Api/wwwroot`) then `dotnet run --project ../Api/po-prostu-silka.Api.csproj` on
  http://localhost:5264, and waits for `/health`. Locally it
  reuses a server already listening there — which then serves whatever bundle is in `wwwroot`.
- **Beware the second line of defence.** `authInterceptor` sends any 401 to `/login`, so "the visitor
  ended up on `/login`" passes even with every route guard removed. The reference spec asserts instead
  that no member-data request (`/api/*` outside `/api/auth/*`) was sent before the login screen showed.
- **Reference spec**: `e2e/guarded-route-redirects-to-login.spec.ts`.
- **Prove it bites**: make `authGuard` and `activeMemberGuard` return `true` — the spec fails on the
  `/api/bookings/mine` request. Revert, and re-run the suite so `wwwroot` is rebuilt from clean code.
- **Run locally**: Docker running and migrations applied, then from `src/app`
  `npx playwright test e2e/<file>.spec.ts` (one spec) or `npm run e2e` (all).

### 6.7 Per-rollout-phase notes

(Appended by each rollout phase as it lands.)

- **Phase 1 (booking invariants):** the headline races were already covered; the gaps were a
  club-local date boundary no daytime test could reach, boundary tests computing their expectation
  with `ClubTime`, and two unasserted pass-stamp rotations. Entries consumed by a club-cancelled class
  were left unpinned and raised as roadmap Open Question 7.
- **Phase 2 (access surface):** the scenario tests already existed; the gap was that nothing proved
  EVERY endpoint authorized, with no fallback policy and S-18 about to re-create each route group.
  Trainers seeing member emails on their class roster was left unpinned as roadmap Open Question 8.
- **Phase 3 (frontend gate and API contract):** the specs were verified green BEFORE the gate was
  switched on — the order §2 requires. The contract turned out to be nearly covered already: of the
  ~67 codes only seven were unasserted (`has_active_bookings`, `invalid_street`,
  `invalid_house_number`, `invalid_weeks`, `invalid_weight`, `reps_too_long`, `note_too_long`), and
  three of those had tests pinning the STATUS but not the code, so they were extended rather than
  duplicated. Left unpinned on purpose: race-only codes and `invalid_registration`.
  `pending_approval` survives in the SPA's `LoginFailureReason` with no producer since S-01; both
  sides already document it as unreachable, so it was left alone rather than raised as a question.
  §4 and §5's description of the post-edit hook was CORRECTED here — it runs the SPA checks, not the
  backend tests, because a Testcontainers SQL Server per edit breaks the per-edit budget.
- **Phase 4 (notification fan-out):** both triggers were already tested, but only by COUNT — a recipient
  list of the right size with the wrong people passed. S-14's identity split (email from the member
  record, push only through a login, members with no account) had no fan-out test at all. The larger
  finding was the test host itself: it ran the real `OutboxDeliveryWorker` with unconfigured senders, so
  every fan-out row was dead-lettered within 15 seconds, `Pending` assertions held only on timing, and no
  test could prove a written row reached a channel. The worker is now removed from the host, and one test
  delivers a real cancellation through a standalone worker over fake channels. Pinned as current behaviour
  by decision: a claimed member is told at the record's address, and moving a class then moving it back
  sends two rounds. The response guidance for risk #5 was corrected in §2 — a permanent failure is
  dead-lettered on the first attempt by design, so "retried rather than lost" overstated it.

## 7. What We Deliberately Don't Test

- **Calendar week-view appearance and UI snapshot tests** — they change
  often and catch nothing. Re-evaluate if a calendar layout bug reaches
  members twice. (Source: Phase 2 interview Q5.)

## 8. Freshness Ledger

- Strategy (§1–§5) last reviewed: 2026-09-11
- Stack versions last verified: 2026-09-14 (e2e row added)
- AI-native tool references last verified: 2026-09-14

Refresh (`/10x-test-plan --refresh`) when:

- a new top-3 risk surfaces from the roadmap or archive,
- a recommended tool's `checked:` date is older than three months,
- the project's tech stack changes (new framework, new test runner),
- §7 negative-space no longer matches what the team believes.
