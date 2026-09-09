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
}

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
    | 'no_valid_pass'
    | 'no_entries_left'
    | 'conflict';
}
