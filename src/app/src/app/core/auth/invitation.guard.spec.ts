import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Injector, PLATFORM_ID, runInInjectionContext } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  Router,
  RouterStateSnapshot,
  UrlTree,
  convertToParamMap,
} from '@angular/router';
import { invitationGuard } from './invitation.guard';
import { AuthService } from './auth.service';

const SIGNED_IN = {
  sessionResolved: () => true,
  isAuthenticated: () => true,
  loadCurrentUser: vi.fn(),
} as unknown as AuthService;

const ANONYMOUS = {
  sessionResolved: () => true,
  isAuthenticated: () => false,
  loadCurrentUser: vi.fn(),
} as unknown as AuthService;

/** Only the query map matters to this guard, so the rest of the snapshot stays a stub. */
function snapshotWith(queryParams: Record<string, string>) {
  return { queryParamMap: convertToParamMap(queryParams) } as ActivatedRouteSnapshot;
}

function runGuard(queryParams: Record<string, string> = {}) {
  const injector = TestBed.inject(Injector);
  return runInInjectionContext(injector, () =>
    invitationGuard(snapshotWith(queryParams), {} as RouterStateSnapshot),
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

describe('invitationGuard', () => {
  it('admits a visitor carrying an invitation code', async () => {
    configure('browser', ANONYMOUS);

    await expect(runGuard({ invitationCode: 'ABCD-2345' })).resolves.toBe(true);
  });

  // THE POINT OF THE SLICE (S-17, IR-02): registration is not a public door. Without a code there is
  // no record to attach an account to, so there is nothing to show them.
  it('sends a visitor with no invitation code to /login', async () => {
    configure('browser', ANONYMOUS);

    expectRedirect(await runGuard(), '/login');
  });

  // A parameter that is present but empty is the same as absent — a truncated copy-paste must not
  // open the form onto a code the API is bound to refuse.
  it('sends a visitor with a blank invitation code to /login', async () => {
    configure('browser', ANONYMOUS);

    expectRedirect(await runGuard({ invitationCode: '   ' }), '/login');
  });

  // Signed in, therefore nothing to register. To the dashboard rather than /login, which would only
  // bounce them straight back.
  it('sends a signed-in visitor to the dashboard, code or no code', async () => {
    configure('browser', SIGNED_IN);

    expectRedirect(await runGuard({ invitationCode: 'ABCD-2345' }), '/');
  });

  it('resolves the session before deciding, when it has not been resolved yet', async () => {
    let signedIn = false;
    const loadCurrentUser = vi.fn(async () => {
      signedIn = true;
      return null;
    });

    configure('browser', {
      sessionResolved: () => false,
      isAuthenticated: () => signedIn,
      loadCurrentUser,
    } as unknown as AuthService);

    // The stub reports a session once loaded, so the guard must answer on the LOADED state.
    expectRedirect(await runGuard({ invitationCode: 'ABCD-2345' }), '/');
    expect(loadCurrentUser).toHaveBeenCalledTimes(1);
  });

  // Every route is prerendered. A guard that decided anything on the server would bake a redirect
  // into /register's prerendered HTML, or fail the build outright.
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
