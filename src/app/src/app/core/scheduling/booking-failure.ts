import { BookingFailure } from './booking.models';

/**
 * What to tell the STAFF member when the API refuses a booking or a release.
 *
 * <h2>There is no member-facing half any more</h2>
 *
 * This table used to serve two audiences: a member acting on their own spot, and an admin acting on
 * somebody else's. S-16 removed the first — MP-01 retired self-service booking — so every message
 * here is now read by a person acting FOR someone else, and every one of them is phrased that way.
 * That also retires `ADMIN_MESSAGES`, the partial second table that existed solely to restate the
 * two person-relative reasons; with one audience there is nothing to restate.
 *
 * <h2>Exhaustiveness</h2>
 *
 * The `Record` is keyed by the reason union itself, so adding a reason to `BookingFailure` fails the
 * build here rather than falling through to a generic message. `membership-pass-failure.ts` follows
 * the same rule, deliberately.
 */
const MESSAGES: Record<BookingFailure['reason'], string> = {
  // Most of these are something that changed while the caller was looking at a stale screen, so each
  // says what happened rather than what they did wrong.
  class_cancelled: 'Te zajęcia zostały odwołane.',
  class_started: 'Te zajęcia już się rozpoczęły — zapisy są zamknięte.',
  already_booked: 'Ta osoba jest już zapisana na te zajęcia.',
  class_full: 'Brak wolnych miejsc na tych zajęciach.',
  member_blocked: 'Ta osoba jest zablokowana i nie może być zapisana na zajęcia.',
  // The two karnet refusals (S-16). Kept apart because they are different conversations at the desk:
  // one is "sell them a karnet", the other is "this one is used up".
  no_valid_pass: 'Ta osoba nie ma karnetu ważnego w dniu tych zajęć.',
  no_entries_left: 'Karnet tej osoby nie ma już wolnych wejść.',
  // Not a product rule: the server lost an optimistic race on every attempt. Trying again is
  // genuinely the right advice, and it is what the message says.
  conflict: 'Ktoś właśnie zmienił zapisy na te zajęcia. Spróbuj ponownie.',
};

/** The fallback for a refusal with no reason, or one this build does not know. */
const UNKNOWN = 'Nie udało się zmienić zapisu. Spróbuj ponownie za chwilę.';

/**
 * The message for a refusal reason.
 *
 * Takes `unknown` rather than the union so callers can hand over whatever came off the wire: a
 * server one version ahead can name a reason this build has never heard of, and that has to read as
 * a message rather than as `undefined`.
 */
export function bookingFailureMessage(reason: unknown): string {
  // `hasOwn`, not `in`: `in` walks the prototype chain, so a server reason of "constructor" or
  // "toString" would return a FUNCTION typed as string and render as source text.
  return typeof reason === 'string' && Object.hasOwn(MESSAGES, reason)
    ? MESSAGES[reason as BookingFailure['reason']]
    : UNKNOWN;
}
