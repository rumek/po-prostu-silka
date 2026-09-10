import { Component, input } from '@angular/core';

/**
 * A labelled value the member may read and cannot change.
 *
 * <p>
 * TEXT, NOT A DISABLED INPUT, and that is the whole point. A disabled field still reads as something
 * that might become editable — it has a box, a border, a cursor — and both callers show values that
 * never will be. /profile shows the two the gym owns (FR-006 as rewritten: no endpoint in this app
 * changes a display name or a login address); /register shows the invitation code, which arrives in
 * the link and is only there to be confirmed at a glance.
 * </p>
 *
 * <p>
 * IT CARRIES ITS OWN <c>&lt;dl&gt;</c> rather than projecting a <c>dt</c>/<c>dd</c> pair into the
 * caller's. A single-pair description list is valid and correct on its own, and the alternative —
 * emitting bare <c>dt</c>/<c>dd</c> and relying on <c>:host { display: contents }</c> to keep them
 * legal children of an outer list — makes every caller responsible for supplying that list and
 * silently produces invalid markup when one forgets. Self-contained means it can be dropped anywhere.
 * </p>
 */
@Component({
  selector: 'app-readonly-field',
  styleUrl: './readonly-field.scss',
  template: `
    <dl class="readonly-field">
      <dt>{{ label() }}</dt>
      <dd>{{ value() }}</dd>
    </dl>
  `,
})
export class ReadonlyField {
  /** What the value is — the same words a `<label>` would carry beside an editable field. */
  readonly label = input.required<string>();

  /**
   * Rendered as-is. Nullable because /profile reads it from a session that may still be loading, and
   * an empty line is a better answer there than a component that refuses to render.
   */
  readonly value = input.required<string | null | undefined>();
}
