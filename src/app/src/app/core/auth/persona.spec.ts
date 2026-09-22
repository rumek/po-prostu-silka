import { CurrentUser } from './auth.models';
import { isStaff, personaOf } from './persona';

function user(roles: string[], overrides: Partial<CurrentUser> = {}): CurrentUser {
  return {
    id: 'u1',
    email: 'u@test.local',
    displayName: 'U',
    status: 'Active',
    membershipStatus: 'Active',
    roles,
    ...overrides,
  };
}

/**
 * The persona precedence (S-25): Admin > Trainer > Member. Every combination the server can produce
 * is here, because a wrong answer for any one of them is a menu that disagrees with a guard.
 */
describe('personaOf', () => {
  it.each([
    // The SEEDED admin's shape: Admin alone, no User. Must not fall through to null.
    [['Admin'], 'admin'],
    [['User', 'Admin'], 'admin'],
    // An owner who teaches: Admin wins.
    [['Admin', 'Trainer'], 'admin'],
    [['User', 'Admin', 'Trainer'], 'admin'],
    // What promoting a member produces.
    [['User', 'Trainer'], 'trainer'],
    [['User'], 'member'],
  ] as const)('reads %j as %s', (roles, expected) => {
    expect(personaOf(user([...roles]))).toBe(expected);
  });

  it('gives an account with no recognised role no persona', () => {
    expect(personaOf(user([]))).toBeNull();
    expect(personaOf(user(['Something']))).toBeNull();
  });

  it('gives a blocked account no persona, whatever it holds', () => {
    expect(personaOf(user(['Admin'], { status: 'Blocked' }))).toBeNull();
    expect(personaOf(user(['User'], { membershipStatus: 'Blocked' }))).toBeNull();
  });

  /** Absent is not Active — the same refusal AuthService.isActive gives. */
  it('gives an account with no member row no persona', () => {
    expect(personaOf(user(['User'], { membershipStatus: null }))).toBeNull();
  });

  it('gives no session no persona', () => {
    expect(personaOf(null)).toBeNull();
  });
});

describe('isStaff', () => {
  it('is true for trainer and admin only', () => {
    expect(isStaff('trainer')).toBe(true);
    expect(isStaff('admin')).toBe(true);
    expect(isStaff('member')).toBe(false);
    expect(isStaff(null)).toBe(false);
  });
});
