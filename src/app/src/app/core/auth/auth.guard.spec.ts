import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { PLATFORM_ID, runInInjectionContext, Injector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { authGuard } from './auth.guard';
import { AuthService } from './auth.service';
import { CurrentUser } from './auth.models';

const ACTIVE_USER: CurrentUser = {
  id: 'u1',
  email: 'member@test.local',
  displayName: 'Member',
  status: 'Active',
  roles: ['User'],
};

function runGuard() {
  const injector = TestBed.inject(Injector);
  return runInInjectionContext(injector, () =>
    authGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
  );
}

describe('authGuard', () => {
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

  it('admits an authenticated user', async () => {
    configure('browser', {
      sessionResolved: () => true,
      isAuthenticated: () => true,
      loadCurrentUser: vi.fn(),
    } as unknown as AuthService);

    await expect(runGuard()).resolves.toBe(true);
  });

  it('redirects an anonymous visitor to /login', async () => {
    configure('browser', {
      sessionResolved: () => true,
      isAuthenticated: () => false,
      loadCurrentUser: vi.fn(),
    } as unknown as AuthService);

    const result = await runGuard();

    expect(result).toBeInstanceOf(UrlTree);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/login');
  });

  // A page reload has a valid cookie but no in-memory session. Without this the guard would bounce
  // a signed-in member to login on every refresh.
  it('resolves the session before deciding, when it has not been resolved yet', async () => {
    let authenticated = false;
    const loadCurrentUser = vi.fn(async () => {
      authenticated = true;
      return ACTIVE_USER;
    });

    configure('browser', {
      sessionResolved: () => false,
      isAuthenticated: () => authenticated,
      loadCurrentUser,
    } as unknown as AuthService);

    await expect(runGuard()).resolves.toBe(true);
    expect(loadCurrentUser).toHaveBeenCalledTimes(1);
  });

  // The build prerenders every route; there is no cookie or API on the server, so a guard that ran
  // there would bake a redirect into the prerendered HTML.
  it('passes on the server without calling the API', async () => {
    const loadCurrentUser = vi.fn();

    configure('server', {
      sessionResolved: () => false,
      isAuthenticated: () => false,
      loadCurrentUser,
    } as unknown as AuthService);

    await expect(runGuard()).resolves.toBe(true);
    expect(loadCurrentUser).not.toHaveBeenCalled();
  });
});
