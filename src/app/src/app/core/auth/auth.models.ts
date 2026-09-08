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
  /**
   * Contact details (S-13). They ride the session payload rather than sitting behind a GET of their
   * own, so the profile form pre-fills without a second round trip.
   *
   * Nullable because an account created before that slice has none — which is exactly what the
   * profile screen's "complete your details" prompt keys off. Optional as well as nullable, so that
   * every consumer treats "absent" and "empty" the same way: check truthiness, never `=== null`.
   */
  phoneNumber?: string | null;
  street?: string | null;
  houseNumber?: string | null;
  postalCode?: string | null;
  city?: string | null;

  /**
   * The club's record of this person (S-14), and whether they may use the club.
   *
   * <p>
   * NULL MEANS THE ACCOUNT HAS NO MEMBER ROW — a state three producers make unreachable, and one the
   * API refuses everywhere rather than papering over. The SPA reads it as "signed in but unusable"
   * and must not substitute a default: inventing `Active` here would hide exactly the failure the
   * policies exist to surface.
   * </p>
   *
   * <p>
   * `membershipStatus` is NOT the same question as `status`. That one says whether this login may be
   * used and owns the pending → active approval; this one says whether the person may use the club
   * and is the only one that means anything for someone the admin blocked without touching their
   * account. Both are checked by every policy on the server.
   * </p>
   */
  memberId?: string | null;
  membershipStatus?: MembershipStatus | null;
}

/** Mirrors src/Domain/AccountStatus.cs. Serialised by name, not by number. */
export type AccountStatus = 'Pending' | 'Active' | 'Blocked';

/** Mirrors src/Domain/Members/MembershipStatus.cs. Serialised by name, not by number. */
export type MembershipStatus = 'Active' | 'Blocked';

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

/**
 * Mirrors RegisterRequest in src/Application/Auth/AuthEndpoints.cs.
 *
 * The five contact fields land with S-13 and are required by the API even though their columns are
 * nullable — the nullability exists only for accounts created before that slice.
 */
export interface RegisterRequest {
  email: string;
  password: string;
  displayName: string;
  phoneNumber: string;
  street: string;
  houseNumber: string;
  postalCode: string;
  city: string;

  /**
   * The club's member code, when the person has one (S-14).
   *
   * With it, the new account attaches to the record the club already keeps and arrives with that
   * person's bookings and plan already on it. Without it, registration behaves exactly as it always
   * has — the field is optional, and most people will never see a code.
   */
  memberCode?: string | null;
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
  | ContactFailureReason

  // S-14. `invalid_member_code` is a format failure — what was typed could not be a code at all.
  // `unknown_member_code` collapses "no such code", "expired", "revoked" and "already used" into one
  // answer on purpose: telling them apart would confirm to a stranger that a code once existed.
  | 'invalid_member_code'
  | 'unknown_member_code'
  | 'invalid_registration';

/**
 * Mirrors the failure codes ContactDetails.TryCreate returns
 * (src/Application/Members/ContactDetails.cs). Shared between registration and the profile
 * endpoint, because both validate the same five fields through the same helper — so the UI maps
 * them once and both screens reuse the mapping.
 */
export type ContactFailureReason =
  | 'invalid_phone'
  | 'invalid_street'
  | 'invalid_house_number'
  | 'invalid_postal_code'
  | 'invalid_city';

export interface RegisterFailure {
  reason: RegisterFailureReason;
}

/**
 * Mirrors ProfileRequest in src/Application/Members/ProfileEndpoints.cs.
 *
 * displayName and email are ABSENT ON PURPOSE and their absence is the enforcement — the gym owns
 * the name on the membership, and no endpoint in this app changes either. Do not add them.
 */
export interface ProfileRequest {
  phoneNumber: string;
  street: string;
  houseNumber: string;
  postalCode: string;
  city: string;

  /**
   * The club's member code, when the person has one (S-14).
   *
   * With it, the new account attaches to the record the club already keeps and arrives with that
   * person's bookings and plan already on it. Without it, registration behaves exactly as it always
   * has — the field is optional, and most people will never see a code.
   */
  memberCode?: string | null;
}

/** Mirrors ProfileFailure — the same five codes registration answers with, same helper behind them. */
export interface ProfileFailure {
  reason: ContactFailureReason;
}

/** Mirrors ChangePasswordRequest in src/Application/Auth/AuthEndpoints.cs. */
export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

/**
 * Mirrors ChangePasswordFailure. Two codes, and both land on a control rather than in a banner.
 *
 * Unlike login's deliberate non-disclosure, naming which password was wrong leaks nothing here —
 * the caller already proved they own the session — and it is the difference between a form the
 * member can fix and a dead end.
 */
export type ChangePasswordFailureReason = 'invalid_current_password' | 'invalid_new_password';

export interface ChangePasswordFailure {
  reason: ChangePasswordFailureReason;
}

/** Mirrors ForgotPasswordRequest in src/Application/Auth/AuthEndpoints.cs. */
export interface ForgotPasswordRequest {
  email: string;
}

/** Mirrors ResetPasswordRequest. The email travels with the token — Identity validates one against the other. */
export interface ResetPasswordRequest {
  email: string;
  token: string;
  newPassword: string;
}

/**
 * Mirrors ResetPasswordFailure.
 *
 * `invalid_token` deliberately covers an unknown address, a malformed token, one belonging to
 * someone else, an already-used one AND an expired one. The API refuses to distinguish them, so the
 * UI must not imply it can — "wygasł" would tell an anonymous caller the address is registered.
 */
export type ResetPasswordFailureReason = 'invalid_token' | 'invalid_new_password';

export interface ResetPasswordFailure {
  reason: ResetPasswordFailureReason;
}
