---
project: "Po Prostu Siłka"
context_type: greenfield
created: 2026-08-30
updated: 2026-08-30
checkpoint:
  current_phase: 8
  phases_completed: [1, 2, 3, 4, 5, 6, 7]
  gray_areas_resolved:
    - topic: "primary persona"
      decision: "two co-primary personas — club owner/admin and gym member; user accepted the doubled-MVP-surface cost"
    - topic: "pain moment"
      decision: "no single trigger dominates — sign-up chaos, schedule changes, and plan delivery all hurt"
    - topic: "build vs buy insight"
      decision: "one app combining class bookings + training plans + exercise library; existing SaaS does one or the other"
    - topic: "auth method"
      decision: "email + password; no social, no passwordless"
    - topic: "approval gate"
      decision: "kept — open registration vetted by admin approval (Socrates check passed)"
    - topic: "pending-account experience"
      decision: "pending user can log in but sees only an awaiting-approval screen"
    - topic: "admin provisioning"
      decision: "admin accounts seeded at setup; never self-registered"
    - topic: "notification channels (MVP)"
      decision: "REVISED in Socratic round: email + push delivery, no in-app notification center; cost re-surfaced, user kept 3-week estimate"
    - topic: "account rejection"
      decision: "dropped from MVP; statuses are pending/active/blocked"
    - topic: "plan assignment semantics"
      decision: "one active plan per member; new assignment replaces the old; no plan-history UI"
    - topic: "exercise library access"
      decision: "detail pages reached from the training plan only; standalone browsing cut from MVP"
    - topic: "cancellation deadline"
      decision: "none — free cancel anytime; deadline rule considered and declined"
  frs_drafted: 23
  quality_check_status: accepted
---

# Shape Notes

Seed: `gym_management_app_overview.md` — web app (PWA, mobile-first) for a gym / fitness club combining user account management (registration → approval → active), a group-class schedule with bookings/cancellations, individual training plans, an exercise library with instructions and YouTube videos, and notifications. Roles: User + Admin (Trainer role out of MVP).

## Vision & Problem Statement

A small gym / fitness club runs class sign-ups, schedule changes, and individual training plans through Excel. The whole workflow hurts: booking chaos before popular classes, no reliable way to notify members about cancellations or changes, and training plans handed out as files or printouts. The cost the owner feels most is lost professional image — the club looks worse than competitors that have booking apps.

The insight: existing gym SaaS does bookings *or* training plans, not both. This app combines the group-class schedule with bookings, individual training plans, and an exercise library (with instructions and videos) in one simple, mobile-first place.

## User & Persona

Two co-primary personas (deliberate choice — user accepted that this doubles the MVP surface):

- **Club owner / admin** — operates the club day-to-day: approves registrations, manages the class schedule, builds and assigns training plans. Reaches for the app whenever they'd otherwise open Excel.
- **Gym member** — checks the schedule on their phone, books/cancels classes, follows their individual training plan using the exercise instructions and videos. Reaches for the app before visiting the club and during a workout.

## Access Control

Email + password login. Self-registration is open, but every new account is gated by admin approval before it becomes active.

- **Roles:** `User` (uses the app) and `Admin` (manages users, schedule, training plans, exercises). Flat two-role model; no Trainer role in MVP (per seed).
- **Account lifecycle:** register → pending (awaiting approval) → active. Admin may **block / unblock** accounts. (Reject was dropped in the Socratic round — block covers bad actors; statuses are pending / active / blocked.)
- **Pending state:** a pending user can log in but sees only an "awaiting approval" screen — no schedule, no booking.
- **Admin origin:** admin accounts are seeded at setup; admins are never self-registered.
- **Unauthenticated access:** login/registration only; all app content requires an active account.

## Success Criteria

MVP proof flow (the smallest end-to-end flow that proves the product works):

1. Member registers → admin approves the account
2. Member logs in and sees the class schedule (name, date/time, room, instructor, free spots)
3. Member books a spot in a class
4. Member can cancel their booking
5. Admin cancels a class → every booked member receives a notification (email + push)
6. Member learns about the cancellation without needing to open the app

Scope decision (revised in the Socratic round): MVP notifications are delivered by **email + push**; there is **no in-app notification center**. The original Phase 3 scope-down to in-app-only was reversed by the user with the integration cost (email service + PWA push) re-surfaced and accepted; the 3-week estimate was kept at the user's call.

### Primary
- The proof flow above works end-to-end: a real member can discover, book, and cancel a class in the app, and reliably learns via email/push when a booked class is cancelled.

### Secondary
- The club owner stops maintaining the Excel sheet — the app fully replaces the old workflow.

### Guardrails
- No overbooking: a class never accepts more bookings than it has spots; the capacity count is always trustworthy.
- Mobile usability: every member-facing screen stays comfortably usable on a phone.
- No missed cancellations: every booked member reliably receives the email/push notification when their class is cancelled or changed.

## Timeline budget

- `mvp_weeks: 3` (user's own estimate; after-hours work)

## Timeline acknowledgment

Acknowledged on 2026-08-30: email + push notification integrations were reinstated into the MVP during the Socratic round, reversing the earlier in-app-only scope-down; the integration cost was surfaced twice and the user kept the 3-week estimate at their own call.

## Functional Requirements

### Accounts & access
- FR-001: Member can register an account (email + password). Priority: must-have
  > Socrates: "Open self-registration invites strangers and junk accounts." Resolution: kept; the admin-approval gate (FR-003) is the mitigation.
- FR-002: Member can log in and out. Priority: must-have
  > Socrates: "Short sessions force constant re-login on phones and kill adoption." Resolution: kept; sessions must be long-lived on mobile — noted for downstream design.
- FR-003: Admin can approve pending registrations. Priority: must-have
  > Socrates: "Reject is a redundant status — block covers bad actors." Resolution: REVISED — reject dropped from MVP; lifecycle is pending → active, with block/unblock for enforcement.
- FR-004: Admin can block and unblock user accounts. Priority: must-have
  > Socrates: "What happens to a blocked member's existing bookings and plan?" Resolution: kept; blocked-user edge cases routed to Open Questions.
- FR-005: Admin can browse members in one searchable list with status badges and a status filter (pending / active / blocked). Priority: must-have
  > Socrates: "Four status tabs is over-structured for a small club." Resolution: REVISED — grouped tabs replaced by a single list + filter; 'rejected' status gone with FR-003's revision.
- FR-006: Member can edit their display name and change their password. Priority: must-have
  > Socrates: "Profile editing is low-value — what's actually editable?" Resolution: REVISED — profile management trimmed to name + password for MVP.

### Class schedule & bookings
- FR-007: Member can browse the class schedule (name, date/time, room, instructor, free spots). Priority: must-have
  > Socrates: "'Schedule' tempts a weekly calendar grid, which is painful on phones." Resolution: kept; design note — day-by-day list, not a calendar grid.
- FR-008: Member can book a spot in a class that has free capacity. Priority: must-have
  > Socrates: "Two members grabbing the last spot simultaneously — no-overbooking is harder than the one-liner suggests." Resolution: kept; the no-overbooking guardrail must hold under concurrent booking.
- FR-009: Member can cancel their booking; the cancelled booking stays in history. Priority: must-have
  > Socrates: "No cancellation deadline — a 5-minutes-before cancel hurts the club." Resolution: kept as written; deadline rule considered and declined (free cancel anytime; small club tolerates it).
- FR-010: Member can view their upcoming classes. Priority: must-have
  > Socrates: "Redundant with the dashboard's nearest-classes card." Resolution: kept; the dashboard shows a summary, this is the full list (may be the 'see all' of the same data).
- FR-011: Admin can create and edit classes. Priority: must-have
  > Socrates: "Editing a class that already has bookings hides complexity (notify? rebook?)." Resolution: kept; an edit to a booked class triggers the class-changed notification (FR-021).
- FR-012: Admin can duplicate classes to following weeks. Priority: must-have
  > Socrates: "Manual duplication is weekly toil standing in for recurring series." Resolution: kept; deliberate MVP substitute — recurring series is explicitly post-MVP per seed.
- FR-013: Admin can cancel classes. Priority: must-have
  > Socrates: "Cancel vs delete — cancelled classes need a distinct visible state." Resolution: kept; 'cancelled' is a state, not deletion, preserving bookings/history/notifications.
- FR-014: Admin can view the bookings for a class. Priority: must-have
  > Socrates: "The owner may really want a check-in/attendance sheet, not a raw booking list." Resolution: kept; attendance/check-in noted as a post-MVP candidate.

### Training plans & exercise library
- FR-015: Admin can create a training plan (ordered exercise list with sets, reps, weight, rest time, note). Priority: must-have
  > Socrates: "Prescribing weight assumes the admin knows each member's working weights." Resolution: kept; plans are individual per member (per seed), so weight is per-member by construction.
- FR-016: Admin can assign a training plan to a member; a member has at most one active plan — assigning a new one replaces (archives) the old. Priority: must-have
  > Socrates: "Assignment semantics unclear (one plan? many? reusable?)." Resolution: REVISED — semantics pinned to one-active-plan-per-member.
- FR-017: Member can view their current (latest) training plan after logging in; no plan-history UI in MVP. Priority: must-have
  > Socrates: "'Current' hides plan history/versioning creep." Resolution: kept with the creep fenced off — latest plan only.
- FR-018: Admin can manage the exercise library (description, muscle group, difficulty, equipment, preparation/starting-position/execution instructions). Priority: must-have
  > Socrates: "The owner must hand-enter dozens of exercises before plans are usable — unscheduled content cost." Resolution: kept; fields are optional per seed; content-entry burden acknowledged and routed to Open Questions.
- FR-019: Admin can attach a YouTube instructional video to an exercise. Priority: must-have
  > Socrates: "YouTube links rot — the library degrades silently." Resolution: kept; risk accepted (own video hosting is explicitly post-MVP per seed).
- FR-020: Member can view exercise details (instructions, video) from within their training plan. Priority: must-have
  > Socrates: "The plan is the only real entry point — a standalone browsable library could be cut." Resolution: REVISED — standalone library browsing cut from MVP; exercises are reached via the plan.

### Notifications & dashboards
- FR-021: Member receives notifications by email and push (account approved, class cancelled/changed). Priority: must-have
  > Socrates: "In-app only ≠ reliably informed — a member who doesn't open the app misses the cancellation." Resolution: REVISED — notification model flipped to email + push delivery; the Phase 3 in-app-only scope-down was reversed by the user with the integration cost re-surfaced.
- FR-022: REMOVED — no in-app notification center (no list, badges, or read/unread state); delivery is external via email + push per FR-021.
  > Socrates: "Read/unread tracking is extra machinery." Resolution: user cut the in-app center entirely rather than simplifying it.
- FR-023: Member's dashboard shows nearest classes and the active training plan. Priority: must-have
  > Socrates: "A dashboard is aggregation before content — the schedule could be the home screen." Resolution: kept as must-have; the unread-notifications card was removed along with the in-app center.
- FR-024: Admin's dashboard shows items needing attention (pending approvals, today's and upcoming classes). Priority: must-have
  > Socrates: "Same aggregation trap as FR-023." Resolution: kept as must-have; it is the admin's daily entry point.

## User Stories

### US-01: Member books a spot in a class

- **Given** a logged-in member with an active (approved) account, and a scheduled class with at least one free spot
- **When** they open the class details from the schedule and tap "Book"
- **Then** their spot is reserved, the class's free-spot count decreases by one, and the class appears in their upcoming classes

### US-02: Booked member learns their class was cancelled

- **Given** a member with an active booking for a class
- **When** the admin cancels that class
- **Then** the member receives an email and a push notification about the cancellation, and the class no longer appears as an upcoming booking

## Business Logic

The app decides who gets in and who gets a spot: every account must pass admin approval before it can act, every booking is admitted only while the class has free capacity (never overbooked, even under simultaneous requests), and every member sees the one training plan assigned personally to them.

Supporting detail:

- **Inputs the rules consume:** a registration request (awaiting the admin's approve/block decision); a booking request against a class's declared capacity and its current booking count; a plan assignment made by the admin for a specific member.
- **Outputs:** an account state (pending / active / blocked) that gates everything else; a confirmed or refused booking with the free-spot count always trustworthy; exactly one active plan visible to each member (a new assignment replaces the old).
- **Where the user meets the rules:** the pending member waits at the approval screen; the member sees live free-spot counts and is refused when a class is full; a cancelled class or edited booked class triggers email + push to every booked member; the member's dashboard opens on their personal plan.

## Non-Functional Requirements

- Mobile-first and installable: the app is comfortable on a phone and installable to the home screen, behaving like an app (PWA per seed).
- Snappy perceived response: common actions (viewing the schedule, booking, cancelling) feel immediate — user-perceived response under ~1 s.
- Notification promptness: cancellation/change notifications reach booked members within minutes of the admin's action, not hours.
- Personal data privacy: member data (names, emails, plans) is visible only to the admin and the member themselves; GDPR-baseline handling.

## Non-Goals

Locked during shaping:

- **Single club only** — no multi-tenancy, no per-club admins; this is one gym's app. Scaling to many clubs is a deliberate future decision, not an accident to prevent.
- **No attendance / check-in tracking** — booking lists only; who actually showed up is not tracked in MVP.
- **No offline-first guarantee** — the PWA is installable but requires a connection; offline caching of schedule/plan is not promised.
- **No cancellation deadline** — free cancel anytime is locked; no late-cancel penalties or time rules.
- **No in-app notification center** — no notification list, badges, or read/unread state; delivery is email + push only.
- **No standalone exercise library browsing** — exercises are reached from the training plan only.
- **No plan history** — one active plan per member; a new assignment replaces the old.
- **No account rejection status** — lifecycle is pending / active / blocked only.

Carried over from the seed ("Poza MVP"):

- No pass/membership sales, payments, subscriptions, or invoices — the app manages participation, not money.
- No waitlist for full classes.
- No full recurring-series management — weekly duplication stands in for it.
- No Trainer role — User and Admin only.
- No chat or social features.
- No Apple Health / Google Fit integrations.
- No native mobile apps — the PWA is the mobile experience.
- No self-hosted video — instructional videos are YouTube materials.
- No advanced training statistics.
- No automatic weight progression.

## Product framing (frontmatter-bound)

- `product_type: web-app` — mobile-first web app, installable as a PWA.
- `target_scale.users: medium` — one club; dozens to a hundred people.
- `timeline_budget.mvp_weeks: 3`, `hard_deadline: null`, `after_hours_only: false` — mixed mode: some dedicated daytime plus evenings.

## Open Questions

1. **What happens to a blocked member's existing bookings and assigned plan?** — Owner: user. Surfaced by FR-004's Socratic challenge.
2. **Who enters the initial exercise library content, and when?** — Owner: user. Dozens of exercises with instructions must exist before training plans are useful (FR-018).

## Quality cross-check

Ran 2026-08-30 — all six greenfield elements present (Access Control; one-sentence Business Logic; project artifacts; timeline-cost acknowledgment; Non-Goals; preserved behavior n/a). No gaps; status: accepted.
