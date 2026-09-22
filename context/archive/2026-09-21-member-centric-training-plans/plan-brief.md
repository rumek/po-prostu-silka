# A Member's Plan Is Reached Through the Member (S-22) — Plan Brief

> Full plan: `context/changes/member-centric-training-plans/plan.md`
> Research: `context/changes/member-centric-training-plans/research.md`

## What & Why

A training plan belongs to one member. Today it is reached through its own screen, `/trainer/plans`. The first column of that screen is the member's name, so it is really a member list in disguise, and the same person can be reached along two axes. This slice (M-7 UX-07, UX-08) removes that second axis:
- the admin reaches the plan through Członkowie → member → Plan;
- a trainer reaches it through a member list of their own;
- the Plany screen is retired.

## Starting Point

- Plans are keyed to the member, and there is at most one active plan per member (a DB index).
- No plan is owned by a trainer: any trainer or admin may edit any plan.
- The builder at `/trainer/plans/:id` chooses the member from a club-wide picker (`AssignableMember`: id, name, hasAccount).
- `/api/admin/members` is Admin-only for the whole group, and its rows carry e-mail and account data.
- A by-member read exists below HTTP (`FindActiveForMemberAsync`) but has no route.

## Desired End State

**Admin:** opens a member's **Plan** from the row menu at `/admin/members/:id/plan`.
- The screen is the builder with the member fixed by the URL.
- With no plan it shows an empty "Przypisz plan"; with a plan it shows the plan to edit.
- Saving stays on the screen and shows a toast.

**Trainer:** opens **Członkowie** from the header or `/more`.
- The list is paged and searched by name. It shows active members only, with "bez konta" where it applies and the plan's name or "brak planu".
- A row opens the same builder at `/trainer/members/:id/plan`.

**Retired:** `/trainer/plans`, its two list endpoints, and both "Plany" nav entries.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Which members a trainer sees | The whole club's active members | Matches the recorded S-11/S-16 stance that plans have no owner, and the picker already exposed exactly these names | Plan |
| What a trainer's row carries | Name, hasAccount, active plan name; search by name only | The minimum the task needs; searching e-mail would make the list an e-mail oracle | Plan |
| Routes | `/admin/members/:id/plan` (adminGuard) and `/trainer/members/:id/plan` (trainerGuard), one builder mounted twice | UX-07 literally, and S-11's rule that the URL says who can use it | Plan |
| API shape | New reads `GET /api/trainer/members` and `GET /api/trainer/members/{id}/plan`; writes stay `POST/PUT /api/trainer/plans[/{id}]` | PUT by plan id keeps today's stale-tab 404; `member_changed` becomes the URL/body check | Plan |
| Old endpoints | Remove the plan list and the picker | No consumer left; one axis into each person | Plan |
| Plan screen | The builder directly; no "new plan instead" action; stay on the screen and toast after a save | Same shape as passes; archived plans are invisible, so replacing a plan is editing it | Plan |
| Blocked member | Admin sees and edits the plan, and create stays refused; absent from the trainer's list | Keeps "a blocked member's plan is left untouched" and the existing `Assignable` predicate | Plan |
| Navigation | A trainer without Admin gets "Członkowie" → `/trainer/members` (header and `/more`); the admin only loses "Plany" | One member list per role, and the trainer's Panel is not left empty; no redesign (UX-09) | Plan |
| Member API authorization | Unchanged; `/api/admin/members*` stays Admin-only | Widening a group-level policy is how a trainer would read the club's contact details | Research |

## Scope

**In scope:**
- the trainer member API with shared search and paging internals;
- the builder rewired to the member;
- both routes, the admin row-menu item and the trainer list screen;
- the nav swap;
- retirement of the Plany screen and its two API routes;
- tests on both sides.

**Out of scope:**
- any trainer↔member relationship;
- e-mail or status on trainer surfaces;
- Open Roadmap Question 8 (roster e-mails);
- changes to the plan write API or to blocked-member rules;
- a table for the trainer's list;
- keeping the admin list's state on the back link;
- keyboard reordering, a role-aware bottom nav, or a redesign;
- extracting the members list's URL-state logic (a known second copy).

## Architecture / Approach

The work goes additive first, then the consumers switch, then the old routes are retired. Every commit on `main` deploys, so each phase must leave the API and the SPA consistent:
1. Phase 1 adds `/api/trainer/members` (`TrainerOrAdmin`, `Assignable` predicate, name-only `MemberSearch.ByName`, shared paging validation).
2. Phases 2 and 3 move the SPA onto it.
3. Phase 4 deletes what nothing calls.

The single builder component learns only two things: its member comes from `:id`, and its route data says which list to return to.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Trainer member API | Paged, name-searched list and member-plan read, with the no-e-mail pin | The name-only search drifts from the admin search's folding (mitigated by a shared helper) |
| 2. Plan through the member (SPA) | Member-scoped builder on both mounts, admin "Plan" menu item, old list bridged | A create not switching to edit would POST again and archive the plan it just made |
| 3. Trainer list replaces Plany | `/trainer/members`, nav swap, Plany screen deleted | The nav condition "trainer and not admin" drifting from its guards; pinned by `app.spec` and `more.spec` |
| 4. Retire old endpoints | Two GETs removed, tests moved to the new read | An open old SPA tab loses its list until reload (accepted, as in S-21) |

**Prerequisites:** S-21 (`member-list-at-scale`) is merged, because its paging envelope, `MemberListFailure` and members table are built on. Docker is needed for the integration tests.
**Estimated effort:** about 3–4 sessions across 4 phases. Phase 2 is the largest.

## Open Risks & Assumptions

- **The PRD privacy NFR's literal text** ("visible only to the admin and the member") predates the Trainer role. The trainer seeing club-wide names is a continuation of the existing picker, not a new widening, but no PRD version records it.
- **Open Roadmap Question 8 stays open.** Trainers still see e-mails on their own class rosters, so this slice sets a stricter standard for the member list than the roster meets.
- **The trainer list's URL-state logic is a second copy** of the S-21 pattern. A third list should extract it.
- **Phases 2 and 3 are pushed to `main` together.** Phase 2 alone would leave trainers unable to create a plan until Phase 3 deploys.
- **The trainer's list includes the trainer themselves and every admin.** `Assignable` has no role filter. This is the same set the picker offered, and it is accepted and pinned by a test.

## Success Criteria (Summary)

- An admin assigns and edits a member's plan without ever seeing a plan list.
- A trainer finds a member by name and edits their plan, and never receives an e-mail address from any new endpoint. A raw-body test proves the second part.
- `/trainer/plans` and its endpoints are gone, and no test, spec or nav entry references them.
