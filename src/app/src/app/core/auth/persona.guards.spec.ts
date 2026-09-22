import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Injector, PLATFORM_ID, runInInjectionContext } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  CanActivateFn,
  Router,
  RouterStateSnapshot,
  UrlTree,
} from '@angular/router';
import { AuthService } from './auth.service';
import { CurrentUser } from './auth.models';
import { memberGuard, staffGuard } from './persona.guards';

function user(roles: string[], overrides: Partial<CurrentUser> = {}): CurrentUser {
  return {
    id: 'u1',
    email: 'u@test.local',
    displayName: 'U',
    status: 'Active',
    membershipStatus: 'Active',
    roles,
    ...overrides,
  };
}

const MEMBER = user(['User']);
const TRAINER = user(['User', 'Trainer']);
const ADMIN = user(['Admin']);
const ADMIN_TRAINER = user(['Admin', 'Trainer']);
const BLOCKED_MEMBER = user(['User'], { membershipStatus: 'Blocked' });

function configure(platformId: string, authStub: Partial<AuthService>) {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: PLATFORM_ID, useValue: platformId },
      { provide: AuthService, useValue: authStub },
    ],
  });
}

function run(guard: CanActivateFn) {
  return runInInjectionContext(TestBed.inject(Injector), () =>
    guard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
  );
}

function withUser(current: CurrentUser | null) {
  configure('browser', {
    sessionResolved: () => true,
    user: () => current,
    loadCurrentUser: vi.fn(),
  } as unknown as AuthService);
}

async function expectHome(result: unknown) {
  expect(result).toBeInstanceOf(UrlTree);
  expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/');
}

/**
 * The two persona guards (S-25). Each mirrors one server policy — memberGuard is MemberOnly,
 * staffGuard is TrainerOrAdmin — and sends a signed-in account of the wrong persona HOME rather
 * than to /login: they have a session, just not this screen.
 */
describe('memberGuard', () => {
  it('admits a member', async () => {
    withUser(MEMBER);
    await expect(run(memberGuard)).resolves.toBe(true);
  });

  it.each([
    ['a trainer', TRAINER],
    ['an admin', ADMIN],
    ['an admin who trains', ADMIN_TRAINER],
    ['a blocked member', BLOCKED_MEMBER],
  ])('sends %s home', async (_, current) => {
    withUser(current);
    await expectHome(await run(memberGuard));
  });
});

describe('staffGuard', () => {
  it.each([
    ['a trainer', TRAINER],
    // The seeded admin's shape — Admin alone, no User.
    ['an admin', ADMIN],
    ['an admin who trains', ADMIN_TRAINER],
  ])('admits %s', async (_, current) => {
    withUser(current);
    await expect(run(staffGuard)).resolves.toBe(true);
  });

  it('sends a member home', async () => {
    withUser(MEMBER);
    await expectHome(await run(staffGuard));
  });

  it('sends a blocked trainer home', async () => {
    withUser(user(['User', 'Trainer'], { status: 'Blocked' }));
    await expectHome(await run(staffGuard));
  });
});

describe('persona guards', () => {
  it('resolve the session before deciding, when it has not been resolved yet', async () => {
    let loaded = false;
    const loadCurrentUser = vi.fn(async () => {
      loaded = true;
      return MEMBER;
    });

    configure('browser', {
      sessionResolved: () => false,
      user: () => (loaded ? MEMBER : null),
      loadCurrentUser,
    } as unknown as AuthService);

    await expect(run(memberGuard)).resolves.toBe(true);
    expect(loadCurrentUser).toHaveBeenCalledTimes(1);
  });

  it('pass on the server without calling the API', async () => {
    const loadCurrentUser = vi.fn();

    configure('server', {
      sessionResolved: () => false,
      user: () => null,
      loadCurrentUser,
    } as unknown as AuthService);

    await expect(run(staffGuard)).resolves.toBe(true);
    await expect(run(memberGuard)).resolves.toBe(true);
    expect(loadCurrentUser).not.toHaveBeenCalled();
  });
});
