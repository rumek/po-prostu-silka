import { Component, output } from '@angular/core';
import { Icon } from '../icons/icon';

/**
 * An overlay's head: its title and the X that closes it.
 *
 * <p>
 * EVERY OVERLAY HAS ONE. A panel is a title, a scrolling body and a pinned foot of actions
 * (`.overlay-body` / `.overlay-actions` in src/styles.scss), and the way out is always the X in the
 * top corner — before this, one overlay closed only by a "Zamknij" link somewhere down its content,
 * which on a phone sat under the fold.
 * </p>
 *
 * <p>
 * The title PROJECTS rather than being an input, for the kit's reason (S-23): the caller keeps its
 * own `h2` with the `id` its dialog's `aria-labelledby` points at, and any markup inside it.
 * </p>
 */
@Component({
  imports: [Icon],
  selector: 'app-overlay-head',
  styleUrl: './overlay-head.scss',
  template: `
    <ng-content />
    <button type="button" class="overlay-close" title="Zamknij" (click)="closed.emit()">
      <app-icon name="close" />
      <span class="cdk-visually-hidden">Zamknij</span>
    </button>
  `,
})
export class OverlayHead {
  readonly closed = output<void>();
}
