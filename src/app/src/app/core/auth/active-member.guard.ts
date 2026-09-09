import { isPlatformServer } from '@angular/common';
import { PLATFORM_ID, inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Keeps a member who may not use the club out of app content — the SPA half of the backend's
 * ActiveMember policy.
 *
 * <p>
 * IT NO LONGER HAS A DESTINATION TO OFFER (S-16, MP-03). It used to send a Pending account to
 * /pending, a screen that explained the wait; approval is gone, so the only way to fail this guard
 * now is to be BLOCKED, and a blocked person has nowhere in the app to be sent. They go to /login,
 * which is also where their next request lands them anyway — blocking rotates the security stamp,
 * so the session is refused on the round trip after it.
 * </p>
 *
 * Deliberately separate from authGuard, which stays authentication-only (S-01 D12). That mirrors the
 * backend, where authentication and authorization are also separate. Compose them:
 * `canActivate: [authGuard, activeMemberGuard]`.
 */
export const activeMemberGuard: CanActivateFn = async () => {
  const platformId = inject(PLATFORM_ID);

  // Same reason authGuard passes here: the build prerenders every route, and there is no cookie or
  // API on the server. A guard that decided anything would bake a redirect into the prerendered
  // HTML — or fail the build. The browser re-runs it on hydration.
  if (isPlatformServer(platformId)) {
    return true;
  }

  const auth = inject(AuthService);
  const router = inject(Router);

  if (!auth.sessionResolved()) {
    await auth.loadCurrentUser();
  }

  if (auth.isActive()) {
    return true;
  }

  // Blocked, or a session whose claims say so. /login either way: there is no screen in this app
  // for somebody the club has barred, and inventing one would be a place to argue with the decision.
  return router.createUrlTree(['/login']);
};
