<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: The Presentational Layer Gets Components, Not Copies (S-23)

- **Plan**: context/changes/frontend-presentational-kit/plan.md
- **Scope**: Full plan — Phases 1–5 of 5
- **Date**: 2026-09-21
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 3 warnings, 5 observations

## Evidence base

- Plan-drift sub-agent: completed. No functional drift found across all five phases; every
  "Adapted during implementation" note matches the code.
- Safety/pattern sub-agent: **did not complete** (session rate limit). Its scope was covered by
  direct checks in the main session instead: rule bypass probing, label/`for` coverage across all 56
  `app-field` sites, dead-selector scan of the seven migrated stylesheets, scope guardrails, and
  config side effects. Accessibility of the new components was reviewed only through the drift
  agent's slot/markup checks and the existing template-accessibility lint, not by a dedicated pass.
- Automated criteria re-run: `npm run build` (513.90 kB eager), `npm run quality:check` (clean),
  `npm test` (61 files, 682 tests) — all pass. Every grep-based criterion (1.4, 2.2–2.4, 3.2, 3.3)
  re-verified. Text content of every migrated template diffed against `70b73fa`: nothing lost.
- Scope guardrails: 0 backend files touched; 4 new packages, all `devDependencies`; no button
  rule removed; no `OnPush`; no `ControlValueAccessor` (both hits are prose).

## Triage (2026-09-21)

All eight findings fixed; none skipped, none recorded as a lesson.

| Decision | Findings |
|----------|----------|
| Fixed | F1 (Fix A), F2, F3, F4, F5, F6, F7, F8 |

After the fixes: `npm run build` 513.90 kB (unchanged), `npm run quality:check` clean — now also
over `tools/**` — and `npm test` 61 files / 686 tests (682 + the 4 new RuleTester cases). The
verdicts below are as found at review time; the fixes are not committed yet.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | WARNING |
| Pattern Consistency | WARNING |
| Success Criteria | WARNING |

## Findings

### F1 — The lint rule does not check what the design says it checks

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Architecture
- **Location**: src/app/tools/eslint-rules/no-hand-rolled-presentational.js; claims in src/app/src/app/shared/list/row.ts:20-21, src/app/src/app/shared/forms/field/field.ts (docblock), AGENTS.md "The presentational kit (S-23)"
- **Detail**: The slice's central trade is "components project, so they cannot check what was projected — the lint rule is the other half". `row.ts` says the rule is what checks "that a row HAS a name"; `field.ts` says "what is not guaranteed here is guaranteed by lint". The rule actually only forbids hand-rolling (`class="field"`, bare `<select>`, etc.). It does not check correct *use*: `<app-field>` with neither `label` nor a `[slot=label]` child passes, as does `<li appRow>` with no `.row-name`. All 56 current `app-field` sites were verified correct by an ad-hoc script (every one has a label; every static `for=` names an id inside its own field) — but nothing pins that, and no screen-level spec asserts a `<label>` renders on any of the 13 migrated forms.
- **Fix A ⭐ Recommended**: Extend the rule with one check — an `app-field` must carry `label`/`[label]` or contain a `[slot=label]` element — plus valid/invalid RuleTester cases.
  - Strength: Closes exactly the gap the design names, on the component with 56 callers; deterministic on the AST, same shape as the existing ancestor check.
  - Tradeoff: ~15 lines of rule logic and three test cases. Bound `[for]` expressions still cannot be verified statically, so the `for`/`id` pairing stays unchecked.
  - Confidence: MED — straightforward AST check, but untried against every current caller.
  - Blind spot: Whether `li[appRow]` should get a matching `.row-name` check — my-classes' row holds a whole component and has no `.row-name`, so that one would need an exemption.
- **Fix B**: Narrow the three docblocks and the AGENTS.md section to what the rule actually enforces ("forbids hand-rolling"), and say plainly that correct use is unchecked.
  - Strength: Zero code; makes the documentation true today.
  - Tradeoff: The gap stays open, on the exact invariant the slice set out to protect.
  - Confidence: HIGH — prose-only change.
  - Blind spot: None significant.
- **Decision**: FIXED via Fix A — `labelAppField` check added (walks into @if/@for branches, stops at a nested app-field); 2 valid + 2 invalid RuleTester cases; `row.ts` docblock corrected to say the rule does NOT check that a row has a name, and why. Whole tree passes lint with the new check.

### F2 — AGENTS.md's bundle paragraph contradicts itself

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: AGENTS.md:51
- **Detail**: The paragraph opens in bold with "The initial-bundle warning in `angular.json` is **550 kB**" — now false; `angular.json:45` says `600kB`. Later in the same paragraph it says "The threshold is 600 kB since S-23". It also states "88 kB of slack"; 600 − 513.90 = **86.1 kB**. AGENTS.md is the file agents read first, and the bold lead is what a skimmer keeps.
- **Fix**: Rewrite the lead to "is 600 kB (error at 1 MB), raised from 550 kB in S-23 and from 500 kB in S-12", keep the history after it, and correct 88 → 86 kB.
- **Decision**: FIXED — lead now reads "is 600 kB (error at 1 MB) — raised from 550 kB in S-23, and from 500 kB in S-12 before that"; the later duplicate sentence folded into it; slack corrected 88 → 86 kB.

### F3 — Criterion 5.8 is not met: the final figure is not in the plan

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: context/changes/frontend-presentational-kit/plan.md, "Performance Considerations"
- **Detail**: 5.8 reads "Final eager-bundle figure recorded in `AGENTS.md` and in this plan". 513.90 kB is in AGENTS.md only. The Phase 2 measurement that 2.11 asks for (513.78 kB) exists only in a commit message. The plan says measurements are "recorded, because the last measured number is the only thing that makes the next such decision evidence-based" — and the plan is where that next reader looks.
- **Fix**: Add a short "Measured" line to the plan's Performance Considerations: 512.42 (S-19) → 512.54 (p1) → 513.78 (p2) → 513.79 (p3) → 513.90 kB (p4, p5).
- **Decision**: FIXED — measurement series added at the end of the plan's Performance Considerations. 2.11 and 5.8 are now satisfiable; both stay unchecked as manual rows for the user to confirm.

### F4 — The rule is bypassed by bound classes and interpolated text

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/app/tools/eslint-rules/no-hand-rolled-presentational.js (`classTokens`, Text visitor)
- **Detail**: Probed on `dashboard.html`: `<div [class]="'field'">`, `<div [ngClass]="{ field: true }">` and `<p class="notice">{{ loadingWord }}</p>` all pass lint. That is normal for a lint rule — it guards accidents, not intent — but AGENTS.md says a hand-rolled copy "fails the build", which reads as absolute.
- **Fix**: Add one sentence to AGENTS.md: the rule reads static markup, so a class bound through `[class]`/`[ngClass]` or a word built by interpolation is outside its reach and is a review finding instead.
- **Decision**: FIXED — AGENTS.md gains a "What it cannot see" paragraph: the rule reads static markup, so a bound `[class]`/`[ngClass]` or an interpolated loading word passes lint and is a review finding instead.

### F5 — `Element$1` / `Text$3` visitor keys never match

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/app/tools/eslint-rules/no-hand-rolled-presentational.js (visitor keys)
- **Detail**: The template parser sets `node.type` from `constructor.name`, which is always `Element`/`Text` (verified in `@angular-eslint/template-parser/dist/index.js:178,191`). The `$1`/`$3` names are bundler variable names and never appear as node types. Harmless — the plain alternatives cover every node — but misleading to the next reader of the one rule that can block every merge.
- **Fix**: Use plain `'Element'` and `'Text'` visitor keys.
- **Decision**: FIXED — visitor keys are plain `Element` and `Text`.

### F6 — `"node"` types now apply to every app spec

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/app/tsconfig.spec.json (`types`)
- **Detail**: `"node"` was added so the rule's spec can call `require()`, but `tsconfig.spec.json` compiles all 61 spec files, so every app spec can now use `process`, `fs` or `require` without a type error — APIs that do not exist in the browser test environment. No app spec does so today (grep: none), so the loosening is latent.
- **Fix**: Drop `"node"` from `types` and add `declare function require(id: string): any;` at the top of `no-hand-rolled-presentational.spec.ts`.
- **Decision**: FIXED — `"node"` dropped from tsconfig.spec.json types; the rule spec declares `require` locally. This broke the compile of an untracked probe file (`tools/eslint-rules/_tmp-check.spec.ts`, plus `_tmp-check.cjs`) left behind by the aborted safety sub-agent; both were deleted with the user's approval.

### F7 — The rule and its spec are never linted in CI

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/app/angular.json (`lint.options.lintFilePatterns`)
- **Detail**: `ng lint` covers `src/**/*.ts` and `src/**/*.html` only, so `tools/eslint-rules/*.js` and its `.spec.ts` are formatted by Prettier in CI but never linted. The post-edit hook linted them locally during implementation; CI does not.
- **Fix**: Add `tools/**/*.ts` and `tools/**/*.js` to `lintFilePatterns`.
- **Decision**: FIXED — `tools/**/*.ts` and `tools/**/*.js` added to lintFilePatterns, plus an eslint.config.js block giving `tools/**/*.js` JS-recommended rules and CommonJS globals (without it the .js would lint against an empty rule set). The widened scope immediately flagged an unused `eslint-disable` directive in the rule spec, since removed.

### F8 — The plan says "five" `.notice` uses and lists four

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: context/changes/frontend-presentational-kit/plan.md, Key Discoveries (".notice's remaining honest uses are five")
- **Detail**: The plan says five, enumerates four (member-passes, forgot-password, reset-password, plan-builder) and closes with "All four". The fifth is `profile.html:6`, the pre-existing "Uzupełnij swoje dane kontaktowe" prompt. Code and AGENTS.md are correct; only the plan's prose undercounts.
- **Fix**: Add `profile.html:6` to the list and change "All four" to "All five".
- **Decision**: FIXED — `profile.html:6` added to the plan's list; "All four" → "All five".

## Manual verification

21 manual Progress rows are pending across all five phases (1.5–1.7, 2.6–2.11, 3.6–3.10, 4.4–4.7,
5.6–5.8). None is checked, so there is no rubber-stamping to flag. Two are partly satisfied by
evidence already in hand but stay unchecked by rule: 2.11 (measured, but see F3) and 5.6 (the three
failing messages observed during the firing probe read as instructions). 5.8 is not satisfied — F3.

Resolved by analysis, still worth the manual glance in 4.5: `/admin/members` identity went from
no flex basis to `flex: 1 1 16rem`. Below 30rem the row is a column anyway (members.scss media
query), and 16rem is 256px, so above 30rem the actions should not wrap earlier than before.
