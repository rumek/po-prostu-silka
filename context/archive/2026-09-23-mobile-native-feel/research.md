---
date: 2026-09-23T08:21:00+02:00
researcher: Claude (Opus 5.5) with Karol Rumianowski
git_commit: eec631a2439b5a824716c90f4c62eaa5e4427e8e
branch: main
repository: po-prostu-silka
topic: "What keeps the installed PWA from feeling like a native app on a phone (Member and Trainer)"
tags: [research, codebase, pwa, mobile, shell, header, bottom-nav, view-transitions, overlays, manifest, service-worker]
status: complete
last_updated: 2026-09-23
last_updated_by: Claude (Opus 5.5)
---

# Research: what keeps the PWA from feeling native on a phone

**Date**: 2026-09-23T08:21:00+02:00
**Researcher**: Claude (Opus 5.5) with Karol Rumianowski
**Git Commit**: eec631a2439b5a824716c90f4c62eaa5e4427e8e
**Branch**: main
**Repository**: po-prostu-silka

## Research Question

Improve the mobile UI/UX so the installed PWA feels more like a native app: find what needs fixing.
The user specifically asked to consider the **top navigation**. Native apps usually show the
screen's title in the header, but then the **logo that sits in the header today** would no longer
be visible.

Scope agreed with the user:
- **Platforms:** iOS Safari standalone and Android Chrome, weighted equally.
- **Personas:** Member and Trainer. The admin screens are desk-oriented.
- **Method:** code analysis plus the running app. The live inspection is **not yet done** (see
  Open Questions). The app was started on a separate `po-prostu-silka-research` database with
  seed data, but logging in requires the user.

## Summary

The phone shell is a web page with a fixed bottom bar added on. The bar itself (S-12, S-25, S-26
pill) is solid. The rest of the native vocabulary is missing:

1. **No top app bar.**
   - On a phone the `<header>` is a centred 140px logo that scrolls away with the page.
   - There is no screen title, no back affordance and no `safe-area-inset-top`.
   - Detail screens put an inline "Wróć …" `routerLink` inside the content.
   - No mechanism feeds a title to the shell: no route `title`, no `TitleStrategy`, and
     `document.title` is static.
   - Keeping the logo on the phone is a *documented* decision (`app.scss:102-106`): the logo is
     the only thing telling the member which app this is when there is no address bar.
2. **Navigation semantics are web-like.**
   - The S-26 slide doesn't tell a tab switch from a push or a pop.
   - It also fires on query-param-only navigations (the trainer member search and pager).
   - The "Wróć" links push new history entries, so Android back returns to the detail screen.
   - The scroll position is never reset or restored between routes.
3. **No touch feedback.**
   - The tap highlight is removed globally and nothing replaces it: there are no `:active`
     states.
   - `:hover` styles stick after a tap.
   - Long-press opens the browser menu or selects text on chrome.
4. **The desktop idioms remain on the phone.**
   - Overlays are centred modals: no bottom sheet, no scroll lock, and back doesn't close them.
   - List rows are cards with small text links instead of full-width tappable cells.
   - Several tap targets are under 44px.
   - Several inputs are under 16px, so iOS zooms when they get focus.
5. **Loading and freshness.**
   - Every tab visit shows "Wczytywanie…" and refetches. There is no cache, no skeleton and no
     preloading.
   - Nothing refreshes data on resume.
   - The service worker caches nothing, not even the app shell.
   - Offline or a 5xx while checking `/me` is treated as signed out.
6. **The platform shell doesn't match the app.**
   - `theme_color` #1f2937 and `background_color` #fff clash with the cream `--ground`
     #f7f3ef.
   - There are no iOS meta tags or startup images. The apple-touch-icon is 192px.
   - The manifest has no `id`, `lang`, `shortcuts` or `screenshots`.
   - There is no update handling (`SwUpdate`) and no install guidance, although iOS push
     depends on installing.

## Detailed Findings

### 1. Shell header and screen titles (the user's explicit concern)

- **Markup:** `src/app/src/app/app.html:3-4` is
  `<header class="shell-header"><img src="logo.png" class="shell-brand" routerLink="/">`, with the
  nav inside `@if (auth.isAuthenticated())`.
- **Styles:**
  - `app.scss:27-38`: flex, `padding: var(--space-4) var(--space-5)`, **not sticky, no
    background**.
  - `.shell-brand { width: 140px }` (`app.scss:40-44`).
  - At `bp.narrow`, `.shell-nav { display: none }` (`app.scss:107-109`), so the phone header is
    the logo alone.
- **Documented rationale** for keeping it (`app.scss:102-106`): it is the skip-link target and the
  `<header>` landmark, and "in standalone mode — no address bar — the brand is the only thing
  telling the member which app this is".
- **No title feed:**
  - No route in `app.routes.ts` has `title`, and there is no `TitleStrategy`.
  - `app.config.ts:30-33` only adds `withViewTransitions`.
  - `index.html` `<title>PoProstu Siłka</title>` is static. It is spelled differently from the
    manifest's "Po Prostu Siłka".
  - Route `data` is used once (`membersLink`, `app.routes.ts:169`, read in
    `plan-builder.ts:171-173`).
  - The only name table is the nav labels in `core/layout/navigation.ts`.
- **What each screen shows as its h1 today (Member and Trainer):**

  | Screen | Heading | Reference |
  | --- | --- | --- |
  | Dashboard | `<h1>Cześć, {name}</h1>` | `dashboard.html:1` |
  | My classes | `<h1>Moje zajęcia</h1>` | `my-classes.html:1` |
  | Schedule | `<h1>Grafik zajęć</h1>` | `schedule.html:1` |
  | Trainer members | `.page-header > h1 Członkowie` | `trainer-members.html:1-3` |
  | My plan | the h1 is the **plan's name** (`plan-summary.html:2`); "Mój plan" only when there is no plan (`my-plan.html:91`); no h1 while loading | |
  | Plan exercise detail | `.page-header--detail` with the exercise name and "Wróć do planu" | `plan-exercise-detail.html:14-17` |
  | Plan builder | `Plan — {member}` and "Wróć do listy członków" | `plan-builder.html:3-17` |
  | More | `<h1>Więcej</h1>` | `more.html:1` |
  | Profile | `<h1>Moje konto</h1>`, **no way back to /more** | `profile.html:1-2` |

- The h1 is `clamp(2rem, …, 2.75rem)` in `--font-display` (`styles.scss:184-194`). It takes up
  a large share of a 390px screen above the content.
- **Titles that can only be known after load** (exercise name, member name, plan name) mean a
  static route `title` alone cannot express every screen.
- The **safe area** is not handled: `viewport-fit=cover` (`index.html:13`), but
  `safe-area-inset-top` is used nowhere (only `-bottom`: `app.scss:100`, `bottom-nav.scss:31`,
  `toast-host.scss:116`).
- The **view-transition snapshot** names only `page` (`.shell-main`, `app.scss:85-90`),
  `bottom-nav` and `toast-host` (`styles.scss:645-747`). A sticky or fixed top bar would need its
  own name, the way `bottom-nav` needed one (`bottom-nav.scss:21-27`, commit eec631a).

### 2. Navigation motion, history and scroll

- `core/layout/view-transitions.ts:20-29` treats `popstate` as back and everything else as
  forward.
  - It ignores the `from`/`to` snapshots of `ViewTransitionInfo`.
  - So bottom-bar tab switches slide as a push, whichever direction you tap.
- Angular starts a transition on every navigation, including query-param-only ones (checked in
  `@angular/router` `_router-chunk.mjs:3666-3706`).
  - `trainer-members.ts:230,263,267-274` drives search and paging through `router.navigate`.
  - So the whole screen slides after every pause in typing and on both pager buttons.
- The "Wróć" links are plain `routerLink`s (`plan-exercise-detail.html:6,16`,
  `plan-builder.html:15`).
  - They animate forward and add a history entry.
  - Android back then goes plan → detail.
- There is no `withInMemoryScrolling` (`app.config.ts:30-33`), `ViewportScroller` or `scrollTo`
  anywhere.
  - The next screen opens scrolled down, and back loses the list position.
- **Done and working:** `skipInitialTransition: true`, `prefers-reduced-motion` for the slide
  (`styles.scss:741-747`) and for the pill (`bottom-nav.scss:117-122`).

### 3. Touch feedback and gestures

- `styles.scss:152-157` sets `-webkit-tap-highlight-color: transparent` globally.
  - The only `:active` rule in the app is a cursor change (`plan-builder.scss:55`).
- No `:hover` sits inside `@media (hover: hover)`. They stick after a tap:
  - `styles.scss:215,371,386`
  - `app.scss:70`, `more.scss:29`, `calendar-week-strip.scss:26`
  - `schedule-calendar.scss:70,192,240,312`, `toast-host.scss:65`, `plan-builder.scss:127`
- There is no `user-select` or `-webkit-touch-callout` anywhere.
  - Long-press on a bottom-nav tab opens the link menu.
  - The `title` tooltip that `bottom-nav.html:10` expects never appears on touch.
- There is no `overscroll-behavior`. Inner scrollers pass their scroll through to the page:
  - the overlay panel
  - the week strip (`calendar-week-strip.scss:32`)
  - the calendar (`schedule-calendar.scss:87`)
- There are no touch or gesture handlers, no pull-to-refresh, and no swipe-to-dismiss.

### 4. Overlays, toasts, lists and tap targets

- **The overlay is a centred modal.**
  - The host is in `class-bookings-overlay.scss:6-14` and the panel in `styles.scss:467-510`.
  - There is no body scroll lock and no `overscroll-behavior: contain`.
  - It closes on Escape or the backdrop only (`class-bookings-overlay.ts:78-80`).
  - It pushes no history entry, so **Android back or an iOS edge swipe leaves `/schedule`**. This
    is the trainer's main phone action.
  - Its search input (`class-bookings-overlay.html:65-76`) sits in a vertically centred panel when
    the keyboard opens.
- **Toast:**
  - Positioned correctly above the bar (`toast-host.scss:108-119`).
  - The × is about 18×18px (`toast-host.scss:53-63`).
  - There is no swipe to dismiss and no exit animation.
- **List rows:**
  - `li[appRow]` is a card with 24px padding (`styles.scss:603-609`), no whole-row tap target,
    no chevron and no pressed state.
  - Trainer-members links only the name (`trainer-members.html:47-49`).
  - My-plan's drill-down is a ~21px ⓘ icon (`my-plan.html:29-35`, `my-plan.scss:42-48`).
  - Only `/more` has native-style cells (`more.scss:22-36`, 44px, with a divider), and still no
    chevron.
- **Targets under 44px:**
  - `.link-button` has `padding: 0` (`styles.scss:515-530`).
  - The week-strip days and arrows are 32×40 (`calendar-week-strip.scss:28-40`).
  - `.calendar-step` is ~32px (`schedule-calendar.scss:59-67`).
  - The bottom-nav icons are ~17px (`bottom-nav.scss:73`), against the 24–28px usual in native
    bars.

### 5. Forms and keyboard

- **16px only reaches `.field input, .field select`** (`styles.scss:263-273`). iOS zooms on focus
  for:
  - the bookings-overlay `<select>` (`class-bookings-overlay.html:86-100`, ~13.3px)
  - the calendar date picker (14px, `schedule-calendar.scss:36-50`)
  - the plan-builder `textarea` (`plan-builder.html:142`, never styled globally)
  - the plan-builder search (`plan-builder.html:170`)
- **The trainer-members search box is effectively invisible.**
  - `trainer-members.scss:15-16` uses the undefined tokens `var(--border)` and
    `var(--radius-2)`.
  - This is a plain bug, independent of the native-feel question.
- **Keyboard behaviour:**
  - There is no `interactive-widget` in the viewport meta and no `visualViewport` handling. The
    fixed bottom bar can float over an open keyboard on iOS.
  - There is no `enterkeyhint` anywhere.
  - `autocomplete` and `inputmode` are good on login, register and profile.

### 6. Loading, caching and freshness

- **Every screen fetches on init:**
  - `dashboard.ts:136-146`
  - `my-classes.ts:55`, `my-plan.ts:51`, `trainer-members.ts:118`, `profile.ts:102`
- There is no `RouteReuseStrategy` and no cache in `core/*` services.
- **Loading state and chunks:**
  - `app-loading` is a text notice (`shared/forms/loading/loading.ts:22`). The slide captures
    it, then the content pops in with a layout jump.
  - There is no `withPreloading`, so lazy tabs also fetch their chunk on the first tap.
- **Nothing refreshes on resume:** no `visibilitychange` or `pageshow` handler. A resumed app
  shows stale bookings.

### 7. Platform shell: manifest, meta, service worker, install

- **Manifest** (`src/app/public/manifest.webmanifest`):
  - `theme_color #1f2937` (:10) and `background_color #ffffff` (:9) don't match the cream
    `--ground #f7f3ef` (`styles.scss:82`). The result is a dark slate Android status bar over a
    cream app, and a white splash that jumps to cream.
  - Missing: `id`, `lang: "pl"`, `display_override`, `shortcuts`, `screenshots`, a monochrome
    icon.
- **`index.html`:**
  - Missing: `apple-mobile-web-app-status-bar-style`, `apple-mobile-web-app-title`,
    `apple-touch-startup-image`, `color-scheme`, `format-detection`.
  - The `apple-touch-icon` is the 192px file (iOS expects 180, opaque).
- **Service worker:**
  - `ngsw-config.json` has empty `assetGroups` and `dataGroups`. It is registered for Web Push
    only (`app.config.ts:40-45`).
  - An offline cold start shows the browser's error page.
  - There is no `SwUpdate` or chunk-load error handling. A PWA resumed after a deploy may fail
    lazy navigations silently.
- **SSR is not actually on** (`angular.json:37` `outputMode: "static"`, an empty
  `prerendered-routes.json`). The comment at `auth.guard.ts:18-20` claiming prerendering is stale.
- **The session check treats every failure as signed out.**
  `AuthService.loadCurrentUser()` catches every error as signed-out
  (`core/auth/auth.service.ts:172-184`). Offline gym Wi-Fi or a 5xx therefore sends a signed-in
  member to `/login`.
- **Cold start:**
  1. White splash.
  2. Blank cream page until `main-*.js` loads.
  3. The logo-only header renders.
  4. The session resolves.
  5. The nav and bottom bar pop in.
  6. `app-loading` shows.
- **Install and push:**
  - There is no `beforeinstallprompt` handling and no iOS "Do ekranu początkowego" guidance.
  - The push prompt (`push-prompt.ts:48-57`) tests `swPush.isEnabled`, which is true in a plain
    Safari tab, then quietly fails.
  - The push prompt is gated on `isActive()` alone (`app.html:37`), so **staff see it**, and the
    push click target `/my-classes` is member-only (`WebPushSender.cs:152`). This is an S-25
    persona-rule violation.
  - `PushService.unsubscribe()` has no callers, and logout doesn't unsubscribe.

## Code References

- `src/app/src/app/app.html:3-4, 37-39, 53` — the header, the push prompt gate, the bottom bar
  gate
- `src/app/src/app/app.scss:27-44, 76-110` — header, brand, main and the narrow rules
- `src/app/src/app/app.config.ts:26-45` — router features and the service worker
- `src/app/src/app/core/layout/view-transitions.ts:20-29` — slide direction
- `src/app/src/app/core/layout/navigation.ts:86-97` — per-persona tab sets and labels
- `src/app/src/app/app.routes.ts:57-207` — routes; no `title`
- `src/app/src/styles.scss:82, 125, 140-145, 152-157, 184-194, 263-273, 430-450, 467-530,
  603-609, 645-747` — tokens, tap highlight, headings, fields, page header, overlay, link-button,
  row, transitions
- `src/app/src/app/features/class-bookings/class-bookings-overlay.{ts,html,scss}` — the trainer's
  main phone overlay
- `src/app/src/app/features/trainer/members/trainer-members.{ts,html,scss}` — URL-driven search,
  the broken tokens
- `src/app/src/app/features/my-plan/plan-exercise-detail.html:6,14-17`,
  `features/trainer/plans/plan-builder.html:3-17` — the inline back links
- `src/app/public/manifest.webmanifest`, `src/app/src/index.html`, `src/app/ngsw-config.json`
- `src/app/src/app/core/auth/auth.service.ts:172-184` — offline treated as signed out

## Architecture Insights

- **The one-table navigation (S-25) is the natural anchor for titles.**
  - `navigationFor()` already names every tab.
  - Detail screens are the only ones whose titles are data-dependent.
- **The fixed layers only stack correctly as a set.** The shell deliberately leaves `App :host`
  and `.shell-main` without a stacking context, and uses the `--z-*` scale (AGENTS.md "How a
  failure reaches the user").
  - A top bar would be a fifth document-root surface.
  - It would need a `--z-*` token and a `view-transition-name`.
- **Global row, overlay and page-header classes live in `styles.scss`** because of emulated
  encapsulation (S-19, S-23). A bottom-sheet variant or a pressed state belongs there, not in each
  component.
- **The admin calendar is withheld on the phone by design (UX-01).** Its touch shortcomings are
  out of scope.

## Historical Context (from prior changes)

- **Offline-first is rejected.**
  - The rejection: `context/foundation/prd.md:193`, `prd-v2.md:534`, `roadmap.md:866`.
  - Empty SW groups were chosen to avoid stale schedule data
    (`context/archive/2026-08-31-notification-delivery-foundation/plan.md:582-583`).
  - Caching the **hashed app shell** was never argued separately and doesn't create a stale-data
    risk.
- **No in-app notification centre or badges** (`prd.md:46, 136, 195`).
- **No native apps** (`roadmap.md:875`), so the PWA must carry the native feel.
  - The standing requirement: "Mobile-first and installable … behaving like an app"
    (`prd.md:149`).
- **The bottom bar and "Więcej" come from S-12**
  (`context/archive/2026-09-06-member-and-admin-dashboards/plan.md:34-37, 90-91`).
  - That plan said "No redesign of the desktop header" (:77).
- **S-25** (`context/changes/role-based-visibility/plan.md:86-92, 144`) scoped out any visual
  redesign of the header or bar.
- **UX-09 visual redesign was deferred by the user on 2026-09-20** (`roadmap.md:128-131`).
  - One-handed reachability and a latency target are "design intentions, deliberately not
    committed" (`roadmap.md:863`).
- **S-26** (the route slide) has no change folder. It is recorded only in commits f7f1e4c, d2cb5a0
  and eec631a, in code comments, and in AGENTS.md (the 11.35 kB budget cost).
- **`roadmap.md` has no planned mobile/PWA item.** This change is the only vehicle.

## Related Research

- `context/archive/2026-09-06-member-and-admin-dashboards/research.md` — bottom bar and safe-area
  groundwork.
- `context/changes/role-based-visibility/plan.md` — per-persona navigation.

## Open Questions

- **The live inspection is not done yet.** The app runs on `localhost:5264` against
  `po-prostu-silka-research`, and there is a phone frame at `/phone.html` (gitignored wwwroot).
  The user has to sign in as `czlonek032@example.test`, then `trener1@example.test`. The points to
  confirm by eye:
  - the header and h1 proportions at 390px
  - the tab slide
  - the loading flash
  - the overlay on the schedule
- **Needs a real device:**
  - whether the iOS standalone edge-swipe back acts on `pushState` history
  - how the fixed bottom bar behaves with the iOS keyboard open
- **Logo versus screen title (a product call):**
  - The logo could stay on Start only.
  - It could become a small mark beside the title.
  - It could live only in the splash and icon.
  - The rationale in `app.scss:102-106` has to be answered, not ignored.
- **Scope split.** This is too much for one slice. Decide which clusters belong in the first cut
  and which become follow-ups:
  - shell (top bar, titles, back)
  - touch feedback
  - overlays as sheets
  - loading and freshness
  - platform meta and SW shell
- **Adjacent bugs found in passing (not native-feel):**
  - the push prompt shown to staff (S-25)
  - `loadCurrentUser` treating offline as signed out
  - the broken trainer-members search tokens

  Fix them here or separately?
