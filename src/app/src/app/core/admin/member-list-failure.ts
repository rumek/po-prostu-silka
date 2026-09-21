import { createFailureMessages } from '../http/failure-messages';
import { MemberListFailure } from './member-admin.models';

/**
 * What to tell someone when the member-list read itself is refused (S-21).
 *
 * THE EIGHTEENTH TABLE, AND — like `schedule-read-failure.ts` — ONE NOTHING RENDERS TODAY. Both
 * reasons mean the client asked for a page or a phrase the API will not serve, which the SPA never
 * does: the members screen clamps its URL and the bookings picker sends a fixed page size. So both
 * callers fall back to their own "failed to load" state instead. The table exists because every
 * `*Failure` union has one (`core/http/failure-contract.spec.ts`), so a screen that one day narrows
 * on these finds the words already written.
 */
const MESSAGES: Record<MemberListFailure['reason'], string> = {
  invalid_page: 'Nie udało się wczytać tej strony listy członków. Wróć na pierwszą stronę.',
  invalid_search: 'Fraza wyszukiwania jest za długa — skróć ją do 100 znaków.',
};

/** The fallback for a refusal with no reason, or one this build does not know. */
const UNKNOWN = 'Nie udało się wczytać listy członków. Spróbuj ponownie za chwilę.';

/** The message for a member-list read refusal. */
export const memberListFailureMessage = createFailureMessages(MESSAGES, UNKNOWN);
