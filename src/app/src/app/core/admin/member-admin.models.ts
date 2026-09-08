/**
 * Mirrors the API's PendingMember record (src/Application/Members/MemberAdminEndpoints.cs).
 * Keep the two in step — this is a contract, not a convenience type.
 */
export interface PendingMember {
  /** The MEMBER's id — every route on the admin surface is addressed by it since S-14. */
  memberId: string;

  /** The account behind them. The pending queue is accounts-only by definition, so never null. */
  userId: string;

  email: string;
  displayName: string;

  /** ISO 8601 from the API. Kept as a string; the screen formats it, nothing does arithmetic on it. */
  createdAt: string;
}

/**
 * Mirrors ApproveFailure. `not_pending` — with Active handled as a no-op, the only status approve can
 * refuse is Blocked, and the action wanted there is unblock. `no_account` — the row is a member the
 * club recorded who never registered, so there is no login to approve.
 */
export interface ApproveFailure {
  reason: 'not_pending' | 'no_account';
}

/**
 * The three ACCOUNT statuses, as AccountStatus names. Null on a member with no login — see
 * `Member.accountStatus`.
 */
export type MemberStatus = 'Pending' | 'Active' | 'Blocked';

/**
 * The two MEMBERSHIP statuses, as MembershipStatus names (S-14).
 *
 * NOT the same question as `MemberStatus`, and the pair must not be collapsed: this one says whether
 * the person may use the club and is always present, that one says whether their login works and only
 * exists if they have one. There is deliberately no membership `Pending` — "registered but not yet
 * approved" is a fact about an account.
 */
export type MembershipStatus = 'Active' | 'Blocked';

/**
 * The positions the members screen's filter offers. These are the states an admin thinks in rather
 * than a projection of either status: `Active` has to mean the same thing for a person with a login
 * and a person without one.
 */
export type MemberFilter = 'Pending' | 'Active' | 'Blocked' | 'WithoutAccount';

/**
 * Mirrors the API's MemberSummary record (src/Application/Members/MemberAdminEndpoints.cs).
 * Keep the two in step — this is a contract, not a convenience type.
 *
 * `status` is the enum NAME, never its int: the numeric values exist for persistence stability and
 * a badge keyed on them would break the day someone renumbers.
 */
export interface Member {
  /** The MEMBER's id. Every action on this screen is addressed by it. */
  id: string;

  /** The linked account, or null when this person has never registered. */
  userId: string | null;

  displayName: string;

  /** Optional since S-14 — a person recorded at the desk may not have given an address. */
  email: string | null;

  /** Always present: whether they may use the club. */
  membershipStatus: MembershipStatus;

  /** Null exactly when `userId` is null. Whether their login works. */
  accountStatus: MemberStatus | null;

  /** Whether a member code is outstanding for them. */
  hasAccessCode: boolean;

  /**
   * Role names as stored ("User", "Admin", "Trainer") — same reasoning as `status`: a name survives
   * a renumbering. A list rather than an is-trainer flag because admins now appear in this list, so
   * a single boolean would immediately need an is-admin one beside it.
   */
  roles: string[];

  /** ISO 8601 from the API. Kept as a string; the screen formats it, nothing does arithmetic on it. */
  createdAt: string;
}

/**
 * Mirrors MemberDetail — one member with everything the edit form needs. Separate from `Member`
 * rather than widening it: the list renders dozens of rows and has no business loading everybody's
 * home address to build a table.
 */
export interface MemberDetail {
  id: string;
  userId: string | null;
  displayName: string;
  email: string | null;
  membershipStatus: MembershipStatus;
  accountStatus: MemberStatus | null;
  roles: string[];
  phoneNumber: string | null;
  street: string | null;
  houseNumber: string | null;
  postalCode: string | null;
  city: string | null;
  createdAt: string;
}

/**
 * Mirrors MemberRequest — what the admin submits to create or edit a record.
 *
 * The five contact fields are ALL-OR-NOTHING: optional as a block, because demanding a full postal
 * address before the club may write down that someone trains here would defeat the point, but half an
 * address is refused by the same validator `/register` uses.
 */
export interface MemberRequest {
  displayName: string;
  email: string | null;
  phoneNumber: string | null;
  street: string | null;
  houseNumber: string | null;
  postalCode: string | null;
  city: string | null;
}

/**
 * Mirrors MemberFailure. The five contact codes come straight from ContactDetails and are the same
 * strings the registration and profile forms already map onto their controls.
 */
export interface MemberFailure {
  reason:
    | 'invalid_display_name'
    | 'invalid_email'
    | 'email_taken'
    | 'conflict'
    | 'invalid_phone'
    | 'invalid_street'
    | 'invalid_house_number'
    | 'invalid_postal_code'
    | 'invalid_city';
}

/**
 * Mirrors TrainerSummary (src/Application/Members/TrainerEndpoints.cs) — one option in the class
 * form's instructor select (prd-v2 FR-009).
 *
 * TWO FIELDS, DELIBERATELY: the value the select submits and the label it shows. `Member` describes
 * an account far more fully, and reusing it here would ship every trainer's email address and status
 * into a dropdown that needs neither.
 */
export interface TrainerSummary {
  id: string;
  displayName: string;
}

/**
 * Mirrors TrainerRoleFailure. `not_active` — the target is Pending or Blocked, and the role is
 * granted to approved accounts only; `failed` — a genuine concurrency failure, typically a block
 * that landed at the same moment. In that case the change did NOT happen and the account may now be
 * in a different state entirely, which is why the screen refetches on an unrecognised 409 rather
 * than patching the row.
 */
export interface TrainerRoleFailure {
  reason: 'not_active' | 'failed' | 'no_account';
}

/**
 * Mirrors BlockFailure. `is_admin` — the target administers the club and is not a member;
 * `conflict` — someone changed the row underneath us, so the list is stale and must be refetched.
 */
export interface BlockFailure {
  reason: 'is_admin' | 'conflict';
}

/**
 * Mirrors UnblockFailure. `conflict` — someone changed the row underneath us, so the list is stale
 * and must be refetched.
 *
 * There is no `not_blocked` since S-14: membership has two states, so "not blocked" is "already
 * active", and the API reports that no-op as success rather than an error.
 */
export interface UnblockFailure {
  reason: 'conflict';
}

/**
 * Mirrors AccessCodeView — the member code as the admin reads it out (S-14, AM-004).
 *
 * `code` arrives ALREADY FORMATTED as `XXXX-XXXX`; the dash is presentation the API owns, so the
 * screen never reassembles it. What is copied to the clipboard is this exact string, because the
 * registration form normalises the dash away anyway.
 */
export interface AccessCodeView {
  code: string;

  /** ISO 8601 from the API — the moment the code stops working. */
  expiresAt: string;
}

/**
 * Mirrors AccessCodeFailure. `has_account` — the member already logs in, so there is nothing to
 * claim; `conflict` — a lost optimistic race (or the generator lost every retry), which means the
 * list is stale and must be refetched.
 */
export interface AccessCodeFailure {
  reason: 'has_account' | 'conflict';
}
