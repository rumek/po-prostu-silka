# Mobile native feel — Plan Brief

> Full plan: `context/changes/mobile-native-feel/plan.md`
> Frame brief: `context/changes/mobile-native-feel/frame.md`
> Research: `context/changes/mobile-native-feel/research.md`

## What & Why

> On a phone, the shell has no notion of *which screen this is* or *how deep it sits*. The title
> lives in each screen's scrolling content, and "back" is ordinary web history. So the app can
> neither show a native top bar nor make Android back behave natively.

That problem statement comes from the frame. The user sees two symptoms on an installed Android
PWA:
- the screen looks like a web page (a logo where a title should be, a big serif heading);
- back and exit behave like a browser.

Both come from the same missing screen model, so one change fixes both.

## Starting Point

- The phone header is a 140px logo that scrolls away.
- Every screen writes its own `h1`. No route has a title.
- Every bottom-bar tap pushes a history entry.
- The "Wróć…" links push too.
- Overlays ignore back.
- The S-26 slide knows only "popstate or not".
- The manifest paints a dark slate status bar and a white splash over a cream app.

## Desired End State

- **The bar.** A pinned top bar on the phone:
  - the logo on Start;
  - the screen title on tabs;
  - a back arrow plus title on child screens.
  The large heading and the in-content back link are gone on the phone. The desktop is unchanged.
- **Back behaves as on Android.**
  - Tab switches do not grow history.
  - Back goes from a tab to Start, and from Start out of the app.
  - A child screen pops cleanly.
  - An open overlay closes.
- **The motion shows the hierarchy.** Tabs fade, push and pop slide, and search or paging does not
  move. The status bar and splash are cream.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| What to fix | Screen identity + back stack together, not a title bar alone | A bar alone duplicates the h1 and leaves back broken | Frame |
| Where the brand lives | Start's bar, icon, splash, status bar colour | "Marka może żyć gdzie indziej" | Frame |
| Bar vs in-content h1 | The bar is the heading on the phone; the screen h1 hides there | Material pattern; one h1 at any width | Plan |
| Start screen | Logo in the bar, greeting stays in the content | Gives the brand a home | Plan |
| Tab back stack | Push from Start, replace between tabs | Android convention | Plan |
| Overlays | Back closes them, via one shared helper (all 3 overlays) | Trainer keeps Grafik context | Plan |
| Bar on scroll | Always visible, CSS hairline | Title and back always reachable; no scroll listener | Plan |
| Motion | Tab fade, push/pop slide, skip on query-only | Motion tells where you are | Plan |
| Title source | Route `title` + `data.level/parent`, a `TitleStrategy`, `ScreenTitle.set()` for data-dependent titles | One table beside `navigationFor()`, enforced by spec | Plan |
| Phase granularity | Two phases | User: four was too granular | Plan |

## Scope

**In scope:**
- route identity for all routes;
- `ScreenTitle` + `TitleStrategy` (`document.title`);
- the phone app bar with a back icon;
- hiding headings and back links on the phone;
- the `/my-plan` heading fix;
- manifest and meta colours + `id` / `lang`;
- the tab replace rule;
- the up behaviour;
- hierarchy-aware transitions;
- back closing overlays;
- two Playwright specs.

**Out of scope:**
- touch feedback;
- loading, caching and the service worker;
- bottom-sheet visuals;
- iOS meta and splash images;
- shortcuts and install prompt;
- 16px inputs and tap targets;
- the adjacent bugs: staff push prompt, offline treated as logout, broken search tokens;
- hide-on-scroll;
- admin back-link rework.

## Architecture / Approach

The route table carries `title`, `level` (brand / tab / child) and `parent`. A custom
`TitleStrategy` publishes them as signals on `ScreenTitle`. Screens override the title when it
depends on loaded data. Everything else reads that one model:
- the shell's phone bar;
- the bottom bar's push/replace;
- the up service (pop if the parent is the previous entry, else replace-navigate);
- the view-transition kind;
- the overlay helper, which pushes a same-URL history entry on open.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Screen identity, phone app bar, colours | Titled or brand bar, back arrow, one h1 per width, cream status bar | The bar as a 5th fixed layer: z-scale and view-transition jump |
| 2. Back stack, motion, overlays | Android back semantics, fade/slide/skip, back closes overlays | The same-URL `pushState` must be ignored by the router (pinned by E2E) |

**Prerequisites:** Docker SQL Server. A seeded database for the manual checks (member and trainer
accounts). Node 24 for the SPA.

**Estimated effort:** ~2–3 sessions.

## Open Risks & Assumptions

- The router must ignore the same-URL `popstate`. This is Angular-internal behaviour; the E2E spec
  pins it, and an adaptation note goes in the plan if it differs.
- Android back after switching tabs is predicted from code, not yet observed. Manual check 2.6
  confirms it.
- There is one accepted deviation from Material. After a child screen, switching tabs leaves one
  extra back step, because a browser cannot prune history.
- The E2E specs run as the admin at phone width, which assumes the behaviour is persona-agnostic.

## Success Criteria (Summary)

- On an installed Android PWA, every signed-in screen has a native-looking pinned bar, with the
  logo on Start.
- System back walks screen → tab → Start → exit, and closes an open overlay first.
- Nothing on the desktop changes apart from the `/my-plan` heading.
