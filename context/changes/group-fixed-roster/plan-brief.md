# Group Fixed Roster — Plan Brief

> Full plan: `context/changes/group-fixed-roster/plan.md`

## What & Why

A group keeps a fixed roster ("stały skład"), entered once, and its members are booked into the
group's upcoming classes automatically. The club's attendance spreadsheet works this way ("STAŁA LISTA
OSÓB - wpisujesz raz"); today staff re-book every person every week, which is the main reason the
spreadsheet cannot be retired. Roadmap S-37.

## Starting Point

A group (`ClassGroup`) is a definition with no members; staff book one person into one class through
`BookingProtocol`, which guarantees no overbooking and enforces the karnet. Duplication copies classes
up to 8 weeks without bookings. Groups are admin-only; trainers never see them.

## Desired End State

Admin and trainer open a group's "Skład", add up to its capacity of members, and see per member which
upcoming classes lack a booking and why. New classes of the group, new or edited karnets, roster adds
and "Uzupełnij zapisy" book the roster automatically; whatever could not be booked is listed in a
dismissible panel. Leaving the roster releases future bookings in the group except makeups.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Where the roster lives | On the group, one group per real club group | Matches one spreadsheet tab = one group, no series entity | Roadmap |
| Removal | Release all future non-makeup bookings in the group | No "roster-made" marker needed; makeups survive | Roadmap + Plan |
| Who edits | Admin, and a trainer instructing an upcoming class of the group | Same shape as the trainer's booking right: a trainer's roster actions book and release only on classes they instruct (S-16); substitutes get access only while they substitute | Roadmap + Plan |
| Duplication bound | Stays 8 weeks | Smaller change | Roadmap |
| Karnet renewal | Issuing/editing a karnet books its holder; plus "Uzupełnij zapisy" and gaps on screen | Monthly karnets would otherwise empty the later weeks | Plan |
| Trainer entry | "Grupy" in Więcej, listing groups they instruct | Bar is full; a list beats reaching rosters through one class | Plan |
| Refusals | Skipped and reported, never fatal; dismissible `.notice` panel + gaps on the roster screen | Partial success like duplication; details persist because gaps are derived | Plan |
| Member side | Nothing new, no notifications | Consistent with staff bookings today | Plan |
| Tests | Integration tests + one E2E | Rules pinned where they break, one end-to-end proof | Plan |

## Scope

**In scope:** roster table and API; batch booking through `BookingProtocol`; hooks in create,
duplicate, karnet issue/edit; roster screen for both personas; trainer "Grupy"; report panel; E2E; docs.

**Out of scope:** series entity, longer duplication, fixed trainer/time on a group, member
notifications, roster-made booking marker, hard lock on roster size.

## Architecture / Approach

`RosterBooking` books members × classes, one protocol transaction each, on a new reason-returning core
of `BookingProtocol`, and returns a `RosterReport`. Every trigger commits its own write first, then
calls it. `RosterAuthorization` (admin, or instructor of an upcoming class) guards the roster routes
and defines the trainer's group list. Gaps on the roster screen are derived on read.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Roster in the API | Table, batch service, roster routes, authorization, tests | Protocol refactor must not change the single-booking route |
| 2. Automatic bookings | Create/duplicate/karnet hooks with reports | DiscardChanges detaching entities the response is built from |
| 3. Screens | Roster screen, trainer Grupy, report panel | Kit lint, eager bundle, persona parity menu/guard/API |
| 4. E2E and docs | Playwright proof, manual cases, roadmap, AGENTS.md | E2E groups cannot be deleted — deactivate on cleanup |

**Prerequisites:** S-36 merged to `main`; work on a branch from it.
**Estimated effort:** ~4 sessions, one per phase.

## Open Risks & Assumptions

- Roster cap is soft: two simultaneous adds can exceed it by one (class capacity stays hard).
- Gap reasons are evaluated per class independently, so they describe "now", not a sequence;
  "Uzupełnij zapisy" is the authority.
- Assumes the club models each real group as its own `ClassGroup`.

## Success Criteria (Summary)

- Staff enter a group's members once and stop booking them weekly.
- A karnet renewal fills the following weeks without any extra click.
- No class is ever overbooked and no entry spent twice by an automatic booking.
