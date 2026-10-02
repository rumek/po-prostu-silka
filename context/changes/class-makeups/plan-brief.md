# Class Makeups — Plan Brief

> Full plan: `context/changes/class-makeups/plan.md`

## What & Why

The club marks every absence in its spreadsheet as one of two kinds:
- **"odrobi":** the absence earns a free makeup class within a deadline;
- **"przepada":** the absence is lost.

The app knows only "absent", and that always returns the karnet entry. So the app counts entries
differently from the club, and nobody can track a makeup. This change brings the club's rule into
the app and replaces the spreadsheet's "Odrabianie" sheet.

## Starting Point

- **Attendance (S-27)** is `Present` / `Absent` on the booking. One expression,
  `EntryConsumption.ConsumesAnEntry`, decides whether a booking spends an entry, and four read sites
  use it.
- **Booking** goes through the race-safe `BookingProtocol`.
- **A trainer reaches only their own classes.**

## Desired End State

- **Roster:** staff mark Był / Odrobi / Przepada.
- **Entries:**
  - "Odrobi" and "Przepada" keep the entry spent;
  - "Odrobi" also opens an item on a new "Odrabianie" list, where an admin or any trainer books ONE
    free makeup into any class within 30 days;
  - legacy "absent" rows keep their returned entry.
- **Member:** sees on Moje zajęcia what they have to make up, and by when.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Entry rule | Odrobi and Przepada spend; the makeup is free | Same as the spreadsheet: one session paid, one session owed | change.md |
| Deadline | 30 days, counted against the makeup **class date** | "Odrobić w 30 dni" means the class happens within 30 days | change.md + Plan |
| Karnet for the makeup | Any karnet valid on the makeup day | A renewal must not cancel the right to a makeup; one rule with S-16 | Plan |
| Missed makeup | Only Był / Przepada on a makeup | One right means one attempt, so no chains | Plan |
| Re-marking an absence with a makeup | Refused (`makeup_booked`) until the makeup is released | Nothing disappears silently | Plan |
| Who books | Admin and ANY trainer, into any class, from the list only | Matches the club; the roster's "Dopisz" stays ordinary | change.md + Plan |
| List | Open items by default; "Pokaż zamknięte" in the URL | A daily working list, with history on demand | Plan |
| Manual close | Close and reopen within the deadline | A mis-click is not permanent | Plan |
| Navigation | Trainer's bar tab, admin header and Więcej; no Start card | The screen stays lazy, so the eager bundle is untouched | Plan |
| Legacy `Absent` | Kept, read-only, entry stays returned | No balance changes on deploy | change.md |
| Model | No new table: link + close fields on `Booking`; status derived on read | Follows the S-35 derived-predicate precedent | Plan |

## Scope

**In scope:**
- The three outcomes, the entry rule and the migration.
- The makeup API: list, eligible classes, book, release, close and reopen, member summary.
- The roster, history and Moje zajęcia.
- The Odrabianie screen with its class picker.
- E2E, the roadmap entry S-36 and the manual cases.

**Out of scope:**
- Fixed group rosters and recurring series.
- Comments on attendance, the WYGASŁ filter, and money.
- Booking a makeup from the roster, a dashboard card, and notifications.

## Architecture / Approach

**Data model.** Each makeup item is an absence booking (`Attendance = Makeup`). It may link to one
makeup booking (`MakeupForBookingId`, guarded by a filtered unique index on active rows) and may
carry a manual close.

**Status.** One Infrastructure predicate derives `open / planned / made_up / not_made_up` from club
time. The list, the member summary and the booking gate all read it.

**Booking.** A makeup books through `BookingProtocol` with the entry check skipped. Capacity and
karnet validity stay on the same guarded path.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Outcomes and entry rule | Enum, columns, migration, `ConsumesAnEntry`, `RecordAttendance` | Changing the one expression the entry balance hangs on |
| 2. Makeup API | Status predicate, eligible classes, book, release, close, `mine` | The deadline and karnet edges around Warsaw midnight; the concurrent makeup race |
| 3. Roster and member screens | Three-way toggle, history labels, Moje zajęcia hint | Three buttons fitting a phone row |
| 4. Odrabianie screen | Lazy staff list, class picker, actions | Trainer nav slot; kit lint |
| 5. E2E and docs | New makeup spec replaces the absence spec; S-36; manual cases | Spec flakiness across two trainers |

**Prerequisites:** `main` at 9db5bc3 or later (S-34/S-35 merged), plus `docker compose up -d`.
**Estimated effort:** ~4–5 sessions across 5 phases.

## Open Risks & Assumptions

- **Rollback:** a rollback after rows are marked Makeup/Forfeited shows them as unmarked in the old
  SPA. The balances stay correct. This is accepted for one release.
- **Status queries:** the status predicate's correlated subqueries are assumed cheap, because makeup
  rows are few. Measure before indexing.
- **Trainer reach:** a trainer can now book a makeup into any class. This is the one deliberate
  widening of `MayActOn`'s reach, and it is persona-gated (TrainerOrAdmin).

## Success Criteria (Summary)

- The app's entries-left matches the club's spreadsheet for Był, Odrobi and Przepada, and legacy
  balances are unchanged.
- A trainer turns an "odrobi" into a booked, attended makeup without the admin, and the item ends as
  "odrobił".
- The Odrabianie list replaces the spreadsheet's shared sheet.
