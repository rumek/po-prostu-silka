# Expiring Karnets Dashboard Card — Plan Brief

> Full plan: `context/changes/expiring-passes-dashboard/plan.md`

## What & Why

The club's spreadsheet flags a client "KOŃCZY SIĘ" from five days before their karnet ends, so that
nobody misses a renewal. The admin gets the same signal on Start: a card listing the members whose karnet
is about to end. Each row leads to that member's karnet screen, where the renewal is issued.

## Starting Point

The app knows each member's current karnet and its end date (`MemberSummary.PassValidTo`), but nothing
compares that date with today. pass-paid-flag added an orthogonal `unpaid` filter to the member list
(server flag + `unpaid=1` URL state + `app-checkbox`). That is the mechanism this change reuses.

## Desired End State

An admin's Start opens with a "Klub" section, "Kończą się karnety". It lists up to 5 members, nearest
end first, with "dziś / jutro / za N dni" and the date. Each row links to the member's karnet screen, and
"Zobacz wszystkich (N)" opens `/admin/members?expiring=1`, whose count is exactly N. Renewing a member
takes them off both.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Shape and persona | Admin-only card on Start, top 5 + "Zobacz wszystkich (N)", compact empty state | The trainer has no member list with karnets to link to | change.md |
| What "ending" means | Current karnet's `ValidTo` within today…today+5, no later karnet | A renewal issued in advance is something the app knows and the sheet can't | change.md |
| Window bounds | Inclusive 0…5 club-local days | Matches the sheet's `G<=TODAY()+5`, so the two agree on every person | Plan |
| Unpaid + ending | Listed | Payment and renewal are separate questions; unpaid stays with pass-paid-flag's filter | Plan |
| Blocked members | Excluded | The card means "to renew", and a block means the club isn't renewing | Plan |
| Entries left / expired | Ignored / not shown | Tied to the end date only; no "WYGASŁ" line | change.md |
| Card order | `ValidTo`, then name, then id; the list stays alphabetical | Deterministic at the 5th/6th cut without changing the list's paging contract | Plan |
| List filter UI | Second checkbox "Tylko kończące się", `expiring=1`; no row marker | Same mechanism as unpaid; both screens are eager | Plan |
| Placement | Its own "Klub" section above "Twoje zajęcia" | It is the admin's action for today, and visible without scrolling on a phone | Plan |
| Day count | `daysLeft` computed on the server | One club-local `today` for predicate and label, so there's no browser/club midnight mismatch | Plan |
| One definition | `ExpiringPassPredicate` in Infrastructure, read by list and card | The card's N always equals the filtered list's total | change.md |

## Scope

**In scope:** predicate; `expiring` on `GET /api/admin/members`; `GET /api/admin/members/expiring-passes`;
member-list checkbox + URL state; Start card; integration, unit and one E2E test; bundle record.

**Out of scope:** configurable threshold, expired line, low-entries signal, unpaid on the card, list row
marker, list re-sorting, trainer view, notifications, schema changes.

## Architecture / Approach

`ExpiringPassPredicate.IsExpiring(db, today)` is an `Expression<Func<Member,bool>>` in the style of
`StaffPredicate`, made of correlated `EXISTS` subqueries. `MemberQuery.GetMembersAsync` applies it
before the count when `expiringOnly` is set. `MemberQuery.GetExpiringPassesAsync` counts and takes 5
from the same queryable, projecting the current `ValidTo` and `daysLeft`. The SPA's
`MemberAdminService` gains `expiring` and `getExpiringPasses()`. `members.ts` carries `expiring=1`, and
`dashboard.ts` renders the card for persona `admin` only, loaded in parallel with the classes feed.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Predicate, list filter, card endpoint | Server side + 12-case predicate tests + card tests in their own container | Shared test DB polluting club-wide assertions (hence the own collection) |
| 2. Member list checkbox | `expiring=1` URL state and "Tylko kończące się" | Eager bundle growth on `/admin/members` |
| 3. Start card | Admin-only "Kończą się karnety" | Persona leak (a trainer requesting it); eager bundle |
| 4. E2E | Renewal clears the member from card and list | Asserting on club-wide counts in a shared local DB |

**Prerequisites:** `pass-paid-flag` merged into `main`; branch from there.
**Estimated effort:** ~2–3 sessions across 4 phases.

## Open Risks & Assumptions

- Bundle: 580.70 kB with ~19 kB to the warning. Two eager screens grow, so both get measured.
- Tests run on the system clock. A test straddling club-local midnight could flake, the same accepted
  risk as the existing pass tests.
- "Renewed" means any later karnet, even after a gap. A member renewed for a date weeks away drops off
  the card.

## Success Criteria (Summary)

- An admin sees who needs a renewal on Start without opening the spreadsheet, and the list agrees with
  the card number for number.
- A member whose next karnet is already issued never appears.
- A trainer's Start is unchanged and makes no new request.
