<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Schedule Calendar View

- **Plan**: `context/changes/schedule-calendar-view/plan.md`
- **Mode**: Deep
- **Date**: 2026-09-02
- **Verdict**: REVISE → SOUND (all findings fixed in triage)
- **Findings**: 2 critical, 3 warnings, 1 observation — 6 fixed, 0 outstanding

## Verdicts

| Dimension | Verdict (at review) | After fixes |
|-----------|--------------------|-------------|
| End-State Alignment | PASS | PASS |
| Lean Execution | WARNING | PASS |
| Architectural Fitness | PASS | PASS |
| Blind Spots | FAIL | PASS |
| Plan Completeness | WARNING | PASS |

## Grounding

16/16 paths ✓, symbols ✓, blast radius clean (`getSchedule`/`getAdminClasses`: 2 callers, both named;
`IClassScheduleQuery`: 1 implementation + 1 DI registration, both named), brief↔plan ✓,
Progress↔Phase 4/4 phases matched (auto 4/5/3/4, manual 3/6/4/6), 0 stray checkboxes outside
`## Progress` ✓. `docs/reference/contract-surfaces.md` absent — surface check skipped.
One Current State claim was contradicted by the build output — F1.

## Findings

### F1 — The prerender premise is false

- **Severity**: ❌ CRITICAL
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Blind Spots
- **Location**: Current State Analysis; Critical Implementation Details; Phase 2
- **Detail**: The plan stated "All routes are prerendered" and built its central Critical
  Implementation Detail on it. Nothing is prerendered: `angular.json`'s build target has no `server`,
  `ssr` or `prerender` key; `dist/app/prerendered-routes.json` is `{"routes": {}}`; there is no
  `dist/app/server/`; `dist/app/browser/index.html` ships a bare `<app-root></app-root>`. The SSR
  scaffolding (`main.server.ts`, `server.ts`, `app.routes.server.ts`, `@angular/ssr`,
  `provideClientHydration()`) exists but is never wired, so `RenderMode.Prerender` is dead
  configuration. This invalidated the day-view justification, the claim that `npm run build` catches an
  unguarded `matchMedia`, and manual step 2.9.
- **Fix A ⭐ Recommended**: Correct the premise, keep the design.
  - Strength: The day default and `isPlatformBrowser` guard stand on their own — mobile-first, and
    jsdom provides no `matchMedia`. Keeps S-07 scoped to S-07.
  - Tradeoff: The half-wired SSR scaffolding is noticed and deliberately left alone.
  - Confidence: HIGH — verified against the build output.
  - Blind spot: Why SSR was scaffolded and never wired is not established.
- **Fix B**: Wire SSR as part of this change.
  - Strength: Makes the premise true and delivers the SSR the repo already pays for.
  - Tradeoff: Turns a UI slice into an infrastructure one; every screen renders on the server for the
    first time.
  - Confidence: MEDIUM — the wiring is mechanical, the fallout is not.
  - Blind spot: Auth-cookie flow and the three route guards have never run under SSR.
- **Decision**: FIXED via Fix A — Current State Analysis rewritten with the verified evidence;
  Critical Implementation Details re-justified on mobile-first + jsdom grounds (guard kept, with the
  reason stated); Phase 2 build criterion now states what it does *not* prove and points at the specs;
  manual 2.9 replaced with a first-paint/promotion check; the library section's "unverified under
  prerender" risk retired; `plan-brief.md` Starting Point, decision row, Phase 2 risk and Open Risks
  all updated.

### F2 — Bundle budget breaks before the plan's contingency triggers

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Performance Considerations; Phase 2
- **Detail**: The plan loaded `angular-calendar` eagerly and treated lazy loading as a contingency "if
  the build's budget complains". `angular.json` sets initial 500 kB warning / 1 MB error, and
  `dist/app/browser/main-CGWIYYLG.js` is 433,848 bytes — ~76 kB of headroom against a ~731 KB unpacked
  library plus `date-fns` v4 and two peers. Phase 2 listed `npm run build` but not the budget, so the
  breach would have surfaced as a mid-phase build failure rather than a decision already made.
- **Fix**: Lazy-load `/schedule` and `/admin/classes` from Phase 2 as the default, and verify the
  budget explicitly.
  - Strength: Keeps the library off login, register and pending — the screens an unapproved member sees.
  - Tradeoff: One extra chunk fetch on the two routes that use it.
  - Confidence: HIGH — measured against the actual dist output and the budget in `angular.json`.
  - Blind spot: The gzipped contribution is unmeasured; direction is certain, exact margin is not.
- **Decision**: FIXED — Performance Considerations rewritten with the measured figures; new Phase 2 §8
  converts both routes to `loadComponent` (guards and route order unchanged); automated criterion 2.3
  added for the budget and lazy chunk; manual 2.11 added for the network check; Progress renumbered
  (auto 2.1–2.5, manual 2.6–2.11); brief's Open Risks bullet replaced with the measured version.

### F3 — `classSelected` output has no consumer

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Lean Execution
- **Location**: Phase 2 §4
- **Detail**: The component contract declared `classSelected: EventEmitter<ScheduledClass>` with no
  phase binding it. The member screen has no details view (booking is S-08); the admin reaches a class
  through the projected action template.
- **Fix**: Drop it from the Phase 2 contract; add it in S-08 when booking gives it a consumer.
- **Decision**: FIXED — output removed and the omission stated with its reason; the event-mapping
  bullet's reference to it removed.

### F4 — `invalid_range` widens a union that Phase 4 makes exhaustive

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Completeness
- **Location**: Phase 1 §5; Phase 4 §1
- **Detail**: Phase 1 added `'invalid_range'` to the shared `ClassFailure` union while Phase 4 required
  `classFailureMessage` to be exhaustive over it — forcing a write-form banner for a failure only the
  read endpoints return, which no screen handles anyway (both fall back to `loadFailed`).
- **Fix A ⭐ Recommended**: Give the read path its own client type.
  - Strength: `ClassFailure` keeps mirroring the write contract field for field, as its doc comment
    promises; the read failure is modelled where it happens.
  - Tradeoff: A second small type in `class.models.ts`.
  - Confidence: HIGH — the split follows the endpoint groups already in `ClassEndpoints.cs`.
  - Blind spot: The server still returns one `ClassFailure` record; the split is client-side modelling.
- **Fix B**: One union, `invalid_range` explicitly excluded from the message table.
  - Strength: One type, one mirror, minimal edit.
  - Tradeoff: The exhaustiveness guarantee acquires a hand-written hole.
  - Confidence: MEDIUM — depends on the exclusion staying commented and respected.
  - Blind spot: None significant.
- **Decision**: FIXED via Fix A — Phase 1 §5 now specifies `ScheduleReadFailure { reason: 'invalid_range' }`
  as a separate client type and states why it is kept out of `ClassFailure`; Phase 1 §3's C# contract
  notes in the doc comment that `invalid_range` is read-path-only.

### F5 — "active types only" describes a filter the service doesn't do

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 4 §3
- **Detail**: The contract read "class type from `ClassTypeService` (active types only)".
  `ClassTypeService.getAll()` is deliberately unfiltered — its doc comment says so — and
  `class-form.ts:135` filters `isActive` itself. An implementer would look for a service method that
  does not exist.
- **Fix**: State that the overlay filters `isActive` client-side, mirroring `class-form.ts:135`, and
  that `getAll()` stays unfiltered.
- **Decision**: FIXED — contract rewritten accordingly, and it now also notes that `getTrainers()`
  already returns active trainers only.

### F6 — Roadmap amendment omits the `PRD refs` line

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1 §2
- **Detail**: Phase 1 adds FR-019 to `prd-v2` and rewrites S-07's Outcome, Risk and Status lines, but
  the item's `- **PRD refs:**` line still listed only v2 FR-015 – FR-018, leaving the new requirement
  untraceable from the roadmap.
- **Fix**: Add `v2 FR-019` to the S-07 `PRD refs` line in the same edit.
- **Decision**: FIXED — instruction added to the Phase 1 §2 contract.
