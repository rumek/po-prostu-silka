<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: A failure is told one way, and the copied patterns are extracted once (S-19)

- **Plan**: `context/changes/frontend-error-and-patterns/plan.md`
- **Scope**: Full plan — Phases 1-7 of 7
- **Date**: 2026-09-20
- **Verdict**: NEEDS ATTENTION → **triaged 2026-09-20**; 4 fixed, 1 partially fixed, 2 skipped
- **Findings**: 0 critical, 3 warnings, 4 observations

> **Post-triage state.** F2, F3, F4 and F5 are fixed; F1 is partially fixed (the two bundle items
> ticked, 17 manual items still pending a browser session); F6 and F7 were accepted as-is with
> reasons recorded. The only dimension still short of PASS is **Success Criteria**, and only
> because manual verification has not been walked. Suite after fixes: **634 tests, 54 files, all
> green**; `quality:check` clean; initial bundle 512.42 kB.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | WARNING |

## Automated verification (run during this review)

| Command | Result |
|---|---|
| `npm test` (from `src/app/`) | PASS — 51 files, 619 tests |
| `npm run quality:check` (from `src/app/`) | PASS — Prettier + ESLint clean |
| `npm run build` (from `src/app/`) | PASS — initial total **512.42 kB** (warning threshold 550 kB, no budget warning emitted) |
| `dotnet test po-prostu-silka.slnx` (repo root) | PASS — 576 tests, 0 failures |

Drift audit found **no contract violations** across all seven phases: the `FailureKind` union is
exactly as specified (409 correctly classified `business`, not a kind of its own), `Object.hasOwn`
is used rather than `in` (pinned per-union against `constructor`/`toString`/`__proto__`), all 17
unions have factory-built tables, `failure-contract.spec.ts` asserts `toHaveLength(17)` plus all
three required per-union assertions, zero `HttpErrorResponse` unwraps remain under `features/`,
`login.spec.ts` non-disclosure and `dashboard.spec.ts` are intact (the latter byte-identical), the
four dashboard fences remain four independent fences, and the optimistic drag rollback is preserved.
The three "Adapted during implementation" notes in the plan accurately describe what shipped.

## Findings

### F1 — Every manual verification item is unchecked, including three explicit pause gates

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Success Criteria
- **Location**: `context/changes/frontend-error-and-patterns/plan.md` — Progress section
- **Detail**: All 30 automated checkboxes are `[x]` with commit shas. All **19 manual checkboxes are
  `[ ]`** — 1.5, 2.6-2.9, 3.5, 4.5-4.7, 5.5-5.8, 6.5-6.7, 7.6-7.8. The plan carries four explicit
  "**Implementation Note**: Pause here for manual confirmation before Phase N" gates (before Phases
  3, 5, 6 and 7); all four were passed without a recorded confirmation. This is not rubber-stamping
  — the checkboxes are honestly left unticked — but the unverified set is exactly where this slice
  carries its novel risk: the toast is the app's **first self-dismissing, animated, `LiveAnnouncer`-
  announced element**, the z-index scale is a new global coordination, and eight page headers plus
  three overlays were restyled. Items 2.9 and 7.8 (bundle under 550 kB) are now satisfied by the
  build above; the rest are genuinely browser-only (screen reader, OS reduce-motion, phone width,
  keyboard focus round-trip).
- **Fix**: Walk the plan's "Manual Testing Steps" 1-7 against the running app (`docker compose up -d`,
  `dotnet run --project src/Api/po-prostu-silka.Api.csproj`, `npm start`) and tick the Progress boxes;
  strike 2.9 and 7.8 now as verified at 512.42 kB.
  - Strength: The four pause gates exist because the plan's author judged these specific risks
    unverifiable from tests; the automated suite cannot reach any of them.
  - Tradeoff: A manual pass over seven scenarios at two viewport widths is real time.
  - Confidence: HIGH — the gap is plainly visible in the Progress section.
  - Blind spot: Some items may have been checked informally during implementation and simply not
    recorded, in which case the cost is only the ticking.
- **Decision**: PARTIALLY FIXED — 2.9 and 7.8 ticked in `plan.md` Progress, verified at 512.42 kB
  during this review. The remaining 17 manual items stay `[ ]` pending a browser session.

### F2 — The a11y behaviour S-19 introduced has zero test coverage

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `src/app/src/app/shared/forms/overlay-focus.ts:1-53`
- **Detail**: `useOverlayFocus()` is correct on inspection — it captures `document.activeElement` at
  construction, focuses the panel container (not its first control, so the dialog title announces),
  and guards restore with `document.contains(opener)` against an opener the action itself removed.
  But nothing tests it: there is no `overlay-focus.spec.ts`, and none of its three consumer specs
  (`class-bookings-overlay.spec.ts`, `class-create-overlay.spec.ts`, `class-details-overlay.spec.ts`)
  assert that focus enters the panel on open or returns to the opener on close — each only tests
  Escape-to-close. Manual item 7.7 covers exactly this and is also unchecked, so the behaviour is
  currently unverified by any means. A regression here is silent: the overlay still opens and closes.
- **Fix**: Add `shared/forms/overlay-focus.spec.ts` with a TestBed host asserting `document.activeElement`
  after first render and again after destroy, including the detached-opener branch.
- **Decision**: FIXED — added `src/app/src/app/shared/forms/overlay-focus.spec.ts` (4 tests: focus
  enters the panel and the panel carries `tabIndex -1`; focus returns to the opener on close; a
  detached opener does not throw; a panel-less component is a no-op). Suite 619 → 623, all green,
  `quality:check` clean. Verified non-vacuous by mutation: commenting out `panel.focus()` fails
  2 of the 4.

### F3 — The AGENTS.md z-index rule is stated more absolutely than the code it describes

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `AGENTS.md:89-91`
- **Detail**: The rule reads "`z-index` values come from the `--z-*` scale… **Never write a literal:
  nothing in this app opens a stacking context**, so every fixed surface resolves at the document
  root." Both halves overshoot the shipped code. Four literals remain and are correct:
  `schedule-calendar.scss:95,115,139,315` (`.calendar-empty` z-1, `.calendar-refusal` z-2,
  `.calendar-draft` z-1, `.cal-resize-handle` z-2) are `position: absolute` inside a positioned
  calendar tile — local stacking that has nothing to do with the document-root scale, and
  `position` + `z-index` there **does** open a stacking context, contradicting the claim literally.
  The `styles.scss:130-132` comment gets this exactly right and narrowly: "Neither App's `:host` nor
  `.shell-main` opens a stacking context." The AGENTS.md sentence generalised that true, scoped claim
  into a false global one. Concrete cost: a contributor following AGENTS.md literally either
  "fixes" the four calendar literals onto the scale — polluting a document-root scale with
  tile-local values — or adds a fifth uncoordinated root-level literal after concluding the rule
  cannot mean what it says.
- **Fix**: Narrow the AGENTS.md sentence to match `styles.scss:130-132` — the scale governs surfaces
  that resolve at the document root (skip link, bottom nav, overlays, toast), because neither `App`'s
  `:host` nor `.shell-main` opens a stacking context; z-index local to an element's own positioned
  ancestor, as in `schedule-calendar.scss`, is outside the scale.
- **Decision**: FIXED — `AGENTS.md:89-95` rewritten: the scale now explicitly governs the four
  document-root surfaces, the stacking-context claim is narrowed to `App`'s `:host` and
  `.shell-main` (matching `styles.scss:130-132`), and `schedule-calendar.scss` is named as the one
  legitimate local-stacking exception.

### F4 — AGENTS.md records 512.49 kB; the build measures 512.42 kB

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `AGENTS.md:51`
- **Detail**: `AGENTS.md` says "Measured at **512.49 kB** after S-19 (from 509.68 kB at that slice's
  midpoint)"; `plan.md:753` says **512.42 kB**, and the build run during this review reports
  512.42 kB. A 0.07 kB discrepancy, immaterial against the 550 kB threshold — but the number is in
  AGENTS.md precisely because it was measured rather than assumed, so the two documents disagreeing
  undercuts the reason for recording it.
- **Fix**: Correct `AGENTS.md:51` to 512.42 kB.
- **Decision**: FIXED — `AGENTS.md:51` now reads 512.42 kB, matching `plan.md:753` and the build
  run during this review.

### F5 — `busy-set.ts` and `load-fence.ts` have no dedicated unit specs

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `src/app/src/app/shared/forms/busy-set.ts`, `load-fence.ts`
- **Detail**: Both are correct as written — `busy-set` builds a **new** `Set` before mutating (the
  immutable replacement signals require; an in-place `.add()` would have missed change detection),
  and `load-fence`'s `generation` is a closure-local `let` recreated per `createLoadFence()` call, so
  fences are genuinely per-instance rather than module-global. Both are covered only indirectly
  through consumer specs (`class-types.spec.ts:222`, `members.spec.ts:271,301`). These are the two
  most-copied primitives in the app; a direct spec would pin the contract at the unit level rather
  than through incidental component behaviour. `form-state.ts`, `failure.ts`, `failure-messages.ts`
  and `toast-host.ts` all do have colocated specs; `transport-messages.ts` and `toast.service.ts` are
  fully exercised inside neighbouring specs.
- **Fix**: Add small direct specs for `Set` immutability and for `begin()`/`isCurrent()` monotonicity
  across two independent instances — or defer until either helper next changes.
- **Decision**: FIXED — added `busy-set.spec.ts` (5 tests) and `load-fence.spec.ts` (6 tests).
  Suite 623 → 634, all green, `quality:check` clean. Both verified non-vacuous by mutation:
  making `busy-set` mutate the existing `Set` in place fails "replaces the set rather than mutating
  it" (a plain `isBusy()` assertion could not catch this, so the test reads through a `computed`);
  hoisting `load-fence`'s `generation` to module scope fails "gives each call its own generation"
  plus 6 consumer-spec assertions.

### F6 — Playwright artifact paths added to `.prettierignore` in a slice that touches no E2E

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: `src/app/.prettierignore` (added in 5b0b876, Phase 1)
- **Detail**: Phase 1 added `playwright/.auth/`, `test-results/` and `playwright-report/`. This is
  unplanned — the plan's "What We're NOT Doing" does not cover it either way, and S-19 touches no E2E
  file. It is benign: all three are generated-artifact directories, no source file is excluded, and
  `npm run quality:check` passes over the full source tree (verified above). Recorded so the next
  reviewer does not re-derive it as suspicious.
- **Fix**: None needed — leave as-is; noted only for the record.
- **Decision**: SKIPPED — accepted as benign hygiene; kept so a future reviewer does not re-derive
  it as suspicious.

### F7 — `login.ts` keeps local signals instead of `createFormState()`

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/app/src/app/features/auth/login/login.ts:24-25`
- **Detail**: Login declares its own `error` and `submitting` signals rather than composing
  `createFormState()`, making it the one form screen in the sampled set that does not use the shared
  helper. It is defensible: login has no initial load (so `loading`/`loadFailed` are dead) and is
  permanently banner-only by the AGENTS.md rule (so `reject()` is dead and must stay dead). Adopting
  the helper would import three unused fields and one field that must never be called. No correctness
  impact.
- **Fix**: Leave as-is; if adopted for uniformity, add a comment stating `reject()` must never be
  called here, so the non-disclosure rule survives the next reader.
- **Decision**: SKIPPED — local signals are the right call here; composing the helper would carry a
  `reject()` that must never be called on this screen.
