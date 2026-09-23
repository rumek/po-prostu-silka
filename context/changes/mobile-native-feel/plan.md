# Mobile native feel — Implementation Plan

## Overview

On a phone, the shell learns **which screen it is showing and how deep that screen sits**. Two
things follow from that:

- **A native top app bar.** Start and the signed-out screens show the logo. Every other screen
  shows its title, and a back arrow when it is a child screen.
- **Native Android back behaviour.** Tab switches do not grow the history stack. Back from a tab
  returns to Start, and back from Start leaves the app. The in-app "back" pops history, and back
  closes an open overlay.

The route-slide animation (S-26) now follows the same hierarchy. The Android status bar and splash
take the app's own colour, which is now where the brand lives.

Scope is the phone (`bp.narrow`, 30rem and below). The desktop header and desktop screens stay
visually unchanged, with one exception: `/my-plan` gains a "Mój plan" heading, see Phase 1 §6.

## Current State Analysis

From `frame.md` (the hypothesis table) and `research.md`:

- **The phone header is only the logo.**
  - `app.html:3-4`, `app.scss:27-44, 95-109`: a 140px logo in a header that scrolls away.
  - No title, no back affordance, no `safe-area-inset-top`.
  - The rationale for keeping the logo there (`app.scss:102-106`) is superseded by the user's
    decision: "Marka może żyć gdzie indziej".
- **Nothing names the screen.**
  - No route has `title`; there is no `TitleStrategy`; `<title>` is static (`index.html`).
  - Each screen writes its own `<h1>`: 23 of them across `features/**`, plus `plan-summary.html:2`.
  - Some of those titles depend on loaded data: the exercise name, `Plan — {member}`,
    `Karnety — {member}`, and the edit-vs-new forms.
- **Screen identity lives in the content.** The `h1` is Cormorant `clamp(2rem, …, 2.75rem)`
  (`styles.scss:194`).
- **History is plain web history.**
  - Bottom-bar tabs are plain `routerLink`s (`shared/bottom-nav/bottom-nav.html`), so every tap
    pushes a history entry.
  - The "Wróć…" links are plain `routerLink`s that push:
    - `plan-exercise-detail.html:6,16`
    - `plan-builder.html:15`
  - No code uses `Location`.
  - Overlays live outside history, so they close only on Escape or the backdrop:
    - `class-bookings-overlay.ts`
    - `admin/classes/class-actions-overlay.ts`
    - `class-create-overlay.ts`
- **Motion does not know the hierarchy.**
  - `core/layout/view-transitions.ts:23` knows only `popstate` vs everything else.
  - Angular also starts a transition on query-param-only navigations
    (`trainer-members.ts:230,263,267-274`).
- **Platform colours clash.**
  - `theme_color #1f2937` and `background_color #ffffff` (`public/manifest.webmanifest:9-10`,
    `index.html:22`) sit against the app's `--ground #f7f3ef` (`styles.scss:82`).

## Desired End State

On an installed Android PWA:

1. **Start shows the logo and the greeting.**
   - A pinned top bar shows the logo.
   - The "Cześć, {imię}" greeting stays in the content.
2. **Every other signed-in screen has a pinned bar with its title.** The bar sits under the status
   bar and does not scroll away. On:
   - a tab root: only the title;
   - a child screen: a back arrow plus the title (exercise name, `Plan — {member}`, "Moje konto" …).

   The large in-content heading and the in-content "Wróć…" link are gone on the phone.
3. **Tab switches do not grow history.**
   - Start → Zajęcia → Plan → Więcej, then system back: lands on Start.
   - Back again: leaves the app.
4. **Child screens pop cleanly.**
   - Plan → exercise → back arrow (or system back): lands on Plan.
   - System back from Plan now goes to Start, not to the exercise.
5. **Open overlays consume back.** Trainer on Grafik, a class's bookings overlay open, system back:
   the overlay closes and the trainer is still on Grafik.
6. **Motion follows the hierarchy.**

   | Move | Motion |
   | --- | --- |
   | Tab to tab | Short fade |
   | Into a child | Slides forward |
   | Up or back | Slides backward |
   | Typing in the trainer's member search, or paging | Nothing |

7. **Platform colours match the app.**
   - The status bar and the launch splash are the app's cream.
   - `document.title` names the screen.

**Verification:**
- `npm test`, `npm run quality:check` and `npm run build` pass (budget included).
- Two new Playwright specs pass.
- The manual checklist is run on a real Android install.

### Key Discoveries:

- **`navigationFor()` is already the one name table for tabs** (`core/layout/navigation.ts:86-97`).
  The tab labels there and the route titles must agree for the five tab routes. A spec pins it.
- **Route `data` has a precedent.** `membersLink` on the plan builder (`app.routes.ts:169`, read at
  `plan-builder.ts:171-173`) is also the child's parent.
- **All three overlays call one helper.** `useOverlayFocus()` in `shared/forms/overlay-focus.ts` is
  the one place to add back-to-close.
- **The fixed layers only stack as a set.**
  - `--z-*` scale: `styles.scss:140-145`, AGENTS.md.
  - Each fixed layer has its own view-transition name: `bottom-nav.scss:21-27`,
    `styles.scss:693-722`.
  - The bar is a fifth document-root surface, so it joins both.
- **There is no back icon.** `IconName` (`shared/icons/icon.ts:4-16`) has none, so one is added.
- **E2E conventions.**
  - Specs run against the built SPA served by the API.
  - Only an **admin** `storageState` exists (`e2e/credentials.ts`).
  - On a phone, the admin's bar is Start | Grafik (`/schedule`) | Członkowie | Ćwiczenia | Więcej,
    and the admin can open the bookings overlay on `/schedule`. Both new specs can therefore run as
    the admin at a phone viewport.
  - The behaviour under test is persona-agnostic.

## What We're NOT Doing

- **Touch feedback**: `:active` states, `@media (hover: hover)`, `user-select` / touch-callout.
- **Loading and freshness**: caching, skeletons, preloading, refresh on resume, service-worker
  shell caching, `SwUpdate`.
- **Overlay shape**: overlays keep their current look. Back closes them, but they are not turned
  into bottom sheets and have no swipe-to-dismiss.
- **iOS specifics**: `apple-*` meta tags, startup images, the 180px touch icon.
- **Other manifest additions**: `shortcuts`, `screenshots`, install prompt / install guidance.
- **Forms**: 16px inputs, `enterkeyhint`, tap-target sizes.
- **Out-of-scope bugs found in research** (each worth its own change):
  - the push prompt shown to staff
  - `loadCurrentUser` treating offline as signed-out
  - the undefined tokens in `trainer-members.scss`
- **Hide-on-scroll for the bar.** The bar is always visible (decision).
- **Admin detail screens' in-content "Wróć…" links.** Admin screens do get titles, and their
  headings are tagged (Phase 1 §3/§6), but their own back links are not reworked. Admin is a
  desk persona.
- **Pruning history when switching tabs from a child screen.** The browser cannot drop entries.
  - Sequence: Start → Plan → exercise → tap Zajęcia gives [Start, Plan, Zajęcia].
  - One back lands on Plan before Start.
  - Accepted as the one known deviation from Material.
- **The desktop header's links.** They keep pushing history. Desktop behaves like the web.

## Implementation Approach

1. **Every route declares its identity in the route table:**
   - a Polish `title`;
   - `data.level`:
     - `'brand'`: Start and the signed-out screens;
     - `'tab'`: a bottom-bar or root destination;
     - `'child'`: anything reached from inside a tab;
   - `data.parent` for a child.
2. **A custom `TitleStrategy` publishes the resolved title and level as signals** on a
   `ScreenTitle` service. Components whose title depends on data call `screenTitle.set(...)`. The
   override clears on the next navigation.
3. **The shell renders the bar from those signals on the phone.** On the desktop, the header is
   untouched.
4. **Screens keep their own `<h1>` for the desktop.** On the phone it is hidden by one global class,
   so exactly one `h1` is displayed at any width.
5. **Phase 2 reads the same identity** to decide:
   - the bottom bar's push-vs-replace;
   - what the back arrow does;
   - which motion a navigation gets.

## Critical Implementation Details

- **Leaf snapshot.** The routes are flat, but `TitleStrategy` and `ViewTransitionInfo` hand over the
  **root** `ActivatedRouteSnapshot`. Read `data` from the deepest `firstChild`.
- **Same-URL history entries and the router.**
  - An overlay's history entry is a `history.pushState` to the **same URL**. It carries a copy of
    the current `history.state` (so Angular's `navigationId` and `ɵrouterPageId` survive) plus an
    overlay marker.
  - When back pops it, the router sees a `popstate` to an identical URL. With the default
    `onSameUrlNavigation: 'ignore'` it performs no navigation and no view transition.
  - This is an Angular-internal behaviour, and the Phase 2 overlay e2e spec is what pins it. If it
    fails, fall back to listening in the helper and letting the router ignore it. Record the result
    as "**Adapted during implementation.**" per `lessons.md`.
- **Closing an overlay pops its own entry.** Closing by ×, the backdrop, Escape or a completed
  action must call `history.back()` when the top entry is still the overlay's. The helper's own
  `popstate` handler must then not close the overlay a second time.
- **A navigation from inside an overlay** (e.g. to a member) replaces nothing and pops nothing.
  The overlay entry is simply left under the new screen, as a same-URL entry that back then skips
  through. Pop the entry before navigating only if the e2e spec shows a double back.
- **The bar's view-transition layer.** A sticky/fixed element in the root snapshot jumps during a
  transition (commit eec631a). The bar gets `view-transition-name: app-bar` and the same
  no-animation treatment as `bottom-nav` / `toast-host` in `styles.scss`.

## Phase 1: Screen identity, the phone app bar, platform colours

### Overview

Every route names itself. The phone shows a pinned bar with the logo (Start and signed out), the
title, or back plus title. In-content headings and back links hide on the phone. Status bar and
splash match the app. History and motion are unchanged in this phase.

### Changes Required:

#### 1. Route identity

**File**: `src/app/src/app/app.routes.ts`

**Intent**: Give every route its screen identity in one place, so the bar, `document.title` and
Phase 2 read the same data. The tab titles must equal the bar labels in `navigationFor()`, with
"Grafik" for `/schedule`.

**Contract**:
- Every route has a `title` (Polish).
- Every route has `data.level: 'brand' | 'tab' | 'child'`.
- A `child` has `data.parent: string`, a URL. `plan-builder`'s parent reuses its `membersLink`.

Assignments:

| Level | Routes |
| --- | --- |
| `brand` | `''`, `login`, `register`, `forgot-password`, `reset-password` |
| `tab` | `schedule`, `my-classes`, `my-plan`, `trainer/members`, `more`, plus the admin list roots: `admin/members`, `admin/classes`, `admin/class-types`, `admin/exercises` |
| `child` | `my-plan/exercises/:id` (parent `/my-plan`), `trainer/members/:id/plan` (`/trainer/members`), `profile` (`/more`), and every admin `new` / `:id` / `:id/…` route (its list root) |

Define the level type and the data-reading helpers in a new `core/layout/screen.ts`, so that routes,
the strategy and Phase 2 share one definition.

#### 2. ScreenTitle service + TitleStrategy

**File**: `src/app/src/app/core/layout/screen-title.ts` (new), registered in `app.config.ts`

**Intent**: One source for "what is this screen called and how deep is it".
- The strategy resolves each navigation's leaf snapshot into `title`, `level` and `parent` signals.
- It sets `document.title` to `"{title} · Po Prostu Siłka"`, or `"Po Prostu Siłka"` for `brand`.
- Components with data-dependent titles override it.

**Contract**:
- `ScreenTitle` (root-provided) exposes read-only signals `title()`, `level()` and `parent()`, plus
  `set(title: string)`.
- `set` updates both the signal and `document.title`.
- The override is dropped on the next resolved navigation.
- A `TitleStrategy` subclass is provided via `{ provide: TitleStrategy, useClass: … }`.

#### 3. Data-dependent titles

**Files**:
- `features/my-plan/plan-exercise-detail.ts` (exercise name)
- `features/trainer/plans/plan-builder.ts` (`Plan — {member}`)
- `features/admin/members/member-passes.ts` (`Karnety — {member}`)
- the admin forms with edit-vs-new headings: `class-type-form`, `class-form`, `exercise-form`,
  `member-form`
- `features/admin/exercises/exercise-detail`

**Intent**: The bar shows the same words the desktop `h1` shows.

**Contract**: each calls `screenTitle.set(...)` with the same expression its `h1` renders, once the
data it depends on is known. The route `title` is the fallback shown while loading.

**Adapted during implementation.**
- The call is `useScreenTitle(() => …)` (in `screen-title.ts`), an effect over `ScreenTitle.set`,
  held in a field. It lands after the strategy's reset because the router calls `updateTitle`
  synchronously right after activation, before any change detection.
- The four edit-vs-new admin forms do NOT call it. Their `new` and `:id` routes are separate
  entries, so each route's own `title` already says "Nowy …" or "Edytuj …".
- `plan-builder`'s `data.membersLink` is gone. The builder reads `data.parent`, so the child's parent
  and the back link are one value.

#### 4. The phone app bar in the shell

**Files**: `src/app/src/app/app.html`, `app.ts`, `app.scss`

**Intent**: At `bp.narrow`, the `<header>` becomes a pinned app bar:
- **brand level, or signed out**: the logo, smaller than today's 140px;
- **tab level**: an `<h1>` holding `title()`;
- **child level**: a back button plus the `<h1>`.

Above narrow, the header is exactly today's: logo plus nav. The bar's `h1` is `display: none`
there.

**Contract**:
- The bar is `position: sticky; top: 0`, with `padding-top: env(safe-area-inset-top)`.
- Its background is `--ground`, plus a bottom hairline or shadow once content scrolls under it.
  Use CSS scroll-driven detection if it is available; otherwise always show the hairline. No
  scroll listener.
- Height comes from a new global token `--app-bar-height` (56px) in `styles.scss`.
- Z-index comes from a new `--z-app-bar`. It goes above page content and below
  `--z-overlay`; place it and document it with the scale.
- `view-transition-name: app-bar`, with the no-animation rules added beside `bottom-nav` in
  `styles.scss`.
- In Phase 1 the back button navigates to `parent()`. Phase 2 replaces that with the up
  behaviour.
- The back button's accessible name is "Wróć".
- **Adapted during implementation.** The skip link surfaces at the top of the viewport, exactly
  where the pinned bar sits, so the scale is reordered: `--z-app-bar: 4`, `--z-bottom-nav: 5`,
  `--z-skip-link: 6` (was 1), `--z-overlay: 10`, `--z-toast: 20`.
- The skip link still targets `#main`. The `<header>` landmark stays.
- Remove the stale rationale comment at `app.scss:102-106`. Replace it with a comment explaining
  that the brand's phone home is Start, the icon and the status bar (this change, frame.md).

#### 5. Back icon

**File**: `src/app/src/app/shared/icons/icon.ts` (+ `icon.html`)

**Intent**: The bar's back arrow, drawn like the existing icons.

**Contract**: `IconName` gains `'back'` (a left chevron or arrow).

#### 6. Screen headings and back links hide on the phone

**Files**:
- `src/styles.scss`
- every `features/**` template that renders a signed-in screen's `h1`
- the two member/trainer "Wróć…" links

**Intent**: Exactly one displayed `h1` at any width.
- On the phone the bar is the heading.
- On the desktop the screen's own heading is.
- The in-content back link duplicates the bar's arrow on the phone, so it hides there.

**Contract**:
- Global class `.screen-title` hides at `bp.narrow`.
- Global class `.up-link` hides at `bp.narrow`.
- Both are global, for the reason `.page-header` is (AGENTS.md S-23: projected, caller-scoped
  content).
- `.screen-title` is applied to every signed-in screen's `h1`. This includes the admin screens, so
  none of them shows a duplicate title on the phone.
- It is NOT applied to:
  - the dashboard greeting, which stays visible on the phone under the brand bar;
  - the signed-out screens, which have no titled bar.
- `.up-link` is applied to:
  - the "Wróć do planu" link (`plan-exercise-detail.html:16`, and `:6` in the not-found state);
  - the "Wróć do listy członków" link (`plan-builder.html:15`).
- **`/my-plan` — the one desktop-visible change.**
  - Today its `h1` is the plan's NAME (`plan-summary` `headingLevel` defaults to `h1`).
  - It becomes a `.screen-title` `<h1>Mój plan</h1>` over the summary rendered with
    `headingLevel="h2"`, in both the has-plan and no-plan states.
  - On the phone the plan name stays visible as the `h2`.
- `.page-header` wrappers whose only child was the `h1` must not leave an empty gap on the phone.

#### 7. Platform colours

**Files**:
- `src/app/public/manifest.webmanifest`
- `src/app/src/index.html`

**Intent**: The Android status bar and the launch splash become the app's own cream, which is the
brand's phone home now that the header no longer carries the logo.

**Contract**:
- Manifest:
  - `theme_color` and `background_color` = `#f7f3ef` (the `--ground` value);
  - add `"id": "/"` and `"lang": "pl"`.
- `index.html`: `<meta name="theme-color">` = `#f7f3ef`.
- `index.html`: `<title>` spelled `Po Prostu Siłka` to match the manifest. The strategy overwrites
  it at runtime anyway.

### Success Criteria:

#### Automated Verification:

- Unit specs pass: `cd src/app && npm test`
- New `core/layout/screen-title.spec.ts` covers:
  - the leaf-snapshot title and level;
  - the `document.title` format for tab and brand;
  - a `set()` override dropped on the next navigation.
- New route-table spec asserts:
  - every route in `app.routes.ts` (except `**`) declares `title` and `data.level`;
  - every `child` has a `parent`;
  - the five tab routes' titles equal their `navigationFor()` labels.
- `app.spec.ts` covers the shell rendering, per level:
  - brand: logo and no bar `h1`;
  - tab: `h1` with the title and no back button;
  - child: a back button named "Wróć" plus the `h1`.
- Lint and format pass: `cd src/app && npm run quality:check`
- Build passes within budget: `cd src/app && npm run build`. Record the initial-bundle size in
  AGENTS.md's budget paragraph, as S-19, S-23 and S-26 did.
- `dotnet build po-prostu-silka.slnx` still passes (the SPA is not in it, a sanity check only).

#### Manual Verification:

- **390px, member** (`czlonek032@example.test`):
  - Start shows the logo bar and the greeting.
  - Zajęcia, Plan and Więcej show a titled bar and no large heading.
  - Plan → an exercise shows the back arrow and the exercise name.
  - Więcej → Moje konto shows back plus "Moje konto".
- **390px, trainer** (`trener1@example.test`):
  - Grafik and Członkowie show a titled bar.
  - A member's plan shows `Plan — {member}` with back.
- The bar stays pinned while scrolling a long list and gains its hairline.
- The bar does not jump during a route transition.
- **Desktop width**: the header, headings and back links look exactly as before, except `/my-plan`'s
  new "Mój plan" heading.
- **Installed Android PWA**: the status bar and splash are cream, and the status-bar icons are dark.

**Implementation Note**: After this phase and its automated verification, pause for the human to
run the manual checks before Phase 2.

---

## Phase 2: Android back stack, hierarchy-aware motion, overlays consume back

### Overview

History and motion now follow the identity from Phase 1:
- tabs replace history instead of pushing;
- up / back pops;
- the slide knows fade vs push vs pop;
- query-only navigations do not animate;
- an open overlay is closed by back.

### Changes Required:

#### 1. Bottom-bar tabs: push from Start, replace between tabs

**Files**: `src/app/src/app/shared/bottom-nav/bottom-nav.ts`, `bottom-nav.html`

**Intent**: Material's back stack.
- History holds at most Start plus the current tab.
- Back from any tab lands on Start; back from Start leaves.

**Contract**:
- A tab navigation uses `replaceUrl: true` unless the current screen's level is `brand`. The
  shell hands the bar the current level, or the bar reads `ScreenTitle.level()`.
- The tabs stay `<a [routerLink]>`, so middle-click and long-press still see a real href.
- The header's desktop nav is unchanged.
- **Adapted during implementation.** The tabs are `<a [attr.href]>` with their own click handler,
  not `routerLink`: `RouterLink` navigates on every plain click and ignores `defaultPrevented`, so it
  cannot be told to pop. The href keeps middle-click and long press; a modified click is left to the
  browser. The active state is computed from the router's URL instead of `routerLinkActive`.
- **Adapted during implementation.** The Start tab goes UP to `/` (the §2 behaviour) instead of
  navigating. A replace from a tab would leave [Start, Start], and back would then land on a second
  Start before leaving.

#### 2. The up behaviour: back arrow and "Wróć…" links

**Files**:
- `src/app/src/app/core/layout/up.ts` (new)
- the bar's back button in `app.html`
- `plan-exercise-detail.html`, `plan-builder.html` (their `.up-link`s)

**Intent**: "Up" returns to the parent the way native does: by popping when the parent is what
this screen was pushed from, so the forward entry does not linger for Android back.

**Contract**: a root-provided service or directive, used by both the bar and the desktop "Wróć…"
links.
- `history.back()` when the previous in-app navigation's URL is the current screen's
  `parent`, tracked from router events within this session.
- Otherwise (a deep link, a reload, a push-notification entry): navigate to `parent` with
  `replaceUrl: true`.
- **Adapted during implementation.**
  - `Up` pops back to the NEAREST entry below whose path is the target, with
    `Location.historyGo(-n)`, not only the immediately previous one. That is what lets the Start tab
    return to Start from a child screen in one step.
  - The model is kept from router events: push appends, `replaceUrl` overwrites, popstate moves to the
    entry whose `navigationId` the router restored. Each entry's id is read back from
    `Location.getState()`, because a popstate navigation rewrites its entry's id.
  - An entry it cannot place resets the model to the current screen, which only ever falls back to
    the replace. The desktop links use an `a[appUp]` directive (`UpLink`) in the same file.

#### 3. Hierarchy-aware transitions

**Files**:
- `src/app/src/app/core/layout/view-transitions.ts`
- `src/styles.scss` (the S-26 block)

**Intent**: The motion tells the member where they are going.

**Contract**:
- `slideDirection` (renamed if that reads better) derives a kind from the `from` and `to` leaf
  snapshots of `ViewTransitionInfo`:

  | Kind | When | Motion |
  | --- | --- | --- |
  | `skip` | same `routeConfig` and same params: a query-only change | `transition.skipTransition()` |
  | `back` | the trigger is `popstate`, or `to` is shallower than `from` (child → tab/brand) | slide back |
  | `forward` | `to` is deeper (tab/brand → child, or child → a different child) | slide forward |
  | `fade` | both are root-level (`brand` or `tab`) | fade |

- The kind is published as `data-nav-direction` on `<html>`, as today, now with `'fade'` added.
- `styles.scss` gets a fade variant for `page`: about 150–200ms, opacity only.
- `prefers-reduced-motion` keeps disabling all of it.

#### 4. Overlays consume back

**Files**:
- `src/app/src/app/shared/forms/overlay-focus.ts`, or a sibling `overlay-history.ts` called from it
- the three overlays' `close` paths

**Intent**: System back closes the open overlay instead of leaving the screen, for all three
overlays at once.

**Contract**: see Critical Implementation Details.
- **On open**: push a same-URL entry carrying `{ ...history.state, overlay: true }`.
- **On `popstate` while open**: call the overlay's close.
- **On close by any other path**: `history.back()` if the overlay's entry is still on top, with a
  guard so the resulting `popstate` does not close twice.
- The helper needs the component's close callback. Its signature grows to take one, or a new
  helper is called beside it; `useOverlayFocus()`'s existing callers are updated.
- **Adapted during implementation.** The helper is `shared/forms/overlay-history.ts`. It is called by
  `useOverlayFocus(onBack)` when a close callback is passed, and the three overlays pass
  `() => this.close()`.
  - The pop happens on DESTROY, not in each close path. Every close (×, backdrop, Escape, a finished
    action) ends with the parent removing the component.
  - It is skipped while a router navigation is in progress, so a navigation from inside the overlay
    is not undone.
  - The same-URL popstate is skipped by the router as predicted. `back-closes-open-overlay.spec.ts`
    passes against Angular 22.1.

### Success Criteria:

#### Automated Verification:

- Unit specs pass: `cd src/app && npm test`, including new or updated specs for:
  - the bottom-nav `replaceUrl` rule (from a brand screen: push; from a tab: replace);
  - the up behaviour: pop when the previous URL is the parent; replace-navigate on a deep entry;
  - the transition-kind derivation: skip, back, forward, fade, including a `popstate` between two
    tabs, which is back;
  - the overlay helper: open pushes one entry; `popstate` closes; × closes and pops exactly once.
- New E2E spec, one risk per file per `e2e/CLAUDE.md`: **`tab-switches-do-not-grow-history.spec.ts`**.
  1. Admin, phone viewport (390×844).
  2. Start → Członkowie → Ćwiczenia via the bottom bar.
  3. `page.goBack()` lands on `/`.
- New E2E spec: **`back-closes-open-overlay.spec.ts`**.
  1. Admin, phone viewport.
  2. A class created through the API for today, deleted in `afterEach`.
  3. `/schedule` → open that class's bookings overlay.
  4. `page.goBack()`: the dialog is gone and the URL is still `/schedule`.
  - **Adapted during implementation.** The class starts an hour from now when that falls within the
    calendar's visible 06:00–23:00; otherwise it starts tomorrow at 10:00 and the spec picks that
    day in the week strip. The spec takes the first trainer from `/api/admin/trainers`, so the
    database needs one. The class type is deactivated in `afterEach`, because types cannot be
    deleted.
- Both E2E specs pass: `cd src/app && npx playwright test e2e/tab-switches-do-not-grow-history.spec.ts e2e/back-closes-open-overlay.spec.ts`
- Lint and format pass: `cd src/app && npm run quality:check`
- Build passes within budget: `cd src/app && npm run build`

#### Manual Verification:

On an installed Android PWA:

- **Tabs**:
  - Start → Zajęcia → Plan → Więcej, then system back, lands on Start.
  - Back again leaves the app.
  - Tab switches fade and do not slide.
- **Child screens**:
  - Plan → exercise slides forward.
  - The back arrow slides back to Plan.
  - System back from Plan then goes to Start, not to the exercise.
- **Deep link**: opening `/my-plan/exercises/{id}` directly and tapping back arrow lands on Plan
  without leaving the app.
- **Trainer**:
  - Typing in the Członkowie search and paging does not slide the screen.
  - Grafik → a class's bookings overlay → system back closes it and stays on Grafik.
  - The overlay's × then system back goes to Start, with no phantom step.
- **Desktop**: the header links and the "Wróć…" links still work. Browser back after a "Wróć"
  behaves sensibly (pop, not a new entry).

**Implementation Note**: After this phase and its automated verification, pause for the human's
manual confirmation.

---

## Testing Strategy

### Unit Tests:

- **`ScreenTitle` / `TitleStrategy`**:
  - leaf resolution;
  - `document.title` format;
  - override lifetime.
- **Route table**:
  - identity completeness;
  - the tab titles equal the `navigationFor()` labels. The two tables cannot drift, the S-25
    one-table rule.
- **Shell per level**: brand, tab, child.
- **Bottom-nav** push-vs-replace.
- **Up**: pop vs replace-navigate.
- **Transition kind**: every row of the table in Phase 2 §3.
- **Overlay history**: single push, single pop, no double close.

### Integration Tests:

- The two Playwright specs above.
- They run as the admin at a phone viewport, because the behaviour is persona-agnostic and only the
  admin `storageState` exists.

### Manual Testing Steps:

1. `docker compose up -d`, then seed a database: set `TestDataSeed:Enabled=true` on a fresh or
   separate database, as this change's research did with `po-prostu-silka-research`.
2. Build and serve: `npm run e2e:stage` and
   `dotnet run --project src/Api/po-prostu-silka.Api.csproj`.
3. Run each phase's manual checklist at 390px in Chrome.
4. Then on a real Android device: install from the LAN URL, or from Staging after deploy.

## Performance Considerations

- The bar, one service and one icon add a few kB to the eager chunk. Measure and record against the
  600 kB warning (AGENTS.md).
- Nothing listens to scroll. The hairline uses CSS only, or is always on.

## Migration Notes

None. There is no data or API change. The manifest `id: "/"` equals the current implicit id
(derived from `start_url`), so existing installs are not duplicated.

## References

- Frame: `context/changes/mobile-native-feel/frame.md`
- Research: `context/changes/mobile-native-feel/research.md`
- The S-26 transitions: `core/layout/view-transitions.ts`, `styles.scss:645-747`, commits f7f1e4c,
  d2cb5a0, eec631a
- The fixed-layer pattern: `shared/bottom-nav/bottom-nav.scss:14-39`
- The one-table navigation: `core/layout/navigation.ts`, `core/layout/navigation.spec.ts`
- Lessons: `context/foundation/lessons.md` (record adaptations in the plan)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Screen identity, the phone app bar, platform colours

#### Automated

- [x] 1.1 Unit specs pass (npm test) — dcfdeb7
- [x] 1.2 screen-title.spec.ts covers leaf title/level, document.title format, override lifetime — dcfdeb7
- [x] 1.3 Route-table spec: every route has title + level, children have parent, tab titles equal navigationFor labels — dcfdeb7
- [x] 1.4 app.spec.ts covers bar rendering per level (brand, tab, child) — dcfdeb7
- [x] 1.5 quality:check passes — dcfdeb7
- [x] 1.6 Build within budget; bundle size recorded in AGENTS.md — dcfdeb7
- [x] 1.7 dotnet build still passes — dcfdeb7

#### Manual

- [ ] 1.8 390px member: brand bar on Start, titled bars on tabs, back+title on exercise and Moje konto
- [ ] 1.9 390px trainer: titled Grafik/Członkowie, back + "Plan — {member}"
- [ ] 1.10 Bar pinned while scrolling, hairline appears, no jump during transitions
- [ ] 1.11 Desktop unchanged apart from /my-plan heading
- [ ] 1.12 Installed Android: cream status bar and splash, dark status icons

### Phase 2: Android back stack, hierarchy-aware motion, overlays consume back

#### Automated

- [x] 2.1 Unit specs pass (bottom-nav replace rule, up behaviour, transition kinds, overlay history)
- [x] 2.2 E2E tab-switches-do-not-grow-history.spec.ts passes
- [x] 2.3 E2E back-closes-open-overlay.spec.ts passes
- [x] 2.4 quality:check passes
- [x] 2.5 Build within budget

#### Manual

- [ ] 2.6 Android: tab switches fade, back from a tab lands on Start, back from Start exits
- [ ] 2.7 Android: child slides forward, back arrow slides back, system back from tab root goes to Start
- [ ] 2.8 Deep-linked child: back arrow lands on parent without leaving the app
- [ ] 2.9 Trainer: search/paging does not slide; back closes bookings overlay; no phantom step after ×
- [ ] 2.10 Desktop: header links and Wróć links behave sensibly with browser back
