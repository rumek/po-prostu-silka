import { ActivatedRouteSnapshot } from '@angular/router';

/**
 * How deep a screen sits (mobile-native-feel) — the one notion the phone's app bar, the bottom
 * bar's history rule and the route transition all read.
 *
 * - `brand` — Start and the signed-out screens. The bar shows the logo, not a title.
 * - `tab` — a root destination: a bottom-bar tab, or an admin list reached from the header. Title,
 *   no back arrow.
 * - `child` — anything reached from inside a tab. Back arrow plus title; its route names a `parent`.
 */
export type ScreenLevel = 'brand' | 'tab' | 'child';

/** What a route declares in its `data`, beside its `title`. */
export interface ScreenData {
  readonly level: ScreenLevel;
  /** The URL "up" returns to. Required for a `child`, meaningless otherwise. */
  readonly parent?: string;
}

/**
 * The route a navigation actually rendered. The routes are flat, but the router hands a strategy
 * the ROOT snapshot, whose own data is empty — reading `data` there would make every screen a brand
 * screen.
 */
export function leafOf(snapshot: ActivatedRouteSnapshot): ActivatedRouteSnapshot {
  let leaf = snapshot;
  while (leaf.firstChild) {
    leaf = leaf.firstChild;
  }
  return leaf;
}

/** `brand` when a route declares nothing: an unknown screen gets the logo, never a blank bar. */
export function levelOf(leaf: ActivatedRouteSnapshot): ScreenLevel {
  return (leaf.data['level'] as ScreenLevel | undefined) ?? 'brand';
}

export function parentOf(leaf: ActivatedRouteSnapshot): string | null {
  return (leaf.data['parent'] as string | undefined) ?? null;
}
