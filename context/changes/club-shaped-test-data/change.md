---
change_id: club-shaped-test-data
title: Reshape the S-24 test data on the club's spreadsheets
status: implementing
created: 2026-10-03
updated: 2026-10-03
archived_at: null
---

## Notes

Reshape the S-24 test data (`src/Infrastructure/TestData/`) so Staging looks like the club described in
`xlsx/treningi.xlsx` (attendance and makeups) and `xlsx/twt.xlsx` (karnety and payments):

- fixed weekly groups of 1, 2, 3 or up to 6 people, each with a fixed time and trainer, and a fixed roster (S-37);
- attendance marks (Był / Nie był – odrobi / Nie był – przepada) and makeups (S-27, S-36);
- karnet types from the spreadsheet, and payment states including ending, expired and unpaid.

Decisions (2026-10-03, with the user):

- Medium scale: about 100 club members.
- Fictional trainer names, not the four from the spreadsheet.
- The spreadsheets supply the SHAPE only. `twt.xlsx` holds real clients' names, and none of them goes
  into seed data.
- The branch is cut from `group-fixed-roster`, because rosters need PR #19. Rebase onto `main` once #19
  merges.
