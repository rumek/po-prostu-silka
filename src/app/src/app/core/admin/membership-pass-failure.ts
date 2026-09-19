import { createFailureMessages } from '../http/failure-messages';
import { MembershipPassFailure } from './member-admin.models';

/**
 * What to tell the admin when the API refuses a karnet action (S-16).
 *
 * <h2>A full Record, not a Partial</h2>
 *
 * Keyed by the reason union itself, so adding a reason to `MembershipPassFailure` fails the BUILD
 * here rather than falling through to a generic message. Every table in the app now follows this
 * rule; the `Partial` half-table this comment used to contrast against (`ADMIN_MESSAGES`) was
 * retired with self-service booking in S-16.
 *
 * <h2>Every message names what to do next</h2>
 *
 * These refusals all reach an admin standing at a desk with a member in front of them, so each says
 * what is in the way rather than merely that something failed.
 */
const MESSAGES: Record<MembershipPassFailure['reason'], string> = {
  member_blocked: 'Ta osoba jest zablokowana — najpierw ją odblokuj, potem wystaw karnet.',
  invalid_type_name: 'Podaj nazwę karnetu (maksymalnie 100 znaków).',
  invalid_range: 'Nieprawidłowy zakres dat — data końca nie może być wcześniejsza niż data startu.',
  invalid_entry_count:
    'Nieprawidłowa liczba wejść. Karnet musi mieć co najmniej jedno wejście i nie może mieć ich mniej, niż już wykorzystano.',
  overlapping_pass:
    'Ta osoba ma już karnet obejmujący część tego okresu. Karnety nie mogą się nakładać.',
  has_active_bookings:
    'Z tego karnetu opłacono aktywne zapisy. Najpierw wypisz osobę z tych zajęć, potem usuń karnet.',
  // Not a product rule: the server lost an optimistic race. Refetching is genuinely the right advice.
  conflict: 'Ktoś właśnie zmienił dane tej osoby. Odśwież listę i spróbuj ponownie.',
};

/** The fallback for a refusal with no reason, or one this build does not know. */
const UNKNOWN = 'Nie udało się zapisać karnetu. Spróbuj ponownie za chwilę.';

/**
 * The message for a refusal reason.
 *
 * Built by the shared factory, which owns the `Object.hasOwn` guard and the `unknown`
 * parameter type — see `core/http/failure-messages.ts`.
 */
export const membershipPassFailureMessage = createFailureMessages(MESSAGES, UNKNOWN);
