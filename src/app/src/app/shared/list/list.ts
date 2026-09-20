import { Component } from '@angular/core';

/**
 * A stack of rows.
 *
 * <p>
 * IT CARRIES ITS OWN <c>&lt;ul&gt;</c> and the rows project into it, rather than the caller writing
 * the list and this decorating it. Four screens — class-types, exercises, members and plans —
 * declared that <c>&lt;ul&gt;</c> rule byte-identically; the three that did not (my-classes' grid,
 * the passes list, the bookings list) keep their own and are not callers of this.
 * </p>
 *
 * <p>
 * Its class lives in <c>src/styles.scss</c> rather than here, for the reason recorded beside it:
 * everything in a row arrives by projection and so carries the caller's encapsulation scope, which
 * a stylesheet on this component could never reach.
 * </p>
 */
@Component({
  selector: 'app-list',
  template: `<ul class="list">
    <ng-content />
  </ul>`,
})
export class List {}
