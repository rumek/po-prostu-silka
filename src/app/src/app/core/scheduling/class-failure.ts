import { ClassFailure } from './class.models';

/**
 * What to tell the admin when the API refuses a class write.
 *
 * <h2>Why this is a shared table and not two switch statements</h2>
 *
 * Since S-07 there are TWO surfaces that create a class: the full form (`class-form`) and the
 * calendar's drag-to-create overlay (prd-v2 FR-019). Both receive the same eleven `ClassFailure`
 * reasons. Two independent switches over eleven reasons is how the same refusal comes to read one
 * way in one place and another way in the other — and the drift is invisible until someone hits the
 * same error twice in one session.
 *
 * <h2>What is NOT here</h2>
 *
 * Mapping a reason onto a specific FORM CONTROL. `class-form` puts `time_conflict` on its start-time
 * field and `instructor_not_trainer` on its instructor select; the overlay has a different shape and
 * different controls. That mapping is form-specific and stays in each form. Only the words are
 * shared.
 *
 * <h2>Exhaustiveness</h2>
 *
 * The `Record` is keyed by the reason union itself, so adding a reason to `ClassFailure` fails the
 * build here rather than falling through to a generic message. `invalid_range` is deliberately
 * absent from that union — it is a read-path refusal, modelled as `ScheduleReadFailure` — so no
 * entry for it exists or should.
 */
const MESSAGES: Record<ClassFailure['reason'], string> = {
  missing_field: 'Wybierz typ zajęć i prowadzącego.',
  invalid_capacity: 'Liczba miejsc musi mieścić się w zakresie 1–200.',
  invalid_duration: 'Czas trwania musi mieścić się w zakresie 1–480 minut.',
  starts_in_past: 'Nie można zaplanować zajęć w przeszłości.',
  invalid_weeks: 'Liczba tygodni musi mieścić się w zakresie 1–8.',
  time_conflict: 'O tej porze są już inne zajęcia. Wybierz inny termin.',
  // All three mean the same thing to the admin: this type cannot be used for this class.
  unknown_class_type: 'Nie można użyć tego typu zajęć. Odśwież stronę i spróbuj ponownie.',
  inactive_class_type: 'Nie można użyć tego typu zajęć. Odśwież stronę i spróbuj ponownie.',
  class_type_immutable: 'Nie można użyć tego typu zajęć. Odśwież stronę i spróbuj ponownie.',
  // Likewise these two: the selection only ever offers active trainers, so either means the list is
  // stale and "pick someone else" is the whole of the useful advice.
  unknown_instructor: 'Wybierz prowadzącego z listy aktywnych trenerów.',
  instructor_not_trainer: 'Wybierz prowadzącego z listy aktywnych trenerów.',
};

/** The fallback for a refusal with no reason, or one this build does not know. */
const UNKNOWN = 'Nie udało się zapisać zajęć. Spróbuj ponownie za chwilę.';

/**
 * The message for a refusal reason.
 *
 * Takes `unknown` rather than the union so callers can hand over whatever came off the wire: a
 * server one version ahead can name a reason this build has never heard of, and that has to read as
 * a message rather than as `undefined`.
 */
export function classFailureMessage(reason: unknown): string {
  return typeof reason === 'string' && reason in MESSAGES
    ? MESSAGES[reason as ClassFailure['reason']]
    : UNKNOWN;
}
