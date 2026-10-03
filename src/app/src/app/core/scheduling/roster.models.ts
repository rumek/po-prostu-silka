/**
 * A group's fixed roster ("stały skład", S-37). Mirrors the API's roster records
 * (src/Application/Scheduling/IGroupRosterQuery.cs, RosterReport.cs) — keep the two in step.
 */

/**
 * One booking an automatic roster booking did NOT make. `reason` is a `BookingFailure` reason (or
 * `not_your_class`), so it reads through `bookingFailureMessage` — the roster adds no words of its own.
 */
export interface RosterSkip {
  memberId: string;
  memberName: string;
  classId: string;
  startsAt: string;
  reason: string;
}

/** What one automatic roster booking did: how many it made, and every one it could not. */
export interface RosterReport {
  booked: number;
  skipped: RosterSkip[];
}

/**
 * An upcoming class of the group a roster member holds no booking on. `reason` is what would refuse a
 * booking NOW, or `bookable` — evaluated per class, so two gaps can both read "bookable" against a
 * karnet with one entry left; "Uzupełnij zapisy" is the authority.
 */
export interface RosterGap {
  classId: string;
  startsAt: string;
  reason: string;
}

export const BOOKABLE_GAP = 'bookable';

export interface GroupRosterMember {
  memberId: string;
  displayName: string;
  hasAccount: boolean;
  addedAt: string;
  /** How many of the group's upcoming classes the member holds a booking on (a makeup included). */
  bookedUpcoming: number;
  gaps: RosterGap[];
}

export interface GroupRosterView {
  groupId: string;
  name: string;
  isActive: boolean;
  /** The group's DEFAULT capacity, which is also the roster's cap. */
  capacity: number;
  upcomingClassCount: number;
  members: GroupRosterMember[];
}

/** The answer of a roster add and of "Uzupełnij zapisy": the refreshed roster, and what booking did. */
export interface RosterChange {
  roster: GroupRosterView;
  report: RosterReport;
}

/** One row of the trainer's "Grupy": a group whose upcoming classes they instruct. */
export interface TrainerGroup {
  id: string;
  name: string;
  rosterCount: number;
  capacity: number;
  nextClassAt: string | null;
}

/**
 * Mirrors RosterFailure — every reason the API refuses a roster write, all 409s. `member_blocked` and
 * `member_is_staff` refuse at the door; a member blocked or promoted LATER stays in the roster and their
 * automatic bookings report it instead.
 */
export interface RosterFailure {
  reason:
    | 'already_in_roster'
    | 'roster_full'
    | 'member_blocked'
    | 'member_is_staff'
    | 'inactive_class_group'
    | 'conflict';
}

/** Every reason in {@link RosterFailure}, as a value — see BOOKING_FAILURE_REASONS for why. */
export const ROSTER_FAILURE_REASONS = Object.keys({
  already_in_roster: true,
  roster_full: true,
  member_blocked: true,
  member_is_staff: true,
  inactive_class_group: true,
  conflict: true,
} satisfies Record<RosterFailure['reason'], true>) as readonly RosterFailure['reason'][];
