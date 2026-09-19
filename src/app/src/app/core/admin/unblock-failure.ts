import { createFailureMessages } from '../http/failure-messages';
import { UnblockFailure } from './member-admin.models';

/**
 * What to tell the admin when unblocking a member is refused.
 *
 * ONE REASON, AND IT STILL GETS A TABLE. There is nothing here a screen could not have inlined —
 * that is exactly why it is worth writing down. The rule S-19 establishes is that every `*Failure`
 * union has a table, checked by `core/http/failure-contract.spec.ts`; an exception "because it is
 * only one reason" is how the next union with two reasons also skips it.
 *
 * There is no `not_blocked`: since S-14 membership has two states, so "not blocked" is "already
 * active", and the API reports that no-op as success rather than as an error.
 */
const MESSAGES: Record<UnblockFailure['reason'], string> = {
  conflict: 'Lista była nieaktualna — odświeżono.',
};

/** The fallback for a refusal with no reason, or one this build does not know. */
const UNKNOWN = 'Nie udało się odblokować tej osoby. Spróbuj ponownie za chwilę.';

/** The message for an unblock refusal. */
export const unblockFailureMessage = createFailureMessages(MESSAGES, UNKNOWN);
