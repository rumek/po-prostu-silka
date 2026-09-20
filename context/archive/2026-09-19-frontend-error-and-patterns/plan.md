# A failure is told one way, and the copied patterns are extracted once — Implementation Plan

## Overview

The SPA gains one classification of failure, one factory for message tables, one toast
component, and a written rule with four outlets that decides which of them a given failure
gets. All seventeen `*Failure` unions get a table; roughly thirty screens migrate onto the
rule area by area; and seven families of copied blocks are extracted once.

This is roadmap slice **S-19** (`context/foundation/roadmap.md:533-559`), carrying M-6
anchors CS-04, CS-05, CS-06 and CS-07. Nothing a member or an admin can *do* changes — some
of the words they read do.

## Current State Analysis

From `context/changes/frontend-error-and-patterns/research.md` (full inventory at commit
`c61fba5`), plus a shell/styles survey done during planning:

- **17 failure unions, 3 message tables.** `BookingFailure`, `ClassFailure` and
  `MembershipPassFailure` have an exhaustive `Record` keyed by the union — the only mechanism
  in the codebase that turns a server vocabulary change into a *build* failure. The other
  fourteen are hand-written `switch` statements with a `default:` inside whichever component
  calls the endpoint.
- **Nine display mechanisms**, at least ten independently worded generic fallbacks, and
  `conflict` alone handled four different ways.
- **The three good tables are bypassed at field level.** `class-form.ts:274-309` states the
  rule ("the WORDS come from `classFailureMessage`") and follows it only on its banner
  branches, producing two different Polish sentences for one `time_conflict`
  (`class-failure.ts:34` vs `class-form.html:76-79`).
- **No 403 / 429 / 500 / offline branch exists anywhere.** The API emits 429
  (`src/Api/Program.cs:150`) and 503 (`GetVapidKey.cs:11`), and has **no exception
  middleware at all** (`src/Application/Auth/ForgotPassword.cs:91-96` says so outright), so
  an unhandled 500 arrives in a shape nothing on the client is written for.
- **One live contract drift**: `AccessCodeFailure` (`member-admin.models.ts:194-196`) omits
  `member_blocked`, which `src/Application/Members/IssueAccessCode.cs:61` emits with a 409.
  The screen renders the right sentence anyway because `members.ts:467-476` types its
  parameter `string | undefined`.
- **No z-index scale.** Three uncoordinated literals — skip-link `1` (`app.scss:17`),
  bottom-nav `5` (`bottom-nav.scss:23`), and `10` shared by the three overlays and the row
  menu (`members.scss:149`). `bottom-nav.scss:1-12` coordinates them by comment alone:
  *"anything added above this bar must declare a value above 10."* Neither `App :host` nor
  `.shell-main` opens a stacking context, so everything resolves at the document root.
- **No timer-driven UI exists.** Zero `setTimeout`, zero `@keyframes` app-wide; the only CSS
  transition is CDK drag-reorder in `plan-builder.scss:200-209`, and it is the only
  `prefers-reduced-motion` guard in the tree. A toast would be the first self-dismissing
  element in this app.
- **The roadmap's page-header count is wrong.** CS-06 says "three copies plus three
  near-identical stylesheets". There are **eight** in two families: six list headers
  (`members.scss:5-15` — the one drift, `--space-3` instead of `--space-4` —
  `exercises.scss:4-14`, `class-types.scss:4-14`, `plans.scss:4-14`,
  `plan-builder.scss:4-14`, `classes.scss:7-17`) and two detail headers
  (`exercise-detail.scss:3-18`, `plan-exercise-detail.scss:5-20`).
- **`@angular/cdk` is already a direct dependency** (`src/app/package.json:20`), used only by
  `drag-drop` in the lazy `plan-builder` chunk (`plan-builder.ts:8`).
- **The three overlays are a fourth uncatalogued triplet**: `.overlay-backdrop`,
  `.overlay-panel`, `.overlay-title` and a local `.link-button` reset duplicated near-verbatim
  across `class-details-overlay.scss`, `class-bookings-overlay.scss`,
  `class-create-overlay.scss`, which the code itself admits in comments. None of the three
  traps or moves focus.

## Desired End State

A developer adding a screen has exactly one decision to make about failure — *which of the
four outlets does this failure take?* — and the rule answers it. Concretely:

- One function classifies every `HttpErrorResponse`; nothing unwraps `.error.reason` by hand.
- Every one of the seventeen unions has an exhaustive table built by one factory, so adding a
  server reason without a Polish sentence fails `npm test` — and, for the unions that carry a
  `satisfies` array, fails the build.
- A failure surfaces as a field error, a form banner, a toast, or a screen state, and nothing
  else. `notice()` is gone, replaced by three explicit tones.
- A 429, a 500 and an offline browser each read as themselves rather than as an unrecognised
  business refusal.
- `reject()`, the four-signal form block, the generation counter, `setBusy`/`isBusy`, the
  page-header block and the overlay chrome each exist once.
- The initial bundle is still under the 550 kB warning threshold, or the number is moved
  deliberately with the reason recorded in `AGENTS.md`.

**Verification**: `npm test` and `npm run quality:check` green from `src/app/`; `dotnet test`
green from the repo root (unchanged — this slice touches no backend code); the contract spec
from Phase 3 passing over all seventeen unions; `npm run build` reporting the initial bundle
against 550 kB.

### Key Discoveries

- `core/scheduling/booking-failure.ts` is the reference recipe, and its `Object.hasOwn` guard
  is a review finding, not a stylistic choice — `reason in MESSAGES` would return
  `MESSAGES['constructor']`, a *function*, typed as `string`
  (`context/archive/2026-09-02-schedule-calendar-view/reviews/impl-review.md:169-174`).
- **Audience, not endpoint, is what forks a message table.** S-14 built a second booking
  mapper because one table addressed the member in the second person and an admin acts for a
  third party (`context/archive/2026-09-07-member-entity-and-accountless-members/plan.md:339-343`);
  S-16 retired the split when self-booking went away.
- **Login is banner-only on purpose** so it does not disclose which field was wrong
  (`context/archive/2026-09-05-member-profile-edit/research.md:253-254`), and
  `login.spec.ts:104-108` pins it. This survives the migration untouched.
- **`membership-pass-failure.ts:9-12` documents a symbol that no longer exists**
  (`ADMIN_MESSAGES`, retired in S-16). The comment goes when the file moves onto the factory.
- `--bottom-nav-tab-height` (`styles.scss:115-119`) is already global precisely because two
  stylesheets drifted on it once. The same argument applies to z-index.
- The post-edit hook `.claude/hooks/post-edit-spa.sh` runs Prettier, ESLint and the edited
  file's colocated spec for `core/` and `shared/` — so foundation work gets feedback per edit.

## What We're NOT Doing

- **No backend change of any kind.** No route moves, no `reason` added or renamed, no status
  code changed, no exception middleware added, no `ProblemDetails` adoption (CS-03, CS-07).
  The missing 500 shape is recorded as a finding, not fixed here.
- **No UI library** (CS-07). `@angular/cdk/a11y` is used for `LiveAnnouncer` only; it is an
  existing dependency, not a new one, and `cdk/overlay` is deliberately declined.
- **No i18n machinery** (D9). Every sentence stays a hardcoded Polish literal.
- **No `ChangeDetectionStrategy.OnPush` sweep**, no move to `resource()`/`httpResource()`, no
  component stores. The SPA's settled patterns stay settled.
- **Not removing `pending_approval`** from `LoginFailureReason` — documented dead on both
  sides and deliberately kept.
- **No route lazy/eager change**, even though `Members`, `ClassForm`, `ClassTypes` and
  `ClassTypeForm` are eager against AGENTS.md's claim (`app.routes.ts:7-13`). Recorded, not
  touched.
- **No new spec coverage for its own sake.** `member-passes.ts` has no spec today; it gets one
  only because Phase 5 changes its error handling.

## Implementation Approach

Build the toolkit before anything consumes it, write the rule before the component that would
otherwise become a tenth mechanism, then migrate screens area by area so each phase is
independently revertible and independently green. The extractions that are *not* about errors
come last, because touching them earlier would mean touching the same forms twice.

Two decisions shape everything downstream:

1. **The classified unwrap.** One function turns any thrown value into
   `{ kind, reason?, status }`, where `kind` separates a business refusal from auth, not-found,
   conflict, rate-limiting, a server fault and an offline browser. Screens branch on `kind`
   instead of inferring from `reason === undefined`.
2. **The table factory.** Each union keeps its own `Record` — that is what buys build-time
   exhaustiveness — but the repeated scaffolding (the `UNKNOWN` fallback and the
   `Object.hasOwn` guard) lives in one function. Fourteen new tables, one implementation.

## Critical Implementation Details

**Z-index and the bottom nav.** The toast host must declare a value above `10`
(`bottom-nav.scss:1-12`), and on phone width it must clear the fixed bottom bar, which is
`position: fixed` with `margin: 12px 20px` and `padding-bottom: env(safe-area-inset-bottom)`
(`bottom-nav.scss:18-23`). Introduce the z-index scale as tokens in `styles.scss` and convert
the four existing literals in the same phase — a fifth uncoordinated literal is the exact
failure mode `--bottom-nav-tab-height` was created to prevent.

**Motion.** This app has no animation conventions and exactly one `prefers-reduced-motion`
guard. A toast that appears and dismisses needs both a transition and that guard; without the
guard it is the app's first accessibility regression of that class.

**Announcement, not just rendering.** A toast that renders but is not announced is invisible
to a screen reader — and unlike the existing `role="alert"` banners, it disappears. Use
`LiveAnnouncer` from `@angular/cdk/a11y` for the announcement and keep the visual host
`aria-hidden`, rather than relying on a live region the user may never reach.

## Phase 1: Foundation — classification and the table factory

### Overview

The two functions everything else consumes, proven by migrating the three existing tables onto
the factory. No screen changes in this phase.

### Changes Required:

#### 1. The classified unwrap

**File**: `src/app/src/app/core/http/failure.ts` (new)

**Intent**: Replace the ~20 hand-written `((e as HttpErrorResponse)?.error as X)?.reason`
unwraps with one function that also says *what kind* of failure this was, so a 429, a 500 and
an offline browser stop reading as an unrecognised business refusal.

**Contract**: A discriminated result other phases branch on. This signature is load-bearing —
every screen and both later helpers depend on it:

```ts
export type FailureKind =
  | 'business'    // a refusal the API named: 4xx with a { reason } body
  | 'auth'        // 401/403 the interceptor did not already handle
  | 'notFound'    // 404 with no reason body
  | 'rateLimited' // 429
  | 'server'      // 5xx, including the shapeless unhandled 500
  | 'offline'     // the request never reached the server
  | 'unknown';

export interface FailureInfo {
  kind: FailureKind;
  reason?: string;   // present only when kind === 'business'
  status?: number;   // absent when the request never completed
}

export function classifyFailure(error: unknown): FailureInfo;
```

`conflict` is deliberately **not** a `kind`: a 409 always carries a `reason` and is therefore
`business`. Screens that refetch on conflict branch on the reason, as they do today.

#### 2. The table factory

**File**: `src/app/src/app/core/http/failure-messages.ts` (new)

**Intent**: Keep the per-union `Record` that buys build-time exhaustiveness while writing the
fallback and the `Object.hasOwn` guard exactly once.

**Contract**: A generic factory returning the lookup function. Callers pass a `Record` keyed by
their own reason union, so an unmapped reason is still a compile error at the call site:

```ts
export function createFailureMessages<R extends string>(
  messages: Record<R, string>,
  unknown: string,
): (reason: unknown) => string;
```

The returned function keeps today's semantics verbatim: takes `unknown` so a server one
version ahead cannot produce `undefined`, and guards with `Object.hasOwn` rather than `in`.

#### 3. Transport messages

**File**: `src/app/src/app/core/http/transport-messages.ts` (new)

**Intent**: Give the non-business kinds their own Polish sentences, so the gap the research
found ("a 403 from a stale permission and a 500 from the server read identically to an
unrecognised business refusal") closes at the source.

**Contract**: A `Record<Exclude<FailureKind, 'business'>, string>` and a
`transportMessage(info: FailureInfo): string | null` returning `null` for `business` so callers
fall through to the union's own table.

#### 4. The three existing tables move onto the factory

**Files**: `core/scheduling/booking-failure.ts`, `core/scheduling/class-failure.ts`,
`core/admin/membership-pass-failure.ts`

**Intent**: Prove the factory against the three tables that already work, and delete three
copies of the guard. Drop the stale `ADMIN_MESSAGES` paragraph from
`membership-pass-failure.ts:9-12` while the file is open.

**Contract**: Each file keeps its exported `xxxFailureMessage` name and its exact message
strings — the existing specs must pass unchanged, which is the point of doing this first.

### Success Criteria:

#### Automated Verification:

- Unit tests pass: `npm test` from `src/app/`
- The three existing failure specs pass **without modification**
- New specs cover `classifyFailure` for each `FailureKind`, including a `ProgressEvent`
  (offline) and a body-less 500
- Lint and format pass: `npm run quality:check` from `src/app/`

#### Manual Verification:

- No screen behaves differently — this phase changes no call site

---

## Phase 2: The rule, the toast, and the form-state helper

### Overview

Write the rule down before building the component it governs, then build the toolkit the
migration phases consume.

### Changes Required:

#### 1. The rule, written down

**File**: `AGENTS.md`

**Intent**: CS-04 puts the rule, not the component, at the centre — a toast without it is only
a tenth mechanism. The rule must live where the next contributor reads it, not in a comment in
the toast.

**Contract**: A new subsection under Style naming four outlets and the test for each:

1. **Field error** — the refusal names a control the user can correct on the form in front of
   them. The *words* come from the union's table; the form decides only which control.
2. **Form banner** (`.alert` + `role="alert"`, inside the form) — the refusal concerns the
   submission as a whole, names no single control, or naming one would disclose something.
   *Login is permanently in this bucket and never in the first.*
3. **Toast** — the action finished somewhere that is not a form and the screen stays put: row
   actions, list activations, overlay actions, clipboard. Carries `success` and `info` too.
4. **Screen state** — the screen could not be populated at all (`loadFailed`, `notFound`).
   Never a toast; there would be nothing behind it.

Plus one line: transport kinds route by the same rule, but their words come from
`transport-messages.ts`, not from a union table.

#### 2. The z-index scale

**File**: `src/app/src/styles.scss`

**Intent**: A fifth uncoordinated literal is exactly what `--bottom-nav-tab-height` exists to
prevent. Introduce tokens and convert the four existing sites in the same change.

**Contract**: `--z-skip-link: 1`, `--z-bottom-nav: 5`, `--z-overlay: 10`, `--z-toast: 20` in
`:root`; `app.scss:17`, `bottom-nav.scss:23`, `members.scss:149` and the three overlay
`:host` blocks reference the tokens. The comment block at `bottom-nav.scss:1-12` is rewritten
to point at the scale instead of restating the numbers.

#### 3. The toast component and service

**Files**: `src/app/src/app/shared/toast/toast-host.ts`, `.html`, `.scss`,
`src/app/src/app/shared/toast/toast.service.ts` (new)

**Intent**: One transient surface for the third outlet, and the app's first self-dismissing
element — so timing, motion and announcement all have to be decided here rather than
per-caller.

**Contract**: The service API the migration phases call:

```ts
toast.success(message: string): void;
toast.info(message: string): void;
toast.error(message: string): void;
```

Three tones map to the three CS-05 meanings — `info` is what a recovered conflict
(`'Lista była nieaktualna — odświeżono.'`) becomes, which is neither success nor failure. The
host is mounted once in `app.html` as a sibling of `<main>`, sits at `--z-toast`, clears the
bottom nav on phone width using the same `env(safe-area-inset-bottom)` the bar itself uses,
announces through `LiveAnnouncer` from `@angular/cdk/a11y`, and gates its transition behind
`prefers-reduced-motion` the way `plan-builder.scss:205-209` does. Errors persist until
dismissed; `success` and `info` auto-dismiss.

#### 4. The form-state factory

**File**: `src/app/src/app/shared/forms/form-state.ts` (new)

**Intent**: Extract the four-signal block and the seven byte-identical `reject()` copies once.
A function, not a base class — the repo uses no component inheritance and every shared
component is standalone (`roadmap.md:549-553` leaves this call to the plan; this is the call).

**Contract**: `createFormState()` returns the four signals (`loading`, `loadFailed`,
`submitting`, `error`) plus `reject(control, errors)` — which keeps today's exact behaviour,
`setErrors` followed by `markAsTouched`. Components hold the result in a field and delegate;
nothing inherits.

### Success Criteria:

#### Automated Verification:

- Unit tests pass: `npm test` from `src/app/`
- Toast spec covers all three tones, auto-dismiss for `success`/`info`, persistence for
  `error`, and that `LiveAnnouncer` is called
- `createFormState` spec covers `reject` setting errors and marking touched
- Lint and format pass: `npm run quality:check` from `src/app/`
- Build succeeds and the initial bundle is reported: `npm run build` from `src/app/`

#### Manual Verification:

- A toast triggered from the console renders above the bottom nav on a phone-width viewport
  and above an open overlay
- A screen reader announces the toast text
- With OS "reduce motion" on, the toast appears without animating
- The initial bundle is still under 550 kB, or the overshoot is quantified

**Implementation Note**: Pause here for manual confirmation before Phase 3 — the z-index
conversion and the bundle number both need eyes.

---

## Phase 3: The remaining fourteen tables

### Overview

Every union gets an exhaustive table built by the factory, field-level wording included, plus
the spec that proves no union was missed.

### Changes Required:

#### 1. Fourteen message modules

**Files** (new, each beside its models file):
`core/auth/login-failure.ts`, `register-failure.ts`, `profile-failure.ts`,
`change-password-failure.ts`, `reset-password-failure.ts`;
`core/scheduling/class-type-failure.ts`;
`core/training/exercise-failure.ts`, `training-plan-failure.ts`;
`core/admin/member-failure.ts`, `trainer-role-failure.ts`, `block-failure.ts`,
`unblock-failure.ts`, `access-code-failure.ts`

**Intent**: Move every Polish sentence out of the fourteen component `switch` statements and
into a `Record` the compiler checks, using the wording that ships today as the starting point.

**Contract**: Each module exports `xxxFailureMessage` built by `createFailureMessages`, keyed
by its union's reason type. **Field-level wording lives here too** — where a form today
hardcodes a control's sentence in its template, that sentence moves into the table, and the
form keeps only the control mapping. The `time_conflict` double-wording
(`class-failure.ts:34` vs `class-form.html:76-79`) is resolved in favour of a single sentence
that reads correctly both under a field and in a banner.

`UnblockFailure` has one reason and no explainer today (`members.ts:191-194`); it still gets a
table, because the point is that every union has one.

**Adapted during implementation.** Three things the plan did not anticipate:

1. **The seventeenth union is `ScheduleReadFailure`** (`class.models.ts:169`), not a fourteenth
   auth/admin table — the plan's file list names thirteen new modules, and `ProfileFailure` turns
   out to BE `ContactFailureReason` rather than a union of its own. So the count lands at seventeen
   via `contact-failure.ts` (serving `/profile`, the member form and registration's legacy codes)
   plus `schedule-read-failure.ts`, which nothing renders today and which exists so the rule has no
   exceptions.
2. **The bounds had to move first.** Thirty-odd sentences quote a numeric limit (`maxName`,
   `maxSets`, …) that was a `const` private to the form component. A table quoting a number the form
   owns privately drifts the moment the number moves, so the limits became `CLASS_TYPE_BOUNDS`,
   `EXERCISE_BOUNDS` and `TRAINING_PLAN_BOUNDS` beside their unions, imported by both the table and
   the form.
3. **The person-relative admin sentences lost the member's name.** `members.ts` interpolated
   `member.displayName` into the trainer-role and access-code refusals. A table keyed by a reason
   returns a sentence, not a template, so these now read "ta osoba" — the wording
   `booking-failure.ts` already settled on in S-16 for the same reason. They are read immediately
   after acting on that member's own row, so the subject is never in doubt.

#### 2. Exhaustiveness arrays

**Files**: `core/auth/auth.models.ts`, `core/scheduling/class-type.models.ts`,
`core/training/exercise.models.ts`, `training-plan.models.ts`,
`core/admin/member-admin.models.ts`

**Intent**: Give the fourteen unions the same `satisfies Record<Reason, true>` companion the
three good ones have, so the contract spec can enumerate them.

**Contract**: Follow the existing shape exactly (`booking.models.ts:95-104`,
`class.models.ts:137-154`, `member-admin.models.ts:264-275`).

#### 3. The `AccessCodeFailure` drift

**File**: `src/app/src/app/core/admin/member-admin.models.ts`

**Intent**: The union omits `member_blocked`, which the API emits
(`src/Application/Members/IssueAccessCode.cs:61`); the screen handles it only because its
helper is typed `string | undefined`. Adding the reason is type-only and changes no behaviour,
so CS-03 does not bar it.

**Contract**: `AccessCodeFailure['reason']` becomes
`'has_account' | 'member_blocked' | 'conflict'`, with a comment citing the backend line — and
noting the backend record's own doc comment omits it too.

#### 4. The contract spec

**File**: `src/app/src/app/core/http/failure-contract.spec.ts` (new)

**Intent**: The enforcement half of the decision. Types catch a missing entry inside a table;
this catches a union that never got a table at all.

**Contract**: One spec that walks a registry of all seventeen `{ reasons, message }` pairs and
asserts, per union: every reason yields a non-empty message that is not the fallback; an
unrecognised reason yields the fallback; and no two reasons in a union share a sentence unless
the table's own comment says they deliberately do (as `class-failure.ts:38-44` already does).

### Success Criteria:

#### Automated Verification:

- Unit tests pass: `npm test` from `src/app/`
- `failure-contract.spec.ts` covers all 17 unions and fails if a new union is added without a
  table
- Type checking passes as part of `npm run build`
- Lint and format pass: `npm run quality:check` from `src/app/`

#### Manual Verification:

- Spot-check the rewritten `time_conflict` sentence reads correctly both under the start-time
  field and in a form banner

---

## Phase 4: Migration — auth and profile

### Overview

The first area onto the rule. Smallest surface, and it contains the one permanent exception
(login's non-disclosure), so it is the right place to prove the toolkit.

### Changes Required:

#### 1. Auth screens

**Files**: `features/auth/login/login.ts`, `features/auth/register/register.ts`,
`features/auth/forgot-password/forgot-password.ts`,
`features/auth/reset-password/reset-password.ts`, `features/profile/profile.ts` (+ their
templates)

**Intent**: Replace each inline `switch` with the union's table, each hand-written unwrap with
`classifyFailure`, and each local four-signal block with `createFormState()`. Route each
failure to the outlet the Phase 2 rule assigns.

**Contract**: `login` stays **banner-only** — the rule's second outlet — and
`login.spec.ts:104-108`, which asserts the message does not leak `'nie istnieje'`, must pass
unmodified.

**Adapted during implementation.** "`login.spec.ts` passes without modification" held for the
non-disclosure anchor and for every business-reason test, but NOT for three assertions across
`login.spec.ts`, `forgot-password.spec.ts` and `profile.spec.ts` that flush a **500 or a 429** and
assert the union's generic fallback (`'Nie udało się zalogować'`, `'Nie udało się wysłać'`,
`'Nie udało się zapisać'`). Those sentences are exactly what the Desired End State removes — "a 429,
a 500 and an offline browser each read as themselves" — so passing them unchanged would have meant
the slice did not work. Each was rewritten to assert the new, specific wording AND to re-assert what
must not change: login still never names a field, forgot-password still never says whether the
address exists, and profile's 500 still leaves every contact control `aria-invalid="false"`. The
non-disclosure test itself was not touched. `profile.ts` carries two independent form groups and therefore two
`createFormState()` instances. The duplicated `passwordsMatch` validator
(`reset-password.ts:17-22`, `profile.ts:25-30`) collapses into `core/auth/validation.ts`,
which already exists for exactly this.

### Success Criteria:

#### Automated Verification:

- Unit tests pass: `npm test` from `src/app/`
- `login.spec.ts` passes **without modification**
- No `HttpErrorResponse` unwrap remains in `features/auth/` or `features/profile/`
- Lint and format pass: `npm run quality:check` from `src/app/`

#### Manual Verification:

- Bad credentials show one banner naming neither field
- A blocked account, a taken e-mail and an invalid invitation each read as before
- Password change and profile save show field errors on the right controls

**Implementation Note**: Pause for manual confirmation before Phase 5.

---

## Phase 5: Migration — admin

### Overview

The largest area: eleven screens and three overlays, and the first place the toast outlet
carries real traffic.

### Changes Required:

#### 1. Member administration

**Files**: `features/admin/members/members.ts`, `member-form.ts`, `member-passes.ts`

**Intent**: `members.ts` hosts four unions (`TrainerRole`, `Block`, `Unblock`, `AccessCode`)
plus the two duplicated `'Lista była nieaktualna — odświeżono.'` literals and a per-row
`failedId` mechanism — it is the densest single file in the slice. Row actions become the
toast outlet; the stale-list recovery becomes `toast.info`.

**Contract**: `member-form.ts:181-202` stops being the odd one out — its five contact-detail
reasons route to controls the way `profile.ts:196-221` already does. `member-passes.ts` gains
its first spec, because its error handling changes.

#### 2. Classes and overlays

**Files**: `features/admin/classes/classes.ts`, `class-form.ts`, `class-create-overlay.ts`,
`class-bookings-overlay.ts`

**Intent**: `classes.ts` currently handles `conflict` by table message plus local mutation and
holds the app's only optimistic write (`classes.ts:185-230`); the overlays are pure toast
territory. `class-bookings-overlay.ts:140-149`'s ad-hoc 404 branch becomes
`classifyFailure`'s `notFound` kind.

**Contract**: `class-form.ts`'s stated rule (`:274-309`) is finally true — the file keeps the
control mapping and takes every sentence from the table. The optimistic rollback behaviour is
preserved exactly; only the message source changes.

**Adapted during implementation.** Two things:

1. **The overlays are not "pure toast territory".** The create overlay and the bookings overlay's
   add-a-member picker are FORMS, so under the Phase 2 rule their refusals are outlet 2 and stay
   inside the overlay — which is also what manual check 5.7 asks for. Releasing a spot keeps its
   per-row message for the same reason: the admin is looking straight at the row. What changed in
   all three is the source (`classifyFailure` + the table) and the addition of a transport branch,
   not the surface. `class-bookings-overlay`'s ad-hoc `status === 404` test did become
   `classifyFailure`'s `notFound` kind as planned.
2. **Eight spec assertions read a message out of the screen's own DOM.** The toast host is mounted
   once in the shell, so it is deliberately not in a component fixture; those assertions now read
   `ToastService` instead, which also lets them pin the TONE — the distinction the single `.notice`
   banner could never carry. Five more assertions moved to the table's wording, which is the
   double-wording this slice set out to collapse (`time_conflict`, the two instructor refusals, and
   `name_taken` on both list screens and both forms).

#### 3. Class types and exercises

**Files**: `features/admin/class-types/class-types.ts`, `class-type-form.ts`,
`features/admin/exercises/exercises.ts`, `exercise-form.ts`, `exercise-detail.ts`

**Intent**: Both list screens hardcode their own "somebody else took this name" sentence for
`name_taken` (`class-types.ts:137-143`, `exercises.ts:157-163`); both move to their tables.
The deliberate silent swallow in `exercise-form.ts:141-150` (datalist suggestions) stays
silent — the rule covers failures the user must know about, and this is not one.

**Contract**: `exercise-detail.ts:69-77`'s `notFound` state is kept, now driven by
`classifyFailure`.

### Success Criteria:

#### Automated Verification:

- Unit tests pass: `npm test` from `src/app/`
- `member-passes.spec.ts` exists and covers its refusal paths
- No `HttpErrorResponse` unwrap remains in `features/admin/`
- Lint and format pass: `npm run quality:check` from `src/app/`

#### Manual Verification:

- Blocking an admin, granting the trainer role to an accountless member, and issuing a code to
  a blocked member each produce the right toast
- A stale list after a 409 refetches and shows an `info` toast, not an error
- Booking someone into a full class shows the refusal in the overlay
- Rescheduling by drag still rolls back visually on refusal

**Implementation Note**: Pause for manual confirmation before Phase 6.

---

## Phase 6: Migration — trainer and member

### Overview

The remaining screens, including the four-card dashboard whose per-card independence must
survive.

### Changes Required:

#### 1. Trainer plans

**Files**: `features/trainer/plans/plans.ts`, `plan-builder.ts`

**Intent**: `plan-builder.ts:340-404` holds ~14 inline sentences over the largest union
(17 reasons) — the single biggest table move in the slice.

**Contract**: The `FormArray` row structure and the CDK drag-reorder behaviour are untouched;
only error handling and form state change.

#### 2. Member screens

**Files**: `features/my-plan/my-plan.ts`, `plan-exercise-detail.ts`,
`features/schedule/schedule.ts`, `features/schedule/class-details-overlay/`,
`features/my-classes/my-classes.ts`, `features/dashboard/dashboard.ts`

**Intent**: Move to `classifyFailure` and the tables while preserving the two distinctions
these screens already draw correctly.

**Contract**: Two behaviours are load-bearing and pinned by existing specs. A 204 "no plan yet"
and a 204 "no karnet" must keep rendering with **no** `[role="alert"]`, while a genuine network
failure must keep producing one (`dashboard.spec.ts:226-235,330-349,357-370`). The dashboard's
four independently fenced loads stay independent — one failed card must not blank the other
three.

**Adapted during implementation.** The member screens carry no `*Failure` union of their own — they
are load-only, and their `loadFailed` state was already outlet 4 and already correct. So "move to
`classifyFailure` and the tables" landed as a `loadMessage` signal beside `loadFailed`, set from
`transportMessage`, which the template renders after the screen's own "nie udało się wczytać X"
sentence. That is what makes manual check 6.6 true: a killed API used to read exactly like a server
that answered and refused. `dashboard.ts` was left alone — it unwraps nothing, its four fences are
already independent, and `dashboard.spec.ts` passes unmodified, which was the point of naming it.

### Success Criteria:

#### Automated Verification:

- Unit tests pass: `npm test` from `src/app/`
- `dashboard.spec.ts` passes **without modification**
- No `HttpErrorResponse` unwrap remains anywhere under `features/`
- Lint and format pass: `npm run quality:check` from `src/app/`

#### Manual Verification:

- A member with no plan and no karnet sees calm empty states, not errors
- Killing the API mid-session produces an offline-specific message, not a generic refusal
- One failing dashboard card leaves the other three rendered

**Implementation Note**: Pause for manual confirmation before Phase 7.

---

## Phase 7: Extractions and close-out

### Overview

The four copied families that are not about errors, plus the bundle reckoning. Scope was
widened here deliberately: CS-06 names four blocks, and the user chose to take all seven
families the research found.

### Changes Required:

#### 1. The busy-row helper

**Files**: `features/admin/members/members.ts:623-632`,
`features/admin/classes/classes.ts:1145-1154`,
`features/admin/exercises/exercises.ts:175-184`,
`features/admin/class-types/class-types.ts:155-164`,
`features/admin/classes/class-bookings-overlay.ts:208-217` → one shared helper

**Intent**: Five copies of the same ten-line `Set`-mutation pair.

**Contract**: A signal-backed helper exposing `setBusy(id, busy)` and `isBusy(id)`, placed
beside `form-state.ts` under `shared/`.

#### 2. The generation-counter fence

**Files**: ~15 load methods across `my-classes.ts`, `schedule.ts`, `members.ts`,
`exercises.ts`, `class-types.ts`, `dashboard.ts` (×4) and the admin forms

**Intent**: The most-copied block in the app — the private counter incremented per load and
checked after every `await` so a slow response cannot overwrite a newer one.

**Contract**: A helper that issues a token and answers whether it is still current. The
dashboard's four independent fences stay four independent fences; this replaces the mechanism,
not the topology.

#### 3. The page-header block

**Files**: the six list headers (`members.scss:5-15`, `exercises.scss:4-14`,
`class-types.scss:4-14`, `plans.scss:4-14`, `plan-builder.scss:4-14`, `classes.scss:7-17`) and
the two detail headers (`exercise-detail.scss:3-18`, `plan-exercise-detail.scss:5-20`)

**Intent**: Eight near-identical stylesheets, not the three CS-06 claims. `members.scss` has
already drifted to `--space-3` where the other five use `--space-4`.

**Contract**: One global `.page-header` class in `styles.scss` covering the shared flex shape,
with the detail family's badge-carrying `h1` rules as a modifier. The drift resolves to
`--space-4`; the six markup blocks adopt the class. Keep the per-screen action content as-is —
only the layout is shared.

#### 4. The overlay chrome

**Files**: `class-details-overlay.scss`, `class-bookings-overlay.scss`,
`class-create-overlay.scss`, and the three components

**Intent**: `.overlay-backdrop`, `.overlay-panel`, `.overlay-title`, `.overlay-actions` and a
local `.link-button` reset duplicated near-verbatim, which the files' own comments admit. None
of the three manages focus.

**Contract**: The shared chrome moves to `styles.scss` alongside `.page-header`; the two real
differences stay local (`class-bookings-overlay`'s wider `max-width: 32rem`, documented as
deliberate). While the three components are open, each receives initial focus on open and
returns focus to its opener on close — the pattern `members.ts:513-525` already implements for
the row menu. `class-create-overlay.scss:32-35` gains the `overflow-wrap: anywhere` the other
two have.

**Adapted during implementation.** Three things:

1. **`.link-button` was copied 18 times, not three.** The plan expected a local reset inside the
   three overlays; sixteen copies were byte-identical, one carried a `[disabled]` rule and one had
   decayed to a single property. All eighteen are gone and the `[disabled]` rule was folded into the
   one global copy.
2. **The overlays' fixed layer stays local.** The shared chrome is everything INSIDE the overlay —
   the layer itself is each component's `:host`, which a global class cannot reach.
3. **Focus is handled by a helper, not by three copies of the `members.ts` pattern.**
   `shared/forms/overlay-focus.ts` reads `document.activeElement` at construction (the overlay is
   opened by a click, so that IS the opener), focuses the panel after the first render, and restores
   on destroy — guarding against an opener the action itself removed from the document. Focus lands
   on the PANEL rather than its first control, so the dialog announces its own title.

**Bundle outcome:** 512.42 kB initial against the 550 kB warning — measured at the end of Phase 2
(509.68 kB) and again here. The threshold did not move; the number is recorded in `AGENTS.md`
anyway, so the next reader knows it was measured rather than assumed.

#### 5. Close-out

**Files**: `AGENTS.md`, `context/foundation/lessons.md` (if a rule earned its place)

**Intent**: Record the bundle outcome and any adaptation the implementation forced, per the
lessons.md rule that a necessary deviation is written into the plan and the docs, not only into
a commit message.

**Contract**: If the initial bundle crossed 550 kB, the new number and its reason go into
`AGENTS.md` beside the existing S-12 note; if it did not, say so explicitly so the next reader
knows it was measured.

### Success Criteria:

#### Automated Verification:

- Unit tests pass: `npm test` from `src/app/`
- Backend tests still pass, untouched: `dotnet test` from the repo root
- Lint and format pass: `npm run quality:check` from `src/app/`
- Build succeeds and reports the initial bundle: `npm run build` from `src/app/`
- No `reject(`, `setBusy(` or generation-counter declaration remains outside `shared/`

#### Manual Verification:

- All eight headers render identically to before on desktop and phone widths
- Each overlay takes focus on open and returns it on close, and Escape still closes from
  anywhere
- The initial bundle is under 550 kB, or the overshoot is recorded in `AGENTS.md`

---

## Testing Strategy

### Unit Tests

- `classifyFailure` over every `FailureKind`, including a `ProgressEvent` for offline and a
  body-less 500 for the shape the server has no middleware for
- `createFailureMessages` for the fallback path and the `Object.hasOwn` guard — specifically
  that a reason of `'constructor'` returns the fallback string, not a function
- The toast: three tones, auto-dismiss vs persistence, `LiveAnnouncer` invocation
- `createFormState`: `reject` sets errors and marks touched

### Integration Tests

- `failure-contract.spec.ts` over all 17 unions — the enforcement mechanism, and the one spec
  that fails when a future union arrives without a table
- Existing specs that must pass **unmodified**, as regression anchors: `login.spec.ts`
  (non-disclosure), `dashboard.spec.ts` (204 vs error), the three existing failure specs
- Backend `dotnet test` stays green throughout; this slice touches no backend file

### Manual Testing Steps

1. Sign in with bad credentials — one banner, no field highlighted, no hint which was wrong
2. Stop the API, then act on any screen — an offline-specific message, not a business refusal
3. Book a member into a full class from the admin overlay — refusal shown in the overlay
4. Trigger a 409 on the member list — an `info` toast and a refetched list
5. Open each of the three overlays with the keyboard — focus enters, Escape closes, focus
   returns to the opener
6. Repeat 1–5 at phone width with the bottom nav visible — toasts clear the bar
7. Turn on OS reduce-motion and confirm toasts appear without animating

## Performance Considerations

The toast host is mounted in the shell and therefore lands in the eager chunk, together with
`@angular/cdk/a11y`'s `LiveAnnouncer`. `angular.json`'s 550 kB initial warning
(`src/app/angular.json:44-47`) is the one number this slice can move, and CDK's drag-drop is
currently confined to the lazy `plan-builder` chunk — importing `cdk/a11y` in the shell is the
first CDK code in the eager bundle. Measure at the end of Phase 2 and again in Phase 7;
`cdk/overlay` was declined partly for this reason.

Fourteen new message modules add source but almost no bundle: they are string tables imported
by the screens that already exist, and tree-shaking keeps an unused table out of a chunk that
does not reference it.

## Migration Notes

No data migration. Each migration phase (4–6) is independently revertible: the foundation from
Phases 1–3 is additive, so reverting a screen phase leaves the toolkit in place and the screen
on its old path. During Phases 4–6 the old and new mechanisms coexist by design — that is the
cost of migrating by area rather than in one commit, and it is bounded by Phase 6 completing.

## References

- Research: `context/changes/frontend-error-and-patterns/research.md`
- Roadmap slice: `context/foundation/roadmap.md:533-559` (S-19); anchors at `:42-60`
- The reference table recipe: `src/app/src/app/core/scheduling/booking-failure.ts`
- The `Object.hasOwn` review finding:
  `context/archive/2026-09-02-schedule-calendar-view/reviews/impl-review.md:169-174`
- Login non-disclosure: `context/archive/2026-09-05-member-profile-edit/research.md:253-254`
- Bundle budget history:
  `context/archive/2026-09-06-member-and-admin-dashboards/reviews/impl-review.md:210`
- Plan-fidelity rule: `context/foundation/lessons.md` ("Record necessary adaptations in the
  plan, not only in the deploy log")

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Foundation — classification and the table factory

#### Automated

- [x] 1.1 Unit tests pass: `npm test` from `src/app/` — 5b0b876
- [x] 1.2 The three existing failure specs pass without modification — 5b0b876
- [x] 1.3 New specs cover `classifyFailure` for each `FailureKind`, including offline and a body-less 500 — 5b0b876
- [x] 1.4 Lint and format pass: `npm run quality:check` from `src/app/` — 5b0b876

#### Manual

- [x] 1.5 No screen behaves differently — this phase changes no call site — manually verified 2026-09-20

### Phase 2: The rule, the toast, and the form-state helper

#### Automated

- [x] 2.1 Unit tests pass: `npm test` from `src/app/` — 20e0513
- [x] 2.2 Toast spec covers three tones, auto-dismiss, persistence, and `LiveAnnouncer` — 20e0513
- [x] 2.3 `createFormState` spec covers `reject` setting errors and marking touched — 20e0513
- [x] 2.4 Lint and format pass: `npm run quality:check` from `src/app/` — 20e0513
- [x] 2.5 Build succeeds and the initial bundle is reported: `npm run build` — 20e0513

#### Manual

- [x] 2.6 Toast renders above the bottom nav at phone width and above an open overlay — manually verified 2026-09-20
- [x] 2.7 A screen reader announces the toast text — manually verified 2026-09-20
- [x] 2.8 With reduce-motion on, the toast appears without animating — manually verified 2026-09-20
- [x] 2.9 Initial bundle under 550 kB, or the overshoot quantified — measured 512.42 kB at impl-review (2026-09-20), no budget warning emitted

### Phase 3: The remaining fourteen tables

#### Automated

- [x] 3.1 Unit tests pass: `npm test` from `src/app/` — 9496fe2
- [x] 3.2 `failure-contract.spec.ts` covers all 17 unions and fails on a union without a table — 9496fe2
- [x] 3.3 Type checking passes as part of `npm run build` — 9496fe2
- [x] 3.4 Lint and format pass: `npm run quality:check` from `src/app/` — 9496fe2

#### Manual

- [x] 3.5 The rewritten `time_conflict` sentence reads correctly under the field and in a banner — manually verified 2026-09-20

### Phase 4: Migration — auth and profile

#### Automated

- [x] 4.1 Unit tests pass: `npm test` from `src/app/` — 5b452c0
- [x] 4.2 `login.spec.ts` passes without modification (non-disclosure anchor unmodified; three 500/429 assertions rewritten — see the Phase 4 adaptation note) — 5b452c0
- [x] 4.3 No `HttpErrorResponse` unwrap remains in `features/auth/` or `features/profile/` — 5b452c0
- [x] 4.4 Lint and format pass: `npm run quality:check` from `src/app/` — 5b452c0

#### Manual

- [x] 4.5 Bad credentials show one banner naming neither field — manually verified 2026-09-20
- [x] 4.6 Blocked account, taken e-mail and invalid invitation each read as before — manually verified 2026-09-20
- [x] 4.7 Password change and profile save show field errors on the right controls — manually verified 2026-09-20

### Phase 5: Migration — admin

#### Automated

- [x] 5.1 Unit tests pass: `npm test` from `src/app/` — 63a5cda
- [x] 5.2 `member-passes.spec.ts` exists and covers its refusal paths — 63a5cda
- [x] 5.3 No `HttpErrorResponse` unwrap remains in `features/admin/` — 63a5cda
- [x] 5.4 Lint and format pass: `npm run quality:check` from `src/app/` — 63a5cda

#### Manual

- [x] 5.5 Blocking an admin, granting trainer to an accountless member, and issuing a code to a blocked member each produce the right toast — manually verified 2026-09-20
- [x] 5.6 A stale list after a 409 refetches and shows an `info` toast, not an error — manually verified 2026-09-20
- [x] 5.7 Booking into a full class shows the refusal in the overlay — manually verified 2026-09-20
- [x] 5.8 Rescheduling by drag still rolls back visually on refusal — manually verified 2026-09-20

### Phase 6: Migration — trainer and member

#### Automated

- [x] 6.1 Unit tests pass: `npm test` from `src/app/` — 18343d1
- [x] 6.2 `dashboard.spec.ts` passes without modification — 18343d1
- [x] 6.3 No `HttpErrorResponse` unwrap remains anywhere under `features/` — 18343d1
- [x] 6.4 Lint and format pass: `npm run quality:check` from `src/app/` — 18343d1

#### Manual

- [x] 6.5 A member with no plan and no karnet sees calm empty states, not errors — manually verified 2026-09-20
- [x] 6.6 Killing the API mid-session produces an offline-specific message — manually verified 2026-09-20
- [x] 6.7 One failing dashboard card leaves the other three rendered — manually verified 2026-09-20

### Phase 7: Extractions and close-out

#### Automated

- [x] 7.1 Unit tests pass: `npm test` from `src/app/` — cae01bb
- [x] 7.2 Backend tests still pass, untouched: `dotnet test` from the repo root — cae01bb
- [x] 7.3 Lint and format pass: `npm run quality:check` from `src/app/` — cae01bb
- [x] 7.4 Build succeeds and reports the initial bundle: `npm run build` — cae01bb
- [x] 7.5 No `reject(`, `setBusy(` or generation-counter declaration remains outside `shared/` — cae01bb

#### Manual

- [x] 7.6 All eight headers render identically to before on desktop and phone widths — manually verified 2026-09-20
- [x] 7.7 Each overlay takes focus on open, returns it on close, and Escape still closes from anywhere — manually verified 2026-09-20
- [x] 7.8 Initial bundle under 550 kB, or the overshoot recorded in `AGENTS.md` — measured 512.42 kB at impl-review (2026-09-20); under the threshold, recorded in `AGENTS.md`
