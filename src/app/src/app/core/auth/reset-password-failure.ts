import { createFailureMessages } from '../http/failure-messages';
import { ResetPasswordFailureReason } from './auth.models';
import { MIN_PASSWORD_LENGTH } from './validation';

/**
 * What to tell someone whose password reset was refused.
 *
 * `invalid_token` deliberately covers an unknown address, a malformed token, one belonging to
 * somebody else, an already-used one AND an expired one. The API refuses to distinguish them, so
 * this must not imply it can — "wygasł" would tell an anonymous caller the address is registered.
 * The screen does not put it on a control either: no field the member can edit would fix a spent
 * link, and the only way forward is a new one.
 */
const MESSAGES: Record<ResetPasswordFailureReason, string> = {
  invalid_token:
    'Ten link do zmiany hasła jest nieprawidłowy lub został już użyty. Poproś o nowy link.',
  invalid_new_password: `Hasło musi mieć co najmniej ${MIN_PASSWORD_LENGTH} znaków.`,
};

/** The fallback for a refusal with no reason, or one this build does not know. */
const UNKNOWN = 'Nie udało się ustawić nowego hasła. Spróbuj ponownie za chwilę.';

/** The message for a password-reset refusal. */
export const resetPasswordFailureMessage = createFailureMessages(MESSAGES, UNKNOWN);
