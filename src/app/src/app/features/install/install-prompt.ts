import { Component, computed, inject, signal } from '@angular/core';
import { InstallService } from '../../core/pwa/install.service';

/** One key, one browser. Namespaced because localStorage is shared across the whole origin. */
const STORAGE_KEY = 'pps.install-prompt.dismissed-at';

/**
 * Longer than the push prompt's week. Installing is a bigger ask than a notification, and the
 * browser's own install entry stays in its menu for the member who changes their mind sooner.
 */
const DISMISSAL_DAYS = 30;

/**
 * Offers "add to home screen" in the club's own words, replaying the `beforeinstallprompt` event
 * `InstallService` held back (PRD: mobile-first and installable).
 *
 * <p>IT RENDERS NOTHING without that event: Safari (iPhone installs go through the share sheet, and
 * no script can open it), Firefox, an app that is already installed, and a browser that has not yet
 * decided the site is installable. None of them has a button that would help.</p>
 */
@Component({
  selector: 'app-install-prompt',
  styleUrl: './install-prompt.scss',
  templateUrl: './install-prompt.html',
})
export class InstallPrompt {
  private readonly installer = inject(InstallService);

  private readonly dismissed = signal(this.wasRecentlyDismissed());

  protected readonly working = signal(false);

  protected readonly visible = computed(() => this.installer.canInstall() && !this.dismissed());

  /**
   * No failure state of its own: the service drops the spent event whatever the answer, so
   * `canInstall` alone withdraws the banner — and brings it back if Chrome offers a fresh event.
   */
  protected async install(): Promise<void> {
    this.working.set(true);

    try {
      await this.installer.install();
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

    // Someone else's key, or an older format: asking once more is the smaller failure.
    if (Number.isNaN(at)) {
      return false;
    }

    return Date.now() - at < DISMISSAL_DAYS * 24 * 60 * 60 * 1000;
  }

  /** Guarded on both sides: absent during SSR, and it throws on access when site data is blocked. */
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
