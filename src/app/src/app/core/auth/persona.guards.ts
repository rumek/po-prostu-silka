import { isPlatformServer } from '@angular/common';
import { PLATFORM_ID, inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { Persona, isStaff, personaOf } from './persona';

/**
 * A guard admitting only the personas `admits` accepts (S-25). Same shape as trainerGuard: true on
 * the server, the session resolved before deciding, and home — not /login — for a signed-in account
 * whose persona does not own the route. They are not missing a session, and a login form would read
 * as a bug.
 *
 * This hides a screen; it secures nothing. The API applies the same persona at the endpoint group
 * (MemberOnly or TrainerOrAdmin), so a user who edits their way past it reaches a screen of 403s.
 */
function personaGuard(admits: (persona: Persona | null) => boolean): CanActivateFn {
  return async () => {
    // Prerendering: see activeMemberGuard. Returning anything but true here breaks the build.
    if (isPlatformServer(inject(PLATFORM_ID))) {
      return true;
    }

    const auth = inject(AuthService);
    const router = inject(Router);

    if (!auth.sessionResolved()) {
      await auth.loadCurrentUser();
    }

    return admits(personaOf(auth.user())) ? true : router.createUrlTree(['/']);
  };
}

/**
 * The member's own screens — their classes and their plan. Mirrors the MemberOnly policy on the
 * `/mine` routes: staff hold no bookings and no plan, so a trainer or an admin goes home.
 */
export const memberGuard: CanActivateFn = personaGuard((persona) => persona === 'member');

/**
 * Staff screens that are not admin-only — the schedule. Mirrors TrainerOrAdmin on `GET /api/classes`,
 * which narrows a trainer to their own classes on the server.
 */
export const staffGuard: CanActivateFn = personaGuard(isStaff);
