import { Component, input } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { NavLink } from '../../core/layout/navigation';
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
 */
@Component({
  imports: [Icon, RouterLink, RouterLinkActive],
  selector: 'app-bottom-nav',
  styleUrl: './bottom-nav.scss',
  templateUrl: './bottom-nav.html',
})
export class BottomNav {
  /** The persona's tabs, from `navigationFor(...).bar`. At most five — the table's spec pins it. */
  readonly links = input.required<readonly NavLink[]>();
}
