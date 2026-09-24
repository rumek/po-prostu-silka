import { Component, input } from '@angular/core';
import { Icon, IconName } from '../../icons/icon';

/**
 * One labelled control, with room under it for whatever the screen needs to say about it.
 *
 * <p>
 * IT PROJECTS THE CONTROL RATHER THAN OWNING ONE, and that is the whole design. The obvious
 * alternative — <c>&lt;app-field [control]="form.controls.email" type="email"&gt;</c> — would let
 * the component guarantee <c>aria-invalid</c> and the <c>for</c>/<c>id</c> pair, which projection
 * cannot. It was rejected because two of the fifty-six sites it has to absorb do not fit it:
 * <c>plan-builder.html</c>'s member field renders STATIC TEXT instead of a select when editing, and
 * carries two independent error sources (the members request failing, and the control's own
 * validation); <c>profile.html</c>'s password fields choose between a <c>server</c> error and a
 * fallback sentence. An owning component leaves both behind as a second, smaller copy — and a copy
 * the lint rule could never forbid, because it would have a legitimate reason to exist.
 * </p>
 *
 * <p>
 * WHAT IS NOT GUARANTEED HERE IS GUARANTEED BY LINT. The rule in
 * <c>tools/eslint-rules/no-hand-rolled-presentational.js</c> is the other half of this decision:
 * projection buys the flexibility, and the rule pays for it by failing the build when a screen
 * hand-rolls the block instead.
 * </p>
 *
 * <p>
 * THE LABEL HAS TWO FORMS because the callers do. Almost all of them want a word, so <c>label</c>
 * is an input and the component emits the <c>&lt;label&gt;</c> itself — including its <c>for</c>,
 * which is the pair most easily forgotten when it is written by hand. The plan builder wants an
 * element (a <c>&lt;span&gt;</c> when the member can no longer be changed, because a
 * <c>&lt;label&gt;</c> pointing at static text is a lie to a screen reader), so it projects into
 * <c>[slot=label]</c> and leaves the input unset.
 * </p>
 */
@Component({
  imports: [Icon],
  selector: 'app-field',
  template: `
    <div class="field">
      @if (label(); as text) {
        <label [attr.for]="for()" [class.field-label--icon]="icon()">
          @if (icon(); as name) {
            <app-icon [name]="name" />
          }
          {{ text }}
        </label>
      }

      <ng-content select="[slot=label]" />
      <ng-content />
    </div>
  `,
})
export class Field {
  /**
   * The label's words. Left unset by the one caller whose label is an element rather than a string
   * — see the class docblock — which then projects into `[slot=label]` instead.
   */
  readonly label = input<string>();

  /**
   * The `id` of the control being labelled. Named after the attribute it becomes so that the
   * template reads the way the hand-written markup it replaces did.
   */
  readonly for = input<string>();

  /**
   * An icon before the label's words — for a field whose value is shown with that same icon
   * elsewhere, so the form and the screen that reads it say the same thing (the plan builder's
   * parameters and the member's plan card). Decorative: the words still name the control.
   */
  readonly icon = input<IconName>();
}
