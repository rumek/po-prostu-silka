import { booleanAttribute, Component, input } from '@angular/core';
import { Icon, IconName } from '../../icons/icon';

/**
 * "There is nothing here" — which is not an error and must not read as one.
 *
 * <p>
 * IT PROJECTS WHERE <c>app-loading</c> FIXES, and the asymmetry is deliberate rather than an
 * inconsistency. Every loading state in the app says the same word; no two empty states do. This
 * one has to hold "Plan jest pusty", "Nikt nie jest jeszcze zapisany na te zajęcia" and "Nie masz
 * jeszcze żadnych zapisów" — and some of them carry a next step, because an empty state a new
 * member will always see first should lead somewhere rather than just report.
 * </p>
 *
 * <p>
 * TWO SHAPES, chosen by where the empty state sits rather than by taste:
 * </p>
 * <ul>
 *   <li>the default is a <b>card of its own</b> — the whole screen or list is empty, so the empty
 *   state stands where the list would have been: the icon in an avatar disc, the words centred
 *   under it, an optional action below;</li>
 *   <li><c>compact</c> is for an empty state INSIDE something that is already a card — a dashboard
 *   card, an overlay, a builder section: no frame of its own, the disc beside the words.</li>
 * </ul>
 *
 * <p>
 * The icon names what is missing, with the meaning it has everywhere else (<c>ticket</c> = the
 * karnet, <c>booking</c> = a class you are signed up for, <c>search</c> = a phrase matched
 * nothing). An action is projected with <c>slot="action"</c> — usually an <c>a.button</c>.
 * </p>
 */
@Component({
  selector: 'app-empty',
  imports: [Icon],
  styleUrl: './empty.scss',
  template: `
    <div class="empty" [class.empty--compact]="compact()">
      @if (icon(); as name) {
        <span class="empty-icon"><app-icon [name]="name" /></span>
      }
      <div class="empty-body">
        <p class="empty-text"><ng-content /></p>
        <div class="empty-action"><ng-content select="[slot=action]" /></div>
      </div>
    </div>
  `,
})
export class Empty {
  readonly icon = input<IconName>();
  readonly compact = input(false, { transform: booleanAttribute });
}
