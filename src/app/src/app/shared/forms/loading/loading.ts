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
 *
 * <p>
 * A SPINNER OVER THE WORD, not the word alone in a <c>.notice</c> box: a box read as content that
 * had arrived, and a still line of text does not say that anything is happening. The ring is
 * decorative (aria-hidden) — the word is what is announced, and it stays visible for the eye too.
 * Both fade in after a beat, so a load that answers at once never flashes a spinner.
 * </p>
 */
@Component({
  selector: 'app-loading',
  styleUrl: './loading.scss',
  template: `
    <div class="loading" role="status">
      <span class="loading-ring" aria-hidden="true"></span>
      <span class="loading-word">Wczytywanie…</span>
    </div>
  `,
})
export class Loading {}
