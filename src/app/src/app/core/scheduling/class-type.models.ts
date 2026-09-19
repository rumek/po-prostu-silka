/**
 * Mirrors the API's ClassTypeSummary record (src/Application/Scheduling/ClassTypeEndpoints.cs).
 * Keep the two in step — this is a contract, not a convenience type.
 */
export interface ClassTypeSummary {
  id: string;
  name: string;

  /** Absent as `null`, never as an empty string — the API normalises blank to null on write. */
  description: string | null;

  /**
   * The DEFAULT duration, kept prefixed all the way out here. S-06 copies it onto an occurrence at
   * creation; an occurrence's own duration is never resolved through the type.
   */
  defaultDurationMinutes: number;

  /**
   * The DEFAULT capacity. Same copy semantics as above, and they matter more here: capacity resolved
   * through the type would let a type edit move the value the no-overbooking guarantee is checked
   * against.
   */
  defaultCapacity: number;

  /** Whether the type is offered. A retired type keeps its occurrences; there is no hard delete. */
  isActive: boolean;

  createdAt: string;
}

/**
 * Mirrors ClassTypeRequest. Create and edit take the same shape; an edit replaces every field.
 *
 * `isActive` is deliberately absent — activation has its own two endpoints, so an edit cannot
 * resurrect a type the admin retired.
 */
export interface ClassTypeRequest {
  name: string;

  /** Send `null` for "no description"; the API also accepts blank and stores it as null. */
  description: string | null;

  defaultDurationMinutes: number;
  defaultCapacity: number;
}

/**
 * Mirrors ClassTypeFailure. Every reason the API can refuse a class-type write.
 *
 * `name_taken` is the only 409 — it is a clash with existing state rather than bad input. It arrives
 * from create and edit, where the form maps it onto the name control, and ALSO from activate, whose
 * request carries no name at all: deactivating releases a name, so another type may have claimed it
 * meanwhile. The list screen reports that case as a row-level message, since there is no control to
 * attach it to.
 */
export interface ClassTypeFailure {
  reason:
    | 'missing_field'
    | 'name_too_long'
    | 'invalid_duration'
    | 'invalid_capacity'
    | 'description_too_long'
    | 'name_taken';
}

/**
 * The bounds the class-type form and the class-type message table both need.
 *
 * <h2>Why they moved here (S-19)</h2>
 *
 * They were private to `class-type-form.ts`, which was fine while the form was also the only place
 * that wrote the sentences. The sentences now live in `class-type-failure.ts`, and a message that
 * quotes a bound the form owns privately is a message that drifts the first time the bound moves.
 * Beside the union they belong to, both consumers import one definition.
 *
 * Every value matches ClassTypeEndpoints.Validate, and the two lengths also match
 * ClassTypeConfiguration's columns. Keep all of them in step.
 */
export const CLASS_TYPE_BOUNDS = {
  minDuration: 1,
  maxDuration: 480,
  minCapacity: 1,
  maxCapacity: 200,
  maxName: 200,
  maxDescription: 1000,
} as const;

/**
 * Every reason in {@link ClassTypeFailure}, as a value. See BOOKING_FAILURE_REASONS in
 * core/scheduling/booking.models.ts for why the object literal.
 */
export const CLASS_TYPE_FAILURE_REASONS = Object.keys({
  missing_field: true,
  name_too_long: true,
  invalid_duration: true,
  invalid_capacity: true,
  description_too_long: true,
  name_taken: true,
} satisfies Record<ClassTypeFailure['reason'], true>) as readonly ClassTypeFailure['reason'][];
