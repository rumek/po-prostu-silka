/**
 * Mirrors the API's training-plan records (src/Application/Training/TrainingPlanEndpoints.cs).
 * Keep the two in step — this is a contract, not a convenience type.
 */

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

  /**
   * How long the exercise itself is held, in seconds (S-15). The counterpart to `weightKg`, not to
   * `reps`: a plank is prescribed in time rather than in load. Per-plan, like every other parameter
   * here — the same exercise is 45 s in one plan and 60 s in another.
   */
  durationSeconds: number | null;

  /**
   * The exercise's muscle group, for the member's plan card. READ-ONLY — it is a fact about the
   * exercise, not part of the prescription, which is why it has no counterpart on
   * `TrainingPlanItemRequest`. Null when the library entry carries none.
   */
  muscleGroup: string | null;
}

/** Mirrors TrainingPlanDetail: one plan with its items, for the builder and the member's screen. */
export interface TrainingPlanDetail {
  id: string;
  name: string;
  memberId: string;
  memberDisplayName: string;
  assignedByDisplayName: string;
  createdAt: string;
  items: TrainingPlanItemView[];
}

/**
 * Mirrors AssignableMember: the trainer-safe identity of a member — id, name, and whether they can
 * sign in. Since S-22 also the member half of {@link MemberPlan}, which may name a BLOCKED member
 * when an admin opens their plan.
 */
export interface AssignableMember {
  /** The MEMBER's id (S-14) — which is what lets someone with no login be offered here at all. */
  id: string;

  displayName: string;

  /**
   * Whether they can sign in. The picker says so, because a plan assigned to someone with no account
   * is real work they will not see in the app until they claim their record with a member code.
   */
  hasAccount: boolean;
}

/**
 * Mirrors MemberPlan (S-22): a member and their active plan, what the builder loads when it is
 * reached through the member. `plan` is null for a member with none — an ordinary state, answered
 * with an empty builder rather than an error.
 */
export interface MemberPlan {
  member: AssignableMember;
  plan: TrainingPlanDetail | null;
}

/**
 * Mirrors TrainerMemberSummary (S-22): one row of the trainer's member list. Deliberately no e-mail,
 * status or account id — the API never sends them to a trainer.
 */
export interface TrainerMember {
  id: string;
  displayName: string;
  hasAccount: boolean;

  /** The ACTIVE plan's name, or null when the member has none. */
  planName: string | null;
}

/**
 * Mirrors the API's `PagedResult<TrainerMemberSummary>` (src/Application/Paging/PagedResult.cs), as
 * `MemberPage` mirrors the admin's. Keep the two in step. `page` is 1-based; a page past the end
 * arrives as an empty `items` with the TRUE `total`.
 */
export interface TrainerMemberPage {
  items: TrainerMember[];
  total: number;
  page: number;
  pageSize: number;
}

/** What the caller asks the trainer's member list for. Absent or empty fields are left off. */
export interface TrainerMemberQuery {
  search?: string;
  page?: number;
  pageSize?: number;
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

  /** Seconds the exercise is held (S-15). No `muscleGroup` counterpart: a prescription may not
   * edit the library. */
  durationSeconds: number | null;
}

/**
 * Mirrors TrainingPlanRequest. Create and edit take the same shape; an edit replaces the name and
 * the ENTIRE item list.
 *
 * `memberId` is VALIDATED on edit, not ignored: the server compares it against the plan and
 * refuses a mismatch with 409 `member_changed`. A plan cannot change hands — it is superseded — and
 * refusing tells a stale tab its state is old instead of silently accepting a write it misunderstood.
 * Since S-22 the builder sends the member id from its URL, so on edit that refusal is also the check
 * that the URL and the plan agree.
 */
export interface TrainingPlanRequest {
  name: string;
  memberId: string;
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
    | 'invalid_duration'
    | 'note_too_long'
    | 'unknown_exercise'
    | 'inactive_exercise'
    | 'duplicate_exercise'
    // 409 — a clash with existing state rather than bad input.
    | 'member_not_found'
    | 'member_not_active'
    | 'member_is_staff'
    | 'member_changed'
    | 'conflict';
}

/**
 * The bounds the plan builder and the plan message table both need.
 *
 * Moved out of `plan-builder.ts` in S-19 for the reason CLASS_TYPE_BOUNDS and EXERCISE_BOUNDS were:
 * fourteen of this union's seventeen refusal sentences quote one of these numbers, and a sentence
 * that quotes a limit the form owns privately drifts the first time that limit moves.
 *
 * Every bound matches a constant in TrainingPlanEndpoints, which in turn matches a column in
 * TrainingPlanConfiguration / TrainingPlanItemConfiguration. Keep all three in step — a client bound
 * looser than the server's turns ordinary typing into an unexplained 400.
 */
export const TRAINING_PLAN_BOUNDS = {
  maxName: 120,
  maxReps: 50,
  maxNote: 500,
  maxItems: 50,
  minSets: 1,
  maxSets: 20,
  minRest: 0,
  maxRest: 3600,
  // ONE, NOT ZERO — the single place duration does not mirror rest. A zero-second rest is a real
  // prescription ("straight into the next set"); a zero-second exercise is a slip. Mirrors
  // MinDurationSeconds/MaxDurationSeconds in TrainingPlanEndpoints.
  minDuration: 1,
  maxDuration: 3600,
  minWeight: 0,
  maxWeight: 999.99,
} as const;

/**
 * Every reason in {@link TrainingPlanFailure}, as a value. See BOOKING_FAILURE_REASONS in
 * core/scheduling/booking.models.ts for why the object literal.
 */
export const TRAINING_PLAN_FAILURE_REASONS = Object.keys({
  missing_field: true,
  name_too_long: true,
  no_items: true,
  too_many_items: true,
  invalid_sets: true,
  reps_too_long: true,
  invalid_weight: true,
  invalid_rest: true,
  invalid_duration: true,
  note_too_long: true,
  unknown_exercise: true,
  inactive_exercise: true,
  duplicate_exercise: true,
  member_not_found: true,
  member_not_active: true,
  member_is_staff: true,
  member_changed: true,
  conflict: true,
} satisfies Record<
  TrainingPlanFailure['reason'],
  true
>) as readonly TrainingPlanFailure['reason'][];
