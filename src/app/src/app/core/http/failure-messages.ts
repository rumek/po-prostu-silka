/**
 * The one implementation behind every `*FailureMessage` table in the app.
 *
 * <h2>Why each union keeps its own Record</h2>
 *
 * The `Record<Reason, string>` is what buys build-time exhaustiveness: adding a reason to a
 * union without adding a sentence fails `npm run build` at the table, not at runtime in
 * front of a user. That property is per-union and cannot be centralised. What CAN be
 * centralised is the scaffolding every table repeated verbatim — the fallback and the
 * `Object.hasOwn` guard — and that is all this function is.
 */
export function createFailureMessages<R extends string>(
  messages: Record<R, string>,
  unknown: string,
): (reason: unknown) => string {
  /**
   * The message for a refusal reason.
   *
   * Takes `unknown` rather than the union so callers can hand over whatever came off the
   * wire: a server one version ahead can name a reason this build has never heard of, and
   * that has to read as a message rather than as `undefined`.
   */
  return (reason: unknown): string =>
    // `hasOwn`, not `in`: `in` walks the prototype chain, so a server reason of
    // "constructor" or "toString" would return a FUNCTION typed as string and render as
    // source text. This guard is a review finding, not a style choice —
    // context/archive/2026-09-02-schedule-calendar-view/reviews/impl-review.md:169-174.
    typeof reason === 'string' && Object.hasOwn(messages, reason) ? messages[reason as R] : unknown;
}
