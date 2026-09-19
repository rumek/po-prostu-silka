import { LiveAnnouncer } from '@angular/cdk/a11y';
import { Component, effect, inject } from '@angular/core';
import { Toast, ToastService } from './toast.service';

/**
 * The single transient surface, mounted once in the shell.
 *
 * <h2>Announced, not merely rendered</h2>
 *
 * The existing `.alert` banners are `role="alert"` and stay on screen, so a screen-reader user
 * who misses the announcement can still go and read them. A toast disappears — for `success`
 * and `info`, before most people would navigate to it. So the visual host is `aria-hidden` and
 * the words go through `LiveAnnouncer` instead: one announcement, at the moment it matters, with
 * no stale live region left behind for someone to stumble into later.
 *
 * `cdk/a11y` is the only CDK entry point in the eager bundle (drag-drop is confined to the lazy
 * plan-builder chunk). `cdk/overlay` was declined for the same budget reason — this host is a
 * fixed-position list, which needs no overlay machinery.
 */
@Component({
  selector: 'app-toast-host',
  styleUrl: './toast-host.scss',
  templateUrl: './toast-host.html',
})
export class ToastHost {
  private readonly announcer = inject(LiveAnnouncer);
  protected readonly toasts = inject(ToastService);

  /** Ids already announced, so a re-render never repeats a sentence already spoken. */
  private readonly announced = new Set<number>();

  constructor() {
    effect(() => {
      for (const toast of this.toasts.toasts()) {
        if (this.announced.has(toast.id)) {
          continue;
        }

        this.announced.add(toast.id);
        // `assertive` for a refusal — it is the answer to something the user just did, and
        // waiting for a pause would land it after they have moved on. `polite` for the rest.
        void this.announcer.announce(
          toast.message,
          toast.tone === 'error' ? 'assertive' : 'polite',
        );
      }
    });
  }

  protected dismiss(toast: Toast): void {
    this.toasts.dismiss(toast.id);
  }
}
