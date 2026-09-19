import { createFailureMessages } from '../http/failure-messages';
import { LoginFailureReason } from './auth.models';

/**
 * What to tell someone whose sign-in was refused.
 *
 * <h2>Non-disclosure is the whole design of this table</h2>
 *
 * `invalid_credentials` covers a wrong password AND an address with no account, because the API
 * deliberately refuses to distinguish them. Saying "nie ma takiego konta" would hand an anonymous
 * caller a way to enumerate which e-mail addresses are members here. That is also why the login
 * screen is permanently a BANNER screen and never puts a refusal on a field: a message under the
 * e-mail box says which half was wrong just as loudly as the words would.
 * `login.spec.ts` pins it; AGENTS.md states the rule.
 */
const MESSAGES: Record<LoginFailureReason, string> = {
  invalid_credentials: 'Nieprawidłowy e-mail lub hasło.',
  blocked: 'Twoje konto zostało zablokowane. Skontaktuj się z obsługą siłowni.',
  // Declared but never produced: S-16 removed approval, so nothing creates a pending account. Kept
  // because the API still declares the literal (AuthEndpoints.LoginFailure) and the union mirrors
  // the API. It gets a sentence like every other reason — the table's rule is exhaustiveness, not
  // reachability, and an unreachable reason with no message is how a fallback ships to production.
  pending_approval: 'Twoje konto oczekuje na aktywację. Skontaktuj się z obsługą siłowni.',
};

/** The fallback for a refusal with no reason, or one this build does not know. */
const UNKNOWN = 'Nie udało się zalogować. Spróbuj ponownie za chwilę.';

/** The message for a sign-in refusal. See `core/http/failure-messages.ts` for the mechanics. */
export const loginFailureMessage = createFailureMessages(MESSAGES, UNKNOWN);
