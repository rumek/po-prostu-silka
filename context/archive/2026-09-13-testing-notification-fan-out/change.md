---
change_id: testing-notification-fan-out
title: Prove class-change notifications reach every booked member and only them
status: archived
created: 2026-09-13
updated: 2026-09-14
archived_at: 2026-09-14T05:28:05Z
---

## Notes

Open a change folder for rollout Phase 4 of context/foundation/test-plan.md: "Class-change notification fan-out". Risks covered: #5. Test types planned: integration with fake channels. Risk response intent: #5 — cancelling or changing a class queues exactly one message per channel for each actively booked member with an address or device, and none for anyone else; a failed send is retried rather than lost (challenge: "an outbox row written means the member was notified"; avoid: asserting only on an outbox count, mocking the dispatcher itself). After creating the folder, follow the downstream continuation rule.
