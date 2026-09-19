import { createFailureMessages } from '../http/failure-messages';
import { AccessCodeFailure } from './member-admin.models';

/**
 * What to tell the admin when issuing an invitation code is refused (S-14, AM-004).
 *
 * All three are 409s and all three mean the list is stale, so the screen refetches in every case —
 * the words are what differ, because two of them name something the admin can act on and the third
 * does not.
 *
 * Third person rather than the member's name, for the reason set out in `trainer-role-failure.ts`.
 */
const MESSAGES: Record<AccessCodeFailure['reason'], string> = {
  has_account: 'Ta osoba ma już konto — kod nie jest potrzebny.',
  member_blocked: 'Ta osoba jest zablokowana — odblokuj ją, zanim wydasz kod.',
  conflict: 'Lista była nieaktualna — odświeżono.',
};

/** The fallback for a refusal with no reason, or one this build does not know. */
const UNKNOWN = 'Nie udało się wydać kodu. Spróbuj ponownie za chwilę.';

/** The message for an access-code refusal. */
export const accessCodeFailureMessage = createFailureMessages(MESSAGES, UNKNOWN);
