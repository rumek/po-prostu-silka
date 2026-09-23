import { inject } from '@angular/core';
import { ActivatedRouteSnapshot, Navigation, Router, ViewTransitionInfo } from '@angular/router';
import { leafOf, levelOf } from './screen';

/** What a routed navigation's motion says about where the member is going. */
export type TransitionKind = 'skip' | 'back' | 'forward' | 'fade';

/** A brand screen and a tab are both roots; only a child sits deeper. */
function depthOf(leaf: ActivatedRouteSnapshot): number {
  return levelOf(leaf) === 'child' ? 1 : 0;
}

function sameParams(a: ActivatedRouteSnapshot, b: ActivatedRouteSnapshot): boolean {
  const keys = new Set([...Object.keys(a.params), ...Object.keys(b.params)]);
  return [...keys].every((key) => a.params[key] === b.params[key]);
}

/**
 * The motion a navigation gets, from the depth each screen declares in the route table
 * (mobile-native-feel):
 *
 * - `skip` — the same screen with only its query changed: the trainer typing in the member search, or
 *   paging. Nothing on screen moved to a different place, so nothing slides.
 * - `back` — the browser's back or forward button (`popstate`), or up to a shallower screen.
 * - `forward` — into a child, or from one child to a different one.
 * - `fade` — between two roots (Start and the tabs). A tab switch is a sideways move, not a step
 *   deeper, and sliding it would claim a direction that does not exist.
 */
export function transitionKind(
  from: ActivatedRouteSnapshot,
  to: ActivatedRouteSnapshot,
  trigger: Navigation['trigger'] | undefined,
): TransitionKind {
  const a = leafOf(from);
  const b = leafOf(to);

  if (a.routeConfig === b.routeConfig && sameParams(a, b)) {
    return 'skip';
  }
  if (trigger === 'popstate' || depthOf(b) < depthOf(a)) {
    return 'back';
  }
  if (depthOf(b) > 0) {
    return 'forward';
  }
  return 'fade';
}

/**
 * Which way a routed screen moves (S-26, made hierarchy-aware by mobile-native-feel).
 *
 * The View Transition itself is the browser's; this decides only its KIND, published as a data
 * attribute on <html> because the pseudo-elements that carry the animation
 * (`::view-transition-old(page)` and friends in styles.scss) resolve at the DOCUMENT root and cannot
 * be reached from a component's stylesheet. Same reasoning as the `--z-*` scale.
 *
 * It is cleared when the transition finishes rather than on the next navigation, so an attribute
 * left behind by a transition the browser skipped cannot steer the following one.
 */
export function slideDirection({ transition, from, to }: ViewTransitionInfo): void {
  const router = inject(Router);
  const kind = transitionKind(from, to, router.currentNavigation()?.trigger);

  if (kind === 'skip') {
    transition.skipTransition();
    return;
  }

  const root = document.documentElement;
  root.dataset['navDirection'] = kind;

  // `finished` rejects when the DOM update itself fails (a skipped transition resolves it). The
  // attribute must go either way, and `then(clear, clear)` rather than `finally`: the promise
  // `finally` returns would re-reject and surface as an unhandled rejection.
  const clear = () => delete root.dataset['navDirection'];
  void transition.finished.then(clear, clear);
}
