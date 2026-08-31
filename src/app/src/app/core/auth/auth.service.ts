import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { CurrentUser, LoginRequest } from './auth.models';

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

  readonly isAdmin = computed(() => this.currentUser()?.roles.includes('Admin') ?? false);

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
