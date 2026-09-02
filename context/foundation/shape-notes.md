---
project: "Po Prostu Siłka"
context_type: brownfield
created: 2026-09-02
updated: 2026-09-02
checkpoint:
  current_phase: 8
  phases_completed: [1, 2, 3, 4, 5, 6, 7]
  gray_areas_resolved:
    - topic: "trainer role depth"
      decision: "full third role — Trainer joins ApplicationRoles.All, is seeded, and is granted by admin to existing accounts; reverses the PRD Non-Goal 'No Trainer role — User and Admin only'"
    - topic: "class type ownership"
      decision: "type owns name, description, default duration, default capacity; instructor stays per-occurrence (no default on the type)"
    - topic: "room removal vs overlap invariant"
      decision: "Room drops from the model but the invariant survives with a widened meaning — one class at a time in the club; 409 room_conflict becomes time_conflict"
    - topic: "calendar UI scope"
      decision: "both the member schedule and the admin class panel move to the new calendar component"
    - topic: "how an account becomes a Trainer"
      decision: "admin grants the role to an existing approved account from the member-management screen; no separate trainer registration path"
    - topic: "role additivity"
      decision: "REVISED mid-phase — roles are additive, not exclusive. Trainer adds a label without removing member capability; an account may hold Admin + Trainer, or User + Trainer. The user first chose exclusivity, then reversed it once the empty-trainer contradiction was surfaced."
    - topic: "trainer permissions in this change"
      decision: "none beyond User — the role only feeds the instructor select; no trainer-only screen, no new authorization policy"
    - topic: "existing bookings when the role is granted"
      decision: "dissolved by additivity — bookings and the assigned plan stay active and usable; nothing is cancelled or hidden"
    - topic: "who manages class type definitions"
      decision: "Admin only, consistent with every other scheduling write"
    - topic: "delivery shape"
      decision: "two stages under one brownfield PRD — Stage 1 model (class type, Trainer role, room removal, selects in the admin form); Stage 2 calendar on both screens. Each stage leaves a working system."
    - topic: "type-to-occurrence binding"
      decision: "name and description live by REFERENCE (editing the type changes every occurrence, past ones included); duration and capacity are COPIES taken at creation"
    - topic: "type deletion"
      decision: "deactivation only - no hard delete; existing occurrences and history untouched"
    - topic: "occurrence name override"
      decision: "none - the occurrence has no name of its own; the name comes from the type"
    - topic: "instructor select contents"
      decision: "active accounts holding the Trainer role only; pending and blocked are not selectable"
    - topic: "existing scheduling data"
      decision: "discarded - the database holds development data only. Migration clears classes and bookings; accounts, roles, statuses and training plans survive. The Phase 3 no-booking-is-lost guardrail was rewritten accordingly."
    - topic: "Room column removal timing"
      decision: "lags one release - the column stays in the schema after the code stops reading it, dropped in a later migration, per the repository rule that destructive changes trail the code"
    - topic: "timeline"
      decision: "no hard deadline, after-hours pace; the four-part scope cost was surfaced and answered by staging rather than by a longer single wave"
  frs_drafted: 18
  quality_check_status: accepted
---

# Shape Notes

Seed (user's words, 2026-09-02): change how class types are defined — a type must be a
*definition*, and occurrences are built from it. The occurrence-creation form gets a select for
the class type. Room is dropped (the gym has only one room). The instructor is also picked from a
select, listing people who hold a Trainer role. Separately, the schedule display becomes a
calendar: on mobile, the current date on top with a calendar icon to jump dates; a second row with
a left arrow, the 7 weekday chips, and a right arrow to change week; picking a day reveals the
list of that day's classes with times below. On web/tablet the whole week fits at once.

## Current System

Po Prostu Siłka is live in its first milestone: an ASP.NET Core 10 Web API with an Angular 22 SSR
PWA, EF Core over SQL Server. The `class-schedule-and-admin` slice shipped (5 commits, migration
`AddClassSchedule`), so this change lands on working code with real bookings behind it.

- **Scheduling model.** `Domain/Scheduling/Class.cs` is a single flat entity: `Name`, `StartsAt`,
  `DurationMinutes`, `Room`, `Instructor`, `Capacity`, `Status`, `CreatedAt`. There is no class
  *type* — `Name`, `Room`, and `Instructor` are free text retyped for every occurrence.
- **Overlap invariant.** `ClassStore.HasRoomConflictAsync` enforces one class at a time per room.
  `ClassEndpoints` returns 409 `room_conflict` on create and edit, and the duplicate-to-weeks
  endpoint *skips* conflicting weeks, reporting them as `SkippedWeeks` (deliberate partial
  success).
- **Roles.** `Domain/ApplicationRoles.cs` is a flat two-role model — `User`, `Admin` — with an
  explicit comment that no Trainer role exists in MVP. `Class.Instructor` is documented as free
  text *precisely because* there is no trainer account to link to.
- **Schedule UI.** `features/schedule/schedule.ts` renders a day-grouped list, matching the PRD's
  FR-007 design note ("day-by-day list, not a calendar grid"). The admin side is a separate list
  at `features/admin/classes/`.
- **Capacity.** No denormalized booking counter exists; free spots are projected at read time via
  `IClassScheduleQuery`. `Capacity` is the value the no-overbooking guarantee is checked against.

**Must not break:** the no-overbooking guarantee; existing bookings and class statuses; a working
reversible `Down` on every migration; the duplicate-to-weeks partial-success behaviour.

## Vision & Problem Statement

Today the admin retypes a class's identity — its name, its room, its instructor — every single
time they put an occurrence on the schedule. Nothing binds the Monday "Joga dla początkujących" to
next Monday's; they are two unrelated rows that merely share a spelling. Weekly duplication hides
the cost rather than removing it, and a typo silently forks one class into two in the member's
eyes.

This change introduces the missing layer: a **class type** is defined once (name, description,
default duration, default capacity), and every occurrence is built from that definition by
selecting it. Two fields stop being free text at the same time — **room disappears entirely**
(the club has one room, so the field never carried information), and **instructor becomes a
choice from real people**, which requires the Trainer role the MVP deliberately deferred.

The second half is presentation: the day-grouped list becomes a calendar. On a phone that means a
week strip with arrows and a day picker, revealing one day's classes at a time; on tablet and web
the full week is visible at once. This deliberately revisits the FR-007 decision that ruled out a
"calendar grid" as phone-hostile — the mobile design here is a day picker, not a grid, so the
original concern is answered rather than overridden. The grid appears only where there is room
for it.

## User & Persona

Existing personas, unchanged in kind but affected by this change:

- **Club owner / admin** — the primary beneficiary. Stops retyping class identity, defines each
  class once, and picks type + instructor from selects. Also gets the new calendar in the admin
  panel.
- **Gym member** — sees the same classes through a new lens: a week strip and day picker on the
  phone, a full week on a larger screen.

New persona introduced by this change:

- **Trainer** — a person who runs classes and now holds a real account with the `Trainer` role,
  granted by the admin. What a trainer can actually *do* once logged in is not yet decided; it is
  the subject of Phase 2 (Access Control) and must not be assumed here.

> Socrates (blast radius): the thing an existing user would notice first if this went wrong is a
> class vanishing from the schedule, or a booking detached from its class, during the migration
> that splits `Class` into type + occurrence. Existing rows carry free-text names that must be
> folded into generated types without losing a single booking.

## Access Control

**Current model — preserved unchanged.** Email + password login; self-registration gated by admin
approval; lifecycle pending → active with block/unblock; admin accounts seeded at setup, never
self-registered; a pending user can log in but sees only the awaiting-approval screen.

**What changes:** a third role.

- **`Trainer` joins `ApplicationRoles.All`** and is seeded alongside `User` and `Admin`. This
  reverses the PRD Non-Goal "No Trainer role — User and Admin only", and the reversal is
  deliberate: the instructor select cannot list people if no person is modelled.
- **Roles are additive, not exclusive.** An account holds a *set* of roles, the way ASP.NET
  Identity already models it. Granting `Trainer` takes nothing away: a trainer still books
  classes and still has a training plan. `Admin` + `Trainer` is a valid combination — the club
  owner who runs classes themselves appears in the same select as everyone else.
- **Granting the role** is an admin action on an existing approved account, reached from the
  member-management screen (the same list that already carries status badges and filters). There
  is no separate trainer registration path and no admin-created trainer account.
- **Trainer permissions in this change: none beyond `User`.** The role is, for now, purely a label
  that populates the instructor select. No trainer-only screen, no new authorization policy.
  Whether trainers eventually see "my classes" is a later change, and the additive role model
  leaves that path open.
- **Class type definitions are Admin-only**, consistent with every other write in the scheduling
  context.

> Socrates: the exclusivity path was tried first and abandoned. Making `Trainer` exclude `User`
> would have left a trainer without the Admin role logging in to an application with no
> functionality at all — the role removed member features while this change adds no trainer
> features to replace them. Additivity dissolves that, and with it the question of what happens to
> a member's live bookings when the role is granted: nothing happens, because nothing is removed.

## Success Criteria

Proof flow for this change (what a user does differently once it ships):

1. Admin defines a class type once: name, description, default duration, default capacity
2. Admin grants the Trainer role to an existing approved account
3. Admin creates an occurrence by selecting the type (duration and capacity prefill from the
   definition and stay overridable) and selecting the instructor from the trainer list; there is
   no room field
4. Member opens the schedule on a phone: a week strip with arrows, picks a day, sees that day's
   classes with times
5. Member books a spot — the no-overbooking guarantee holds exactly as before
6. The same calendar serves the admin panel; on tablet and web the whole week is visible at once

### Delivery staging

Steps 1–3 are Stage 1 (model and admin form). Steps 4–6 are Stage 2 (calendar). Each stage leaves
the application working end to end; Stage 1 ships against the existing day-grouped list, and
Stage 2 does not depend on anything Stage 1 defers.

### Primary
- The proof flow works end to end: a class type is defined once and reused across occurrences,
  the instructor is a real account rather than retyped text, and the schedule is navigable as a
  calendar on a phone and as a full week on a larger screen.

### Secondary
- The admin lays out a week of classes without typing a single name by hand - every identity field
  comes from a select. Measurable proof the definition layer did its job.

### Guardrails
- **No overbooking, unchanged.** Capacity moves onto the occurrence as a copied value, never read
  through a reference to the type — changing a type's default must not retroactively alter a
  scheduled class's capacity or the guarantee checked against it.
- **The wipe stays narrow, and reversibility holds.** The database carries development data only,
  so the migration discards classes and bookings rather than folding them into generated types.
  Nothing outside the scheduling context may be touched - accounts, roles, statuses, and training
  plans survive - and every migration keeps a working `Down`.
- **The overlap invariant survives the room removal.** Two occurrences must still not overlap in
  time; only the reason changes from "same room" to "one club, one class at a time".
- **Duplicate-to-weeks keeps its partial-success behaviour.** Conflicting weeks are still skipped
  and reported rather than failing the whole operation.
- **Mobile usability.** The calendar must not become the phone-hostile grid FR-007 ruled out; on a
  phone it is a day picker with a list, and the grid appears only where the width allows.

## Timeline budget

- `delivery_weeks: null` — no hard deadline; after-hours pace.
- `hard_deadline: null`
- `after_hours_only: true`

## Timeline acknowledgment

Acknowledged on 2026-09-02: the scope was surfaced as four independently risky parts (new
aggregate + data migration, room removal and invariant rename, Trainer role, calendar rework on
two screens) and identified as larger than the previous 3-week MVP budget. The user answered the
cost by **staging** rather than by extending a single wave — Stage 1 model, Stage 2 calendar —
with no hard deadline and an after-hours pace.

## Functional Requirements

Numbering restarts at FR-001 for this change. Where a requirement modifies or preserves a
requirement from the existing PRD, the original is named in parentheses.

### Roles

- FR-001: Admin can grant and revoke the Trainer role on an approved account, from the member
  management list. Priority: must-have. Change: new
  > Socrates: "This reverses the PRD Non-Goal 'No Trainer role — User and Admin only', which was
  > locked deliberately." Resolution: accepted reversal — an instructor select cannot list people
  > if no person is modelled, and `Class.Instructor` was free text *because* of that Non-Goal.
  > The Non-Goal is retired, not violated.
- FR-002: An account holding the Trainer role keeps every member capability — booking, upcoming
  classes, an assigned training plan. Roles are additive. Priority: must-have. Change: preserved
  > Socrates: "Should a trainer really be able to book the classes they teach?" Resolution: kept;
  > exclusivity was tried and abandoned because it left a non-admin trainer with an empty
  > application. Additivity is what makes granting the role safe for existing members.
- FR-003: An account may hold Admin and Trainer at once, so an owner who teaches appears in the
  instructor select. Priority: must-have. Change: new
  > Socrates: "Two roles on one account complicates every authorization check." Resolution: kept;
  > ASP.NET Identity already models roles as a set, so this costs nothing new.

### Class type definitions

- FR-004: Admin can create a class type with a name, a description, a default duration, and a
  default capacity. Priority: must-have. Change: new
  > Socrates: "Four fields on a definition when the club runs a handful of classes — is a lookup
  > table enough?" Resolution: kept; defaults are the reason the definition removes retyping, and
  > the description is the member-facing text that has nowhere to live today.
- FR-005: Admin can browse and edit class types. Priority: must-have. Change: new
  > Socrates: "Editing a type rewrites the name on classes that already happened." Resolution:
  > kept knowingly — see FR-007; the user chose reference semantics for the name with that
  > consequence stated.
- FR-006: Admin can deactivate a class type; it disappears from the occurrence select while
  existing occurrences and history stay intact. There is no hard delete. Priority: must-have.
  Change: new
  > Socrates: "Without delete, a typo'd type is permanent clutter." Resolution: kept; deactivation
  > removes it from every place it would be chosen, and orphaned occurrences are a worse failure
  > than a hidden row.
- FR-007: A class type's name and description resolve by reference — editing them changes every
  occurrence, past ones included. Duration and capacity are copied onto the occurrence at
  creation and are never re-read from the type. Priority: must-have. Change: new
  > Socrates: "Mixed semantics are hard to explain — why not make everything consistent?"
  > Resolution: kept; the asymmetry is load-bearing. Capacity by reference would let a type edit
  > change the capacity of a class that already has bookings, hitting the no-overbooking guarantee
  > directly. Name by reference is what makes a typo fixable everywhere at once.

### Occurrences

- FR-008: Admin creates an occurrence by selecting a class type; duration and capacity prefill
  from the definition and remain overridable for that occurrence. Priority: must-have.
  Change: modified (PRD FR-011)
  > Socrates: "If everything is overridable, the definition is only a template and drift returns."
  > Resolution: kept; the name — the field that actually drifted — is *not* overridable (FR-010).
  > Numbers vary legitimately per session.
- FR-009: Admin selects the instructor from a list of active accounts holding the Trainer role;
  the free-text instructor field is gone. Priority: must-have. Change: modified (PRD FR-011)
  > Socrates: "What if a guest instructor without an account runs one class?" Resolution: kept;
  > routed to Open Questions rather than solved by keeping free text alongside the select, which
  > would defeat the change.
- FR-010: An occurrence has no name of its own; its name is the type's. Priority: must-have.
  Change: modified (PRD FR-011)
  > Socrates: "A one-off variation ('Joga — zajęcia otwarte') now needs a whole new type."
  > Resolution: kept; the per-occurrence note option was offered and declined. Creating a type is
  > cheap.
- FR-011: The room field is removed from the occurrence model, the create/edit form, and the
  schedule display. Priority: must-have. Change: modified (PRD FR-011)
  > Socrates: "What if the club adds a second room later?" Resolution: kept; one room is the
  > stated fact today, and re-adding a column is cheaper than carrying a field that never varies.
- FR-012: Two occurrences may not overlap in time anywhere in the club; the create, edit, and
  duplicate paths refuse the conflict. The `room_conflict` failure becomes `time_conflict`.
  Priority: must-have. Change: modified (PRD FR-011)
  > Socrates: "Dropping room could have dropped the rule entirely — is club-wide overlap
  > prevention actually wanted?" Resolution: kept deliberately; the option to drop the rule was
  > offered and declined. The invariant survives with a widened meaning.
- FR-013: Duplicating an occurrence to following weeks still succeeds partially, skipping and
  reporting weeks that conflict. Priority: must-have. Change: preserved (PRD FR-012)
  > Socrates: "Once classes come from types, is manual duplication still the right shape, or does
  > this become recurring series?" Resolution: kept; recurring series is an existing Non-Goal and
  > this change does not reopen it.
- FR-014: Existing behaviour that must survive unchanged: booking a class with free capacity
  under the no-overbooking guarantee, cancelling a booking, cancelling a class with email + push
  to booked members, and class status transitions. Priority: must-have. Change: preserved
  (PRD FR-008, FR-009, FR-013, FR-021)
  > Socrates: "Is a defensive catch-all FR meaningful, or is it decoration?" Resolution: kept; it
  > is the guardrail the migration is measured against — every one of these paths touches the
  > `Class` row being split.

### Calendar

- FR-015: On a phone the schedule shows the current date with a calendar icon to jump to another
  date, a row beneath it with a left arrow, the seven weekday chips, and a right arrow to move
  between weeks; selecting a day lists that day's classes with their times below.
  Priority: must-have. Change: modified (PRD FR-007)
  > Socrates: "PRD FR-007 explicitly ruled out a calendar because grids are phone-hostile."
  > Resolution: kept; the original concern was a grid, and this is a day picker with a list. The
  > grid appears only at FR-016's width. The decision is revisited, not ignored.
- FR-016: On tablet and web the whole week is visible at once. Priority: must-have.
  Change: modified (PRD FR-007)
  > Socrates: "'Probably fits' is not a layout decision — what happens on a busy week?"
  > Resolution: kept, with the density question routed to Open Questions.
- FR-017: The admin class panel uses the same calendar navigation as the member schedule, with
  admin actions on top. Priority: must-have. Change: modified
  > Socrates: "Sharing a component between two screens with different needs invites a widget with
  > a dozen flags." Resolution: kept; the alternative — two calendars drifting apart — is the
  > failure this whole change is about.
- FR-018: Members continue to see class name, date/time, instructor, and free spots; the room is
  gone from the display. Priority: must-have. Change: modified (PRD FR-007)
  > Socrates: "Losing room from the display could confuse existing members." Resolution: kept; a
  > single-room club never used the information.

## User Stories

### US-01: Admin schedules a class from a definition

- **Given** an admin, an active class type "Joga dla początkujących" with a default duration of 60
  minutes and a default capacity of 12, and an active account holding the Trainer role
- **When** they create an occurrence, select that type, select that trainer, and pick a start time
  that does not overlap any other class
- **Then** the occurrence is created carrying a copy of the duration and capacity, showing the
  type's name, with no room recorded, and it appears on the schedule

### US-02: Member navigates the week on a phone

- **Given** a logged-in member on a phone, with classes scheduled across the week
- **When** they open the schedule, move to the next week with the right arrow, and tap a weekday
- **Then** that day's classes are listed beneath the strip with their times, and booking from the
  list works exactly as before

## Business Logic

**The change modifies an existing rule and adds one.**

The system currently decides who gets in and who gets a spot: an account must pass admin approval
before it can act, a booking is admitted only while the class has free capacity, and a class may
not overlap another in the same room.

This change modifies the last of those and adds a definition layer beneath it:

- **A class is no longer described, it is instantiated.** A class type is defined once and every
  occurrence is built from it. The type owns identity (name, description) and supplies defaults
  (duration, capacity); the occurrence owns its moment in time, its instructor, and its own copy
  of the numbers.
- **The binding is deliberately asymmetric.** Identity resolves by reference, so correcting a
  name corrects it everywhere. The numbers are copies, so editing a type can never change the
  capacity of a class that already has bookings — the no-overbooking guarantee is checked against
  a value nothing upstream can move.
- **The overlap rule widens.** "One room, one class at a time" becomes "one club, one class at a
  time". The room was never information in a single-room gym; the time conflict was the real rule
  all along, and removing the room makes that explicit rather than removing the protection.
- **The instructor becomes a person, not a string.** Only an active account holding the Trainer
  role can be assigned to an occurrence, so the schedule refers to someone the system knows.

Supporting detail:

- **Inputs the rules consume:** a class-type definition; an occurrence request naming a type, a
  start time, an instructor, and possibly overridden numbers; the set of already-scheduled
  occurrences, for the time-conflict check.
- **Outputs:** an occurrence carrying a resolved name and copied capacity; a refusal when the
  requested time collides with an existing class; an instructor list restricted to active trainers.
- **Where the user meets the rules:** the admin fills a form of selects instead of text fields and
  is refused on a colliding time; the member sees a name that is consistent across every week and
  a free-spot count that no type edit can silently change.

## Constraints & Preserved Behavior

**Preserved unchanged:**

- The no-overbooking guarantee, and the read-time free-spot projection it relies on
  (`IClassScheduleQuery`) — no denormalized counter is introduced by this change.
- Account lifecycle (pending / active / blocked), admin seeding, and the approval gate.
- Training plans, the exercise library, and the notification model (email + push, no in-app
  centre) — untouched by this change.
- Duplicate-to-weeks partial success, including the `SkippedWeeks` report.
- Class cancellation notifying every booked member.

**Data migration — existing scheduling data is discarded.** The database holds development data
only; no real club is using the application yet. The migration therefore clears classes and
bookings rather than folding free-text names into generated types.

- The wipe is **narrow**: classes and bookings only. Accounts, roles, statuses, and training plans
  survive. Nothing outside the scheduling context is touched.
- This is the reason the "no booking is lost" guardrail was rewritten in Phase 3 — it protected
  data that does not exist. What is protected instead is reversibility and everything outside
  scheduling.
- Had real data existed, the chosen path would have been automatic type generation (one distinct
  name becomes one type, instructor left empty for the admin to fill).

**Room removal lags one release, per the repository's migration rule.** The `Room` column stays in
the schema for one release after the code stops reading it, and is dropped in a later migration.
This keeps rollback safe: redeploying the previous artifact finds the column it expects, since
rollback restores code but not schema.

**Every migration keeps a working `Down`.**

**Contracts that change and must be updated in step:** the class create/edit/duplicate request and
response shapes (room removed, class type id added, instructor becomes an account reference), and
the failure code `room_conflict` → `time_conflict`. The Angular client is the only consumer, so
the change is coordinated rather than versioned.

## Non-Functional Requirements

Committed for this change:

- **No response-time regression.** Browsing the schedule and booking a class stay within the
  ~1 s user-perceived response the PRD already commits to, despite the occurrence now resolving
  its name through a class type.
- **The empty schedule reads as empty on purpose.** A day or week with no classes shows a clear
  message rather than blank space — load-bearing immediately after the data wipe, when the
  schedule starts empty.

Carried over from the existing PRD and not renegotiated here: mobile-first and installable (PWA),
notification promptness, personal data privacy.

Considered and deliberately not committed: a latency target for week-to-week navigation, and a
one-handed reachability commitment for the calendar controls. Both were offered and declined —
they are design intentions, not measurable promises this change is held to.

## Non-Goals

Newly locked by this change:

- **No trainer screen.** The Trainer role gets no view and no permissions here; it exists to
  populate the instructor select. A "my classes" view is a separate, later change.
- **No multiple rooms.** The room disappears for good; no rooms lookup table is introduced "just
  in case". Returning to multiple rooms would be a deliberate future decision, not something this
  change prepares for.
- **No month view.** The calendar works in days and weeks. Month view, agenda view, and export to
  an external calendar are out of scope.

Carried over from the existing PRD and unaffected by this change: no recurring series (weekly
duplication stands in for it), no payments, no waitlist, no chat, no native apps, no self-hosted
video, no attendance tracking, no multi-tenancy, no plan history, no standalone exercise library
browsing, no in-app notification centre.

Retired by this change: **"No Trainer role — User and Admin only"** is no longer a Non-Goal. It
was locked when instructors were free text; a select over real people requires the role.

## Product framing

- `product_type: web-app` — no change; the mobile-first PWA is unchanged in kind.
- `target_scale.users: medium` — no change; still one club, dozens to a hundred people.
- `timeline_budget.delivery_weeks: null`, `hard_deadline: null`, `after_hours_only: true` — no
  hard deadline; the scope is answered by two-stage delivery rather than a fixed duration.

## Open Questions

1. **A guest instructor without an account runs one class — what then?** Surfaced by FR-009's
   Socratic challenge. Keeping free text alongside the select would defeat the change, so the
   answer is probably a lightweight trainer account or an optional instructor; undecided.
2. **How dense can the full-week view get before it stops working?** FR-016 rests on "the whole
   week probably fits". A busy week on a narrow tablet is the case to check during design.
3. **What does a trainer eventually see?** Deliberately deferred, not overlooked — the additive
   role model keeps the path open, but the scope of a future trainer view is unspecified.

Carried over from the previous PRD and still unanswered:

4. **What happens to a blocked member's existing bookings and assigned plan?**
5. **Who enters the initial exercise library content, and when?**

## Quality cross-check

Ran 2026-09-02 against the brownfield bar (7 elements).

| Element | Result |
| --- | --- |
| Access Control | present — Trainer role added, additivity and granting path pinned |
| Business Logic | present — "a class is no longer described, it is instantiated", with the asymmetric type/occurrence binding as supporting rules |
| Project artifacts | present |
| Timeline-cost acknowledgment | present — four-part scope surfaced, answered by two-stage delivery |
| Non-Goals | present — 3 newly locked, 1 explicitly retired |
| Preserved behavior | present — `## Constraints & Preserved Behavior` names the wipe boundary, the invariants that survive, and the contracts that change |

Status: **accepted** — no gaps.

Two things a reader should carry into the PRD, neither of them a gap:

1. **This change retires a locked PRD Non-Goal.** "No Trainer role — User and Admin only" is
   deliberately reversed. The brownfield PRD must state the retirement rather than silently
   contradict its predecessor.
2. **One Phase 3 guardrail was rewritten mid-session.** "No booking is lost" was replaced once it
   emerged the database holds development data only. The replacement protects reversibility and
   everything outside the scheduling context instead. The original was not dropped for
   convenience — it protected data that does not exist.
