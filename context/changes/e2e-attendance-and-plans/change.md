---
change_id: e2e-attendance-and-plans
title: A browser-level test covers attendance and a trainer's plan
status: impl_reviewed
created: 2026-09-28
updated: 2026-09-29
---

## Notes

Roadmap item S-32 (outside any milestone, test tooling). Manual plan cases `ATT-01`, `ATT-02`,
`PLAN-01`, `MBR-05`, `MBR-06` (`context/testing/`). Builds on S-31's support layer.

User's decisions (2026-09-28, Polish, recorded in English):

- A started class is made by booking a future class, then editing its start into the past
  (`PUT /api/admin/classes/{id}`, which deliberately skips the `starts_in_past` check). No waiting.
- A started class with bookings can be neither deleted nor cancelled, so cleanup leaves it behind
  as history, as with members and karnets.
- The karnet is asserted before AND after: `4 z 5` once the class has started, `5 z 5` after
  "Nieobecny", so the spec proves the absence returned the entry.
- The plan's order is set by a real pointer drag in the builder. Unit fallback only if it proves
  unstable.
- The exercise detail is asserted on its description and instructions, not on a video, so the local
  gate never depends on YouTube.
- Parameters: one meaningful parameter per row plus one note, asserted on the right exercise after
  the drag.
