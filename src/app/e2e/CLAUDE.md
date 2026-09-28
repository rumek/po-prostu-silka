# E2E Testing Rules

Playwright specs for the running app (API + built SPA on http://localhost:5264). Model every new
spec on `seed.spec.ts`. Run one spec with `npx playwright test e2e/<file>.spec.ts`; all with
`npm run e2e`. Prerequisites: `docker compose up -d` and migrations applied.

- Use getByRole, getByLabel, getByText as primary locators.
  Fall back to getByTestId only when accessibility attributes are ambiguous.
- Never use CSS selectors, XPath, or DOM structure for locating elements.
- Each test must be independently runnable — no shared state between tests.
- Never use page.waitForTimeout(). Wait for specific conditions:
  toBeVisible(), waitForURL(), waitForResponse().
- Assert the business outcome, not implementation details.
- Use unique identifiers (e.g., timestamp suffix) for test data
  to avoid collisions in parallel runs. Clean up in afterEach.
- Use storageState for authentication — never log in through UI
  in individual tests. The only exception is the spec whose risk IS the login form
  (`guarded-route-redirects-to-login.spec.ts`), which opts out with an empty storageState.
- Name each test after the risk it protects, and put a provenance header (risk + seed) at the top.
- One test per file. E2E only for risks that cross several boundaries (auth, routing, API, DB) or
  exist only in the rendered UI — see context/foundation/test-plan.md before adding one.
- Copy is Polish: locate by the Polish accessible names the user actually sees.

## Arranging data (support/)

- Import `test` and `expect` from `./support/fixtures`, not from `@playwright/test`. It adds
  `adminApi` (the admin's session as an API context) and `club` (the builders).
- Arrange through `club`'s builders (`support/club.ts`) — never production code, never ad-hoc
  `page.request` calls in a spec. Each builder registers its own removal at creation, and the fixture
  runs removals in reverse order after the test, pass or fail. So arrange a karnet BEFORE the class
  its booking goes into: the class's removal must run first. A class nobody was booked on is
  deleted; a booked one is history the API will not delete, so it is CANCELLED (which frees its
  slot), and a karnet that paid for a booking stays behind on its E2E member.
- Classes only through `club.createClass`, which takes a random free slot (`support/slots.ts`) and
  retries on `time_conflict`. Never a fixed time: the overlap rule is club-wide. `showWeekOf` brings the
  slot's week into view.
- Register accounts only through the support layer (`club.registerMember`, `anonymousContext`). They
  send a unique `X-Forwarded-For`, so the registration rate limit — not under test here — never
  answers 429 on a re-run.
- Other personas get their own context: `anonymousContext(browser)` or
  `signedInContext(browser, credentials)` (`support/sessions.ts`). Both start from an empty session,
  because `browser.newContext()` otherwise inherits the admin's.
- Classes are instructed by the one E2E trainer (`trainerCredentials`), which `trainer.setup.ts`
  get-or-creates before any spec runs.
- Members and accounts cannot be deleted, so they stay behind: name every member
  `E2E <purpose> <Date.now()>` and give every address `@example.test`.
- E2E runs against the LOCAL database only — never staging, never in the deploy pipeline — because
  of what it leaves behind. The `pre-push` hook (`.githooks/pre-push`) runs the suite before a push;
  `git push --no-verify` skips it.
