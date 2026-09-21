---
change_id: frontend-presentational-kit
title: The presentational layer gets components, not copies (S-23)
status: archived
created: 2026-09-20
updated: 2026-09-21
archived_at: 2026-09-21T08:34:30Z
---

## Notes

S-23: the presentational layer gets components, not copies. Numbered S-23 because S-20, S-21 and
S-22 were already taken (`admin-schedule-web-only`, `member-list-at-scale`,
`member-centric-training-plans`) — the ID is a name, not a schedule, and the roadmap entry records
that this is worth running BEFORE S-20 rather than after it: those three slices each rewrite screens
full of the copied blocks. Its milestone is deliberately unassigned; M-6's CS anchors are spent and
M-7 is about surface fitness, not component structure.

S-19 unified how a failure is *told* (the four outlets, the message tables, seven behavioural
duplicate families in TypeScript). Its `plan-brief.md:53` scope excluded the presentational layer
entirely, and that is what this slice picks up. Raised by the user after noticing divergent
notices, buttons, dropdowns and single-choice lists across screens, with production-readiness as
the stated bar.

### Defects already in the tree (audited 2026-09-20)

1. **Two different dropdowns for the same job.** `class-form.html:33` wraps its `<select>` in
   `<span class="select">`; `class-create-overlay.html:36,46` does not. `styles.scss:285` sets
   `appearance: none` and draws the chevron from `::after` on the WRAPPER, so those two selects
   have no arrow at all. `styles.scss:279` warns about exactly this failure in a comment.
2. **`.field-label` is undefined.** Used 4x in `class-create-overlay.html`, declared nowhere, so
   those labels miss the 0.875rem/500 that `.field label` gives every other form.
3. **`.empty` is a lie in two comments and a copy in nine files.** `dashboard.scss:1` and
   `member-passes.scss:1` claim it comes from `src/styles.scss`; it does not. Nine identical
   declarations (`margin: 0; color: var(--muted)`). Same shape for `.section-title` (3) and
   `.form-actions` (2).
4. **Success has two mechanisms.** Toast in five admin screens vs inline `.notice` in
   `profile.html:17,131` and the auth screens — so `.notice` now carries four unrelated meanings:
   loading, empty, info, success.
5. **Two unstyled checkboxes.** `class-types.html:14`, `exercises.html:13` — bare
   `<input type="checkbox">`, the only controls in the app with no shared class.

### Copy counts that make the next duplicate inevitable

- `.field` block (label + input + `aria-invalid` ternary + `.field-error`): **56 / 59** copies.
- `<p class="notice" role="status">Wczytywanie…</p>`: **22** copies.
- `<feature>-list / -row / -name / -meta / -actions`: the same flex recipe under ~9 different names.

Buttons are NOT part of the problem: `.button`, `.button--secondary`, `.button--block` and
`.link-button` are already consistent, and this slice should not churn them.

### Scope decided with the user before planning

Full kit plus enforcement: the five defects, standalone presentational components
(`app-field`, `app-select`, `app-checkbox`, `app-loading`, `app-empty`, list/row), the three
copied classes lifted into `styles.scss`, success closed onto the toast outlet, migration of the
~30 screens, a rule in `AGENTS.md`, and a contract spec in the spirit of
`core/http/failure-contract.spec.ts` so a `<select>` without its wrapper (or a hand-rolled
`.field`) fails rather than merely reviews badly.

**Deliberately not in scope:** any UI library (Angular Material would break both the 550 kB
eager-bundle threshold and the existing design), i18n, an `OnPush` sweep, any backend change.

**Watch the bundle:** eager chunk measured 512.42 kB after S-19 against a 550 kB warning
threshold (`AGENTS.md`). These components land in eager screens, so the number must be re-measured.

**Depends on:** `frontend-error-and-patterns` (S-19), still `impl_reviewed` and unarchived at the
time this change was opened.
