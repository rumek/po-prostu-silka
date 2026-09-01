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
 * Mirrors ApproveFailure. `not_pending` is the only named reason: approving a Blocked member is
 * S-02's unblock, which has to answer what happens to their existing bookings first.
 */
export interface ApproveFailure {
  reason: 'not_pending';
}
