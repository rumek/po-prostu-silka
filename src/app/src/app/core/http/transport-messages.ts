import { FailureInfo, FailureKind } from './failure';

/**
 * What to say when the failure was not a refusal the API named.
 *
 * <h2>The gap this closes</h2>
 *
 * Every union table in the app answers "the server said no, and here is why". None of them
 * answer "the server never replied", "the server broke", or "you are going too fast" — so
 * before S-19 all three read as the union's generic fallback, which advises retrying a
 * business rule that was never the problem. These sentences belong to the transport, not to
 * any one endpoint, so they live once here rather than in seventeen tables.
 */
const TRANSPORT_MESSAGES: Record<Exclude<FailureKind, 'business'>, string> = {
  auth: 'Nie masz uprawnień do tej operacji. Odśwież stronę i zaloguj się ponownie.',
  notFound: 'Nie znaleziono tych danych — mogły zostać usunięte. Odśwież stronę.',
  rateLimited: 'Zbyt wiele prób. Odczekaj chwilę i spróbuj ponownie.',
  server: 'Coś poszło nie tak po naszej stronie. Spróbuj ponownie za chwilę.',
  offline: 'Brak połączenia z serwerem. Sprawdź internet i spróbuj ponownie.',
  unknown: 'Coś poszło nie tak. Spróbuj ponownie za chwilę.',
};

/**
 * The transport-level sentence for a classified failure, or `null` when there isn't one.
 *
 * `null` is the signal to fall through to the union's own table: a `business` kind means the
 * API named a reason, and the words for a named reason always come from that endpoint's
 * table, never from here.
 */
export function transportMessage(info: FailureInfo): string | null {
  return info.kind === 'business' ? null : TRANSPORT_MESSAGES[info.kind];
}
