import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('authInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let router: { navigate: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    router = { navigate: vi.fn() };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: Router, useValue: router },
      ],
    });

    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  /** The interceptor rethrows, so every request here errors; swallow it and assert the side effect. */
  function respondWith(url: string, status: number) {
    const seen: unknown[] = [];
    http.get(url).subscribe({ error: (e: unknown) => seen.push(e) });
    controller.expectOne(url).flush(null, { status, statusText: 'Error' });
    return seen;
  }

  function expect401(url: string) {
    return respondWith(url, 401);
  }

  it('redirects to /login on a 401 from a normal request', () => {
    expect401('/api/classes');

    expect(router.navigate).toHaveBeenCalledWith(['/login']);
  });

  it('clears session state on a 401 from a normal request', () => {
    const auth = TestBed.inject(AuthService);
    const clear = vi.spyOn(auth, 'clear');

    expect401('/api/classes');

    expect(clear).toHaveBeenCalled();
  });

  // A 401 here is the answer, not an expired session - and redirecting on /me would loop:
  // guard -> /me -> 401 -> redirect -> guard.
  it('does NOT redirect on a 401 from /api/auth/login', () => {
    expect401('/api/auth/login');

    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('does NOT redirect on a 401 from /api/auth/me', () => {
    expect401('/api/auth/me');

    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('does not redirect on non-401 errors', () => {
    respondWith('/api/classes', 500);

    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('rethrows the error so callers can still handle it', () => {
    const errors = expect401('/api/classes');

    expect(errors).toHaveLength(1);
  });
});
