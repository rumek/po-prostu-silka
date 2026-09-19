import { Injectable, signal } from '@angular/core';

/** The three meanings a toast can carry. There is no fourth. */
export type ToastTone = 'success' | 'info' | 'error';

/** One live toast. */
export interface Toast {
  id: number;
  tone: ToastTone;
  message: string;
}

/**
 * How long a self-dismissing toast stays. Long enough to read a Polish sentence at a glance,
 * short enough that a queue of confirmations does not pile up on a phone.
 */
const AUTO_DISMISS_MS = 5000;

/**
 * The third outlet of the failure rule (see AGENTS.md, "How a failure reaches the user").
 *
 * <h2>Why errors do not auto-dismiss</h2>
 *
 * A confirmation that vanishes costs nothing — the thing happened, and the screen shows it. A
 * REFUSAL that vanishes costs the user the only explanation they were given, and there is no
 * second copy anywhere: the row still looks the way it did before they acted. So `success` and
 * `info` time out and `error` waits to be dismissed.
 *
 * <h2>Why the service owns the timer and not the component</h2>
 *
 * The host renders the list; it does not decide lifetimes. Keeping the timer here means a spec
 * can advance fake timers against the service alone, and means a toast raised while the host is
 * mid-render still expires on schedule.
 */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly items = signal<readonly Toast[]>([]);
  private nextId = 1;

  /** The live toasts, oldest first. Read by `ToastHost`; nothing else should need it. */
  readonly toasts = this.items.asReadonly();

  /** Something the user asked for happened. */
  success(message: string): void {
    this.push('success', message);
  }

  /**
   * Something changed that the user did not ask for and needs to know about — a stale list that
   * refetched itself after a 409 is the canonical case. Neither a success nor a failure, which is
   * exactly why it is not folded into either.
   */
  info(message: string): void {
    this.push('info', message);
  }

  /** The action was refused or failed. Stays until dismissed. */
  error(message: string): void {
    this.push('error', message);
  }

  /** Removes one toast, by id. The host calls this from its dismiss button. */
  dismiss(id: number): void {
    this.items.update((current) => current.filter((toast) => toast.id !== id));
  }

  private push(tone: ToastTone, message: string): void {
    const id = this.nextId++;

    this.items.update((current) => [...current, { id, tone, message }]);

    if (tone !== 'error') {
      // Unreferenced on purpose: there is nothing to cancel. Dismissing early removes the toast
      // from the list, and a timer that then fires filters on an id that is already gone.
      setTimeout(() => this.dismiss(id), AUTO_DISMISS_MS);
    }
  }
}
