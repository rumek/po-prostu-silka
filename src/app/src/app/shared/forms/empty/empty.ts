import { Component } from '@angular/core';

/**
 * "There is nothing here" — which is not an error and must not read as one.
 *
 * <p>
 * IT PROJECTS WHERE <c>app-loading</c> FIXES, and the asymmetry is deliberate rather than an
 * inconsistency. Every loading state in the app says the same word; no two empty states do. This
 * one has to hold "Plan jest pusty", "Nikt nie jest jeszcze zapisany na te zajęcia" and "Nie masz
 * jeszcze żadnych zapisów" — and the last of those carries a LINK to the schedule, because an empty
 * state a new member will always see first should lead somewhere rather than just report.
 * </p>
 *
 * <p>
 * Three of its callers were wearing <c>.notice</c> rather than <c>.empty</c> before S-23, which is
 * part of how that one class came to carry four unrelated meanings at once.
 * </p>
 */
@Component({
  selector: 'app-empty',
  template: `<p class="empty"><ng-content /></p>`,
})
export class Empty {}
