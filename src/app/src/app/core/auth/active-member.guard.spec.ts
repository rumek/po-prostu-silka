import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Injector, PLATFORM_ID, runInInjectionContext } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { activeMemberGuard } from './active-member.guard';
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
    activeMemberGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
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

function expectRedirect(result: Awaited<ReturnType<typeof runGuard>>, to: string) {
  expect(result).toBeInstanceOf(UrlTree);
  expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe(to);
}

describe('activeMemberGuard', () => {
  it('admits an approved member', async () => {
    configure('browser', {
      sessionResolved: () => true,
      isActive: () => true,
      isAuthenticated: () => true,
      loadCurrentUser: vi.fn(),
    } as unknown as AuthService);

    await expect(runGuard()).resolves.toBe(true);
  });

  // The core of D1: a pending member HAS a session, so bouncing them to /login would be wrong. They
  // belong on the screen that exists for them.
  it('sends an authenticated but pending member to /pending', async () => {
    configure('browser', {
      sessionResolved: () => true,
      isActive: () => false,
      isAuthenticated: () => true,
      loadCurrentUser: vi.fn(),
    } as unknown as AuthService);

    expectRedirect(await runGuard(), '/pending');
  });

  it('sends an anonymous visitor to /login', async () => {
    configure('browser', {
      sessionResolved: () => true,
      isActive: () => false,
      isAuthenticated: () => false,
      loadCurrentUser: vi.fn(),
    } as unknown as AuthService);

    expectRedirect(await runGuard(), '/login');
  });

  it('resolves the session before deciding, when it has not been resolved yet', async () => {
    let active = false;
    const loadCurrentUser = vi.fn(async () => {
      active = true;
      return ACTIVE_USER;
    });

    configure('browser', {
      sessionResolved: () => false,
      isActive: () => active,
      isAuthenticated: () => active,
      loadCurrentUser,
    } as unknown as AuthService);

    await expect(runGuard()).resolves.toBe(true);
    expect(loadCurrentUser).toHaveBeenCalledTimes(1);
  });

  // Every route is prerendered. A guard that decided anything on the server would bake a redirect
  // into the prerendered HTML, or fail the build outright.
  it('passes on the server without calling the API', async () => {
    const loadCurrentUser = vi.fn();

    configure('server', {
      sessionResolved: () => false,
      isActive: () => false,
      isAuthenticated: () => false,
      loadCurrentUser,
    } as unknown as AuthService);

    await expect(runGuard()).resolves.toBe(true);
    expect(loadCurrentUser).not.toHaveBeenCalled();
  });
});
