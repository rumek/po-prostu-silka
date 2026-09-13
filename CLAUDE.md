<!-- Project-specific rules. Kept OUTSIDE the 10x-cli managed block above so a CLI
     update does not clobber them. -->

## Project rules (po-prostu-silka)

Full contributor guidance lives in @AGENTS.md — read it before touching `src/`. The rules
below are the ones most easily broken by accident.

- **Layering.** `src/` is one project with three folders: `Domain` (references nothing) →
  `Application` (references Domain) → `Infrastructure` (references both). **Only
  `Infrastructure` may reference EF Core.** Nothing enforces this but convention.
- **EF Core lives in `src/Infrastructure/Persistence/`** — DbContext, entity
  configurations, and migrations. Entity config goes in `IEntityTypeConfiguration<T>`
  classes under `Configurations/`; they are auto-discovered, so don't grow
  `OnModelCreating`.
- **`AppDbContextFactory` is deliberate, not a bug.** Its placeholder connection string is
  never used to connect — it exists so `dotnet ef` works without runtime config. Commands
  that connect get a real string via `--connection`.
- **Migrations must have a working `Down`.** Rollback redeploys the previous artifact but
  does not roll back schema; destructive changes lag one release.
- **`nuget.config` pins nuget.org only.** Keep the `<clear />` — this machine has private
  feeds the CI runner cannot reach.
- **Local DB:** `docker compose up -d` (real SQL Server, not SQLite — locking semantics
  must match Azure SQL). Check connectivity with `GET /health`.

<!-- BEGIN @przeprogramowani/10x-cli -->

## 10xDevs AI Toolkit - Module 3, Lesson 4 (E2E Tests)

**For E2E tests, use the `/10x-e2e` skill.** It is the single source of truth
for the workflow — risk → seed test + rules → generate → review against the five
anti-patterns → re-prompt → verify. The skill's `references/` carry the full
rules, anti-patterns, seed pattern, and prompt-template.

A few hard rules that hold even before you invoke the skill:

- **Locators:** `getByRole` / `getByLabel` / `getByText` first; `getByTestId`
  only when accessibility attributes are ambiguous. Never CSS selectors, XPath,
  or DOM structure.
- **Never `page.waitForTimeout()`.** Wait for state: `toBeVisible()`,
  `waitForURL()`, `waitForResponse()`.
- **Test independence + cleanup.** Each test runs standalone — its own setup,
  action, assertion, and cleanup; unique ids (timestamp suffix) so parallel runs
  and re-runs don't collide.

Two boundaries to keep straight:

- **DOM (snapshot) is the default.** Vision (`--caps=vision`) is a supplement for
  visual-only risks (layout, z-index, animation); for pixel regression prefer
  deterministic tools (`toMatchSnapshot`, Argos, Lost Pixel). VLM model
  selection/cost is a debugging topic (Lesson 5), not testing.
- **Healer helps on selectors, harms on logic.** A changed selector → healer
  re-finds it (route through PR review). A changed business behavior → healer
  masks the bug; that failing-test-to-fix case is Lesson 5.

<!-- END @przeprogramowani/10x-cli -->
