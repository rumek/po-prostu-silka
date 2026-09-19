import { createFailureMessages } from '../http/failure-messages';
import { EXERCISE_BOUNDS, ExerciseFailure } from './exercise.models';

/**
 * What to tell the admin when the API refuses an exercise write.
 *
 * <h2>The eight length refusals do not share a sentence</h2>
 *
 * They could — every one of them means "shorter, please" — but the exercise form has eight text
 * fields, and a single generic sentence under one of them would be indistinguishable from the same
 * sentence under any other. Each names its own field and its own bound, which is what makes the
 * message useful when it appears alone in a banner with no control beside it.
 *
 * `name_taken` reaches the form (on the name control) and the list's activate action (no control at
 * all — deactivating released the name and something else took it). One sentence serves both.
 */
const MESSAGES: Record<ExerciseFailure['reason'], string> = {
  missing_field: 'Uzupełnij nazwę ćwiczenia.',
  name_too_long: `Nazwa może mieć najwyżej ${EXERCISE_BOUNDS.maxName} znaków.`,
  description_too_long: `Opis może mieć najwyżej ${EXERCISE_BOUNDS.maxDescription} znaków.`,
  muscle_group_too_long: `Partia mięśniowa może mieć najwyżej ${EXERCISE_BOUNDS.maxMuscleGroup} znaków.`,
  difficulty_too_long: `Poziom trudności może mieć najwyżej ${EXERCISE_BOUNDS.maxDifficulty} znaków.`,
  equipment_too_long: `Sprzęt może mieć najwyżej ${EXERCISE_BOUNDS.maxEquipment} znaków.`,
  preparation_too_long: `Przygotowanie może mieć najwyżej ${EXERCISE_BOUNDS.maxPreparation} znaków.`,
  starting_position_too_long: `Pozycja startowa może mieć najwyżej ${EXERCISE_BOUNDS.maxStartingPosition} znaków.`,
  execution_too_long: `Wykonanie może mieć najwyżej ${EXERCISE_BOUNDS.maxExecution} znaków.`,
  invalid_video_url: 'Podaj poprawny link do filmu na YouTube.',
  name_taken: 'Ta nazwa jest już zajęta przez inne aktywne ćwiczenie. Wybierz inną.',
};

/** The fallback for a refusal with no reason, or one this build does not know. */
const UNKNOWN = 'Nie udało się zapisać ćwiczenia. Spróbuj ponownie za chwilę.';

/** The message for an exercise refusal. */
export const exerciseFailureMessage = createFailureMessages(MESSAGES, UNKNOWN);
