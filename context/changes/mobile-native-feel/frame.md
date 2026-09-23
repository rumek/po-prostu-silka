# Frame Brief: mobile native feel

> This is the framing step before /10x-plan. It records what is *actually* at issue, kept separate
> from what was first assumed.

## Reported Observation

The installed PWA, on a phone, does not feel like a native app. The user noticed it on an
installed Android PWA (standalone), and notices it most:
- **while looking at a screen**, and
- **when going back or leaving the app**.

## Initial Framing (preserved)

- **User's stated cause or approach.** The top navigation. Native apps show the current screen's
  title in the header. Doing that here would displace the logo that the header shows today.
- **User's proposed direction.** Research what to improve, starting with a header that carries the
  screen title.
- **Pre-dispatch narrowing.**
  - Where it hits: "Patrząc na ekran" and "Przy powrocie/wyjściu". It is *not* touch feedback and
    *not* startup or loading.
  - Source: "Zainstalowana na Androidzie".
- **Post-dispatch narrowing.**
  - Android back after switching tabs: "Nie sprawdzałem".
  - Logo trade-off: "Marka może żyć gdzie indziej". In the header, "where am I" matters more than
    the brand.

## Dimension Map

The observation could originate in any of these:

1. **Shell chrome.** The phone header is a logo that scrolls away. There is no top app bar, no
   screen title and no back affordance. ← initial framing
2. **In-content heading hierarchy.**
   - Every screen opens with a large serif display `h1` and an intro paragraph. That is a web-page
     pattern.
   - The screen's identity lives inside the scrolling content, not in chrome.
   - Adding a title bar on top of it would duplicate the title rather than replace a gap.
3. **Navigation depth and Android back.**
   - The app doesn't model "root tab" vs "child screen".
   - Tab taps, "Wróć…" links and overlays all behave like web history, so system back walks
     through tabs, re-enters detail screens, and leaves a screen instead of closing its overlay.
4. **Android platform chrome.** The status bar and splash colours come from a manifest that
   doesn't match the app. The system-drawn parts of the screen look foreign.

## Hypothesis Investigation

| Hypothesis | Evidence | Verdict |
| --- | --- | --- |
| 1. No app bar or title in the shell | Phone header is only the 140px logo, not sticky (`app.scss:27-44, 95-109`). No route `title`, no `TitleStrategy`, static `<title>` (`app.routes.ts`, `app.config.ts:30-33`, `index.html`). No `safe-area-inset-top` anywhere | STRONG |
| 2. Screen identity lives in a web-style content heading | `h1` is Cormorant `clamp(2rem, …, 2.75rem)` (`styles.scss:184-198`). Each screen writes its own h1, some dynamic (`dashboard.html:1` "Cześć, {name}", `my-plan` → plan name, `plan-builder.html:4` "Plan — {member}"). 8 hand-placed `.page-header`s (`styles.scss:430-450`). Back links sit inside the content (`plan-exercise-detail.html:14-17`, `plan-builder.html:15`) | STRONG |
| 3. Depth and back are not modelled | Bottom-bar tabs are plain `routerLink`s with no `replaceUrl` (`bottom-nav.html`), so every tab tap pushes history. "Wróć" links push history (`plan-exercise-detail.html:6,16`, `plan-builder.html:15`). No `Location.back()` anywhere. Overlays are signals outside history (`class-bookings-overlay.ts:78-80`, `schedule.html:34-42`). `/profile` has no way back to `/more`. The slide knows only `popstate` (`view-transitions.ts:23`), not depth. **Seen in code, not yet observed on a device** | STRONG (code) |
| 4. Platform chrome mismatch on Android | `theme_color #1f2937` (`manifest.webmanifest:10`, `index.html:22`) vs `--ground #f7f3ef` (`styles.scss:82`): a dark slate status bar over a cream app. `background_color #ffffff` gives a white splash. No manifest `id` | STRONG |
| (excluded) Touch feedback, loading, caching | Real gaps (`research.md` §3, §6), but the user placed the observation elsewhere | out of this frame |

## Narrowing Signals

- The user puts the "web page" feeling at **looking at the screen** and at **going back**, not at
  touch or loading. That rules dimensions 1–4 in and moves touch and loading to follow-ups.
- **Android, installed.**
  - The status-bar colour (4) is on screen at all times.
  - Hardware and gesture back (3) is the main way to leave a screen, so both rank high.
- **"Marka może żyć gdzie indziej."**
  - The rationale in `app.scss:102-106` ("the brand is the only thing telling the member which
    app this is") is set aside as a product decision.
  - That moves the job of carrying the brand onto dimension 4 (icon, splash, status-bar colour)
    and the Start screen.
- **Android back after tab switches is unverified by the user.** The code predicts it walks back
  through every visited tab.

## Cross-System Convention

- **Native mobile, both Material and iOS.** The top app bar is part of the chrome and owned by
  the shell:
  - A root destination shows its title and no back arrow.
  - A child screen shows a back/up arrow and its own title.
  - Switching tabs does not grow the back stack. On Android, back from a non-start tab goes to the
    start tab, then exits.
  - A sheet or dialog is closed by back.
  - The large in-content title is either the app bar's own expanded state or absent, never a
    second, separate heading.
- **Prior history in this project.**
  - A header redesign was repeatedly scoped *out*, not rejected on merit:
    - S-12 "No redesign of the desktop header" (`archive/2026-09-06-member-and-admin-dashboards/plan.md:77`)
    - S-25 "No visual redesign of header or bar" (`changes/role-based-visibility/plan.md:144`)
    - UX-09 deferred on 2026-09-20 (`roadmap.md:128-131`)
  - Nothing in the archive argues against a phone app bar.
- **Inverse check.** If the shell lacks a model of screen identity and depth, we should find:
  - no shared title source
  - hand-rolled per-screen back links
  - tab history that grows
  - no layout service

  All four are present. Nothing contradicts the hypothesis.

## Reframed Problem Statement

> **The actual problem to plan around is**: on a phone, the shell has no notion of *which screen
> this is* or *how deep it sits*. The title lives in each screen's scrolling content, and "back"
> is ordinary web history. So the app can neither show a native top bar nor make Android back
> behave natively.

- **The initial framing is right but covers half of it.** A title in the header is the visible
  symptom of dimension 1, and it comes from the same missing thing as the broken back behaviour
  (dimension 3).
- **The shared fix.** Once the shell knows a screen's title and whether it is a root tab or a
  child, both the title and the back behaviour follow:
  - title bar vs back arrow
  - tab switches that replace history instead of pushing it
  - "Wróć" that pops history
  - overlays that consume back
- **Plan for both together, not one after the other.** A title bar planned alone would duplicate
  the in-content `h1` (dimension 2), and still leave back broken.
- **The logo is not really a header question.** With the brand moved out of the header, it needs
  a home: the platform chrome (dimension 4) and the Start screen.

## Confidence

**HIGH** for the problem statement. It rests on strong code evidence in all four dimensions, it
matches native convention, and the user's narrowing answers were decisive.

One verification step for /10x-plan:
- On the installed Android PWA, confirm how back behaves across tab switches and on a detail
  screen.
- The code predicts "walks through tabs, re-enters detail". The plan should cite the observed
  behaviour, not the prediction.

## What Changes for /10x-plan

**The plan should be about:**
- a phone app bar owned by the shell, with the screen's title and a root-vs-child notion;
- the title moving out of the in-content `h1` (or merging with it);
- the Android back stack (tabs, "Wróć" links, overlays) as one unit;
- the Android status-bar and splash colours as the brand's new home.

**Explicitly deferred to follow-ups:** touch feedback, loading and caching, bottom sheets'
visual form, and iOS-specific meta.

## References

- Research: `context/changes/mobile-native-feel/research.md`
- Source files:
  - `src/app/src/app/app.html:3-4`, `app.scss:27-44, 95-109`
  - `shared/bottom-nav/bottom-nav.html`, `core/layout/view-transitions.ts:20-29`
  - `core/layout/navigation.ts:86-97`, `app.routes.ts`
  - `styles.scss:82, 184-198, 430-450`
  - `features/my-plan/plan-exercise-detail.html:6,14-17`, `features/trainer/plans/plan-builder.html:3-17`
  - `features/class-bookings/class-bookings-overlay.ts:78-80`
  - `public/manifest.webmanifest:9-10`, `index.html:22`
- Investigation: evidence from the three /10x-research sub-agents, plus an in-frame check of
  `bottom-nav.html` history behaviour. No extra hypothesis agents were spawned, because every
  dimension already had file-level evidence.
