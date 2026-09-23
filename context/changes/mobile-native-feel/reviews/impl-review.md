<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Mobile native feel

- **Plan**: context/changes/mobile-native-feel/plan.md
- **Scope**: Phases 1–2 of 2 (full plan; manual checks pending)
- **Date**: 2026-09-23
- **Verdict**: REJECTED
- **Findings**: 1 critical, 3 warnings, 5 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | FAIL |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

Automated checks re-run on 2026-09-23: `npm test` 852/852, `quality:check` clean, `npm run build`
534.06 kB, `dotnet build` 0 errors. Both E2E specs passed locally on e4cfd89. Manual rows 1.8–1.12 and
2.6–2.10 are unchecked. Staging runs checked at 390px on the admin account:
- tab history;
- the back arrow, including a deep link;
- back closing the overlay on /schedule;
- the pinned bar and its hairline;
- the desktop header.

The member and trainer paths, the motion, and the installed Android PWA are still pending.

## Findings

### F1 — Swapping the actions overlay for the bookings overlay closes the bookings overlay at once

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/app/src/app/shared/forms/overlay-history.ts:47-55; features/admin/classes/classes.html:44-72; classes.ts (`openBookings` → `closeTransient()` then `viewingBookings.set(row)`)
- **Detail**: This happens in a single change-detection pass:
  1. The actions overlay is destroyed. Its entry is still on top, so it calls `history.back()`, which is asynchronous.
  2. The bookings overlay is created. It `pushState`s and registers its `popstate` listener.
  3. The queued back lands. The state is not the bookings overlay's own token, so `onBack()` closes it.

  Confirmed on Staging at desk width: `/admin/classes` → tile "TRX" → "Zapisani" leaves no dialog open 200 ms later. This is a regression in the admin's desk flow. The same race hits any overlay opened in the same tick another one closes. The specs only cover a single overlay.
- **Fix A ⭐ Recommended**: Coordinate pops at module level in overlay-history.ts.
  - A destroy that calls `history.back()` increments a `pendingPops` counter.
  - One shared `popstate` listener decrements it and swallows that pop.
  - An overlay opened while pops are pending defers its `pushState` and listener registration until they land.
  - Add a spec that closes one overlay and opens another in the same tick.
  - Strength: Fixes the race for every overlay pair at the source, and the three overlays keep their one-line call.
  - Tradeoff: Module-level mutable state and a short deferral. A back press during that window is ignored.
  - Confidence: MED — the ordering is clear from the code, but the timing of `pushState` during a pending traversal differs between browsers, so it needs a real-browser check (Staging repro).
  - Blind spot: Two pops queued back to back, e.g. a double Escape.
- **Fix B**: Let the swapping screen handle it. `openBookings` replaces the actions overlay's history entry instead of closing and reopening.
  - Strength: Local to one screen and easy to reason about.
  - Tradeoff: Each future swap site must remember to do it, and the general race stays.
  - Confidence: MED — the only known swap site today is `classes.ts`.
  - Blind spot: `select()` while the create overlay is open is another swap path.
- **Decision**: FIXED via Fix A — module-level `pendingPops` in overlay-history.ts; swap spec added (red before, green after)

### F2 — Up matches entries by navigationId, which restarts on every page load

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/app/src/app/core/layout/up.ts:87-97
- **Detail**:
  - After a reload, a popstate into an entry from before the reload carries an old id. That id can equal an entry of the current load, so the model moves to the wrong index instead of resetting.
  - `stepsBackTo` can then return more steps than really exist, and `historyGo(-n)` can land on the wrong screen or leave the app. That contradicts the class doc ("never pops to somewhere unknown").
  - Related: a popstate that ends in a guard redirect overwrites the model at the old index (it self-heals on the next popstate).
- **Fix**: Tag entries with a per-load nonce. After each `NavigationEnd`, `replaceState({...history.state, upLoad: LOAD_ID})`, and reset the model when a restored state lacks the current `LOAD_ID`. Add an up.spec case: reload, push, back, back.
  - Strength: Makes "unknown entry → reset" hold across reloads, which is the model's safety property.
  - Tradeoff: Writes one extra key into state the router owns. The router ignores and copies unknown keys (verified: `navigateToSyncWithBrowser` copies extra state into `extras.state`).
  - Confidence: MED — needs a check that the router's own `replaceState` on popstate keeps the key.
  - Blind spot: SpyLocation behaviour in the unit spec versus real History.
- **Decision**: FIXED — `LOAD_ID` tag written into each recorded entry's state; a restored state without it resets the model; up.spec reload case added (red before, green after)

### F3 — Leftover same-URL overlay entries make historyGo(-n) undercount

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/app/src/app/core/layout/up.ts:28-29; shared/forms/overlay-history.ts
- **Detail**:
  - An overlay entry is left in history in three cases: a navigation starts from inside the overlay ("Edytuj" in class-actions-overlay), a navigation from outside tears the overlay down (an auth redirect), or an overlay is swapped (F1).
  - `Up` cannot see these entries, so a `to()` that crosses one lands one entry short.
  - Exposure today is desk-only, where neither the app bar arrow nor the bottom bar exists. The `Up` doc comment says these entries are "invisible", which is only true when they have been popped.
- **Fix**: State the limit precisely in the `Up` doc comment and in plan.md's Critical Details: a leftover entry from a navigation out of an overlay undercounts `to()`, and why that is desk-only today. Revisit if a phone overlay ever navigates.
- **Decision**: FIXED — known limit stated in the `Up` doc comment (which also now names overlay-history.ts) and in plan.md Critical Details

### F4 — `transition.finished.finally(...)` can surface an unhandled rejection

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/app/src/app/core/layout/view-transitions.ts (last line of `slideDirection`)
- **Detail**:
  - `finally` returns a derived promise that rejects whenever `finished` rejects, which happens when the DOM update callback fails.
  - Angular's own `.catch` on `finished` does not cover the derived promise, so it reaches `provideBrowserGlobalErrorListeners` as an uncaught rejection.
  - The comment also has it backwards: a skipped transition resolves `finished` and rejects `ready`.
  - Pre-existing since S-26, but this change rewrote the function.
- **Fix**: `const clear = () => delete root.dataset['navDirection']; void transition.finished.then(clear, clear);` and correct the comment.
- **Decision**: FIXED — `finished.then(clear, clear)`; comment corrected

### F5 — AGENTS.md's "never a plain routerLink back to a parent" contradicts the admin screens

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: AGENTS.md, "Screen identity", "History behaves like Android's"
- **Detail**:
  - The admin "Wróć do listy" and "Anuluj" links still use `routerLink` to their parent: class-form, class-type-form, exercise-detail, exercise-form, member-form, member-passes, and plan-builder's "Anuluj".
  - The plan scoped them out on purpose (desk persona), but the rule reads as already broken, or invites a blind "fix".
- **Fix**: Qualify the rule: it applies to member and trainer screens reached on a phone; the admin desk screens' back and "Anuluj" links are deliberately still `routerLink` (plan: What We're NOT Doing).
- **Decision**: FIXED — rule qualified in AGENTS.md with the admin desk exception

### F6 — A query-only navigation drops a data-dependent title override

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/app/src/app/core/layout/screen-title.ts (`resolve` / `ScreenTitleStrategy.updateTitle`)
- **Detail**:
  - `updateTitle` runs on query-only navigations too, and `resolve` clears the override.
  - A `useScreenTitle` effect does not re-run while its data is unchanged, so the bar would fall back to the route title.
  - No current victim: none of the four data-titled screens navigates by query.
- **Fix**: Keep the override in the strategy when the leaf's `routeConfig` and params are unchanged. Add a screen-title.spec case.
- **Decision**: FIXED — the strategy passes `sameScreen` (same routeConfig and params) and `resolve` keeps the override; two screen-title.spec cases added

### F7 — E2E overlay spec branches on a non-waiting check and on Node's timezone

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/app/e2e/back-closes-open-overlay.spec.ts:96-99
- **Detail**:
  - `if (!(await day.isVisible()))` is conditional flow on a non-waiting check, which runs against e2e/CLAUDE.md's "wait for state".
  - The day label is computed in the Node process's timezone, while the app renders in the browser's.
  - It only runs after about 21:00 local time.
- **Fix**: Pin `timezoneId: 'Europe/Warsaw'` and `locale: 'pl-PL'` in `test.use`. Replace the branch by computing whether the target day is in the next ISO week and clicking "Następny tydzień" deterministically.
- **Decision**: FIXED — the browser timezone is pinned to the Node process's own and the locale to pl-PL; the week step is decided from `getDay() === 0` instead of `isVisible()`. NOT re-run: Docker Desktop was down at triage time, so both E2E specs still need a local run

### F8 — Unplanned: the phone's top padding on `.shell-main` shrank from --space-6 to --space-5

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: src/app/src/app/app.scss (`@include bp.narrow { .shell-main { padding … } }`)
- **Detail**: This is a benign spacing adjustment under the new pinned bar, but no plan section describes it.
- **Fix**: Add one line to Phase 1 §4's adaptation notes.
- **Decision**: FIXED — adaptation note added to Phase 1 §4

### F9 — Manual verification still open

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: plan.md Progress 1.8–1.12, 2.6–2.10
- **Detail**:
  - Nothing is rubber-stamped, and all manual rows are unchecked.
  - Staging covered the admin-persona part of 1.10, 1.11, 2.9 and 2.10.
  - Still pending: the member and trainer accounts, the motion (the browser tab was backgrounded), and the installed Android PWA.
- **Fix**: Run the remaining manual rows on a phone after F1 is fixed, and re-check the admin desk "Zapisani" flow as part of 2.10.
- **Decision**: FIXED (in part) — review fixes pushed in d1c1a60; the admin desk "Zapisani" flow was re-checked on Staging (main-FMKLNRVT.js): the bookings overlay stays open, and back closes it. The phone rows (member/trainer, motion, installed Android PWA) stay with the user
