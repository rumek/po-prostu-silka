import { isPlatformServer } from '@angular/common';
import { PLATFORM_ID, inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Guards the admin routes. Mirrors the backend's Admin policy, which requires Active AND the Admin
 * role — an admin whose own account is not approved is not an admin here either.
 *
 * This hides a screen; it does not secure anything. The API enforces the same rule on every request
 * (MemberAdminEndpoints applies the policy at the group), so a member who edits their way past this
 * guard reaches a screen whose every call answers 403.
 */
export const adminGuard: CanActivateFn = async () => {
  const platformId = inject(PLATFORM_ID);

  // Prerendering: see activeMemberGuard. Returning anything but true here breaks the build.
  if (isPlatformServer(platformId)) {
    return true;
  }

  const auth = inject(AuthService);
  const router = inject(Router);

  if (!auth.sessionResolved()) {
    await auth.loadCurrentUser();
  }

  if (auth.isAdmin() && auth.isActive()) {
    return true;
  }

  // Home, not /login: an authenticated non-admin is not missing a session, and bouncing them to a
  // login form they do not need would read as a bug. Let the root route sort them out by status.
  return router.createUrlTree(['/']);
};
