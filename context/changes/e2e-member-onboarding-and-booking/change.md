---
change_id: e2e-member-onboarding-and-booking
title: A browser-level test covers the invitation claim and a staff booking
status: impl_reviewed
created: 2026-09-28
updated: 2026-09-28
---

## Notes

Roadmap item S-31 (outside any milestone, test tooling), stream I. Manual plan cases `REG-01`,
`BOOK-01`, `BOOK-03` (`context/testing/`).

User's decisions (2026-09-28, Polish, recorded in English):

- The registration rate limit is NOT under test here. Specs step around it by sending a unique
  `X-Forwarded-For` on registration; no production change.
- Members and accounts a run creates stay behind, named `E2E … <ts>` with `@example.test`
  addresses. Everything that can be removed (booking, class, karnet, class type) is.
- One fixed E2E trainer, get-or-created once per run by a setup project, so a fresh database (CI,
  S-30) works.
- Three spec files, one test each.
- Arrange through the API; drive through the UI only where the risk lives.
- The staff booking is made by the E2E trainer on a class they instruct, not by the admin.

Later decisions the same day (beyond the plan's "no production code"):

- E2E runs locally only — never on staging, never in CI — through `.githooks/pre-push`. Roadmap
  S-30 was re-scoped from CI to this local gate and ships inside this change.
- A booked class is cancelled in cleanup rather than deleted, and the karnet that paid for it stays.
- `RevokePass` no longer answers 500 for a karnet whose bookings were all released: it refuses with a
  new reason, `has_booking_history` (409). Production code, integration test and SPA message.
