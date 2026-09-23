import { DOCUMENT } from '@angular/common';
import { DestroyRef, inject } from '@angular/core';
import { Router } from '@angular/router';

let issued = 0;

/**
 * Lets the system back gesture close an open overlay instead of leaving the screen under it
 * (mobile-native-feel) — what a native sheet or dialog does, and on Android the first thing a member
 * tries.
 *
 * - **On open** it pushes a history entry for the SAME URL, carrying a copy of the current
 *   `history.state` (so the router's `navigationId` survives) plus a marker naming this overlay. The
 *   router sees nothing: a popstate to an identical URL is skipped under the default
 *   `onSameUrlNavigation: 'ignore'`, so no navigation and no view transition run.
 * - **Back while open** pops that entry, and `onBack` closes the overlay.
 * - **Any other close** — ×, the backdrop, Escape, a finished action — ends in the component being
 *   destroyed, and destroy pops the entry if it is still on top, so no phantom step is left behind.
 *   That is why the pop lives on destroy rather than in each overlay's close method.
 *
 * A navigation started from INSIDE the overlay leaves the entry where it is: popping it then would
 * pop the navigation. The new screen lands above it, and back later walks through one same-URL
 * entry the router ignores.
 */
export function useOverlayHistory(onBack: () => void): void {
  const document = inject(DOCUMENT);
  const router = inject(Router);
  const window = document.defaultView;

  if (window === null) {
    return;
  }

  const token = `${Date.now()}-${++issued}`;
  const ours = () => (window.history.state as { overlay?: string } | null)?.overlay === token;
  let open = true;

  window.history.pushState({ ...(window.history.state ?? {}), overlay: token }, '');

  const onPopState = () => {
    if (open && !ours()) {
      open = false;
      onBack();
    }
  };
  window.addEventListener('popstate', onPopState);

  inject(DestroyRef).onDestroy(() => {
    window.removeEventListener('popstate', onPopState);

    if (open && ours() && router.currentNavigation() === null) {
      window.history.back();
    }
    open = false;
  });
}
