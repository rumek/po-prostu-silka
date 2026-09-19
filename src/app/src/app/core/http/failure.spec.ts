import { HttpErrorResponse } from '@angular/common/http';
import { classifyFailure } from './failure';

/**
 * `classifyFailure` is the one place the app decides what a thrown error MEANS. Before it,
 * a 403, a 429, a body-less 500 and a dead network all reached the user as the same
 * "unrecognised business refusal" sentence. These tests pin each of those apart, because the
 * whole value of the function is that they stay apart.
 */
describe('classifyFailure', () => {
  function httpError(init: { status: number; error?: unknown }): HttpErrorResponse {
    return new HttpErrorResponse({
      status: init.status,
      error: init.error ?? null,
      url: '/api/test',
    });
  }

  it('reads a named 4xx refusal as business, carrying the reason', () => {
    const info = classifyFailure(httpError({ status: 409, error: { reason: 'class_full' } }));

    expect(info.kind).toBe('business');
    expect(info.reason).toBe('class_full');
    expect(info.status).toBe(409);
  });

  it('treats a 400 with a reason as business too — the status class is not the signal', () => {
    expect(
      classifyFailure(httpError({ status: 400, error: { reason: 'missing_field' } })).kind,
    ).toBe('business');
  });

  it.each([401, 403])('reads %i as auth', (status) => {
    const info = classifyFailure(httpError({ status }));

    expect(info.kind).toBe('auth');
    expect(info.reason).toBeUndefined();
  });

  it('reads a body-less 404 as notFound', () => {
    expect(classifyFailure(httpError({ status: 404 })).kind).toBe('notFound');
  });

  it('reads 429 as rateLimited', () => {
    // The API really does emit this — src/Api/Program.cs:150.
    expect(classifyFailure(httpError({ status: 429 })).kind).toBe('rateLimited');
  });

  /**
   * THE SHAPE THE API HAS NO MIDDLEWARE FOR. `src/Application/Auth/ForgotPassword.cs:91-96`
   * records that there is no exception handler, so an unhandled fault arrives as a 500 whose
   * body is whatever the host wrote — often a string, often nothing. It must not read as a
   * business refusal.
   */
  it('reads a body-less 500 as server', () => {
    const info = classifyFailure(httpError({ status: 500 }));

    expect(info.kind).toBe('server');
    expect(info.reason).toBeUndefined();
  });

  it('reads a 500 as server even if something reason-shaped rode along', () => {
    // A server fault is never something the user can act on, whatever the body claims.
    expect(classifyFailure(httpError({ status: 500, error: { reason: 'whatever' } })).kind).toBe(
      'server',
    );
  });

  it('reads a 503 as server', () => {
    // GetVapidKey.cs:11 emits one.
    expect(classifyFailure(httpError({ status: 503 })).kind).toBe('server');
  });

  /**
   * OFFLINE. Angular reports a request that never completed as status 0 with a ProgressEvent
   * where the body would be — the exact case that used to render as "try again later" over a
   * business rule that was never consulted.
   */
  it('reads a status-0 ProgressEvent as offline, with no status', () => {
    const info = classifyFailure(
      new HttpErrorResponse({
        status: 0,
        error: new ProgressEvent('error'),
        url: '/api/test',
      }),
    );

    expect(info.kind).toBe('offline');
    expect(info.status).toBeUndefined();
  });

  it('ignores a string body — only an object can carry a reason', () => {
    // A 500 rendered as an HTML error page, or a plain-text body, parses to a string.
    expect(classifyFailure(httpError({ status: 400, error: '<html>oops</html>' })).kind).toBe(
      'unknown',
    );
  });

  it('ignores a non-string or empty reason', () => {
    expect(classifyFailure(httpError({ status: 409, error: { reason: 42 } })).kind).not.toBe(
      'business',
    );
    expect(classifyFailure(httpError({ status: 409, error: { reason: '' } })).kind).not.toBe(
      'business',
    );
  });

  it('answers unknown for anything that is not an HttpErrorResponse', () => {
    // A throw inside a subscriber must not crash the classification meant to make failures
    // legible.
    expect(classifyFailure(new Error('boom')).kind).toBe('unknown');
    expect(classifyFailure(undefined).kind).toBe('unknown');
    expect(classifyFailure(null).kind).toBe('unknown');
  });
});
