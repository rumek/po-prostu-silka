import { Component, computed, inject, signal } from '@angular/core';
import { PushService } from '../../core/notifications/push.service';

/** One key, one browser. Namespaced because localStorage is shared across the whole origin. */
const STORAGE_KEY = 'pps.push-prompt.dismissed-at';

/**
 * How long "Nie teraz" holds. Long enough that declining is not punished by being asked again on the
 * next navigation, short enough that a member who declined before they had booked anything is asked
 * once more when the app has become useful to them.
 */
const DISMISSAL_DAYS = 7;

/**
 * The PRE-PERMISSION prompt: the club's own explanation, shown before the browser's prompt.
 *
 * <p>WHY A SECOND PROMPT AT ALL. The browser's permission dialog is one-shot and one-way — "block"
 * is permanent for that device and cannot be undone from inside the app, only from browser
 * settings. Calling `requestSubscription()` on a member who has not decided yet spends that single
 * chance on a dialog with no context. This screen is the recoverable version of the same question:
 * "Nie teraz" costs nothing, and the member who says yes here says yes to the browser too.</p>
 *
 * <p>IT RENDERS NOTHING when push is unsupported or the subscribe attempt fails. `push.service.ts`
 * lists the legitimate reasons — desktop Safari, a non-installed iPhone, a dev build with the worker
 * off, a server without VAPID keys — and none of them is the member's problem to solve. Push is
 * best-effort by design; email is the channel FR-021's guarantee actually rests on, and it arrives
 * either way.</p>
 */
@Component({
  selector: 'app-push-prompt',
  styleUrl: './push-prompt.scss',
  templateUrl: './push-prompt.html',
})
export class PushPrompt {
  private readonly push = inject(PushService);

  private readonly dismissed = signal(this.wasRecentlyDismissed());

  /**
   * Set only by a FAILED subscribe. An unsupported browser is already covered by `isSupported`;
   * this covers the member who reached the browser's own prompt and declined it, where re-offering
   * a button that can no longer do anything would be a lie.
   */
  private readonly failed = signal(false);

  protected readonly working = signal(false);

  protected readonly visible = computed(
    () =>
      this.push.isSupported() && !this.push.isSubscribed() && !this.dismissed() && !this.failed(),
  );

  protected async enable(): Promise<void> {
    this.working.set(true);

    try {
      // subscribe() never throws — it reports false and records a reason. A false here means the
      // browser prompt was declined, the worker is missing, or the server has no VAPID keys.
      this.failed.set(!(await this.push.subscribe()));
    } finally {
      this.working.set(false);
    }
  }

  /**
   * Persisted rather than held in memory: the component is re-created on every navigation through
   * the shell, and a dismissal that lasted until the next route change would not be a dismissal.
   */
  protected dismiss(): void {
    this.dismissed.set(true);
    this.write(new Date().toISOString());
  }

  private wasRecentlyDismissed(): boolean {
    const stored = this.read();

    if (stored === null) {
      return false;
    }

    const at = Date.parse(stored);

    // Unparseable means someone else wrote the key, or an older format did. Treat it as no
    // dismissal rather than as a permanent one: the worst case is asking once more.
    if (Number.isNaN(at)) {
      return false;
    }

    return Date.now() - at < DISMISSAL_DAYS * 24 * 60 * 60 * 1000;
  }

  /**
   * Storage is guarded on both sides. It does not exist during server-side rendering, and a browser
   * configured to block site data throws on ACCESS rather than returning null — an unhandled throw
   * in a field initialiser would take the whole shell down over a notification prompt.
   */
  private read(): string | null {
    try {
      return localStorage.getItem(STORAGE_KEY);
    } catch {
      return null;
    }
  }

  private write(value: string): void {
    try {
      localStorage.setItem(STORAGE_KEY, value);
    } catch {
      // Private mode, or site data blocked. The dismissal still holds for this component instance.
    }
  }
}
