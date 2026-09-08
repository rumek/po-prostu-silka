import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';
import { CurrentUser } from './auth.models';

/**
 * An approved member as the API answers since S-14: BOTH statuses active, and a member id.
 */
const ACTIVE: CurrentUser = {
  id: 'u1',
  email: 'member@test.local',
  displayName: 'Anna Kowalska',
  status: 'Active',
  roles: ['User'],
  memberId: 'm1',
  membershipStatus: 'Active',
};

describe('AuthService.isActive', () => {
  let service: AuthService;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(AuthService);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  /** Signs a session in through /login, which is what populates the service's signal. */
  async function signIn(user: CurrentUser) {
    const login = service.login({ email: user.email, password: 'x' });

    (await vi.waitFor(() => controller.expectOne('/api/auth/login'))).flush(user);
    await login;
  }

  it('admits a member whose account and membership are both active', async () => {
    await signIn(ACTIVE);

    expect(service.isActive()).toBe(true);
  });

  it('refuses a member whose account is still pending approval', async () => {
    await signIn({ ...ACTIVE, status: 'Pending' });

    expect(service.isActive()).toBe(false);
  });

  /**
   * THE S-14 CASE: the admin barred the person from the club and their login still works. The guard
   * has to refuse them here, or it routes them into screens the API answers with 403.
   */
  it('refuses a member whose membership is blocked while the account stays active', async () => {
    await signIn({ ...ACTIVE, membershipStatus: 'Blocked' });

    expect(service.isActive()).toBe(false);
  });

  /**
   * An account with no member row. Three producers make this unreachable in production and the API
   * refuses it everywhere; the SPA must agree rather than assume a default, or the two would disagree
   * about who may be let in.
   */
  it('refuses an account with no membership at all rather than assuming one', async () => {
    await signIn({ ...ACTIVE, memberId: null, membershipStatus: null });

    expect(service.isActive()).toBe(false);
  });
});
