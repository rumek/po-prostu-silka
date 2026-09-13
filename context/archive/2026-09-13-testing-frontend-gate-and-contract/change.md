---
change_id: testing-frontend-gate-and-contract
title: Testing frontend gate and contract
status: archived
created: 2026-09-13
updated: 2026-09-13
archived_at: 2026-09-13T19:58:58Z
---

## Notes

Rollout Phase 3 of `context/foundation/test-plan.md` (risks #6 and #7).

Decisions taken during planning, recorded here because they bound what the plan may do:

- The CI gate is two steps added to the existing `.github/workflows/deploy.yml`, not a new workflow and
  not a non-blocking job.
- Local layers: a `PostToolUse` per-edit hook and a lefthook pre-commit. No pre-push, and no backend
  post-edit hook — `dotnet test` starts a SQL Server container and would block the agent loop.
- The `reason` contract is pinned where the SPA reads it. A declared catalogue of all 60 codes was
  rejected as a snapshot of today's state.
- Drift found while writing tests is recorded (§6.6 note or a roadmap question), never fixed here —
  the Phase 1 / Phase 2 precedent.
- New message tables for exercise / training-plan / class-type belong to S-19 (CS-05), not here.
