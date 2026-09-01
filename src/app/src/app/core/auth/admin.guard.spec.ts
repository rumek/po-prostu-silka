import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Injector, PLATFORM_ID, runInInjectionContext } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { adminGuard } from './admin.guard';
import { AuthService } from './auth.service';
import { CurrentUser } from './auth.models';

const ADMIN_USER: CurrentUser = {
  id: 'a1',
  email: 'admin@test.local',
  displayName: 'Admin',
  status: 'Active',
  roles: ['User', 'Admin'],
};

function runGuard() {
  const injector = TestBed.inject(Injector);
  return runInInjectionContext(injector, () =>
    adminGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
  );
}

function configure(platformId: object | string, authStub: Partial<AuthService>) {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: PLATFORM_ID, useValue: platformId },
      { provide: AuthService, useValue: authStub },
    ],
  });
}

describe('adminGuard', () => {
  it('admits an active admin', async () => {
    configure('browser', {
      sessionResolved: () => true,
      isAdmin: () => true,
      isActive: () => true,
      loadCurrentUser: vi.fn(),
    } as unknown as AuthService);

    await expect(runGuard()).resolves.toBe(true);
  });

  it('redirects a non-admin member to the app root', async () => {
    configure('browser', {
      sessionResolved: () => true,
      isAdmin: () => false,
      isActive: () => true,
      loadCurrentUser: vi.fn(),
    } as unknown as AuthService);

    const result = await runGuard();

    // Root, not /login: they are not missing a session, and a login form they do not need would
    // read as a bug.
    expect(result).toBeInstanceOf(UrlTree);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/');
  });

  // Mirrors the backend's Admin policy, which requires Active AND the role. An admin whose own
  // account is not approved is not an admin here either.
  it('refuses an admin whose account is not active', async () => {
    configure('browser', {
      sessionResolved: () => true,
      isAdmin: () => true,
      isActive: () => false,
      loadCurrentUser: vi.fn(),
    } as unknown as AuthService);

    expect(await runGuard()).toBeInstanceOf(UrlTree);
  });

  it('resolves the session before deciding, when it has not been resolved yet', async () => {
    let loaded = false;
    const loadCurrentUser = vi.fn(async () => {
      loaded = true;
      return ADMIN_USER;
    });

    configure('browser', {
      sessionResolved: () => false,
      isAdmin: () => loaded,
      isActive: () => loaded,
      loadCurrentUser,
    } as unknown as AuthService);

    await expect(runGuard()).resolves.toBe(true);
    expect(loadCurrentUser).toHaveBeenCalledTimes(1);
  });

  it('passes on the server without calling the API', async () => {
    const loadCurrentUser = vi.fn();

    configure('server', {
      sessionResolved: () => false,
      isAdmin: () => false,
      isActive: () => false,
      loadCurrentUser,
    } as unknown as AuthService);

    await expect(runGuard()).resolves.toBe(true);
    expect(loadCurrentUser).not.toHaveBeenCalled();
  });
});
