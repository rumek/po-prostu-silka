import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  ChangePasswordRequest,
  CurrentUser,
  LoginRequest,
  ProfileRequest,
  RegisterRequest,
} from './auth.models';
import { ROLES } from './roles';

/**
 * Owns session state for the SPA, so guards and screens read one source instead of each calling
 * /api/auth/me.
 *
 * No token handling and no API base URL: the SPA is served from the API's own wwwroot, so relative
 * /api/... paths are same-origin and the browser carries the HttpOnly auth cookie automatically.
 * There is deliberately nothing here for XSS to steal.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly currentUser = signal<CurrentUser | null>(null);

  /** Null until loadCurrentUser() has resolved, or after a 401/logout. */
  readonly user = this.currentUser.asReadonly();

  readonly isAuthenticated = computed(() => this.currentUser() !== null);

  /** True only for an approved account. A Pending member is authenticated but not active. */
  readonly isActive = computed(() => this.currentUser()?.status === 'Active');

  readonly isAdmin = computed(() => this.currentUser()?.roles.includes(ROLES.admin) ?? false);

  /**
   * Holds the Trainer role. Additive and confers nothing alone - the authoring screens gate on
   * `(isTrainer() || isAdmin()) && isActive()`, mirroring the API's TrainerOrAdmin policy, which
   * requires Active plus either role. A trainer who is not approved is not a trainer here either.
   */
  readonly isTrainer = computed(() => this.currentUser()?.roles.includes(ROLES.trainer) ?? false);

  /**
   * Has loadCurrentUser() finished at least once? The guard waits on this so a page reload with a
   * valid cookie is not bounced to login before the session is known.
   */
  private readonly resolved = signal(false);
  readonly sessionResolved = this.resolved.asReadonly();

  async login(credentials: LoginRequest): Promise<CurrentUser> {
    const user = await firstValueFrom(this.http.post<CurrentUser>('/api/auth/login', credentials));

    this.currentUser.set(user);
    this.resolved.set(true);
    return user;
  }

  /**
   * Creates the account and signs in, in one call (S-01 D1) - the API returns the same CurrentUser
   * shape /login does, so the caller has one code path for "you now have a session".
   *
   * Like login(), this does NOT catch: a RegisterFailure body has to reach the screen so it can put
   * `email_taken` on the email control.
   */
  async register(request: RegisterRequest): Promise<CurrentUser> {
    const user = await firstValueFrom(this.http.post<CurrentUser>('/api/auth/register', request));

    this.currentUser.set(user);
    this.resolved.set(true);
    return user;
  }

  /**
   * Re-mints the auth cookie's claims from the member's current row.
   *
   * NOT an alias over loadCurrentUser(). /me reads the database and would report Active while the
   * cookie's account_status claim still said Pending - the claim is refreshed only every 30 minutes
   * by the security-stamp validator. Routing on /me alone puts a just-approved member into an app
   * where every ActiveMember endpoint answers 403 for up to half an hour. /api/auth/refresh is the
   * endpoint that fixes the claim; the awaiting-approval screen must call this one.
   */
  async refresh(): Promise<CurrentUser> {
    const user = await firstValueFrom(this.http.post<CurrentUser>('/api/auth/refresh', null));

    this.currentUser.set(user);
    this.resolved.set(true);
    return user;
  }

  /**
   * Saves the member's contact details and replaces the session signal from the response.
   *
   * The response IS the new session state — the endpoint returns the same CurrentUser shape /me
   * does — so nothing re-fetches afterwards and every screen reading auth.user() sees the new
   * values immediately.
   *
   * Does NOT catch, like every other method here: a ProfileFailure body has to reach the screen so
   * it can put `invalid_postal_code` on the postal-code control rather than in a banner.
   */
  async updateProfile(request: ProfileRequest): Promise<CurrentUser> {
    const user = await firstValueFrom(this.http.put<CurrentUser>('/api/profile', request));

    this.currentUser.set(user);
    return user;
  }

  /**
   * Replaces the member's password without ending their session.
   *
   * Returns nothing and touches no signal: the API answers 204, and the session survives because the
   * endpoint refreshes the cookie against the rotated security stamp server-side. Every OTHER
   * session for this member expires on its own within the validator's interval — that is the point
   * of a password change, not a bug.
   *
   * Does NOT catch, like its neighbours: the failure has to reach the screen so it can put
   * `invalid_current_password` on the current-password control.
   */
  async changePassword(request: ChangePasswordRequest): Promise<void> {
    await firstValueFrom(this.http.post<void>('/api/auth/change-password', request));
  }

  async logout(): Promise<void> {
    await firstValueFrom(this.http.post<void>('/api/auth/logout', null));
    this.clear();
  }

  /**
   * Resolves the session from the cookie. A 401 is an expected answer here, not an error - it means
   * "not signed in" - so it resolves to null rather than throwing.
   */
  async loadCurrentUser(): Promise<CurrentUser | null> {
    try {
      const user = await firstValueFrom(this.http.get<CurrentUser>('/api/auth/me'));
      this.currentUser.set(user);
      return user;
    } catch {
      this.currentUser.set(null);
      return null;
    } finally {
      this.resolved.set(true);
    }
  }

  /** Drops local session state without calling the API. Used by the interceptor on a 401. */
  clear(): void {
    this.currentUser.set(null);
    this.resolved.set(true);
  }
}
