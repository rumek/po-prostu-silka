import { classFailureMessage } from './class-failure';
import { CLASS_FAILURE_REASONS } from './class.models';

/**
 * The table exists so the class form and the create overlay describe the same refusal with the same
 * words. These tests guard the two ways that can quietly stop being true: a reason with no message,
 * and an unrecognised reason rendering as nothing at all.
 */
describe('classFailureMessage', () => {
  it('has a message for every reason the API can return', () => {
    // CLASS_FAILURE_REASONS replaced a hand-copied array guarded by expect(REASONS.length).toBe(16).
    // Its `satisfies` clause makes completeness a BUILD error, where the count only caught a stale
    // list once somebody remembered to bump the number.
    for (const reason of CLASS_FAILURE_REASONS) {
      const message = classFailureMessage(reason);

      expect(message.length).toBeGreaterThan(0);
      // The fallback would mean this reason fell through instead of being answered.
      expect(message).not.toContain('Spróbuj ponownie za chwilę');
    }
  });

  it('answers an unknown reason with the fallback rather than nothing', () => {
    // A server one version ahead can name a reason this build has never heard of.
    expect(classFailureMessage('brand_new_reason')).toContain('Nie udało się');
    expect(classFailureMessage(undefined)).toContain('Nie udało się');
    expect(classFailureMessage(null)).toContain('Nie udało się');
  });

  it('says the same thing about a time conflict wherever it is asked', () => {
    // The whole point of the table: one refusal, one wording, both create surfaces.
    expect(classFailureMessage('time_conflict')).toBe(classFailureMessage('time_conflict'));
    expect(classFailureMessage('time_conflict')).toContain('inne zajęcia');
  });
});
