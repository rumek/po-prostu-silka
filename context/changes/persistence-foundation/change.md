---
change_id: persistence-foundation
title: Provision Azure SQL and wire EF Core persistence
status: implementing
created: 2026-08-31
updated: 2026-08-31
archived_at: null
---

## Notes

Roadmap item **F-01** (milestone `first-usable-mvp`). The first infra-touching change since go-live.

Gives the app a database: Azure SQL (Basic DTU) provisioned, EF Core wired with a bootstrapped
DbContext, schema migrations applied through CI on every deploy, connection string in App Service
settings. Ships plumbing plus one proving migration — not the whole schema.

Unlocks F-02 (auth-identity-foundation), F-03 (notification-delivery-foundation), and transitively
every slice S-01…S-09.

Planned via `/10x-plan` — see `plan-brief.md` (start here) and `plan.md`.
