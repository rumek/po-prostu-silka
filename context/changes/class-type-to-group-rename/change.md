---
change_id: class-type-to-group-rename
title: Class types are called groups, from the screen down to the table
status: impl_reviewed
created: 2026-10-01
updated: 2026-10-01
---

## Notes

Roadmap item S-33 (outside any milestone).

User's decisions (2026-10-01, Polish, recorded in English):

- A full rename: UI, SPA routes, API, code and database. "Grupa" is a new word for the same thing a
  "typ zajęć" was, not a new concept (not a fixed set of members).
- Code name `ClassGroup` (`class-groups` in URLs, `class_group` in reason codes).
- UI words: "Grupy" / "grupa" ("Nowa grupa", "Edytuj grupę", field "Grupa").
- Database: a rename migration is accepted **as a one-off exception** because the only environment
  is still staging. The deploy-window outage (migrations run before the deploy, so the old build
  serves against the renamed schema for a few minutes) and the rollback gap (`rollback.yml` runs no
  migration) are accepted; no rehearsal of `Up`/`Down` against a copy of real data. This is NOT a
  precedent for future schema renames.
- A new `group` glyph replaces `repeat` for groups; `repeat` keeps meaning "Powtórzenia" only.
- No redirect from `/admin/class-types`.
- Only living docs are updated; `prd-v2.md`, `shape-notes.md`, the roadmap's history and
  `context/archive/` keep the old word.
