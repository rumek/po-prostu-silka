---
date: 2026-09-19T17:03:12+02:00
researcher: Karol Rumianowski
git_commit: c61fba53ba7c2aebc573cb20077aa3c4abe2790f
branch: main
repository: PoProstuSilka
topic: "Frontend error handling and the patterns copied across screens (S-19)"
tags: [research, codebase, angular, error-handling, ui-patterns, s-19, m-6]
status: complete
last_updated: 2026-09-19
last_updated_by: Karol Rumianowski
---

# Research: Frontend error handling and the patterns copied across screens

**Date**: 2026-09-19T17:03:12+02:00
**Researcher**: Karol Rumianowski
**Git Commit**: c61fba53ba7c2aebc573cb20077aa3c4abe2790f
**Branch**: main
**Repository**: PoProstuSilka

## Research Question

How does the Angular SPA handle and display errors today, and which UI/code patterns have
been copied from screen to screen — where are the inconsistencies, and what does a future
slice have to collapse? Full analysis, both halves (errors *and* patterns).

This change is **roadmap slice S-19** (`context/foundation/roadmap.md:533-559`, status
`ready`), carrying M-6 anchors CS-04, CS-05, CS-06, CS-07.

## Summary

The roadmap's baseline survey (`context/foundation/roadmap.md:186-189`) said: *"A failure is
shown nine different ways, the `HttpErrorResponse` unwrap is hand-written ~20 times in four
spellings, the `reject()` form helper exists in seven byte-identical copies, and there is no
toast component. The one interceptor handles 401 and nothing else."* **Every part of that
still holds at `c61fba5`**, and this investigation adds numbers the survey did not carry:

1. **17 failure unions, 3 message tables.** `core/**/*.models.ts` declares seventeen
   `{ reason: <union> }` shapes. Only three — `BookingFailure`, `ClassFailure`,
   `MembershipPassFailure` — have an exhaustive `Record`-based message module with a spec.
   The other **fourteen are mapped by a hand-written `switch` with a `default:`** inside the
   component that happens to call the endpoint, so a new server reason silently degrades to
   a generic sentence with no compiler check.
2. **The generic sentence is itself unshared.** At least ten independently worded
   "something went wrong" fallbacks exist, from `'Nie udało się zapisać danych…'` down to a
   bare `'Nie udało się. Spróbuj ponownie.'` (`members.html:221`).
3. **The three good tables are bypassed at field level.** `class-form` routes
   `time_conflict` to a control, and the control's text is hardcoded in the template —
   producing *two different Polish sentences for one server refusal* on one form
   (`class-failure.ts:34` vs `class-form.html:76-79`).
4. **No 403 / 429 / 500 / offline handling exists anywhere.** A stale permission, a rate
   limit, a crashed server and an unrecognised business refusal all render the same
   sentence. The backend actively emits 429 (`src/Api/Program.cs:150`) and 503
   (`GetVapidKey.cs:11`), and has **no exception middleware at all**, so an unhandled 500
   arrives in a shape nothing on the client is written for.
5. **The copied blocks are real and countable**: `reject()` ×7, `setBusy`/`isBusy` ×5, the
   generation-counter fence in ~15 load methods, `passwordsMatch` ×2, and two pairs of
   near-duplicate whole files (`exercises.ts`/`class-types.ts`,
   `exercise-detail.ts`/`plan-exercise-detail.ts`).
6. **One live contract drift**: `AccessCodeFailure` declares `'has_account' | 'conflict'`
   (`member-admin.models.ts:194-196`) while the API also emits `member_blocked`
   (`src/Application/Members/IssueAccessCode.cs:61`). The screen shows the right sentence
   anyway, because `codeFailureMessage` takes `string | undefined` (`members.ts:467-476`) —
   the type is wrong, the behaviour is right, and nothing would have caught it.

The good news for scoping: the SPA is **exceptionally consistent everywhere else**. Signals
+ `async/await` over `Promise`-returning services is universal; `@if`/`@for` is universal;
`role="status"` / `role="alert"` is applied uniformly; no RxJS leaks past `core/`. The
inconsistency is concentrated exactly where S-19 says it is.

## Detailed Findings

### 1. The failure contract, end to end

The wire shape is `{ "reason": "<snake_case>" }` with a business status code — camelCase
property (ASP.NET `JsonSerializerDefaults.Web`; no JSON options are configured anywhere in
`src/Api/Program.cs`), snake_case value. CS-07 pins this deliberately (`roadmap.md:57-60`):
*"`{ reason }` stays the failure shape on both sides; `ProblemDetails` is deliberately not
adopted."*

Backend-side there are **five shapes**, not one:

| Shape | Where | Body |
| --- | --- | --- |
| A — `Results.Json(new XFailure(reason), statusCode)` | ~90 call sites, 15 one-field records (`src/Application/**/XFailure.cs`) | `{"reason":"class_full"}` |
| B — `Results.Problem(detail, statusCode)` | `Register.cs:187,262`, `UpdateProfile.cs:84` (500), `GetVapidKey.cs:11` (503) | ProblemDetails — **no `reason` field** |
| C — plain status, empty body | `Results.NotFound()` ×~35, `Results.Unauthorized()`, `Results.Forbid()` (`BookingAuthorization.cs:63`) | — |
| D — Identity redirect override | `src/Api/Program.cs:126-131` | bare 401 / 403 |
| E — rate-limit rejection | `src/Api/Program.cs:150` | framework default 429 |

There is **no shared failure base type or factory** — `public record XFailure(string Reason);`
is copy-pasted fifteen times; two local `Refuse()` helpers exist
(`TrainingPlanValidator.cs:225`, `BookingProtocol.cs:233`) but are private to their file.

**No exception-handling middleware.** Grep for `IExceptionHandler`, `UseExceptionHandler`,
`AddProblemDetails` across `src/Api`, `src/Application`, `src/Infrastructure` returns
nothing. The code knows: `src/Application/Auth/ForgotPassword.cs:91-96` says *"There is no
`UseExceptionHandler` here, so an unhandled throw would answer 500 for a registered address
while an unregistered one still answered 200"* — and catches locally for that reason. So an
unhandled 500 is the one response shape **neither side has a contract for**.

**No validation filter either** — no FluentValidation, no DataAnnotations, no
`IEndpointFilter`. Validation is hand-rolled per area and returns the same Shape A, so a
malformed-input 400 and a business-rule 400 are indistinguishable on the wire. There is no
field-level `errors: {field: [...]}` dictionary anywhere; the *client* invents the field
mapping (see §3).

**Casing is three-way inconsistent inside one body**: camelCase property names, snake_case
`reason` values, PascalCase status strings (`entity.Status.ToString()` at
`ClassDtoMapping.cs:65`, `CurrentUserBuilder.cs:47,57`) because no `JsonStringEnumConverter`
is registered. The client models document the awareness (`auth.models.ts:55`,
`member-admin.models.ts:1-9`) rather than fixing it.

**Drift check across all 17 unions — two findings, one live:**

- `LoginFailureReason` (`auth.models.ts:69`) carries `pending_approval`, which the API has
  not emitted since S-01/S-16. Deliberately kept, documented on both sides
  (`auth.models.ts:62-68`, `src/Application/Auth/LoginFailure.cs:17`), and explicitly left
  alone by the last test slice
  (`context/archive/2026-09-13-testing-frontend-gate-and-contract/plan.md:72-74`).
  **Not a bug.**
- `AccessCodeFailure` (`member-admin.models.ts:194-196`) is **missing `member_blocked`**,
  which `src/Application/Members/IssueAccessCode.cs:61` emits with a 409 — and the backend
  record's own doc comment (`AccessCodeFailure.cs:12-13`) omits it too. Verified
  first-hand. The screen still shows the right sentence, because `members.ts:467-476` types
  its parameter `string | undefined` rather than the union. **This is the one live contract
  gap, and it is a gap on both sides.**

Every other union matches field for field.

### 2. Seventeen unions, three tables

| Failure union | Declared at | Reasons | Message module |
| --- | --- | --- | --- |
| `BookingFailure` | `booking.models.ts:72-82` | 8 | ✅ `core/scheduling/booking-failure.ts` |
| `ClassFailure` | `class.models.ts:109-128` | 16 | ✅ `core/scheduling/class-failure.ts` |
| `MembershipPassFailure` | `member-admin.models.ts:248-257` | 7 | ✅ `core/admin/membership-pass-failure.ts` |
| `LoginFailure` | `auth.models.ts:71-73` | 3 | ❌ inline `switch` — `login.ts:84-98` |
| `RegisterFailure` | `auth.models.ts:116-148` | 6 | ❌ `register.ts:116-144` |
| `ProfileFailure` | `auth.models.ts:165-167` | 5 | ❌ `profile.ts:194-221` |
| `ChangePasswordFailure` | `auth.models.ts:184-186` | 2 | ❌ `profile.ts:170-181` |
| `ResetPasswordFailure` | `auth.models.ts:207-211` | 2 | ❌ `reset-password.ts:112-125` |
| `ClassTypeFailure` | `class-type.models.ts:56-64` | 6 | ❌ `class-type-form.ts:148-174` + `class-types.ts:137-143` |
| `ExerciseFailure` | `exercise.models.ts:69-82` | 11 | ❌ `exercise-form.ts:197-238` + `exercises.ts:157-163` |
| `TrainingPlanFailure` | `training-plan.models.ts:124-145` | 17 | ❌ `plan-builder.ts:340-404` (~14 inline strings) |
| `MemberFailure` | `member-admin.models.ts:122-131` | 7 | ❌ `member-form.ts:181-202` |
| `TrainerRoleFailure` | `member-admin.models.ts:153-155` | 3 | ❌ inline closure — `members.ts:296-302` |
| `BlockFailure` | `member-admin.models.ts:161-163` | 2 | ❌ inline closure — `members.ts:175-179` |
| `UnblockFailure` | `member-admin.models.ts:172-174` | 1 | ❌ `members.ts:191-194` |
| `AccessCodeFailure` | `member-admin.models.ts:194-196` | 2 (+1 undeclared) | ❌ private `codeFailureMessage` — `members.ts:467-476` |
| `ScheduleReadFailure` | `class.models.ts:169-171` | 1 | ➖ deliberately unrendered (`class.models.ts:165-167`) |

The three good tables share an exact recipe, and each one's doc comment explains it: a
`Record<Union['reason'], string>` keyed by the union so an unmapped reason **fails the
build**, an `UNKNOWN` fallback, and a `xxxFailureMessage(reason: unknown)` guarded with
`Object.hasOwn` rather than `in`. Three companion `satisfies Record<Reason, true>` arrays
(`booking.models.ts:95-104`, `class.models.ts:137-154`, `member-admin.models.ts:264-275`)
back exhaustiveness specs (`booking-failure.spec.ts:13-50`, `class-failure.spec.ts:10-34`,
`membership-pass-failure.spec.ts:18-49`).

The `Object.hasOwn` detail is not incidental — it is a review finding from S-07
(`context/archive/2026-09-02-schedule-calendar-view/reviews/impl-review.md:169-174`:
`reason in MESSAGES` would return `MESSAGES['constructor']`, a *function*, typed as
`string`). Every later table copied the fixed form. **Any generic mapper S-19 builds has to
keep that guard.**

**Stale doc comment, confirmed**: `membership-pass-failure.ts:9-12` documents
`booking-failure.ts`'s `ADMIN_MESSAGES` as a live "known soft spot". That symbol was fully
retired during S-16
(`context/archive/2026-09-09-membership-pass-and-staff-booking/reviews/impl-review.md:166`)
and does not exist in the file today.

### 3. How a failure reaches the screen

**Services never catch.** Every `core/**` service documents it ("Nothing here catches… has
to reach the screen") and none of `booking.service.ts`, `class.service.ts`,
`class-type.service.ts`, `exercise.service.ts`, `training-plan.service.ts`,
`member-admin.service.ts` contains a `catch`. Two intentional exceptions:
`auth.service.ts:172-183` (a 401 on `/me` means "not signed in" → `null`) and the whole of
`push.service.ts` (best-effort by design, `:72-105`, `:112-134`).

**One interceptor, one concern.** `auth.interceptor.ts:19-38` clears the session and
navigates to `/login` on an *unexpected* 401 — `/api/auth/login` and `/api/auth/me` are
excluded (`:14`) or the guard would loop — and rethrows (`:35`). It is the only `catchError`
in `core/`, and it is spec'd (`auth.interceptor.spec.ts:42-81`).

**Everything else is per-component.** The unwrap is written by hand at every call site:

```ts
((failure as HttpErrorResponse)?.error as XFailure | undefined)?.reason
```

**No logging anywhere** — zero `console.error`/`console.warn` for HTTP failures in `core/`
or `features/`.

**The display mechanisms**, counted from the templates (this is the "nine ways"):

1. `<p class="alert" role="alert">{{ error() }}</p>` — form/screen banner
   (`class-form.html:23-25`, `profile.html:12,119`, `dashboard.html:12,52,85,119,151`)
2. `<p class="field-error">` under a control (`class-form.html:63,76-85`,
   `profile.html:46,60,76,91,105`)
3. `<p class="notice" role="status">` reused for *success and info* — the
   `notice()`-means-both problem CS-05 names (`class-types.html`, `profile.html:16,123`,
   `members.html:59`)
4. A `loadFailed` boolean whose sentence is hardcoded in the template, with the reason
   discarded — ~15 screens
5. `notFound` as a distinct screen state (`exercise-detail.ts:69-77`,
   `plan-exercise-detail.ts:81-89`)
6. A per-row `failedId` signal with a generic row-level sentence (`members.html:221`)
7. An inline ad-hoc 404 branch bypassing the shared table
   (`class-bookings-overlay.ts:140-149`)
8. A silent swallow with a comment (`class-bookings-overlay.ts:107-113`,
   `exercise-form.ts:141-150`)
9. A signal that is neither error nor notice (`unavailableReason` in `push.service.ts`,
   `codeCopyFailed` in `members.ts:363-370`)

**`conflict` alone is handled four different ways**: table message with no refetch; table
message plus a local list mutation (`classes.ts`); an explicit `status === 409` branch that
forces a refetch (`members.ts:453-462`, `:594-603`); and table message plus
`await this.load()` (`member-passes.ts:207-221`).

**No 403 / 429 / 500 / offline branch exists.** Grep across `core/` and `features/` for
`403`, `429`, `500`, `ProgressEvent`, `navigator.onLine` finds nothing API-related.

**Field-level mapping is bespoke per form and text-duplicated.** Each form owns a private
`applyFailure()` that switches on the reason and calls a private `reject()` with a synthetic
validation key (`{ timeConflict: true }`, `{ nameTaken: true }`, `{ emailTaken: true }`…);
the *words* then come from a hardcoded string in that form's template, **not** from the
shared table. `class-form.ts:274-309` states the intended rule — *"The CONTROL mapping is
form-specific and stays here; the WORDS come from `classFailureMessage`"* — and then only
follows it on the banner branches. Result, on one form:

- `class-failure.ts:34` → `'O tej porze są już inne zajęcia. Wybierz inny termin.'`
- `class-form.html:76-79` → `'O tej porze odbywają się już inne zajęcia. Wybierz inną godzinę.'`

One server refusal, two sentences, depending on which branch fires.

`member-form.ts:181-202` deviates the other way: it maps *everything* to a banner with no
per-control `setErrors`, even for the five contact-detail reasons that `profile.ts:196-221`
routes to controls.

### 4. The copied patterns (CS-06)

| Block | Copies | Locations |
| --- | --- | --- |
| `reject(control, errors)` | **7**, byte-identical | `register.ts:151-154`, `reset-password.ts:136-139`, `profile.ts:224-227`, `class-form.ts:311-315`, `class-type-form.ts:176-180`, `exercise-form.ts:240-244`, `plan-builder.ts:406-410` |
| `setBusy` / `isBusy` (per-row `Set`) | **5** | `members.ts:623-632`, `classes.ts:1145-1154`, `exercises.ts:175-184`, `class-types.ts:155-164`, `class-bookings-overlay.ts:208-217` |
| Generation-counter race fence | ~**15** load methods | `my-classes.ts:31-70`, `schedule.ts:38-103`, `members.ts:50-146`, `exercises.ts:32-98`, `class-types.ts:30-90`, `dashboard.ts:141-246` (×4), … |
| Four-signal form state (`loading`/`loadFailed`/`submitting`/`error`) | **7** forms | every file in the `reject()` row above |
| `passwordsMatch` group validator | **2**, identical incl. comment | `reset-password.ts:17-22`, `profile.ts:25-30` |
| Whole-file near-duplicates | **2 pairs** | `exercises.ts` ↔ `class-types.ts`; `exercise-detail.ts` ↔ `plan-exercise-detail.ts` (the latter pair duplicates the `bypassSecurityTrustResourceUrl` video-trust logic) |
| Local `settle()`/`respond()` spec helpers | most of 41 specs | e.g. `my-classes.spec.ts:56-60` |
| Server-bound constants mirrored per file | 5 files | `class-form.ts:21-27`, `class-type-form.ts:14-30`, `exercise-form.ts:16-34`, `plan-builder.ts:29-49`, `member-passes.ts:21-24` — while `core/auth/validation.ts` exists for exactly this and is used by only 4 files |

The page-header block CS-06 names (three copies plus three near-identical stylesheets) was
not re-counted in this pass; the roadmap's figure stands unchallenged.

### 5. What is already consistent (do not "fix" it)

This matters as much as the inconsistency list — it bounds the slice.

- **Standalone components, no NgModules**; kebab file → PascalCase class, no `Component`
  suffix; external `templateUrl`/`styleUrl` (one deliberate inline exception,
  `readonly-field.ts:22-30`).
- **Signals everywhere.** No `toSignal`, no `resource`/`httpResource`, no `AsyncPipe`, and
  **no `.subscribe()` in production code at all**. RxJS lives only inside services as
  `firstValueFrom(...)` returning `Promise<T>`. Already pinned as settled in S-12
  (`context/archive/2026-09-06-member-and-admin-dashboards/research.md:47-50`: *"The
  dashboard must not introduce a fifth pattern."*)
- **Reactive forms** (D8,
  `context/archive/2026-09-01-registration-and-approval/plan.md:170-175`), with a documented
  secondary `[ngModel]`-on-a-signal idiom for single unvalidated inputs.
- **`@if`/`@for`/`@switch` at 346 sites, zero legacy structural directives.**
- **Hand-rolled SCSS over design tokens** (D7, same plan `:150-168`) — no Tailwind, no
  Material, self-hosted fonts. CS-07 forbids introducing a UI library.
- **`role="status"` vs `role="alert"` applied uniformly** (70+ occurrences) — the SPA leans
  on implicit live-region semantics and never writes `aria-live`. Icons are
  `aria-hidden="true"` by default (`icon.html:10-11`).
- **Guards compose** (`authGuard` + `activeMemberGuard`/`adminGuard`/`trainerGuard`).
- **Tests are TestBed + real `HttpClient` + `HttpTestingController`**, asserting rendered DOM
  via semantic queries, never `data-testid`.
- **`ChangeDetectionStrategy.OnPush` is used nowhere** — uniform, if silent, and *not* in
  S-19's charter.

Deviations worth noting but outside the error/pattern charter: `members.ts:500,517` uses
legacy `@HostListener` where the three overlays use `host: {}`; the header `<nav>`
(`app.html:7`) is unlabelled while the bottom nav is; `Members`, `ClassForm`, `ClassTypes`
and `ClassTypeForm` are **statically imported and therefore eager** (`app.routes.ts:7-13`),
which contradicts AGENTS.md's "everything except login/register/pending//" claim; five of
eight `core/` services and the whole `member-passes.ts` screen have no spec.

## Code References

- `src/app/src/app/core/scheduling/booking-failure.ts` — the reference table pattern (Record + UNKNOWN + `Object.hasOwn`)
- `src/app/src/app/core/scheduling/class-failure.ts:34` — the `time_conflict` sentence the form's own template contradicts
- `src/app/src/app/core/admin/membership-pass-failure.ts:9-12` — stale comment describing a retired `ADMIN_MESSAGES`
- `src/app/src/app/core/auth/auth.interceptor.ts:14-38` — the only interceptor; 401 only
- `src/app/src/app/core/admin/member-admin.models.ts:194-196` — `AccessCodeFailure`, missing `member_blocked`
- `src/app/src/app/features/admin/members/members.ts:467-476` — handles `member_blocked` anyway, via `string | undefined`
- `src/app/src/app/features/admin/classes/class-form.ts:274-315` — the stated control-vs-words rule, and the `reject()` copy
- `src/app/src/app/features/admin/classes/class-form.html:76-79` — the contradicting hardcoded sentence
- `src/app/src/app/app.config.ts:16-20` — `provideBrowserGlobalErrorListeners()`, no custom `ErrorHandler`
- `src/app/src/app/app.routes.ts:7-13` — the four eagerly-imported admin screens
- `src/Application/Members/IssueAccessCode.cs:61` — the undeclared `member_blocked` emission
- `src/Application/Auth/ForgotPassword.cs:91-96` — "There is no `UseExceptionHandler` here"
- `src/Api/Program.cs:126-131,150` — 401/403 body stripping; the 429 rejection code

## Architecture Insights

- **The failure vocabulary is the API's public surface, and it is enforced in exactly three
  places.** The `Record`-keyed-by-union trick is the only mechanism in the codebase that
  turns a server-side vocabulary change into a client-side *build* failure. Fourteen unions
  currently have no such tripwire; that is the structural finding, not the count of
  sentences.
- **The "nine mechanisms" are not nine bad decisions.** Each is locally defensible — a
  banner for a form, a `notFound` state for a detail route, a silent swallow for a typeahead.
  What is missing is the *rule* that says which one a given failure gets, which is precisely
  why CS-04 puts the rule ahead of the toast component (`roadmap.md:554-558`: *"a toast
  component without the rule … simply becomes a tenth way to show an error"*).
- **There is prior art for both directions of this refactor, and one prior rejection.** S-14
  deliberately built a *second* mapper rather than a second table, because
  `booking-failure.ts` addressed the member in the second person and an admin acting for a
  third party needs different words
  (`context/archive/2026-09-07-member-entity-and-accountless-members/plan.md:339-343`) — and
  S-16 then retired that split entirely when self-booking went away. The lesson for S-19:
  **audience, not endpoint, is what forks a message table.**
- **Banner-vs-control routing is a repeat review finding**, not a fresh observation —
  flagged in at least two slices
  (`context/archive/2026-09-02-occurrences-from-class-types/reviews/impl-review.md:65-72`;
  `context/archive/2026-09-04-training-plans/research.md:312-313`). One deliberate exception
  must survive the cleanup: **login is banner-only on purpose**, so it does not disclose
  which field was wrong
  (`context/archive/2026-09-05-member-profile-edit/research.md:253-254`, spec'd at
  `login.spec.ts:104-108`).
- **The backend half of the contract is out of scope but not irrelevant.** CS-03/CS-07
  forbid touching it, yet the missing exception middleware means the client cannot write a
  correct 500 branch even if S-19 wants one. That is a server-side gap S-19 should record
  rather than fix.

## Historical Context (from prior changes)

- `context/foundation/roadmap.md:533-559` — S-19's charter: outcome, prerequisites (S-17),
  parallel with S-18, two risks (toast-without-rule; the 550 kB eager bundle, since the toast
  host lives in the shell).
- `context/foundation/roadmap.md:42-60` — CS-04…CS-07 in full, including the explicit refusal
  of `ProblemDetails` and of any UI library.
- `context/foundation/roadmap.md:549-553` — the slice's one recorded **unknown**: whether the
  four-signal form-state block becomes a functional helper or a base class. Owner
  `/10x-plan`. The roadmap notes the repo uses no component inheritance, "which argues for a
  helper, but it is a call the plan should make explicitly rather than inherit."
- `context/archive/2026-09-02-schedule-calendar-view/plan.md:640-653` — origin of the table
  pattern (S-07), and why it was extracted *before* the second consumer existed.
- `context/archive/2026-09-03-class-booking-and-cancel/plan.md:528-540` — the recipe restated
  for `booking-failure.ts`, including `reason: unknown` and `Object.hasOwn`.
- `context/archive/2026-09-09-membership-pass-and-staff-booking/plan.md:548-549` and
  `reviews/impl-review.md:166` — the `ADMIN_MESSAGES` soft spot and its retirement.
- `context/archive/2026-09-13-testing-frontend-gate-and-contract/plan.md:78-83` — the test
  slice explicitly **deferred** the remaining message tables to "S-19's CS-05", and left
  `pending_approval` alone.
- `context/archive/2026-09-01-registration-and-approval/plan.md:150-188` — D7 (SCSS + tokens,
  no framework), D8 (reactive forms), D9 (Polish copy, no i18n), D10 (English routes). D9 is
  why every message in this research is a hardcoded Polish literal.
- `context/foundation/prd.md:129,184` — no in-app notification center. A toast is a transient
  surface and does not breach this, but a persistent error *list* would.
- `context/archive/2026-09-06-member-and-admin-dashboards/reviews/impl-review.md:210` — the
  550 kB budget was raised from 500 kB in S-12 and is the number S-19's toast host can move.

## Related Research

- `context/archive/2026-09-06-member-and-admin-dashboards/research.md:47-50,270-271` — the
  settled client patterns, and "`shared/` holds only `calendar/` and `week-navigator/`".
- `context/archive/2026-09-03-class-booking-and-cancel/research.md:228,246-249` — no UI
  library; the table pattern as the thing to copy.
- `context/archive/2026-09-05-member-profile-edit/research.md:253-256` — the
  `.alert[role=alert]` / `.field-error` / disabled-while-submitting markers, and login's
  deliberate non-disclosure.

## Open Questions

1. **What is the rule?** CS-04's centre of gravity. A first cut from the evidence: field
   error when the reason names a control the user can fix; banner when it names the form or
   the screen's own state; toast when the action succeeded-or-failed elsewhere on the page
   (row actions, overlays) and the screen stays put; screen state for load failures and 404s.
   This needs to be *decided*, not inferred — owner `/10x-plan`.
2. **Helper or base class** for the four-signal form block — the roadmap's own recorded
   unknown (`roadmap.md:549-553`).
3. **One generic mapper, or fourteen more tables?** The three existing tables buy build-time
   exhaustiveness from the `Record`-keyed-by-union trick. A single generic
   `failureMessage(reason)` over a flat dictionary would lose that per-union guarantee unless
   it is built as a registry of per-union `Record`s. Which one CS-05 means by "the ~20
   unwraps collapse to one function" is genuinely ambiguous — the *unwrap* is clearly one
   function; the *messages* may not be.
4. **Does `notice()` split into `notice()` and `warn()`, or into a toast and a banner?**
   CS-05 says only that it "stops meaning both success and failure."
5. **Should the field-level text move into the tables?** It is the only way to kill the
   `time_conflict` double-wording, but it inverts `class-form.ts:274-309`'s stated rule and
   needs an answer before any form is touched.
6. **Out of scope, but record it**: the `AccessCodeFailure` / `member_blocked` drift is a
   two-sided type gap that CS-03 (no backend behaviour change) does not forbid fixing on the
   *client* side — adding the reason to the union is type-only. Worth one line in the plan
   either way.
7. **Also out of scope**: no exception middleware on the server, so there is no defined 500
   shape for any client branch to target.
