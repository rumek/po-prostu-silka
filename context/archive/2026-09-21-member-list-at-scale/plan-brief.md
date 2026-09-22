# Member List at Scale — Plan Brief

> Full plan: `context/changes/member-list-at-scale/plan.md`

## What & Why

The admin's member list (Członkowie) fetches every member on every visit and searches them in the
browser. That is fine for a demo, not for a club with hundreds of members. This slice (roadmap S-21,
M-7 UX-05/UX-06) moves paging, search and filtering into the API, and renders the list as a table on
wide screens and one compact row per member on a phone.

## Starting Point

`GET /api/admin/members` returns a bare array of everyone matching a status filter, ordered by name
alone. `members.ts` searches it client-side, accent-sensitively. A second consumer, the class-bookings
"Dopisz członka" picker, loads the whole active club into a `<select>`. About 24 backend test call
sites find a member by fetching the full list, and the integration tests share one database.

## Desired End State

`/admin/members?q=kowal&filter=Active&page=3` is a real address. Typing searches the server after a
pause, and "lukasz" finds "Łukasz". A pager reads `1–25 z 312`, and coming back from editing a member
lands on the same page. From 640px the list is a table with columns; on a phone each member is one
row with the name, the status and Akcje. The booking picker searches instead of loading the club.
Nothing in the SPA fetches the whole member list any more.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Search semantics | Substring on name or e-mail, case- and accent-insensitive, `ł` folded explicitly | An admin on a phone rarely types Polish diacritics, and `ł` has no Unicode decomposition to fold. |
| Request rate | ~300 ms debounce, load fence discards stale answers | A search box, not a load test. This answers the roadmap unknown. |
| Paging UI | Poprzednia / Następna + `1–25 z N`, fixed page size 25 | Works the same at every width, with the smallest contract. |
| List state | `q` / `filter` / `page` in the URL | At hundreds of members, losing the page and phrase after every edit is a real cost. |
| Booking picker | Becomes a server search on the same endpoint (`filter=Active`, 20 results) | Removes the last "fetch everyone" without a second endpoint or silent truncation. |
| Contract migration | Change the response shape in place, accept the stale-tab window | One artifact ships SPA + API. Only a tab open across the deploy breaks, until reload. |
| Table breakpoint | Existing `form-columns` mixin (40rem) | Five columns stay legible there. No new threshold. |
| Two shapes | One DOM: a semantic `<table>`, collapsed to rows in CSS | AGENTS.md: a layout-only difference stays in the stylesheet. |
| Filter chips | Stay chips | Four chips fit; changing them is a redesign (UX-09). |
| Ordering | `DisplayName, Id` | Without the tiebreak, same-named members duplicate or vanish across pages. |

## Scope

**In scope:**
- Paged/searched `GET /api/admin/members` with `PagedResult<T>`, 400 on invalid paging
- Test helper and migration of ~24 test lookups to search by e-mail
- Members screen: server search, pager, URL state
- Booking picker as a search
- Table / compact-row layout, AGENTS.md and roadmap notes

**Out of scope:**
- Visual redesign, sortable columns, page-size chooser, "load more"
- A search index, full-text or a normalised search column
- The trainer's member list and member-centric plans (S-22)
- Paging other lists (exercises, class types, plans)

## Architecture / Approach

The API applies filter → search → count → order (`DisplayName, Id`) → `Skip/Take` → the existing
projection, and wraps the result in `PagedResult<MemberSummary>`. In the SPA, the URL's
`queryParamMap` is the only thing that triggers a load. Chips, the pager and the debounced search box
navigate, and the existing load fence discards out-of-order responses. The table is one template; CSS
at `bp.form-columns` decides whether it lays out as columns or as compact rows.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. API pages, searches and filters | Envelope, search folding, stable order, 400s, test-helper migration | EF translation of collate + replace + contains; checked against the real engine |
| 2. Members screen asks the server | Server search with debounce, pager, URL state | URL ↔ load loops, and normalising junk params |
| 3. Booking picker searches | Overlay search on the same endpoint | Picker UX regression for small clubs (no scroll-the-whole-list) |
| 4. Table on web, row on phone | Semantic table collapsing in CSS at 40rem | Row menu clipping inside table cells |

**Prerequisites:** S-20 (the breakpoint partial) landed in code. Its uncommitted working-tree changes
should be committed before phase 1 starts.
**Estimated effort:** ~3–4 sessions. Phases 1–3 form one deployable unit: no push between them.

## Open Risks & Assumptions

- An admin tab running the old SPA through the service worker during the deploy shows the list's
  failure state until it reloads. This is accepted.
- The substring search scans `Members`. That is assumed cheap at one club's scale (low thousands at
  most); the debounce bounds the request rate.
- On a phone, collapsing a `<table>` with CSS may drop table semantics in some browsers. That is
  acceptable, because the compact row carries only name, status and actions.

## Success Criteria (Summary)

- An admin finds one member among hundreds by typing part of a name or e-mail, with or without Polish
  letters, and the page and phrase survive a trip into the member's record.
- The list reads as a table on a laptop and as one row per member on a phone, with every action
  reachable at both.
- No screen downloads the whole member list.
