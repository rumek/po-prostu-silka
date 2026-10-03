import { createFailureMessages } from '../http/failure-messages';
import { RosterFailure } from './roster.models';

/**
 * The words for every refused roster write (S-37). Phrased for staff, about the member, like the
 * booking table. A screen decides where a refusal goes; it never writes the sentence (S-19).
 */
const MESSAGES: Record<RosterFailure['reason'], string> = {
  already_in_roster: 'Ta osoba jest już w składzie tej grupy.',
  roster_full: 'Skład jest pełny — nie zmieści się więcej osób niż miejsc w grupie.',
  member_blocked: 'Ta osoba jest zablokowana i nie może dołączyć do składu.',
  member_is_staff: 'Trenerzy i administratorzy nie należą do składów grup.',
  inactive_class_group: 'Ta grupa jest nieaktywna — nie przyjmuje nowych osób.',
  conflict: 'Ktoś właśnie zmienił ten skład. Spróbuj ponownie.',
};

const UNKNOWN = 'Nie udało się zmienić składu. Spróbuj ponownie za chwilę.';

export const rosterFailureMessage = createFailureMessages(MESSAGES, UNKNOWN);
