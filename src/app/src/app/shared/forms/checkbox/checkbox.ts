import { Component, input, output } from '@angular/core';

/**
 * A checkbox and the words beside it.
 *
 * <p>
 * NO <c>ControlValueAccessor</c>, DELIBERATELY. Both callers are filter toggles — "pokaż
 * nieaktywne" on the class-types and exercises lists — and no form in this app has a checkbox at
 * all. A CVA written now would be the only untested code path in the kit, existing for a caller
 * that does not exist; when a form does need one it is an additive change, not a rewrite, because
 * nothing about this component's markup would have to move.
 * </p>
 *
 * <p>
 * THE INPUT SITS INSIDE THE <c>&lt;label&gt;</c> rather than beside it with a <c>for</c>/<c>id</c>
 * pair. Both hand-written copies already did this, and it is the stronger of the two forms: the
 * association cannot be broken by a missing or duplicated id, and there is no id for a caller to
 * forget to pass. It also means the whole row — box and words — is one hit target, which is what
 * the <c>cursor: pointer</c> in the stylesheet was promising.
 * </p>
 *
 * <p>
 * <c>checked</c>/<c>checkedChange</c> and not a two-way <c>model()</c>: both callers hold the state
 * in a signal they own and flip it through a method that also refetches. A <c>model()</c> would
 * write the signal behind that method's back.
 * </p>
 */
@Component({
  selector: 'app-checkbox',
  styleUrl: './checkbox.scss',
  template: `
    <label class="checkbox">
      <input type="checkbox" [checked]="checked()" (change)="toggle()" />
      <ng-content />
    </label>
  `,
})
export class Checkbox {
  /** Whether the box is ticked. Owned by the caller; this component never writes it. */
  readonly checked = input.required<boolean>();

  /**
   * Raised on every change. It carries no payload on purpose — the caller already knows what the
   * next state is, and both of them respond by calling a method that toggles and refetches rather
   * than by storing whatever they were handed.
   */
  readonly checkedChange = output<void>();

  protected toggle(): void {
    this.checkedChange.emit();
  }
}
