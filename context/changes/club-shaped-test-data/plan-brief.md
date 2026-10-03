# Club-Shaped Test Data — Plan Brief

> Full plan: `context/changes/club-shaped-test-data/plan.md`

## What & Why

Staging runs on the S-24 test club: a 200-member chain gym with random daily classes. It shows none of
the features built since: rosters, attendance, makeups. This change reshapes it into the club its
spreadsheets describe. `treningi.xlsx` supplies the fixed weekly groups of 1–6 people, attendance and
makeups; `twt.xlsx` supplies the karnet types and payment states. A demo or a manual test then looks
like the client's own club.

## Starting Point

`TestDataGenerator` builds the club in memory from a fixed seed and mirrors `BookingProtocol` by hand,
because the seeder writes straight to the database. `TestDataSeeder` persists it in one save, behind
fail-closed gates. Rosters are already wiped on reset, but they are never seeded.

## Desired End State

A reset-and-seed produces about 100 members with:
- about 30 fixed weekly groups (individual, pairs, trios, groups of up to 6), each with a roster, a fixed
  time and a trainer, never overlapping, plus a few substitutions, moved times and cancellations;
- "Miesięczny" karnety (4 entries, 30 days), one-offs and vouchers, with every payment state present on
  any day;
- marked attendance;
- makeups in every state.

Every row is one the app itself could have produced.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Scale | ~100 members (~70 with account) | Medium: enough for lists and filters, still studio-shaped. |
| Trainers | 4 fictional + admin2 teaching | The spreadsheet's real names stay out of seed data. |
| Groups | ~30, once a week each | The spreadsheet's "co 7 dni" model. |
| Mix | ~10 individual, 8 pairs, 5 trios, 7 groups of 6 | The spreadsheet treats all four kinds equally. |
| "Miesięczny" | 4 entries / 30 days | A fifth weekly occurrence naturally shows "brak wejść", as it would at the club. |
| Karnet types | Miesięczny, Wejście jednorazowe, Voucher | The full set from `twt.xlsx`. |
| Attendance | Marked past, last 2 days partly open; makeups in every state | The Odrabianie screen and attendance have content at once. |
| Payment states | Guaranteed counts (expiring, unpaid, expired) | The cards and filters are never empty, whatever the reseed day. |
| Accountless | ~30%, half with live invitation codes | The club enters clients itself; the S-24 invitation cases stay. |
| Group names | Individual named after the person; others after day and time | Mirrors how the spreadsheet names its sheets. |
| Overlap | None, even across trainers | The app refuses overlap club-wide. |
| Accounts | `admin1`, `trener1/2`, `czlonek010` keep their meaning | The manual test scripts depend on them. |

## Scope

**In scope:**
- `TestDataNames`, `TestDataGenerator` and roster persistence in `TestDataSeeder`;
- the generator invariant tests and updated seeder tests;
- the runbook and test-environment docs.

**Out of scope:**
- schema changes (karnet price and payment form);
- real names from the spreadsheets;
- changes to the seeder's gates, reset or sentinel;
- legacy `Absent` marks.

## Architecture / Approach

The group is the unit. The generator:
1. builds a disjoint weekly slot table;
2. fills the rosters;
3. issues karnety timed against today;
4. walks each group's weekly occurrences, applying the scripted exceptions;
5. books roster members in start order through an in-memory mirror of the protocol, then places
   one-offs, vouchers and makeups in the free spots;
6. marks attendance last and derives the makeup items.

Pure invariant tests run over 10 consecutive days, including a DST change, and check the result against
the app's own rules: `EntryConsumption`, `MakeupRules.StateOf` and the overlap rule.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. The club-shaped generator | The whole new data set plus invariant and seeder tests | A guarantee that holds on one reseed day but not another — covered by the 10-day sweep. |
| 2. Docs and reseed | Runbook and test-environment docs; local and Staging reseed and walkthrough | Staging reseed needs `TestDataSeed__Reset=true` set and then cleared. |

**Prerequisites:** PR #19 (group fixed roster). This branch is cut from it; rebase onto `main` once #19
merges.
**Estimated effort:** about 2 sessions. Phase 1 is the bulk.

## Open Risks & Assumptions

- Making 30 one-hour weekly slots disjoint on Mon–Sat fits easily (about 5 per day across 06:15–21:30).
- The past window grows from 28 to 35 days, so an absence can pass its 30-day makeup deadline.

## Success Criteria (Summary)

- `dotnet test` is green, and the invariants prove that every seeded row is one the app could have
  written.
- After a reseed, every main screen shows the club as the spreadsheets describe it: Start, Grupy/Skład,
  Odrabianie, karnety, unpaid filter, trainer view, member view.
