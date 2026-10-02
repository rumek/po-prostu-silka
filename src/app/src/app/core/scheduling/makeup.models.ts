/**
 * Makeups (S-36, class-makeups). Mirrors src/Application/Scheduling/MakeupItem.cs — keep the two in
 * step; this is a contract, not a convenience type.
 *
 * An absence marked "odrobi" spends its entry and earns ONE free makeup, booked by staff into any
 * class taking place within thirty club-local days. A makeup ITEM is that absence and what became of
 * it; its status is derived on the server and never computed here.
 */

/**
 * Where one item stands. `open` — "do odrobienia"; `planned` — a makeup is booked; `made_up` —
 * "odrobił"; `not_made_up` — "nie odrobił" (forfeited at the makeup, past the deadline, or closed by
 * hand).
 */
export type MakeupStatus = 'open' | 'planned' | 'made_up' | 'not_made_up';

/** The class a makeup is booked on. */
export interface MakeupClass {
  classId: string;
  bookingId: string;
  name: string;

  /** ISO 8601 UTC. */
  startsAt: string;
  instructor: string;
}

/** Mirrors MakeupItem — one row of the staff "Odrabianie" list. */
export interface MakeupItem {
  /** The absence booking: the item's identity on every makeup route. */
  absenceBookingId: string;
  memberId: string;
  displayName: string;

  /** The group of the class missed. */
  className: string;

  /** ISO 8601 UTC. When the missed class started. */
  absenceStartsAt: string;

  /** Who instructed the missed class. */
  instructor: string;

  /** YYYY-MM-DD, club-local and inclusive: the last day a makeup class may take place on. */
  deadline: string;
  status: MakeupStatus;

  /** Staff closed it as "nie odrobił" by hand; it may be reopened while the deadline allows. */
  closedByHand: boolean;

  /** The live makeup booking, or null when none stands. */
  makeup: MakeupClass | null;
}

/** Mirrors `PagedResult<MakeupItem>`. */
export interface MakeupPage {
  items: MakeupItem[];
  total: number;
  page: number;
  pageSize: number;
}

/** Mirrors MyMakeups — what the member is told on Moje zajęcia. */
export interface MyMakeups {
  /** Open items only — planned ones are already on the member's upcoming list. */
  count: number;

  /** YYYY-MM-DD, or null with nothing open. */
  nearestDeadline: string | null;
}

/**
 * Mirrors MakeupFailure. Every one is a 409.
 *
 * `makeup_not_open` — the item is no longer "do odrobienia": a makeup is booked, it was made up, or it
 * was closed. `makeup_deadline_passed` — the chosen class falls after the deadline.
 * `makeup_not_reopenable` — the item was not closed by hand, or its deadline has passed. The rest are
 * the booking loop's own refusals, reached through the makeup route.
 */
export interface MakeupFailure {
  reason:
    | 'makeup_not_open'
    | 'makeup_deadline_passed'
    | 'makeup_not_reopenable'
    | 'class_full'
    | 'class_started'
    | 'class_cancelled'
    | 'already_booked'
    | 'no_valid_pass'
    | 'member_blocked'
    | 'member_is_staff'
    | 'conflict';
}

/** Every reason in {@link MakeupFailure}, as a value — compiler-checked, as BOOKING_FAILURE_REASONS. */
export const MAKEUP_FAILURE_REASONS = Object.keys({
  makeup_not_open: true,
  makeup_deadline_passed: true,
  makeup_not_reopenable: true,
  class_full: true,
  class_started: true,
  class_cancelled: true,
  already_booked: true,
  no_valid_pass: true,
  member_blocked: true,
  member_is_staff: true,
  conflict: true,
} satisfies Record<MakeupFailure['reason'], true>) as readonly MakeupFailure['reason'][];
