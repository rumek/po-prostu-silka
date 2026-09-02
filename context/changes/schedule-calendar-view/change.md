---
change_id: schedule-calendar-view
title: Member and admin browse the schedule as a calendar
status: implementing
created: 2026-09-02
updated: 2026-09-02
---

## Notes

<!-- Free-form notes for this change: links, ad-hoc context, decisions that don't belong in research/frame/plan. -->

- Roadmap item: `S-07` in `context/foundation/roadmap.md`.
- Source requirements: `context/foundation/prd-v2.md` — US-02, FR-015 through FR-018; supersedes
  `context/foundation/prd.md` FR-007 in part.
- Predecessor: `context/archive/2026-09-02-occurrences-from-class-types/` (S-06) settled the class
  model this calendar renders — type + occurrence, no room, instructor as an account.
- No `frame.md` or `research.md` — this plan was built from both PRDs, direct codebase reading, and
  external verification of `angular-calendar` on npm and its published changelog.
- **This change deliberately departs from `prd-v2` FR-015 as written.** The library decision
  (`angular-calendar`) replaces the phone day-strip with a day view, and adds drag-to-create, which
  FR-015 through FR-018 do not ask for. Phase 1 amends the PRD rather than leaving it contradicted.
