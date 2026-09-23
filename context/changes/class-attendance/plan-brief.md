# S-27 Class Attendance — Plan Brief

> Full plan: `context/changes/class-attendance/plan.md`
> Research: `context/changes/class-attendance/research.md`

## What & Why

Staff record who came to a class, and a karnet entry is spent by attending rather than by booking.
The trainer does this for the classes they instruct, the admin for any class. The member sees their
own attendance history on a phone, as something to scan rather than a wall of sentences. This is
roadmap S-27, which closes M-8. It amends MP-06 and answers Open Roadmap Question 7.

## Starting Point

- `Booking` has no attendance, and entries used is "active bookings on the pass", copied in three
  places.
- A cancelled class keeps its entries spent.
- Release is allowed after a class happened, as a no-show workaround.
- Members have no read path for past classes.

## Desired End State

- **Staff.** Once a class starts, its roster switches to Obecny/Nieobecny toggles, and corrections
  are allowed forever. An admin can reach last week's roster from the desk calendar.
- **The karnet.** It gives an entry back on a recorded absence or a cancelled class, and can never be
  overdrawn.
- **The member.** Zajęcia gets a Historia tab with:
  - a current-karnet summary;
  - month groups with tallies;
  - status chips per class.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Data shape | Nullable `Attendance` column on `Booking`, not new statuses | Every `Status == Active` predicate (capacity, unique index, roster, `mine`) stays correct untouched | Research |
| Entry rule | Booking reserves; absent / released / cancelled class returns | Keeps MP-06's no-overdraw guarantee with no migration | Research |
| Unrecorded attendance | Stays spent, forever | No background job, and it is today's behaviour | Plan |
| Marking window | From class start, unlimited, same for trainer and admin | One rule (AT-01), and admins can fix mistakes late | Plan |
| Release after start | Refused (`class_started`) | "Nieobecny" is the honest no-show record, and release erased history | Plan |
| Absent → present on a full pass | Refused `no_entries_left` | Same gate as booking, so the invariant holds | Plan |
| Clearing a mark | Not possible, only present ↔ absent | Simpler toggle, and "nie odnotowano" means nobody checked | Plan |
| History scope | Active bookings on started classes, plus club-cancelled ones | The member sees why an entry came back, without desk noise | Plan |
| History placement | Tabs on `/my-classes`, `?widok=historia`, `replaceUrl` | User's choice; the URL keeps reload and back honest | Plan |
| Summary and paging | Current karnet counts, 3-month windows, "Pokaż wcześniejsze" | Answers "how much of this karnet did I use", with a bounded query | Plan |
| Desk past weeks | Past tiles open the roster in attendance mode | Unlimited corrections need a reachable surface | Plan |
| Karnet card | Unchanged | User's choice | Plan |
| RevokePass | Still refused while any active booking references the pass | Absent rows are history and the FK is `Restrict` | Plan |

## Scope

**In scope:**
- The attendance column and migration.
- One shared entries-used predicate.
- Cancel returns entries.
- Release refused after the start.
- The PUT attendance endpoint.
- The roster DTO field.
- The member history endpoint.
- The roster overlay's attendance mode.
- Desk past tiles.
- The `/my-classes` history tab.

**Out of scope:**
- Walk-ins, self check-in, statistics and no-show patterns.
- A window for unrecorded classes.
- Clearing marks.
- Karnet card changes.
- Released bookings in history.
- Roster e-mails for trainers (ORQ 8).

## Architecture / Approach

**Backend.**
- `EntryConsumption.ConsumesAnEntry` in Infrastructure is the single predicate
  (`Status == Active && Class.Status != Cancelled && Attendance != Absent`), used by the booking gate
  and both karnet read paths.
- `RecordAttendance` sits in the staff booking group behind `MayActOn`. It runs in the booking retry
  loop, rotates the pass stamp on every change and re-runs the entry gate on absent → present.
- `CancelClass` rotates each booked pass's stamp.
- `GET /api/bookings/history` (`MemberOnly`) returns windowed items plus a summary.

**SPA.**
- The roster overlay picks its mode from the start time.
- `/my-classes` gains ARIA tabs and an `attendance-history` component built from the kit.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Entry rule and schema | Column and migration, the shared predicate, cancel returns entries, release refused after start | Predicate fails to translate in a correlated subquery. Fallback: copies plus an agreement test |
| 2. Marking API | PUT attendance, roster field, AT-01/02 tests | The absent → present race against a concurrent booking |
| 3. History API | `/api/bookings/history` with windows and summary | Club-local month boundaries |
| 4. SPA roster and desk | Attendance toggles, past-week tiles | Mode split leaves release reachable on a started class |
| 5. SPA history tab | Tabs, summary card, month groups, chips | AT-05 quality: the "cards with sentences" regression |

**Prerequisites:** S-16 and S-25 are done (archived). Local SQL Server (`docker compose up -d`),
Node 22+.
**Estimated effort:** about 4–5 sessions, one per phase. Phases 4–5 are the UX-heavy ones.

## Open Risks & Assumptions

- On deploy, already-cancelled classes retroactively return entries, so affected members' balances
  rise. Intended; it goes in the release note.
- Staff who never mark leave members' entries spent. This was accepted as today's behaviour.
- Phase 1 refuses release after the start before the SPA hides the button (Phase 4). Meanwhile the
  row shows the existing "already started" sentence, which is acceptable.

## Success Criteria (Summary)

- A trainer marks attendance on their started class from a phone, and the member's entries-left
  follows the marks.
- No sequence of bookings, marks, corrections or cancellations overdraws a karnet, pinned by
  concurrency tests.
- A member opens Zajęcia → Historia and understands their month at a glance.
