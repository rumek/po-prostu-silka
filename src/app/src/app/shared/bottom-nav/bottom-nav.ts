import { Component, inject, input } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, map } from 'rxjs';
import { NavLink } from '../../core/layout/navigation';
import { ScreenTitle } from '../../core/layout/screen-title';
import { Up } from '../../core/layout/up';
import { Icon } from '../icons/icon';

/**
 * The phone's primary navigation (S-12).
 *
 * STILL ROLE-BLIND, BUT NO LONGER ONE FIXED SET (S-25). Until S-25 every role got the same five tabs,
 * and every role-conditional destination lived on /more, so the bar had no visibility matrix to get
 * wrong. The persona model makes the SET differ per persona — a member has no Grafik, staff have no
 * Plan — so the set is now decided by the shell from `core/layout/navigation.ts`, the one table the
 * header and /more read too. This component still reads no AuthService: it renders the links it is
 * given, in order, and a component that cannot see a role still cannot disagree with a guard about
 * one. The table's spec is where the matrix is pinned.
 *
 * Hidden above the 30rem breakpoint in CSS rather than removed in TypeScript — a matchMedia read here
 * would need the isPlatformBrowser double-guard schedule-calendar.ts carries, to solve a problem CSS
 * already solves without touching the DOM on resize.
 *
 * THE ACTIVE TAB IS A PILL: icon plus label, on the accent colour; every other tab is its icon alone,
 * named by its aria-label. The label is in the DOM on every tab and only collapsed in CSS, so the pill
 * can grow into it rather than snap — see bottom-nav.scss.
 *
 * TABS DO NOT GROW HISTORY (mobile-native-feel), which is Android's back stack for a bottom bar:
 * history holds at most Start plus the current tab, so back from any tab lands on Start and back
 * from Start leaves the app.
 * - From Start (a `brand` screen) a tab PUSHES, so Start stays underneath it.
 * - From any other screen a tab REPLACES the current entry.
 * - The Start tab itself goes UP to `/`: a pop when Start is below, a replace when it is not.
 */
@Component({
  imports: [Icon],
  selector: 'app-bottom-nav',
  styleUrl: './bottom-nav.scss',
  templateUrl: './bottom-nav.html',
})
export class BottomNav {
  /** The persona's tabs, from `navigationFor(...).bar`. At most five — the table's spec pins it. */
  readonly links = input.required<readonly NavLink[]>();

  private readonly router = inject(Router);
  private readonly screen = inject(ScreenTitle);
  private readonly up = inject(Up);

  private readonly path = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.currentPath()),
    ),
    { initialValue: this.currentPath() },
  );

  /** `exact` for Start alone: `/` is a prefix of every route, and would mark two tabs at once. */
  protected isActive(tab: NavLink): boolean {
    const path = this.path();
    return tab.exact ? path === tab.route : path === tab.route || path.startsWith(`${tab.route}/`);
  }

  protected go(event: MouseEvent, tab: NavLink): void {
    // A modified click is the browser's: a new tab or window from the href.
    if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) {
      return;
    }
    event.preventDefault();

    if (tab.route === '/') {
      this.up.to(tab.route);
    } else {
      void this.router.navigateByUrl(tab.route, { replaceUrl: this.screen.level() !== 'brand' });
    }
  }

  private currentPath(): string {
    return this.router.url.split(/[?#]/)[0];
  }
}
