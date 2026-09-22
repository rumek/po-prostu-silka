import { createFailureMessages } from '../http/failure-messages';
import { TRAINING_PLAN_BOUNDS, TrainingPlanFailure } from './training-plan.models';

/**
 * What to tell the trainer when the API refuses a plan write. Seventeen reasons — the largest union
 * in the app.
 *
 * <h2>The per-item reasons carry no index, and the wording admits it</h2>
 *
 * The API refuses on the first offending item without saying which, so a sentence cannot point at a
 * row. Every one of these is already blocked by a client validator too, which means reaching one of
 * them at all says the client and server definitions have drifted. So each says what to look for
 * rather than pretending to know where it is.
 */
const MESSAGES: Record<TrainingPlanFailure['reason'], string> = {
  // 400 — bad input.
  missing_field: 'Uzupełnij nazwę planu.',
  name_too_long: `Nazwa planu może mieć najwyżej ${TRAINING_PLAN_BOUNDS.maxName} znaków.`,
  no_items: 'Dodaj przynajmniej jedno ćwiczenie do planu.',
  too_many_items: `Plan może mieć najwyżej ${TRAINING_PLAN_BOUNDS.maxItems} ćwiczeń.`,
  invalid_sets: `Liczba serii musi mieścić się w zakresie ${TRAINING_PLAN_BOUNDS.minSets}–${TRAINING_PLAN_BOUNDS.maxSets}.`,
  reps_too_long: `Zapis powtórzeń może mieć najwyżej ${TRAINING_PLAN_BOUNDS.maxReps} znaków.`,
  invalid_weight: `Ciężar musi mieścić się w zakresie ${TRAINING_PLAN_BOUNDS.minWeight}–${TRAINING_PLAN_BOUNDS.maxWeight} kg.`,
  invalid_rest: `Przerwa musi mieścić się w zakresie ${TRAINING_PLAN_BOUNDS.minRest}–${TRAINING_PLAN_BOUNDS.maxRest} sekund.`,
  invalid_duration: `Czas musi mieścić się w zakresie ${TRAINING_PLAN_BOUNDS.minDuration}–${TRAINING_PLAN_BOUNDS.maxDuration} sekund.`,
  note_too_long: `Notatka może mieć najwyżej ${TRAINING_PLAN_BOUNDS.maxNote} znaków.`,
  // These two read identically on purpose: the trainer's next move is the same either way — reload
  // and rebuild — and telling them apart would only say whether the exercise was deleted or merely
  // retired, which changes nothing they can do.
  unknown_exercise:
    'Któreś z wybranych ćwiczeń zostało w międzyczasie zmienione lub dezaktywowane. Odśwież stronę i złóż plan ponownie.',
  inactive_exercise:
    'Któreś z wybranych ćwiczeń zostało w międzyczasie zmienione lub dezaktywowane. Odśwież stronę i złóż plan ponownie.',
  duplicate_exercise: 'To samo ćwiczenie występuje w planie dwa razy. Usuń jedno z powtórzeń.',

  // 409 — a clash with existing state rather than bad input.
  //
  // Likewise a deliberate pair: whether the member was removed or is blocked, no plan can be
  // created for them. Since S-22 the member is fixed by the URL, so the words point back to the
  // member list rather than at a picker that no longer exists.
  member_not_found: 'Tej osobie nie można teraz przypisać planu. Wróć do listy członków.',
  member_not_active: 'Tej osobie nie można teraz przypisać planu. Wróć do listy członków.',
  // S-25: a different sentence from the pair above on purpose — the person is active and exists,
  // and "try later" would be wrong advice. Staff hold no plan.
  member_is_staff: 'Planu treningowego nie przypisuje się trenerom ani administratorom.',
  member_changed:
    'Ten plan należy do innego członka, niż pokazuje ta strona. Odśwież ją i spróbuj ponownie.',
  conflict: 'Ktoś zmieniał ten plan w tej samej chwili. Odśwież stronę i spróbuj ponownie.',
};

/** The fallback for a refusal with no reason, or one this build does not know. */
const UNKNOWN = 'Nie udało się zapisać planu. Spróbuj ponownie za chwilę.';

/** The message for a training-plan refusal. */
export const trainingPlanFailureMessage = createFailureMessages(MESSAGES, UNKNOWN);
