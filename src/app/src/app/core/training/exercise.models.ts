/**
 * Mirrors the API's ExerciseSummary record (src/Application/Training/ExerciseEndpoints.cs).
 * Keep the two in step — this is a contract, not a convenience type.
 *
 * One shape serves the list, the detail screen and the edit form; the list simply ignores the fields
 * it does not render.
 */
export interface ExerciseSummary {
  id: string;
  name: string;

  /**
   * Every field below is absent as `null`, never as an empty string — the API normalises blank to
   * null on write, so "no instructions" has one representation everywhere.
   */
  description: string | null;

  muscleGroup: string | null;
  difficulty: string | null;
  equipment: string | null;
  preparation: string | null;
  startingPosition: string | null;
  execution: string | null;

  /**
   * The bare 11-character YouTube id, never a URL. The thumbnail and the player are both composed
   * from it client-side (see youtube.ts), so the API carries no derived URLs that could drift.
   */
  videoId: string | null;

  /** Whether the exercise is offered. A retired one is kept; there is no hard delete. */
  isActive: boolean;

  createdAt: string;
}

/**
 * Mirrors ExerciseRequest. Create and edit take the same shape; an edit replaces every field.
 *
 * It carries `videoUrl`, not a video id: the form sends whatever the admin pasted and the SERVER
 * parses it. A second parser here would eventually disagree with that one.
 *
 * `isActive` is deliberately absent — activation has its own two endpoints, so an edit cannot
 * resurrect an exercise the admin retired.
 */
export interface ExerciseRequest {
  name: string;
  description: string | null;
  muscleGroup: string | null;
  difficulty: string | null;
  equipment: string | null;
  preparation: string | null;
  startingPosition: string | null;
  execution: string | null;

  /** Any YouTube link shape, or `null` for "no video". */
  videoUrl: string | null;
}

/**
 * Mirrors ExerciseFailure. Every reason the API can refuse an exercise write.
 *
 * `name_taken` is the only 409 — a clash with existing state rather than bad input. It arrives from
 * create and edit, where the form maps it onto the name control, and ALSO from activate, whose
 * request carries no name at all: deactivating releases a name, so another exercise may have claimed
 * it meanwhile. The list reports that case as a row-level message, since there is no control to
 * attach it to.
 */
export interface ExerciseFailure {
  reason:
    | 'missing_field'
    | 'name_too_long'
    | 'description_too_long'
    | 'muscle_group_too_long'
    | 'difficulty_too_long'
    | 'equipment_too_long'
    | 'preparation_too_long'
    | 'starting_position_too_long'
    | 'execution_too_long'
    | 'invalid_video_url'
    | 'name_taken';
}

/**
 * The bounds the exercise form and the exercise message table both need.
 *
 * Moved out of `exercise-form.ts` in S-19 for the reason CLASS_TYPE_BOUNDS was: a refusal message
 * that quotes a limit the form owns privately drifts the first time that limit moves.
 *
 * Every length matches a HasMaxLength in ExerciseConfiguration AND the check in
 * ExerciseEndpoints.Validate — keep all three in step, since a client bound looser than the column
 * turns ordinary typing into a 500. `maxVideoUrl` is the one that guards no column: the server
 * stores the parsed id rather than the pasted link, so it only stops an absurd paste reaching the
 * parser (mirrors MaxVideoUrlLength in ExerciseEndpoints).
 */
export const EXERCISE_BOUNDS = {
  maxName: 200,
  maxDescription: 1000,
  maxMuscleGroup: 100,
  maxDifficulty: 50,
  maxEquipment: 200,
  maxPreparation: 2000,
  maxStartingPosition: 2000,
  maxExecution: 4000,
  maxVideoUrl: 2048,
} as const;

/**
 * Every reason in {@link ExerciseFailure}, as a value. See BOOKING_FAILURE_REASONS in
 * core/scheduling/booking.models.ts for why the object literal.
 */
export const EXERCISE_FAILURE_REASONS = Object.keys({
  missing_field: true,
  name_too_long: true,
  description_too_long: true,
  muscle_group_too_long: true,
  difficulty_too_long: true,
  equipment_too_long: true,
  preparation_too_long: true,
  starting_position_too_long: true,
  execution_too_long: true,
  invalid_video_url: true,
  name_taken: true,
} satisfies Record<ExerciseFailure['reason'], true>) as readonly ExerciseFailure['reason'][];
