---
change_id: schedule-calendar-view
title: Member and admin browse the schedule as a calendar
status: impl_reviewed
created: 2026-09-02
updated: 2026-09-03
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
- Manual verification completed 2026-09-03 against the deployed environment, in two passes. The first
  returned five defects — a full-day grid, American time and date formats, a drag preview that was
  specified but never drawn, a create gesture that only learned about the past from the API, and no way
  to move or resize an existing class. Those became the follow-up commits recorded as Phase 5, which
  also carried `prd-v2` FR-020 and the FR-015 amendment restoring the weekday strip as navigation. The
  second pass passed clean.
