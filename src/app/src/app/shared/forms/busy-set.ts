import { signal } from '@angular/core';

/** Tracks which rows have a mutation in flight, so one slow row does not disable a whole list. */
export interface BusySet {
  /** Marks a row busy or free. */
  setBusy(id: string, busy: boolean): void;
  /** Whether this row has something in flight. Read by the template, per row. */
  isBusy(id: string): boolean;
}

/**
 * The per-row busy flag, written once (S-19, CS-06).
 *
 * <h2>Why a Set and not a single id</h2>
 *
 * Five screens carried a byte-identical copy of this, and all five chose a `Set` for the same
 * reason: the admin can start a second row's action before the first has answered, and a single
 * "the busy one" would then free the wrong row when the slower request returned.
 *
 * The signal is replaced rather than mutated on every change — a `Set` mutated in place is the same
 * object, so a signal holding it would not notify and the button would never re-render.
 */
export function createBusySet(): BusySet {
  const busy = signal<ReadonlySet<string>>(new Set());

  return {
    setBusy(id: string, value: boolean): void {
      busy.update((ids) => {
        const next = new Set(ids);

        if (value) {
          next.add(id);
        } else {
          next.delete(id);
        }

        return next;
      });
    },

    isBusy(id: string): boolean {
      return busy().has(id);
    },
  };
}
