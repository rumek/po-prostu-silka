import { Component } from '@angular/core';

/**
 * One row of a list: an optional something on the left, what the row is about, what can be done to
 * it, and an optional line underneath.
 *
 * <p>
 * AN ATTRIBUTE SELECTOR, NOT AN ELEMENT ONE, and this is the part that is not a style choice.
 * <c>&lt;app-row&gt;</c> as an element would make the DOM <c>ul &gt; app-row</c>, which is invalid
 * list markup: <c>&lt;ul&gt;</c> admits <c>&lt;li&gt;</c> and nothing else, and a screen reader
 * that finds anything else stops reporting the list's length. Written as
 * <c>&lt;li appRow&gt;</c> the host element IS the list item, and the component only furnishes it.
 * </p>
 *
 * <p>
 * FOUR SLOTS AND NO INPUTS. Every row in the app carries something of its own — exercises a 16:9
 * thumbnail, class-types and members a badge row, members an anchored action menu, plan-builder a
 * drag handle, my-classes an entire component in place of an identity. An input per variation would
 * have grown one every time a screen needed something; projection holds all of them without the
 * component knowing what it holds. What it cannot then do is check that a row HAS a name, or that
 * its parts are in order — and neither does the lint rule, which only forbids a hand-rolled row.
 * That is a deliberate gap, not an oversight: my-classes' row holds a whole component and no
 * `.row-name` at all, so a "must have a name" check would need an exemption on day one.
 * </p>
 *
 * <p>
 * The <c>card</c> class stays the caller's: six of the seven rows want it and the bookings list,
 * which sits inside an overlay panel that is already a card, does not.
 * </p>
 */
@Component({
  selector: 'li[appRow]',
  host: { class: 'row' },
  template: `
    <ng-content select="[slot=lead]" />

    <div class="row-identity">
      <ng-content />
    </div>

    <ng-content select="[slot=actions]" />
    <ng-content select="[slot=foot]" />
  `,
})
export class Row {}
