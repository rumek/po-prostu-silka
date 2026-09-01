---
project: "Po Prostu Siłka"
version: 1
status: draft
created: 2026-08-31
updated: 2026-09-01
prd_version: 1
main_goal: speed
top_blocker: external
milestone_id: first-usable-mvp
milestone_seq: 1
milestone_status: open
---

# Roadmap: Po Prostu Siłka

> Derived from `context/foundation/prd.md` (v1) + `tech-stack.md` + `infrastructure.md` + `context/deployment/deploy-plan.md` + auto-researched codebase baseline.
> Edit-in-place; archive when superseded.
> Slices below are listed in dependency order. The "At a glance" table is the index.

## Milestone

**M-1: First usable MVP** — Status: open

- **Intent:** The club runs on the app instead of Excel: a member can register, get approved, browse the schedule, book and cancel classes, follow their assigned training plan — and is reliably told by email + push when a booked class is cancelled or changed.
- **Source materials:** `context/foundation/prd.md` (v1)
- **Done when:** every F-NN and S-NN below is `done`.
- **Scope anchors:** FR-001–FR-024 (all must-have; FR-022 removed by the PRD itself), US-01, US-02, and the four NFRs (mobile-first/installable, ~1 s perceived response, notification promptness, personal-data privacy).

## Vision recap

A single gym runs class sign-ups, schedule changes, and individual training plans through Excel: booking chaos before popular classes, no reliable way to tell members about cancellations, and plans handed out as files or printouts. The owner feels the cost as lost professional image next to competitors with booking apps. This app puts the group-class schedule, bookings, individual training plans, and an exercise library (instructions + videos) in one mobile-first place — the combination existing gym SaaS doesn't offer.

## North star

**S-05: Booked member is notified by email and push when the admin cancels or changes their class** — the most-felt pain is that members can't be reached when plans change, and this slice is also the riskiest to build (background delivery worker, outbox + retry, multi-day email-domain verification), so shipping it is what proves the product works — directly serving the speed goal by de-risking the hardest part of the must-have set early.

> "North star" here means the smallest end-to-end slice whose successful delivery would prove the core product hypothesis — placed as early as its Prerequisites allow, because everything else only matters if this works.

## At a glance

| ID   | Change ID                       | Outcome (user can …)                                                    | Prerequisites          | PRD refs                              | Status   |
| ---- | ------------------------------- | ----------------------------------------------------------------------- | ---------------------- | ------------------------------------- | -------- |
| F-01 | persistence-foundation          | (foundation) EF Core + Azure SQL wired; migrations run on deploy        | —                      | NFR privacy, Business Logic           | done     |
| F-02 | auth-identity-foundation        | (foundation) Identity auth, User/Admin roles, admin seeded              | F-01                   | FR-001, FR-002, Access Control        | done     |
| F-03 | notification-delivery-foundation | (foundation) email + push transport with outbox/retry landed            | F-01                   | FR-021, NFR promptness                | done     |
| S-01 | registration-and-approval       | register, wait at approval screen; admin approves                       | F-01, F-02             | FR-001, FR-002, FR-003                | done        |
| S-02 | member-management               | admin searches/filters members, blocks and unblocks                     | S-01                   | FR-004, FR-005                        | done     |
| S-03 | class-schedule-and-admin        | browse day-by-day schedule; admin creates/edits/duplicates classes      | S-01                   | FR-007, FR-011, FR-012                | proposed |
| S-04 | class-booking-and-cancel        | book a spot, cancel it, see upcoming classes; admin sees bookings       | S-03                   | US-01, FR-008, FR-009, FR-010, FR-014 | proposed |
| S-05 | class-change-notifications      | booked member gets email + push on class cancel/change                  | F-03, S-04             | US-02, FR-013, FR-021, FR-011         | proposed |
| S-06 | exercise-library                | admin manages exercises with instructions and YouTube videos            | S-01                   | FR-018, FR-019                        | proposed |
| S-07 | training-plans                  | admin builds and assigns a plan; member follows it with exercise details | S-01, S-06             | FR-015, FR-016, FR-017, FR-020        | proposed |
| S-08 | member-and-admin-dashboards     | member and admin land on their at-a-glance home screens                 | S-01, S-03, S-04, S-07 | FR-023, FR-024                        | proposed |
| S-09 | member-profile-edit             | member edits display name and changes password                          | F-02, S-01             | FR-006                                | proposed |

## Streams

Navigation aid — groups items that share a Prerequisites chain. Canonical ordering still lives in the dependency graph below; this table is the proposed reading order across parallel tracks.

| Stream | Theme                 | Chain                                    | Note                                                                                    |
| ------ | --------------------- | ---------------------------------------- | --------------------------------------------------------------------------------------- |
| A      | Access & identity     | `F-01` → `F-02` → `S-01` → `S-02` → `S-09` | The spine — everything else hangs off an approved account.                              |
| B      | Notification delivery | `F-03` → `S-05`                          | Carries the north star; F-03 starts early (external email-domain lead time), S-05 joins Stream C at `S-04`. |
| C      | Schedule & booking    | `S-03` → `S-04` → `S-08`                 | The booking core; `S-08` also joins from Stream D at `S-07` (it aggregates both).       |
| D      | Training domain       | `S-06` → `S-07`                          | Independent bounded context — a separate agent run can build it in parallel with Stream C. |

## Baseline

What's already in place in the codebase as of `2026-08-31` (auto-researched + user-confirmed).
Foundations below assume these are present and do NOT re-scaffold them.

- **Frontend:** partial — Angular 22 SPA scaffolded at `src/app/` (per tech-stack.md), template-only: empty routes, default component, static build served from the API's `wwwroot`.
- **Backend / API:** partial — ASP.NET Core .NET 10 minimal API (`src/Program.cs`) with only the sample `/weatherforecast` endpoint; OpenAPI and SPA fallback wired.
- **Data:** absent — no EF Core packages, no DbContext, no migrations, no connection string (`deploy-plan.md` confirms Azure SQL was deliberately deferred).
- **Auth:** absent — no ASP.NET Core Identity, no auth middleware; tech-stack.md pins Identity as the choice.
- **Deploy / infra:** present — Azure App Service (Linux, B1) live at `po-prostu-silka.azurewebsites.net`, GitHub Actions deploy on push to `main`, verified end-to-end (`deploy-plan.md`). Azure SQL not yet provisioned.
- **Observability:** absent — console logging only; App Insights deliberately parked (see `## Parked`).

**Architecture intent (user-stated):** the domain is to be organised the DDD way — bounded contexts (membership, scheduling/booking, training, notifications), aggregates guarding invariants (class capacity), and domain events for cross-context reactions (class cancelled → notify booked members). This roadmap groups slices along those context lines; aggregate boundaries, repositories, and event mechanics are `/10x-plan`'s territory, not decided here.

## Foundations

### F-01: Persistence foundation

- **Outcome:** (foundation) Azure SQL Database (Basic DTU tier) provisioned and connected; EF Core installed with a bootstrapped DbContext; schema migrations run automatically on deploy; connection string lives in App Service settings; Always On re-verified.
- **Change ID:** persistence-foundation
- **PRD refs:** NFR "personal data privacy" (data lives in a managed DB, not files), Business Logic (all rules consume persisted state)
- **Unlocks:** S-01 (accounts must persist) and every downstream slice that stores data; establishes the migration-on-deploy verification path all later slices rely on; reduces infrastructure.md's reversible-migration/rollback risk by settling the policy now.
- **Prerequisites:** —
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** first infra-touching change since go-live. Must be the Basic DTU tier, not the free serverless offer (auto-pause quota trap per infrastructure.md); each later slice brings its own entities and migration — this foundation deliberately ships plumbing plus one proving migration, not the whole schema.
- **Status:** done

### F-02: Auth & identity foundation

- **Outcome:** (foundation) ASP.NET Core Identity wired for email + password; sessions long-lived and mobile-friendly (FR-002 design note); flat User/Admin role model; an admin account seeded at setup (never self-registered); route-level authorization available; unauthenticated access limited to login and registration.
- **Change ID:** auth-identity-foundation
- **PRD refs:** FR-001, FR-002, Access Control (roles, admin origin, unauthenticated access), NFR "personal data privacy"
- **Unlocks:** S-01 (registration and the approval gate need Identity records and the Admin role) and every authenticated screen in S-02–S-09.
- **Prerequisites:** F-01
- **Parallel with:** F-03
- **Blockers:** —
- **Unknowns:**
  - Target session lifetime on mobile (30 / 60 / 90 days)? — Owner: user. Block: no.
- **Risk:** Identity brings a broad surface; scoped to email+password, roles, and the admin seed — no reset/confirmation flows beyond what registration needs. Password change is its own slice (S-09), keeping this foundation minimal.
- **Status:** done

### F-03: Notification delivery foundation

- **Outcome:** (foundation) a transactional email path (Azure Communication Services, or the documented SMTP fallback) with a verified sender domain; Web Push with a subscription endpoint and stored browser subscriptions; an outbox table plus an idempotent retry worker (hosted service) that survives App Service recycles; a heartbeat log line and outbox-failure count for visibility; one test message delivered end-to-end to a real inbox and device.
- **Change ID:** notification-delivery-foundation
- **PRD refs:** FR-021, NFR "notification promptness", Success Criteria guardrail "no missed cancellations"
- **Unlocks:** S-05 (the north star — cancel/change notifications) and the account-approved notification of FR-021; creates the delivery verification path S-05 is tested against.
- **Prerequisites:** F-01 (the outbox table needs persistence)
- **Parallel with:** F-02
- **Blockers:** ACS email sender-domain verification — DNS records + sender approval, provider-side, multi-day elapsed time. Owner: user (start immediately; see Open Roadmap Questions #3).
- **Unknowns:**
  - Push on iOS requires iOS 16.4+ and a home-screen install — acceptable for the member base, with email as the guaranteed channel and push best-effort? — Owner: user. Block: no.
- **Risk:** the #1-blocker foundation, pulled early because domain verification is multi-day elapsed time infrastructure.md says "belongs in week 1, not week 3". App Service recycles drop in-flight sends unless delivery goes through the outbox + retry — fire-and-forget is explicitly ruled out.
- **Status:** done

## Slices

### S-01: Member registers, and admin approves the account

- **Outcome:** user can register with email + password, log in while `pending` and see only the awaiting-approval screen (no schedule, no booking); the admin sees pending registrations and approves one; the approved member logs in and reaches the app proper.
- **Change ID:** registration-and-approval
- **PRD refs:** FR-001, FR-002, FR-003, Access Control (account lifecycle, pending state), Business Logic (approval gates everything)
- **Prerequisites:** F-01, F-02
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** the pending → active state machine is the spine of the whole access model; getting it right here de-risks every later slice. Approval raises the domain event whose email/push delivery lands with S-05 — no notification is required for this slice to be done.
- **Status:** done

### S-02: Admin manages members

- **Outcome:** user (admin) can browse all members in one searchable list with status badges and a pending / active / blocked filter, block a member (who then loses access to app content), and unblock them.
- **Change ID:** member-management
- **PRD refs:** FR-004, FR-005
- **Prerequisites:** S-01
- **Parallel with:** S-03, S-06
- **Blockers:** —
- **Unknowns:**
  - None blocking. Two decisions surfaced by `context/changes/member-management/frame.md` that the plan must settle rather than inherit: (a) block must rotate the Identity security stamp, or a blocked member's live cookie keeps passing the `ActiveMember` policy for up to 30 minutes (`src/Program.cs:118-122`); (b) the seeded admin is an ordinary `ApplicationUser` in the same table (`AdminSeeder.cs:57-65`) and must be excluded from the blockable member list, or a solo-admin club can lock itself out.
- **Risk:** low. Mostly a generalisation of S-01's shipped approvals surface — the endpoint group, admin policy, `Application`→`Infrastructure` query seam, and the status index (`ApplicationUserConfiguration.cs:29-30`, added for this slice) all already exist. The real care goes into session revocation and admin self-block, not into new construction.
- **Status:** done

### S-03: Member browses the schedule; admin runs it

- **Outcome:** user (admin) can create a class (name, date/time, room, instructor, capacity), edit it, and duplicate classes to following weeks; an active member can browse the schedule as a mobile-friendly day-by-day list showing name, date/time, room, instructor, and free spots.
- **Change ID:** class-schedule-and-admin
- **PRD refs:** FR-007, FR-011, FR-012
- **Prerequisites:** S-01
- **Parallel with:** S-02, S-06
- **Blockers:** —
- **Unknowns:** —
- **Risk:** the read surface the booking slice builds on; "day-by-day list, not a calendar grid" (PRD design note) bounds mobile scope. Editing a class that already has bookings triggers a notification — that corner of FR-011 belongs to S-05, not here.
- **Status:** proposed

### S-04: Member books and cancels a class spot

- **Outcome:** user can book a spot in a class with free capacity (free-spot count drops by one, the class appears in their upcoming list), cancel the booking (spot released, cancelled booking kept in history), and view all upcoming classes; the admin can view a class's booking list. A class never accepts more bookings than it has spots, even under simultaneous requests.
- **Change ID:** class-booking-and-cancel
- **PRD refs:** US-01, FR-008, FR-009, FR-010, FR-014
- **Prerequisites:** S-03
- **Parallel with:** S-06, S-07
- **Blockers:** —
- **Unknowns:**
  - What happens to a blocked member's *existing bookings* — cascade-cancel on block, or leave them standing while access is refused? — Owner: user. Block: yes. (PRD Open Question 1, booking half; reassigned here from S-02 because the `Booking` aggregate is defined in this slice.)
- **Risk:** the no-overbooking guardrail must hold under concurrent booking — the load-bearing correctness work of the milestone (deep-investment area). Sequenced immediately before S-05, which needs real bookings to notify against.
- **Status:** proposed

### S-05: Booked member is notified when their class is cancelled or changed

- **Outcome:** user (admin) cancels a class → it moves to a visible `cancelled` state (not deleted; bookings and history preserved) → every booked member receives an email and a push notification within minutes, and the class disappears from their upcoming bookings; editing a booked class triggers the same class-changed delivery; the account-approved notification (from S-01's event) rides the same path.
- **Change ID:** class-change-notifications
- **PRD refs:** US-02, FR-013, FR-021, FR-011 (booked-class-edit trigger), NFR "notification promptness", guardrail "no missed cancellations"
- **Prerequisites:** F-03, S-04
- **Parallel with:** S-07
- **Blockers:** ACS sender-domain verification complete (carried from F-03). Owner: user.
- **Unknowns:** —
- **Risk:** the north star and the differentiator — delivery must survive platform recycles (outbox + retry from F-03), and `cancelled` must be a state transition, not a delete. Everything before it exists so this slice can be real.
- **Status:** proposed

### S-06: Admin builds the exercise library

- **Outcome:** user (admin) can create and edit exercises — description, muscle group, difficulty, equipment, and preparation / starting-position / execution instructions (all fields optional) — and attach a YouTube instructional video to an exercise.
- **Change ID:** exercise-library
- **PRD refs:** FR-018, FR-019
- **Prerequisites:** S-01
- **Parallel with:** S-02, S-03, S-04
- **Blockers:** —
- **Unknowns:**
  - Who enters the initial exercise content, and when? — Owner: user. Block: no. (Content-ops, not a build blocker — but plans stay useless until dozens of exercises exist; see Open Roadmap Questions #2.)
- **Risk:** low-risk admin CRUD in an independent bounded context — the head of the training stream, buildable by a separate agent run in parallel with the booking chain (no shared dependencies beyond S-01).
- **Status:** proposed

### S-07: Admin assigns a training plan; member follows it

- **Outcome:** user (admin) can create a training plan (ordered exercise list with sets, reps, weight, rest time, note) and assign it to a member — assigning a new plan replaces (archives) the old, so each member has at most one active plan; the member sees their current plan after logging in and opens any exercise's details (instructions + video) from within it.
- **Change ID:** training-plans
- **PRD refs:** FR-015, FR-016, FR-017, FR-020
- **Prerequisites:** S-01, S-06
- **Parallel with:** S-03, S-04, S-05
- **Blockers:** —
- **Unknowns:**
  - What happens to a blocked member's *assigned plan* — does blocking void, archive, or simply hide it? — Owner: user. Block: yes. (PRD Open Question 1, plan half; reassigned here from S-02 because the `TrainingPlan` aggregate is defined in this slice.)
- **Risk:** the one-active-plan replace/archive rule is the domain invariant to get right; exercise details are reached only from the plan (standalone browsing is a Non-Goal), which bounds the UI.
- **Status:** proposed

### S-08: Member and admin dashboards

- **Outcome:** user (member) lands on a dashboard showing their nearest upcoming classes and active training plan; the admin lands on a dashboard of items needing attention — pending approvals, today's and upcoming classes.
- **Change ID:** member-and-admin-dashboards
- **PRD refs:** FR-023, FR-024
- **Prerequisites:** S-01, S-03, S-04, S-07
- **Parallel with:** S-09
- **Blockers:** —
- **Unknowns:** —
- **Risk:** pure aggregation over data the earlier slices produce — deliberately last, because building it earlier means stubbing every card.
- **Status:** proposed

### S-09: Member edits their profile

- **Outcome:** user can edit their display name and change their password.
- **Change ID:** member-profile-edit
- **PRD refs:** FR-006
- **Prerequisites:** F-02, S-01
- **Parallel with:** S-02, S-03, S-04, S-06, S-07, S-08
- **Blockers:** —
- **Unknowns:** —
- **Risk:** smallest slice in the milestone; nothing depends on it, so it slots into any idle parallel lane after S-01.
- **Status:** proposed

## Backlog Handoff

| Roadmap ID | Change ID                        | Suggested issue title                                        | Ready for `/10x-plan` | Notes                                      |
| ---------- | -------------------------------- | ------------------------------------------------------------ | --------------------- | ------------------------------------------ |
| F-01       | persistence-foundation           | Provision Azure SQL and wire EF Core persistence             | yes                   | Run `/10x-plan persistence-foundation`     |
| F-02       | auth-identity-foundation         | Add ASP.NET Core Identity, roles, and admin seed             | no                    | Needs F-01                                 |
| F-03       | notification-delivery-foundation | Build email + push delivery with outbox/retry                | no                    | Needs F-01; start ACS domain verification now |
| S-01       | registration-and-approval        | Member registration with admin approval gate                 | no                    | Needs F-01, F-02                           |
| S-02       | member-management                | Member list, filter, block/unblock                           | yes                   | Framed 2026-09-01; run `/10x-plan member-management`  |
| S-03       | class-schedule-and-admin         | Class schedule browsing and admin class management           | no                    | Needs S-01                                 |
| S-04       | class-booking-and-cancel         | Class booking and cancellation with no-overbooking guarantee | no                    | Needs S-03                                 |
| S-05       | class-change-notifications       | Email + push notifications on class cancel/change            | no                    | North star; needs F-03, S-04               |
| S-06       | exercise-library                 | Exercise library management with YouTube videos              | no                    | Needs S-01                                 |
| S-07       | training-plans                   | Training plan creation, assignment, and member view          | no                    | Needs S-01, S-06                           |
| S-08       | member-and-admin-dashboards      | Member and admin dashboards                                  | no                    | Needs S-01, S-03, S-04, S-07               |
| S-09       | member-profile-edit              | Member profile: edit name and change password                | no                    | Needs F-02, S-01                           |

## Open Roadmap Questions

1. **What happens to a blocked member's existing bookings and assigned plan?** — Owner: user. Block: S-04, S-07. (PRD Open Question 1. Reassigned from S-02 on 2026-09-01 by `context/changes/member-management/frame.md`: the question asks about consequences on `Booking` and `TrainingPlan`, neither of which exists yet, and S-02 ships nothing that touches them — it is decidable, and binding, only once those aggregates land.)
2. **Who enters the initial exercise library content, and when?** — Owner: user. Block: none directly, but S-07 delivers no real value until dozens of exercises exist. (PRD Open Question 2 — schedule the content entry alongside S-06.)
3. **Which sender domain will notification email use, and is DNS access available to add ACS verification records?** — Owner: user. Block: F-03 (and transitively S-05). Multi-day provider-side lead time — the single highest-leverage thing to start today.
4. **Is best-effort push acceptable on iOS (16.4+, home-screen install required), with email as the guaranteed channel?** — Owner: user. Block: none — F-03 proceeds either way; the answer sets S-05's acceptance bar.

## Parked

- **Observability beyond a heartbeat + outbox-failure count** — Why parked: no must-have FR requires it, speed is the goal, and infrastructure.md flags App Insights bill-creep; revisit if notification-failure visibility proves insufficient.
- **Multi-club / multi-tenancy** — Why parked: PRD §Non-Goals — one gym's app; scaling out is a deliberate future decision.
- **Attendance / check-in tracking** — Why parked: PRD §Non-Goals; booking lists only (noted post-MVP candidate at FR-014).
- **Offline-first guarantee** — Why parked: PRD §Non-Goals; installable, but a connection is required.
- **Cancellation deadline / late-cancel rules** — Why parked: PRD §Non-Goals; free cancel anytime is locked.
- **In-app notification center** — Why parked: PRD §Non-Goals; delivery is email + push only (FR-022 removed).
- **Standalone exercise library browsing** — Why parked: PRD §Non-Goals; exercises are reached from the plan only.
- **Plan history / versioning UI** — Why parked: PRD §Non-Goals; one active plan per member.
- **Account rejection status** — Why parked: PRD §Non-Goals; lifecycle is pending / active / blocked.
- **Payments, passes, subscriptions, invoices** — Why parked: PRD §Non-Goals (seed); the app manages participation, not money.
- **Waitlist for full classes** — Why parked: PRD §Non-Goals (seed).
- **Full recurring-series management** — Why parked: PRD §Non-Goals (seed); weekly duplication (FR-012) stands in.
- **Trainer role** — Why parked: PRD §Non-Goals (seed); User and Admin only.
- **Chat / social features; Apple Health / Google Fit; native mobile apps; self-hosted video; advanced statistics; automatic weight progression** — Why parked: PRD §Non-Goals (seed).

## Milestone History

(Append-only. Empty on the first milestone.)

## Done

(Empty on first generation. `/10x-archive` appends an entry here — and flips that item's `Status` to `done` — when a change whose `Change ID` matches the item is archived. Do NOT pre-populate.)

- **F-01: (foundation) Azure SQL Database (Basic DTU tier) provisioned and connected; EF Core installed with a bootstrapped DbContext; schema migrations run automatically on deploy; connection string lives in App Service settings; Always On re-verified.** — Archived 2026-08-31 → `context/archive/2026-08-31-persistence-foundation/`. Lesson: —.
- **F-02: (foundation) ASP.NET Core Identity wired for email + password; sessions long-lived and mobile-friendly (FR-002 design note); flat User/Admin role model; an admin account seeded at setup (never self-registered); route-level authorization available; unauthenticated access limited to login and registration.** — Archived 2026-08-31 → `context/archive/2026-08-31-auth-identity-foundation/`. Lesson: —.
- **F-03: (foundation) a transactional email path (Azure Communication Services, or the documented SMTP fallback) with a verified sender domain; Web Push with a subscription endpoint and stored browser subscriptions; an outbox table plus an idempotent retry worker (hosted service) that survives App Service recycles; a heartbeat log line and outbox-failure count for visibility; one test message delivered end-to-end to a real inbox and device.** — Archived 2026-09-01 → `context/archive/2026-08-31-notification-delivery-foundation/`. Lesson: Record necessary adaptations in the plan, not only in the deploy log (`context/foundation/lessons.md`).
- **S-01: user can register with email + password, log in while `pending` and see only the awaiting-approval screen (no schedule, no booking); the admin sees pending registrations and approves one; the approved member logs in and reaches the app proper.** — Archived 2026-09-01 → `context/archive/2026-09-01-registration-and-approval/`. Lesson: —.
- **S-02: user (admin) can browse all members in one searchable list with status badges and a pending / active / blocked filter, block a member (who then loses access to app content), and unblock them.** — Archived 2026-09-01 → `context/archive/2026-09-01-member-management/`. Lesson: —.
