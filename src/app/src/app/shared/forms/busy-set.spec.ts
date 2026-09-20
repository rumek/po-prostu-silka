import { computed } from '@angular/core';
import { createBusySet } from './busy-set';

describe('createBusySet', () => {
  it('starts with nothing in flight', () => {
    const busy = createBusySet();

    expect(busy.isBusy('m1')).toBe(false);
  });

  it('frees only the row that answered', () => {
    // The reason all five copies chose a Set over a single "the busy one" id: the admin can start
    // a second row's action before the first answers, and a single id would free the wrong row.
    const busy = createBusySet();

    busy.setBusy('m1', true);
    busy.setBusy('m2', true);
    busy.setBusy('m1', false);

    expect(busy.isBusy('m1')).toBe(false);
    expect(busy.isBusy('m2')).toBe(true);
  });

  it('gives each call its own set', () => {
    const list = createBusySet();
    const overlay = createBusySet();

    list.setBusy('m1', true);

    expect(overlay.isBusy('m1')).toBe(false);
  });

  it('is a no-op when a row that was never busy is freed', () => {
    const busy = createBusySet();

    expect(() => busy.setBusy('ghost', false)).not.toThrow();
    expect(busy.isBusy('ghost')).toBe(false);
  });

  /**
   * THE HALF THAT IS EASY TO BREAK. A `Set` mutated in place is the same object, so the signal
   * holding it would not notify and the row's button would never re-render — stuck busy, or never
   * busy at all. A direct `isBusy()` call cannot catch that, because it re-reads the signal every
   * time and would see the in-place change too. A `computed` can: it recomputes only when the
   * signal's version actually moves, so an implementation that switched to `.add()` on the
   * existing Set would leave this cached at `false`.
   */
  it('replaces the set rather than mutating it, so dependents recompute', () => {
    const busy = createBusySet();
    const rowDisabled = computed(() => busy.isBusy('m1'));

    expect(rowDisabled()).toBe(false);

    busy.setBusy('m1', true);

    expect(rowDisabled()).toBe(true);
  });
});
