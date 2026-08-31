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
 * Why a login failure is named rather than generic: S-01 renders the awaiting-approval screen off
 * `pending_approval`, and blocked members need a different message. `invalid_credentials` covers
 * both a wrong password and an unknown address - the API deliberately does not distinguish them,
 * so the UI must not imply it does.
 */
export type LoginFailureReason = 'invalid_credentials' | 'pending_approval' | 'blocked';

export interface LoginFailure {
  reason: LoginFailureReason;
}

export interface LoginRequest {
  email: string;
  password: string;
}
