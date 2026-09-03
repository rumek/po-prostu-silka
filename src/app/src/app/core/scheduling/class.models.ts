/**
 * Mirrors the API's ScheduledClass record (src/Application/Scheduling/ClassEndpoints.cs).
 * Keep the two in step — this is a contract, not a convenience type.
 */
export interface ScheduledClass {
  id: string;

  /** Which class type this occurrence instantiates. Immutable once created — see ClassRequest. */
  classTypeId: string;

  /**
   * RESOLVED FROM THE CLASS TYPE, not stored on the occurrence (prd-v2 FR-010). The occurrence has
   * no name of its own, which is what makes correcting a typo on the type correct it on every week
   * at once, past occurrences included.
   */
  name: string;

  /** The type's description, same reference semantics as `name`. Absent as `null`. */
  description: string | null;

  /** ISO 8601 UTC from the API. Kept as a string; the screen converts and formats it. */
  startsAt: string;

  /**
   * A COPY of the type's default, taken at creation and overridable for this occurrence alone. The
   * opposite of `name`: never re-read from the type. Same for `capacity` — see prd-v2 FR-007.
   */
  durationMinutes: number;

  /** The instructor's account id. What the form's trainer select submits. */
  instructorUserId: string;

  /**
   * RESOLVED — the instructor's display name, not a typed string. Free text until S-06, when the
   * Trainer role gave the schedule a real person to point at.
   */
  instructor: string;

  /** A COPY, like `durationMinutes`. The value the no-overbooking guarantee is checked against. */
  capacity: number;

  /**
   * How many spots are left. REAL since S-08 — it was equal to capacity only while nothing could be
   * booked. Read it, never derive it: the server counts active bookings, and it is deliberately not
   * clamped at zero, so a negative value would mean a broken invariant rather than a full class.
   */
  freeSpots: number;

  /** The enum NAME, never its int — the numeric values exist only for persistence stability. */
  status: 'Scheduled' | 'Cancelled';
}

/**
 * Mirrors ClassRequest. Create and edit take the same shape.
 *
 * A FORM OF SELECTIONS, NOT OF TEXT (prd-v2 US-01): there is no name and no room to type. What
 * remains typed are the two numbers, and they arrive prefilled from the type's defaults.
 */
export interface ClassRequest {
  /**
   * Required on an edit too, but only so the server can refuse a CHANGE to it: the type is immutable
   * once an occurrence exists (`class_type_immutable`). Send back what the class already has.
   */
  classTypeId: string;

  /** ISO 8601 UTC. The form works in local time and converts on submit — see class-form. */
  startsAt: string;

  durationMinutes: number;

  /** An account id from `/api/admin/trainers`. Unlike the type, this IS editable. */
  instructorUserId: string;

  capacity: number;
}

/**
 * Mirrors DuplicateResult. NOT a bare success: a batch where some weeks collided is a partial
 * success, and the screen has to say which weeks were skipped or the admin believes in classes that
 * were never created.
 */
export interface DuplicateResult {
  created: number;

  /**
   * 1-based week offsets refused because another class already occupies that time. The REASON
   * changed in S-06 — it used to be a room collision — but the shape did not.
   */
  skippedWeeks: number[];
}

/**
 * Mirrors ClassFailure. Every reason the API can refuse a class write.
 *
 * Four are 409s — a clash with existing state rather than bad input. `time_conflict` is the one the
 * form maps onto the start-time field rather than showing a banner; it replaced `room_conflict` when
 * the overlap rule widened from one room to the whole club (prd-v2 FR-012).
 *
 * S-08 ADDED THREE MORE, all 409s, and all consequences of bookings existing: `has_bookings` refuses
 * a delete once anyone has ever booked the class, `capacity_below_bookings` refuses an edit that
 * would shrink it below the number already signed up, and `conflict` means a booking committed
 * between the server's check and its write.
 */
export interface ClassFailure {
  reason:
    | 'missing_field'
    // NOTE: `invalid_range` is deliberately NOT here — see ScheduleReadFailure below.
    | 'invalid_capacity'
    | 'invalid_duration'
    | 'starts_in_past'
    | 'invalid_weeks'
    | 'time_conflict'
    | 'unknown_class_type'
    | 'inactive_class_type'
    | 'class_type_immutable'
    | 'unknown_instructor'
    | 'instructor_not_trainer'
    | 'has_bookings'
    | 'capacity_below_bookings'
    | 'conflict';
}

/**
 * Why a schedule READ was refused. Mirrors the same `ClassFailure` record on the wire — the server
 * reuses that shape — but is a separate type here on purpose.
 *
 * `invalid_range` is the only reason the two read endpoints produce, and no write path can ever
 * return it. Folding it into ClassFailure would widen a union the class form switches over
 * exhaustively, forcing a form-field message for a refusal the form cannot receive. Two endpoint
 * groups, two types.
 *
 * Neither screen renders this today: a bad range is a client bug, not something the member can act
 * on, so both fall back to their generic "failed to load" state. The type exists so that a screen
 * which one day wants to distinguish the two has something to narrow on.
 */
export interface ScheduleReadFailure {
  reason: 'invalid_range';
}
