/**
 * Mirrors the API's CurrentUser record (src/Application/Auth/AuthEndpoints.cs).
 * Keep the two in step - this is a contract, not a convenience type.
 */
export interface CurrentUser {
  id: string;
  email: string;
  displayName: string;
  status: AccountStatus;
  roles: string[];
}

/** Mirrors src/Domain/AccountStatus.cs. Serialised by name, not by number. */
export type AccountStatus = 'Pending' | 'Active' | 'Blocked';

/**
 * Why a login failure is named rather than generic: blocked members need a different message from a
 * wrong password. `invalid_credentials` covers both a wrong password and an unknown address - the
 * API deliberately does not distinguish them, so the UI must not imply it does.
 *
 * `pending_approval` is NO LONGER REACHABLE from /login. S-01 inverted that rule: a pending member
 * now receives a session and is routed to /pending by status, not by a login failure. The member is
 * kept in the union because the API still declares the literal, and removing it from both sides is
 * churn for no gain.
 */
export type LoginFailureReason = 'invalid_credentials' | 'pending_approval' | 'blocked';

export interface LoginFailure {
  reason: LoginFailureReason;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  email: string;
  password: string;
  displayName: string;
}

/**
 * Mirrors RegisterFailure in src/Application/Auth/AuthEndpoints.cs.
 *
 * `email_taken` is deliberate disclosure (S-01 D3) and deliberately asymmetric with login's
 * non-disclosure: with no email-confirmation flow, silence would strand a member who forgot they had
 * signed up. The register screen surfaces it on the email field.
 *
 * `invalid_registration` is the API's fallback for an Identity error code it does not recognise -
 * the codes are an open set, so the UI needs a generic branch or an unmapped failure would render
 * the wrong message on a screen the member cannot get past.
 */
export type RegisterFailureReason =
  | 'email_taken'
  | 'invalid_password'
  | 'invalid_email'
  | 'invalid_display_name'
  | 'invalid_registration';

export interface RegisterFailure {
  reason: RegisterFailureReason;
}
