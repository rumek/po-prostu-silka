import { createFailureMessages } from '../http/failure-messages';
import { ChangePasswordFailureReason } from './auth.models';
import { MIN_PASSWORD_LENGTH } from './validation';

/**
 * What to tell a signed-in member whose password change was refused.
 *
 * Both reasons land on a control rather than in a banner, and naming which password was wrong leaks
 * nothing: the caller already proved they own the session. That is the difference between a form the
 * member can fix and a dead end — and the reason this table is not modelled on login's.
 */
const MESSAGES: Record<ChangePasswordFailureReason, string> = {
  invalid_current_password: 'Obecne hasło jest nieprawidłowe.',
  invalid_new_password: `Hasło musi mieć co najmniej ${MIN_PASSWORD_LENGTH} znaków.`,
};

/** The fallback for a refusal with no reason, or one this build does not know. */
const UNKNOWN = 'Nie udało się zmienić hasła. Spróbuj ponownie za chwilę.';

/** The message for a password-change refusal. */
export const changePasswordFailureMessage = createFailureMessages(MESSAGES, UNKNOWN);
