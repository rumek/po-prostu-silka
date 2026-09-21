import { isPlatformBrowser } from '@angular/common';
import { DestroyRef, PLATFORM_ID, Signal, inject, signal } from '@angular/core';

/**
 * A media query as a signal that follows the viewport (S-20). Must be called in an injection
 * context — a field initializer or a constructor.
 *
 * `fallback` is what the signal holds wherever there is no viewport to measure, and the caller
 * chooses it because the right answer differs: the calendar's week view falls back to `false`
 * (day-first is the mobile-first answer), while the desk check falls back to `true` (a screen that
 * cannot measure itself should render, not refuse).
 *
 * Guarded twice, as the calendar's original read was. `isPlatformBrowser` because SSR is one
 * angular.json key away from being live and an unguarded `matchMedia` would crash it; `typeof`
 * because a browser platform does NOT guarantee the API — jsdom, which the specs run in, has none.
 */
export function mediaQuerySignal(query: string, fallback: boolean): Signal<boolean> {
  const platformId = inject(PLATFORM_ID);

  if (!isPlatformBrowser(platformId) || typeof window.matchMedia !== 'function') {
    return signal(fallback).asReadonly();
  }

  const list = window.matchMedia(query);
  const matches = signal(list.matches);
  const follow = (event: MediaQueryListEvent) => matches.set(event.matches);

  list.addEventListener('change', follow);

  // A MediaQueryList lives as long as the page, so an un-removed listener outlives its component —
  // and the screens that read one are lazy routes, destroyed on every navigation away.
  inject(DestroyRef).onDestroy(() => list.removeEventListener('change', follow));

  return matches.asReadonly();
}
