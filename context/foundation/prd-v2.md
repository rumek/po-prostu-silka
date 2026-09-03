---
project: "Po Prostu Siłka"
version: 2
status: draft
created: 2026-09-02
context_type: brownfield
product_type: web-app
target_scale:
  users: medium
  qps: low
  data_volume: small
timeline_budget:
  delivery_weeks: null
  hard_deadline: null
  after_hours_only: true
---

## Current System Overview

**Purpose.** Po Prostu Siłka is a single gym's class-booking and training-plans application,
replacing the spreadsheet the club used to run sign-ups, schedule changes, and individual plans.

**Architecture.** One deployable web API with an SPA client. The API is organised in DDD layers as
folders in a single project — Domain (references nothing), Application (references Domain),
Infrastructure (references both, and is the only layer permitted to touch the ORM). Bounded
contexts (membership, scheduling, training, notifications) are subfolders within those layers.

**Tech stack.** ASP.NET Core Web API on .NET 10 (C#), Angular 22 SPA with SSR as a sibling
project in the same repository, EF Core over SQL Server (SQL Server in Docker locally, Azure SQL
in production), ASP.NET Identity for accounts and roles.

**User base.** One club — dozens to a hundred people. Two roles today: `User` (member) and
`Admin` (club owner). Accounts are self-registered and gated by admin approval, with a
pending / active / blocked lifecycle. Admin accounts are seeded at setup.

**Core functionality today.** The first milestone shipped the class schedule and admin surface.
A class is a single flat entity carrying its name, start time, duration, room, instructor,
capacity, and status. Members browse a day-grouped schedule, book and cancel spots under a
no-overbooking guarantee, and receive email and push notifications when a booked class is
cancelled or changed. Admins create, edit, cancel, and duplicate classes to following weeks, and
manage members. Free spots are projected at read time rather than kept as a denormalized counter.
An overlap rule refuses two classes in the same room at the same time — surfaced as a distinct
conflict failure on create and edit, and as a skipped-and-reported week during duplication.
Training plans and the exercise library are defined in the product PRD but sit outside the shipped
scheduling slice.

## Problem Statement & Motivation

**The gap.** A class has no definition. The admin retypes its identity — name, room, instructor —
every time they put an occurrence on the schedule. Nothing binds this Monday's
"Joga dla początkujących" to next Monday's; they are unrelated rows that happen to share a
spelling. A typo silently forks one class into two in the member's eyes, and there is no place for
a description telling a member what a class actually is before they book it.

Two of those three retyped fields carry no information at all. The club has **one room**, so the
room field never varies. The instructor is free text precisely because the product deliberately
shipped without a trainer role — so the schedule names people the system does not know, and cannot
link a class to the person who runs it.

**The current workaround and its cost.** Weekly duplication hides the retyping rather than
removing it: the admin copies a class forward and the drift is deferred, not prevented. Every
correction has to be made occurrence by occurrence.

**Why now.** The scheduling slice has just shipped and the data is still development-only, so the
model can be restructured before real bookings exist to migrate. The same window makes the second
half of this change cheap: the schedule currently renders as a day-grouped list, and moving it to
a calendar — one day at a time on a phone, a full week on wider screens — is a
rewrite that gets more expensive once members have habits built on the current view.

## User & Persona

**Club owner / admin — primary beneficiary, experience changes most.** Stops retyping class
identity. Defines each class once as a type, then builds occurrences by selecting the type and the
instructor from dropdowns. Also receives the new calendar navigation in the admin panel.

**Gym member — same tasks, new lens.** Books and cancels exactly as before. Sees the schedule as a
calendar instead of a day-grouped list: on a phone, the current date with a control to jump
elsewhere, controls to move by day and by week, and that day's classes; on tablet and
web, the whole week at once. Also sees a consistent class name across weeks, and a class
description that has nowhere to live today.

### New persona

**Trainer.** A person who runs classes and now holds a real account carrying a `Trainer` role,
granted by the admin. In this change the role confers no additional capability — it exists so the
instructor can be a person the system knows rather than a typed string. What a trainer eventually
sees after signing in is deliberately unresolved (see Open Questions).

## Success Criteria

Proof flow for this change — what a user does differently once it ships:

1. Admin defines a class type once: name, description, default duration, default capacity
2. Admin grants the Trainer role to an existing approved account
3. Admin creates an occurrence by selecting that type — duration and capacity prefill from the
   definition and stay overridable — and selecting the instructor from the trainer list; there is
   no room field
4. Member opens the schedule on a phone: the day view for today, moves by day and by week, sees
   that day's classes with times
5. Member books a spot; the no-overbooking guarantee holds exactly as before
6. The same calendar serves the admin panel; on tablet and web the whole week is visible at once

Delivery is staged: steps 1–3 are Stage 1 (model and admin form), steps 4–6 are Stage 2
(calendar). Each stage leaves the application working end to end. Stage 1 ships against the
existing day-grouped list; Stage 2 depends on nothing Stage 1 defers.

### Primary
- The proof flow works end to end: a class type is defined once and reused across occurrences, the
  instructor is a real account rather than retyped text, and the schedule is navigable as a
  calendar on a phone and as a full week on a larger screen.

### Secondary
- The admin lays out a week of classes without typing a single name by hand — every identity field
  comes from a selection. Measurable proof the definition layer did its job.

### Guardrails
- **No overbooking, unchanged.** Capacity is carried on the occurrence as a copied value, never
  resolved through the type — editing a type must not alter the capacity of a class that already
  has bookings, nor the value the guarantee is checked against.
- **The wipe stays narrow, and reversibility holds.** Existing scheduling data is development-only
  and is discarded rather than migrated. Nothing outside the scheduling context may be touched —
  accounts, roles, statuses, and training plans survive — and every schema change stays reversible.
- **The overlap protection survives the room's removal.** Two occurrences must still not overlap
  in time; only the reason changes, from "same room" to "one club, one class at a time".
- **Duplicate-to-following-weeks keeps its partial-success behaviour.** Conflicting weeks are
  still skipped and reported rather than failing the whole operation.
- **Mobile usability.** The calendar must not become the phone-hostile grid the product PRD ruled
  out. On a phone it shows one day at full width; the seven-column week appears only from 48rem up.
  Amended 2026-09-02 (`schedule-calendar-view`): this originally read "on a phone it is a day picker
  with a list". The mechanism changed with FR-015 — a day view rather than a strip plus list — and the
  guardrail itself did not: a phone never renders the week grid.
- **No response-time regression.** Browsing the schedule and booking a class stay within the
  ~1 s user-perceived response the product already commits to, despite the occurrence now
  resolving its name through a type.
- **The empty schedule reads as empty on purpose.** A day or week with no classes shows a clear
  message rather than blank space — load-bearing immediately after the wipe, when the schedule
  starts empty.

## User Stories

### US-01: Admin schedules a class from a definition

- **Given** an admin, an active class type "Joga dla początkujących" with a default duration of 60
  minutes and a default capacity of 12, and an active account holding the Trainer role
- **When** they create an occurrence, select that type, select that trainer, and pick a start time
  that does not overlap any other class
- **Then** the occurrence is created carrying a copy of the duration and capacity, showing the
  type's name, with no room recorded, and it appears on the schedule

**Before this change:** the admin typed the name, the room, and the instructor by hand for every
occurrence, and nothing connected it to the same class in another week.

#### Acceptance Criteria
- Duration and capacity prefill from the type and remain editable for that occurrence
- The occurrence carries no name of its own; the name resolves from the type
- A start time colliding with any existing class is refused, whatever the class
- The form offers no room field
- The instructor list offers only active accounts holding the Trainer role

### US-02: Member navigates the week on a phone

- **Given** a logged-in member on a phone, with classes scheduled across the week
- **When** they open the schedule, move to the next week with the forward control, and land on a
  weekday
- **Then** that day's classes are shown with their times, and booking from them works exactly as
  before

**Before this change:** the schedule was a single day-grouped list with no week navigation and no
way to jump to a chosen date.

#### Acceptance Criteria
- The current date is shown, with a control to jump to another date
- Controls move the view by one day and by one week, in both directions
- The selected day shows its classes with their start times
- A day with no classes shows an explanatory empty state, not blank space
- From 48rem up the whole week is visible without day-by-day selection

> Amended 2026-09-02 (`schedule-calendar-view`): the story and its criteria described a week strip of
> weekday chips with a list beneath. The user journey is unchanged — open, move between weeks, read a
> day — but the phone renders that day as a day view rather than a chip strip plus list. See FR-015.

## Scope of Change

Socratic challenges recorded during shaping are preserved verbatim beneath the items they belong
to. Item identifiers (FR-NNN) are this change's own numbering; where an item modifies or preserves
a requirement from the product PRD, that original is named in parentheses.

### Roles

- **[new]** FR-001: Admin can grant and revoke the Trainer role on an approved account, from the
  member management list. Priority: must-have
  > Socratic: "This reverses the product PRD's Non-Goal 'No Trainer role — User and Admin only',
  > which was locked deliberately." Resolution: accepted reversal — an instructor selection cannot
  > list people if no person is modelled, and the instructor was free text *because* of that
  > Non-Goal. The Non-Goal is retired, not violated.
- **[preserved]** FR-002: An account holding the Trainer role keeps every member capability —
  booking, upcoming classes, an assigned training plan. Roles are additive. Priority: must-have
  > Socratic: "Should a trainer really be able to book the classes they teach?" Resolution: kept;
  > exclusivity was tried and abandoned because it left a non-admin trainer with an empty
  > application. Additivity is what makes granting the role safe for existing members.
- **[new]** FR-003: An account may hold Admin and Trainer at once, so an owner who teaches appears
  in the instructor selection. Priority: must-have
  > Socratic: "Two roles on one account complicates every authorization check." Resolution: kept;
  > roles are already modelled as a set, so this costs nothing new.

### Class type definitions

- **[new]** FR-004: Admin can create a class type with a name, a description, a default duration,
  and a default capacity. Priority: must-have
  > Socratic: "Four fields on a definition when the club runs a handful of classes — is a simple
  > lookup enough?" Resolution: kept; defaults are the reason the definition removes retyping, and
  > the description is the member-facing text that has nowhere to live today.
- **[new]** FR-005: Admin can browse and edit class types. Priority: must-have
  > Socratic: "Editing a type rewrites the name on classes that already happened." Resolution:
  > kept knowingly — see FR-007; reference semantics for the name were chosen with that
  > consequence stated.
- **[new]** FR-006: Admin can deactivate a class type; it disappears from the occurrence selection
  while existing occurrences and history stay intact. There is no hard delete.
  Priority: must-have
  > Socratic: "Without deletion, a mistyped type is permanent clutter." Resolution: kept;
  > deactivation removes it from every place it would be chosen, and orphaned occurrences are a
  > worse failure than a hidden entry.
- **[new]** FR-007: A class type's name and description resolve by reference — editing them
  changes every occurrence, past ones included. Duration and capacity are copied onto the
  occurrence at creation and are never re-read from the type. Priority: must-have
  > Socratic: "Mixed semantics are hard to explain — why not make everything consistent?"
  > Resolution: kept; the asymmetry is load-bearing. Capacity by reference would let a type edit
  > change the capacity of a class that already has bookings, hitting the no-overbooking guarantee
  > directly. Name by reference is what makes a correction apply everywhere at once.

### Occurrences

- **[modified]** FR-008: Admin creates an occurrence by selecting a class type; duration and
  capacity prefill from the definition and remain overridable for that occurrence.
  Priority: must-have — was: every field typed by hand with no definition behind it
  (product PRD FR-011)
  > Socratic: "If everything is overridable, the definition is only a template and drift returns."
  > Resolution: kept; the name — the field that actually drifted — is *not* overridable (FR-010).
  > Numbers vary legitimately per session.
- **[modified]** FR-009: Admin selects the instructor from a list of active accounts holding the
  Trainer role. Priority: must-have — was: instructor typed as free text
  (product PRD FR-011)
  > Socratic: "What if a guest instructor without an account runs one class?" Resolution: kept;
  > routed to Open Questions rather than solved by keeping free text alongside the selection,
  > which would defeat the change.
- **[modified]** FR-010: An occurrence has no name of its own; its name is the type's.
  Priority: must-have — was: each occurrence carried its own typed name (product PRD FR-011)
  > Socratic: "A one-off variation ('Joga — zajęcia otwarte') now needs a whole new type."
  > Resolution: kept; a per-occurrence note was offered and declined. Creating a type is cheap.
- **[removed]** FR-011: The room disappears from the occurrence, from the create and edit forms,
  and from the schedule display. Rationale: the club has one room, so the field never carried
  information. (product PRD FR-011)
  > Socratic: "What if the club adds a second room later?" Resolution: kept; one room is the
  > stated fact today, and reintroducing the concept later is cheaper than carrying a field that
  > never varies.
- **[modified]** FR-012: Two occurrences may not overlap in time anywhere in the club; the create,
  edit, and duplicate paths refuse the conflict. Priority: must-have — was: the same refusal,
  scoped to a shared room (product PRD FR-011)
  > Socratic: "Dropping the room could have dropped the rule entirely — is club-wide overlap
  > prevention actually wanted?" Resolution: kept deliberately; dropping the rule was offered and
  > declined. The invariant survives with a widened meaning.
- **[preserved]** FR-013: Duplicating an occurrence to following weeks still succeeds partially,
  skipping and reporting weeks that conflict. Priority: must-have (product PRD FR-012)
  > Socratic: "Once classes come from types, is manual duplication still the right shape, or does
  > this become recurring series?" Resolution: kept; recurring series is an existing Non-Goal and
  > this change does not reopen it.
- **[preserved]** FR-014: Booking a class with free capacity under the no-overbooking guarantee,
  cancelling a booking, cancelling a class with email and push to booked members, and class status
  transitions all survive unchanged. Priority: must-have
  (product PRD FR-008, FR-009, FR-013, FR-021)
  > Socratic: "Is a defensive catch-all requirement meaningful, or is it decoration?" Resolution:
  > kept; it is what the restructuring is measured against — every one of these paths touches the
  > class being split into type and occurrence.

### Calendar

- **[modified]** FR-015: On a phone the schedule shows a single day at a time — the current date
  above it, controls to move by day and by week, and a control to jump to a chosen date. That day's
  classes appear with their times. Priority: must-have — was: a single day-grouped list with no week
  navigation (product PRD FR-007)
  > Socratic: "The product PRD explicitly ruled out a calendar because grids are phone-hostile."
  > Resolution: kept; the original concern was a *week* grid — seven columns squeezed onto a phone —
  > and one day at full width is not that. The week appears only at FR-016's width. The decision is
  > revisited, not ignored.
  > Amended 2026-09-02 (`schedule-calendar-view`): this requirement originally specified a day-picker
  > strip — a row of seven weekday chips between arrows, with the selected day's classes listed
  > beneath. It now specifies a day view. The change follows the decision to build both breakpoints on
  > the `angular-calendar` library rather than by hand: the library renders a day and a week view, and
  > has no day-strip. The mobile guardrail below was rewritten in the same edit and still holds — a
  > phone never renders the seven-column week.
  > Amended 2026-09-03 (`schedule-calendar-view`): the weekday strip is back, as NAVIGATION rather than
  > as layout — a row of seven weekday buttons between week arrows, sitting under the date control,
  > with the day it selects rendered in the day view below. What the earlier amendment dropped was the
  > strip *plus list*; what it took with it was the ability to reach Friday in one press instead of
  > four. The strip appears only below FR-016's width, where the week is not already on screen, and it
  > REPLACES the day-by-day controls this requirement originally listed rather than joining them — a
  > view with two navigations has neither read. Returning to today from a distant week is the date
  > control's job there.
- **[modified]** FR-016: From the tablet width up (48rem), the whole week is visible at once, as a
  seven-column view; below that width the day view of FR-015 applies. The switch is automatic and
  follows the viewport. Priority: must-have — was: the same day-grouped list at every width
  (product PRD FR-007)
  > Socratic: "'Probably fits' is not a layout decision — what happens on a busy week?"
  > Resolution: kept, with the density question routed to Open Questions.
  > Amended 2026-09-02 (`schedule-calendar-view`): the breakpoint is now named rather than left to
  > "tablet and web", and the density question is answered by the library's own week layout rather
  > than by a hand-built one.
- **[modified]** FR-017: The admin class panel uses the same calendar navigation as the member
  schedule, with admin actions on top. Priority: must-have — was: a separate flat list in the
  admin panel
  > Socratic: "Sharing one component between two screens with different needs invites a widget
  > with a dozen flags." Resolution: kept; the alternative — two calendars drifting apart — is the
  > failure this whole change is about.
- **[modified]** FR-018: Members continue to see class name, date and time, instructor, and free
  spots; the room is gone from the display. Priority: must-have (product PRD FR-007)
  > Socratic: "Losing the room from the display could confuse existing members." Resolution:
  > kept; a single-room club never used the information.
- **[new]** FR-019: On the admin calendar, dragging across empty time creates a class: the gesture
  fixes the start time and the duration, and an overlay collects the class type and the trainer
  before anything is written. The same refusals apply as on the class form, the time conflict of
  FR-012 included. A week already in the past accepts no gesture. Priority: must-have
  > Added 2026-09-02 (`schedule-calendar-view`). This requirement did not come from the shaping
  > session — it exceeds the calendar scope FR-015 – FR-018 describe, which is browsing only, and was
  > added by explicit decision during planning as the main return on adopting a calendar library.
  > It does not replace the class form: FR-008's form remains the full path, and the gesture is a
  > shortcut into the same validated write.
- **[new]** FR-020: On the admin calendar, an existing class can be dragged to another time and
  resized from either edge to change when it starts and how long it runs. Both gestures snap to the
  calendar's half-hour grid and write through the same update path as FR-008's form, so every refusal
  that form can receive applies here too — the FR-012 time conflict included. Nothing else about the
  class is editable by gesture: the type, the trainer and the capacity are not things a pointer can
  express, and stay the form's job. A class that has already started accepts no gesture, and a week
  already in the past accepts none at all. Priority: must-have
  > Added 2026-09-03 (`schedule-calendar-view`), by explicit decision after manual verification, on
  > the same reasoning as FR-019: direct manipulation is the return on adopting a calendar library,
  > and an admin who can draw a class on the grid but must open a form to move it by half an hour is
  > being told the calendar is a picture rather than the thing itself.

## Constraints & Compatibility

**Existing scheduling data is discarded, not migrated.** The database holds development data only;
no real club is using the application yet. Classes and bookings are cleared rather than folded
into generated types.

- The clearing is **narrow**: classes and bookings only. Accounts, roles, statuses, and training
  plans survive. Nothing outside the scheduling context is touched.
- This is why the shaping session's original "no booking is lost" guardrail was rewritten — it
  protected data that does not exist. What is protected instead is reversibility and everything
  outside scheduling.
- Had real data existed, the chosen path would have been automatic type generation: one distinct
  name becomes one type, with the instructor left empty for the admin to fill.

**The room's removal from storage lags one release.** The repository's rule is that rollback
redeploys the previous build but does not roll back the database, so destructive schema changes
trail the code that stopped needing them by one release. The room column therefore stays in place
after the application stops reading it and is dropped later. This keeps rollback safe: the
previous build finds the column it expects.

**Every schema change stays reversible** — a working reverse step is required, per the repository
rule.

**Contracts that change and must move in step.** The class create, edit, and duplicate request and
response shapes change: the room is removed, the class type is referenced, and the instructor
becomes an account reference rather than a string. The conflict-refusal reason changes from a
room collision to a time collision. The product ships its own client as the only consumer of these contracts, so
the change is coordinated across both sides rather than versioned for external callers.

**Preserved integrations and behaviour.** Email and push notification delivery on class
cancellation and change; the read-time free-spot projection, with no denormalized counter
introduced by this change; the account lifecycle and approval gate; training plans and the
exercise library, which this change does not touch.

## Business Logic Changes

**A class is no longer described, it is instantiated.**

The system currently decides who gets in and who gets a spot: an account must pass admin approval
before it can act, a booking is admitted only while the class has free capacity, and a class may
not overlap another in the same room. This change modifies the last of those and adds a definition
layer beneath it.

- **New rule — definition and instance.** A class type is defined once and every occurrence is
  built from it. The type owns identity (name, description) and supplies defaults (duration,
  capacity); the occurrence owns its moment in time, its instructor, and its own copy of the
  numbers.
- **New rule — the binding is deliberately asymmetric.** Identity resolves by reference, so
  correcting a name corrects it everywhere, past occurrences included. The numbers are copies, so
  editing a type can never change the capacity of a class that already has bookings — the
  no-overbooking guarantee is checked against a value nothing upstream can move.
- **Modified rule — the overlap rule widens.** "One room, one class at a time" becomes "one club,
  one class at a time". The room was never information in a single-room gym; the time conflict was
  the real rule all along, and removing the room makes that explicit rather than removing the
  protection.
- **Modified rule — the instructor becomes a person, not a string.** Only an active account
  holding the Trainer role can be assigned to an occurrence, so the schedule refers to someone the
  system knows.

**Inputs the rules consume:** a class-type definition; an occurrence request naming a type, a
start time, an instructor, and possibly overridden numbers; the set of already-scheduled
occurrences, for the time-conflict check.

**Outputs:** an occurrence carrying a resolved name and a copied capacity; a refusal when the
requested time collides with an existing class; an instructor list restricted to active trainers.

**Where the user meets the rules:** the admin fills a form of selections instead of text fields
and is refused on a colliding time; the member sees a name that is consistent across every week
and a free-spot count that no type edit can silently change.

## Access Control Changes

**Preserved unchanged:** email and password sign-in; self-registration gated by admin approval;
the pending → active lifecycle with block and unblock; admin accounts seeded at setup and never
self-registered; a pending user able to sign in but seeing only the awaiting-approval screen.

**What changes — a third role is added.**

- **`Trainer` joins the role set** and is seeded alongside `User` and `Admin`. This retires the
  product PRD's Non-Goal "No Trainer role — User and Admin only". The reversal is deliberate: an
  instructor selection cannot list people if no person is modelled.
- **Roles are additive, not exclusive.** An account holds a *set* of roles. Granting `Trainer`
  takes nothing away — a trainer still books classes and still has a training plan. `Admin` plus
  `Trainer` is a valid combination, so an owner who teaches appears in the same selection as
  everyone else.
- **Granting the role** is an admin action on an existing approved account, reached from the
  member-management list that already carries status badges and filters. There is no separate
  trainer registration path and no admin-created trainer account.
- **Trainer capability in this change: none beyond `User`.** The role is a label that populates the
  instructor selection. No trainer-only screen and no new authorization rule. Whether trainers
  eventually see their own classes is a later change; the additive role model keeps that path open.
- **Class type definitions are Admin-only**, consistent with every other write in the scheduling
  context.

Exclusivity was tried during shaping and abandoned: making `Trainer` exclude `User` would have
left a trainer without the Admin role signing in to an application with no functionality at all —
the role removed member features while this change adds no trainer features to replace them.

## Non-Goals

**Newly locked by this change:**

- **No trainer screen.** The Trainer role gets no view and no permissions here; it exists to
  populate the instructor selection. A "my classes" view is a separate, later change.
- **No multiple rooms.** The room disappears for good; no rooms lookup is introduced "just in
  case". Returning to multiple rooms would be a deliberate future decision, not something this
  change prepares for.
- **No month view.** The calendar works in days and weeks. Month view, agenda view, and export to
  an external calendar are out of scope. Amended 2026-09-02 (`schedule-calendar-view`): the library
  chosen to implement FR-015 and FR-016 ships a month view. No route, control or view mode reaches
  it, so the Non-Goal stands as written — it is unreachable rather than unavailable.

**Carried over from the product PRD and unaffected by this change:** no recurring series (weekly
duplication stands in for it), no payments or memberships, no waitlist for full classes, no chat
or social features, no native mobile apps, no self-hosted video, no attendance or check-in
tracking, no multi-tenancy, no plan history, no standalone exercise library browsing, no in-app
notification centre, no cancellation deadline.

**Non-functional non-goals.** Two qualities were offered during shaping and deliberately not
committed: a latency target for week-to-week navigation, and a one-handed reachability commitment
for the calendar controls. Both are design intentions, not measurable promises this change is held
to. The existing no-offline-first non-goal is unchanged.

**Retired by this change:** "No Trainer role — User and Admin only" is no longer a Non-Goal. It
was locked when instructors were free text; a selection over real people requires the role.

## Open Questions

1. **A guest instructor without an account runs one class — what then?** — Owner: user. Surfaced
   by FR-009's Socratic challenge. Keeping free text alongside the selection would defeat the
   change, so the answer is probably a lightweight trainer account or an optional instructor.
   Block: no (FR-009 ships without it; the case is currently unsupported).
2. **How dense can the full-week view get before it stops working?** — Owner: user. FR-016 rests
   on "the whole week probably fits". A busy week on a narrow tablet is the case to check during
   design. Block: no (Stage 2 design decision).
3. **What does a trainer eventually see after signing in?** — Owner: user. Deliberately deferred,
   not overlooked; the additive role model keeps the path open but the scope of a future trainer
   view is unspecified. Block: no (explicitly a Non-Goal for this change).
4. **What is the delivery budget in weeks?** — Owner: user. The shaping session recorded no hard
   deadline and an after-hours pace, answering the scope cost by two-stage delivery rather than a
   week count, so `timeline_budget.delivery_weeks` is null where the schema expects an integer.
   Block: no (staging is the commitment; the number is informational).
5. **What happens to a blocked member's existing bookings and assigned plan?** — Owner: user.
   Carried over unresolved from the product PRD. Block: no (untouched by this change, but it
   intersects the roles work).
6. **Who enters the initial exercise library content, and when?** — Owner: user. Carried over
   unresolved from the product PRD. Block: no (outside this change's scope).
