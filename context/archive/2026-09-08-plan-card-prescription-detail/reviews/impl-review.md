<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: S-15 — The plan card carries the whole prescription

- **Plan**: `context/changes/2026-09-08-plan-card-prescription-detail/plan.md`
- **Scope**: Full plan (Phases 1–3, all Progress items `[x]`)
- **Date**: 2026-09-09
- **Verdict**: REJECTED
- **Findings**: 2 critical, 4 warnings, 2 observations

## Context

The three planned commits (`eacacd1`, `ef03739`, `94f6218`) implement the plan faithfully — contract,
bounds, projection, write path, tests, builder field, card rebuild, all matching the stated contracts
including the documented "Adapted during implementation" notes.

Every finding below except F2's mechanism traces to **`50ce249 "UI fixes"`**, an unplanned hand commit
that landed *after* the plan's epilogue (`d644645`) and rewrote Phase 3's presentation contract.

## Verification run at HEAD (50ce249)

| Check | Result |
|---|---|
| `dotnet build` from `src/` | PASS — 0 warnings, 0 errors |
| `dotnet test` from repo root | PASS — 494/494 |
| `npm test` from `src/app/` | **FAIL — 435 passed, 1 failed** |
| `npm run quality:check` | PASS — Prettier + ESLint clean |
| `npm run build` | PASS — no budget warning |
| Migration `Up`/`Down`, nullable, no `defaultValue` | PASS — verified in migration source |

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | WARNING |
| Safety & Quality | FAIL |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | FAIL |

## Findings

### F1 — The `clock` icon has no template case, so the rest parameter renders a blank

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `src/app/src/app/shared/icons/icon.html` (no `@case ('clock')`) vs `src/app/src/app/features/my-plan/my-plan.html:67`
- **Detail**: `50ce249` added `'clock'` to the `IconName` union (`icon.ts:11`) and used it for the
  Przerwa parameter, but never added the matching `@case` to `icon.html` — the file has cases for
  `home, calendar, booking, plan, info, more, time, idea, repeat, hantle`, and no `clock`. The
  `@switch` falls through to nothing, so an empty `<svg>` renders. `tsc` is satisfied because the name
  IS in the union, lint passes, and the build passes — nothing catches it. On the member's card every
  parameter shows a glyph except Przerwa, which shows a gap. This is precisely the silent-failure mode
  `icon.ts`'s own doc rule ("adding an icon means adding a case to the template") exists to prevent.
- **Fix**: Add a `@case ('clock')` to `icon.html` beside `('time')`, with a clock-face path in the same
  24×24 `currentColor` stroke idiom as its siblings.
  - Strength: Restores the one missing glyph; no other file changes.
  - Tradeoff: None — the alternative (dropping the icon from Przerwa) would leave the row visually
    inconsistent with its four neighbours.
  - Confidence: HIGH — grep over `icon.html` confirms the case is absent and `my-plan.html:67`
    confirms the usage.
  - Blind spot: None significant.
- **Decision**: FIXED — added `@case ('clock')` to `icon.html` with a dial distinct from the `time` stopwatch.

### F2 — `npm test` is red at HEAD: the bottom-nav aria-current assertion was not updated

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: `src/app/src/app/shared/bottom-nav/bottom-nav.spec.ts:101`
- **Detail**: `50ce249` renamed two nav tabs in `bottom-nav.ts` (`'Moje zajęcia'` → `'Zajęcia'`,
  `'Mój plan'` → `'Plan'`) and updated the label-list assertion, but left the `marks the current tab
  with aria-current` test asserting the old string. `npm test` fails: *expected 'Zajęcia' to be 'Moje
  zajęcia'*. Progress item 3.1 ("Frontend unit tests pass — 94f6218") was true when stamped and is
  false now. This is the second time this exact suite has gone stale behind a hand UI commit —
  `032ecdd` was raised to fix the same thing after `529520f`.
- **Fix**: Update the expected `aria-label` at `bottom-nav.spec.ts:101` to `'Zajęcia'`.
  - Strength: One string; restores the suite to green.
  - Tradeoff: None — the assertion is a stale mirror of a deliberate design change, not a real defect.
  - Confidence: HIGH — the failure output names the exact line and both values.
  - Blind spot: None significant.
- **Decision**: FIXED — `bottom-nav.spec.ts:101` now expects `'Zajęcia'`; `npm test` is 436/436 green.

### F3 — An unplanned commit rewrote Phase 3's presentation contract after the plan closed

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Scope Discipline / Plan Adherence
- **Location**: commit `50ce249` — `my-plan.html`, `my-plan.scss`, `icon.ts`, `icon.html`, `bottom-nav.*`
- **Detail**: `50ce249 "UI fixes"` landed after the epilogue commit `d644645` and materially changed
  what Phase 3 shipped: an icon on every parameter, the parameter list restyled from a wrapping
  gapped row into bordered equal-width columns, value-before-label ordering, the muscle group turned
  from a plain caption into a pill badge, and the note callout's background swapped off the `--ink`
  derivation. None of it appears in `plan.md`, and no "Adapted during implementation" note records
  it. The plan's Phase-3 adaptation note still describes code that no longer exists. `lessons.md`
  records this exact failure mode as an accepted rule — an implementation that moves past the plan
  while the plan keeps asserting the original contract, with the cost landing on the next reader and
  on every future review.
- **Fix A ⭐ Recommended**: Append an "Adapted after the plan closed (`50ce249`)" block to Phase 3 in
  `plan.md`, naming each presentation change and why.
  - Strength: Follows the recorded lesson exactly; makes the plan true again before it is archived,
    which is the last moment it can be corrected — archived plans are immutable by rule.
  - Tradeoff: Records a change that never went through plan review.
  - Confidence: HIGH — this repo's plans already carry three such blocks and the rule is written down.
  - Blind spot: The presentation changes themselves stay unreviewed as design decisions.
- **Fix B**: Open a follow-up change for the card restyle and review it on its own terms.
  - Strength: The restyle gets a real contract and a real review rather than a retrospective note.
  - Tradeoff: Heavier; the code is already shipped, so the change would be documentation-after-the-fact
    either way.
  - Confidence: MEDIUM — depends whether more card work is coming in M-3.
  - Blind spot: Haven't checked the roadmap for a queued follow-up slice.
- **Decision**: FIXED via Fix A — an "Adapted after the plan closed (`50ce249`)" block now records every presentation change in Phase 3 of `plan.md`.

### F4 — Two comments now argue against the code they sit on

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/app/src/app/features/my-plan/my-plan.scss:78-80`; `plan.md` Phase 3 adaptation note
- **Detail**: The `.my-plan-note` comment reads "The icon is aligned to the first line rather than
  centred: a three-line note with a centred icon points at nothing in particular" — but `50ce249`
  changed `align-items: flex-start` to `center`, so the comment states the opposite of what the rule
  does. Separately, the plan's Phase 3 note says the callout background is "derived from `--ink`, not
  a new token", while the code now reads `background: var(--section-cool)`. The plan itself flagged
  this class of problem for `.my-plan-link` — "leaving the comment would put a stated rationale next
  to code that contradicts it" — and it has reappeared one commit later.
- **Fix**: Rewrite the `.my-plan-note` comment to describe centring (or restore `flex-start` if
  centring was accidental), and correct the plan's adaptation note to name `--section-cool`.
- **Decision**: FIXED — the `.my-plan-note` comment now describes centring as the deliberate choice, and the stale `margin-top: 0.1em` nudge (with its first-line comment) was removed.

### F5 — Parameter dividers render doubled between items and trailing on the last

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/app/src/app/features/my-plan/my-plan.scss:80-88`
- **Detail**: `.my-plan-param` sets `border-right: solid 1px` on every item, and `& + .my-plan-param`
  adds `border-left: solid 1px` to every item after the first. Adjacent parameters therefore draw two
  1px lines where one is intended, and the last parameter carries a right border with nothing beyond
  it. On a card with a single parameter — the duration-only plank this slice exists for — that is a
  lone rule hanging off the right edge.
- **Fix**: Keep one rule only — drop `border-right` and rely on `& + .my-plan-param { border-left: … }`.
- **Decision**: FIXED — `border-right` dropped; the divider is drawn only by `& + .my-plan-param`, so it appears between parameters and nowhere else.

### F6 — The muscle-group pill uses raw values where the file uses design tokens

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/app/src/app/features/my-plan/my-plan.scss:53-60`
- **Detail**: `font-size: 0.65rem`, `border-radius: 23px`, `padding: 4px 8px` — raw numbers in a file
  that otherwise reaches for `var(--space-*)`, `var(--radius-sm)` and `var(--muted)`, and next to
  `.my-plan-label`, which uses `0.8125rem`. The plan's contract asked for exactly that: "reusing
  `var(--muted)` and the `0.8125rem` size `.my-plan-label` already uses". `23px` in particular is an
  arbitrary figure where a pill wants a large radius (`999px`) that cannot go wrong at any height.
- **Fix**: Replace with `var(--space-1) var(--space-2)` padding, a large radius, and the sibling's
  `0.8125rem`, or add a token if the smaller size is deliberate.
- **Decision**: FIXED — `border-radius: 999px`, `padding: var(--space-1) var(--space-2)` (pixel-identical to `4px 8px`), `font-size: 0.8125rem` matching `.my-plan-label`.

### F7 — The `idea` icon is dead code

- **Severity**: 💭 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: `src/app/src/app/shared/icons/icon.ts:10`, `icon.html:51-56`
- **Detail**: `50ce249` added `'idea'` to the union and a five-path lightbulb case to the template. A
  grep across `src/app/src/app` finds no usage. It ships in the eager shell bundle for nothing, and a
  closed union of icons is only useful while every member is real.
- **Fix**: Remove `'idea'` from the union and its `@case`, or use it.
- **Decision**: ACCEPTED — kept deliberately: the user intends to use `idea` for the trainer's note in the plan.

### F8 — Serie and Powtórzenia share the same glyph

- **Severity**: 💭 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/app/src/app/features/my-plan/my-plan.html:43,49`
- **Detail**: Both parameters render `<app-icon name="repeat" />`. Two adjacent cells with an identical
  icon carry no distinguishing information, so the icon column stops helping exactly where the card is
  densest — the text label does all the work. Not wrong, but worth a deliberate second glyph or none.
- **Fix**: Give Serie its own glyph, or drop the icon from one of the two.
- **Decision**: SKIPPED — the text label carries the distinction; a second glyph was not judged worth it.

## Post-triage (2026-09-09)

Six findings fixed, one accepted, one skipped. Re-verified after the fixes:

| Check | Result |
|---|---|
| `npm test` from `src/app/` | PASS — 436/436, 44 files |
| `npm run quality:check` | PASS — Prettier + ESLint clean |
| `npm run build` | PASS — initial total 510.42 kB, inside the 550 kB warning budget |
| `dotnet build` / `dotnet test` | PASS — unchanged by the triage (frontend-only edits) |

Both FAIL dimensions are cleared: **Safety & Quality** by F1 and **Success Criteria** by F2. The
remaining WARNING on Plan Adherence / Scope Discipline is now documented rather than open — `plan.md`
records what `50ce249` actually shipped.

► **Post-triage verdict: APPROVED**
