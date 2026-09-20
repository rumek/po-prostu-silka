import { Component } from '@angular/core';

/**
 * The wrapper a `<select>` needs in order to have an arrow.
 *
 * <p>
 * THIS COMPONENT IS A DEFECT MADE IMPOSSIBLE. <c>styles.scss</c> sets <c>appearance: none</c> on
 * every select — the platform arrow is drawn by the OS, ignores our colour and sits at a different
 * inset on every system — and draws the replacement chevron from <c>::after</c>. A select is a
 * replaced element and generates no pseudo-element of its own, so that <c>::after</c> has to live
 * on a wrapper. <c>styles.scss</c> warned about this in a comment for three slices, and five of the
 * app's seven selects still shipped with their native arrow suppressed and NOTHING in its place.
 * </p>
 *
 * <p>
 * IT WRAPS RATHER THAN OWNS, which is why it needs no inputs at all. The caller writes its own
 * <c>&lt;select&gt;</c> inside it, and because projected content is compiled in the CALLER's
 * template, <c>formControlName</c>, <c>(change)</c>, <c>[disabled]</c> and
 * <c>[attr.aria-invalid]</c> keep binding against the caller's form group and injector with no
 * passthrough of any kind. A component that owned the select would need an input per attribute, and
 * would gain one every time a screen needed something new.
 * </p>
 *
 * <p>
 * The consequence is that it cannot check what was projected into it. The lint rule is what
 * reports a <c>&lt;select&gt;</c> with no <c>app-select</c> ancestor.
 * </p>
 */
@Component({
  selector: 'app-select',
  template: `<span class="select"><ng-content /></span>`,
})
export class Select {}
