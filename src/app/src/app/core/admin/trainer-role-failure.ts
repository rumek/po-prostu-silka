import { createFailureMessages } from '../http/failure-messages';
import { TrainerRoleFailure } from './member-admin.models';

/**
 * What to tell the admin when granting or revoking the trainer role is refused.
 *
 * <h2>Third person, no name</h2>
 *
 * These sentences used to interpolate the member's display name. They do not any more, for the
 * reason `booking-failure.ts` settled in S-16: a table keyed by a reason returns a sentence, and a
 * sentence that needs a name needs a second argument that only one of its three callers can supply.
 * The action is taken from that member's own row and reported the moment it finishes, so the person
 * is never in doubt — "ta osoba" is unambiguous where it is read.
 */
const MESSAGES: Record<TrainerRoleFailure['reason'], string> = {
  not_active: 'Ta osoba nie jest aktywna — rolę Trenera można nadać tylko aktywnemu kontu.',
  no_account: 'Ta osoba nie ma konta — rola Trenera wymaga logowania.',
  // Not a product rule: a genuine concurrency failure, typically a block that landed at the same
  // moment. The change did NOT happen and the account may now be in a different state entirely,
  // which is why the screen refetches rather than patching the row.
  failed: 'Nie udało się zmienić roli — dane tej osoby właśnie się zmieniły. Odświeżono listę.',
};

/** The fallback for a refusal with no reason, or one this build does not know. */
const UNKNOWN = 'Nie udało się zmienić roli. Spróbuj ponownie za chwilę.';

/** The message for a trainer-role refusal. */
export const trainerRoleFailureMessage = createFailureMessages(MESSAGES, UNKNOWN);
