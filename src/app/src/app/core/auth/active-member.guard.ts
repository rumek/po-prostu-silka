import { isPlatformServer } from '@angular/common';
import { PLATFORM_ID, inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Keeps a Pending member out of app content and sends them to the awaiting-approval screen — the
 * SPA half of the backend's ActiveMember policy.
 *
 * Deliberately separate from authGuard, which stays authentication-only (S-01 D12). That mirrors the
 * backend, where authentication and authorization are also separate, and it means a route that
 * SHOULD admit a pending member — /pending itself — simply does not list this guard. Compose them:
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

  // Authenticated but not approved: they have somewhere to be.
  if (auth.isAuthenticated()) {
    return router.createUrlTree(['/pending']);
  }

  return router.createUrlTree(['/login']);
};
