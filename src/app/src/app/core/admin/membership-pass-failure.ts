import { MembershipPassFailure } from './member-admin.models';

/**
 * What to tell the admin when the API refuses a karnet action (S-16).
 *
 * <h2>A full Record, not a Partial</h2>
 *
 * Keyed by the reason union itself, so adding a reason to `MembershipPassFailure` fails the BUILD
 * here rather than falling through to a generic message. `booking-failure.ts`'s `ADMIN_MESSAGES` is
 * a `Partial` for a reason that does not apply to this file — it deliberately restates only the
 * person-relative half of a table it shares — and that shape is a known soft spot precisely because
 * a new reason slips through it silently. This one does not repeat it.
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
 * Takes `unknown` rather than the union so callers can hand over whatever came off the wire: a
 * server one version ahead can name a reason this build has never heard of, and that has to read as
 * a message rather than as `undefined`.
 */
export function membershipPassFailureMessage(reason: unknown): string {
  // `hasOwn`, not `in`: `in` walks the prototype chain, so a server reason of "constructor" or
  // "toString" would return a FUNCTION typed as string and render as source text.
  return typeof reason === 'string' && Object.hasOwn(MESSAGES, reason)
    ? MESSAGES[reason as MembershipPassFailure['reason']]
    : UNKNOWN;
}
