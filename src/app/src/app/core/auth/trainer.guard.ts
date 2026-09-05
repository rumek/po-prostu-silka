import { isPlatformServer } from '@angular/common';
import { PLATFORM_ID, inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Guards the plan-authoring routes. Mirrors the backend's TrainerOrAdmin policy, which requires
 * Active AND (Trainer OR Admin) — an admin authors plans too (prd.md FR-015, FR-016), so this is
 * deliberately not "trainer only".
 *
 * This hides a screen; it does not secure anything. The API enforces the same rule on every request
 * (TrainingPlanEndpoints applies the policy at the group), so a member who edits their way past this
 * guard reaches a screen whose every call answers 403.
 */
export const trainerGuard: CanActivateFn = async () => {
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

  if ((auth.isTrainer() || auth.isAdmin()) && auth.isActive()) {
    return true;
  }

  // Home, not /login: an authenticated member without the role is not missing a session, and
  // bouncing them to a login form they do not need would read as a bug.
  return router.createUrlTree(['/']);
};
