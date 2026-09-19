import { createFailureMessages } from '../http/failure-messages';
import { RegisterFailureReason } from './auth.models';
import { MIN_PASSWORD_LENGTH } from './validation';

/**
 * What to tell someone whose registration was refused.
 *
 * <h2>Deliberately asymmetric with login</h2>
 *
 * `email_taken` names the problem outright (S-01 D3), where login refuses to say whether an address
 * exists. That is not an inconsistency: with no e-mail-confirmation flow, silence here would strand
 * a member who forgot they had already signed up, and the person typing is the owner of the address
 * either way.
 *
 * <h2>The two code refusals say the same thing on purpose</h2>
 *
 * `unknown_member_code` already collapses "no such code", "expired", "revoked" and "already used"
 * server-side, precisely so nothing confirms to a stranger that a code once existed. Splitting the
 * wording between it and `invalid_member_code` would undo that in the UI.
 */
const MESSAGES: Record<RegisterFailureReason, string> = {
  email_taken: 'Ten adres e-mail jest już zajęty. Zaloguj się albo zresetuj hasło.',
  invalid_email: 'Podaj poprawny adres e-mail.',
  invalid_password: `Hasło musi mieć co najmniej ${MIN_PASSWORD_LENGTH} znaków.`,
  invalid_member_code: 'Ten link z zaproszeniem jest nieprawidłowy. Poproś siłownię o nowy.',
  unknown_member_code: 'Ten link z zaproszeniem jest nieprawidłowy. Poproś siłownię o nowy.',
  // The API's own catch-all for an Identity error code it does not recognise — the codes are an
  // open set, so this reason is the server saying "something about this was rejected" and the
  // sentence can be no more specific than the answer is.
  invalid_registration: 'Nie udało się utworzyć konta. Sprawdź dane i spróbuj ponownie.',
};

/** The fallback for a refusal with no reason, or one this build does not know. */
const UNKNOWN = 'Nie udało się utworzyć konta. Spróbuj ponownie za chwilę.';

/** The message for a registration refusal. */
export const registerFailureMessage = createFailureMessages(MESSAGES, UNKNOWN);
