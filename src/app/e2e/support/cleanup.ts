/**
 * A per-test list of removals, run in REVERSE order of registration after the test - whether it
 * passed or failed. Reverse order is what makes the API's refusals line up: a class is removed before
 * the karnet its bookings point at, and before the type it is an instance of.
 *
 * A removal that fails is reported, and the rest still run: one stuck row must not strand the others.
 * 404 is success - the thing is already gone.
 */
import { APIResponse } from '@playwright/test';

type Removal = () => Promise<APIResponse | void>;

export class Cleanup {
  private readonly removals: { label: string; run: Removal }[] = [];

  add(label: string, run: Removal): void {
    this.removals.push({ label, run });
  }

  async runAll(): Promise<void> {
    const failures: string[] = [];

    for (const { label, run } of this.removals.reverse()) {
      try {
        const response = await run();
        if (response && !response.ok() && response.status() !== 404) {
          failures.push(`${label}: ${response.status()} ${await response.text()}`);
        }
      } catch (error) {
        failures.push(`${label}: ${String(error)}`);
      }
    }

    if (failures.length > 0) {
      throw new Error(`Cleanup left data behind:\n${failures.join('\n')}`);
    }
  }
}
