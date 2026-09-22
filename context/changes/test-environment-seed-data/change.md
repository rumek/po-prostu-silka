---
change_id: test-environment-seed-data
title: Seed a realistic test data set for the development environment
status: implementing
created: 2026-09-22
updated: 2026-09-22
archived_at: null
---

## Notes

Seed the (single, still-in-development) Azure environment and local Docker DB with a realistic
data set for manual testing.

**Volume requested by the user (2026-09-22):**

- 200 sample members
- 2 accounts in the Admin role
- 2 trainers
- a few exercise sets (the exercise library)
- a few already-defined training plans

**Also in scope (user's answers, 2026-09-22):**

- Membership passes (karnety) — a mix of valid, expired and used-up, so staff booking can be exercised.
- Class types and a schedule of classes for the coming weeks, assigned to the two trainers.
- Bookings — some classes partly booked, some full (to exercise the no-overbooking guardrail).
  Bookings must respect S-16: every booking carries a pass valid on the class's club-local date
  with a free entry; entries left stay derived, never stored.
- Accountless members (S-14) — part of the 200 as records without an account, with a claim code.

**Decisions taken (user's answers, 2026-09-22):**

- **Mechanism: a config-gated startup seeder, not a migration.** A `TestDataSeeder` next to
  `AdminSeeder`, running only when a flag (e.g. `TestDataSeed:Enabled`) is set in app settings.
  There is only one Azure environment and `deploy.yml` applies migrations on every merge to `main`,
  so seed rows in a migration would stay in the migration history forever and reach the future
  production database. No schema change, no migration.
- **Reset: wipe and re-insert.** Seeding clears members, passes, classes, bookings, class types,
  exercises and plans (keeping the `AdminSeed` account and roles) and inserts the full data set.
  The user accepts that the development environment loses its data.

**Open for the plan:**

- Wipe-on-every-start would erase data on each App Service recycle (Always On restarts the app on
  its own schedule). The reset needs a trigger that is not "the app started" — e.g. a separate
  `TestDataSeed:Reset` flag, or a seed version stamp that reseeds only when it changes.
- Credentials for seeded accounts: one shared password from configuration (never committed),
  predictable e-mails on a reserved domain (e.g. `@example.test`).
- Deterministic generation (fixed `Random` seed) so a reseed produces the same people, and Polish
  names/phone numbers so the UI reads realistically.
- Outbox: seeding must not enqueue e-mail/push notifications for 200 fake people.
