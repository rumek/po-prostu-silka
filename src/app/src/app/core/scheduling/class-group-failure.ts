import { createFailureMessages } from '../http/failure-messages';
import { CLASS_GROUP_BOUNDS, ClassGroupFailure } from './class-group.models';

/**
 * What to tell the admin when the API refuses a class-group write.
 *
 * <h2>`name_taken` reaches two surfaces and now says one thing</h2>
 *
 * It arrives from create and edit, where the form puts it on the name control, and ALSO from
 * activate, whose request carries no name at all — deactivating releases a name, so another type may
 * have claimed it meanwhile. The list screen used to write its own, longer sentence for that second
 * case. Both now read this one: it has to work under a field and on its own, so it names the clash
 * without assuming a control is next to it.
 */
const MESSAGES: Record<ClassGroupFailure['reason'], string> = {
  missing_field: 'Uzupełnij wszystkie wymagane pola.',
  name_too_long: `Nazwa może mieć najwyżej ${CLASS_GROUP_BOUNDS.maxName} znaków.`,
  invalid_duration: `Domyślny czas trwania musi mieścić się w zakresie ${CLASS_GROUP_BOUNDS.minDuration}–${CLASS_GROUP_BOUNDS.maxDuration} minut.`,
  invalid_capacity: `Domyślna liczba miejsc musi mieścić się w zakresie ${CLASS_GROUP_BOUNDS.minCapacity}–${CLASS_GROUP_BOUNDS.maxCapacity}.`,
  description_too_long: `Opis może mieć najwyżej ${CLASS_GROUP_BOUNDS.maxDescription} znaków.`,
  name_taken: 'Ta nazwa jest już zajęta przez inną aktywną grupę. Wybierz inną.',
};

/** The fallback for a refusal with no reason, or one this build does not know. */
const UNKNOWN = 'Nie udało się zapisać grupy. Spróbuj ponownie za chwilę.';

/** The message for a class-group refusal. */
export const classGroupFailureMessage = createFailureMessages(MESSAGES, UNKNOWN);
