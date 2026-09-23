/**
 * Mirrors the API's MyBooking record (src/Application/Scheduling/BookingEndpoints.cs).
 * Keep the two in step — this is a contract, not a convenience type.
 *
 * Deliberately NOT a `ScheduledClass`. That shape carries capacity, free spots and the instructor's
 * account id, none of which "Moje zajęcia" needs, and it lacks the two fields that make a row a
 * booking rather than a class: `bookingId` and `bookedAt`.
 */
export interface MyBooking {
  bookingId: string;

  /** The occurrence booked. What the cancel call addresses — cancelling is by CLASS, not by booking. */
  classId: string;

  /**
   * RESOLVED FROM THE CLASS TYPE, like `ScheduledClass.name`. A booking stores none of the three
   * resolved fields, so correcting a typo on the type corrects it here too.
   */
  name: string;

  /** The type's description, same reference semantics as `name`. Absent as `null`. */
  description: string | null;

  /** ISO 8601 UTC from the API. Kept as a string; the screen converts and formats it. */
  startsAt: string;

  durationMinutes: number;

  /** RESOLVED — the instructor's display name. */
  instructor: string;

  /** ISO 8601 UTC. When the member claimed the spot. */
  bookedAt: string;
}

/**
 * Mirrors ClassBooking — one signed-up member, as the admin's list shows them (prd.md FR-014).
 *
 * Admin-only. The email is here because the club's actual use for this list is reaching people when
 * a class moves or a trainer is ill.
 */
export interface ClassBooking {
  bookingId: string;

  /** Who holds the spot. A MEMBER id since S-14 — the person, not their login. */
  memberId: string;

  /**
   * Their account, or null when they have none (S-14). Present because push notifications are
   * account-keyed; the screen itself has no use for it.
   */
  userId: string | null;
  displayName: string;
  email: string;
  bookedAt: string;

  /**
   * Whether they came (S-27): `present`, `absent`, or null while nobody recorded it. Always null
   * before the class starts — the server refuses to mark earlier.
   */
  attendance: Attendance | null;
}

/** A recorded attendance mark (S-27). Unrecorded is `null` on the row, never a third value here. */
export type Attendance = 'present' | 'absent';

/**
 * Mirrors BookingFailure. Every reason the API can refuse a booking write.
 *
 * EVERY ONE OF THESE IS A 409, which is what makes this union different from `ClassFailure`. A
 * booking request carries no fields to get wrong — the class is in the URL and the member is the
 * caller — so there is nothing here that could be a 400. A missing class is a 404 and not a reason,
 * and neither is a member id nobody issued.
 *
 * `member_blocked` reaches only the ADMIN booking route (S-14): on the member's own route the
 * ActiveMember policy has already vouched for them, so it cannot occur there.
 *
 * `member_is_staff` (S-25): trainers and admins are never booked as participants. The trainer's
 * picker never offers them; the admin's does, and this is the refusal it then shows.
 *
 * `class_not_started` (S-27) comes from the attendance route only: attendance is marked from the
 * class's start, never before.
 *
 * `conflict` is the only one that is not a product rule: the server's retry loop lost its race on
 * every attempt, and the honest advice is to try again.
 */
export interface BookingFailure {
  reason:
    | 'class_cancelled'
    | 'class_started'
    | 'already_booked'
    | 'class_full'
    | 'member_blocked'
    | 'member_is_staff'
    | 'no_valid_pass'
    | 'no_entries_left'
    | 'class_not_started'
    | 'conflict';
}

/**
 * Every reason in {@link BookingFailure}, as a value.
 *
 * <h2>Why the object literal</h2>
 *
 * `satisfies Record<BookingFailure['reason'], true>` makes the COMPILER check the list is complete:
 * omit a reason and the build fails here. The specs used to hand-copy this list and guard it with
 * `expect(REASONS.length).toBe(n)` - a second oracle that only caught a stale list after someone
 * remembered to bump the number. A union cannot be enumerated at runtime, so this is the cheapest
 * construct that turns "the list is complete" into a build-time fact.
 */
export const BOOKING_FAILURE_REASONS = Object.keys({
  class_cancelled: true,
  class_started: true,
  already_booked: true,
  class_full: true,
  member_blocked: true,
  member_is_staff: true,
  no_valid_pass: true,
  no_entries_left: true,
  class_not_started: true,
  conflict: true,
} satisfies Record<BookingFailure['reason'], true>) as readonly BookingFailure['reason'][];

/**
 * What became of one past class of the member's (S-27). `cancelled` wins over any mark: nobody
 * attends a cancelled class, and the member is owed the reason their entry came back.
 */
export type AttendanceOutcome = 'present' | 'absent' | 'unrecorded' | 'cancelled';

/** Mirrors MyAttendanceEntry — one row of the member's history. */
export interface MyAttendanceEntry {
  bookingId: string;
  classId: string;
  name: string;

  /** ISO 8601 UTC. Grouped and shown by the CLUB's calendar, not the device's. */
  startsAt: string;
  durationMinutes: number;
  instructor: string;
  outcome: AttendanceOutcome;
}

/** Mirrors MyAttendanceSummary — the current karnet, read as attendance. */
export interface MyAttendanceSummary {
  typeName: string;

  /** ISO dates (YYYY-MM-DD), club-local and inclusive. */
  validFrom: string;
  validTo: string;
  entryCount: number;
  present: number;
  absent: number;
  unrecorded: number;
}

/** Mirrors MyAttendanceHistory — one page: three club-local months, newest first. */
export interface MyAttendanceHistory {
  /** Only on the first page, and only while a karnet covers today. */
  summary: MyAttendanceSummary | null;
  items: MyAttendanceEntry[];

  /** The `before` for the next page back (YYYY-MM-DD), or null at the start of the history. */
  earlierBefore: string | null;
}
