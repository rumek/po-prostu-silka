import { DOCUMENT } from '@angular/common';
import { DestroyRef, ElementRef, afterNextRender, inject } from '@angular/core';

/**
 * Gives an overlay the focus behaviour a modal is supposed to have (S-19).
 *
 * <h2>What was missing</h2>
 *
 * All three overlays declared `role="dialog"` and `aria-modal="true"`, and none of them moved focus.
 * Opening one left the keyboard back on the calendar tile behind the panel: Tab walked the page
 * under the modal, and a screen reader announced nothing, because nothing had moved. Escape closed
 * it and focus was still wherever it had been.
 *
 * <h2>Why the opener is read here rather than passed in</h2>
 *
 * The overlay is created by a click, so `document.activeElement` at construction IS the control the
 * user pressed. Threading an opener reference down from three different parents would be three more
 * things to keep in step for a value already sitting right there.
 *
 * Call it from the component's field initialiser or constructor — it must run early enough to read
 * the active element before the panel steals it.
 */
export function useOverlayFocus(): void {
  const document = inject(DOCUMENT);
  const host = inject(ElementRef<HTMLElement>);

  // Captured NOW: once the panel takes focus this is gone, and on destroy there would be nothing
  // to go back to.
  const opener = document.activeElement as HTMLElement | null;

  afterNextRender(() => {
    const panel = (host.nativeElement as HTMLElement).querySelector<HTMLElement>('.overlay-panel');

    if (panel === null) {
      return;
    }

    // The PANEL, not the first control in it. Focusing the first input would skip the title, so a
    // screen-reader user would never hear what the dialog is about; focusing the container lets the
    // dialog announce itself and Tab then walks its contents in order.
    panel.tabIndex = -1;
    panel.focus();
  });

  inject(DestroyRef).onDestroy(() => {
    // Only if the opener is still in the document — the row it belonged to may have been removed by
    // the very action the overlay performed, and focusing a detached element silently does nothing
    // while leaving focus on <body>.
    if (opener !== null && document.contains(opener)) {
      opener.focus();
    }
  });
}
