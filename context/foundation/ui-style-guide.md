# UI style guide

The visual language of the SPA, written down from the screens that set it: **Start** (`features/dashboard`),
**Mój plan** (`features/my-plan`), the **exercise** screens (`shared/exercise-view`,
`features/admin/exercises`), **Więcej** (`features/more`) and **Moje konto** (`features/profile`).
**Grafik** (`features/schedule` + `shared/calendar`) was brought into line with them after the fact.

Read this before styling a screen. It is a living doc: edit it in place when a pattern changes. It does
not repeat the structural rules in `AGENTS.md` — the presentational kit (S-23), the failure outlets
(S-19), breakpoints (S-20), screen identity — it sits on top of them.

## Principles

1. **Warm paper, one accent.** A cream page, white cards, brown ink. Colour carries meaning (accent =
   "you are here / go", danger = error, success = done); it is never decoration and never the only
   channel — a state is always also a word or a weight.
2. **Tokens only.** Every colour, radius, space and shadow is a custom property from `src/styles.scss`.
   A component stylesheet declares no colour literal; a new colour is a new token, argued for there.
3. **One display moment per screen.** The serif (`--font-display`) is the screen's `h1` (and a hero
   panel's title). Everything below it — block titles, card titles, calendar headings — is the body face.
4. **The class card is the unit.** Almost every card in the app is the same reading order: a small
   capitals line, then a figure beside a hairline rule, then a name over a quieter line. New cards reuse
   it rather than inventing a layout.
5. **One meaning, one glyph.** Icons come only from `shared/icons`, and a meaning has one icon
   everywhere. No text glyphs (`«`, `»`, `▼`, `⠿`) — they take the font's weight, not the icon set's.

## Tokens

| Token | Value | Use it for |
| --- | --- | --- |
| `--page` | `#f7efe7` | The page background. Nothing else. |
| `--surface` | `#fff` | Cards, the calendar grid, form controls — the paper things sit on. |
| `--ground` | `#f7f3ef` | Pill tracks, chips, row hover inside a card, disabled controls. |
| `--section-warm` | `#ece3dc` | Filled-but-quiet: avatar discs, calendar tiles, empty thumbnails, pill hover, progress tracks. |
| `--section-cool` | `#edeae5` | Callouts (the plan note), `.notice`, a full class's tile. |
| `--ink` | `#272321` | Body text, values. |
| `--accent` | `#654b45` | Selected pill fill, navigation chevrons, kicker icons, today, the tile's time. |
| `--accent2` | `#917773` | Card fact icons (instructor, parameters, rows in Więcej), the tile's left bar, punch marks. |
| `--gray` | `#9d9b9a` | Kicker text, parameter labels, the secondary half of a figure ("z 10", end time). |
| `--muted` | ink @ 62% | Meta lines, hints, empty states. |
| `--line` / `--line-strong` | ink @ 14% / 30% | Hairlines between rows / the rule beside a figure, control borders. |
| `--danger` / `--danger-bg` | | Errors and "Brak miejsc" only. |
| `--success` / `--success-bg` | | Confirmation toasts only. |
| `--radius` / `--radius-sm` | 10px / 6px | Cards and the grid / controls, tiles, thumbnails, callouts. |
| `--shadow-card` | | Every card, pill track and hero panel. One shadow; don't write another. |
| `--space-1`…`--space-7` | 0.25–3rem | All spacing. `2px`/`3px`/`4px` literals are allowed only for hairline gaps inside a pill or an icon row. |

## Type

| Role | Recipe | Where |
| --- | --- | --- |
| Screen title | global `h1` (display serif) + `class="screen-title"` | Hides on a phone; the app bar says it. |
| Block title | body face, `1.125rem`, 600, margin 0 | Above a card: `.dashboard-card-title`, `.profile-title`, `.more-title`, `.calendar-title`. |
| Kicker | `0.7rem`, 600, `letter-spacing: 0.06em`, uppercase, `--gray`; icon `--accent` at `0.8rem` | The first line of a card: class date, karnet type, "1 / 5". |
| Name | `.row-name` (600) | The thing the card is about. |
| Meta | `0.875rem`, `--muted`; icon `--accent2` at `0.7–0.8rem` | Instructor, validity, e-mail. |
| Figure | `--font-display` 2.5rem (the one answer on a screen) or 700 ink over 300 `--gray` | Entries left over total; start over end. |
| Label / value pair | label `0.75rem --gray`, value 700 `--ink` | Plan parameters, exercise facts. |
| Hint | `.hint` (`0.875rem`, `--muted`), `max-width: 44rem` | One or two lines under the title. |

Numbers that line up down a list or a grid take `font-variant-numeric: tabular-nums`.

## Screen anatomy

- **Member tabs with a photo** (Start, Moje zajęcia, Mój plan, the exercise library) open with a
  `.screen-hero`; what the screen says about itself sits on a `.hero-panel`. Staff tools (Grafik, the
  admin lists) have no hero — they open with the title, a hint and the tool.
- **Single-column screens** (Moje konto, Więcej) are a `:host` grid, `gap: var(--space-5)`,
  `max-width: 36rem`; the screen title's own margin is zeroed so the gap is the only rhythm.
- **Block = title above card.** The heading names the block and sits outside the card; the card holds
  the content. A block is `display: flex; flex-direction: column; gap: var(--space-2)`.
- **Card density.** Summary cards: `padding: var(--space-5)` (dashboard, profile, exercise guide).
  List cards, one per item: `padding: var(--space-3) var(--space-4)` (plan items, tiles).
  Rows edge-to-edge inside one card: the card has no padding, each row pads itself (Więcej).
- **Grids of cards** size themselves: `repeat(auto-fit | auto-fill, minmax(min(100%, 16–18rem), 1fr))`,
  no media query. Tiles use `app-list.list--tiles`.

## Patterns

| Pattern | Recipe | Reference |
| --- | --- | --- |
| Figure · rule · text | figure column centred; text column `padding-left: var(--space-3); border-left: 1px solid var(--line-strong)` | `shared/class-date/booked-class`, the karnet card |
| Hairline rows | `li + li { border-top: 1px solid var(--line) }` and padding-top | `.dashboard-list`, `.more-list` |
| Parameter strip | grid of equal columns under a hairline, icon over label over value, `border-left` only *between* columns | `.my-plan-params`, `.exercise-view-facts` |
| Card foot | actions under a `--line` hairline, pushed down with `margin-top: auto` | `.exercises-actions` |
| Callout | `--section-cool`, `--radius-sm`, `var(--space-3)` padding, `--accent2` icon centred | `.my-plan-note` |
| Chip vs badge | `.chip` = a quiet fact (muscle group); `.badge` = a state, outlined, carries a word | `src/styles.scss` |
| Avatar disc | 3rem circle, `--section-warm`, ink icon | `.more-avatar`, `.profile-avatar` |
| Pill track | `--ground` track, `padding: 4px`, `gap: 2px`, `border-radius: 32px`, `--shadow-card`; cells `border-radius: 32px`, ≥ 40px tall, hover `--section-warm`; **selected = `--accent` fill + light text + 600** | Zajęcia tabs, bottom bar, desktop menu, calendar strip and week nav |
| "See all" link | `0.8125–0.875rem`, `--accent` `chevron-right` after the words | `.dashboard-header-action`, `.exercises-open` |
| Progress | 6px track in `--section-warm`, fill `--accent2`, radius 3px | Karnet punch card and bar |
| Calendar tile | `--section-warm`, `border-left: 3px solid var(--accent2)`, `--radius-sm`; time `--accent` tabular, name 700, facts with `person` / `members` icons; full = `--gray` bar on `--section-cool` + the words "Brak miejsc" | `shared/calendar` |

The pill track is still declared per component (four copies). A fifth copy is the point to lift it into
`src/styles.scss` — weigh that against the eager bundle, since the global stylesheet ships to everyone.

## Icons

- Size through `font-size` on the caller (the SVG is `1.5em`): `0.6–0.7rem` inline in a tile,
  `0.8rem` in a meta line or kicker, `0.875rem` heading a column, `0.9rem` beside a link.
- Colour by role: **`--accent`** for navigation (chevrons, the kicker's leading icon, the strip arrows);
  **`--accent2`** for a fact about the thing (who, how many, how long); **`--muted`** inside a form label.
- Current meanings: `person` = who instructs · `members` = how many are signed up · `calendar` = a
  date · `ticket` = the karnet · `clock` = rest · `time` = hold duration · `repeat` = reps · `sets` =
  sets · `info` = more to read · `chevron-left` / `chevron-right` = previous / next or "go" ·
  `chevron-down` = a select · `back` = up to the parent. Add a case to `shared/icons/icon.html` rather
  than reusing one for a second meaning.

## Interaction

- Hover: `--section-warm` on a transparent control, `--ground` on a row inside a white card.
- Focus: the global `:focus-visible` ring; don't restyle it per component.
- Selected: accent fill, light text, weight 600 — never colour alone.
- Today: `--accent` text plus an underline bar, distinct from "selected" (both can be the same day).
- Motion: `200ms ease` on background/colour at most, always paired with a
  `prefers-reduced-motion: reduce` opt-out; slides only for routed screens and tab panels.
- Tap targets ≥ 40px (pills) / 44px (buttons, rows). Inputs keep `font-size: 1rem` — below that iOS
  zooms on focus.

## Third-party markup

A library that brings its own look (angular-calendar) is re-skinned under one anchor with `::ng-deep`,
in a stylesheet of its own, and must not leak its palette: the calendar's zebra rows, grey hovers and
dark-red weekends are all overridden to tokens in `shared/calendar/schedule-calendar-library.scss`.

## Checklist for a new or restyled screen

- [ ] Colours, radii, shadows and spacing are tokens; no literals in the component stylesheet.
- [ ] One serif heading; block titles in the body face above their cards.
- [ ] Cards use the class-card reading order, the density above, and `--shadow-card`.
- [ ] Icons from `shared/icons`, coloured by role; no text glyphs.
- [ ] Selected/active states use fill + weight; errors and full states also say so in words.
- [ ] Transitions have a reduced-motion opt-out; tap targets meet 40/44px.
- [ ] The component stylesheet stays under `angular.json`'s 6 kB `anyComponentStyle` budget.
