import { Router } from '@angular/router';
import { RosterReport } from '../../core/scheduling/roster.models';

/**
 * The navigation-state key a roster report rides on (S-37), when the action that produced it ends on
 * another screen: the class form creates a class and lands on the class list, which shows the report.
 */
export const ROSTER_REPORT_STATE = 'rosterReport';

/**
 * The report handed over by the navigation that is building the current screen, or null — whole, so
 * the screen can toast the booked count as well as panel the skips. Call it in an injection context,
 * while that navigation is still current — a component's field initializer.
 */
export function handedOverRosterReport(router: Router): RosterReport | null {
  const report = router.currentNavigation()?.extras.state?.[ROSTER_REPORT_STATE] as
    RosterReport | undefined;

  return report ?? null;
}
