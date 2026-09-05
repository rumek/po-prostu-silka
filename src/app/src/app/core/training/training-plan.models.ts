/**
 * Mirrors the API's training-plan records (src/Application/Training/TrainingPlanEndpoints.cs).
 * Keep the two in step — this is a contract, not a convenience type.
 */

/** Mirrors TrainingPlanSummary: one row of the trainer's list of active plans. */
export interface TrainingPlanSummary {
  id: string;
  name: string;
  memberUserId: string;
  memberDisplayName: string;
  assignedByDisplayName: string;
  createdAt: string;

  /** Counted by the server; the list renders a number, not the rows. */
  itemCount: number;
}

/**
 * Mirrors TrainingPlanItemView: one prescribed exercise as the API returns it.
 *
 * Every parameter is optional (`null`, never an empty string — the API normalises blank to null on
 * write). A trainer may prescribe an exercise with no numbers at all.
 */
export interface TrainingPlanItemView {
  id: string;
  exerciseId: string;
  exerciseName: string;

  /** The trainer's order, dense from 0. The API already sorts by it; nothing here re-sorts. */
  position: number;

  sets: number | null;

  /** Free text, not a number: "8-12", "do upadku", "12" are all legitimate (FR-015). */
  reps: string | null;

  weightKg: number | null;
  restSeconds: number | null;
  note: string | null;
}

/** Mirrors TrainingPlanDetail: one plan with its items, for the builder and the member's screen. */
export interface TrainingPlanDetail {
  id: string;
  name: string;
  memberUserId: string;
  memberDisplayName: string;
  assignedByDisplayName: string;
  createdAt: string;
  items: TrainingPlanItemView[];
}

/** Mirrors AssignableMember: the minimal read of who a plan may be assigned to. */
export interface AssignableMember {
  id: string;
  displayName: string;
}

/**
 * Mirrors TrainingPlanItemRequest.
 *
 * NO POSITION FIELD, and that is the contract: the ARRAY ORDER is the order. The server numbers the
 * items from the array it receives, which is what makes reordering a matter of moving an element
 * rather than renumbering every row.
 */
export interface TrainingPlanItemRequest {
  exerciseId: string;
  sets: number | null;
  reps: string | null;
  weightKg: number | null;
  restSeconds: number | null;
  note: string | null;
}

/**
 * Mirrors TrainingPlanRequest. Create and edit take the same shape; an edit replaces the name and
 * the ENTIRE item list.
 *
 * `memberUserId` is ignored by the edit endpoint — a plan cannot change hands, it is superseded.
 */
export interface TrainingPlanRequest {
  name: string;
  memberUserId: string;
  items: TrainingPlanItemRequest[];
}

/**
 * Mirrors TrainingPlanFailure. Every reason the API can refuse a plan write.
 *
 * Closed union rather than a string, for the reason ExerciseFailure is: the builder maps each reason
 * onto the control that owns it, and a `default` branch that silently swallows a new server reason
 * is how a form starts lying about what went wrong.
 */
export interface TrainingPlanFailure {
  reason:
    // 400 — bad input.
    | 'missing_field'
    | 'name_too_long'
    | 'no_items'
    | 'too_many_items'
    | 'invalid_sets'
    | 'reps_too_long'
    | 'invalid_weight'
    | 'invalid_rest'
    | 'note_too_long'
    | 'unknown_exercise'
    | 'inactive_exercise'
    | 'duplicate_exercise'
    // 409 — a clash with existing state rather than bad input.
    | 'member_not_found'
    | 'member_not_active'
    | 'member_changed'
    | 'conflict';
}
