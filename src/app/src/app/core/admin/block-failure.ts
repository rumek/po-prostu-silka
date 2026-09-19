import { createFailureMessages } from '../http/failure-messages';
import { BlockFailure } from './member-admin.models';

/** What to tell the admin when blocking a member is refused. */
const MESSAGES: Record<BlockFailure['reason'], string> = {
  is_admin: 'Ta osoba zarządza klubem i nie może zostać zablokowana.',
  // Someone changed the row underneath us, so the list is stale and is refetched rather than
  // patched. Not a product rule — hence "odświeżono" rather than anything the admin did wrong.
  conflict: 'Lista była nieaktualna — odświeżono.',
};

/** The fallback for a refusal with no reason, or one this build does not know. */
const UNKNOWN = 'Nie udało się zablokować tej osoby. Spróbuj ponownie za chwilę.';

/** The message for a block refusal. */
export const blockFailureMessage = createFailureMessages(MESSAGES, UNKNOWN);
