import { inject } from '@angular/core';
import { Router, ViewTransitionInfo } from '@angular/router';

/**
 * Which way a routed screen slides (S-26).
 *
 * The View Transition itself is the browser's; all this decides is the DIRECTION, because a slide
 * that always travels the same way contradicts the back button — going back should undo the motion
 * that brought you here, not repeat it. The router's own navigation trigger is the only honest
 * source for that: `popstate` is the browser's back or forward button, everything else is a link or
 * a `router.navigate`.
 *
 * The direction is published as a data attribute on <html> rather than passed anywhere, because the
 * pseudo-elements that carry the animation (`::view-transition-old(page)` and friends in
 * styles.scss) resolve at the DOCUMENT root and cannot be reached from a component's stylesheet.
 * Same reasoning as the `--z-*` scale.
 *
 * It is cleared when the transition finishes rather than on the next navigation, so an attribute
 * left behind by a transition the browser skipped cannot steer the following one.
 */
export function slideDirection({ transition }: ViewTransitionInfo): void {
  const router = inject(Router);
  const back = router.getCurrentNavigation()?.trigger === 'popstate';

  const root = document.documentElement;
  root.dataset['navDirection'] = back ? 'back' : 'forward';

  // `finished` rejects if the transition is skipped mid-flight; the attribute must go either way.
  void transition.finished.finally(() => delete root.dataset['navDirection']);
}
