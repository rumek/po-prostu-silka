/**
 * Mirrors the API's ScheduledClass record (src/Application/Scheduling/ClassEndpoints.cs).
 * Keep the two in step — this is a contract, not a convenience type.
 */
export interface ScheduledClass {
  id: string;
  name: string;

  /** ISO 8601 UTC from the API. Kept as a string; the screen converts and formats it. */
  startsAt: string;

  durationMinutes: number;
  room: string;
  instructor: string;
  capacity: number;

  /**
   * Read this, never assume it equals capacity. It does today only because Booking does not exist
   * until S-04 — that slice changes one projection expression on the server and this field starts
   * differing without any change here.
   */
  freeSpots: number;

  /** The enum NAME, never its int — the numeric values exist only for persistence stability. */
  status: 'Scheduled' | 'Cancelled';
}

/** Mirrors ClassRequest. Create and edit take the same shape; an edit replaces every field. */
export interface ClassRequest {
  name: string;

  /** ISO 8601 UTC. The form works in local time and converts on submit — see class-form. */
  startsAt: string;

  durationMinutes: number;
  room: string;
  instructor: string;
  capacity: number;
}

/**
 * Mirrors DuplicateResult. NOT a bare success: a batch where some weeks collided is a partial
 * success, and the screen has to say which weeks were skipped or the admin believes in classes that
 * were never created.
 */
export interface DuplicateResult {
  created: number;

  /** 1-based week offsets refused for a room conflict. */
  skippedWeeks: number[];
}

/**
 * Mirrors ClassFailure. Every reason the API can refuse a class write.
 *
 * `room_conflict` is the only 409 — it is a clash with existing state rather than bad input, and
 * the form maps it onto the room control rather than showing a banner.
 */
export interface ClassFailure {
  reason:
    | 'missing_field'
    | 'invalid_capacity'
    | 'invalid_duration'
    | 'starts_in_past'
    | 'room_conflict'
    | 'invalid_weeks';
}
