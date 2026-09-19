/**
 * A fence against an older response overwriting a newer one.
 *
 * `begin()` claims the fence and returns a token; `isCurrent(token)` answers whether that load is
 * still the one whose answer the screen wants. Check it after EVERY await, not only the last one.
 */
export interface LoadFence {
  begin(): number;

  /**
   * The current token WITHOUT claiming a new one.
   *
   * For a write rather than a load: a mutation does not start a generation of its own, but it must
   * still notice if a reload happened while it was in flight — otherwise it patches rows that are
   * no longer on screen, or rolls an optimistic change back over a window it never belonged to.
   */
  current(): number;

  isCurrent(token: number): boolean;
}

/**
 * The generation counter, written once (S-19, CS-06) — the most-copied block in this SPA.
 *
 * <h2>What it is actually for</h2>
 *
 * Nothing cancels an in-flight request here. Without a fence the LAST RESPONSE wins rather than the
 * last REQUEST: two quick filter or week clicks can resolve out of order and leave the rows
 * disagreeing with the highlighted chip, silently and with no error anywhere. A stale response is
 * discarded instead of applied.
 *
 * <h2>One fence per independent load, not one per screen</h2>
 *
 * This replaces the mechanism, never the topology. The dashboard's four cards each hold their own
 * `createLoadFence()` and stay four independent fences — a slow karnet response must not discard
 * the classes card's rows, which is exactly what a single shared counter would do.
 */
export function createLoadFence(): LoadFence {
  let generation = 0;

  return {
    begin: () => ++generation,
    current: () => generation,
    isCurrent: (token: number) => token === generation,
  };
}
