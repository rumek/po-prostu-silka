import { MEMBERSHIP_PASS_FAILURE_REASONS } from './member-admin.models';
import { membershipPassFailureMessage } from './membership-pass-failure';

/**
 * The karnet table is read by the admin standing at the desk with the member in front of them, so a
 * refusal that renders as nothing - or as the same sentence as a different refusal - costs a real
 * conversation. These tests guard the two ways that can quietly stop being true: a reason with no
 * message, and an unrecognised reason falling through to nothing.
 *
 * The table itself has had no spec since S-16; it was the only one of the three failure tables without
 * one when the testing-frontend-gate-and-contract rollout phase wired the deploy gate.
 *
 * The reason list comes from MEMBERSHIP_PASS_FAILURE_REASONS, whose `satisfies` clause makes
 * completeness a BUILD error - so unlike the older specs there is no hand-copied list here and no
 * count assertion standing in for one.
 */
describe('membershipPassFailureMessage', () => {
  it('has a message for every reason the API can return', () => {
    for (const reason of MEMBERSHIP_PASS_FAILURE_REASONS) {
      const message = membershipPassFailureMessage(reason);

      expect(message.length).toBeGreaterThan(0);
      // The fallback would mean this reason fell through instead of being answered.
      expect(message).not.toContain('Spróbuj ponownie za chwilę');
    }
  });

  it('answers an unknown reason with the fallback rather than nothing', () => {
    // A server one version ahead can name a reason this build has never heard of.
    expect(membershipPassFailureMessage('brand_new_reason')).toContain('Nie udało się');
    expect(membershipPassFailureMessage(undefined)).toContain('Nie udało się');
    expect(membershipPassFailureMessage(null)).toContain('Nie udało się');
  });

  /**
   * THE THREE REFUSALS THAT LEAD TO DIFFERENT ACTIONS AT THE DESK. Conflating any two of them sends
   * the admin down the wrong path: unblock the person, free up their bookings, or pick another date.
   */
  it('tells the blocking refusals apart', () => {
    const blocked = membershipPassFailureMessage('member_blocked');
    const booked = membershipPassFailureMessage('has_active_bookings');
    const overlapping = membershipPassFailureMessage('overlapping_pass');

    expect(blocked).toContain('zablokowana');
    expect(booked).toContain('wypisz');
    expect(overlapping).toContain('nakładać');

    expect(new Set([blocked, booked, overlapping]).size).toBe(3);
  });
});
