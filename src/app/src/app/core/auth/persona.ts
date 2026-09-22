import { CurrentUser } from './auth.models';
import { ROLES } from './roles';

/**
 * What an account SEES (S-25). Roles stay a set on the server; a persona is derived from that set,
 * with the precedence **Admin > Trainer > Member**:
 *
 * - `'admin'` — holds Admin, whatever else it holds (an owner who teaches is an admin here);
 * - `'trainer'` — holds Trainer and not Admin;
 * - `'member'` — holds User and neither of the others.
 *
 * THE SPA'S ONE DEFINITION, the twin of the server's policies: `MemberOnly` is the member persona,
 * `TrainerOrAdmin` is the other two. A screen gates on exactly one persona predicate, and its menu
 * link, its route guard and its API policy all read the same one — see `core/layout/navigation.ts`.
 */
export type Persona = 'member' | 'trainer' | 'admin';

/**
 * The persona of this user, or null when there is none to have.
 *
 * NULL for no session, for an account that is not active (the same two statuses
 * `AuthService.isActive` reads — a blocked member is no persona at all, and keeps only the account
 * screen and logout), and for an account holding no role this app recognises.
 *
 * A PURE FUNCTION OF THE USER rather than a signal on AuthService, on purpose: every spec in the app
 * stubs AuthService as an object literal exposing `user`, and a function over that keeps all of them
 * working where a new signal would have had to be added to each stub.
 *
 * NEVER REQUIRES `User` FOR STAFF. The seeded admin holds Admin alone; a test for "member first"
 * would have made them a nobody.
 */
export function personaOf(user: CurrentUser | null | undefined): Persona | null {
  if (!user || user.status !== 'Active' || user.membershipStatus !== 'Active') {
    return null;
  }

  if (user.roles.includes(ROLES.admin)) {
    return 'admin';
  }

  if (user.roles.includes(ROLES.trainer)) {
    return 'trainer';
  }

  return user.roles.includes(ROLES.member) ? 'member' : null;
}

/** Trainer or admin — the SPA side of the server's TrainerOrAdmin policy. */
export function isStaff(persona: Persona | null): boolean {
  return persona === 'trainer' || persona === 'admin';
}
