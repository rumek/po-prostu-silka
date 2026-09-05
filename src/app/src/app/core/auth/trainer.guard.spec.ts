import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Injector, PLATFORM_ID, runInInjectionContext } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { AuthService } from './auth.service';
import { CurrentUser } from './auth.models';
import { trainerGuard } from './trainer.guard';

const TRAINER_USER: CurrentUser = {
  id: 't1',
  email: 'trainer@test.local',
  displayName: 'Trener',
  status: 'Active',
  roles: ['User', 'Trainer'],
};

function runGuard() {
  const injector = TestBed.inject(Injector);
  return runInInjectionContext(injector, () =>
    trainerGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
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

describe('trainerGuard', () => {
  it('admits an active trainer', async () => {
    configure('browser', {
      sessionResolved: () => true,
      isTrainer: () => true,
      isAdmin: () => false,
      isActive: () => true,
      loadCurrentUser: vi.fn(),
    } as unknown as AuthService);

    await expect(runGuard()).resolves.toBe(true);
  });

  /**
   * NOT "trainer only", and deliberately: FR-015 named only the admin and the rule was WIDENED
   * rather than moved, so an admin who does not teach keeps every capability the PRD gave them. This
   * mirrors the API's TrainerOrAdmin policy exactly.
   */
  it('admits an active admin who holds no trainer role', async () => {
    configure('browser', {
      sessionResolved: () => true,
      isTrainer: () => false,
      isAdmin: () => true,
      isActive: () => true,
      loadCurrentUser: vi.fn(),
    } as unknown as AuthService);

    await expect(runGuard()).resolves.toBe(true);
  });

  it('redirects a plain member to the app root', async () => {
    configure('browser', {
      sessionResolved: () => true,
      isTrainer: () => false,
      isAdmin: () => false,
      isActive: () => true,
      loadCurrentUser: vi.fn(),
    } as unknown as AuthService);

    const result = await runGuard();

    expect(result).toBeInstanceOf(UrlTree);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/');
  });

  /** Active AND the role, matching the policy — a trainer awaiting approval is not one here either. */
  it('refuses a trainer whose account is not active', async () => {
    configure('browser', {
      sessionResolved: () => true,
      isTrainer: () => true,
      isAdmin: () => false,
      isActive: () => false,
      loadCurrentUser: vi.fn(),
    } as unknown as AuthService);

    expect(await runGuard()).toBeInstanceOf(UrlTree);
  });

  it('resolves the session before deciding, when it has not been resolved yet', async () => {
    let loaded = false;
    const loadCurrentUser = vi.fn(async () => {
      loaded = true;
      return TRAINER_USER;
    });

    configure('browser', {
      sessionResolved: () => false,
      isTrainer: () => loaded,
      isAdmin: () => false,
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
      isTrainer: () => false,
      isAdmin: () => false,
      isActive: () => false,
      loadCurrentUser,
    } as unknown as AuthService);

    await expect(runGuard()).resolves.toBe(true);
    expect(loadCurrentUser).not.toHaveBeenCalled();
  });
});
