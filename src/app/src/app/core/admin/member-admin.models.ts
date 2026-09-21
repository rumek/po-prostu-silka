/**
 * The ACCOUNT statuses, as AccountStatus names. Null on a member with no login — see
 * `Member.accountStatus`.
 *
 * `'Pending'` STAYS IN THE UNION even though S-16 retired it, and it must, for the same reason the
 * server keeps the enum member: the value is still readable from the database, so a response could
 * still carry it and this type has to be able to describe what arrives. Nothing produces it.
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
 *
 * `'Pending'` is GONE (S-16) — unlike `MemberStatus`, nothing forces this union to describe a value
 * that may still arrive, because a filter is something the SCREEN sends. A chip that could only ever
 * return an empty list is a chip that teaches the admin the screen is broken.
 */
export type MemberFilter = 'Active' | 'Blocked' | 'WithoutAccount';

/**
 * Mirrors the API's MemberSummary record (src/Application/Members/MemberSummary.cs).
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
 * Mirrors the API's `PagedResult<MemberSummary>` (src/Application/Paging/PagedResult.cs) — one page of
 * the member list and how long the whole list is (S-21). Keep the two in step.
 *
 * `page` is 1-based. A page past the end arrives as an empty `items` with the TRUE `total`, which is
 * how the screen tells "that page no longer exists" apart from "nothing matches".
 */
export interface MemberPage {
  items: Member[];
  total: number;
  page: number;
  pageSize: number;
}

/**
 * What the caller asks the member list for. Every field is optional, and an absent or empty one is
 * left OFF the request rather than sent blank — see `MemberAdminService.getMembers`.
 */
export interface MemberQuery {
  filter?: MemberFilter;
  search?: string;
  page?: number;
  pageSize?: number;
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
 * NO EMAIL ADDRESS, and its absence is the enforcement (S-17). The desk records who trains here; the
 * address arrives when that person registers with their invitation and becomes their login. Until
 * then they receive no email and no push, which is an accepted consequence (IR-01) rather than a gap
 * to be filled in here. `MemberSummary` and `MemberDetail` still CARRY an address, because reading
 * one back and writing one are different questions.
 *
 * The five contact fields are ALL-OR-NOTHING: optional as a block, because demanding a full postal
 * address before the club may write down that someone trains here would defeat the point, but half an
 * address is refused by the same validator `PUT /api/profile` uses.
 */
export interface MemberRequest {
  displayName: string;
  phoneNumber: string | null;
  street: string | null;
  houseNumber: string | null;
  postalCode: string | null;
  city: string | null;
}

/**
 * Mirrors MemberFailure. The five contact codes come straight from ContactDetails and are the same
 * strings the profile form already maps onto its controls.
 *
 * `invalid_email` and `email_taken` are GONE (S-17): this endpoint no longer accepts an address, so
 * neither is reachable. Registration still answers `email_taken`, which is where an address now
 * enters a member record.
 */
export interface MemberFailure {
  reason:
    | 'invalid_display_name'
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
 * claim; `member_blocked` — the target is blocked and a code would let them back in;
 * `conflict` — a lost optimistic race (or the generator lost every retry), which means the list is
 * stale and must be refetched.
 *
 * `member_blocked` WAS MISSING UNTIL S-19, and the API has emitted it all along
 * (`src/Application/Members/IssueAccessCode.cs:61`, a 409). The screen said the right sentence
 * anyway only because its message helper was typed `string | undefined` rather than by the union —
 * which is precisely the hole `createFailureMessages` closes. The backend record's own doc comment
 * omits it too; adding it here is type-only and changes no behaviour.
 */
export interface AccessCodeFailure {
  reason: 'has_account' | 'member_blocked' | 'conflict';
}

/**
 * Mirrors the API's MembershipPassView record (src/Application/Members/MembershipPassEndpoints.cs).
 * Keep the two in step — this is a contract, not a convenience type.
 *
 * `entriesUsed` and `entriesLeft` are DERIVED SERVER-SIDE from active bookings, never stored. The
 * practical consequence for this screen: they are a reading taken at request time, so anything that
 * books or releases a spot invalidates them and the screen must refetch rather than adjust them.
 */
export interface MembershipPassView {
  id: string;

  /** What the club calls this pass — free text, never an enum. */
  typeName: string;

  /**
   * First day covered, as `YYYY-MM-DD`. A DATE, not an instant: the API sends `DateOnly`, which has
   * no hour and no offset to be misread in another timezone. Never pass it through `new Date()` and
   * back — that reintroduces exactly the timezone shift the type exists to avoid.
   */
  validFrom: string;

  /** Last day covered, INCLUSIVE, same shape as `validFrom`. */
  validTo: string;

  /** How many entries the pass was issued with. */
  entryCount: number;

  /** How many are spent — the count of active bookings attributed to this pass. */
  entriesUsed: number;

  /** `entryCount - entriesUsed`. Sent rather than computed here, so one place owns the arithmetic. */
  entriesLeft: number;

  /** ISO 8601 from the API. Kept as a string; the screen formats it. */
  issuedAt: string;

  /** Whether this is the pass covering today, by the CLUB's calendar rather than the browser's. */
  coversToday: boolean;
}

/** Mirrors IssuePassRequest — what the admin submits to issue or correct a karnet. */
export interface IssuePassRequest {
  typeName: string;
  validFrom: string;
  validTo: string;
  entryCount: number;
}

/** Mirrors MembershipPassFailure. See that record for what each reason means. */
export interface MembershipPassFailure {
  reason:
    | 'member_blocked'
    | 'invalid_type_name'
    | 'invalid_range'
    | 'invalid_entry_count'
    | 'overlapping_pass'
    | 'has_active_bookings'
    | 'conflict';
}

/**
 * Every reason in {@link MembershipPassFailure}, as a value. See BOOKING_FAILURE_REASONS in
 * core/scheduling/booking.models.ts for why the object literal - the `satisfies` clause is what makes
 * a missing entry a build error rather than a stale count in a spec.
 */
export const MEMBERSHIP_PASS_FAILURE_REASONS = Object.keys({
  member_blocked: true,
  invalid_type_name: true,
  invalid_range: true,
  invalid_entry_count: true,
  overlapping_pass: true,
  has_active_bookings: true,
  conflict: true,
} satisfies Record<
  MembershipPassFailure['reason'],
  true
>) as readonly MembershipPassFailure['reason'][];

/**
 * Every reason in {@link MemberFailure}, as a value. See BOOKING_FAILURE_REASONS in
 * core/scheduling/booking.models.ts for why the object literal — the `satisfies` clause is what
 * makes a missing entry a build error rather than a stale count in a spec.
 */
export const MEMBER_FAILURE_REASONS = Object.keys({
  invalid_display_name: true,
  conflict: true,
  invalid_phone: true,
  invalid_street: true,
  invalid_house_number: true,
  invalid_postal_code: true,
  invalid_city: true,
} satisfies Record<MemberFailure['reason'], true>) as readonly MemberFailure['reason'][];

/** Every reason in {@link TrainerRoleFailure}, as a value. */
export const TRAINER_ROLE_FAILURE_REASONS = Object.keys({
  not_active: true,
  failed: true,
  no_account: true,
} satisfies Record<TrainerRoleFailure['reason'], true>) as readonly TrainerRoleFailure['reason'][];

/** Every reason in {@link BlockFailure}, as a value. */
export const BLOCK_FAILURE_REASONS = Object.keys({
  is_admin: true,
  conflict: true,
} satisfies Record<BlockFailure['reason'], true>) as readonly BlockFailure['reason'][];

/** Every reason in {@link UnblockFailure}, as a value. One entry, same construct — see
 * `unblock-failure.ts` for why the single-reason union is not treated as an exception. */
export const UNBLOCK_FAILURE_REASONS = Object.keys({
  conflict: true,
} satisfies Record<UnblockFailure['reason'], true>) as readonly UnblockFailure['reason'][];

/** Every reason in {@link AccessCodeFailure}, as a value. */
export const ACCESS_CODE_FAILURE_REASONS = Object.keys({
  has_account: true,
  member_blocked: true,
  conflict: true,
} satisfies Record<AccessCodeFailure['reason'], true>) as readonly AccessCodeFailure['reason'][];

/**
 * Mirrors MemberListFailure (S-21) — why a member-list READ was refused: `invalid_page` for a page
 * below 1 or a page size outside 1–100, `invalid_search` for a phrase over 100 characters.
 *
 * The SPA never sends such values. The members screen and the bookings picker fall back to their own
 * "failed to load" state; the trainer's member list (S-22), a third caller over the same union from
 * `/api/trainer/members`, renders the table's words. Every `*Failure` union has a table
 * (core/http/failure-contract.spec.ts).
 */
export interface MemberListFailure {
  reason: 'invalid_page' | 'invalid_search';
}

/** Every reason in {@link MemberListFailure}, as a value. */
export const MEMBER_LIST_FAILURE_REASONS = Object.keys({
  invalid_page: true,
  invalid_search: true,
} satisfies Record<MemberListFailure['reason'], true>) as readonly MemberListFailure['reason'][];
