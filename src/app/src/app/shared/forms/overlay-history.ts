import { DOCUMENT } from '@angular/common';
import { DestroyRef, inject } from '@angular/core';
import { Router } from '@angular/router';

let issued = 0;

/**
 * The pops this helper has asked for and the browser has not delivered yet. `history.back()` is
 * ASYNCHRONOUS, so between a closing overlay's pop and its `popstate` there is a window in which
 * another overlay can open — an overlay SWAP, e.g. the admin's actions overlay handing over to the
 * bookings overlay in one change-detection pass. Were the new overlay to push and listen at once,
 * the old overlay's pop would land on it and close it the moment it opened.
 *
 * So the pops are counted, and module-wide rather than per overlay: the `popstate` a pop produces
 * is swallowed by one shared listener, never read as a back gesture, and an overlay opened while one
 * is outstanding waits for it before pushing its own entry.
 */
let pendingPops = 0;
const waiting: (() => void)[] = [];
const swallowed = new WeakSet<Event>();
let installedOn: Window | null = null;

/**
 * If the browser never delivers a pop (it has nowhere to go), overlays must not wait forever. Far
 * longer than a same-document traversal takes, far shorter than a member would notice.
 */
const POP_TIMEOUT_MS = 1000;

function settle(): void {
  pendingPops = 0;
  waiting.splice(0).forEach((open) => open());
}

// Registered before any overlay's own listener, so it runs first and can mark the event.
function install(window: Window): void {
  if (installedOn === window) {
    return;
  }
  installedOn = window;
  window.addEventListener('popstate', (event) => {
    if (pendingPops === 0) {
      return;
    }
    swallowed.add(event);
    pendingPops--;
    if (pendingPops === 0) {
      settle();
    }
  });
}

function popOwnEntry(window: Window): void {
  pendingPops++;
  window.history.back();
  window.setTimeout(() => {
    if (pendingPops > 0) {
      settle();
    }
  }, POP_TIMEOUT_MS);
}

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
 * - **Opened while another overlay's pop is in flight** (a swap), it waits for that pop before
 *   pushing — see `pendingPops`. A back gesture made inside that window is taken for the pop.
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
  install(window);

  const token = `${Date.now()}-${++issued}`;
  const ours = () => (window.history.state as { overlay?: string } | null)?.overlay === token;
  let open = true;
  let pushed = false;

  const onPopState = (event: PopStateEvent) => {
    if (open && !swallowed.has(event) && !ours()) {
      open = false;
      onBack();
    }
  };

  const push = () => {
    if (!open) {
      return;
    }
    window.history.pushState({ ...(window.history.state ?? {}), overlay: token }, '');
    window.addEventListener('popstate', onPopState);
    pushed = true;
  };

  if (pendingPops > 0) {
    waiting.push(push);
  } else {
    push();
  }

  inject(DestroyRef).onDestroy(() => {
    window.removeEventListener('popstate', onPopState);

    if (open && pushed && ours() && router.currentNavigation() === null) {
      popOwnEntry(window);
    }
    open = false;
  });
}
