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
