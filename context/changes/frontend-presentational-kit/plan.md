# The Presentational Layer Gets Components, Not Copies (S-23) — Implementation Plan

## Overview

S-19 unified how a failure is *told* — four outlets, message tables, and seven behavioural
duplicate families extracted into `shared/forms/`. Its `plan-brief.md:53` scope excluded the
presentational layer outright, and that is what this slice picks up.

Six families of copied markup in the SPA become one artifact each — `app-field`, `app-select`,
`app-checkbox`, `app-loading`, `app-empty`, and an `app-list` / `app-row` pair — three copied
CSS classes move into `src/styles.scss` where two comments already claim they live, success stops
being an inline `.notice` and becomes the toast outlet S-19 built for it, and a custom ESLint rule
on Angular templates fails the build when a screen under `app/features/**` hand-rolls any of them.

The enforcement is the anchor, not the components. A component set without the rule is a ninth
way to draw a field — exactly as CS-04 put the rule, not the toast, at the centre of S-19.

## Current State Analysis

Audited against the tree on 2026-09-20. Every count below was re-verified during planning.

**Five defects already shipped:**

1. **Two different dropdowns for the same job.** `styles.scss:285` sets `appearance: none` on
   `.field select`, and `styles.scss:297` draws the replacement chevron from `::after` on the
   `.select` **wrapper** — a `<select>` is a replaced element and generates no pseudo-element of
   its own. Only `class-form.html:33` and `class-form.html:111` supply that wrapper. The other
   five selects — `class-create-overlay.html:36`, `class-create-overlay.html:46`,
   `class-bookings-overlay.html:65`, `plan-builder.html:52` — have their native arrow suppressed
   and **nothing in its place**. `styles.scss:280-283` warns about precisely this failure in a
   comment, and the failure happened anyway.
2. **`.field-label` is undefined.** Used 4× in `class-create-overlay.html` (lines 35, 45, 60, 72),
   declared in no stylesheet. Those labels miss the `0.875rem` / `500` that `.field label`
   (`styles.scss:249`) gives every other form in the app.
3. **`.empty` is a lie in two comments and a copy in nine files.** `dashboard.scss:1` and
   `member-passes.scss:1` claim it comes from `src/styles.scss`; it does not. Nine identical
   declarations exist: `class-types.scss:73`, `exercise-detail.scss:68`, `exercises.scss:107`,
   `members.scss:169`, `dashboard.scss:69`, `my-plan.scss:121`, `plan-exercise-detail.scss:54`,
   `plan-builder.scss:156`, `plans.scss:66`. `.section-title` has the same shape in 3 files
   (`member-form.scss:3`, `member-passes.scss:4`, `profile.scss:3`), `.form-actions` in 2
   (`member-form.scss:17`, `member-passes.scss:26`).
4. **Success has two mechanisms.** Five admin screens raise a toast; `profile.html:16`,
   `profile.html:137` and `login.html:13` render an inline `.notice`. `.notice` therefore carries
   four unrelated meanings at once — loading, empty, info, success.
5. **Two unstyled checkboxes.** `class-types.html:14` and `exercises.html:13` are bare
   `<input type="checkbox">` — the only controls in the app with no shared class.

**Copy counts that make the next duplicate inevitable:**

- `.field` block (label + control + `aria-invalid` ternary + `.field-error`): **56 copies** across
  13 templates — `exercise-form.html` 9, `plan-builder.html` 8, `profile.html` 8,
  `member-form.html` 6, `class-form.html` 5, `member-passes.html` 4, `class-create-overlay.html` 4,
  `class-type-form.html` 4, `reset-password.html` 2, `register.html` 2, `login.html` 2,
  `forgot-password.html` 1, `class-bookings-overlay.html` 1.
- `<p class="notice" role="status">Wczytywanie…</p>`: **22 copies** across 18 templates, one of
  which is `shared/calendar/schedule-calendar.html:59`.
- The `-row` flex recipe: identical to the character in `exercises.scss:21`, `class-types.scss:23`
  and `plans.scss:26` (they differ only in one `justify-content`), under 7 different prefixes
  across 7 templates.

**What is NOT a problem:** `.button`, `.button--secondary`, `.button--block` and `.link-button` are
already consistent. This slice does not churn them.

### Key Discoveries:

- **`<app-toast-host />` is mounted globally** — `app.html:63`, outside `<main>`, outside
  `router-outlet`, and outside every auth gate, with a comment stating "a toast can be raised from
  the login screen as readily as from the admin's member list". A toast therefore **survives a route
  change**, which is what makes Phase 3's success migration possible for the auth screens.
- **CI gates both lanes.** `.github/workflows/deploy.yml:39` runs `npm run quality:check` and
  `:50` runs `npm test`. An ESLint rule blocks a merge exactly as a spec does.
- **The ESLint rule is feasible without a toolchain change.** `eslint.config.js` is CommonJS flat
  config with an existing `files: ['**/*.html']` block already carrying angular-eslint's template
  parser. `@angular-eslint/utils@22.2.0`, `@angular-eslint/bundled-angular-compiler@22.2.0` and
  `@typescript-eslint/utils@8.68.0` are present in `node_modules` — but **transitively only**, none
  is declared in `package.json`. `@angular-eslint/test-utils` (RuleTester) is absent.
- **`plan-builder.html:36-78` is not a `.field`.** It has a branching label (`<span
  class="builder-caption">` when editing, `<label>` otherwise), static text in place of a
  `<select>` when editing, and **two independent error sources** (`membersFailed()` plus the
  control's own validation). This is the roadmap's stated unknown, and it is why `app-field`
  projects rather than owns.
- **`profile.html:145-160` carries a `server`/fallback error pair** — a second shape an owning
  `app-field` could not hold.
- **The row shape is regular even though the names are not.** Every one is
  `<li class="card *-row">` → optional leading media → `*-identity` (name + meta lines) → `*-actions`
  → optional trailing error line.
- **`.notice`'s remaining honest uses are five, and they are one meaning.** After loading moves to
  `app-loading`, empty to `app-empty` and success to the toast, what is left is
  `member-passes.html:11` (the active pass panel), `forgot-password.html:8` and
  `reset-password.html:9` (a page-level statement that replaced the form), and
  `plan-builder.html:216` (the item-limit statement). All four are informational screen content.
  `.notice` survives as **one** named meaning rather than four.
- **`?reset=ok` plumbing is two lines.** `reset-password.ts:95-96` navigates with
  `queryParams: { reset: 'ok' }`; `login.ts:40` reads it into `passwordReset`; `login.html:12`
  branches on it. Nothing else touches it.
- **House style is settled and strict** (`shared/readonly-field/readonly-field.ts`,
  `shared/toast/toast.service.ts`): standalone components, `input.required<T>()`, `styleUrl`,
  signals throughout (the app is **zoneless** — `readonly-field.spec.ts:13` notes a plain field
  reassigned in a test never marks the host dirty), and a docblock that states *why* the component
  is shaped the way it is, not what it does.

## Desired End State

Every form control, loading state, empty state and list row in `app/**` is one component rather
than one copy per screen. Re-running the audit that opened this change returns zero on all six
families. A contributor who writes `<div class="field">` or a bare `<select>` inside
`app/features/**` gets a failing `npm run quality:check` naming the rule, and `AGENTS.md` explains
what to use instead.

Verify by: `npm run quality:check` and `npm test` pass; `grep -rn 'class="field"' src/app/src/app
--include=*.html` returns nothing; `grep -rn 'Wczytywanie' src/app/src/app --include=*.html`
returns nothing; a `<select>` added to any feature template without an `<app-select>` ancestor
fails lint.

## What We're NOT Doing

- **No UI library.** Angular Material would break both the eager-bundle budget and the existing
  design. Explicitly rejected.
- **No i18n.** Polish strings stay inline.
- **No `OnPush` sweep.** The app is zoneless; change-detection strategy is out of scope.
- **No backend change.** Not one file under `src/Domain`, `src/Application`, `src/Infrastructure`
  or `src/Api` is touched.
- **No churn of the button classes.** `.button`, `.button--secondary`, `.button--block` and
  `.link-button` are already consistent and stay exactly as they are.
- **No `ControlValueAccessor` on `app-checkbox`.** Both existing checkboxes are filter toggles, not
  form fields; no form in the app has a checkbox today. CVA is an additive change when one arrives.
- **No restoration of the bundle threshold.** It goes to 600 kB in Phase 1 and stays there — see
  "Open risks" in the brief; this was decided against the measure-first alternative.
- **No ESLint rule coverage of `app/shared/**` or `app/core/**`.** The boundary is structural: the
  kit lives in `shared/`, screens live in `features/`. `shared/` templates are still migrated in
  Phases 3 and 4, but nothing enforces them afterwards.

## Implementation Approach

Components are **paired with their migration**, not built up front. A phase that adds six
components with no callers cannot be verified by looking at the app, and leaves the tree in a state
where the old copies and the new kit both exist with nothing deciding between them. Each phase here
instead ends with a tree that is strictly better than the one before it, passes `quality:check` and
`npm test`, and is independently commitable.

Every component **projects** rather than owns. This is the load-bearing decision of the slice:
`plan-builder.html:36-78` and `profile.html:145-160` are the two shapes an owning component could
not hold, and leaving them behind would mean a kit that exists alongside a second, smaller copy the
rule could never forbid. Projection costs the guarantee that `aria-invalid` and `for`/`id` are
wired — which is precisely the gap the ESLint rule in Phase 5 exists to close.

The rule lands last and lands as `error`. Introduced earlier it would fail every not-yet-migrated
file, making every intermediate phase un-commitable and CI red throughout the slice.

## Critical Implementation Details

**Zoneless change detection.** Every spec host must hold its inputs in `signal()`, not plain
fields — a reassigned plain property never marks the host dirty and the assertion reads stale DOM.
`shared/readonly-field/readonly-field.spec.ts:13` states this and is the pattern to copy.

**Projected content binds in the caller's context.** `<app-select><select formControlName="x">` works
because content projection leaves the `<select>` in the calling template — `formControlName`,
`(change)`, `[disabled]` and `[attr.aria-invalid]` resolve against the caller's `formGroup` and
injector, not the component's. This is what makes the wrapper-only `app-select` contract free of
attribute passthrough. It also means the component cannot read or validate what was projected into
it.

**Ordering within Phase 3.** The `?reset=ok` query param must be removed from `reset-password.ts`
**and** `login.ts`/`login.html` in the same commit. Removing the producer alone leaves `login.ts:40`
reading a param that is never set — a silent dead branch; removing the consumer alone leaves a
navigation writing a param nobody reads.

---

## Phase 1: The global stylesheet foundation

### Overview

Move the three genuinely-copied classes into `src/styles.scss`, delete their 14 local copies,
correct the two comments that already claim the move happened, and raise the bundle threshold so no
later phase stalls on a build warning. No template changes at all — this phase is provably
invisible in the rendered app.

### Changes Required:

#### 1. The global stylesheet

**File**: `src/app/src/styles.scss`

**Intent**: Give `.empty`, `.section-title` and `.form-actions` a single home, beside the classes
S-19 already lifted here (`.page-header` at `:423`, `.overlay-*` at `:460`). Follow the commenting
convention those set: state how many copies collapsed and note any drift resolved in the process.

**Contract**: Three new top-level rules. `.empty` takes the shape all nine copies share
(`margin: 0; color: var(--muted)`). `.section-title` and `.form-actions` take the shape their 3 and
2 copies share, including `member-form.scss:27`'s `.form-actions a.button` descendant. Any
divergence found between copies is resolved to the majority value and named in the comment — the
`.page-header` comment at `styles.scss:423-435` is the precedent for how to record that.

#### 2. The nine `.empty` copies

**File**: `class-types.scss:73`, `exercise-detail.scss:68`, `exercises.scss:107`, `members.scss:169`,
`dashboard.scss:69`, `my-plan.scss:121`, `plan-exercise-detail.scss:54`, `plan-builder.scss:156`,
`plans.scss:66`

**Intent**: Delete each local `.empty` declaration now that the global one covers it.

**Contract**: Declaration removed. Where a file's `.empty` carried anything beyond
`margin`/`color`, that extra stays behind as a screen-local rule rather than being pushed into the
global class — a shared class that absorbs one screen's specifics becomes a second place to look
for that screen's markup, which is the failure `styles.scss:433` warns about for `.page-header`.

#### 3. The lying comments

**File**: `src/app/src/app/features/dashboard/dashboard.scss:1`,
`src/app/src/app/features/admin/members/member-passes.scss:1`

**Intent**: Both claim `.empty` comes from `src/styles.scss`. As of this phase that is true — the
comments become correct rather than being deleted.

**Contract**: Comment text updated to state the fact rather than assert it, or removed if the
global class needs no local pointer.

#### 4. `.section-title` and `.form-actions` copies

**File**: `member-form.scss:3,17,27`, `member-passes.scss:4,26`, `profile.scss:3`

**Intent**: Delete the local declarations.

**Contract**: Declarations removed; `.form-actions a.button` moves with `.form-actions`.

#### 5. The bundle threshold

**File**: `src/app/angular.json`

**Intent**: Raise the initial-bundle warning from 550 kB to 600 kB so that no phase in this slice
stalls on a build warning. Decided deliberately and against the measure-first alternative.

**Contract**: The `budgets` entry of the `build` target's production configuration — `maximumWarning`
`550kb` → `600kb`. The error ceiling (`1mb`) is untouched. `AGENTS.md`'s bundle paragraph is updated
in Phase 5 alongside the rest of the documentation, with the measured figure recorded there.

### Success Criteria:

#### Automated Verification:

- SPA builds: `npm run build` (from `src/app/`)
- Lint and format pass: `npm run quality:check`
- Specs pass: `npm test`
- No `.empty`, `.section-title` or `.form-actions` declaration remains outside `styles.scss`:
  `grep -rn '^\.empty\|^\.section-title\|^\.form-actions' src/app/src/app --include=*.scss` returns
  nothing

#### Manual Verification:

- Dashboard, members, exercises, class-types, plans, my-plan and plan-builder render their empty
  states exactly as before — this phase must be visually a no-op
- The `Zmiana hasła` heading on `/profile` and the section headings on the member forms keep their
  size and weight
- Form action rows on `/admin/members/:id` and the passes screen keep their spacing

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human before proceeding.

---

## Phase 2: The controls — `app-field`, `app-select`, `app-checkbox`, and their migration

### Overview

Three components, then every caller in the same phase. This phase closes defects 1, 2 and 5 and
removes all 56 `.field` copies.

### Changes Required:

#### 1. `app-field`

**File**: `src/app/src/app/shared/forms/field/field.ts` (+ `.scss` if the global `.field` is not
sufficient, + `.spec.ts`)

**Intent**: Hold the label-plus-control-plus-error block that exists 56 times. It **projects** the
control rather than owning it, so that the two irregular shapes — `plan-builder.html:36-78`'s
branching label and two error sources, and `profile.html:145-160`'s `server`/fallback pair — are
absorbed rather than left behind as a second copy.

**Contract**: Standalone component, selector `app-field`, rendering `<div class="field">` around
its projected content. It takes `label` and the control's `id` (for `for=`) as
`input.required<string>()` where the label is a plain string, and offers a projection slot for the
label where it is not — `plan-builder` needs an element, not a string. Errors and hints are ordinary
projected content. The component emits no `aria-invalid` and no validation logic; the caller keeps
both, and Phase 5's rule is what checks they are present. Docblock states why it projects, naming
both irregular callers.

**Adapted during implementation.** `label` and `for` are `input<string>()`, not
`input.required<string>()`. The plan's own sentence contains the contradiction: a field whose label
is projected into `[slot=label]` passes neither, so `required` would have made the one caller the
slot exists for fail at runtime. The slot is `[slot=label]` on the projected element, matched with
`<ng-content select="[slot=label]" />`.

#### 2. `app-select`

**File**: `src/app/src/app/shared/forms/select/select.ts` (+ `.spec.ts`)

**Intent**: Make the `.select` wrapper impossible to omit. The wrapper is the entire defect: it is
what carries the `::after` chevron, and five of seven selects in the app are missing it today.

**Contract**: Standalone component, selector `app-select`, whose template is the `.select` wrapper
and a single `<ng-content />`. The caller writes its own `<select>` inside it, keeping
`formControlName`, `id`, `(change)`, `[disabled]` and `[attr.aria-invalid]` untouched — projected
content binds in the caller's context, so no attribute passthrough exists or is needed. The
component adds no inputs. Its spec asserts the wrapper element is present and that a projected
`<select>` reaches the DOM unmodified.

#### 3. `app-checkbox`

**File**: `src/app/src/app/shared/forms/checkbox/checkbox.ts` (+ `.scss`, `.spec.ts`)

**Intent**: Give the app's only two checkboxes a styled, labelled control. Both are filter toggles
("pokaż nieaktywne"), not form fields.

**Contract**: Standalone component, selector `app-checkbox`, with `checked` as an input and a
`checkedChange` output — **no** `ControlValueAccessor`, and the docblock says why: no form in the
app has a checkbox, and CVA is additive when one arrives. The label is projected content, wired to
the input via a generated id so the association cannot be forgotten.

**Adapted during implementation.** No generated id: the `<input>` sits INSIDE the `<label>`, which
is what both hand-written copies already did. Implicit association is the stronger form of the same
guarantee — there is no id to duplicate, collide or forget — and it makes the whole row one hit
target, which is what the copied `cursor: pointer` had been promising. The component also sets
`accent-color: var(--accent)` on the box; that, not the missing class, is why these two read as
foreign: they were rendering in the browser's default blue.

#### 4. Migrate the 13 templates carrying `.field`

**File**: `exercise-form.html`, `plan-builder.html`, `profile.html`, `member-form.html`,
`class-form.html`, `member-passes.html`, `class-create-overlay.html`, `class-type-form.html`,
`reset-password.html`, `register.html`, `login.html`, `forgot-password.html`,
`class-bookings-overlay.html`

**Intent**: Replace every `<div class="field">` with `<app-field>`, importing it in each component's
`imports` array.

**Contract**: No `class="field"` remains in any template. Each component's `imports` gains `Field`.
Behaviour — including which control is focused, which errors show when, and every `aria-invalid`
expression — is unchanged; this is a markup move, not a behaviour change.

#### 5. Fix the five selects missing a wrapper

**File**: `class-create-overlay.html:36`, `class-create-overlay.html:46`,
`class-bookings-overlay.html:65`, `plan-builder.html:52`, and convert the two existing
`<span class="select">` wrappers at `class-form.html:33,111`

**Intent**: Wrap every `<select>` in `<app-select>`. Four of these gain a chevron they have never
had.

**Contract**: No raw `<span class="select">` remains. `class-form`'s explanatory comment at
`:30-32` is rewritten to point at the component instead of at the risk, since the risk is now
enforced.

#### 6. Delete `.field-label`

**File**: `class-create-overlay.html:35,45,60,72`

**Intent**: Remove the undefined class. Each of the four becomes `app-field`'s label.

**Contract**: The string `field-label` appears nowhere in the repository afterwards. Where the
control has no stable `id` to point a `for=` at, one is added — `class-create-overlay`'s selects
currently have none.

#### 7. Migrate the two checkboxes

**File**: `class-types.html:14`, `exercises.html:13`

**Intent**: Replace the bare inputs with `<app-checkbox>`.

**Contract**: `[checked]` / `(checkedChange)` replace `[checked]` / `(change)`; the toggle methods
on both components are unchanged.

### Success Criteria:

#### Automated Verification:

- Build, lint and specs pass: `npm run build`, `npm run quality:check`, `npm test` (from `src/app/`)
- `grep -rn 'class="field"' src/app/src/app --include=*.html` returns nothing
- `grep -rn 'field-label\|class="select"' src/app/src/app --include=*.html --include=*.scss`
  returns nothing
- `grep -rn 'type="checkbox"' src/app/src/app --include=*.html` returns nothing outside
  `shared/forms/checkbox/`
- New specs for `app-field`, `app-select` and `app-checkbox` pass

#### Manual Verification:

- All five previously-bare selects now show the chevron: the two on
  `/admin/classes` → new-class overlay, the one on the bookings overlay, and the member picker in
  the plan builder
- The class form's type select still disables on edit and its chevron dims (`styles.scss:320`)
- The four labels in the new-class overlay now match every other label in the app
- Every form still shows its errors on the same controls at the same moments — check `/login` with
  a bad address, `/profile`'s password form with a wrong current password (the `server` error
  branch), and the plan builder with no member chosen
- The "pokaż nieaktywne" toggles on `/admin/class-types` and `/admin/exercises` still filter, and
  their labels are clickable

**Implementation Note**: Pause for manual confirmation before Phase 3. Re-measure the eager bundle
here (`npm run build` prints it) and record the figure in the plan.

---

## Phase 3: The states — `app-loading`, `app-empty`, and success on the toast

### Overview

Two components plus the success migration. This phase closes defect 4 and removes the 22 loading
copies. After it, `.notice` carries one meaning instead of four.

### Changes Required:

#### 1. `app-loading`

**File**: `src/app/src/app/shared/forms/loading/loading.ts` (+ `.spec.ts`)

**Intent**: Hold the loading line that exists 22 times, **with the word inside the component**. It
takes no message input: an input that exists will be used, and three screens later there are four
different sentences for the same state — the exact defect this slice removes.

**Contract**: Standalone component, selector `app-loading`, no inputs, rendering
`<p class="notice" role="status">Wczytywanie…</p>`. Docblock states why there is no input.

#### 2. `app-empty`

**File**: `src/app/src/app/shared/forms/empty/empty.ts` (+ `.spec.ts`)

**Intent**: Hold the empty state. Unlike loading, it **projects** its content — each of the empty
states says something different, and two of them carry a link to somewhere else.

**Contract**: Standalone component, selector `app-empty`, rendering `<p class="empty">` around
`<ng-content />`. No inputs. Its spec asserts projected links survive.

#### 3. Migrate the 22 loading copies

**File**: the 18 templates listed in Current State Analysis, `shared/calendar/schedule-calendar.html`
included

**Intent**: Replace each with `<app-loading />`.

**Contract**: `grep -rn 'Wczytywanie' src/app/src/app --include=*.html` returns nothing. The
dashboard's four independent loads each keep their own `app-loading` — the four load fences
(`AGENTS.md`: "the dashboard's four cards hold four, and must keep holding four") are untouched.

#### 4. Migrate the empty states

**File**: the 11 templates carrying `class="empty"`, plus `dashboard.html:17`,
`my-classes.html:22` and `class-bookings-overlay.html:30`

**Intent**: Replace `<p class="empty">` with `<app-empty>`, and convert the three `.notice` uses
that are empty states wearing the wrong class.

**Contract**: No `class="empty"` remains in a template. The three converted `.notice` uses keep
their projected links (`dashboard.html:18` → `/schedule`, `my-classes.html:24` → `/schedule`).
`my-classes.html:20`'s comment about an empty state that leads somewhere stays — it explains the
projection.

#### 5. Success becomes a toast

**File**: `profile.html:16`, `profile.html:137`, `profile.ts:68,117,132,152,160`

**Intent**: Replace the `saved` and `passwordChanged` inline notices with `toast.success(...)`,
routing both through the third outlet as `AGENTS.md`'s failure rule already prescribes.

**Contract**: `saved` and `passwordChanged` signals are deleted along with their template branches;
`ToastService` is injected and called on each success path. The two forms stay independent —
`profile.html:128`'s comment explains why, and a toast does not change that.

#### 6. Remove the `?reset=ok` plumbing

**File**: `reset-password.ts:95-96`, `login.ts:40`, `login.html:12-16`

**Intent**: `reset-password` raises `toast.success(...)` before navigating; the query param, the
`passwordReset` field and the template branch all go. The toast host is outside `router-outlet`
(`app.html:63`), so the toast survives the navigation.

**Contract**: `queryParams: { reset: 'ok' }` is dropped from the navigate call; `passwordReset`
and its `@if` block are deleted. The string `reset` as a query param appears nowhere. Both edits
land in **one commit** — the producer and consumer must not be split, or one half becomes a silent
dead branch. `login.spec.ts` is checked for a reference and updated if one exists.

### Success Criteria:

#### Automated Verification:

- Build, lint and specs pass: `npm run build`, `npm run quality:check`, `npm test`
- `grep -rn 'Wczytywanie\|class="empty"' src/app/src/app --include=*.html` returns nothing
- `grep -rn 'passwordReset\|reset.*ok' src/app/src/app --include=*.ts --include=*.html` returns
  nothing
- New specs for `app-loading` and `app-empty` pass
- `login.spec.ts` still pins the form-banner rule for login (it must not regress to a field error)

#### Manual Verification:

- Every screen still shows "Wczytywanie…" while it loads — check the dashboard's four cards load
  independently, one slow card not blanking the others
- `/profile`: saving contact details raises a success toast that auto-dismisses; changing the
  password raises a second one; a rejected postal code still does **not** disable the password
  button
- Resetting a password end to end: the success toast appears **after** the redirect to `/login` and
  is readable there
- `/my-classes` and the dashboard with no bookings still show the empty state **with** its link to
  the schedule
- The four remaining `.notice` uses (`member-passes`, `forgot-password`, `reset-password`,
  `plan-builder` item limit) are unchanged

**Implementation Note**: Pause for manual confirmation before Phase 4.

---

## Phase 4: The list — `app-list` / `app-row` and their migration

### Overview

The most variable of the six families, and the one where the recipe is identical but the names are
not. Four projection slots, no inputs.

### Changes Required:

#### 1. `app-list` and `app-row`

**File**: `src/app/src/app/shared/list/list.ts`, `src/app/src/app/shared/list/row.ts`
(+ `.scss`, `.spec.ts`)

**Intent**: Hold the `<ul>` and the `<li class="card *-row">` flex recipe that is identical to the
character in `exercises.scss:21`, `class-types.scss:23` and `plans.scss:26`.

**Adapted during implementation, in four ways.**

1. **`app-row` is `li[appRow]`, an ATTRIBUTE selector.** As an element it would have produced
   `ul > app-row`, and a `<ul>` admits nothing but `<li>` — the list stops reporting its length to
   anyone reading it aloud. Written `<li appRow>` the host element is the list item itself.
2. **`eslint.config.js`'s `component-selector` rule gained a second entry** (attribute /
   camelCase) so that selector passes lint. Elements are unchanged; the attribute spelling follows
   the camelCase this file already requires of `directive-selector`.
3. **`card` stays the caller's class**, not the component's: six of the seven rows want it and the
   bookings list, which sits inside an overlay panel that is already a card, does not.
4. **Two `*-row` classes deliberately survive**, so criterion 4.2 is read as "no copy of the
   recipe remains" rather than literally. `bookings-row` carries a bottom border, a tighter gap and
   a `:last-child` rule — a real per-screen difference, not a copy — and `passes-row--current` is a
   state modifier that was never part of the recipe. Keeping them is the same call `.page-header`
   records: only the layout is shared.

A fifth thing had to be MEASURED rather than assumed, and `list.spec.ts` now pins it: every
per-row failure line in the app sits inside an `@if`, and a control-flow block is not a static
element a projection selector can match. `slot="foot"` does survive the block — the element lands
as a direct child of the `<li>`, not inside `.row-identity`. Had it not, six screens would have
silently moved their error line up beside the name.

**Contract**: `app-list` renders `<ul class="list">` around `<ng-content />`. `app-row` renders
`<li class="card row">` and offers **four** projection slots and no inputs: `[slot=lead]`
(optional leading media), default content (the identity block — name, badges, meta lines),
`[slot=actions]`, and `[slot=foot]` (the optional trailing error line). The component does not know
what is inside it and enforces nothing; Phase 5's rule checks the naming. `justify-content:
space-between` — the single point on which the three recipes differ — is resolved one way in the
shared rule and the losing screen keeps a local override if it needs one, recorded in a comment.

#### 2. Migrate the seven list screens

**File**: `class-types.html`, `exercises.html`, `plans.html`, `members.html`, `my-classes.html`,
`member-passes.html`, `class-bookings-overlay.html` (and their `.scss` siblings)

**Intent**: Replace each hand-rolled `<ul>`/`<li>` pair with `<app-list>`/`<app-row>`, and collapse
the seven prefixes into the shared slot names.

**Contract**: `exercises`' fixed 16:9 thumbnail box moves into the `lead` slot but keeps its own
sizing rule in `exercises.scss` — `exercises.scss:29`'s comment (a late-arriving thumbnail must not
reflow the list) still applies and moves with it. `class-types`' and `exercises`' badges,
`members`' row menu, `member-passes`' `passes-row-main` split and `plan-builder`'s drag handle all
enter as ordinary projected content. Per-screen `-name` / `-meta` / `-actions` rules are deleted
where the shared ones cover them and kept, renamed, where they carry something real.

### Success Criteria:

#### Automated Verification:

- Build, lint and specs pass: `npm run build`, `npm run quality:check`, `npm test`
- `grep -rn 'class="card [a-z-]*-row"\|class="[a-z-]*-row' src/app/src/app --include=*.html`
  returns nothing
- New specs for `app-list` and `app-row` pass, including one asserting all four slots project

#### Manual Verification:

- `/admin/exercises`: thumbnails keep their fixed box, a slow image does not reflow the list, and
  the "Nieaktywne" badge sits where it did
- `/admin/class-types`, `/admin/members`, `/trainer/plans`: rows wrap the same way at phone width
- `/admin/members/:id` passes list and the bookings overlay render unchanged
- `/my-classes` rows unchanged
- The per-row failure line (`class-types.html`'s `field-error` after the actions) still appears in
  the right place, and one busy row still does not disable its neighbours

**Implementation Note**: Pause for manual confirmation before Phase 5.

---

## Phase 5: Enforcement — the ESLint rule and the written rule

### Overview

The anchor of the slice. The tree is clean by now, so the rule lands directly as `error` and no
intermediate phase was ever red.

### Changes Required:

#### 1. Declare the rule-authoring dependencies

**File**: `src/app/package.json`

**Intent**: `@angular-eslint/utils`, `@angular-eslint/bundled-angular-compiler` and
`@typescript-eslint/utils` are present in `node_modules` **transitively only**. A rule that imports
them needs them declared, or a future dedupe silently breaks lint. `@angular-eslint/test-utils` is
absent and is needed for RuleTester.

**Contract**: Four `devDependencies` added at the versions already resolved (22.2.0 for the three
angular-eslint packages, 8.68.0 for `@typescript-eslint/utils`), pinned in the style the file
already uses for `angular-eslint` and `typescript-eslint` (exact, not caret).

#### 2. The rule

**File**: `src/app/tools/eslint-rules/no-hand-rolled-presentational.js` (CommonJS — `eslint.config.js`
uses `require`, and a plain-JS rule needs no build step)

**Intent**: Fail lint when a template under `app/features/**` hand-rolls one of the six families.

**Contract**: An ESLint rule built with `ESLintUtils.RuleCreator.withoutDocs` and
`getTemplateParserServices(context)`, reporting via
`parserServices.convertNodeSourceSpanToLoc(node.sourceSpan)`. It carries one `messageId` per family,
each naming the component to use instead. The checks:

- `Element[name="select"]` with no `app-select` ancestor → `useAppSelect`
- `Element[name="input"]` carrying `type="checkbox"` → `useAppCheckbox`
- any element with `class` containing the token `field`, `empty`, `select` or a `*-row` suffix →
  the matching `useAppX` message
- any text node matching `Wczytywanie` → `useAppLoading`

The ancestor check is what makes the projected-`<select>` contract enforceable, and it is the one
part of the rule that a regex-over-HTML scan could not have done — which is why the rule is an AST
rule and not a spec.

#### 3. Wire it into the config

**File**: `src/app/eslint.config.js`

**Intent**: Register the rule as an inline plugin, scoped to feature templates only.

**Adapted during implementation.** Two pieces of test plumbing the plan did not foresee, both
because the rule's spec lives outside `src/`:

- `angular.json`'s `test` target gained an explicit `include`
  (`['**/*.spec.ts', '../tools/**/*.spec.ts']`). The unit-test builder globs from `src/` and would
  never have discovered the rule's spec — it would have sat in the tree passing by never running.
- `tsconfig.spec.json` gained `tools/**/*.spec.ts` to its `include` and `"node"` to its `types`,
  because the spec loads the rule with `require()` — which is how the linter itself loads it.

Also: criterion 5.5 (`npm ci` from a clean `node_modules`) was verified without deleting
`node_modules`, which the environment refused as destructive. Checked instead that all four
packages are declared at exact versions in `package.json` and present in `package-lock.json` as
`dev` — the two facts `npm ci` would rely on.

**Contract**: A new config block with `files: ['src/app/features/**/*.html']`, a `plugins` entry
holding the required rule module, and the rule set to `'error'`. The existing `**/*.html` block is
left as it is. `shared/` and `core/` are deliberately outside the scope — the kit lives in
`shared/`, and the boundary is structural rather than a maintained exemption list.

#### 4. The rule's own spec

**File**: `src/app/tools/eslint-rules/no-hand-rolled-presentational.spec.ts`

**Intent**: The rule is the only code in the project that can block every merge. It gets negative
cases, not just positive ones — nothing otherwise proves it does **not** fail correct code.

**Contract**: `RuleTester` from `@angular-eslint/test-utils`, with `valid` cases covering a
`<select>` inside `<app-select>`, the word `Wczytywanie` inside an HTML comment, and a class
attribute containing `fieldset` or `battlefield` (the substring trap), and `invalid` cases for each
of the six families. Runs under the existing `npm test`.

#### 5. The written rule

**File**: `AGENTS.md`

**Intent**: A failing lint message must send the reader somewhere that explains the decision, the
way S-19's `failure-contract.spec.ts` sends them to the four-outlet rule.

**Contract**: A new subsection in the "Style" section, in the register of the existing
"Shared shapes, not copied ones (S-19)" block: the six components, what each is for, that they
project rather than own and why, the `features/` vs `shared/` enforcement boundary, and that
`.notice` now carries exactly one meaning. The bundle paragraph is updated to state the 600 kB
threshold and the figure measured at the end of this slice.

#### 6. The project pointer

**File**: `CLAUDE.md`

**Intent**: Keep the pointer file pointing.

**Contract**: One line in the frontend rules referring to the new `AGENTS.md` subsection. No
duplication of its content.

### Success Criteria:

#### Automated Verification:

- Lint passes on the clean tree: `npm run quality:check`
- Build and specs pass: `npm run build`, `npm test`
- The rule's own spec passes, negative cases included
- The rule actually fires: adding `<div class="field"></div>` to any feature template makes
  `npm run quality:check` fail naming the rule; reverting makes it pass
- `npm ci` from a clean `node_modules` succeeds with the four new devDependencies

#### Manual Verification:

- The failing message reads as an instruction ("use `<app-field>`"), not as a code
- `AGENTS.md`'s new subsection is accurate against the shipped components — every name, slot and
  boundary it states is checked against the code
- Final eager-bundle figure recorded in `AGENTS.md` and in this plan
- The audit that opened this change returns zero on all six families

**Implementation Note**: This is the last phase. Confirm the audit result with the human before
closing.

---

## Testing Strategy

### Unit Tests:

- One spec per kit component, in the house style: a signal-holding host component (zoneless — a
  plain reassigned field never marks the host dirty), and assertions about the **reason the
  component exists** rather than about its markup. `readonly-field.spec.ts` is the model, including
  its "THE REASON THIS EXISTS" test.
- `app-select`'s key assertion: a projected `<select>` reaches the DOM unmodified and inside the
  `.select` wrapper.
- `app-loading`'s key assertion: no input exists by which a caller could change the word.
- `app-row`'s key assertion: all four slots project, and an absent slot renders nothing.
- The ESLint rule's `RuleTester` spec, with valid cases as the point — the substring traps
  (`fieldset`, `battlefield`) and the HTML-comment case are what keep it from failing correct code.

### Integration Tests:

No new backend or contract tests. `login.spec.ts` must keep pinning login's form-banner rule
through Phase 3's toast migration — a field-level "no such account" discloses which e-mails exist,
and that pin is what stops it.

### Manual Testing Steps:

1. Every screen in the app is opened once after Phases 2, 3 and 4 — the slice touches ~30 templates
   and its whole claim is that nothing looks different.
2. The five previously-bare selects are checked for their chevron (the one user-visible improvement
   the slice does make).
3. Password reset is run end to end, confirming the success toast survives the redirect.
4. Phone width is checked on the seven list screens and the eight page headers.
5. A deliberate violation is added and reverted to prove the rule fires.

## Performance Considerations

The kit lands in `login`, `register` and the dashboard, all eager by design. The threshold moves to
600 kB in Phase 1 rather than after a measurement, so the build will not stall — but the figure is
measured at the end of Phase 2 and again at the end of Phase 5 and recorded, because the last
measured number (512.42 kB after S-19) is the only thing that makes the next such decision
evidence-based. Six small standalone components with no external dependencies should cost single
-digit kB; a materially larger jump is a finding, not a rounding error.

## Migration Notes

No data migration. No backend change. Every phase is independently revertable: Phases 1-4 are pure
markup and stylesheet moves, and Phase 5 is additive except for the `package.json` entries.

## References

- Change identity and audit: `context/changes/frontend-presentational-kit/change.md`
- Roadmap item: `context/foundation/roadmap.md` § S-23
- Prerequisite slice: `context/archive/2026-09-19-frontend-error-and-patterns/`
- The four-outlet failure rule: `AGENTS.md` § "How a failure reaches the user (S-19)"
- The enforcement precedent: `src/app/src/app/core/http/failure-contract.spec.ts`
- The component house style: `src/app/src/app/shared/readonly-field/readonly-field.ts`
- Custom template rule authoring:
  https://github.com/angular-eslint/angular-eslint/blob/main/docs/WRITING_CUSTOM_RULES.md

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not
> rename step titles. See `references/progress-format.md`.

### Phase 1: The global stylesheet foundation

#### Automated

- [x] 1.1 SPA builds: `npm run build` — 18fa4ef
- [x] 1.2 Lint and format pass: `npm run quality:check` — 18fa4ef
- [x] 1.3 Specs pass: `npm test` — 18fa4ef
- [x] 1.4 No `.empty`, `.section-title` or `.form-actions` declaration remains outside `styles.scss` — 18fa4ef

#### Manual

- [ ] 1.5 Empty states render exactly as before across all seven screens — visually a no-op
- [ ] 1.6 Section headings keep their size and weight
- [ ] 1.7 Form action rows keep their spacing

### Phase 2: The controls — `app-field`, `app-select`, `app-checkbox`, and their migration

#### Automated

- [x] 2.1 Build, lint and specs pass — 9fab4f3
- [x] 2.2 No `class="field"` remains in any template — 9fab4f3
- [x] 2.3 No `field-label` or `class="select"` remains — 9fab4f3
- [x] 2.4 No bare `type="checkbox"` outside `shared/forms/checkbox/` — 9fab4f3
- [x] 2.5 New specs for `app-field`, `app-select` and `app-checkbox` pass — 9fab4f3

#### Manual

- [ ] 2.6 All five previously-bare selects now show the chevron
- [ ] 2.7 The class form's type select still disables on edit and its chevron dims
- [ ] 2.8 The four new-class-overlay labels match every other label in the app
- [ ] 2.9 Every form shows its errors on the same controls at the same moments
- [ ] 2.10 The two "pokaż nieaktywne" toggles still filter and their labels are clickable
- [ ] 2.11 Eager bundle re-measured and recorded

### Phase 3: The states — `app-loading`, `app-empty`, and success on the toast

#### Automated

- [x] 3.1 Build, lint and specs pass — bde09dd
- [x] 3.2 No `Wczytywanie` or `class="empty"` remains in any template — bde09dd
- [x] 3.3 No `passwordReset` or `reset=ok` plumbing remains — bde09dd
- [x] 3.4 New specs for `app-loading` and `app-empty` pass — bde09dd
- [x] 3.5 `login.spec.ts` still pins login's form-banner rule — bde09dd

#### Manual

- [ ] 3.6 Loading shows on every screen; the dashboard's four cards still load independently
- [ ] 3.7 `/profile` raises a success toast for each form, and the two forms stay independent
- [ ] 3.8 Password reset end to end: the toast appears after the redirect and is readable
- [ ] 3.9 Empty states keep their links to the schedule
- [ ] 3.10 The four remaining `.notice` uses are unchanged

### Phase 4: The list — `app-list` / `app-row` and their migration

#### Automated

- [x] 4.1 Build, lint and specs pass — f492796
- [x] 4.2 No `*-row` class remains in any template — f492796
- [x] 4.3 New specs for `app-list` and `app-row` pass, all four slots asserted — f492796

#### Manual

- [ ] 4.4 Exercises thumbnails keep their fixed box and badge placement
- [ ] 4.5 Rows wrap the same way at phone width on all seven screens
- [ ] 4.6 Passes list and bookings overlay render unchanged
- [ ] 4.7 Per-row failure line still appears in place; one busy row does not disable its neighbours

### Phase 5: Enforcement — the ESLint rule and the written rule

#### Automated

- [x] 5.1 Lint passes on the clean tree
- [x] 5.2 Build and specs pass
- [x] 5.3 The rule's own spec passes, negative cases included
- [x] 5.4 The rule fires on a deliberate violation and stops on revert
- [x] 5.5 `npm ci` from a clean `node_modules` succeeds with the four new devDependencies

#### Manual

- [ ] 5.6 The failing message reads as an instruction, not a code
- [ ] 5.7 `AGENTS.md`'s new subsection is accurate against the shipped components
- [ ] 5.8 Final eager-bundle figure recorded in `AGENTS.md` and in this plan
- [ ] 5.9 The opening audit returns zero on all six families
