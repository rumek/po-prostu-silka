---
project: "Po Prostu Siłka"
version: 3
status: draft
created: 2026-08-31
updated: 2026-09-10
prd_version: 1, 2
main_goal: speed
top_blocker: none
milestone_id: invitation-only-registration
milestone_seq: 5
milestone_status: open
---

# Roadmap: Po Prostu Siłka

> Derived from `context/foundation/prd.md` (v1) + `context/foundation/prd-v2.md` (v2) + `tech-stack.md` + `infrastructure.md` + `context/deployment/deploy-plan.md` + auto-researched codebase baseline.
> Edit-in-place; archive when superseded.
> Slices below are listed in dependency order. The "At a glance" table is the index.

## Milestone

**M-5: An account is created only by invitation** — Status: open

- **Intent:** Registration stops being a public door. The club's desk is where a person enters the
  records, and the only way to attach a login to those records is an invitation code handed over in
  person. What registration asks for shrinks to the two things the club cannot know for them: an
  address to sign in with and a password.
- **Source materials:** the user's own description, recorded as the `IR-NN` anchors below. No PRD
  version describes an invitation-only door — v1 FR-001 describes open self-registration — so this
  milestone supersedes published product scope, exactly as M-4 did.
- **Done when:** every S-NN in this milestone is `done`.
- **Scope anchors:**
  - IR-01: An admin records a member with no email address — and, since the correction below, CANNOT
    record one at all: the field left the admin form and the API contract together. That person
    receives no email and no push, and that is an accepted consequence rather than a defect — the
    club reaches them at the desk. The address arrives when they register with their invitation and
    becomes their login, which is the only writer of one.
  - IR-02: `/register` is reachable ONLY with an `invitationCode` query parameter. A request without
    one is redirected to `/login`, and no screen in the app links to registration.
  - IR-03: The code arrives in the query string and is DISPLAYED AS TEXT, not as a form field. A
    person registering never types a code and never edits the one they were handed. It shipped first
    as a readonly input and was corrected: a box the member cannot type into is still a box, and it
    invites them to try.
  - IR-04: Registration asks for an email address and a password, and nothing else. Display name,
    phone number and postal address are no longer collected — they come from the member record the
    code attaches to.
  - IR-05: Registration without a valid code is impossible, and the refusal lives in the API rather
    than only in the SPA. A code that is unknown, expired, revoked or already used is still refused
    as one answer, for the account-enumeration reason S-14 recorded.
  - IR-06: The admin can copy the whole invitation URL, not only the bare code. Added during
    planning, deliberately widening this milestone: the link is what an admin pastes into a message,
    and without it every invitation would have to be assembled by hand. The bare code stays copyable
    beside it — it exists to be read down the phone, which is what its alphabet was designed for.

**Naming, settled with the user:** the query parameter is `invitationCode`; the API contract keeps
`memberCode`. Two names for one thing is a deliberate, recorded trade — the SPA reads the friendlier
word out of the URL and sends the field the API already answers to, and no shipped contract moves.

**Corrected after the first implementation, on the user's review:** two things the slice got wrong
the first time. The admin form still asked for an email address, which contradicted IR-01's premise —
the desk does not take one, and the club consciously forgoes notifications until the member registers.
And the invitation code shipped as a readonly `<input>` rather than as text. Both are folded into
IR-01 and IR-03 above rather than left as errata.

**Not in scope, deliberately:** GENERATING, expiring and revoking the invitation. That admin surface
shipped with S-14 and is untouched. Note the narrowing: this line used to say "issuing the
invitation" outright, and IR-06 makes that no longer true — copying the link is part of issuing it,
and the scope was extended on purpose rather than by drift.

## PRD addendum (M-2)

Recorded here rather than in `prd.md`, which is versioned as shipped and describes the account-only
model M-1 was built against.

- **AM-001:** Admin can create a member record for a person who has no account, with a display name
  and contact details. Priority: must-have
- **AM-002:** Admin can edit and block such a record. A blocked member — with an account or without —
  loses their future bookings, exactly as a blocked account does today. Priority: must-have
- **AM-003:** A member record and a login are separate things. Bookings, training plans and the class
  instructor belong to the member; devices and credentials belong to the login. Priority: must-have
- **AM-004:** Admin can issue a single-use, expiring member code for a record that has no account, can
  see it in order to hand it over, and can revoke it. Priority: must-have
- **AM-005:** A person registering with a valid code gets an account attached to that existing record,
  inheriting its bookings and its active plan, instead of a fresh one. Registering without a code
  behaves exactly as it does today. Priority: must-have
  - **Superseded in part by M-4 (MP-03).** The last sentence used to read "the account is still
    created `pending` — the code proves the club knows them, not that login is approved". Approval was
    retired in S-16: registration now produces an ACTIVE account whether or not a code was used, and
    what decides whether somebody may train is the karnet. The claim's own point survives — the code
    proves the club knows this person and never granted them anything else.
- **AM-006:** Admin can assign a training plan to a member with no account, and can book one into a
  class on their behalf. The no-overbooking guarantee holds identically on that path. Priority:
  must-have

**Not in scope, deliberately:** a member with no account receives no email or push (there is no
address and no device); an instructor still needs an account, because the Trainer role lives in
Identity; and there is no self-service way to link an account to a record without the admin's code.

## Vision recap

A single gym runs class sign-ups, schedule changes, and individual training plans through Excel: booking chaos before popular classes, no reliable way to tell members about cancellations, and plans handed out as files or printouts. This app puts the group-class schedule, bookings, individual training plans, and an exercise library in one mobile-first place — the combination existing gym SaaS doesn't offer.

Mid-milestone, a second decision landed: a class stops being retyped text and becomes a definition that occurrences are built from, the instructor becomes a real account rather than a typed name, the single room disappears from the model, and the schedule becomes a calendar. That restructuring sits ahead of booking in this roadmap, because booking has not been built yet and building it twice is the waste worth avoiding.

## North star

**S-17: Registration is reachable only through an invitation, and asks for almost nothing** — M-5 has
one slice, so it is the north star by construction. It earns the name on the closed door rather than
on the shortened form: the milestone's hypothesis is that nobody should be able to create an account
the club did not hand out, and the only way to test that is to open `/register` with no code and land
back on the login screen.

> "North star" here means the smallest end-to-end slice whose successful delivery would prove the core
> product hypothesis. M-1's was S-09 (email + push on class changes), M-2's was S-14 (the claim path),
> M-3's was S-15 (the plan card) and M-4's was S-16 (the karnet refusal); all shipped, and their
> entries live in `## Milestone History`.

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
| S-07 | schedule-calendar-view           | browse the schedule as a day on a phone, a full week from tablet width   | S-06                   | v2 US-02, v2 FR-015, v2 FR-016, v2 FR-017, v2 FR-018, v2 FR-019, v2 FR-020, v1 FR-007 | done        |
| S-08 | class-booking-and-cancel         | book a spot, cancel it, see upcoming classes; admin sees bookings        | S-07                   | v1 US-01, v1 FR-008, v1 FR-009, v1 FR-010, v1 FR-014, v2 FR-014 | done        |
| S-09 | class-change-notifications       | booked member gets email + push on class cancel/change                   | F-03, S-08             | v1 US-02, v1 FR-013, v1 FR-021, v2 FR-014                       | done        |
| S-10 | exercise-library                 | admin manages exercises with instructions and videos                     | S-01                   | v1 FR-018, v1 FR-019                                            | done        |
| S-11 | training-plans                   | admin builds and assigns a plan; member follows it with exercise details | S-01, S-10             | v1 FR-015, v1 FR-016, v1 FR-017, v1 FR-020                      | done        |
| S-12 | member-and-admin-dashboards      | member and admin land on their at-a-glance home screens                  | S-01, S-07, S-08, S-11 | v1 FR-023, v1 FR-024                                            | done        |
| S-13 | member-profile-edit              | member edits contact details, changes password, resets a forgotten one   | F-02, S-01             | v1 FR-006, v1 FR-025, v1 FR-026                                 | done        |
| S-14 | member-entity-and-accountless-members | admin keeps a record for someone with no account; that person later claims it with a code | S-02, S-08, S-11, S-13 | M-2 AM-001–AM-006 | done |
| S-15 | plan-card-prescription-detail | member reads their plan card with the muscle group, a prescribed duration where the trainer set one, an info icon to the exercise, and the note set off as a callout | S-10, S-11 | M-3 MS-001–MS-004 | done |
| S-16 | membership-pass-and-staff-booking | admin issues a karnet and books a member in; a trainer books into their own classes; a member with no valid karnet is refused; nobody self-books and nobody waits for approval | S-01, S-04, S-08, S-14 | M-4 MP-01–MP-07 (retires v1 US-01, FR-002, FR-003, FR-008, FR-009) | done        |
| S-17 | invitation-only-registration | register only through an invitation link — the code is prefilled and readonly, the form asks for an email and a password, and there is no way in without a code | S-14, S-16 | M-5 IR-01–IR-06 (supersedes v1 FR-001's open self-registration) | done |

## Streams

Navigation aid — groups items that share a Prerequisites chain. Canonical ordering still lives in the dependency graph below; this table is the proposed reading order across parallel tracks.

| Stream | Theme                 | Chain                                              | Note                                                                                             |
| ------ | --------------------- | -------------------------------------------------- | ------------------------------------------------------------------------------------------------ |
| A      | Access & identity     | `F-01` → `F-02` → `S-01` → `S-02` → `S-04` → `S-13` → `S-14` → `S-16` → `S-17` | The spine. Through M-1 everything hung off an approved account; `S-14` is where that stops being true and a member outlives the login, and `S-16` is where the approval flag stops deciding anything at all — the karnet does. `S-16` also closes Stream C's member-facing half, which is why it belongs to both chains. `S-17` closes the stream where it started: the registration door `S-01` opened is shut to everyone the club did not invite. |
| B      | Notification delivery | `F-03` → `S-09`                                    | Carries the north star; `S-09` joins Stream C at `S-08`, which produces the bookings to notify against. |
| C      | Scheduling & booking  | `S-03` → `S-05` → `S-06` → `S-07` → `S-08` → `S-12` | The longest chain and the milestone's critical path; `S-12` also joins from Stream D at `S-11`.  |
| D      | Training domain       | `S-10` → `S-11` → `S-15`                           | Independent bounded context — a separate agent run can build it alongside the whole of Stream C. `S-15` extends the plan surface `S-11` created; it is M-3's only slice. |

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

- **Outcome:** user opens the schedule on a phone and sees one day at a time — the current date, a weekday strip that navigates the week it belongs to, a control to jump to a chosen date, and that day's classes with their times on a 06:00–24:00 grid; from 48rem up the whole week is visible at once; the admin panel uses the same calendar with admin actions on top, can look at past weeks read-only, can create a class by dragging across empty time, and can move or resize an existing one on the grid; a day or week with no classes says so.
- **Change ID:** schedule-calendar-view
- **PRD refs:** v2 US-02, v2 FR-015, v2 FR-016, v2 FR-017, v2 FR-018, v2 FR-019, v2 FR-020, v1 FR-007
- **Prerequisites:** S-06
- **Parallel with:** S-10, S-11, S-13
- **Blockers:** —
- **Unknowns:**
  - How dense can the full-week view get before it stops working? — Owner: user. Block: no. (`prd-v2` Open Question 2; a design-time check — now answered by the calendar library's own week layout rather than a hand-built one.)
- **Risk:** the one slice that revisits a locked product decision — the original PRD ruled out a calendar as phone-hostile, and this shows one day at a time on a phone, with the week appearing only from 48rem up. No longer presentation-only: `prd-v2` FR-019 was added during planning and FR-020 after manual verification, so this slice also carries write paths (drag-to-create, drag-to-move and resize), and it adopts a third-party calendar library into a deliberately hand-rolled design system. Sequenced before booking because both rewrite the same schedule surface, and touching it twice is the cost this ordering avoids. It unblocks nothing downstream, which is the price of the chosen sequence.
- **Status:** done

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
- **Status:** done

### S-09: Booked member is notified when their class is cancelled or changed

- **Outcome:** user (admin) cancels a class → it moves to a visible `cancelled` state (not deleted; bookings and history preserved) → every booked member receives an email and a push notification within minutes, and the class disappears from their upcoming bookings; editing a booked class triggers the same delivery.
- **Change ID:** class-change-notifications
- **PRD refs:** v1 US-02, v1 FR-013, v1 FR-021, v1 FR-011, v2 FR-014, v1 NFR "notification promptness"
- **Prerequisites:** F-03, S-08
- **Parallel with:** S-11, S-13
- **Blockers:** —
- **Unknowns:** —
- **Risk:** the north star and the differentiator. Delivery must survive platform recycles through F-03's outbox and retry, and `cancelled` must be a state transition rather than a delete. Everything before it exists so this slice can be real.
- **Status:** done

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
- **Status:** done

### S-11: Admin assigns a training plan; member follows it

- **Outcome:** user (admin) can create a training plan (ordered exercise list with sets, reps, weight, rest time, note) and assign it to a member — a new assignment replaces the old, so each member has at most one active plan; the member sees their current plan and opens any exercise's details from within it.
- **Change ID:** training-plans
- **PRD refs:** v1 FR-015, v1 FR-016, v1 FR-017, v1 FR-020
- **Prerequisites:** S-01, S-10
- **Parallel with:** S-07, S-08, S-09, S-13
- **Blockers:** —
- **Unknowns:** — (resolved 2026-09-04: a blocked member's plan is left untouched; blocking cuts access at read time, and the plan is waiting when the account is unblocked.)
- **Risk:** the one-active-plan replace-and-archive rule is the domain invariant to get right; exercise details are reached only from the plan, which bounds the interface. Scope widened during planning: plan authoring is available to Trainer as well as Admin, which retires prd-v2's "No trainer screen" Non-Goal and answers its Open Question 3.
- **Status:** done

### S-12: Member and admin dashboards

- **Outcome:** user (member) lands on a dashboard showing their nearest upcoming classes and active training plan; the admin lands on a dashboard of items needing attention — pending approvals, today's and upcoming classes.
- **Change ID:** member-and-admin-dashboards
- **PRD refs:** v1 FR-023, v1 FR-024
- **Prerequisites:** S-01, S-07, S-08, S-11
- **Parallel with:** S-13
- **Blockers:** —
- **Unknowns:** —
- **Risk:** pure aggregation over data every earlier slice produces — deliberately last, because building it earlier means stubbing every card. Its class-facing cards read the calendar surface S-07 settles, which is why it waits for that rather than for S-03.
- **Status:** done

### S-13: Member edits their profile

- **Outcome:** member sees their name and email, edits their contact details (phone and address),
  changes their password while signed in, and resets a forgotten password by emailed link before login.
  The display name is deliberately NOT editable — the gym owns how a member appears on its lists.
- **Change ID:** member-profile-edit
- **PRD refs:** v1 FR-006, v1 FR-025, v1 FR-026
- **Prerequisites:** F-02, S-01
- **Parallel with:** S-03, S-04, S-05, S-06, S-07, S-08, S-09, S-10, S-11, S-12
- **Blockers:** —
- **Unknowns:** —
- **Risk:** not the small slice it first looked like. The password-reset half is anonymous, sends mail
  on demand, and must answer identically for every address — an account-enumeration oracle is the
  failure mode, and it is invisible to a build or a lint run. Contact details also became required at
  registration, which touches the schema and every account created before this slice.
- **Status:** done

### S-14: A member without an account

- **Outcome:** the admin creates a member record for someone who has never registered, edits and
  blocks it, assigns them a training plan and books them into a class; the admin issues a single-use
  code, and the person registers with it and lands on that record — their existing bookings and their
  active plan already there.
- **Change ID:** member-entity-and-accountless-members
- **PRD refs:** M-2 AM-001, AM-002, AM-003, AM-004, AM-005, AM-006
- **Prerequisites:** S-02 (the member list this extends), S-08 (bookings), S-11 (plans), S-13 (contact details)
- **Parallel with:** — (it re-points the FKs every other context reads through)
- **Blockers:** —
- **Unknowns:** whether an instructor should eventually be allowed to have no account. Represented by
  the schema after this slice, deliberately still refused by it — see Open Roadmap Question 3.
- **Risk:** the highest of any slice so far, and structural rather than local. It splits the entity
  every other bounded context points at, so it moves four foreign keys, and it puts a second claim
  into the authorization contract — an account left without a member record would fail every policy,
  including `Admin`, which is how a club locks itself out of its own app. Sequenced expand/contract
  across releases because CI applies migrations before the artifact ships, so the previous artifact
  must keep running against the new schema.
- **Status:** done

### S-15: The plan card carries the whole prescription

- **Outcome:** user (member) opens their plan and each exercise card shows which muscle group it
  trains, the duration the trainer prescribed where the exercise is measured in time, a distinct info
  icon leading to the exercise details, and the trainer's note set off as a callout.
- **Change ID:** plan-card-prescription-detail
- **PRD refs:** M-3 MS-001, MS-002, MS-003, MS-004
- **Prerequisites:** S-11 (the plan screen and the trainer's builder this extends), S-10 (the muscle
  group is a field on the exercise library's entity)
- **Parallel with:** — (M-3's only slice)
- **Blockers:** —
- **Unknowns:** — (the one design question, where the duration lives, was settled before this slice
  opened: on the plan item, beside weight and rest, not on the exercise definition — see MS-001)
- **Risk:** The schema change is additive and nullable, so the migration itself is the easy part. The
  real risk is contract drift: one shape, `TrainingPlanItemView`, deliberately serves BOTH the
  trainer's edit load and the member's read, so the two new fields touch both screens at once — and a
  field added to the record but missed in the query projection surfaces as a silently empty cell
  rather than as an error. The second risk is scope: three of the four anchors are presentation, and
  a plan card is exactly the kind of surface where "while we're in here" turns one slice into four.
- **Status:** done

### S-16: The karnet decides who trains

- **Outcome:** user (admin) issues a member a karnet — a type name, a validity range and a number of
  entries — and books that member into a class; a trainer does the same for the classes they
  instruct; the member opens the app, sees their karnet and how many entries are left, sees their
  upcoming classes and cannot book or cancel anything; a booking into a class the karnet does not
  cover, or with no entry left, is refused; a newly registered account is active immediately and
  there is no approvals tab.
- **Change ID:** membership-pass-and-staff-booking
- **PRD refs:** M-4 MP-01–MP-07. Retires v1 US-01, FR-002, FR-003, FR-008, FR-009 as member-facing
  capabilities.
- **Prerequisites:** S-01 (registration, whose approval half this removes), S-04 (the Trainer role
  the trainer booking surface is gated on), S-08 (the booking aggregate and its no-overbooking
  protocol this extends), S-14 (`Member`, which the karnet hangs off, and the admin book-on-behalf
  route this generalises)
- **Parallel with:** — (M-4's only slice)
- **Blockers:** —
- **Unknowns:** — (all seven anchors were settled with the user before planning; see the M-4 scope
  anchors)
- **Risk:** two risks, and the smaller one is the new entity. The karnet's entry pool is a second
  read-then-write invariant sitting inside the write path that already carries the club's headline
  guarantee, so it needs its own concurrency stamp rotated in the same `SaveChanges` — a pass with a
  last entry and two classes is exactly the race `Class.ConcurrencyStamp` exists to stop, one level
  up. The larger risk is subtraction: this slice deletes shipped, working capability on both ends
  (self-booking, approval) across API, tests, routes, guards and navigation, and a half-removed
  approval gate leaves accounts that can log in but pass no policy. The migration that activates
  existing pending accounts must land before the code that stops producing them.
- **Status:** done

### S-17: An account is created only by invitation

- **Outcome:** user (admin) records a member without an email address and hands them an invitation
  link; that person opens `/register?invitationCode=…`, finds the code already filled in and not
  editable, supplies only an email address and a password, and lands on the record the club already
  keeps — its bookings, its karnet and its plan already there; opening `/register` without a code
  sends them to the login screen, and the API refuses a registration that carries no code.
- **Change ID:** invitation-only-registration
- **PRD refs:** M-5 IR-01–IR-06. Supersedes v1 FR-001's open self-registration; leaves S-14's AM-004
  (issuing and revoking the code) untouched.
- **Prerequisites:** S-14 (the member code and the claim path this makes the only door), S-16
  (registration that produces an active account — the form this slice shortens is the one S-16 left
  behind)
- **Parallel with:** — (M-5's only slice)
- **Blockers:** —
- **Unknowns:** — (all five anchors were settled with the user before planning; the two calls that
  could have gone either way — what the query parameter is called, and what happens when there is no
  code — are recorded in the M-5 charter)
- **Risk:** two risks, and the sharper one is subtraction from a write path. `DisplayName`, phone and
  address stop arriving at registration and must come from the member record instead, so the create
  path changes shape for inputs it has required since S-01 — and a member record with an empty
  display name would produce a nameless account, a case the admin surface has never had to refuse.
  The second risk is the door itself: the route guard, the removed link and the API refusal have to
  land together, because any one of them alone leaves a way in that the other two claim is closed.
- **Status:** done

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
| S-11       | training-plans                   | Training plan creation, assignment, and member view          | no                    | Done — archived 2026-09-06                          |
| S-12       | member-and-admin-dashboards      | Member and admin dashboards                                  | no                    | Needs S-01, S-07, S-08, S-11                        |
| S-13       | member-profile-edit              | Member profile, password change, and password reset          | no                    | Done — archived 2026-09-06                          |
| S-14       | member-entity-and-accountless-members | Member entity split from the account; accountless members and the claim code | no                    | Done — archived 2026-09-08                          |
| S-15       | plan-card-prescription-detail    | Prescribed duration, muscle group, info icon and note callout on the plan card | no                    | Done — archived 2026-09-09                          |
| S-16       | membership-pass-and-staff-booking | Membership pass gates booking; staff book on a member's behalf; approval retired | no                    | Done — archived 2026-09-09                          |
| S-17       | invitation-only-registration     | Invitation-only registration with a prefilled, readonly code | yes                   | Run `/10x-plan invitation-only-registration` — M-5's only slice |

## Open Roadmap Questions

1. **What happens to a blocked member's existing bookings and assigned plan?** — Owner: user. RESOLVED. The booking half was answered by S-08 (cascade-cancel active future bookings). The plan half was answered during S-11 planning on 2026-09-04: the assigned plan is left untouched. (v1 Open Question 1, v2 Open Question 5.)
2. **Who enters the initial exercise library content, and when?** — Owner: user. Block: none directly, but S-11 delivers no real value until dozens of exercises exist. (v1 Open Question 2, v2 Open Question 6.)
3. **A guest instructor without an account runs one class — what then?** — Owner: user. Block: none; S-06 shipped with the case unsupported and S-14 keeps refusing it. S-14 does make it *representable* — the instructor foreign key moves onto `Member`, so a record with no account can hold the slot — but the `Trainer` role lives in Identity and an accountless record can hold none, so the validation still requires an active account. Closing this is now one column and one branch, not a schema change. (v2 Open Question 1.)
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
- **Payments, subscriptions, invoices** — Why parked: v1 §Non-Goals; the app manages participation, not money. **Passes are no longer parked** — M-4 makes the karnet the thing that decides who trains — but the money half stands: a karnet is issued by an admin, never bought, and carries no price.
- **Waitlist for full classes** — Why parked: v1 §Non-Goals.
- **Full recurring-series management** — Why parked: v1 §Non-Goals; weekly duplication stands in, and v2 explicitly declined to reopen it.
- **Chat / social features; health-app integrations; native mobile apps; self-hosted video; advanced statistics; automatic weight progression** — Why parked: v1 §Non-Goals.

**Unparked by this regeneration:** the Trainer role was a v1 Non-Goal and is no longer parked — v2 retires it, and S-04 delivers it.

## Milestone History

(Append-only.)

- **M-1: First usable MVP** — seq 1, opened 2026-08-31, closed 2026-09-07. Delivered F-01–F-03 and
  S-01–S-13: registration with admin approval, member management, the class-type/occurrence model with
  a calendar, booking and cancellation under a no-overbooking guarantee, email + push notification on
  class changes, the exercise library, training plans, member profile editing, and both dashboards.
  Closed with every F-NN and S-NN `done` and its "Done when" satisfied. Mid-milestone it absorbed
  `prd-v2` (class types, the Trainer role, the room's removal, the calendar) rather than opening a
  milestone for it, because that change restructured how M-1's own slices were built without altering
  its intent.

- **M-2: The club's records outlive the login** (`accountless-members`) — seq 2, opened 2026-09-07,
  closed 2026-09-08. Delivered S-14, its only slice: `Member` split from the Identity account as a
  first-class entity, the club's four foreign keys repointed onto it, membership status added to the
  authorization contract, an admin surface for a person with no login, a single-use member code that
  attaches a new account to an existing record, and admin booking on a member's behalf. Closed with
  its "Done when" satisfied. Its scope anchors AM-001–AM-006 are preserved in `## PRD addendum (M-2)`
  below, which stays in this document because no PRD version describes the accountless model.

- **M-3: The plan card carries the whole prescription** (`plan-card-prescription`) — seq 3, opened
  2026-09-08, closed 2026-09-09. Delivered S-15, its only slice: a nullable per-plan duration beside
  weight and rest, the muscle group on the member's plan card, a separate info icon carrying the
  navigation the exercise name used to, and the trainer's note set off as a callout. Closed with its
  "Done when" satisfied. Its scope anchors MS-01–MS-04 are preserved in this entry; no PRD version
  describes the plan card.

- **M-4: The karnet decides who trains** (`membership-pass-and-staff-booking`) — seq 4, opened
  2026-09-09, closed 2026-09-09. Delivered S-16, its only slice: `MembershipPass` as a named karnet
  with an inclusive validity range and an always-required entry pool, entries derived from active
  bookings rather than stored, booking and release moved onto staff routes (an admin anywhere, a
  trainer in the classes they instruct), self-service booking removed from the member surface, and
  admin approval retired so registration produces an active account. Closed with its "Done when"
  satisfied. Its "not in scope" line stands unchanged: a karnet is issued, never sold. Its scope
  anchors MP-01–MP-07 are preserved in this entry, because no PRD version describes a pass:
  - MP-01: Self-service booking is gone. A member sees their upcoming classes and their plan; they
    cannot book a spot and cannot cancel one. This retires v1 US-01, FR-008 and FR-009 as
    member-facing capabilities — the behaviour survives, but only on staff routes.
  - MP-02: An admin books any member into any class and releases any spot. A trainer does the same,
    but only for the classes they personally instruct.
  - MP-03: Admin approval of new accounts is gone, along with the Zgłoszenia tab and the
    awaiting-approval screen. Registration produces an active account. This retires v1 FR-002 and
    FR-003. Blocking remains the admin's lever, unchanged.
  - MP-04: A `MembershipPass` (karnet) belongs to a member and carries a type name, a validity range
    (inclusive both ends), and an entry count. The entry count is ALWAYS required — there is no
    unlimited pass.
  - MP-05: A member always sees their pass: its type, its validity, and how many entries are left.
    They can never issue or edit one.
  - MP-06: Nobody can be booked into a class unless the member holds a pass valid **on the day of the
    class** with an entry still free. An entry is consumed by an active booking and returned when
    that booking is released — derived from the bookings themselves, never a stored counter.
  - MP-07: A member's passes form a history and their validity ranges may not overlap, so any date
    resolves to at most one pass.

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
- **S-07: user opens the schedule on a phone and sees one day at a time — the current date, a weekday strip that navigates the week it belongs to, a control to jump to a chosen date, and that day's classes with their times on a 06:00–24:00 grid; from 48rem up the whole week is visible at once; the admin panel uses the same calendar with admin actions on top, can look at past weeks read-only, can create a class by dragging across empty time, and can move or resize an existing one on the grid; a day or week with no classes says so.** — Archived 2026-09-03 → `context/archive/2026-09-02-schedule-calendar-view/`. Lesson: —.
- **S-08: user can book a spot in a class with free capacity (free-spot count drops by one, the class appears in their upcoming list), cancel the booking (spot released, cancelled booking kept in history), and view all upcoming classes; the admin can view a class's booking list. A class never accepts more bookings than it has spots, even under simultaneous requests.** — Archived 2026-09-04 → `context/archive/2026-09-03-class-booking-and-cancel/`. Lesson: —.
- **S-09: user (admin) cancels a class → it moves to a visible `cancelled` state (not deleted; bookings and history preserved) → every booked member receives an email and a push notification within minutes, and the class disappears from their upcoming bookings; editing a booked class triggers the same delivery.** — Archived 2026-09-04 → `context/archive/2026-09-04-class-change-notifications/`. Lesson: —.
- **S-10: user (admin) can create and edit exercises — description, muscle group, difficulty, equipment, and preparation / starting-position / execution instructions, all optional — and attach an instructional video to an exercise.** — Archived 2026-09-04 → `context/archive/2026-09-04-exercise-library/`. Lesson: —.
- **S-11: user (admin) can create a training plan (ordered exercise list with sets, reps, weight, rest time, note) and assign it to a member — a new assignment replaces the old, so each member has at most one active plan; the member sees their current plan and opens any exercise's details from within it.** — Archived 2026-09-06 → `context/archive/2026-09-04-training-plans/`. Lesson: —.
- **S-13: member sees their name and email, edits their contact details (phone and address), changes their password while signed in, and resets a forgotten password by emailed link before login. The display name is deliberately NOT editable — the gym owns how a member appears on its lists.** — Archived 2026-09-06 → `context/archive/2026-09-05-member-profile-edit/`. Lesson: Verify a prerequisite against the code, not against an archived plan (`context/foundation/lessons.md`).
- **S-12: user (member) lands on a dashboard showing their nearest upcoming classes and active training plan; the admin lands on a dashboard of items needing attention — pending approvals, today's and upcoming classes.** — Archived 2026-09-07 → `context/archive/2026-09-06-member-and-admin-dashboards/`. Lesson: —.
- **S-14: the admin creates a member record for someone who has never registered, edits and blocks it, assigns them a training plan and books them into a class; the admin issues a single-use code, and the person registers with it and lands on that record — their existing bookings and their active plan already there.** — Archived 2026-09-08 → `context/archive/2026-09-07-member-entity-and-accountless-members/`. Lesson: —.
- **S-15: user (member) opens their plan and each exercise card shows which muscle group it trains, the duration the trainer prescribed where the exercise is measured in time, a distinct info icon leading to the exercise details, and the trainer's note set off as a callout.** — Archived 2026-09-09 → `context/archive/2026-09-08-plan-card-prescription-detail/`. Lesson: —.
- **S-16: user (admin) issues a member a karnet — a type name, a validity range and a number of entries — and books that member into a class; a trainer does the same for the classes they instruct; the member opens the app, sees their karnet and how many entries are left, sees their upcoming classes and cannot book or cancel anything; a booking into a class the karnet does not cover, or with no entry left, is refused; a newly registered account is active immediately and there is no approvals tab.** — Archived 2026-09-09 → `context/archive/2026-09-09-membership-pass-and-staff-booking/`. Lesson: —.
- **S-17: user (admin) records a member without an email address and hands them an invitation link; that person opens `/register?invitationCode=…`, finds the code already filled in and not editable, supplies only an email address and a password, and lands on the record the club already keeps — its bookings, its karnet and its plan already there; opening `/register` without a code sends them to the login screen, and the API refuses a registration that carries no code.** — Archived 2026-09-10 → `context/archive/2026-09-09-invitation-only-registration/`. Lesson: —.
