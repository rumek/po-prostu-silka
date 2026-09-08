import { BookingFailure } from './booking.models';

/**
 * What to tell the member when the API refuses a booking or a cancellation.
 *
 * <h2>Why this is a shared table and not a switch</h2>
 *
 * The same discipline `class-failure.ts` documents, applied BEFORE the second consumer exists rather
 * than after. Two surfaces already receive these reasons — the class-detail overlay and "Moje
 * zajęcia" — and the admin's booking panel joins them in S-08's last phase. Two independent switches
 * over six reasons is how the same refusal comes to read one way in one place and another way in the
 * other, and the drift is invisible until someone hits the same error twice in one session.
 *
 * <h2>Exhaustiveness</h2>
 *
 * The `Record` is keyed by the reason union itself, so adding a reason to `BookingFailure` fails the
 * build here rather than falling through to a generic message.
 */
const MESSAGES: Record<BookingFailure['reason'], string> = {
  // Every one of these is something that changed while the member was looking at a stale screen, so
  // each says what happened rather than what they did wrong.
  class_cancelled: 'Te zajęcia zostały odwołane.',
  class_started: 'Te zajęcia już się rozpoczęły — zapisy są zamknięte.',
  already_booked: 'Jesteś już zapisany na te zajęcia.',
  class_full: 'Brak wolnych miejsc na tych zajęciach.',
  not_booked: 'Nie jesteś zapisany na te zajęcia.',
  // Admin route only — see BookingFailure. Phrased about the member because that is the only place
  // it can ever be shown.
  member_blocked: 'Ta osoba jest zablokowana i nie może być zapisana na zajęcia.',
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

/**
 * The two reasons that are ABOUT A PERSON, as the admin needs to read them.
 *
 * The shared table above addresses the member in the second person, which is right on every screen
 * that shows a member their own booking and wrong on the one screen where an admin is acting for
 * somebody else — "Jesteś już zapisany" about a third party is simply false. Only the person-relative
 * reasons are restated; everything else (a cancelled class, a full class, a lost race) reads the same
 * whoever is looking at it, and still comes from the one table.
 */
const ADMIN_MESSAGES: Partial<Record<BookingFailure['reason'], string>> = {
  already_booked: 'Ta osoba jest już zapisana na te zajęcia.',
  not_booked: 'Ta osoba nie jest zapisana na te zajęcia.',
};

/** The message for a refusal the ADMIN caused, acting on somebody else's behalf (S-14). */
export function adminBookingFailureMessage(reason: unknown): string {
  return (
    (typeof reason === 'string' && Object.hasOwn(ADMIN_MESSAGES, reason)
      ? ADMIN_MESSAGES[reason as BookingFailure['reason']]
      : undefined) ?? bookingFailureMessage(reason)
  );
}
