import { createFailureMessages } from '../http/failure-messages';
import { contactFailureMessage } from '../auth/contact-failure';
import { MemberFailure } from './member-admin.models';

/**
 * What to tell the admin when the API refuses an edit to a member record.
 *
 * <h2>The five contact codes are not restated here</h2>
 *
 * They come from `ContactDetails.TryCreate`, the same helper `/profile` posts through, so they carry
 * the same five sentences — and `contact-failure.ts` is where those live. Copying them would be the
 * exact drift `validation.ts` was created to stop on the client-side halves of the same rules.
 * The table SPREADS that one, so this union stays exhaustive without owning a second copy.
 */
const MESSAGES: Record<MemberFailure['reason'], string> = {
  invalid_display_name: 'Podaj imię i nazwisko (maksymalnie 100 znaków).',
  conflict: 'Dane zmieniły się w międzyczasie. Odśwież i spróbuj ponownie.',
  invalid_phone: contactFailureMessage('invalid_phone'),
  invalid_street: contactFailureMessage('invalid_street'),
  invalid_house_number: contactFailureMessage('invalid_house_number'),
  invalid_postal_code: contactFailureMessage('invalid_postal_code'),
  invalid_city: contactFailureMessage('invalid_city'),
};

/** The fallback for a refusal with no reason, or one this build does not know. */
const UNKNOWN = 'Nie udało się zapisać. Spróbuj ponownie za chwilę.';

/** The message for a member-record refusal. */
export const memberFailureMessage = createFailureMessages(MESSAGES, UNKNOWN);
