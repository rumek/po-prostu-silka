import { Component } from '@angular/core';

/**
 * "Wczytywanie…", announced.
 *
 * <p>
 * NO INPUT, AND THAT IS THE FEATURE. The obvious shape — <c>[message]</c> with a default — is the
 * one thing this component must not have. There were twenty-two copies of this line before S-23 and
 * every one of them said the same word, which is the only reason a component can replace them at
 * all; an input would be used, and three screens later there would be four sentences for one state
 * and nothing to point at when asking which is right. The word lives here so that changing it is
 * one edit and having two of them is impossible.
 * </p>
 *
 * <p>
 * <c>role="status"</c> rather than <c>aria-live="assertive"</c>: a screen reader should mention
 * that something is loading when it reaches a pause, not interrupt whatever it is reading.
 * </p>
 */
@Component({
  selector: 'app-loading',
  template: `<p class="notice" role="status">Wczytywanie…</p>`,
})
export class Loading {}
