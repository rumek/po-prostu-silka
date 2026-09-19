import { createFailureMessages } from '../http/failure-messages';
import { ContactFailureReason } from './auth.models';

/**
 * The five contact-detail refusals, in the words three screens already use.
 *
 * <h2>One table for three screens, because it is one helper on the server</h2>
 *
 * `/profile`, the admin's member form and (until S-17) registration all write the same five columns
 * through `ContactDetails.TryCreate`, so they all receive the same five codes. `validation.ts`
 * already exists because the client-side halves of these rules drifted between two of those screens
 * once; this is the same argument applied to the sentences.
 *
 * <h2>These are also the client-side validation messages</h2>
 *
 * The forms check the same rules before submitting, and the sentence a member reads must not depend
 * on whether the browser or the API caught it. Templates take their field text from here too.
 */
const MESSAGES: Record<ContactFailureReason, string> = {
  invalid_phone: 'Podaj numer telefonu w formacie 123 456 789.',
  invalid_street: 'Podaj nazwę ulicy.',
  invalid_house_number: 'Podaj numer domu lub mieszkania.',
  invalid_postal_code: 'Podaj kod pocztowy w formacie 00-000.',
  invalid_city: 'Podaj miejscowość.',
};

/** The fallback for a refusal with no reason, or one this build does not know. */
const UNKNOWN = 'Nie udało się zapisać danych. Spróbuj ponownie za chwilę.';

/**
 * The message for a contact-detail refusal.
 *
 * Exported as `profileFailureMessage` as well, since `ProfileFailure['reason']` IS
 * `ContactFailureReason` — one union, one table, two names only because the endpoints differ.
 */
export const contactFailureMessage = createFailureMessages(MESSAGES, UNKNOWN);

/** @see contactFailureMessage — the profile endpoint answers with exactly this union. */
export const profileFailureMessage = contactFailureMessage;
