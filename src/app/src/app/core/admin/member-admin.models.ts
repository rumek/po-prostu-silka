/**
 * Mirrors the API's PendingMember record (src/Application/Members/MemberAdminEndpoints.cs).
 * Keep the two in step — this is a contract, not a convenience type.
 */
export interface PendingMember {
  id: string;
  email: string;
  displayName: string;

  /** ISO 8601 from the API. Kept as a string; the screen formats it, nothing does arithmetic on it. */
  createdAt: string;
}

/**
 * Mirrors ApproveFailure. `not_pending` is the only named reason: with Active handled as a no-op,
 * the only status approve can refuse is Blocked, and the action wanted there is unblock.
 */
export interface ApproveFailure {
  reason: 'not_pending';
}

/** The three account statuses, as AccountStatus names — see MemberSummary on the API side. */
export type MemberStatus = 'Pending' | 'Active' | 'Blocked';

/**
 * Mirrors the API's MemberSummary record (src/Application/Members/MemberAdminEndpoints.cs).
 * Keep the two in step — this is a contract, not a convenience type.
 *
 * `status` is the enum NAME, never its int: the numeric values exist for persistence stability and
 * a badge keyed on them would break the day someone renumbers.
 */
export interface Member {
  id: string;
  email: string;
  displayName: string;
  status: MemberStatus;

  /** ISO 8601 from the API. Kept as a string; the screen formats it, nothing does arithmetic on it. */
  createdAt: string;
}

/**
 * Mirrors BlockFailure. `is_admin` — the target administers the club and is not a member;
 * `conflict` — someone changed the row underneath us, so the list is stale and must be refetched.
 */
export interface BlockFailure {
  reason: 'is_admin' | 'conflict';
}

/**
 * Mirrors UnblockFailure. `not_blocked` — the target is Pending, so approve is the action wanted;
 * `conflict` — as above.
 */
export interface UnblockFailure {
  reason: 'not_blocked' | 'conflict';
}
