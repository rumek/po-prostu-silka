import { HttpErrorResponse } from '@angular/common/http';

/**
 * What KIND of failure this was — the question every screen used to answer by inference.
 *
 * <h2>Why a kind and not just a reason</h2>
 *
 * Before S-19 a screen learned only one thing from a thrown error: whether
 * `error.error.reason` happened to be a string. Everything else — a 403 from a stale
 * permission, a 429 from the login limiter (`src/Api/Program.cs:150`), an unhandled 500
 * that arrives with no body at all because the API has no exception middleware
 * (`src/Application/Auth/ForgotPassword.cs:91-96` says so outright), and a browser with no
 * network — collapsed into the same "unrecognised business refusal" fallback sentence.
 * Kind is what separates them, so each can read as itself.
 *
 * <h2>Why `conflict` is NOT a kind</h2>
 *
 * A 409 from this API always carries a `reason`, so it is a named business refusal like any
 * other. Screens that refetch on conflict branch on the reason, exactly as they do today.
 */
export type FailureKind =
  /** A refusal the API named: a 4xx carrying a `{ reason }` body. */
  | 'business'
  /** A 401/403 the interceptor did not already turn into a redirect. */
  | 'auth'
  /** A 404 with no reason body — the thing itself is gone. */
  | 'notFound'
  /** A 429 from the rate limiter. */
  | 'rateLimited'
  /** A 5xx, including the shapeless unhandled 500. */
  | 'server'
  /** The request never reached the server. */
  | 'offline'
  /** Not an `HttpErrorResponse` at all, or a status this classification has no opinion on. */
  | 'unknown';

/** One classified failure. */
export interface FailureInfo {
  kind: FailureKind;
  /** Present only when `kind === 'business'`. */
  reason?: string;
  /** Absent when the request never completed. */
  status?: number;
}

/** The `{ reason }` envelope every named refusal in this API arrives in. */
interface ReasonBody {
  reason?: unknown;
}

/**
 * Pulls the `reason` out of an error body, whatever shape it arrived in.
 *
 * ASP.NET Core hands back a parsed object for `application/json`, but a response whose
 * content type is wrong — or whose body was never JSON at all, which is what an unhandled
 * 500 produces here — arrives as a string. Only the object case can carry a reason.
 */
function readReason(body: unknown): string | undefined {
  if (typeof body !== 'object' || body === null) {
    return undefined;
  }

  const reason = (body as ReasonBody).reason;

  return typeof reason === 'string' && reason.length > 0 ? reason : undefined;
}

/**
 * Classifies anything thrown by an HTTP call.
 *
 * Takes `unknown` rather than `HttpErrorResponse` because that is what a `catch` block and
 * a rejected promise actually hand over, and because a non-HTTP throw inside a subscriber
 * must not crash the classification that is supposed to make failures legible.
 */
export function classifyFailure(error: unknown): FailureInfo {
  if (!(error instanceof HttpErrorResponse)) {
    return { kind: 'unknown' };
  }

  // Status 0 is Angular's marker for "the request never completed": DNS failure, a dead
  // server, a CORS rejection, or a browser that is simply offline. `error.error` is a
  // ProgressEvent in that case, never a body.
  if (error.status === 0) {
    return { kind: 'offline' };
  }

  const status = error.status;
  const reason = readReason(error.error);

  // A named reason wins over the status class, but only inside the 4xx range: a 500 that
  // somehow carried a `reason` is still a server fault, not a refusal the user can act on.
  if (reason !== undefined && status >= 400 && status < 500) {
    return { kind: 'business', reason, status };
  }

  if (status === 401 || status === 403) {
    return { kind: 'auth', status };
  }

  if (status === 404) {
    return { kind: 'notFound', status };
  }

  if (status === 429) {
    return { kind: 'rateLimited', status };
  }

  if (status >= 500) {
    return { kind: 'server', status };
  }

  return { kind: 'unknown', status };
}
