import { createLoadFence } from './load-fence';

describe('createLoadFence', () => {
  it('treats the token it just issued as current', () => {
    const fence = createLoadFence();

    const token = fence.begin();

    expect(fence.isCurrent(token)).toBe(true);
  });

  /**
   * The whole point. Nothing cancels an in-flight request here, so without the fence the LAST
   * RESPONSE wins rather than the last REQUEST: two quick week or filter clicks can resolve out of
   * order and leave the rows disagreeing with the highlighted chip, silently.
   */
  it('stops recognising a token once a newer load has begun', () => {
    const fence = createLoadFence();

    const first = fence.begin();
    const second = fence.begin();

    expect(fence.isCurrent(first)).toBe(false);
    expect(fence.isCurrent(second)).toBe(true);
  });

  it('never reissues a token, so a stale one cannot come back around', () => {
    const fence = createLoadFence();
    const tokens = [fence.begin(), fence.begin(), fence.begin()];

    expect(new Set(tokens).size).toBe(3);
    // Strictly increasing — an older token is always behind, never equal to a later generation.
    expect(tokens).toEqual([...tokens].sort((a, b) => a - b));
  });

  /**
   * `current()` is for a WRITE rather than a load: a mutation does not start a generation of its
   * own, but it must still notice a reload that happened while it was in flight — otherwise it
   * patches rows that are no longer on screen.
   */
  it('reports the current token without claiming a new one', () => {
    const fence = createLoadFence();
    const load = fence.begin();

    expect(fence.current()).toBe(load);
    // Reading it did not advance the generation: the load it belongs to is still the current one.
    expect(fence.isCurrent(load)).toBe(true);
  });

  it('notices from a write token that a reload happened mid-flight', () => {
    const fence = createLoadFence();
    fence.begin();
    const beforeWrite = fence.current();

    fence.begin();

    expect(fence.isCurrent(beforeWrite)).toBe(false);
  });

  /**
   * ONE FENCE PER INDEPENDENT LOAD, NOT ONE PER SCREEN. The dashboard's four cards each hold their
   * own `createLoadFence()`; a module-level counter shared between instances would make a slow
   * karnet response discard the classes card's rows. This is the test that would catch that
   * regression.
   */
  it('gives each call its own generation', () => {
    const passes = createLoadFence();
    const classes = createLoadFence();

    const passesToken = passes.begin();
    classes.begin();
    classes.begin();

    expect(passes.isCurrent(passesToken)).toBe(true);
  });
});
