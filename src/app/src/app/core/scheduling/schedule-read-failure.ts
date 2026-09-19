import { createFailureMessages } from '../http/failure-messages';
import { ScheduleReadFailure } from './class.models';

/**
 * What to tell someone when the schedule read itself is refused.
 *
 * THE SEVENTEENTH TABLE, AND THE ONE NOTHING RENDERS TODAY. `invalid_range` means the client asked
 * for a window the API will not serve — a client bug rather than anything the member did — so both
 * schedule screens fall back to their generic "failed to load" state instead of showing this. The
 * table exists because the rule S-19 establishes is that every `*Failure` union has one
 * (`core/http/failure-contract.spec.ts` enforces it); a screen that one day wants to narrow on this
 * then finds the words already written rather than inventing a tenth mechanism for them.
 */
const MESSAGES: Record<ScheduleReadFailure['reason'], string> = {
  invalid_range: 'Nie udało się wczytać grafiku dla tego zakresu dat. Odśwież stronę.',
};

/** The fallback for a refusal with no reason, or one this build does not know. */
const UNKNOWN = 'Nie udało się wczytać grafiku. Spróbuj ponownie za chwilę.';

/** The message for a schedule-read refusal. */
export const scheduleReadFailureMessage = createFailureMessages(MESSAGES, UNKNOWN);
