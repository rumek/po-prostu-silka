import { isPlatformServer } from '@angular/common';
import { PLATFORM_ID, inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Keeps unauthenticated visitors out of app routes - the PRD's "all app content requires an active
 * account".
 *
 * Only authentication is decided here. Status-specific routing (sending a Pending member to the
 * awaiting-approval screen) belongs to S-01, which owns that screen; the guard just makes the
 * status available on AuthService for S-01 to branch on.
 */
export const authGuard: CanActivateFn = async () => {
  const platformId = inject(PLATFORM_ID);

  // The build prerenders every route (angular.json outputMode "static"). There is no cookie and no
  // API on the server, so a guard that ran there would either fail the build or bake a redirect
  // into the prerendered HTML. Let it pass; the browser re-runs the guard on hydration.
  if (isPlatformServer(platformId)) {
    return true;
  }

  const auth = inject(AuthService);
  const router = inject(Router);

  // Resolve once on first navigation, so a page reload with a valid cookie is not bounced to login
  // before the session is known.
  if (!auth.sessionResolved()) {
    await auth.loadCurrentUser();
  }

  if (auth.isAuthenticated()) {
    return true;
  }

  return router.createUrlTree(['/login']);
};
