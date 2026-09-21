import { createFailureMessages } from '../http/failure-messages';
import { MemberListFailure } from './member-admin.models';

/**
 * What to tell someone when the member-list read itself is refused (S-21).
 *
 * THE EIGHTEENTH TABLE. Both reasons mean the client asked for a page or a phrase the API will not
 * serve, which the SPA never does: the members screen and the trainer's member list (S-22) clamp
 * their URLs, and the bookings picker sends a fixed page size. The first two callers fall back to
 * their own "failed to load" state; the trainer's list (S-22) is the third caller, and the first to
 * render these words, since `/api/trainer/members` answers with the same `MemberListFailure`. The
 * table exists because every `*Failure` union has one (`core/http/failure-contract.spec.ts`).
 */
const MESSAGES: Record<MemberListFailure['reason'], string> = {
  invalid_page: 'Nie udało się wczytać tej strony listy członków. Wróć na pierwszą stronę.',
  invalid_search: 'Fraza wyszukiwania jest za długa — skróć ją do 100 znaków.',
};

/** The fallback for a refusal with no reason, or one this build does not know. */
const UNKNOWN = 'Nie udało się wczytać listy członków. Spróbuj ponownie za chwilę.';

/** The message for a member-list read refusal. */
export const memberListFailureMessage = createFailureMessages(MESSAGES, UNKNOWN);
