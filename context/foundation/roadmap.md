---
project: "Po Prostu Siłka"
version: 2
status: draft
created: 2026-08-31
updated: 2026-09-02
prd_version: 1, 2
main_goal: speed
top_blocker: decisions
milestone_id: first-usable-mvp
milestone_seq: 1
milestone_status: open
---

# Roadmap: Po Prostu Siłka

> Derived from `context/foundation/prd.md` (v1) + `context/foundation/prd-v2.md` (v2) + `tech-stack.md` + `infrastructure.md` + `context/deployment/deploy-plan.md` + auto-researched codebase baseline.
> Edit-in-place; archive when superseded.
> Slices below are listed in dependency order. The "At a glance" table is the index.

## Milestone

**M-1: First usable MVP** — Status: open

- **Intent:** The club runs on the app instead of Excel: a member can register, get approved, browse the schedule, book and cancel classes, follow their assigned training plan — and is reliably told by email + push when a booked class is cancelled or changed.
- **Source materials:** `context/foundation/prd.md` (v1) and `context/foundation/prd-v2.md` (v2). The v2 change — class types, the Trainer role, the room's removal, and the calendar — was folded into this milestone rather than opening a new one: it restructures how M-1's own scheduling slices are built, and M-1's intent is unchanged by it.
- **Done when:** every F-NN and S-NN below is `done`.
- **Scope anchors:** from v1 — FR-001–FR-024 (all must-have; FR-022 removed by the PRD itself), US-01, US-02, and the four NFRs. From v2 — FR-001–FR-018 and US-01, US-02. Both PRDs number from FR-001, so every reference below names its source (`prd.md FR-007` vs `prd-v2 FR-007`).

## Vision recap

A single gym runs class sign-ups, schedule changes, and individual training plans through Excel: booking chaos before popular classes, no reliable way to tell members about cancellations, and plans handed out as files or printouts. This app puts the group-class schedule, bookings, individual training plans, and an exercise library in one mobile-first place — the combination existing gym SaaS doesn't offer.

Mid-milestone, a second decision landed: a class stops being retyped text and becomes a definition that occurrences are built from, the instructor becomes a real account rather than a typed name, the single room disappears from the model, and the schedule becomes a calendar. That restructuring sits ahead of booking in this roadmap, because booking has not been built yet and building it twice is the waste worth avoiding.

## North star

**S-09: Booked member is notified by email and push when the admin cancels or changes their class** — the most-felt pain is that members can't be reached when plans change, and this slice is also the hardest to make reliable, so shipping it is what proves the product works.

> "North star" here means the smallest end-to-end slice whose successful delivery would prove the core product hypothesis — placed as early as its Prerequisites allow, because everything else only matters if this works. It sits later in this regeneration than in the previous one: the class-model restructuring was deliberately sequenced ahead of booking, which the north star depends on.

## At a glance

| ID   | Change ID                        | Outcome (user can …)                                                     | Prerequisites          | PRD refs                                                        | Status      |
| ---- | -------------------------------- | ------------------------------------------------------------------------ | ---------------------- | --------------------------------------------------------------- | ----------- |
| F-01 | persistence-foundation           | (foundation) EF Core + Azure SQL wired; migrations run on deploy         | —                      | v1 NFR privacy, v1 Business Logic                               | done        |
| F-02 | auth-identity-foundation         | (foundation) Identity auth, User/Admin roles, admin seeded               | F-01                   | v1 FR-001, v1 FR-002, v1 Access Control                         | done        |
| F-03 | notification-delivery-foundation | (foundation) email + push transport with outbox/retry landed             | F-01                   | v1 FR-021, v1 NFR promptness                                    | done        |
| S-01 | registration-and-approval        | register, wait at approval screen; admin approves                        | F-01, F-02             | v1 FR-001, v1 FR-002, v1 FR-003                                 | done        |
| S-02 | member-management                | admin searches/filters members, blocks and unblocks                      | S-01                   | v1 FR-004, v1 FR-005                                            | done        |
| S-03 | class-schedule-and-admin         | browse day-by-day schedule; admin creates/edits/duplicates classes       | S-01                   | v1 FR-007, v1 FR-011, v1 FR-012                                 | done        |
| S-04 | trainer-role-and-assignment      | admin grants and revokes the Trainer role on an approved account         | S-02                   | v2 FR-001, v2 FR-002, v2 FR-003                                 | done        |
| S-05 | class-type-definitions           | admin defines, edits and deactivates a class type                        | S-03                   | v2 FR-004, v2 FR-005, v2 FR-006, v2 FR-007                      | done        |
| S-06 | occurrences-from-class-types     | admin schedules a class by picking a type and a trainer; no room field   | S-03, S-04, S-05       | v2 US-01, v2 FR-008–FR-013, v1 FR-011, v1 FR-012                | done        |
| S-07 | schedule-calendar-view           | browse the schedule as a day on a phone, a full week from tablet width   | S-06                   | v2 US-02, v2 FR-015, v2 FR-016, v2 FR-017, v2 FR-018, v2 FR-019, v2 FR-020, v1 FR-007 | in-progress |
| S-08 | class-booking-and-cancel         | book a spot, cancel it, see upcoming classes; admin sees bookings        | S-07                   | v1 US-01, v1 FR-008, v1 FR-009, v1 FR-010, v1 FR-014, v2 FR-014 | blocked     |
| S-09 | class-change-notifications       | booked member gets email + push on class cancel/change                   | F-03, S-08             | v1 US-02, v1 FR-013, v1 FR-021, v2 FR-014                       | proposed    |
| S-10 | exercise-library                 | admin manages exercises with instructions and videos                     | S-01                   | v1 FR-018, v1 FR-019                                            | ready       |
| S-11 | training-plans                   | admin builds and assigns a plan; member follows it with exercise details | S-01, S-10             | v1 FR-015, v1 FR-016, v1 FR-017, v1 FR-020                      | blocked     |
| S-12 | member-and-admin-dashboards      | member and admin land on their at-a-glance home screens                  | S-01, S-07, S-08, S-11 | v1 FR-023, v1 FR-024                                            | proposed    |
| S-13 | member-profile-edit              | member edits display name and changes password                           | F-02, S-01             | v1 FR-006                                                       | ready       |

## Streams

Navigation aid — groups items that share a Prerequisites chain. Canonical ordering still lives in the dependency graph below; this table is the proposed reading order across parallel tracks.

| Stream | Theme                 | Chain                                              | Note                                                                                             |
| ------ | --------------------- | -------------------------------------------------- | ------------------------------------------------------------------------------------------------ |
| A      | Access & identity     | `F-01` → `F-02` → `S-01` → `S-02` → `S-04` → `S-13` | The spine — everything hangs off an approved account. `S-04` adds the third role the scheduling stream needs. |
| B      | Notification delivery | `F-03` → `S-09`                                    | Carries the north star; `S-09` joins Stream C at `S-08`, which produces the bookings to notify against. |
| C      | Scheduling & booking  | `S-03` → `S-05` → `S-06` → `S-07` → `S-08` → `S-12` | The longest chain and the milestone's critical path; `S-12` also joins from Stream D at `S-11`.  |
| D      | Training domain       | `S-10` → `S-11`                                    | Independent bounded context — a separate agent run can build it alongside the whole of Stream C. |

## Baseline

What's already in place in the codebase as of `2026-09-02` (auto-researched + user-confirmed). Foundations below assume these are present and do NOT re-scaffold them.

- **Frontend:** present — Angular 22 SPA at `src/app/`, 11 routes, feature areas for admin, auth, home and schedule, with core services for admin, auth, notifications and scheduling.
- **Backend / API:** present — ASP.NET Core .NET 10 minimal API with endpoint groups for members, scheduling and notifications.
- **Data:** present — EF Core over SQL Server; six migrations through `AddClassSchedule`; entity configurations auto-discovered.
- **Auth:** present — ASP.NET Core Identity with email + password, `User`/`Admin` roles, a seeded admin account, and route-level authorization including the active-member policy.
- **Deploy / infra:** present — Azure App Service (Linux, B1) with a GitHub Actions deploy workflow on push to `main`; Azure SQL provisioned.
- **Observability:** absent — console logging only; application monitoring deliberately parked (see `## Parked`).

**Correction recorded 2026-09-02.** `prd-v2.md` §Current System Overview states that members already book and cancel spots and already receive class-change notifications. Neither is true: there is no booking entity and no booking migration, and the only notification wired to the delivery foundation is account-approved. Booking is `S-08` and class-change notifications are `S-09`, both still ahead. This roadmap sequences from the verified codebase, not from that paragraph; `prd-v2`'s `FR-014` is therefore new work here, not preserved behaviour.

**Architecture intent (user-stated):** the domain is organised the DDD way — bounded contexts (membership, scheduling/booking, training, notifications), aggregates guarding invariants (class capacity), and domain events for cross-context reactions. This roadmap groups slices along those context lines; aggregate boundaries, repositories, and event mechanics are `/10x-plan`'s territory.

## Foundations

### F-01: Persistence foundation

- **Outcome:** (foundation) Azure SQL Database (Basic DTU tier) provisioned and connected; EF Core installed with a bootstrapped DbContext; schema migrations run automatically on deploy; connection string lives in App Service settings; Always On re-verified.
- **Change ID:** persistence-foundation
- **PRD refs:** v1 NFR "personal data privacy", v1 Business Logic (all rules consume persisted state)
- **Unlocks:** S-01 and every downstream slice that stores data; establishes the migration-on-deploy verification path all later slices rely on, including the reversible-migration guardrail `prd-v2` depends on.
- **Prerequisites:** —
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** first infra-touching change since go-live; ships plumbing plus one proving migration, not the whole schema.
- **Status:** done

### F-02: Auth & identity foundation

- **Outcome:** (foundation) ASP.NET Core Identity wired for email + password; sessions long-lived and mobile-friendly; flat User/Admin role model; an admin account seeded at setup; route-level authorization available; unauthenticated access limited to login and registration.
- **Change ID:** auth-identity-foundation
- **PRD refs:** v1 FR-001, v1 FR-002, v1 Access Control, v1 NFR "personal data privacy"
- **Unlocks:** S-01 and every authenticated screen after it; its role model is what S-04 extends with a third role.
- **Prerequisites:** F-01
- **Parallel with:** F-03
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Identity brings a broad surface; scoped to email+password, roles, and the admin seed. Password change is its own slice (S-13), keeping this foundation minimal.
- **Status:** done

### F-03: Notification delivery foundation

- **Outcome:** (foundation) a transactional email path with a verified sender domain; Web Push with stored browser subscriptions; an outbox table plus an idempotent retry worker that survives platform recycles; a heartbeat log line and outbox-failure count; one test message delivered end-to-end to a real inbox and device.
- **Change ID:** notification-delivery-foundation
- **PRD refs:** v1 FR-021, v1 NFR "notification promptness", v1 Success Criteria guardrail "no missed cancellations"
- **Unlocks:** S-09 (the north star) and the account-approved notification already riding this path; creates the delivery verification path S-09 is tested against.
- **Prerequisites:** F-01
- **Parallel with:** F-02
- **Blockers:** —
- **Unknowns:**
  - Push on recent iOS requires a home-screen install — acceptable, with email as the guaranteed channel and push best-effort? — Owner: user. Block: no.
- **Risk:** the delivery path must survive platform recycles through the outbox and retry worker; fire-and-forget was explicitly ruled out. The multi-day sender-domain verification that once gated this foundation is complete.
- **Status:** done

## Slices

### S-01: Member registers, and admin approves the account

- **Outcome:** user can register with email + password, log in while `pending` and see only the awaiting-approval screen; the admin sees pending registrations and approves one; the approved member reaches the app proper.
- **Change ID:** registration-and-approval
- **PRD refs:** v1 FR-001, v1 FR-002, v1 FR-003, v1 Access Control, v1 Business Logic
- **Prerequisites:** F-01, F-02
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** the pending → active state machine is the spine of the access model; getting it right de-risks every later slice.
- **Status:** done

### S-02: Admin manages members

- **Outcome:** user (admin) can browse all members in one searchable list with status badges and a pending / active / blocked filter, block a member, and unblock them.
- **Change ID:** member-management
- **PRD refs:** v1 FR-004, v1 FR-005
- **Prerequisites:** S-01
- **Parallel with:** S-03, S-10
- **Blockers:** —
- **Unknowns:** —
- **Risk:** low — a generalisation of S-01's approvals surface. The care went into session revocation on block and admin self-block, not new construction.
- **Status:** done

### S-03: Member browses the schedule; admin runs it

- **Outcome:** user (admin) can create a class (name, date/time, room, instructor, capacity), edit it, and duplicate classes to following weeks; an active member can browse the schedule as a mobile-friendly day-by-day list.
- **Change ID:** class-schedule-and-admin
- **PRD refs:** v1 FR-007, v1 FR-011, v1 FR-012
- **Prerequisites:** S-01
- **Parallel with:** S-10
- **Blockers:** —
- **Unknowns:** —
- **Risk:** the read surface later slices build on. Note that `prd-v2` supersedes part of what this slice shipped — its free-text name, room and instructor are replaced by S-05 and S-06, and its day-grouped list by S-07. That is planned succession, not rework of a mistake: this slice established the scheduling context those three build inside.
- **Status:** done

### S-04: Admin makes someone a trainer

- **Outcome:** user (admin) can grant the Trainer role to an approved account from the member list, and revoke it; the account keeps every member capability it had, and an account may hold Admin and Trainer at once.
- **Change ID:** trainer-role-and-assignment
- **PRD refs:** v2 FR-001, v2 FR-002, v2 FR-003, v2 Access Control Changes
- **Prerequisites:** S-02
- **Parallel with:** S-05, S-10, S-13
- **Blockers:** —
- **Unknowns:** —
- **Risk:** small and low-risk — an extra action on a list that already exists. Its value is only realised by S-06, which consumes the role to populate the instructor selection; shipped alone it is a label nothing reads. Kept separate anyway so S-06 stays tractable, and because it is the one piece of `prd-v2` that touches identity rather than scheduling.
- **Status:** done

### S-05: Admin defines a class type

- **Outcome:** user (admin) can create a class type with a name, description, default duration and default capacity, browse and edit types, and deactivate one so it disappears from selection while existing classes and history stay intact.
- **Change ID:** class-type-definitions
- **PRD refs:** v2 FR-004, v2 FR-005, v2 FR-006, v2 FR-007, v2 Business Logic Changes
- **Prerequisites:** S-03
- **Parallel with:** S-04, S-10, S-13
- **Blockers:** —
- **Unknowns:** —
- **Risk:** the asymmetric binding is decided here and everything downstream inherits it — name and description resolve by reference, duration and capacity are copied at creation. Getting that backwards would let a type edit change the capacity of a class that already has bookings, which is exactly what the no-overbooking guarantee cannot survive. Deactivation rather than deletion keeps occurrences from being orphaned.
- **Status:** done

### S-06: Admin schedules a class from a definition

- **Outcome:** user (admin) creates an occurrence by selecting a class type and a trainer — duration and capacity prefill from the definition and stay overridable, the name comes from the type, and there is no room field; two classes may not overlap in time anywhere in the club; duplication to following weeks still skips and reports conflicting weeks.
- **Change ID:** occurrences-from-class-types
- **PRD refs:** v2 US-01, v2 FR-008, v2 FR-009, v2 FR-010, v2 FR-011, v2 FR-012, v2 FR-013, v1 FR-011, v1 FR-012
- **Prerequisites:** S-03, S-04, S-05
- **Parallel with:** S-10, S-13
- **Blockers:** —
- **Unknowns:**
  - A guest instructor without an account runs one class — what then? — Owner: user. Block: no. (`prd-v2` Open Question 1; the case ships unsupported.)
- **Risk:** the structural heart of `prd-v2` and the slice that discards existing scheduling data. The wipe must stay narrow — classes only, with accounts, roles, statuses and training plans untouched — and the schema change must stay reversible, with the room column left in place for one release rather than dropped in step with the code. The overlap rule changes meaning rather than disappearing; losing it here would let duplication silently create doubles.
- **Status:** done

### S-07: Member and admin browse the schedule as a calendar

- **Outcome:** user opens the schedule on a phone and sees one day at a time — the current date, controls to move by day and by week, a control to jump to a chosen date, and that day's classes with their times; from 48rem up the whole week is visible at once; the admin panel uses the same calendar with admin actions on top, can look at past weeks read-only, and can create a class by dragging across empty time; a day or week with no classes says so.
- **Change ID:** schedule-calendar-view
- **PRD refs:** v2 US-02, v2 FR-015, v2 FR-016, v2 FR-017, v2 FR-018, v2 FR-019, v2 FR-020, v1 FR-007
- **Prerequisites:** S-06
- **Parallel with:** S-10, S-11, S-13
- **Blockers:** —
- **Unknowns:**
  - How dense can the full-week view get before it stops working? — Owner: user. Block: no. (`prd-v2` Open Question 2; a design-time check — now answered by the calendar library's own week layout rather than a hand-built one.)
- **Risk:** the one slice that revisits a locked product decision — the original PRD ruled out a calendar as phone-hostile, and this shows one day at a time on a phone, with the week appearing only from 48rem up. No longer presentation-only: `prd-v2` FR-019 was added during planning and FR-020 after manual verification, so this slice also carries write paths (drag-to-create, drag-to-move and resize), and it adopts a third-party calendar library into a deliberately hand-rolled design system. Sequenced before booking because both rewrite the same schedule surface, and touching it twice is the cost this ordering avoids. It unblocks nothing downstream, which is the price of the chosen sequence.
- **Status:** in-progress

### S-08: Member books and cancels a class spot

- **Outcome:** user can book a spot in a class with free capacity (free-spot count drops by one, the class appears in their upcoming list), cancel the booking (spot released, cancelled booking kept in history), and view all upcoming classes; the admin can view a class's booking list. A class never accepts more bookings than it has spots, even under simultaneous requests.
- **Change ID:** class-booking-and-cancel
- **PRD refs:** v1 US-01, v1 FR-008, v1 FR-009, v1 FR-010, v1 FR-014, v2 FR-014
- **Prerequisites:** S-07
- **Parallel with:** S-10, S-11, S-13
- **Blockers:** —
- **Unknowns:**
  - What happens to a blocked member's *existing bookings* — cascade-cancel on block, or leave them standing while access is refused? — Owner: user. Block: yes.
- **Risk:** the load-bearing correctness work of the milestone. The no-overbooking guarantee must hold under concurrent booking, and it is checked against the capacity S-05 decided to copy onto the occurrence rather than resolve through the type. Deliberately sequenced after the whole class-model restructuring so this aggregate and its concurrency design are built once, against the final shape.
- **Status:** blocked

### S-09: Booked member is notified when their class is cancelled or changed

- **Outcome:** user (admin) cancels a class → it moves to a visible `cancelled` state (not deleted; bookings and history preserved) → every booked member receives an email and a push notification within minutes, and the class disappears from their upcoming bookings; editing a booked class triggers the same delivery.
- **Change ID:** class-change-notifications
- **PRD refs:** v1 US-02, v1 FR-013, v1 FR-021, v1 FR-011, v2 FR-014, v1 NFR "notification promptness"
- **Prerequisites:** F-03, S-08
- **Parallel with:** S-11, S-13
- **Blockers:** —
- **Unknowns:** —
- **Risk:** the north star and the differentiator. Delivery must survive platform recycles through F-03's outbox and retry, and `cancelled` must be a state transition rather than a delete. Everything before it exists so this slice can be real.
- **Status:** proposed

### S-10: Admin builds the exercise library

- **Outcome:** user (admin) can create and edit exercises — description, muscle group, difficulty, equipment, and preparation / starting-position / execution instructions, all optional — and attach an instructional video to an exercise.
- **Change ID:** exercise-library
- **PRD refs:** v1 FR-018, v1 FR-019
- **Prerequisites:** S-01
- **Parallel with:** S-03, S-04, S-05, S-06, S-07, S-08, S-13
- **Blockers:** —
- **Unknowns:**
  - Who enters the initial exercise content, and when? — Owner: user. Block: no. (Content-ops, not a build blocker — but plans stay useless until dozens of exercises exist.)
- **Risk:** low-risk admin work in an independent bounded context — the head of the training stream, buildable by a separate agent run alongside the entire scheduling chain. With the scheduling chain now four slices longer, this is the most valuable parallel lane in the milestone.
- **Status:** ready

### S-11: Admin assigns a training plan; member follows it

- **Outcome:** user (admin) can create a training plan (ordered exercise list with sets, reps, weight, rest time, note) and assign it to a member — a new assignment replaces the old, so each member has at most one active plan; the member sees their current plan and opens any exercise's details from within it.
- **Change ID:** training-plans
- **PRD refs:** v1 FR-015, v1 FR-016, v1 FR-017, v1 FR-020
- **Prerequisites:** S-01, S-10
- **Parallel with:** S-07, S-08, S-09, S-13
- **Blockers:** —
- **Unknowns:**
  - What happens to a blocked member's *assigned plan* — does blocking void, archive, or simply hide it? — Owner: user. Block: yes.
- **Risk:** the one-active-plan replace-and-archive rule is the domain invariant to get right; exercise details are reached only from the plan, which bounds the interface.
- **Status:** blocked

### S-12: Member and admin dashboards

- **Outcome:** user (member) lands on a dashboard showing their nearest upcoming classes and active training plan; the admin lands on a dashboard of items needing attention — pending approvals, today's and upcoming classes.
- **Change ID:** member-and-admin-dashboards
- **PRD refs:** v1 FR-023, v1 FR-024
- **Prerequisites:** S-01, S-07, S-08, S-11
- **Parallel with:** S-13
- **Blockers:** —
- **Unknowns:** —
- **Risk:** pure aggregation over data every earlier slice produces — deliberately last, because building it earlier means stubbing every card. Its class-facing cards read the calendar surface S-07 settles, which is why it waits for that rather than for S-03.
- **Status:** proposed

### S-13: Member edits their profile

- **Outcome:** user can edit their display name and change their password.
- **Change ID:** member-profile-edit
- **PRD refs:** v1 FR-006
- **Prerequisites:** F-02, S-01
- **Parallel with:** S-03, S-04, S-05, S-06, S-07, S-08, S-09, S-10, S-11, S-12
- **Blockers:** —
- **Unknowns:** —
- **Risk:** smallest slice in the milestone; nothing depends on it, so it slots into any idle parallel lane.
- **Status:** ready

## Backlog Handoff

| Roadmap ID | Change ID                        | Suggested issue title                                        | Ready for `/10x-plan` | Notes                                              |
| ---------- | -------------------------------- | ------------------------------------------------------------ | --------------------- | -------------------------------------------------- |
| F-01       | persistence-foundation           | Provision Azure SQL and wire EF Core persistence             | no                    | Done — archived 2026-08-31                          |
| F-02       | auth-identity-foundation         | Add ASP.NET Core Identity, roles, and admin seed             | no                    | Done — archived 2026-08-31                          |
| F-03       | notification-delivery-foundation | Build email + push delivery with outbox/retry                | no                    | Done — archived 2026-08-31                          |
| S-01       | registration-and-approval        | Member registration with admin approval gate                 | no                    | Done — archived 2026-09-01                          |
| S-02       | member-management                | Member list, filter, block/unblock                           | no                    | Done — archived 2026-09-01                          |
| S-03       | class-schedule-and-admin         | Class schedule browsing and admin class management           | no                    | Done — archived 2026-09-02                          |
| S-04       | trainer-role-and-assignment      | Grant and revoke the Trainer role from the member list       | yes                   | Run `/10x-plan trainer-role-and-assignment`         |
| S-05       | class-type-definitions           | Class type definitions with defaults and deactivation        | no                    | Needs S-03 closed                                   |
| S-06       | occurrences-from-class-types     | Schedule occurrences from a class type; drop the room field  | no                    | Needs S-03, S-04, S-05                              |
| S-07       | schedule-calendar-view           | Day/week calendar for member schedule and admin panel        | no                    | Needs S-06                                          |
| S-08       | class-booking-and-cancel         | Class booking and cancellation with no-overbooking guarantee | no                    | Needs S-07; blocked on Open Question 1              |
| S-09       | class-change-notifications       | Email + push notifications on class cancel/change            | no                    | North star; needs F-03, S-08                        |
| S-10       | exercise-library                 | Exercise library management with instructional videos        | yes                   | Run `/10x-plan exercise-library` — best parallel lane |
| S-11       | training-plans                   | Training plan creation, assignment, and member view          | no                    | Needs S-10; blocked on Open Question 1              |
| S-12       | member-and-admin-dashboards      | Member and admin dashboards                                  | no                    | Needs S-01, S-07, S-08, S-11                        |
| S-13       | member-profile-edit              | Member profile: edit name and change password                | yes                   | Run `/10x-plan member-profile-edit`                 |

## Open Roadmap Questions

1. **What happens to a blocked member's existing bookings and assigned plan?** — Owner: user. Block: S-08, S-11. (v1 Open Question 1, v2 Open Question 5. The single blocking decision in the milestone, and it sits on the path to the north star. The two halves are separable: the booking half gates S-08, the plan half gates S-11.)
2. **Who enters the initial exercise library content, and when?** — Owner: user. Block: none directly, but S-11 delivers no real value until dozens of exercises exist. (v1 Open Question 2, v2 Open Question 6.)
3. **A guest instructor without an account runs one class — what then?** — Owner: user. Block: none; S-06 ships with the case unsupported. (v2 Open Question 1.)
4. **How dense can the full-week view get before it stops working?** — Owner: user. Block: none; a design-time check inside S-07. (v2 Open Question 2.)
5. **What does a trainer eventually see after signing in?** — Owner: user. Block: none — explicitly out of scope for this milestone; the additive role model keeps the path open. (v2 Open Question 3.)
6. **Is best-effort push acceptable on recent iOS, with a home-screen install required and email as the guaranteed channel?** — Owner: user. Block: none; sets S-09's acceptance bar.

Resolved since the previous roadmap: the sender-domain question that gated F-03 is closed — the foundation shipped and was archived.

## Parked

- **Observability beyond a heartbeat + outbox-failure count** — Why parked: no must-have requirement needs it, `speed` is the goal, and monitoring bill-creep was flagged during infrastructure work; revisit if notification-failure visibility proves insufficient.
- **Trainer screen ("my classes")** — Why parked: v2 §Non-Goals; the Trainer role populates the instructor selection and nothing more in this milestone.
- **Multiple rooms** — Why parked: v2 §Non-Goals; the room disappears for good and no rooms lookup is introduced in advance.
- **Month view, agenda view, external calendar export** — Why parked: v2 §Non-Goals; the calendar works in days and weeks.
- **A latency target for week-to-week navigation, and one-handed reachability for calendar controls** — Why parked: v2 §Non-Goals; design intentions, deliberately not committed as measurable promises.
- **Multi-club / multi-tenancy** — Why parked: v1 §Non-Goals — one gym's app.
- **Attendance / check-in tracking** — Why parked: v1 §Non-Goals; booking lists only.
- **Offline-first guarantee** — Why parked: v1 §Non-Goals; installable, but a connection is required.
- **Cancellation deadline / late-cancel rules** — Why parked: v1 §Non-Goals; free cancel anytime is locked.
- **In-app notification center** — Why parked: v1 §Non-Goals; delivery is email + push only.
- **Standalone exercise library browsing** — Why parked: v1 §Non-Goals; exercises are reached from the plan only.
- **Plan history / versioning UI** — Why parked: v1 §Non-Goals; one active plan per member.
- **Account rejection status** — Why parked: v1 §Non-Goals; lifecycle is pending / active / blocked.
- **Payments, passes, subscriptions, invoices** — Why parked: v1 §Non-Goals; the app manages participation, not money.
- **Waitlist for full classes** — Why parked: v1 §Non-Goals.
- **Full recurring-series management** — Why parked: v1 §Non-Goals; weekly duplication stands in, and v2 explicitly declined to reopen it.
- **Chat / social features; health-app integrations; native mobile apps; self-hosted video; advanced statistics; automatic weight progression** — Why parked: v1 §Non-Goals.

**Unparked by this regeneration:** the Trainer role was a v1 Non-Goal and is no longer parked — v2 retires it, and S-04 delivers it.

## Milestone History

(Append-only. Empty on the first milestone.)

## Done

- **F-01: (foundation) Azure SQL Database (Basic DTU tier) provisioned and connected; EF Core installed with a bootstrapped DbContext; schema migrations run automatically on deploy; connection string lives in App Service settings; Always On re-verified.** — Archived 2026-08-31 → `context/archive/2026-08-31-persistence-foundation/`. Lesson: —.
- **F-02: (foundation) ASP.NET Core Identity wired for email + password; sessions long-lived and mobile-friendly (FR-002 design note); flat User/Admin role model; an admin account seeded at setup (never self-registered); route-level authorization available; unauthenticated access limited to login and registration.** — Archived 2026-08-31 → `context/archive/2026-08-31-auth-identity-foundation/`. Lesson: —.
- **F-03: (foundation) a transactional email path (Azure Communication Services, or the documented SMTP fallback) with a verified sender domain; Web Push with a subscription endpoint and stored browser subscriptions; an outbox table plus an idempotent retry worker (hosted service) that survives App Service recycles; a heartbeat log line and outbox-failure count for visibility; one test message delivered end-to-end to a real inbox and device.** — Archived 2026-09-01 → `context/archive/2026-08-31-notification-delivery-foundation/`. Lesson: Record necessary adaptations in the plan, not only in the deploy log (`context/foundation/lessons.md`).
- **S-01: user can register with email + password, log in while `pending` and see only the awaiting-approval screen (no schedule, no booking); the admin sees pending registrations and approves one; the approved member logs in and reaches the app proper.** — Archived 2026-09-01 → `context/archive/2026-09-01-registration-and-approval/`. Lesson: —.
- **S-02: user (admin) can browse all members in one searchable list with status badges and a pending / active / blocked filter, block a member (who then loses access to app content), and unblock them.** — Archived 2026-09-01 → `context/archive/2026-09-01-member-management/`. Lesson: —.
- **S-03: user (admin) can create a class (name, date/time, room, instructor, capacity), edit it, and duplicate classes to following weeks; an active member can browse the schedule as a mobile-friendly day-by-day list.** — Archived 2026-09-02 → `context/archive/2026-09-01-class-schedule-and-admin/`. Lesson: —.
- **S-04: user (admin) can grant the Trainer role to an approved account from the member list, and revoke it; the account keeps every member capability it had, and an account may hold Admin and Trainer at once.** — Archived 2026-09-02 → `context/archive/2026-09-02-trainer-role-and-assignment/`. Lesson: —.
- **S-05: user (admin) can create a class type with a name, description, default duration and default capacity, browse and edit types, and deactivate one so it disappears from selection while existing classes and history stay intact.** — Archived 2026-09-02 → `context/archive/2026-09-02-class-type-definitions/`. Lesson: —.
- **S-06: user (admin) creates an occurrence by selecting a class type and a trainer — duration and capacity prefill from the definition and stay overridable, the name comes from the type, and there is no room field; two classes may not overlap in time anywhere in the club; duplication to following weeks still skips and reports conflicting weeks.** — Archived 2026-09-02 → `context/archive/2026-09-02-occurrences-from-class-types/`. Lesson: —.
