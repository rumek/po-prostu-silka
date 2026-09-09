import { isPlatformServer } from '@angular/common';
import { PLATFORM_ID, inject } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Keeps /register shut to anyone who was not invited (S-17, IR-02).
 *
 * <p>
 * REGISTRATION IS NOT A PUBLIC DOOR ANY MORE. The club enters a person into its records and hands
 * them an invitation link; that link is the only way to the form. A visitor who arrives without one
 * has nothing to fill in — the account they would create could not attach to any record — so they go
 * to /login, which is where somebody who already trains here belongs anyway.
 * </p>
 *
 * <p>
 * IT NEVER CHECKS WHETHER THE CODE IS VALID, only whether one is present. Asking the API "is this a
 * real code?" before the form would hand an anonymous caller an oracle it could grind against —
 * exactly what the single collapsed <c>unknown_member_code</c> answer exists to deny. Validity is
 * settled once, by the registration itself.
 * </p>
 *
 * <p>
 * The API refuses a codeless registration on its own (AuthEndpoints.RegisterAsync). This guard is
 * the courtesy half: it turns a form the server would refuse into a redirect the visitor can act on.
 * </p>
 */
export const invitationGuard: CanActivateFn = async (route: ActivatedRouteSnapshot) => {
  const platformId = inject(PLATFORM_ID);

  // Same reason activeMemberGuard passes here: the build prerenders every route, and there is no
  // cookie or API on the server. A guard that decided anything would bake a redirect into
  // /register's static HTML. The browser re-runs it on hydration.
  if (isPlatformServer(platformId)) {
    return true;
  }

  const auth = inject(AuthService);
  const router = inject(Router);

  if (!auth.sessionResolved()) {
    await auth.loadCurrentUser();
  }

  // A LIVE SESSION HAS NOTHING TO REGISTER. Sent to the dashboard rather than to /login: they are
  // already signed in, and the login screen would only bounce them back.
  if (auth.isAuthenticated()) {
    return router.createUrlTree(['/']);
  }

  // The query parameter is `invitationCode`; the API field it fills is `memberCode`. Two names for
  // one thing, settled deliberately in the M-5 charter — the URL is what a member sees, and no
  // shipped API contract moves for the sake of matching it.
  const code = route.queryParamMap.get('invitationCode');

  if (!code || code.trim().length === 0) {
    return router.createUrlTree(['/login']);
  }

  return true;
};
