---
change_id: class-makeups
title: Absence is either made up or forfeited, and staff track the makeups
status: implemented
created: 2026-10-02
updated: 2026-10-02
archived_at: null
---

## Notes

Origin: the club's attendance spreadsheet (`xlsx/treningi.xlsx`). Every person on every training gets
one of three statuses: "Był", "Nie był – odrobi" or "Nie był – przepada". Each "odrobi" is copied to a
shared "Odrabianie" sheet with a status ("Do odrobienia / Odrobił / Nie odrobił") and the makeup's
date, time and trainer. The app knows only `Present` / `Absent`, and `Absent` always returns the entry
(`EntryConsumption.ConsumesAnEntry`). So it counts entries differently from the club, and nothing
tracks a makeup. Fixed group rosters were considered and set aside for now (user, 2026-10-02).

Decisions made with the user (2026-10-02):

- **Three attendance outcomes, as in the sheet:**
  - present;
  - absent with a makeup ("odrobi");
  - absent forfeited ("przepada").

  They replace `Absent` for every NEW record.
- **Entry rule, as in the sheet:**
  - present spends the entry;
  - forfeited spends the entry;
  - "odrobi" ALSO spends the entry, on the original class, and earns the right to ONE free makeup.

  The makeup booking consumes no entry. It still needs a karnet valid on the makeup class's
  club-local date, and a free spot. S-16 and the no-overbooking rule stay intact, and there is no
  makeup once the karnet has expired.
- **Deadline:** a fixed 30 days from the absence's club-local date, as a constant (like S-35's 5-day
  window). Past it, the item reads "nie odrobił". The status is derived on read; there is no
  background job.
- **Makeup item status:** "do odrobienia" moves to either:
  - "odrobił", when the makeup booking is marked present;
  - "nie odrobił", when the deadline passes or staff close the item by hand.
- **Who books a makeup:** an admin into any class, and a trainer into ANY class. This widens
  `BookingAuthorization.MayActOn` for makeup bookings only, never for ordinary ones.
- **Who sees the "Do odrobienia" list:** the admin and the trainer, the whole list. The member sees
  in Moje zajęcia that they have a class to make up. Staff never hold makeups (`member_is_staff`).
- **Legacy `Absent` rows keep their meaning:** the entry stays returned, the value is read-only and
  cannot be selected, and no member's balance changes on deploy.

Constraints and points for `/10x-plan`:

- **One definition of consumption.** `EntryConsumption.ConsumesAnEntry` stays the single expression.
  The three sites that read it (`BookingStore.CountConsumingForPassAsync` and both
  `MembershipPassQuery` paths) change together, and the makeup booking's exclusion goes into that same
  expression.
- **`BookingAttendance` is persisted as an int.** Never reorder its values: `Absent = 1` stays as the
  legacy value, and new values are appended.
- **Link the makeup booking to its absence,** e.g. a nullable `MakeupForBookingId` on `Booking` with a
  filtered unique index, so that each absence has at most one makeup. The plan decides what a
  cancelled makeup booking does; the likely answer is that the item reopens.
- **Concurrency.** A makeup booking bypasses the pass's entry pool, but not the class's capacity
  stamp.
- **This amends S-27 AT-03.**
  - Roadmap: a new slice, with S-27's rule noted as amended.
  - `e2e/absence-returns-karnet-entry.spec.ts` must be rewritten, because a new absence no longer
    returns an entry.
  - Manual cases ATT-01/02 need review.
- **The words come from failure tables (S-19).** New refusal reasons get a union and a
  `*FailureMessage` table: past the deadline, makeup already used, no valid karnet.
- **Out of scope:**
  - fixed group rosters;
  - comments on attendance;
  - the "WYGASŁ" filter.
