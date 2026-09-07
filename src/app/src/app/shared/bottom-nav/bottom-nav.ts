import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { Icon, IconName } from '../icons/icon';

/** A tab. `exact` matters for exactly one of them — see BOTTOM_NAV_TABS. */
export interface BottomNavTab {
  readonly route: string;
  /** Not rendered as text: the tabs are icon-only, so this becomes the tab's aria-label. */
  readonly label: string;
  readonly icon: IconName;
  readonly exact: boolean;
}

/**
 * The five tabs, THE SAME FIVE FOR EVERY ROLE.
 *
 * That is the design decision this component exists to carry, not an omission. Six destinations are
 * role-conditional today and S-10 deferred four more here, which is up to ten for an admin against a
 * bar that comfortably holds five. Rather than branch the bar, every conditional destination lives on
 * /more — so the bar has no visibility matrix to get wrong, and the matrix it would have had is
 * enforced in one tested place instead.
 *
 * `/` MUST be exact. Every other route has it as a prefix, so without it the Start tab reads as
 * active on every screen in the app and the bar stops telling the member anything.
 */
export const BOTTOM_NAV_TABS: readonly BottomNavTab[] = [
  { route: '/', label: 'Start', icon: 'home', exact: true },
  { route: '/schedule', label: 'Grafik', icon: 'calendar', exact: false },
  { route: '/my-classes', label: 'Moje zajęcia', icon: 'booking', exact: false },
  { route: '/my-plan', label: 'Mój plan', icon: 'plan', exact: false },
  { route: '/more', label: 'Więcej', icon: 'more', exact: false },
];

/**
 * The phone's primary navigation (S-12).
 *
 * STATELESS AND ROLE-BLIND. It reads no AuthService and takes no inputs: the shell decides whether
 * there is a session at all, and the tab set is fixed. A component that cannot see a role cannot
 * disagree with a guard about one, which is the S-01 F5 bug class designed out rather than tested for.
 *
 * Hidden above the 30rem breakpoint in CSS rather than removed in TypeScript — a matchMedia read here
 * would need the isPlatformBrowser double-guard schedule-calendar.ts carries, to solve a problem CSS
 * already solves without touching the DOM on resize.
 *
 * ICON-ONLY, so each tab's accessible name comes from its aria-label rather than from anything on
 * screen. Two consequences worth keeping in mind when editing: the label is now invisible, so it can
 * only be verified by a test or a screen reader; and the active tab has no text weight to change,
 * which is why the marking is a colour shift plus a bar along the top edge.
 */
@Component({
  imports: [Icon, RouterLink, RouterLinkActive],
  selector: 'app-bottom-nav',
  styleUrl: './bottom-nav.scss',
  templateUrl: './bottom-nav.html',
})
export class BottomNav {
  protected readonly tabs = BOTTOM_NAV_TABS;
}
