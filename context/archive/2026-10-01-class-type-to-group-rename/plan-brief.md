# Class Type → Group Rename — Plan Brief

> Full plan: `context/changes/class-type-to-group-rename/plan.md`

## What & Why

The club calls its class definitions "grupy", and the app calls them "typy zajęć" (`ClassType`).
Roadmap S-33 renames the concept everywhere: the screens, the SPA routes, the API, the code, the
database, the seeder, the tests and the living docs. Behaviour does not change at all.

## Starting Point

The concept arrived in S-05/S-06 and today spans about 30 backend files, a `ClassTypes` table with an
FK from `Classes`, an admin feature folder of 8 files, about 40 Polish strings, three API reason
codes and an E2E helper. It borrows the `repeat` glyph, which already means "Powtórzenia".

## Desired End State

The admin works with **Grupy** at `/admin/class-groups`: "Nowa grupa", "Edytuj grupę", and a "Grupa"
select on the class form, all under a dedicated `group` glyph. The code says `ClassGroup`, the API
says `/api/admin/class-groups` and `classGroupId`, and the database says `ClassGroups` /
`ClassGroupId`. Staging data survives the rename.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Depth | Full rename, database included | The user's choice; "grupa" is a new word for the same thing. |
| DB safety | Rename migration, deploy-window outage and manual-`Down` rollback accepted **once** | Staging only for now. The user waived the safe path explicitly, and it is not a precedent. |
| Migration body | Hand-written renames, never the scaffolded drop/create | Keeping the rows costs nothing, and drop/create would delete every group and class. |
| Code name | `ClassGroup` / `class-groups` / `class_group` | A bare `Group` collides with `MapGroup` and LINQ `group`. |
| UI words | "Grupy" / "grupa" | Exactly what was asked for; short in the menu and the phone bar. |
| Icon | New `group` glyph; `repeat` keeps only "Powtórzenia" | One meaning, one icon. |
| Old URL | No redirect | Admin-only, and the menu already points at the new path. |
| Docs | Living docs only; PRD, shape-notes, roadmap history and archive untouched | The PRD is versioned as shipped, and the archive is immutable. |

## Scope

**In scope:** Domain/Application/Infrastructure/Api renames; the API route, wire field and reason codes; the schema rename migration; the seeder; the integration tests; the SPA core, feature folder, routes, navigation, copy and specs; the `group` glyph; the E2E `club.ts`; the living docs; the AGENTS.md bundle figure; the rollback step in `deploy-plan.md`.

**Out of scope:** any behaviour change; a safe expand/contract deploy; a redirect; PRD and archive edits; editing past migrations.

## Architecture / Approach

Rename outward from the schema. Phase 1 changes the backend and the API contract together, with
the integration suite as proof. Phase 2 moves the SPA and the E2E helper onto that contract. Phase 3
updates the docs and runs the full gate, the browser suite included. Everything goes into one branch
and one PR, because the SPA is incompatible with the API between phases 1 and 2.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Backend, API contract, database | `ClassGroup` everywhere server-side, plus the rename migration | EF scaffolding drop/create instead of renames |
| 2. SPA, icon, E2E helpers | "Grupy" screens, new routes and the `group` glyph | Missing a string or a field the lint rule cannot see |
| 3. Living docs, full gate | Docs, bundle figure, rollback step, E2E green | — |

**Prerequisites:** Docker SQL Server running locally; Node 22+.
**Estimated effort:** about 1–2 sessions.

## Open Risks & Assumptions

- During the staging deploy, the old build serves against the renamed schema for about 6–7 minutes, and screens that read classes return 500. This is accepted. Deploy outside demo hours.
- A rollback past this release needs `dotnet ef database update 20260923133910_AddBookingAttendance` first, because `rollback.yml` runs no migration.
- An admin tab left open across the deploy calls the old API path until it reloads.

## Success Criteria (Summary)

- An admin manages "Grupy" and builds classes from them exactly as they did with types.
- No `ClassType` / "typ zajęć" remains outside migration history, the PRD and the archive.
- Staging keeps its groups and classes after the migration, and `/health` stays `Healthy`.
