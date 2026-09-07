<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Member and Admin Dashboards with a Mobile Bottom Bar

- **Plan**: `context/changes/member-and-admin-dashboards/plan.md`
- **Scope**: Full plan — Phases 1–4 (all complete), plus the post-phase icon-only bar change
- **Date**: 2026-09-07
- **Verdict**: REJECTED
- **Findings**: 1 critical, 2 warnings, 4 observations
- **Commits reviewed**: `a361ead`, `79a383f`, `bfcaddf`, `85e5309`, `4efd20c`, `704d449`, `72be064` (43 files, +2869/-99)

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | FAIL |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | WARNING |

**Overall: REJECTED** — one CRITICAL defect in Safety & Quality. The fix is a single line; the
severity reflects the consequence (modals stop being modal on a phone), not the size of the change.

Two independent sub-agents reviewed the diff and converged on F1 without prompting each other.

## Findings

### F1 — Bottom bar paints over the modal overlays it is supposed to sit under

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `src/app/src/app/shared/bottom-nav/bottom-nav.scss:7,18`
- **Detail**: All four elements declare the same stacking level:
  - `bottom-nav.scss:7` — `$bottom-nav-z: 10`
  - `features/schedule/class-details-overlay/class-details-overlay.scss:9` — `z-index: 10`
  - `features/admin/classes/class-create-overlay.scss:6` — `z-index: 10`
  - `features/admin/classes/class-bookings-overlay.scss:9` — `z-index: 10`

  Equal `z-index` inside one stacking context is broken by document order. `App`'s `:host`
  (`app.scss:4-8`, `display: flex`) and `.shell-main` (a flex item with no `z-index`) create no
  intervening stacking context, so all four resolve at the root. `<app-bottom-nav>` is rendered
  **after** `</main>` (`app.html:69-71`) while the overlays render inside it — so the bar wins the
  tie and paints **on top of** the modal.

  Consequence on a phone (≤30rem, the only width the bar is visible): the bottom ~53px of an
  `aria-modal="true"` overlay is covered by the bar, and the bar's tabs remain hit-testable — a user
  inside a modal can navigate away by tapping what looks like part of it.

  This contradicts the plan's explicit contract ("A modal must cover the bar, so the bar's `z-index`
  goes **below** theirs" — `plan.md`, Critical Implementation Details) and the commit message of
  `85e5309`. The code comment at `bottom-nav.scss:3-6` asserts the value is "deliberately low so the
  overlays keep winning without having to declare anything" — factually wrong: the overlays do
  declare `z-index: 10`. The comment is the root of the error, not a bystander to it.
- **Fix**: Lower the bar below the overlays — `$bottom-nav-z: 5` — and rewrite the comment to state
  the real relationship (overlays sit at 10; the bar must stay under them).
  - Strength: One line; makes the stated invariant true rather than accidentally-true, and starts the
    shared z-index scale the comment already claims to start.
  - Tradeoff: None material. Raising the three overlays to 20 instead would touch three files for the
    same effect.
  - Confidence: HIGH — the four values were read directly and the stacking-context analysis was
    verified independently against `app.scss` and `app.html`.
  - Blind spot: Not visually re-confirmed on a device after the fix; manual check 4.9 should be redone.
- **Decision**: FIXED

### F2 — Dashboard loaders omit the generation fence the codebase uses for this shape of screen

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/app/src/app/features/dashboard/dashboard.ts:101-170`
- **Detail**: None of the four loaders (`loadBookings`, `loadPlan`, `loadPending`, `loadClasses`)
  check a generation counter after their `await`. `my-classes.ts:42-46` carries the fence with a
  docblock justifying it for exactly this shape: *"There is no navigation here, but a reload racing a
  first load is still two responses that can land in either order."* `schedule.ts:75-114` does the
  same. The dashboard has five retry buttons, none disabled while its request is in flight.

  **Reachability was checked and is marginal.** The project is zoneless (no `zone.js` dependency, no
  `polyfills` entry in `angular.json`), and each loader sets its `loading` signal synchronously, so
  the retry button leaves the DOM before the next paint — a second physical tap cannot find it. The
  interleaving is therefore latent rather than currently reachable. It is reported because the
  divergence from an established, deliberately-documented pattern is the kind of thing that becomes a
  live bug the moment someone adds a non-retry refresh path.
- **Fix**: Add the `private generation = 0` fence from `my-classes.ts:42-46` to each of the four
  loaders.
- **Decision**: FIXED

### F3 — Two sibling `<h2>` headings in the dashboard's plan card

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (accessibility)
- **Location**: `src/app/src/app/features/dashboard/dashboard.html:44` and `:56-61`
- **Detail**: The card renders `<h2>Twój plan treningowy</h2>` and then, when a plan is loaded,
  `<app-plan-summary headingLevel="h2">` emits a second `<h2>` for the plan's name. A screen-reader
  heading list shows two adjacent same-level headings — a label and its content read as two unrelated
  topics. The admin section one card over nests correctly (`h2` section title → `h3` card titles,
  `dashboard.html:75,78,98,128`), so this is also internally inconsistent.

  `PlanSummary.headingLevel` deliberately offers only `'h1' | 'h2'` (`plan-summary.ts:31`) to prevent
  outline bugs; that reasoning did not anticipate a caller that already supplies its own card heading.
- **Fix**: Drop the static `<h2>Twój plan treningowy</h2>` when the card has a plan, letting the
  plan's own name be the card's heading — the same thing `/my-plan` does. Keep the static heading for
  the loading, error and no-plan states, which have no name to show.
- **Decision**: FIXED

### F4 — Stale arithmetic comment in `.shell-main`'s phone padding

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/app/src/app/app.scss:84-90`
- **Detail**: The padding is `calc(48px + var(--space-5) + env(safe-area-inset-bottom))` = 72px +
  inset, with a comment saying "Keep the 48px in step with `.bottom-nav-tab`'s min-height". That
  min-height became **52px** in `704d449` (icon-only bar) and the literal here was not updated. The
  bar actually occupies 53px (52px tab + 1px `border-top`) + inset, so 72px still clears it — but only
  thanks to the incidental 24px `--space-5` buffer, not because the two numbers are in step as the
  comment claims. No visible defect today; the asserted invariant is simply false.
- **Fix**: Source both from one custom property so they cannot drift again, or update the literal to
  `52px` and correct the comment.
- **Decision**: FIXED

### F5 — Adaptation note in the plan names the wrong folder

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `context/changes/member-and-admin-dashboards/plan.md` — "Adapted during
  implementation" note on the bottom-nav contract
- **Detail**: The note says "A new `shared/icon/` primitive". The actual folder is **`shared/icons/`**
  (plural), and the plural is load-bearing: `.gitignore:443` carries the stock macOS `Icon` entry,
  which matches any path segment named `icon` case-insensitively and silently untracks it. Everything
  else in both adaptation notes was verified accurate, including the bundle figures.
- **Fix**: Correct the path to `shared/icons/` and note why the plural matters.
- **Decision**: FIXED

### F6 — Initial bundle is 0.54 kB under the warning budget

- **Severity**: 📝 OBSERVATION
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Success Criteria
- **Location**: `src/app/angular.json:42-53`
- **Detail**: The build passes at **499.46 kB** against a 500 kB warning. Every phase criterion about
  the budget is legitimately green, but the margin is now smaller than a single small component. The
  next change touching the shell or the eager `/` route trips the warning immediately. This is
  recorded in `plan-brief.md` under Open Risks, and is out of scope for this slice — but it is the
  first thing the next change will hit, and the user's stated follow-up ("more icons across the
  system") points straight at it.
- **Fix**: Decide deliberately rather than discovering it mid-change — either raise the budget (500 kB
  was an estimate, not a measured constraint) or make the dashboard lazy (the plan argued against
  this: it delays first paint for every member on the one route everyone loads).
- **Decision**: FIXED

### F7 — One-pixel window where both navigation surfaces render

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/app/src/app/app.scss:84` and `src/app/src/app/shared/bottom-nav/bottom-nav.scss:27`
- **Detail**: The header's nav hides at `max-width: 30rem` (≤480px); the bar hides at
  `min-width: 30.0625rem` (≥481px). Between them — a fractional width such as 480.5px, reachable
  through browser zoom or a fractional device pixel ratio — neither rule applies and both navigation
  surfaces render at once. Harmless and effectively unreachable on real hardware, but the two
  breakpoints are meant to be complementary and are not.
- **Fix**: Drive both from the same value, e.g. hide the bar at `min-width: 30.0001rem`, or invert the
  header rule to `min-width` so the pair partitions the axis exactly.
- **Decision**: FIXED

## What passed

Verified clean, with evidence:

- **Every `/more` role condition matches its guard literally** — `more.ts:41-47` against
  `admin.guard.ts:29` and `trainer.guard.ts:30`. This was the highest-risk check in the review (the
  S-01 F5 regression class) and it holds, with `more.spec.ts` asserting the inactive-admin and
  inactive-trainer cases explicitly.
- **The admin "today" window** starts at local midnight, issues one request, and buckets client-side
  (`dashboard.ts:152-191`). Month-rollover and DST were checked: the code uses `setHours`/`setDate`
  on local wall-clock time with no raw millisecond arithmetic, which is the trap it avoids. Poland's
  DST transition is at 02:00–03:00, never midnight.
- **No `date-fns` anywhere in the eager chain** — dashboard, all four shared components, and the shell.
- **`class-summary` takes primitives**, so both `MyBooking` and `ScheduledClass` feed it.
- **The bar is role-blind by construction** — injects no `AuthService`, branches on no role.
- **Accessibility of the icon-only bar**: every tab has an `aria-label`, the SVG is `aria-hidden`,
  `aria-current` nulls out rather than setting `"false"`, and Start is exact-matched.
- **Scope discipline**: zero backend diff; no new endpoint; dashboard is read-only; `.link-button` not
  promoted; SSR untouched; no notification centre. No file outside the plan's list was touched.
- **Automated criteria re-run on the final tree**: 400 tests in 42 files pass, `quality:check` clean,
  build within budget, largest component stylesheet 2.4 kB against a 6 kB warning.

## Success criteria note

Manual check **4.9** ("Opening a class-details overlay covers the bar rather than sitting under it")
is marked `[x] — 85e5309`, but F1 shows the code cannot guarantee it. The check either passed by
accident of browser behaviour or was not exercised at a phone width with an overlay open. It should be
re-run after F1 is fixed. This is why Success Criteria is WARNING rather than PASS.

## Triage outcome — 2026-09-07

All seven findings were triaged and fixed in one pass.

| ID | Decision | What changed |
| --- | --- | --- |
| F1 | FIXED | `$bottom-nav-z: 10` → `5`, and the comment rewritten to state the real relationship instead of the assumed one. |
| F2 | FIXED | A per-card generation fence on all four dashboard loaders, matching `my-classes.ts`. Per-card, not per-screen: a shared counter would let a retry on one card discard another card's in-flight response. |
| F3 | FIXED | The static plan-card heading now renders only in the loading, error and no-plan states; a loaded card's single `h2` is the plan's own name. Two specs added to hold the outline. |
| F4 | FIXED | New `--bottom-nav-tab-height` token in `styles.scss`, consumed by both `bottom-nav.scss` and `.shell-main`'s padding, which now also accounts for the bar's 1px border. |
| F5 | FIXED | Plan note corrected to `shared/icons/`, with the `.gitignore` reason recorded. |
| F6 | FIXED | Initial-bundle warning raised 500 kB → 550 kB. Rationale recorded in `AGENTS.md` (JSON takes no comments), together with the Node 22+ requirement. |
| F7 | FIXED | Bar breakpoint `30.0625rem` → `30.0001rem`, so the two nav breakpoints partition the axis exactly. |

**Verification after triage**: 402 tests in 42 files pass (2 added for F3), `quality:check` clean,
build at **500.28 kB**.

That build figure is worth recording: the triage fixes alone carried the bundle *past* the old 500 kB
warning. F6 was not a hypothetical — the margin was consumed by the next change, and that change was
this one.

**Closed 2026-09-07.** Manual check 4.9 ("opening a class-details overlay covers the bar") was
re-run on a phone width against the post-F1 code and confirmed by the user. The overlay now covers the
bar, so the check is honest against the code it is recorded against. Nothing from this review remains
open.
