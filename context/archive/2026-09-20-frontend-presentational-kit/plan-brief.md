# The Presentational Layer Gets Components, Not Copies (S-23) — Plan Brief

> Full plan: `context/changes/frontend-presentational-kit/plan.md`

## What & Why

Six families of copied markup in the SPA become one artifact each, and a custom ESLint rule on
Angular templates fails the build when a screen hand-rolls one of them again. S-19 unified how a
failure is *told*; its scope excluded the presentational layer outright, and that is what this
slice picks up. The enforcement is the anchor, not the components — a kit without the rule is a
ninth way to draw a field.

## Starting Point

56 copies of the `.field` block across 13 templates. 22 copies of `<p class="notice"
role="status">Wczytywanie…</p>`. Nine identical `.empty` declarations, two of them under comments
claiming the class already lives in `src/styles.scss`. Five of seven `<select>` elements missing
the `.select` wrapper that carries the chevron — so they have their native arrow suppressed and
**nothing in its place**, a failure `styles.scss:280` warns about in a comment. `.field-label` used
four times and declared nowhere. Two bare checkboxes. `.notice` carrying four unrelated meanings at
once.

## Desired End State

Every form control, loading state, empty state and list row in `app/**` is one component. A
contributor who writes `<div class="field">` or a bare `<select>` inside `app/features/**` gets a
failing `npm run quality:check` naming the component to use instead, and `AGENTS.md` explains why.
Four selects gain a chevron they never had; nothing else looks different.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| `app-field` contract | Projects the control (`ng-content`) | The two irregular shapes — `plan-builder`'s branching label and two error sources, `profile`'s `server`/fallback pair — are absorbed instead of left behind as a second copy the rule could never forbid. |
| `app-select` contract | Wrapper only; the `<select>` is projected | Makes the wrapper impossible to omit (the entire defect), and projected content binds in the caller's context so `formControlName`, `(change)` and `[disabled]` need no passthrough. |
| `app-checkbox` | Toggle with `checked`/`checkedChange`, no CVA | Both existing checkboxes are filter toggles and no form in the app has one; CVA is additive when a form needs it. |
| `app-loading` | Fixed word, no message input | An input that exists will be used, and three screens later there are four sentences for one state. |
| `app-empty` | Projects its content | Each empty state says something different and two carry a link onward. |
| `app-list` / `app-row` | Four projection slots, no inputs | The recipe is identical across seven screens but every row carries something of its own — thumbnail, badge, menu, drag handle. |
| Enforcement mechanism | Custom ESLint rule on templates | A real AST, so comments and substrings don't produce false alarms, and the `<select>`-inside-`app-select` ancestor check is something a source scan could not do. |
| Enforcement boundary | `app/features/**` only | Structural and self-explaining — the kit lives in `shared/`, screens in `features/` — with no exemption list to maintain. |
| Rule rollout | Last phase, straight to `error` | Introduced earlier it fails every not-yet-migrated file, making every intermediate phase un-commitable. |
| Migration scope | Every template in `app/**`, `shared/` and overlays included | Otherwise `schedule-calendar.html:59` stays as a pattern to copy that the rule does not reach. |
| Success outlet | Toast raised **before** navigating; `?reset=ok` deleted | The toast host is mounted outside `router-outlet` (`app.html:63`), so it survives the redirect and the query-param plumbing becomes dead weight. |
| Bundle threshold | 550 kB → 600 kB in Phase 1, permanently | Decided against measure-first; see Open Risks. |

## Scope

**In scope:** six kit components with specs; `.empty`, `.section-title`, `.form-actions` lifted into
`src/styles.scss` and their 14 copies deleted; all five defects closed; ~30 templates migrated; the
ESLint rule plus its RuleTester spec and four declared devDependencies; the written rule in
`AGENTS.md`.

**Out of scope:** any UI library (Angular Material breaks both the budget and the design); i18n; an
`OnPush` sweep; any backend change; the button classes, which are already consistent; ESLint
coverage of `shared/` and `core/`; restoring the bundle threshold.

## Architecture / Approach

Components are **paired with their migration** rather than built up front — a phase that adds six
components with no callers cannot be verified by looking at the app, and leaves both the old copies
and the new kit in the tree with nothing deciding between them. Every component projects rather
than owns, which costs the guarantee that `aria-invalid` and `for`/`id` are wired; the ESLint rule
is what closes that gap. Each phase ends with a tree that passes `quality:check` and `npm test` and
is independently commitable.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Global stylesheet | Three classes lifted, 14 copies deleted, threshold to 600 kB | A copy carried something extra and deleting it changes a screen — this phase must be visually a no-op |
| 2. Controls + migration | `app-field`, `app-select`, `app-checkbox`; 56 `.field` copies gone, 5 selects fixed | `app-field` fails to absorb `plan-builder` or `profile`, leaving a second copy behind |
| 3. States + success | `app-loading`, `app-empty`; 22 loading copies gone; success on the toast | The reset toast is raised or lost at the wrong moment across the redirect |
| 4. List + migration | `app-list` / `app-row`; seven prefixes collapsed | The four slots don't hold the thumbnail box or the row menu, and layout regresses at phone width |
| 5. Enforcement | The rule, its spec, the devDependencies, the `AGENTS.md` rule | A rule with no negative cases blocks correct code on someone else's CI |

**Prerequisites:** S-19 (`frontend-error-and-patterns`), archived 2026-09-20. Node 22+, `npm ci` in
`src/app/`.
**Estimated effort:** ~5 sessions, one per phase; Phase 2 is the largest (13 templates).

## Open Risks & Assumptions

- **The bundle threshold moves before anything is measured.** `AGENTS.md` records the 500→550 move
  as having happened because "the original figure was an estimate rather than a measured
  constraint"; going to 600 kB up front repeats that, and 88 kB of slack over the last measured
  512.42 kB means the budget warns about nothing until a large regression. The figure is measured
  at the end of Phases 2 and 5 and recorded, but the threshold does not come back down. Decided
  deliberately.
- **`shared/` is migrated but not enforced.** `schedule-calendar.html` and the summary components
  are cleaned in Phases 3 and 4, and nothing stops them drifting back.
- **Three rule-authoring packages are used transitively today.** Phase 5 declares them; until then
  a dedupe in `node_modules` could change what resolves.
- **Assumption: the seven row screens differ only in content, not in behaviour.** Verified by
  reading the markup, not by running them; Phase 4's manual step is where it is actually tested.

## Success Criteria (Summary)

- The audit that opened this change returns zero on all six families.
- Four selects that had no arrow at all now have one; nothing else in the app looks different.
- A hand-rolled `.field` or a bare `<select>` in a feature template fails `npm run quality:check`
  with a message naming what to use instead.
